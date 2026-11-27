using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Lament.VisualNovel
{
    public sealed class NovelLuaSurface : INovelLuaSurface
    {
        private readonly Dictionary<int, Image> images = new Dictionary<int, Image>();
        private readonly Transform parent;
        private readonly NovelPresentationController view;
        private readonly Func<NovelStoryAsset> story;
        private RectTransform root;
        private int nextId;
        public int Count => images.Count;
        public RectTransform Root => root;

        public NovelLuaSurface(NovelPresentationController view, Func<NovelStoryAsset> story)
        {
            this.view = view;
            this.story = story;
            parent = view.overlay.transform.parent;
        }
        private void EnsureRoot()
        {
            if (root != null) return;
            root = new GameObject("Lua临时演出层", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(parent, false);
            root.SetSiblingIndex(view.overlay.transform.GetSiblingIndex());
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
        }
        private int Add(Sprite sprite)
        {
            if (images.Count >= 128) throw new InvalidOperationException("Lua临时图片上限128个");
            EnsureRoot();
            var image = new GameObject("Lua图片", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(root, false);
            image.sprite = sprite;
            image.raycastTarget = false;
            image.preserveAspect = true;
            image.rectTransform.sizeDelta = new Vector2(200, 200);
            int id = ++nextId;
            images.Add(id, image);
            return id;
        }
        public int Create(string resourceId)
        {
            var sprite = story()?.FindResource(resourceId)?.image;
            if (sprite == null) throw new InvalidOperationException("Lua图片资源不存在：" + resourceId);
            return Add(sprite);
        }
        public int ClonePortrait(int slot)
        {
            if (slot < 0 || slot >= view.portraits.Length) throw new InvalidOperationException("Lua立绘槽越界");
            return Clone(view.portraits[slot]);
        }
        public int CloneBackground() => Clone(view.background);
        private int Clone(Image source)
        {
            if (source.sprite == null) throw new InvalidOperationException("Lua克隆目标没有图片");
            int id = Add(source.sprite);
            var image = images[id];
            image.rectTransform.position = source.rectTransform.position;
            image.rectTransform.sizeDelta = source.rectTransform.rect.size;
            image.transform.localScale = source.transform.localScale;
            image.transform.rotation = source.transform.rotation;
            image.color = source.color;
            return id;
        }
        public void Set(int handle, string property, float value)
        {
            if (!images.TryGetValue(handle, out var image)) throw new InvalidOperationException("Lua图片句柄已删除或不存在：" + handle);
            if (float.IsNaN(value) || float.IsInfinity(value) || Mathf.Abs(value) > 100000) throw new InvalidOperationException("Lua图片参数无效");
            var rect = image.rectTransform;
            var pos = rect.anchoredPosition;
            var scale = rect.localScale;
            var size = rect.sizeDelta;
            var color = image.color;
            switch (property)
            {
                case "x": pos.x = value; rect.anchoredPosition = pos; break;
                case "y": pos.y = value; rect.anchoredPosition = pos; break;
                case "scale_x": scale.x = Mathf.Clamp(value, -20, 20); rect.localScale = scale; break;
                case "scale_y": scale.y = Mathf.Clamp(value, -20, 20); rect.localScale = scale; break;
                case "width": size.x = Mathf.Clamp(value, 0, 8192); rect.sizeDelta = size; break;
                case "height": size.y = Mathf.Clamp(value, 0, 8192); rect.sizeDelta = size; break;
                case "rotation": rect.localRotation = Quaternion.Euler(0, 0, value); break;
                case "alpha": color.a = Mathf.Clamp01(value); image.color = color; break;
                case "r": color.r = Mathf.Clamp01(value); image.color = color; break;
                case "g": color.g = Mathf.Clamp01(value); image.color = color; break;
                case "b": color.b = Mathf.Clamp01(value); image.color = color; break;
                default: throw new InvalidOperationException("不支持的Lua图片属性：" + property);
            }
        }
        public void Remove(int handle)
        {
            if (!images.TryGetValue(handle, out var image)) return;
            images.Remove(handle);
            if (image != null) { image.gameObject.SetActive(false); Destroy(image.gameObject); }
        }
        public void Clear()
        {
            images.Clear();
            if (root != null) { root.gameObject.SetActive(false); Destroy(root.gameObject); root = null; }
        }
        private static void Destroy(GameObject target)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(target);
            else UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
