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
            Application.targetFrameRate = targetFrameRate;
        }
    }
}
