using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Codely.UnityMcp
{
    public sealed class ToolException : Exception
    {
        public string Code { get; }
        public object Payload { get; }

        public ToolException(string code, string message, object data = null) : base(message)
        {
            Code = code;
            Payload = data;
        }
    }

    /// <summary>
    /// Editor-side half of the bridge. Owns a loopback-only TCP listener, a
    /// length-prefixed JSON protocol, a main-thread command pump and a heartbeat file
    /// that lets the MCP server discover the port without guessing.
    ///
    /// Everything Unity-specific runs on the main thread via EditorApplication.update;
    /// socket I/O runs on per-client threads and only ever touches queues.
    /// </summary>
    [InitializeOnLoad]
    public static class McpBridgeServer
    {
        public const string PackageVersion = "0.1.0";
        public const string ProtocolVersion = "1";

        private const string HeartbeatFileName = ".codely-unity-mcp.json";
        private const int DefaultPort = 27180;
        private const int PortProbeCount = 24;
        private const int MaxCommandsPerTick = 64;
        private const double HeartbeatIntervalSeconds = 2.0;
        private const string ManualStopKey = "Codely.UnityMcp.ManualStop";

        private static readonly ConcurrentQueue<Incoming> Queue = new ConcurrentQueue<Incoming>();
        private static readonly List<ClientSession> Clients = new List<ClientSession>();
        private static readonly object ClientsGate = new object();

        private static TcpListener _listener;
        private static Thread _acceptThread;
        private static volatile bool _stopping;
        private static double _nextHeartbeatAt;
        private static long _handled;
        private static int _nextClientId;
        private static int _autoStartAttempts;
        private static double _nextAutoStartAt;

        public static bool IsRunning { get; private set; }
        public static int Port { get; private set; }
        public static long HandledCommands => Interlocked.Read(ref _handled);
        public static string LastError { get; private set; }

        public static int ClientCount
        {
            get { lock (ClientsGate) return Clients.Count; }
        }

        public static string ProjectRoot =>
            Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;

        public static string HeartbeatPath =>
            Path.Combine(ProjectRoot, "Temp", HeartbeatFileName);

        static McpBridgeServer()
        {
            if (SessionState.GetBool(ManualStopKey, false)) return;

            // A domain reload destroys both the listener and the update hook. Try right
            // away, then keep retrying from update until the editor is idle enough to
            // bind; otherwise a reload leaves the bridge silently stopped and clients
            // wait forever on a heartbeat that still says "reloading".
            EditorApplication.delayCall += EnsureAutoStart;
            EditorApplication.update += EnsureAutoStart;
        }

        private static void EnsureAutoStart()
        {
            if (IsRunning)
            {
                EditorApplication.update -= EnsureAutoStart;
                return;
            }
            if (SessionState.GetBool(ManualStopKey, false)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (EditorApplication.timeSinceStartup < _nextAutoStartAt) return;

            _nextAutoStartAt = EditorApplication.timeSinceStartup + 2.0;
            if (++_autoStartAttempts > 30) return;
            Start();
        }

        // ---- lifecycle ----------------------------------------------------- //

        public static bool Start(int requestedPort = 0)
        {
            if (IsRunning) return true;

            try
            {
                SessionState.EraseBool(ManualStopKey);
                int port = requestedPort > 0 ? requestedPort : ResolvePort();
                _listener = new TcpListener(IPAddress.Loopback, port);
                _listener.Start();
                Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                SavePort(Port);

                _stopping = false;
                IsRunning = true;
                _nextHeartbeatAt = 0;
                _autoStartAttempts = 0;
                LastError = null;

                _acceptThread = new Thread(AcceptLoop)
                {
                    IsBackground = true,
                    Name = "Codely.UnityMcp.Accept"
                };
                _acceptThread.Start();

                EditorApplication.update -= Tick;
                EditorApplication.update += Tick;
                AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
                AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
                EditorApplication.quitting -= OnQuitting;
                EditorApplication.quitting += OnQuitting;
                EditorApplication.playModeStateChanged -= OnPlayModeChanged;
                EditorApplication.playModeStateChanged += OnPlayModeChanged;

                WriteHeartbeat("ready");
                Debug.Log($"<b><color=#4EA1FF>Codely Unity MCP</color></b> listening on 127.0.0.1:{Port} " +
                          $"(handshake file: Temp/{HeartbeatFileName})");
                return true;
            }
            catch (Exception ex)
            {
                IsRunning = false;
                LastError = ex.Message;
                Debug.LogError($"Codely Unity MCP failed to start: {ex.Message}");
                WriteHeartbeat("error");
                return false;
            }
        }

        public static void Stop(bool manual = false, string reason = "stopped")
        {
            if (manual) SessionState.SetBool(ManualStopKey, true);

            _stopping = true;
            IsRunning = false;
            Port = 0;

            EditorApplication.update -= Tick;
            EditorApplication.update -= EnsureAutoStart;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            EditorApplication.quitting -= OnQuitting;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;

            try { _listener?.Stop(); } catch { /* already closed */ }
            _listener = null;

            ClientSession[] snapshot;
            lock (ClientsGate)
            {
                snapshot = Clients.ToArray();
                Clients.Clear();
            }
            foreach (var client in snapshot) client.Close();

            WriteHeartbeat(reason);
        }

        private static void OnBeforeAssemblyReload()
        {
            // Managed threads die with the domain; release the socket and tell clients
            // to wait for the next heartbeat instead of reconnecting to a dead port.
            _stopping = true;
            IsRunning = false;
            try { _listener?.Stop(); } catch { }
            _listener = null;
            WriteHeartbeat("reloading");
        }

        private static void OnQuitting() => Stop(false, "unity_quit");

        private static void OnPlayModeChanged(PlayModeStateChange _) => WriteHeartbeat(IsRunning ? "ready" : "stopped");

        // ---- accept / read / write ---------------------------------------- //

        private static void AcceptLoop()
        {
            while (!_stopping)
            {
                TcpClient client;
                try { client = _listener.AcceptTcpClient(); }
                catch (SocketException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (InvalidOperationException) { break; }

                try
                {
                    client.NoDelay = true;
                    var session = new ClientSession(Interlocked.Increment(ref _nextClientId), client);
                    lock (ClientsGate) Clients.Add(session);
                    session.Start(Queue, RemoveClient);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Codely Unity MCP client setup failed: {ex.Message}");
                    try { client.Close(); } catch { }
                }
            }
        }

        private static void RemoveClient(ClientSession session)
        {
            lock (ClientsGate) Clients.Remove(session);
        }

        // ---- main-thread pump --------------------------------------------- //

        private static void Tick()
        {
            if (!IsRunning)
            {
                // The listener can die underneath us (port closed externally); self-heal
                // instead of leaving clients pointed at a dead port.
                if (_listener == null) Stop(false, "listener_lost");
                return;
            }

            int processed = 0;
            while (processed < MaxCommandsPerTick && Queue.TryDequeue(out Incoming incoming))
            {
                processed++;
                Process(incoming);
            }

            double now = EditorApplication.timeSinceStartup;
            if (now >= _nextHeartbeatAt)
            {
                _nextHeartbeatAt = now + HeartbeatIntervalSeconds;
                WriteHeartbeat("ready");
            }
        }

        private static void Process(Incoming incoming)
        {
            Interlocked.Increment(ref _handled);

            string id = null;
            string type = null;
            try
            {
                var root = MiniJson.Deserialize(incoming.Json) as IDictionary<string, object>;
                if (root == null) throw new ToolException("invalid_request", "The request must be a JSON object.");

                id = JsonUtil.Str(root, "id");
                type = JsonUtil.Str(root, "type");
                if (string.IsNullOrEmpty(type))
                    throw new ToolException("invalid_request", "Missing 'type'. Use list_tools to discover available types.");

                var p = JsonUtil.Obj(root, "params") ?? new Dictionary<string, object>(StringComparer.Ordinal);

                var blocked = WriteGuard.Check(type, p);
                if (blocked != null) throw new ToolException(
                    Convert.ToString(blocked["code"]), Convert.ToString(blocked["message"]), blocked);

                string message;
                object data = CommandRouter.Execute(type, p, out message);
                incoming.Reply(Success(id, message, data));
            }
            catch (ToolException ex)
            {
                incoming.Reply(Failure(id, ex.Code, ex.Message, ex.Payload));
            }
            catch (Exception ex)
            {
                Debug.LogError($"Codely Unity MCP command '{type}' failed: {ex}");
                incoming.Reply(Failure(id, "internal_error", ex.Message, new Dictionary<string, object>
                {
                    ["type"] = type,
                    ["stackTrace"] = ex.StackTrace
                }));
            }
        }

        // ---- response envelopes ------------------------------------------- //

        public static string Success(string id, string message, object data)
        {
            return MiniJson.Serialize(new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["id"] = id,
                ["success"] = true,
                ["message"] = message ?? "OK",
                ["data"] = data
            });
        }

        public static string Failure(string id, string code, string message, object data)
        {
            return MiniJson.Serialize(new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["id"] = id,
                ["success"] = false,
                ["code"] = code ?? "error",
                ["error"] = message ?? "Unknown error",
                ["message"] = message ?? "Unknown error",
                ["data"] = data
            });
        }

        // ---- port + heartbeat --------------------------------------------- //

        private static int ResolvePort()
        {
            int preferred = LoadPort();
            if (preferred <= 0) preferred = DefaultPort;

            for (int offset = 0; offset < PortProbeCount; offset++)
            {
                int candidate = preferred + offset;
                if (candidate > 65535) break;
                if (TryProbe(candidate)) return candidate;
            }

            var random = new System.Random();
            for (int attempt = 0; attempt < PortProbeCount; attempt++)
            {
                int candidate = random.Next(30000, 45000);
                if (TryProbe(candidate)) return candidate;
            }

            throw new IOException("No free loopback port found for the Codely Unity MCP bridge.");
        }

        private static bool TryProbe(int port)
        {
            TcpListener probe = null;
            try
            {
                probe = new TcpListener(IPAddress.Loopback, port);
                probe.Start();
                return true;
            }
            catch (SocketException) { return false; }
            finally
            {
                try { probe?.Stop(); } catch { }
            }
        }

        private static string PortPrefKey =>
            "Codely.UnityMcp.Port." + StableHash(ProjectRoot);

        private static int LoadPort()
        {
            int value = EditorPrefs.GetInt(PortPrefKey, 0);
            return value > 0 && value <= 65535 ? value : 0;
        }

        private static void SavePort(int port) => EditorPrefs.SetInt(PortPrefKey, port);

        public static void WriteHeartbeat(string state)
        {
            try
            {
                string directory = Path.Combine(ProjectRoot, "Temp");
                Directory.CreateDirectory(directory);

                var payload = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["protocol"] = ProtocolVersion,
                    ["server"] = "codely-unity-mcp",
                    ["package_version"] = PackageVersion,
                    ["port"] = IsRunning ? Port : 0,
                    ["project_root"] = ProjectRoot,
                    ["project_path"] = Application.dataPath,
                    ["pid"] = System.Diagnostics.Process.GetCurrentProcess().Id,
                    ["unity_version"] = Application.unityVersion,
                    ["product"] = Application.productName,
                    ["state"] = state,
                    ["is_playing"] = EditorApplication.isPlaying,
                    ["is_compiling"] = EditorApplication.isCompiling,
                    ["is_updating"] = EditorApplication.isUpdating,
                    ["clients"] = ClientCount,
                    ["last_error"] = LastError,
                    ["last_updated"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                };

                string path = HeartbeatPath;
                string temp = path + ".tmp";
                File.WriteAllText(temp, MiniJson.Serialize(payload), new UTF8Encoding(false));
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
            }
            catch (Exception ex)
            {
                // Heartbeat failures must never break the bridge; clients can still be
                // pointed at the port explicitly.
                if (EditorPrefs.GetBool("Codely.UnityMcp.Debug", false))
                    Debug.LogWarning($"Codely Unity MCP heartbeat write failed: {ex.Message}");
            }
        }

        private static string StableHash(string text)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in text)
                {
                    hash ^= c;
                    hash *= 16777619;
                }
                return hash.ToString("x8", CultureInfo.InvariantCulture);
            }
        }

        // ---- session -------------------------------------------------------- //

        private sealed class Incoming
        {
            public string Json;
            public Action<string> Reply;
        }

        private sealed class ClientSession
        {
            private readonly int _id;
            private readonly TcpClient _client;
            private readonly NetworkStream _stream;
            private readonly BlockingCollection<byte[]> _outbox = new BlockingCollection<byte[]>(256);
            private Thread _readThread;
            private Thread _writeThread;

            public ClientSession(int id, TcpClient client)
            {
                _id = id;
                _client = client;
                _stream = client.GetStream();
            }

            public void Start(ConcurrentQueue<Incoming> queue, Action<ClientSession> onClosed)
            {
                var welcome = MiniJson.Serialize(new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["type"] = "welcome",
                    ["protocol"] = ProtocolVersion,
                    ["server"] = "codely-unity-mcp",
                    ["package_version"] = PackageVersion,
                    ["unity_version"] = Application.unityVersion,
                    ["project_root"] = ProjectRoot
                });
                Enqueue(welcome);

                _writeThread = new Thread(WriteLoop) { IsBackground = true, Name = "Codely.UnityMcp.Write" + _id };
                _writeThread.Start();

                _readThread = new Thread(() =>
                {
                    try
                    {
                        while (Framing.TryReadFrame(_stream, out byte[] payload))
                        {
                            string json = Encoding.UTF8.GetString(payload);
                            queue.Enqueue(new Incoming
                            {
                                Json = json,
                                Reply = Enqueue
                            });
                        }
                    }
                    catch (Exception) { /* connection closed */ }
                    finally
                    {
                        try { _outbox.CompleteAdding(); } catch { }
                        onClosed(this);
                    }
                })
                { IsBackground = true, Name = "Codely.UnityMcp.Read" + _id };
                _readThread.Start();
            }

            public void Enqueue(string json)
            {
                if (string.IsNullOrEmpty(json)) return;
                try
                {
                    if (!_outbox.IsAddingCompleted)
                        _outbox.Add(Encoding.UTF8.GetBytes(json));
                }
                catch (InvalidOperationException) { /* completed */ }
            }

            private void WriteLoop()
            {
                try
                {
                    foreach (byte[] payload in _outbox.GetConsumingEnumerable())
                    {
                        if (!Framing.TryWriteFrame(_stream, payload)) break;
                    }
                }
                catch (Exception) { /* connection closed */ }
                finally
                {
                    try { _client.Close(); } catch { }
                }
            }

            public void Close()
            {
                try { _outbox.CompleteAdding(); } catch { }
                try { _client.Close(); } catch { }
            }
        }
    }
}
