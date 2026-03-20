using System;
using UnityEngine;
using CyanNook.UI;

namespace CyanNook.Voice
{
    /// <summary>
    /// 音声入力全体の統合コントローラー
    /// WebSpeechRecognitionとUIを接続
    /// </summary>
    public class VoiceInputController : MonoBehaviour
    {
        [Header("References")]
        public WebSpeechRecognition speechRecognition;
        public VoiceActivityDetector activityDetector;
        public UIController uiController;

        [Header("Settings")]
        [Tooltip("認識言語")]
        public string language = "ja-JP";

        /// <summary>
        /// マイクON/OFF状態が変更されたときに発火（UIの同期用）
        /// </summary>
        public event Action<bool> OnEnabledChanged;

        private bool _isEnabled = false;
        private bool _isSuppressedByTTS = false;

        private void Start()
        {
            // WebSpeechRecognition初期化
            if (speechRecognition == null)
            {
                Debug.LogError("[VoiceInputController] WebSpeechRecognition not assigned");
                return;
            }

            speechRecognition.language = language;
            bool initialized = speechRecognition.Initialize();

            if (!initialized)
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                Debug.LogError("[VoiceInputController] Failed to initialize speech recognition");
#else
                Debug.Log("[VoiceInputController] Voice input not available in Editor (WebGL build only)");
#endif
                return;
            }

            // イベント接続
            speechRecognition.OnPartialResultEvent.AddListener(OnPartialTranscription);
            speechRecognition.OnFinalResultEvent.AddListener(OnFinalTranscription);
            speechRecognition.OnRecognitionErrorEvent.AddListener(OnRecognitionError);

            if (activityDetector != null)
            {
                activityDetector.OnSilenceDetected.AddListener(OnSilenceDetected);
            }
        }

        /// <summary>
        /// マイクON/OFF（VoiceSettingsPanelから呼ばれる）
        /// </summary>
        public void SetEnabled(bool enabled)
        {
            _isEnabled = enabled;

            _isSuppressedByTTS = false;

            if (enabled)
            {
                speechRecognition.StartRecognition();
                Debug.Log("[VoiceInputController] Voice input enabled");
            }
            else
            {
                speechRecognition.StopRecognition();
                activityDetector?.Reset();

                if (uiController != null)
                {
                    uiController.chatInputField.text = "";
                }

                Debug.Log("[VoiceInputController] Voice input disabled");
            }

            OnEnabledChanged?.Invoke(_isEnabled);
        }

        /// <summary>
        /// 言語変更（VoiceSettingsPanelから呼ばれる）
        /// </summary>
        public void SetLanguage(string newLanguage)
        {
            language = newLanguage;
            speechRecognition.SetLanguage(language);
        }

        /// <summary>
        /// 無音閾値変更（VoiceSettingsPanelから呼ばれる）
        /// </summary>
        public void SetSilenceThreshold(float threshold)
        {
            if (activityDetector != null)
            {
                activityDetector.silenceThreshold = threshold;
            }
        }

        private void OnPartialTranscription(string text)
        {
            if (!_isEnabled || _isSuppressedByTTS) return;

            // 部分結果をリアルタイムで入力フィールドに表示
            if (uiController != null)
            {
                uiController.chatInputField.text = text;
            }

            // VADに通知
            activityDetector?.OnPartialResult(text);
        }

        private void OnFinalTranscription(string text)
        {
            if (!_isEnabled || _isSuppressedByTTS) return;

            Debug.Log($"[VoiceInputController] Final: {text}");

            // VADに通知
            activityDetector?.OnFinalResult(text);
        }

        private void OnRecognitionError(string errorMessage)
        {
            Debug.LogWarning($"[VoiceInputController] Recognition error: {errorMessage}");

            // "no-speech" エラーは無視（無音が続いた場合）
            if (errorMessage == "no-speech")
            {
                return;
            }

            // その他のエラーは一旦停止
            if (errorMessage == "not-allowed" || errorMessage == "service-not-allowed")
            {
                SetEnabled(false);
                Debug.LogError("[VoiceInputController] Microphone permission denied");
            }
        }

        private void OnSilenceDetected()
        {
            if (!_isEnabled || _isSuppressedByTTS || activityDetector == null || uiController == null)
            {
                return;
            }

            // N秒無音 → 自動送信
            string finalText = activityDetector.GetAndClearText();

            if (!string.IsNullOrEmpty(finalText))
            {
                Debug.Log($"[VoiceInputController] Auto-sending: {finalText}");

                // 音声入力から送信
                uiController.SendMessageFromVoice(finalText);
            }
        }

        public bool IsEnabled => _isEnabled;

        /// <summary>
        /// TTS再生中のエコー防止用一時停止
        /// SetEnabled(false)と異なり、入力フィールドやVAD状態はクリアしない
        /// </summary>
        public void SuppressForTTS()
        {
            if (!_isEnabled || _isSuppressedByTTS) return;

            _isSuppressedByTTS = true;
            speechRecognition.StopRecognition();
            Debug.Log("[VoiceInputController] STT suppressed for TTS playback");
        }

        /// <summary>
        /// TTS再生完了後にSTTを再開
        /// </summary>
        public void ResumeFromTTS()
        {
            if (!_isEnabled || !_isSuppressedByTTS) return;

            _isSuppressedByTTS = false;
            activityDetector?.Reset();
            speechRecognition.StartRecognition();
            Debug.Log("[VoiceInputController] STT resumed after TTS playback");
        }
    }
}
