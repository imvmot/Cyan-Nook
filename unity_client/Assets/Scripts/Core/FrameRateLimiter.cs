using UnityEngine;

namespace CyanNook.Core
{
    /// <summary>
    /// フレームレート制限（Inspectorで設定可能）
    /// </summary>
    public class FrameRateLimiter : MonoBehaviour
    {
        [Tooltip("目標フレームレート（-1で無制限）")]
        [SerializeField] private int targetFrameRate = 60;

        private void Awake()
        {
            QualitySettings.vSyncCount = 0;

#if MOBILE_WEB_BUILD
            // モバイル向けビルドでは MobileWebBootstrap (シーンロード前) が設定した
            // 30FPS 上限をここで上書きしない（省電力・発熱抑制を優先）。
            // Inspector 値がそれより低い場合のみ尊重する
            Application.targetFrameRate = targetFrameRate > 0
                ? Mathf.Min(targetFrameRate, MobileWebBootstrap.TargetFrameRate)
                : MobileWebBootstrap.TargetFrameRate;
#else
            Application.targetFrameRate = targetFrameRate;
#endif
        }
    }
}
