using System.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Events;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 场景转场控制器：播放 .anim 帧动画（或等待目标 Animator），播放结束后淡出/加载目标场景。
/// 使用 Playables 播放片段，不要求片段标记为 Legacy，可复用现有的 Animator 片段。
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("场景/帧动画场景切换 (AnimSceneTransition)")]
public class AnimSceneTransition : MonoBehaviour
{
    public enum 转场方式
    {
        缓出缓入,
        缓出,
        缓入,
        直出直入,
    }

    public enum 计时方式
    {
        播放动画片段,
        等待目标动画,
        固定延时,
    }

    [Header("触发")]
    [Tooltip("勾选后进入场景自动开始；关闭后可调用 PlayNow() 手动触发")]
    [SerializeField] private bool 自动开始 = true;

    [Tooltip("等待期间点击鼠标或按任意键可立即转场")]
    [SerializeField] private bool 允许跳过 = true;

    [Header("动画设置")]
    [Tooltip("计时方式为「播放动画片段」时播放的 .anim 文件")]
    [SerializeField] private AnimationClip 帧动画文件;

    [Tooltip("动画作用的对象，其子层级路径需与片段绑定一致；留空则使用自身")]
    [SerializeField] private Transform 动画根节点;

    [Tooltip("播放动画片段=播放上面的片段；等待目标动画=等待指定 Animator 播完当前状态；固定延时=等待固定秒数")]
    [SerializeField] private 计时方式 计时 = 计时方式.等待目标动画;

    [Tooltip("计时方式为「等待目标动画」时，等待这个 Animator 的当前状态播放完毕")]
    [SerializeField] private Animator 等待目标;

    [Tooltip("计时方式为「固定延时」时的等待秒数")]
    [SerializeField, Min(0f)] private float 固定延时 = 3f;

    [Tooltip("片段播放速度")]
    [SerializeField, Min(0.01f)] private float 播放速度 = 1f;

    [Tooltip("开始播放前的等待时间")]
    [SerializeField, Min(0f)] private float 起始延迟 = 0f;

    [Tooltip("动画播放完毕后再等待的时间")]
    [SerializeField, Min(0f)] private float 播放完毕等待 = 0f;

    [Tooltip("循环播放片段（仅「播放动画片段」方式有效）")]
    [SerializeField] private bool 循环播放 = false;

    [Header("场景切换")]
    [Tooltip("动画播完后要跳转的场景（需已加入 Build Settings）")]
    [SerializeField] private string 目标场景 = "";

    [Tooltip("缓出缓入=淡出到黑再从黑淡入；缓出=只淡出后切换；缓入=切换后从黑淡入；直出直入=立即切换")]
    [SerializeField] private 转场方式 场景切换方式 = 转场方式.缓出缓入;

    [Tooltip("每段淡入淡出各持续的时长（秒）")]
    [SerializeField, Min(0f)] private float 转场时长 = 1f;

    [Tooltip("转场遮罩颜色")]
    [SerializeField] private Color 遮罩颜色 = Color.black;

    [Tooltip("遮罩在 Canvas 中的排序层级，越大越靠前")]
    [SerializeField] private int 遮罩层级 = 32767;

    [Tooltip("勾选后使用异步加载，避免大场景卡顿")]
    [SerializeField] private bool 异步加载 = true;

    [Header("事件")]
    [SerializeField] private UnityEvent 开始播放;
    [SerializeField] private UnityEvent 播放结束;
    [SerializeField] private UnityEvent 加载场景前;

    private PlayableGraph _graph;
    private AnimationClipPlayable _clipPlayable;
    private bool _running;
    private bool _skipRequested;

    /// <summary>开始播放并等待转场；重复调用会被忽略。</summary>
    public void PlayNow()
    {
        if (_running) return;
        StartCoroutine(Run());
    }

    /// <summary>跳过当前等待，立即执行转场。</summary>
    public void Skip()
    {
        if (_running && 允许跳过) _skipRequested = true;
    }

    private void Start()
    {
        if (自动开始) PlayNow();
    }

    private void Update()
    {
        if (!_running || !允许跳过 || _skipRequested) return;
        if (SkipPressed()) _skipRequested = true;
    }

    private static bool SkipPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null &&
            (Mouse.current.leftButton.wasPressedThisFrame || Mouse.current.rightButton.wasPressedThisFrame))
            return true;
        if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
            return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.anyKeyDown) return true;
#endif
        return false;
    }

    private IEnumerator Run()
    {
        _running = true;
        _skipRequested = false;
        开始播放?.Invoke();

        if (起始延迟 > 0f) yield return WaitUnscaled(起始延迟);

        switch (计时)
        {
            case 计时方式.播放动画片段:
                if (帧动画文件 == null)
                {
                    Debug.LogWarning("[AnimSceneTransition] 未设置帧动画文件，改用固定延时。", this);
                    yield return WaitUnscaled(固定延时);
                }
                else
                {
                    yield return PlayClipRoutine();
                }
                break;

            case 计时方式.等待目标动画:
                if (等待目标 == null)
                {
                    Debug.LogWarning("[AnimSceneTransition] 未设置等待目标 Animator，改用固定延时。", this);
                    yield return WaitUnscaled(固定延时);
                }
                else
                {
                    yield return WaitAnimatorRoutine();
                }
                break;

            default:
                yield return WaitUnscaled(固定延时);
                break;
        }

        if (播放完毕等待 > 0f) yield return WaitUnscaled(播放完毕等待);

        播放结束?.Invoke();
        加载场景前?.Invoke();
        TriggerTransition();
    }

    private IEnumerator PlayClipRoutine()
    {
        var root = 动画根节点 != null ? 动画根节点 : transform;
        PlayClip(帧动画文件, root);

        float length = 帧动画文件.length;
        if (length <= 0f) yield break;

        while (!_skipRequested)
        {
            if (_clipPlayable.GetTime() >= length)
            {
                if (!循环播放) break;
                _clipPlayable.SetTime(0);
            }
            yield return null;
        }

        StopClip();
    }

    private IEnumerator WaitAnimatorRoutine()
    {
        // 等一帧，确保 Animator 已进入状态
        yield return null;

        while (!_skipRequested)
        {
            if (!等待目标.gameObject.activeInHierarchy || !等待目标.enabled) yield break;

            var state = 等待目标.GetCurrentAnimatorStateInfo(0);
            if (!等待目标.IsInTransition(0) && state.length > 0f && state.normalizedTime >= 0.999f)
                yield break;

            yield return null;
        }
    }

    private IEnumerator WaitUnscaled(float seconds)
    {
        float elapsed = 0f;
        while (elapsed < seconds && !_skipRequested)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private void PlayClip(AnimationClip clip, Transform root)
    {
        var animator = root.GetComponent<Animator>();
        if (animator == null) animator = root.gameObject.AddComponent<Animator>();

        _graph = PlayableGraph.Create("AnimSceneTransition");
        _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

        _clipPlayable = AnimationClipPlayable.Create(_graph, clip);
        _clipPlayable.SetSpeed(播放速度);

        var output = AnimationPlayableOutput.Create(_graph, "Animation", animator);
        output.SetSourcePlayable(_clipPlayable);

        _graph.Play();
    }

    private void StopClip()
    {
        if (_graph.IsValid()) _graph.Stop();
    }

    private void OnDestroy()
    {
        if (_graph.IsValid()) _graph.Destroy();
    }

    private void TriggerTransition()
    {
        _running = false;

        if (string.IsNullOrEmpty(目标场景))
        {
            Debug.LogWarning("[AnimSceneTransition] 未设置目标场景！", this);
            return;
        }

        if (!Application.CanStreamedLevelBeLoaded(目标场景))
        {
            Debug.LogWarning($"[AnimSceneTransition] 场景 \"{目标场景}\" 无法加载，请确认已加入 Build Settings。", this);
            return;
        }

        bool 需要淡出 = 场景切换方式 == 转场方式.缓出缓入 || 场景切换方式 == 转场方式.缓出;
        bool 需要淡入 = 场景切换方式 == 转场方式.缓出缓入 || 场景切换方式 == 转场方式.缓入;

        if (场景切换方式 == 转场方式.直出直入 || 转场时长 <= 0f)
        {
            if (异步加载) SceneManager.LoadSceneAsync(目标场景);
            else SceneManager.LoadScene(目标场景);
            return;
        }

        var runner = new GameObject("__场景转场执行器");
        DontDestroyOnLoad(runner);
        runner.AddComponent<SceneTransitionRunner>().Begin(
            目标场景, 需要淡出, 需要淡入, 转场时长, 遮罩颜色, 遮罩层级, 异步加载);
    }
}

/// <summary>
/// 转场执行器：负责全屏遮罩的渐变及场景加载。
/// </summary>
public class SceneTransitionRunner : MonoBehaviour
{
    public void Begin(string scene, bool fadeOut, bool fadeIn, float duration, Color color, int sortingOrder, bool async)
    {
        StartCoroutine(RunTransition(scene, fadeOut, fadeIn, duration, color, sortingOrder, async));
    }

    private IEnumerator RunTransition(string scene, bool fadeOut, bool fadeIn, float duration, Color color, int sortingOrder, bool async)
    {
        Image mask = null;

        if (fadeOut || fadeIn)
        {
            // 纯淡入时初始就是全遮罩；否则从透明开始
            mask = CreateMask(color, fadeIn && !fadeOut ? color.a : 0f, sortingOrder);
        }

        if (fadeOut) yield return FadeMask(mask, 0f, color.a, duration);

        if (async)
        {
            var op = SceneManager.LoadSceneAsync(scene);
            if (op != null) while (!op.isDone) yield return null;
        }
        else
        {
            SceneManager.LoadScene(scene);
        }

        if (fadeIn) yield return FadeMask(mask, color.a, 0f, duration);

        Destroy(gameObject);
    }

    private Image CreateMask(Color color, float initialAlpha, int sortingOrder)
    {
        var go = new GameObject("转场遮罩", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster), typeof(Image));
        go.transform.SetParent(transform, false);

        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        var image = go.GetComponent<Image>();
        image.color = new Color(color.r, color.g, color.b, initialAlpha);
        image.raycastTarget = true; // 转场期间挡住鼠标点击

        // 铺满全屏，避免只盖住中间一小块
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        return image;
    }

    private IEnumerator FadeMask(Image mask, float from, float to, float duration)
    {
        float elapsed = 0f;
        Color baseColor = mask.color;

        while (elapsed < duration)
        {
            if (mask == null) yield break;

            elapsed += Time.unscaledDeltaTime; // 不受 timeScale 影响
            float t = Mathf.Clamp01(elapsed / duration);
            t = t * t * (3f - 2f * t);

            mask.color = new Color(baseColor.r, baseColor.g, baseColor.b, Mathf.Lerp(from, to, t));
            yield return null;
        }

        if (mask != null)
            mask.color = new Color(baseColor.r, baseColor.g, baseColor.b, to);
    }
}
