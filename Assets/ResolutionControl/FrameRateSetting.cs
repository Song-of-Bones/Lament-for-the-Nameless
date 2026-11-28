using UnityEngine;

/// <summary>
/// 全局帧率：移动端默认跟随 VSync（常见 30/60），这里统一请求 60 fps。
/// QualitySettings.vSyncCount 必须为 0，否则 Application.targetFrameRate 会被忽略。
/// </summary>
public static class FrameRateSetting
{
    public const int TargetFrameRate = 60;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Apply()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = TargetFrameRate;
    }
}
