using UnityEngine;
using TMPro;
using System.Runtime.InteropServices;
using CyanNook.Character;

namespace CyanNook.UI
{
    /// <summary>
    /// 常時表示のステータスオーバーレイ
    /// FPS、キャラクターステート、再生中タイムライン、JSヒープメモリを表示
    /// </summary>
    public class StatusOverlay : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private GameObject statusPanel;

        [Header("References")]
        [SerializeField] private CyanNook.Character.CharacterController characterController;
        [SerializeField] private CharacterAnimationController animationController;

        [Header("Settings")]
        [Tooltip("表示更新間隔（秒）")]
        [SerializeField] private float updateInterval = 0.5f;

        private float _timer;
        private float _fps;
        private int _frameCount;
        private float _fpsTimer;

        private string _errorMessage;
        private float _errorTimer;

        // WebGL JS heap memory
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern float GetJSHeapUsedMB();

        [DllImport("__Internal")]
        private static extern float GetJSHeapTotalMB();
#else
        private static float GetJSHeapUsedMB() => 0f;
        private static float GetJSHeapTotalMB() => 0f;
#endif

        [Header("Error Display")]
        [Tooltip("エラー表示の持続時間（秒）")]
        [SerializeField] private float errorDisplayDuration = 10f;

        public bool IsVisible => statusPanel != null && statusPanel.activeSelf;

        public void SetVisible(bool visible)
        {
            if (statusPanel != null)
                statusPanel.SetActive(visible);
        }

        /// <summary>
        /// エラーメッセージを表示（一定時間後に自動消去）
        /// </summary>
        public void ShowError(string message)
        {
            _errorMessage = message;
            _errorTimer = errorDisplayDuration;
        }

        private void Update()
        {
            if (statusPanel == null || !statusPanel.activeSelf) return;

            // FPS計算（フレームカウント方式で安定化）
            _frameCount++;
            _fpsTimer += Time.unscaledDeltaTime;
            if (_fpsTimer >= updateInterval)
            {
                _fps = _frameCount / _fpsTimer;
                _frameCount = 0;
                _fpsTimer = 0f;
            }

            // エラータイマー
            if (_errorTimer > 0f)
            {
                _errorTimer -= Time.unscaledDeltaTime;
                if (_errorTimer <= 0f)
                    _errorMessage = null;
            }

            // 表示更新（updateInterval間隔）
            _timer += Time.unscaledDeltaTime;
            if (_timer < updateInterval) return;
            _timer = 0f;

            UpdateStatusText();
        }

        private void UpdateStatusText()
        {
            if (statusText == null) return;

            // FPS
            string fpsColor = _fps >= 55f ? "#88FF88" : _fps >= 30f ? "#FFFF88" : "#FF8888";
            string fpsStr = $"<color={fpsColor}>{_fps:F0}</color> FPS";

            // Character State
            string charState = "---";
            if (characterController != null)
                charState = characterController.CurrentState.ToString();

            // Animation / Timeline
            string animState = "---";
            string timelineName = "---";
            if (animationController != null)
            {
                animState = animationController.CurrentState.ToString();
                if (animationController.director != null && animationController.director.playableAsset != null)
                    timelineName = animationController.director.playableAsset.name;
            }

            // Memory
            string memStr;
#if UNITY_WEBGL && !UNITY_EDITOR
            float usedMB = GetJSHeapUsedMB();
            float totalMB = GetJSHeapTotalMB();
            if (totalMB > 0f)
                memStr = $"{usedMB:F0} / {totalMB:F0} MB";
            else
                memStr = "N/A";
#else
            // Editor/Standalone: Unityのマネージドメモリ
            long totalMem = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
            memStr = $"{totalMem / (1024 * 1024)} MB";
#endif

            string line = $"{fpsStr}  |  {charState}  |  {animState}: {timelineName}  |  Heap: {memStr}";

            if (!string.IsNullOrEmpty(_errorMessage))
            {
                line += $"\n<color=#FF6666>{_errorMessage}</color>";
            }

            statusText.text = line;
        }
    }
}
