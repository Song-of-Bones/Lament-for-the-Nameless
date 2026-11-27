using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Lament.VisualNovel
{
    // One tween per visual channel; replacement keeps the current visible value.
    public sealed class NovelStageEffects
    {
        private sealed class Tween
        {
            public float time;
            public float duration;
            public NovelEase ease;
            public Action<float> apply;
            public Action cleanup;
        }
        private readonly Dictionary<string, Tween> active = new Dictionary<string, Tween>();
        private readonly Image background;
        private readonly Image[] portraits;
        private readonly Image overlay;
        private readonly Vector2[] positions;
        private readonly Vector3[] scales;
        private readonly Quaternion[] rotations;
        private readonly Image flash;
        private readonly Image cg;
        private readonly Vector2 backgroundPosition;
        private readonly Vector3 backgroundScale;
        private readonly Quaternion backgroundRotation;
        public Image CG => cg;
        public int ActiveCount => active.Count;

        public NovelStageEffects(Image background, Image[] portraits, Image overlay)
        {
            this.background = background; this.portraits = portraits; this.overlay = overlay;
            backgroundPosition = background.rectTransform.anchoredPosition;
            backgroundScale = background.transform.localScale;
            backgroundRotation = background.transform.localRotation;
            cg = CreateLayer(overlay, "剧情CG与道具层", true);
            cg.preserveAspect = true; cg.enabled = false;
            positions = new Vector2[portraits.Length]; scales = new Vector3[portraits.Length]; rotations = new Quaternion[portraits.Length];
            for (int i = 0; i < portraits.Length; i++)
            {
                positions[i] = portraits[i].rectTransform.anchoredPosition;
                scales[i] = portraits[i].transform.localScale;
                rotations[i] = portraits[i].transform.localRotation;
            }
            var go = new GameObject("剧情闪光层", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            flash = go.GetComponent<Image>();
            go.transform.SetParent(overlay.transform.parent, false);
            go.transform.SetSiblingIndex(overlay.transform.GetSiblingIndex() + 1);
            flash.rectTransform.anchorMin = Vector2.zero; flash.rectTransform.anchorMax = Vector2.one;
            flash.rectTransform.offsetMin = flash.rectTransform.offsetMax = Vector2.zero;
            flash.raycastTarget = false; flash.color = Color.clear;
        }

        private static Image CreateLayer(Image reference, string name, bool below)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var image = go.GetComponent<Image>();
            go.transform.SetParent(reference.transform.parent, false);
            go.transform.SetSiblingIndex(reference.transform.GetSiblingIndex() + (below ? 0 : 1));
            image.rectTransform.anchorMin = Vector2.zero; image.rectTransform.anchorMax = Vector2.one;
            image.rectTransform.offsetMin = image.rectTransform.offsetMax = Vector2.zero;
            image.raycastTarget = false; image.color = Color.clear;
            return image;
        }

        public void ApplyBackgroundPose(NovelPortraitPose pose)
        {
            background.rectTransform.anchoredPosition = backgroundPosition + pose.position;
            background.transform.localScale = Vector3.Scale(backgroundScale, new Vector3(pose.scale.x, pose.scale.y, 1));
            background.transform.localRotation = backgroundRotation * Quaternion.Euler(0, 0, pose.rotation);
        }

        public static float Ease(NovelEase ease, float t)
        {
            t = Mathf.Clamp01(t);
            switch (ease)
            {
                case NovelEase.Smooth: return t * t * (3 - 2 * t);
                case NovelEase.EaseIn: return t * t;
                case NovelEase.EaseOut: return 1 - (1 - t) * (1 - t);
                default: return t;
            }
        }

        public bool Busy(string key) => active.ContainsKey(key);
        private void Cancel(string key)
        {
            if (!active.TryGetValue(key, out var old)) return;
            active.Remove(key); old.cleanup?.Invoke();
        }
        private void Animate(string key, NovelNodeAsset node, Action<float> apply, Action cleanup = null)
        {
            Cancel(key);
            if (node.seconds <= 0) { apply(1); cleanup?.Invoke(); return; }
            active[key] = new Tween { duration = node.seconds, ease = node.easing, apply = apply, cleanup = cleanup };
            apply(0);
        }
        public void Tick(float delta, bool paused)
        {
            if (paused) return;
            foreach (var key in new List<string>(active.Keys))
            {
                var tween = active[key]; tween.time = Mathf.Min(tween.duration, tween.time + Mathf.Max(0, delta));
                tween.apply(Ease(tween.ease, tween.time / tween.duration));
                if (tween.time >= tween.duration) { active.Remove(key); tween.cleanup?.Invoke(); }
            }
        }
        public void CancelAll()
        {
            foreach (var key in new List<string>(active.Keys)) Cancel(key);
            flash.color = Color.clear;
        }

        public void ApplyPose(int slot, NovelPortraitPose pose)
        {
            var image = portraits[slot];
            image.rectTransform.anchoredPosition = positions[slot] + pose.position;
            image.transform.localScale = Vector3.Scale(scales[slot], new Vector3(pose.scale.x, pose.scale.y, 1));
            image.transform.localRotation = rotations[slot] * Quaternion.Euler(0, 0, pose.rotation);
            var color = image.color; color.a = pose.alpha; image.color = color;
        }
        public void Restore(NovelStoryAsset story, NovelPresentationState state)
        {
            CancelAll(); state.EnsureDefaults();
            background.sprite = story.FindResource(state.background)?.image;
            background.color = background.sprite != null ? Color.white : new Color(.035f, .04f, .055f);
            for (int i = 0; i < portraits.Length; i++)
            {
                portraits[i].sprite = story.FindResource(state.portraits[i])?.image;
                portraits[i].enabled = portraits[i].sprite != null;
                ApplyPose(i, state.poses[i]);
            }
            overlay.color = state.overlay;
            ApplyBackgroundPose(state.backgroundPose);
            cg.sprite = story.FindResource(state.cg)?.image;
            cg.color = Color.white; cg.enabled = cg.sprite != null;
        }
        public void Execute(NovelStoryAsset story, NovelNodeAsset node)
        {
            int slot = node.portraitSlot;
            switch (node.command)
            {
                case NovelCommandType.ShowCG:
                case NovelCommandType.HideCG:
                    SwitchImage("cg", cg, node.command == NovelCommandType.ShowCG ? story.FindResource(node.resourceId)?.image : null, node, 1); break;
                case NovelCommandType.BackgroundTransform:
                    Cancel("shake"); Cancel("backgroundPose"); Cancel("distortion");
                    var bgRect = background.rectTransform;
                    var startPos = bgRect.anchoredPosition; var startScale = bgRect.localScale; var startRotation = bgRect.localRotation;
                    var endPos = backgroundPosition + node.pose.position;
                    var endScale = Vector3.Scale(backgroundScale, new Vector3(node.pose.scale.x, node.pose.scale.y, 1));
                    var endRotation = backgroundRotation * Quaternion.Euler(0, 0, node.pose.rotation);
                    Animate("backgroundPose", node, t =>
                    {
                        bgRect.anchoredPosition = Vector2.Lerp(startPos, endPos, t);
                        bgRect.localScale = Vector3.Lerp(startScale, endScale, t);
                        bgRect.localRotation = Quaternion.Slerp(startRotation, endRotation, t);
                    });
                    break;
                case NovelCommandType.Shatter:
                    Shatter(node);
                    break;
                case NovelCommandType.Distortion:
                    Cancel("distortion");
                    var doubleImage = CreateLayer(overlay, "错位重影", true);
                    doubleImage.sprite = background.sprite; doubleImage.preserveAspect = background.preserveAspect;
                    var doubleRect = doubleImage.rectTransform;
                    doubleRect.localScale = background.rectTransform.localScale;
                    Animate("distortion", node, t =>
                    {
                        float phase = t * node.seconds * node.shakeFrequency;
                        doubleRect.anchoredPosition = background.rectTransform.anchoredPosition + new Vector2(Mathf.Sin(phase * 7), Mathf.Cos(phase * 11)) * node.shakeStrength * (1 - t);
                        var tint = node.color; tint.a *= 1 - t; doubleImage.color = tint;
                    }, () => DestroyLayer(doubleImage));
                    break;
                case NovelCommandType.Background:
                    Cancel("distortion");
                    SwitchImage("background", background, story.FindResource(node.resourceId)?.image, node, 1); break;
                case NovelCommandType.Portrait:
                case NovelCommandType.PortraitExpression:
                case NovelCommandType.HidePortrait:
                    var resourceId = node.command == NovelCommandType.PortraitExpression ? story.ResolveExpression(node.characterId, node.expressionId) : node.resourceId;
                    SwitchImage("portrait" + slot, portraits[slot], node.command == NovelCommandType.HidePortrait ? null : story.FindResource(resourceId)?.image, node, portraits[slot].color.a); break;
                case NovelCommandType.PortraitTransform:
                    Cancel("shake");
                    Cancel("portrait" + slot);
                    Cancel("pose" + slot);
                    var image = portraits[slot]; var rect = image.rectTransform;
                    var fromPos = rect.anchoredPosition; var fromScale = rect.localScale; var fromRotation = rect.localRotation; float fromAlpha = image.color.a;
                    var targetPos = positions[slot] + node.pose.position;
                    var targetScale = Vector3.Scale(scales[slot], new Vector3(node.pose.scale.x, node.pose.scale.y, 1));
                    var targetRotation = rotations[slot] * Quaternion.Euler(0, 0, node.pose.rotation);
                    Animate("pose" + slot, node, t =>
                    {
                        rect.anchoredPosition = Vector2.LerpUnclamped(fromPos, targetPos, t);
                        rect.localScale = Vector3.LerpUnclamped(fromScale, targetScale, t);
                        rect.localRotation = Quaternion.SlerpUnclamped(fromRotation, targetRotation, t);
                        var c = image.color; c.a = Mathf.Lerp(fromAlpha, node.pose.alpha, t); image.color = c;
                    });
                    break;
                case NovelCommandType.Fade:
                    var start = overlay.color;
                    Animate("overlay", node, t => overlay.color = Color.Lerp(start, node.color, t)); break;
                case NovelCommandType.Flash:
                    Animate("flash", node, t => { var c = node.color; c.a *= 1 - t; flash.color = c; }, () => flash.color = Color.clear); break;
                case NovelCommandType.Shake:
                    Cancel("shake");
                    Cancel("backgroundPose");
                    for (int i = 0; i < portraits.Length; i++) Cancel("pose" + i);
                    var targets = new List<RectTransform> { background.rectTransform };
                    foreach (var portrait in portraits) targets.Add(portrait.rectTransform);
                    var origins = new Vector2[targets.Count];
                    for (int i = 0; i < targets.Count; i++) origins[i] = targets[i].anchoredPosition;
                    // A separate container is unnecessary: offsets are removed before each update, so motion can run concurrently.
                    Vector2 previous = Vector2.zero;
                    Action clear = () => { foreach (var target in targets) target.anchoredPosition -= previous; previous = Vector2.zero; };
                    Animate("shake", node, t =>
                    {
                        clear();
                        float phase = t * node.seconds * node.shakeFrequency * Mathf.PI * 2;
                        previous = new Vector2(Mathf.Sin(phase), Mathf.Sin(phase * 1.37f)) * node.shakeStrength * (1 - t);
                        foreach (var target in targets) target.anchoredPosition += previous;
                    }, clear);
                    break;
            }
        }
        private void Shatter(NovelNodeAsset node)
        {
            Cancel("shatter");
            if (background.sprite == null) return;
            var layer = CreateLayer(overlay, "背景碎片转场", true);
            layer.color = Color.clear;
            var root = layer.rectTransform;
            root.anchorMin = background.rectTransform.anchorMin;
            root.anchorMax = background.rectTransform.anchorMax;
            root.offsetMin = background.rectTransform.offsetMin;
            root.offsetMax = background.rectTransform.offsetMax;
            root.localScale = background.rectTransform.localScale;
            root.localRotation = background.rectTransform.localRotation;
            var pieces = new RectTransform[16];
            var graphics = new RawImage[16];
            var uv = background.sprite.rect;
            var texture = background.sprite.texture;
            for (int i = 0; i < 16; i++)
            {
                int x = i % 4; int y = i / 4;
                var go = new GameObject("碎片" + i, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                var graphic = go.GetComponent<RawImage>();
                graphic.transform.SetParent(root, false);
                var rect = graphic.rectTransform;
                rect.anchorMin = new Vector2(x / 4f, y / 4f); rect.anchorMax = new Vector2((x + 1) / 4f, (y + 1) / 4f);
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                graphic.texture = texture;
                graphic.uvRect = new Rect((uv.x + x * uv.width / 4) / texture.width, (uv.y + y * uv.height / 4) / texture.height, uv.width / 4 / texture.width, uv.height / 4 / texture.height);
                graphic.raycastTarget = false;
                pieces[i] = rect; graphics[i] = graphic;
            }
            Animate("shatter", node, t =>
            {
                for (int i = 0; i < pieces.Length; i++)
                {
                    var direction = new Vector2(i % 4 - 1.5f, i / 4 - 1.5f);
                    pieces[i].anchoredPosition = direction * node.shakeStrength * t * 8;
                    pieces[i].localRotation = Quaternion.Euler(0, 0, (i % 2 == 0 ? 1 : -1) * t * 35);
                    graphics[i].color = new Color(1, 1, 1, 1 - t);
                }
            }, () => DestroyLayer(layer));
        }

        private static void DestroyLayer(Image image)
        {
            if (image == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(image.gameObject);
            else UnityEngine.Object.DestroyImmediate(image.gameObject);
        }

        private void SwitchImage(string key, Image image, Sprite target, NovelNodeAsset node, float alpha)
        {
            Cancel(key);
            Image ghost = null;
            if (node.seconds > 0 && image.enabled && image.sprite != null)
            {
                var go = new GameObject("切换残影", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                ghost = go.GetComponent<Image>();
                ghost.transform.SetParent(image.transform, false);
                ghost.rectTransform.anchorMin = Vector2.zero; ghost.rectTransform.anchorMax = Vector2.one;
                ghost.rectTransform.offsetMin = ghost.rectTransform.offsetMax = Vector2.zero;
                ghost.sprite = image.sprite; ghost.preserveAspect = image.preserveAspect; ghost.raycastTarget = false;
            }
            image.sprite = target; image.enabled = target != null || ghost != null;
            Animate(key, node, t =>
            {
                var c = image.color; c.a = target == null ? 0 : alpha * t; image.color = c;
                if (ghost != null) ghost.color = new Color(1, 1, 1, alpha * (1 - t));
            }, () =>
            {
                if (ghost != null)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(ghost.gameObject);
                    else UnityEngine.Object.DestroyImmediate(ghost.gameObject);
                }
                image.enabled = image.sprite != null;
            });
        }
    }
}
