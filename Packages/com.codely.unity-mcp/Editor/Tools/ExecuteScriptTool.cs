using System;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.CSharp;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Codely.UnityMcp
{
    /// <summary>
    /// Compiles a C# snippet with the editor's built-in compiler and runs it
    /// synchronously. This is the escape hatch for work no dedicated tool covers.
    ///
    /// Deliberate limits: synchronous only (Task/IEnumerator are returned as-is), and
    /// blocked in Play Mode so a script cannot silently mutate project state that Unity
    /// would throw away.
    /// </summary>
    public static class ExecuteScriptTool
    {
        private const string HostTypeName = "CodelyUnityMcp.ScriptHost";

        public static ToolResult Handle(IDictionary<string, object> p)
        {
            string script = JsonUtil.Str(p, "script");
            string scriptPath = JsonUtil.Str(p, "script_path", null, "scriptPath");

            if (string.IsNullOrWhiteSpace(script) && string.IsNullOrWhiteSpace(scriptPath))
                throw new ToolException("missing_param", "one of 'script' or 'script_path' is required.");

            if (string.IsNullOrWhiteSpace(script))
            {
                string absolute = scriptPath;
                if (!File.Exists(absolute)) absolute = PathUtil.Absolute(PathUtil.AssetsPath(scriptPath));
                if (!File.Exists(absolute))
                    throw new ToolException("script_not_found", $"Script file '{scriptPath}' was not found.");
                script = File.ReadAllText(absolute);
            }

            string summary = JsonUtil.Str(p, "summary", null, "description") ?? "script";
            var imports = JsonUtil.StrArray(p, "imports");
            bool captureLogs = JsonUtil.Bool(p, "capture_logs", true, "captureLogs");

            string source = BuildSource(script, imports);
            var logs = new List<object>();
            Application.LogCallback callback = null;
            if (captureLogs)
            {
                callback = (message, stack, type) => logs.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["type"] = type.ToString().ToLowerInvariant(),
                    ["message"] = message
                });
                Application.logMessageReceived += callback;
            }

            try
            {
                var assembly = Compile(source);
                var type = assembly.GetType(HostTypeName);
                if (type == null)
                    throw new ToolException("compile_error", $"Compiled assembly did not contain {HostTypeName}.");

                var method = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
                if (method == null)
                    throw new ToolException("compile_error", "Compiled script has no public static Run() method.");

                object raw;
                try { raw = method.Invoke(null, null); }
                catch (TargetInvocationException ex)
                {
                    var inner = ex.InnerException ?? ex;
                    throw new ToolException("script_error", inner.Message, new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["exception"] = inner.GetType().FullName,
                        ["stackTrace"] = inner.StackTrace
                    });
                }

                var data = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["summary"] = summary,
                    ["result"] = Normalize(raw),
                    ["returns"] = raw != null ? raw.GetType().FullName : "null"
                };
                if (captureLogs) data["logs"] = logs;

                return new ToolResult($"Script '{summary}' executed.", data);
            }
            finally
            {
                if (callback != null) Application.logMessageReceived -= callback;
            }
        }

        private static Assembly Compile(string source)
        {
            var provider = new CSharpCodeProvider();
            string outputAssembly = Path.Combine(
                Path.GetTempPath(),
                "CodelyUnityMcp_" + Guid.NewGuid().ToString("N") + ".dll");

            var options = new CompilerParameters
            {
                // GenerateInMemory silently fails on some Mono toolchains; writing a real
                // assembly and loading its bytes makes failures diagnosable and avoids
                // locking the temp file in the editor process.
                GenerateInMemory = false,
                GenerateExecutable = false,
                OutputAssembly = outputAssembly,
                TreatWarningsAsErrors = false,
                WarningLevel = 0
            };

            foreach (var reference in References())
                options.ReferencedAssemblies.Add(reference);

            CompilerResults results;
            try { results = provider.CompileAssemblyFromSource(options, source); }
            catch (Exception ex)
            {
                throw new ToolException("compiler_unavailable",
                    "C# compilation is unavailable in this editor: " + ex.Message);
            }

            if (results.Errors != null && results.Errors.HasErrors)
            {
                var errors = new List<object>();
                foreach (CompilerError error in results.Errors)
                {
                    if (error.IsWarning) continue;
                    errors.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["line"] = error.Line,
                        ["column"] = error.Column,
                        ["number"] = error.ErrorNumber,
                        ["message"] = error.ErrorText
                    });
                }
                throw new ToolException("compile_error", "The script did not compile.", new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["errors"] = errors,
                    ["hint"] = "The body is wrapped in 'public static object Run()'. Use 'return value;' to return data."
                });
            }

            if (!File.Exists(outputAssembly))
            {
                var output = results.Output != null
                    ? string.Join("\n", results.Output.Cast<string>().ToArray())
                    : string.Empty;
                throw new ToolException("compiler_unavailable",
                    "The C# compiler produced no assembly. " +
                    $"Exit code: {results.NativeCompilerReturnValue}. Output: {(output.Length > 800 ? output.Substring(0, 800) : output)}");
            }

            byte[] bytes = File.ReadAllBytes(outputAssembly);
            try { File.Delete(outputAssembly); } catch { /* temp file, best effort */ }
            return Assembly.Load(bytes);
        }

        private static IEnumerable<string> References()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string path)
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                seen.Add(path);
            }

            // Fixed core surface. The compiler is spawned as a child process, so the
            // reference list becomes command-line arguments: passing every loaded
            // assembly overflows the Windows limit. Project scripts are added below.
            Add(typeof(object).Assembly.Location);
            Add(typeof(Uri).Assembly.Location);
            Add(typeof(Enumerable).Assembly.Location);
            Add(typeof(UnityEngine.Object).Assembly.Location);
            Add(typeof(UnityEngine.Debug).Assembly.Location);
            Add(typeof(UnityEditor.Editor).Assembly.Location);
            Add(typeof(UnityEditor.AssetDatabase).Assembly.Location);
            Add(typeof(UnityEditor.SceneManagement.EditorSceneManager).Assembly.Location);

            // Unity's editor targets netstandard 2.1; without this facade the compiler
            // rejects even System.Object in generated code (CS0012).
            string coreDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location) ?? string.Empty;
            if (coreDirectory.Length > 0)
                Add(Path.Combine(coreDirectory, "Facades", "netstandard.dll"));

            int projectAssemblies = 0;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (projectAssemblies >= 8) break;
                if (assembly.IsDynamic) continue;

                string name;
                try { name = assembly.GetName().Name; }
                catch { continue; }

                if (name != "Assembly-CSharp" && name != "Assembly-CSharp-Editor" &&
                    !name.StartsWith("Assembly-CSharp-", StringComparison.Ordinal))
                    continue;

                Add(assembly.Location);
                projectAssemblies++;
            }

            return seen;
        }

        private static string BuildSource(string body, string[] imports)
        {
            var sb = new StringBuilder(1024);
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using System.Linq;");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine("using UnityEditor;");
            sb.AppendLine("using UnityEngine.SceneManagement;");
            sb.AppendLine("using UnityEditor.SceneManagement;");
            if (imports != null)
            {
                foreach (var import in imports)
                {
                    string ns = (import ?? string.Empty).Trim().TrimEnd(';');
                    if (ns.Length == 0) continue;
                    sb.Append("using ").Append(ns).AppendLine(";");
                }
            }

            sb.AppendLine("namespace CodelyUnityMcp {");
            sb.AppendLine("  public static class ScriptHost {");
            sb.AppendLine("    public static object Run() {");
            sb.AppendLine(body);
            sb.AppendLine("      return null;");
            sb.AppendLine("    }");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        /// <summary>
        /// Converts arbitrary script return values into JSON-safe data instead of letting
        /// MiniJson serialize a live Unity object graph.
        /// </summary>
        private static object Normalize(object value)
        {
            if (value == null) return null;
            if (value is string || value is bool) return value;
            if (value is int || value is long || value is float || value is double || value is decimal)
                return value;
            if (value is Object unityObject)
            {
                return new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["name"] = unityObject.name,
                    ["type"] = unityObject.GetType().Name,
                    ["instanceID"] = UnityEngine.EntityId.ToULong(unityObject.GetEntityId()),
                    ["assetPath"] = string.IsNullOrEmpty(AssetDatabase.GetAssetPath(unityObject))
                        ? null
                        : AssetDatabase.GetAssetPath(unityObject)
                };
            }
            if (value is IDictionary<string, object> dict)
            {
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var kv in dict) result[kv.Key] = Normalize(kv.Value);
                return result;
            }
            if (value is IDictionary loose)
            {
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (DictionaryEntry entry in loose)
                    result[Convert.ToString(entry.Key)] = Normalize(entry.Value);
                return result;
            }
            if (value is IEnumerable sequence)
            {
                var list = new List<object>();
                foreach (var item in sequence) list.Add(Normalize(item));
                return list;
            }
            return value.ToString();
        }
    }
}
