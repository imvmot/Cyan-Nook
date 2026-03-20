using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Events;

namespace CyanNook.Voice
{
    /// <summary>
    /// Web Speech APIのC#ラッパー
    /// WebGLビルドでのみ動作
    /// </summary>
    public class WebSpeechRecognition : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("認識言語（ja-JP, en-US等）")]
        public string language = "ja-JP";

        [Tooltip("継続的認識（trueで自動再起動）")]
        public bool continuous = true;

        [Tooltip("部分結果を取得（話している途中の結果）")]
        public bool interimResults = true;

        [Header("Events")]
        public UnityEvent OnRecognitionStartedEvent;
        public UnityEvent<string> OnPartialResultEvent;
        public UnityEvent<string> OnFinalResultEvent;
        public UnityEvent<string> OnRecognitionErrorEvent;
        public UnityEvent OnRecognitionEndedEvent;

        private bool _isInitialized = false;
        private bool _isRecognizing = false;
        private bool _shouldAutoRestart = true;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern bool WebSpeech_Initialize(string callbackObjectName, string language, bool continuous, bool interimResults);

        [DllImport("__Internal")]
        private static extern bool WebSpeech_Start();

        [DllImport("__Internal")]
        private static extern bool WebSpeech_Stop();

        [DllImport("__Internal")]
        private static extern bool WebSpeech_Abort();

        [DllImport("__Internal")]
        private static extern bool WebSpeech_SetLanguage(string language);

        [DllImport("__Internal")]
        private static extern bool WebSpeech_IsSupported();
#endif

        /// <summary>
        /// 初期化
        /// </summary>
        public bool Initialize()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (_isInitialized)
            {
                Debug.LogWarning("[WebSpeechRecognition] Already initialized");
                return true;
            }

            if (!WebSpeech_IsSupported())
            {
                Debug.LogError("[WebSpeechRecognition] Web Speech API not supported in this browser");
                return false;
            }

            _isInitialized = WebSpeech_Initialize(gameObject.name, language, continuous, interimResults);

            if (_isInitialized)
            {
                Debug.Log($"[WebSpeechRecognition] Initialized (language: {language})");
            }

            return _isInitialized;
#else
            // エディタでは動作しない（WebGL専用機能）
            return false;
#endif
        }

        /// <summary>
        /// 認識開始
        /// </summary>
        public bool StartRecognition()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (!_isInitialized)
            {
                Debug.LogWarning("[WebSpeechRecognition] Not initialized");
                return false;
            }

            if (_isRecognizing)
            {
                Debug.LogWarning("[WebSpeechRecognition] Already recognizing");
                return true;
            }

            _shouldAutoRestart = true;

            bool success = WebSpeech_Start();
            if (success)
            {
                _isRecognizing = true;
                Debug.Log("[WebSpeechRecognition] Recognition started");
            }
            return success;
#else
            // エディタでは動作しない（WebGL専用機能）
            return false;
#endif
        }

        /// <summary>
        /// 認識停止
        /// </summary>
        public bool StopRecognition()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            _shouldAutoRestart = false;

            if (!_isRecognizing)
            {
                return true;
            }

            // abort()で即座に中断（stop()は処理中の音声を最後まで処理してしまう）
            bool success = WebSpeech_Abort();
            if (success)
            {
                _isRecognizing = false;
                Debug.Log("[WebSpeechRecognition] Recognition aborted");
            }
            return success;
#else
            return false;
#endif
        }

        /// <summary>
        /// 言語変更
        /// </summary>
        public bool SetLanguage(string newLanguage)
        {
            language = newLanguage;

#if UNITY_WEBGL && !UNITY_EDITOR
            if (_isInitialized)
            {
                return WebSpeech_SetLanguage(language);
            }
#endif
            return true;
        }

        public bool IsRecognizing => _isRecognizing;

        // =====================================================================
        // JavaScript側からのコールバック（SendMessageで呼ばれる）
        // =====================================================================

        private void OnRecognitionStarted(string _)
        {
            _isRecognizing = true;
            OnRecognitionStartedEvent?.Invoke();
        }

        private void OnPartialResult(string transcript)
        {
            Debug.Log($"[WebSpeechRecognition] Partial: {transcript}");
            OnPartialResultEvent?.Invoke(transcript);
        }

        private void OnFinalResult(string transcript)
        {
            Debug.Log($"[WebSpeechRecognition] Final: {transcript}");
            OnFinalResultEvent?.Invoke(transcript);
        }

        private void OnRecognitionError(string errorMessage)
        {
            Debug.LogError($"[WebSpeechRecognition] Error: {errorMessage}");
            _isRecognizing = false;
            OnRecognitionErrorEvent?.Invoke(errorMessage);
        }

        private void OnRecognitionEnded(string _)
        {
            _isRecognizing = false;
            OnRecognitionEndedEvent?.Invoke();

            // continuous=trueかつ意図的な停止でない場合、自動再起動
            if (continuous && _isInitialized && _shouldAutoRestart)
            {
                Debug.Log("[WebSpeechRecognition] Auto-restarting recognition");
                StartRecognition();
            }
        }
    }
}
