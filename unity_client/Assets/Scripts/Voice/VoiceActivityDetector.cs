using UnityEngine;
using UnityEngine.Events;

namespace CyanNook.Voice
{
    /// <summary>
    /// Voice Activity Detection: 無音検出して自動送信
    /// Web Speech API版は音量情報がないため、結果更新頻度で判定
    /// </summary>
    public class VoiceActivityDetector : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("この秒数無音（更新なし）が続いたら送信")]
        public float silenceThreshold = 2f;

        [Header("Events")]
        public UnityEvent OnSilenceDetected;

        private float _lastUpdateTime;
        private bool _hasActivity = false;
        private string _accumulatedText = "";

        private void Update()
        {
            if (!_hasActivity) return;

            // 最後の更新からの経過時間
            float timeSinceLastUpdate = Time.time - _lastUpdateTime;

            if (timeSinceLastUpdate >= silenceThreshold)
            {
                // N秒無音 → 送信
                Debug.Log($"[VAD] Silence detected ({timeSinceLastUpdate:F1}s)");
                OnSilenceDetected?.Invoke();
                _hasActivity = false;
            }
        }

        /// <summary>
        /// 部分結果を受信（活動中マーク）
        /// </summary>
        public void OnPartialResult(string text)
        {
            _lastUpdateTime = Time.time;
            _hasActivity = true;
            _accumulatedText = text;  // 部分結果は上書き
        }

        /// <summary>
        /// 確定結果を受信
        /// </summary>
        public void OnFinalResult(string text)
        {
            _lastUpdateTime = Time.time;
            _hasActivity = true;
            _accumulatedText += text + " ";  // 確定結果は追加
        }

        /// <summary>
        /// 蓄積テキスト取得＆クリア
        /// </summary>
        public string GetAndClearText()
        {
            string result = _accumulatedText.Trim();
            _accumulatedText = "";
            return result;
        }

        /// <summary>
        /// 手動リセット
        /// </summary>
        public void Reset()
        {
            _hasActivity = false;
            _accumulatedText = "";
        }
    }
}
