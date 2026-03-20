using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace CyanNook.Chat
{
    /// <summary>
    /// WebLLM JavaScript ブリッジ
    /// WebLLM.jslib の DllImport 宣言と SendMessage コールバック受信を担当。
    /// シーンに配置する MonoBehaviour。WebLLMProvider がイベントを購読して使用する。
    /// </summary>
    public class WebLLMBridge : MonoBehaviour
    {
        // --- Singleton ---
        public static WebLLMBridge Instance { get; private set; }

        // --- DllImport ---
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern int WebLLM_IsWebGPUSupported();

        [DllImport("__Internal")]
        private static extern void WebLLM_Initialize(string callbackObjectName, string jsonSchema);

        [DllImport("__Internal")]
        private static extern void WebLLM_LoadModel(string modelId);

        [DllImport("__Internal")]
        private static extern int WebLLM_IsModelLoaded();

        [DllImport("__Internal")]
        private static extern int WebLLM_IsLoading();

        [DllImport("__Internal")]
        private static extern void WebLLM_SetGenerationParams(float temperature, float topP, int maxTokens, float repeatPenalty);

        [DllImport("__Internal")]
        private static extern void WebLLM_SendRequest(string systemPrompt, string userMessage);

        [DllImport("__Internal")]
        private static extern void WebLLM_SendStreamingRequest(string systemPrompt, string userMessage);

        [DllImport("__Internal")]
        private static extern void WebLLM_Abort();

        [DllImport("__Internal")]
        private static extern void WebLLM_Unload();
#endif

        // --- イベント ---
        /// <summary>モデルDL進捗（loadedBytes, totalBytes, progress, text）</summary>
        public event Action<long, long, float, string> OnLoadProgress;

        /// <summary>モデルロード完了</summary>
        public event Action OnModelLoaded;

        /// <summary>非ストリーミングレスポンス受信</summary>
        public event Action<string> OnResponse;

        /// <summary>ストリーミングチャンク受信</summary>
        public event Action<string> OnStreamChunk;

        /// <summary>ストリーミング完了</summary>
        public event Action OnStreamComplete;

        /// <summary>エラー発生</summary>
        public event Action<string> OnError;

        // --- 状態 ---
        private bool _initialized;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        // ─────────────────────────────────────
        // Public API（C#側から呼び出す）
        // ─────────────────────────────────────

        /// <summary>
        /// WebGPUがサポートされているか
        /// </summary>
        public bool IsWebGPUSupported()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return WebLLM_IsWebGPUSupported() == 1;
#else
            return false;
#endif
        }

        /// <summary>
        /// 初期化（コールバック先の登録 + JSONスキーマ設定）
        /// </summary>
        public void Initialize(string jsonSchema = "")
        {
            if (_initialized) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            WebLLM_Initialize(gameObject.name, jsonSchema);
            _initialized = true;
            Debug.Log("[WebLLMBridge] Initialized");
#else
            Debug.LogWarning("[WebLLMBridge] WebLLM is only available in WebGL builds");
#endif
        }

        /// <summary>
        /// モデルのダウンロード＆ロードを開始
        /// </summary>
        public void LoadModel(string modelId)
        {
            if (!_initialized) Initialize();
#if UNITY_WEBGL && !UNITY_EDITOR
            WebLLM_LoadModel(modelId);
#endif
        }

        /// <summary>
        /// モデルがロード済みか
        /// </summary>
        public bool IsModelLoaded
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return WebLLM_IsModelLoaded() == 1;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// モデルロード中か
        /// </summary>
        public bool IsLoading
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return WebLLM_IsLoading() == 1;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// 生成パラメータを設定（リクエスト前に呼び出す）
        /// </summary>
        public void SetGenerationParams(float temperature, float topP, int maxTokens, float repeatPenalty)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            WebLLM_SetGenerationParams(temperature, topP, maxTokens, repeatPenalty);
#endif
        }

        /// <summary>
        /// 非ストリーミングリクエスト送信
        /// </summary>
        public void SendChatRequest(string systemPrompt, string userMessage)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            WebLLM_SendRequest(systemPrompt, userMessage);
#endif
        }

        /// <summary>
        /// ストリーミングリクエスト送信
        /// </summary>
        public void SendStreamingChatRequest(string systemPrompt, string userMessage)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            WebLLM_SendStreamingRequest(systemPrompt, userMessage);
#endif
        }

        /// <summary>
        /// 生成を中断
        /// </summary>
        public void AbortGeneration()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            WebLLM_Abort();
#endif
        }

        /// <summary>
        /// モデルをアンロード
        /// </summary>
        public void UnloadModel()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            WebLLM_Unload();
#endif
        }

        // ─────────────────────────────────────
        // SendMessage コールバック（jslib → Unity）
        // ─────────────────────────────────────

        /// <summary>
        /// モデルDL進捗通知
        /// </summary>
        private void OnWebLLMLoadProgress(string json)
        {
            try
            {
                var data = JsonUtility.FromJson<LoadProgressData>(json);
                OnLoadProgress?.Invoke(data.loaded, data.total, data.progress, data.text);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[WebLLMBridge] Failed to parse progress: {e.Message}");
            }
        }

        /// <summary>
        /// モデルロード完了通知
        /// </summary>
        private void OnWebLLMModelLoaded(string status)
        {
            Debug.Log($"[WebLLMBridge] Model loaded: {status}");
            OnModelLoaded?.Invoke();
        }

        /// <summary>
        /// 非ストリーミングレスポンス受信
        /// </summary>
        private void OnWebLLMResponse(string responseText)
        {
            OnResponse?.Invoke(responseText);
        }

        /// <summary>
        /// ストリーミングチャンク受信
        /// </summary>
        private void OnWebLLMStreamChunk(string chunk)
        {
            OnStreamChunk?.Invoke(chunk);
        }

        /// <summary>
        /// ストリーミング完了
        /// </summary>
        private void OnWebLLMStreamComplete(string _)
        {
            OnStreamComplete?.Invoke();
        }

        /// <summary>
        /// エラー通知
        /// </summary>
        private void OnWebLLMError(string error)
        {
            Debug.LogError($"[WebLLMBridge] Error: {error}");
            OnError?.Invoke(error);
        }

        // ─────────────────────────────────────
        // 内部データクラス
        // ─────────────────────────────────────

        [Serializable]
        private class LoadProgressData
        {
            public long loaded;
            public long total;
            public float progress;
            public string text;
        }
    }
}
