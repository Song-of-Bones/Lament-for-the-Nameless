using System;
using MoonSharp.Interpreter;

namespace Lament.VisualNovel
{
    public interface INovelLuaSurface
    {
        int Create(string resourceId);
        int ClonePortrait(int slot);
        int CloneBackground();
        void Set(int handle, string property, float value);
        void Remove(int handle);
        void Clear();
    }

    // No userdata registration: the VM only sees numbers, strings, and explicit callbacks.
    public sealed class NovelLuaEffect
    {
        public const int MaxSourceLength = 65536;
        private readonly INovelLuaSurface surface;
        private readonly Action completed;
        private readonly Action<string> failed;
        private Script script;
        private Coroutine coroutine;
        private float wait;
        private float elapsed;
        private float timeout;
        private int resumes;
        private int forcedYields;
        private int calls;
        private string label;
        public bool Running { get; private set; }
        public string LastError { get; private set; }

        public NovelLuaEffect(INovelLuaSurface surface, Action completed, Action<string> failed)
        {
            this.surface = surface;
            this.completed = completed;
            this.failed = failed;
        }

        public void Start(string source, string name, float duration)
        {
            Cancel();
            label = name;
            LastError = null;
            try
            {
                if (string.IsNullOrWhiteSpace(source) || source.Length > MaxSourceLength)
                    throw new InvalidOperationException("Lua源文件为空或超过64K字符");
                if (float.IsNaN(duration) || float.IsInfinity(duration) || duration < .1f || duration > 120)
                    throw new InvalidOperationException("Lua时限必须为0.1～120秒");
                script = new Script(CoreModules.Basic | CoreModules.GlobalConsts | CoreModules.Math | CoreModules.Table | CoreModules.TableIterators | CoreModules.Coroutine);
                // No io/os/debug/require/load/dofile/CLR, and no nested coroutines that bypass the host budget.
                var fx = new Table(script);
                fx.Set("image", Callback(args => DynValue.NewNumber(surface.Create(Text(args, 0)))));
                fx.Set("portrait", Callback(args => DynValue.NewNumber(surface.ClonePortrait(Integer(args, 0)))));
                fx.Set("background", Callback(args => DynValue.NewNumber(surface.CloneBackground())));
                fx.Set("set", Callback(args => { surface.Set(Integer(args, 0), Text(args, 1), Number(args, 2)); return DynValue.Nil; }));
                fx.Set("remove", Callback(args => { surface.Remove(Integer(args, 0)); return DynValue.Nil; }));
                script.Globals.Set("fx", DynValue.NewTable(fx));
                script.DoString("local yield = coroutine.yield; fx.wait = function(seconds) return yield(seconds or 0) end; coroutine = nil; collectgarbage = nil; print = nil", null, "effect-bootstrap");
                var function = script.LoadString(source, null, name);
                coroutine = script.CreateCoroutine(function).Coroutine;
                coroutine.AutoYieldCounter = 10000;
                timeout = duration;
                elapsed = wait = 0;
                calls = resumes = forcedYields = 0;
                Running = true;
            }
            catch (Exception ex) { Fail(ex); }
        }

        private DynValue Callback(Func<CallbackArguments, DynValue> action)
        {
            return DynValue.NewCallback((context, args) =>
            {
                if (++calls > 50000) throw new ScriptRuntimeException("演出API调用超过50000次");
                return action(args);
            });
        }
        private static string Text(CallbackArguments args, int index)
        {
            if (args[index].Type != DataType.String || args[index].String.Length > 256) throw new ScriptRuntimeException("需要不超过256字符的字符串参数");
            return args[index].String;
        }
        private static float Number(CallbackArguments args, int index)
        {
            if (args[index].Type != DataType.Number) throw new ScriptRuntimeException("需要数值参数");
            double value = args[index].Number;
            if (double.IsNaN(value) || double.IsInfinity(value) || Math.Abs(value) > 100000) throw new ScriptRuntimeException("数值必须有限且绝对值不超过100000");
            return (float)value;
        }
        private static int Integer(CallbackArguments args, int index)
        {
            float value = Number(args, index);
            if (value != Math.Floor(value)) throw new ScriptRuntimeException("需要整数句柄/槽位");
            return (int)value;
        }

        public void Tick(float delta, bool paused)
        {
            if (!Running || paused) return;
            try
            {
                if (float.IsNaN(delta) || float.IsInfinity(delta) || delta < 0) throw new InvalidOperationException("非法帧时长");
                elapsed += delta;
                if (elapsed > timeout) throw new InvalidOperationException("Lua演出超过时间预算");
                wait -= delta;
                if (wait > 0) return;
                if (++resumes > 10000) throw new InvalidOperationException("Lua恢复次数超过预算");
                var yielded = coroutine.Resume();
                if (coroutine.State == CoroutineState.Dead)
                {
                    Cancel();
                    completed?.Invoke();
                    return;
                }
                if (coroutine.State == CoroutineState.ForceSuspended)
                {
                    if (++forcedYields > 20) throw new InvalidOperationException("Lua指令预算耗尽，可能存在无限循环");
                    wait = 0;
                    return;
                }
                if (yielded.Type != DataType.Number || double.IsNaN(yielded.Number) || double.IsInfinity(yielded.Number) || yielded.Number < 0 || yielded.Number > 120)
                    throw new InvalidOperationException("fx.wait只接受0～120秒");
                wait = (float)yielded.Number;
            }
            catch (Exception ex) { Fail(ex); }
        }

        public void Cancel()
        {
            Running = false;
            coroutine = null;
            script = null;
            surface.Clear();
        }
        private void Fail(Exception ex)
        {
            var lua = ex as InterpreterException;
            LastError = "[Lua " + label + "] " + (lua?.DecoratedMessage ?? ex.Message);
            Cancel();
            failed?.Invoke(LastError);
        }
    }
}
