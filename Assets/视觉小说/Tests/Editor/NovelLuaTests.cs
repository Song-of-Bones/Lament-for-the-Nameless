using System.Collections.Generic;
using Lament.VisualNovel;
using NUnit.Framework;

public sealed class NovelLuaTests
{
    private sealed class Surface : INovelLuaSurface
    {
        public int count;
        public int writes;
        public readonly Dictionary<string, float> values = new Dictionary<string, float>();
        public int Create(string id) { count++; return count; }
        public int ClonePortrait(int slot) => Create("portrait");
        public int CloneBackground() => Create("background");
        public void Set(int handle, string property, float value) { writes++; values[property] = value; }
        public void Remove(int handle) { count--; }
        public void Clear() { count = 0; }
    }

    [Test] public void ExecutesRealLuaMathLoopsAndWaitWithPause()
    {
        var surface = new Surface(); bool done = false; string error = null;
        var lua = new NovelLuaEffect(surface, () => done = true, e => error = e);
        lua.Start("local h=fx.image('test'); for i=1,3 do fx.set(h,'x',math.sin(i)*100); fx.wait(0.2) end", "math-test", 5);
        lua.Tick(.01f, false);
        Assert.AreEqual(1, surface.count); Assert.AreEqual(1, surface.writes);
        lua.Tick(100, true); Assert.IsTrue(lua.Running); Assert.AreEqual(1, surface.writes);
        for (int i = 0; i < 8; i++) lua.Tick(.21f, false);
        Assert.IsTrue(done); Assert.IsNull(error); Assert.AreEqual(3, surface.writes); Assert.AreEqual(0, surface.count);
    }
    [Test] public void InfiniteLoopIsPreemptedAndStopped()
    {
        var lua = new NovelLuaEffect(new Surface(), null, null);
        lua.Start("while true do end", "loop", 30);
        for (int i = 0; i < 30 && lua.Running; i++) lua.Tick(.01f, false);
        Assert.IsFalse(lua.Running); StringAssert.Contains("指令预算", lua.LastError);
    }
    [Test] public void ForbiddenGlobalsAreUnavailable()
    {
        string error = null; bool done = false;
        var lua = new NovelLuaEffect(new Surface(), () => done = true, e => error = e);
        lua.Start("assert(io==nil and os==nil and debug==nil and require==nil and load==nil and dofile==nil and luanet==nil and coroutine==nil)", "sandbox", 5);
        lua.Tick(.01f, false);
        Assert.IsTrue(done); Assert.IsNull(error);
    }
    [Test] public void SyntaxErrorsCarryScriptName()
    {
        var lua = new NovelLuaEffect(new Surface(), null, null);
        lua.Start("local =", "story/node/broken", 5);
        Assert.IsFalse(lua.Running); StringAssert.Contains("story/node/broken", lua.LastError);
    }
    [Test] public void InvalidWaitCleansObjects()
    {
        var surface = new Surface(); var lua = new NovelLuaEffect(surface, null, null);
        lua.Start("fx.image('test'); fx.wait(-1)", "wait", 5); lua.Tick(.01f, false);
        Assert.IsFalse(lua.Running); Assert.AreEqual(0, surface.count); Assert.IsNotNull(lua.LastError);
    }
    [Test] public void CancelAndTimeoutClearTemporaryObjects()
    {
        var surface = new Surface(); var lua = new NovelLuaEffect(surface, null, null);
        lua.Start("fx.image('test'); fx.wait(30)", "timeout", .1f); lua.Tick(.01f, false);
        Assert.AreEqual(1, surface.count); lua.Tick(.2f, false);
        Assert.AreEqual(0, surface.count); StringAssert.Contains("时间预算", lua.LastError);
        lua.Start("fx.image('test'); fx.wait(30)", "cancel", 30); lua.Tick(.01f, false); lua.Cancel();
        Assert.IsFalse(lua.Running); Assert.AreEqual(0, surface.count);
    }
    [Test] public void SourceSizeLimitIsEnforced()
    {
        var lua = new NovelLuaEffect(new Surface(), null, null);
        lua.Start(new string(' ', NovelLuaEffect.MaxSourceLength + 1), "oversize", 5);
        Assert.IsFalse(lua.Running); Assert.IsNotNull(lua.LastError);
    }
}
