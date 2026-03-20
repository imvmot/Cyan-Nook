using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Events;

namespace CyanNook.Voice
{
    /// <summary>
    /// Web Speech Synthesis API (TTS) のC#ラッパー
    /// WebGLビルドでのみ動作
    /// </summary>
    public class WebSpeechSynthesis : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("選択した音声のURI")]
        public string voiceURI = "";

        [Tooltip("話速 (0.5-3.0)")]
        [Range(0.5f, 3.0f)]
        public float rate = 1.0f;

        [Tooltip("ピッチ (0.0-2.0)")]
        [Range(0.0f, 2.0f)]
        public float pitch = 1.0f;

        [Header("Events")]
        public UnityEvent OnSpeechStartedEvent;
        public UnityEvent OnSpeechEndedEvent;
        public UnityEvent<string> OnSpeechErrorEvent;
        public UnityEvent<string> OnVoicesLoadedEvent;
        public UnityEvent OnQueueEmptyEvent;

        // PlayerPrefs キー
        private const string PrefKey_VoiceURI = "voice_webSpeechVoiceURI";
        private const string PrefKey_Rate = "voice_webSpeechRate";
        private const string PrefKey_Pitch = "voice_webSpeechPitch";

        private bool _isInitialized = false;
        private bool _isSpeaking = false;
        private List<WebSpeechVoice> _availableVoices = new List<WebSpeechVoice>();

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern bool WebSpeechSynth_Initialize(string callbackObjectName);

        [DllImport("__Internal")]
        private static extern bool WebSpeechSynth_IsSupported();

        [DllImport("__Internal")]
        private static extern string WebSpeechSynth_GetVoices();

        [DllImport("__Internal")]
        private static extern void WebSpeechSynth_Speak(string text, string voiceURI, float rate, float pitch);

        [DllImport("__Internal")]
        private static extern void WebSpeechSynth_Enqueue(string text, string voiceURI, float rate, float pitch);

        [DllImport("__Internal")]
        private static extern void WebSpeechSynth_Cancel();

        [DllImport("__Internal")]
        private static extern bool WebSpeechSynth_IsSpeaking();
#endif

        /// <summary>
        /// 初期化
        /// </summary>
        public bool Initialize()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (_isInitialized)
            {
                Debug.LogWarning("[WebSpeechSynthesis] Already initialized");
                return true;
            }

            if (!WebSpeechSynth_IsSupported())
            {
                Debug.LogError("[WebSpeechSynthesis] Web Speech Synthesis API not supported in this browser");
                return false;
            }

            _isInitialized = WebSpeechSynth_Initialize(gameObject.name);

            if (_isInitialized)
            {
                Debug.Log("[WebSpeechSynthesis] Initialized");
            }

            return _isInitialized;
#else
            Debug.Log("[WebSpeechSynthesis] Not available in Editor (WebGL build only)");
            return false;
#endif
        }

        /// <summary>
        /// ブラウザ対応チェック
        /// </summary>
        public bool IsSupported()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return WebSpeechSynth_IsSupported();
#else
            return false;
#endif
        }

        /// <summary>
        /// 即時発話（テスト用、キュークリア）
        /// </summary>
        public void Speak(string text)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (!_isInitialized)
            {
                Debug.LogWarning("[WebSpeechSynthesis] Not initialized");
                return;
            }

            if (string.IsNullOrEmpty(text)) return;

            WebSpeechSynth_Speak(text, voiceURI ?? "", rate, pitch);
            Debug.Log($"[WebSpeechSynthesis] Speak: {text.Substring(0, Math.Min(20, text.Length))}...");
#else
            Debug.Log($"[WebSpeechSynthesis] Speak (Editor stub): {text}");
#endif
        }

        /// <summary>
        /// キューに追加（ストリーミング用、順次再生）
        /// </summary>
        public void Enqueue(string text)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (!_isInitialized)
            {
                Debug.LogWarning("[WebSpeechSynthesis] Not initialized");
                return;
            }

            if (string.IsNullOrEmpty(text)) return;

            WebSpeechSynth_Enqueue(text, voiceURI ?? "", rate, pitch);
            Debug.Log($"[WebSpeechSynthesis] Enqueue: {text.Substring(0, Math.Min(20, text.Length))}...");
#else
            Debug.Log($"[WebSpeechSynthesis] Enqueue (Editor stub): {text}");
#endif
        }

        /// <summary>
        /// 発話中止+キュークリア
        /// </summary>
        public void Cancel()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (!_isInitialized) return;

            WebSpeechSynth_Cancel();
            _isSpeaking = false;
            Debug.Log("[WebSpeechSynthesis] Cancelled");
#endif
        }

        /// <summary>
        /// 発話中か
        /// </summary>
        public bool IsSpeaking => _isSpeaking;

        /// <summary>
        /// 利用可能な音声リスト（日本語のみ）
        /// </summary>
        public List<WebSpeechVoice> AvailableVoices => _availableVoices;

        // ─────────────────────────────────────
        // 設定の読み書き
        // ─────────────────────────────────────

        /// <summary>
        /// PlayerPrefsから設定を読み込み
        /// </summary>
        public void LoadSettings()
        {
            if (PlayerPrefs.HasKey(PrefKey_VoiceURI))
                voiceURI = PlayerPrefs.GetString(PrefKey_VoiceURI);
            if (PlayerPrefs.HasKey(PrefKey_Rate))
                rate = PlayerPrefs.GetFloat(PrefKey_Rate);
            if (PlayerPrefs.HasKey(PrefKey_Pitch))
                pitch = PlayerPrefs.GetFloat(PrefKey_Pitch);

            Debug.Log($"[WebSpeechSynthesis] Settings loaded - Voice: {voiceURI}, Rate: {rate}, Pitch: {pitch}");
        }

        /// <summary>
        /// PlayerPrefsに設定を保存
        /// </summary>
        public void SaveSettings()
        {
            PlayerPrefs.SetString(PrefKey_VoiceURI, voiceURI ?? "");
            PlayerPrefs.SetFloat(PrefKey_Rate, rate);
            PlayerPrefs.SetFloat(PrefKey_Pitch, pitch);
            PlayerPrefs.Save();

            Debug.Log($"[WebSpeechSynthesis] Settings saved - Voice: {voiceURI}, Rate: {rate}, Pitch: {pitch}");
        }

        // =====================================================================
        // JavaScript側からのコールバック（SendMessageで呼ばれる）
        // =====================================================================

        private void OnSpeechStarted(string _)
        {
            _isSpeaking = true;
            OnSpeechStartedEvent?.Invoke();
        }

        private void OnSpeechEnded(string _)
        {
            _isSpeaking = false;
            OnSpeechEndedEvent?.Invoke();
        }

        private void OnSpeechError(string errorMessage)
        {
            _isSpeaking = false;
            Debug.LogError($"[WebSpeechSynthesis] Error: {errorMessage}");
            OnSpeechErrorEvent?.Invoke(errorMessage);
        }

        private void OnVoicesLoaded(string voicesJson)
        {
            try
            {
                var wrapper = JsonUtility.FromJson<WebSpeechVoiceListWrapper>($"{{\"voices\":{voicesJson}}}");
                _availableVoices = wrapper?.voices ?? new List<WebSpeechVoice>();
                Debug.Log($"[WebSpeechSynthesis] Voices loaded: {_availableVoices.Count} Japanese voices");
            }
            catch (Exception e)
            {
                Debug.LogError($"[WebSpeechSynthesis] Failed to parse voices: {e.Message}");
                _availableVoices = new List<WebSpeechVoice>();
            }

            OnVoicesLoadedEvent?.Invoke(voicesJson);
        }

        private void OnQueueEmpty(string _)
        {
            _isSpeaking = false;
            OnQueueEmptyEvent?.Invoke();
        }
    }

    // ─────────────────────────────────────
    // データクラス
    // ─────────────────────────────────────

    [Serializable]
    public class WebSpeechVoice
    {
        public string name;
        public string lang;
        public string voiceURI;
        public bool isDefault;
    }

    [Serializable]
    public class WebSpeechVoiceListWrapper
    {
        public List<WebSpeechVoice> voices;
    }
}
