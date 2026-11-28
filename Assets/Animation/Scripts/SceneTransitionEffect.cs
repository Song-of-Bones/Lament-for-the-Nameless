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

    [Tooltip("开始后多少秒才允许跳过，避免开场误触打断帧动画")]
    [SerializeField, Min(0f)] private float 跳过需等待 = 1.5f;

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
    [Tooltip("交给场景流程管理器：只把「流程 ID」发出去，跳哪个场景由 WindowDatabase 的跳转列表决定")]
    [SerializeField] private bool 交给流程管理器 = true;

    [Tooltip("流程 ID，对应 WindowDatabase「跳转窗口配置」里的 id；场景连接图里拖线会自动维护")]
    [SerializeField] private string 流程ID = "";

    [Tooltip("未启用流程管理器时才会用到：动画播完后跳转到的场景（需已加入 Build Settings）")]
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

    [Tooltip("动画播放期间就在后台把目标场景加载好（不激活），播完直接切换；不改变动画表现")]
    [SerializeField] private bool 提前预加载 = true;

    [Tooltip("等待动画的兜底上限（秒），0 表示不限制；动画被冻结时到点强制转场")]
    [SerializeField, Min(0f)] private float 最多等待 = 0f;

    [Header("事件")]
    [SerializeField] private UnityEvent 开始播放;
    [SerializeField] private UnityEvent 播放结束;
    [SerializeField] private UnityEvent 加载场景前;

    private PlayableGraph _graph;
    private AnimationClipPlayable _clipPlayable;
    private bool _running;
    private bool _skipRequested;
    private AsyncOperation _preload;
    private float _startedAt;
    private SceneFlowManager _manager;

    private bool UseFlowManager => 交给流程管理器 && !string.IsNullOrEmpty(流程ID);
    private SceneFlowManager Manager => _manager != null ? _manager : (_manager = SceneFlowManager.Find());

    /// <summary>开始播放并等待转场；重复调用会被忽略。</summary>
    public void PlayNow()
    {
        if (_running) return;
        StartCoroutine(Run());
    }

    /// <summary>跳过当前等待，立即执行转场。</summary>
    public void Skip()
    {
        if (_running && 允许跳过 && CanSkipNow()) _skipRequested = true;
    }

    private bool CanSkipNow() => Time.unscaledTime - _startedAt >= 跳过需等待;

    private void Start()
    {
        if (自动开始) PlayNow();
    }

    private void Update()
    {
        if (!_running || !允许跳过 || _skipRequested || !CanSkipNow()) return;
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
        _startedAt = Time.unscaledTime;
        开始播放?.Invoke();

        // 动画和预加载互不干扰：目标场景只在后台准备好，等动画播完那一刻才激活。
        StartPreload();

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

    /// <summary>
    /// 帧动画是固定时长，所以用「时长」作主判定：与 Animator 同步起算，播到片尾就切。
    /// Animator 先播完或提前归零也立刻放行，动画对象被禁用时仍靠时长兜底，绝不停在登录页。
    /// </summary>
    private IEnumerator WaitAnimatorRoutine()
    {
        // 等一帧，确保 Animator 已进入默认状态
        yield return null;

        float duration = ResolveDuration();
        float elapsed = SyncOffset(duration);

        while (!_skipRequested)
        {
            elapsed += Time.deltaTime; // Animator 也吃 timeScale，两边同步

            if (duration > 0f && elapsed >= duration) yield break;
            if (AnimatorFinished()) yield break;

            if (最多等待 > 0f && elapsed >= 最多等待)
            {
                Debug.LogWarning($"[AnimSceneTransition] 等待动画超过 {最多等待:0.##}s，强制转场。", this);
                yield break;
            }

            yield return null;
        }
    }

    private float ResolveDuration()
    {
        if (等待目标 != null)
        {
            var state = 等待目标.GetCurrentAnimatorStateInfo(0);
            if (state.length > 0f) return state.length;
        }
        return 帧动画文件 != null ? 帧动画文件.length : 0f;
    }

    /// <summary>把计时起点对齐到动画已经播到的位置，避免协程晚启一帧就晚切一帧。</summary>
    private float SyncOffset(float duration)
    {
        if (等待目标 == null || duration <= 0f) return 0f;
        var state = 等待目标.GetCurrentAnimatorStateInfo(0);
        return Mathf.Clamp01(state.normalizedTime) * duration;
    }

    private bool AnimatorFinished()
    {
        if (等待目标 == null || !等待目标.isActiveAndEnabled) return false;
        var state = 等待目标.GetCurrentAnimatorStateInfo(0);
        return !等待目标.IsInTransition(0) && state.length > 0f && state.normalizedTime >= 0.999f;
    }

    private void StartPreload()
    {
        StartCoroutine(PreloadRoutine());
    }

    private IEnumerator PreloadRoutine()
    {
        // 先让开场几帧画出来，再开始后台加载，避免加载尖峰落在动画起手处。
        yield return WaitUnscaled(0.5f);

        if (UseFlowManager)
        {
            var manager = Manager;
            if (manager != null) manager.Preload(流程ID);
            else _preload = null;
            yield break;
        }

        if (!提前预加载 || string.IsNullOrEmpty(目标场景)) yield break;

        if (!Application.CanStreamedLevelBeLoaded(目标场景)) yield break;

        _preload = SceneManager.LoadSceneAsync(目标场景);
        if (_preload == null) yield break;

        // 只加载不激活：动画期间准备数据，播完那一刻再让场景出现。
        _preload.allowSceneActivation = false;
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

        // 交给管理器：脚本只发流程 ID，目标场景由 WindowDatabase 的跳转列表决定。
        if (UseFlowManager)
        {
            var manager = Manager;
            if (manager == null)
            {
                Debug.LogWarning(
                    $"[AnimSceneTransition] 场景里没有 SceneFlowManager，流程 ID「{流程ID}」无法执行。", this);
                return;
            }

            manager.Play(流程ID);
            return;
        }

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
            ActivatePreload();
            if (_preload == null)
            {
                if (异步加载) SceneManager.LoadSceneAsync(目标场景);
                else SceneManager.LoadScene(目标场景);
            }
            return;
        }

        var runner = new GameObject("__场景转场执行器");
        DontDestroyOnLoad(runner);
        runner.AddComponent<SceneTransitionRunner>().Begin(
            目标场景, 需要淡出, 需要淡入, 转场时长, 遮罩颜色, 遮罩层级, 异步加载, _preload);
    }

    /// <summary>激活动画期间预加载好的场景；没有预加载时什么也不做。</summary>
    private void ActivatePreload()
    {
        if (_preload == null) return;
        _preload.allowSceneActivation = true;
        _preload = null;
    }
}

/// <summary>
/// 转场执行器：负责全屏遮罩的渐变及场景加载。
/// </summary>
public class SceneTransitionRunner : MonoBehaviour
{
    public void Begin(string scene, bool fadeOut, bool fadeIn, float duration, Color color, int sortingOrder, bool async,
        AsyncOperation preload = null)
    {
        StartCoroutine(RunTransition(scene, fadeOut, fadeIn, duration, color, sortingOrder, async, preload));
    }

    private IEnumerator RunTransition(string scene, bool fadeOut, bool fadeIn, float duration, Color color, int sortingOrder, bool async,
        AsyncOperation preload = null)
    {
        Image mask = null;

        if (fadeOut || fadeIn)
        {
            // 纯淡入时初始就是全遮罩；否则从透明开始
            mask = CreateMask(color, fadeIn && !fadeOut ? color.a : 0f, sortingOrder);
        }

        if (fadeOut) yield return FadeMask(mask, 0f, color.a, duration);

        if (preload != null)
        {
            // 动画期间已经加载好，这里只等遮罩盖住再让场景出现
            preload.allowSceneActivation = true;
            while (!preload.isDone) yield return null;
        }
        else if (async)
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
