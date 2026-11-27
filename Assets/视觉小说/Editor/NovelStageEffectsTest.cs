using System;
using System.IO;
using Lament.VisualNovel;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class NovelStageEffectsTest
{
    [MenuItem("工具/视觉小说/运行立绘特效测试")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("先停止播放");
        var root = new GameObject("演出测试夹具", typeof(RectTransform));
        var story = ScriptableObject.CreateInstance<NovelStoryAsset>();
        var node = ScriptableObject.CreateInstance<NovelNodeAsset>();
        var texture = new Texture2D(2, 2);
        var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * .5f);
        var expressionSprite = Sprite.Create(texture, new Rect(0, 0, 1, 2), Vector2.one * .5f);
        string report = "";
        Action<bool, string> check = (ok, text) => { if (!ok) throw new InvalidOperationException(text); report += "PASS " + text + "\n"; };
        NovelStageEffects effects = null;
        try
        {
            var bg = Image(root.transform, "背景"); var overlay = Image(root.transform, "遮罩");
            var portraits = new[] { Image(root.transform, "左"), Image(root.transform, "中"), Image(root.transform, "右") };
            story.resources.Add(new NovelResourceDefinition { id = "fixture", image = sprite });
            story.resources.Add(new NovelResourceDefinition { id = "smile-image", image = expressionSprite });
            var character = new NovelCharacterDefinition { id = "hero" };
            character.expressions.Add(new NovelExpressionDefinition { id = "smile", resourceId = "smile-image" });
            story.characters.Add(character);
            effects = new NovelStageEffects(bg, portraits, overlay);
            var state = new NovelPresentationState(); effects.Restore(story, state);
            node.command = NovelCommandType.Portrait; node.resourceId = "fixture"; node.portraitSlot = 1; node.seconds = 1;
            effects.Execute(story, node); effects.Tick(.5f, false);
            check(portraits[1].enabled && portraits[1].sprite == sprite, "立绘切入绑定图像");
            check(portraits[1].color.a > 0 && portraits[1].color.a < 1, "立绘切换中间帧透明度");
            float alpha = portraits[1].color.a; effects.Tick(.5f, true);
            check(portraits[1].color.a == alpha, "暂停冻结切换");
            effects.Tick(.5f, false); check(portraits[1].color.a == 1, "立绘切换完成");
            node.command = NovelCommandType.PortraitTransform; node.pose.position = new Vector2(120, 30); node.pose.scale = new Vector2(.8f, 1.2f); node.pose.rotation = 15; node.pose.alpha = .6f;
            effects.Execute(story, node); effects.Tick(.5f, false);
            check(portraits[1].rectTransform.anchoredPosition.x > 0 && portraits[1].rectTransform.anchoredPosition.x < 120, "立绘移动插值");
            effects.Tick(.5f, false);
            check(Vector2.Distance(portraits[1].rectTransform.anchoredPosition, node.pose.position) < .01f, "移动最终位置");
            check(Mathf.Abs(portraits[1].transform.localScale.x - .8f) < .01f, "缩放完成");
            check(Mathf.Abs(Mathf.DeltaAngle(portraits[1].transform.localEulerAngles.z, 15)) < .01f, "旋转完成");
            check(Mathf.Abs(portraits[1].color.a - .6f) < .01f, "透明度完成");
            node.command = NovelCommandType.PortraitExpression; node.characterId = "hero"; node.expressionId = "smile";
            effects.Execute(story, node); effects.Tick(.5f, false);
            check(portraits[1].sprite == expressionSprite && effects.Busy("portrait1"), "差分按角色和差分ID解析并渐变");
            effects.Tick(.5f, false);
            check(portraits[1].rectTransform.anchoredPosition == node.pose.position && Mathf.Abs(portraits[1].color.a - .6f) < .01f, "切差分保持位置和透明度");
            node.command = NovelCommandType.Background; node.resourceId = "fixture"; node.seconds = 0; effects.Execute(story, node);
            check(bg.sprite == sprite && !effects.Busy("background"), "背景立即切换");
            node.resourceId = "smile-image"; node.seconds = 1; effects.Execute(story, node); effects.Tick(.5f, false);
            check(bg.sprite == expressionSprite && bg.color.a > 0 && bg.color.a < 1 && bg.transform.childCount == 1, "背景交叉淡化保留旧图残影");
            effects.Tick(.5f, false); check(bg.transform.childCount == 0 && bg.color.a == 1, "背景渐变结束清理残影");
            node.command = NovelCommandType.Flash; node.color = Color.white; effects.Execute(story, node);
            check(effects.Busy("flash"), "闪光独立通道"); effects.Tick(1, false);
            check(!effects.Busy("flash"), "闪光完成清理");
            var origin = portraits[1].rectTransform.anchoredPosition;
            node.command = NovelCommandType.Shake; effects.Execute(story, node); effects.Tick(.13f, false);
            check(portraits[1].rectTransform.anchoredPosition != origin, "震动产生偏移");
            effects.CancelAll(); check(Vector2.Distance(portraits[1].rectTransform.anchoredPosition, origin) < .01f, "震动取消无位置残留");
            node.command = NovelCommandType.PortraitTransform; node.pose.position = new Vector2(500, 0); effects.Execute(story, node); effects.Tick(.2f, false);
            state.portraits[1] = "fixture"; state.EnsureDefaults(); state.poses[1].position = new Vector2(20, 40);
            effects.Restore(story, state);
            check(effects.ActiveCount == 0 && portraits[1].rectTransform.anchoredPosition == new Vector2(20, 40), "读档取消动画并重建最终变换");
            node.command = NovelCommandType.HidePortrait; effects.Execute(story, node); effects.Tick(1, false);
            check(!portraits[1].enabled, "立绘淡出隐藏");
            Debug.Log("[VisualNovel] 立绘特效测试全部通过\n" + report);
        }
        catch (Exception ex) { report += "FAIL " + ex + "\n"; Debug.LogError("[VisualNovel] 立绘特效测试失败\n" + report); }
        finally
        {
            effects?.CancelAll();
            UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(story);
            UnityEngine.Object.DestroyImmediate(node); UnityEngine.Object.DestroyImmediate(expressionSprite); UnityEngine.Object.DestroyImmediate(sprite); UnityEngine.Object.DestroyImmediate(texture);
            Directory.CreateDirectory(".codely-cli/test-results");
            File.WriteAllText(".codely-cli/test-results/visual-novel-stage.txt", report);
        }
    }
    private static Image Image(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false); return go.GetComponent<Image>();
    }
}
