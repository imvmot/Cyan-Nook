using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using CyanNook.Core;

namespace CyanNook.Chat
{
    /// <summary>
    /// LLM通信の統合管理
    /// ILLMProviderを切り替えて複数のAPI（Ollama, Dify等）に対応
    /// ブロッキング方式とストリーミング方式の両方をサポート
    /// </summary>
    public class LLMClient : MonoBehaviour
    {
        private const string LocalhostBlockedMessage =
            "Cannot connect to localhost from this hosting environment.\n" +
            "Use WebLLM (in-browser AI) or a cloud API (OpenAI / Claude / Gemini).\n" +
            "To use a local LLM, download and run the app locally.\n\n" +
            "このホスティング環境からlocalhostには接続できません。\n" +
            "WebLLM（ブラウザAI）またはクラウドAPI（OpenAI / Claude / Gemini）をお使いください。\n" +
            "ローカルLLMを使用するには、アプリをダウンロードしてローカルで起動してください。";

        [Header("Connection Settings (Runtime)")]
        [Tooltip("LLM API エンドポイント（起動時にLLMConfigManagerから読み込み）")]
        [SerializeField]
        private string _apiEndpoint;
        public string ApiEndpoint => _apiEndpoint;

        [Tooltip("使用するモデル名")]
        [SerializeField]
        private string _modelName;
        public string ModelName => _modelName;

        [Tooltip("リクエストタイムアウト（秒）")]
        [SerializeField]
        private float _timeout;

        [Tooltip("Temperature (0.0-1.0)")]
        [SerializeField]
        private float _temperature;

        [Header("Status")]
        [SerializeField]
        private bool _isProcessing = false;
        public bool IsProcessing => _isProcessing;

        // 実行中のコルーチン参照（中断用）
        private Coroutine _currentCoroutine;

        [SerializeField]
        private string _currentApiType;

        // 現在の設定
        private LLMConfig _currentConfig;
        public LLMConfig CurrentConfig => _currentConfig;

        // 現在のプロバイダー
        private ILLMProvider _provider;

        // --- ブロッキング方式イベント ---
        public event Action<LLMResponseData> OnResponseReceived;
        public event Action<string> OnRawResponseReceived;
        /// <summary>送信リクエストボディ（デバッグ表示用）</summary>
        public event Action<string> OnRequestBodySent;
        public event Action<string> OnError;
        public event Action OnRequestStarted;
        public event Action OnRequestCompleted;
        public event Action<LLMConfig> OnConfigChanged;

        // --- ストリーミング方式イベント ---
        /// <summary>ヘッダー（メタデータ）受信時</summary>
        public event Action<LlmResponseHeader> OnStreamHeaderReceived;

        /// <summary>JSONフィールド逐次受信時（fieldName, rawJsonValue）</summary>
        public event Action<string, string> OnStreamFieldReceived;

        /// <summary>テキストチャンク受信時（逐次発火）</summary>
        public event Action<string> OnStreamTextReceived;

        /// <summary>ストリーミング完了時（最終LLMResponseDataを含む）</summary>
        public event Action<LLMResponseData> OnStreamCompleted;

        /// <summary>ストリーミング中のJSONパースエラー時（errorMessage, rawText）</summary>
        public event Action<string, string> OnStreamParseError;

        private void Awake()
        {
            LoadConfig();
        }

        /// <summary>
        /// 設定を読み込み
        /// </summary>
        public void LoadConfig()
        {
            _currentConfig = LLMConfigManager.Load();
            ApplyConfig(_currentConfig);
            Debug.Log($"[LLMClient] Config loaded: type={_currentConfig.apiType}, endpoint={_apiEndpoint}, model={_modelName}");
        }

        /// <summary>
        /// 設定を適用（プロバイダーも再生成）
        /// </summary>
        public void ApplyConfig(LLMConfig config)
        {
            if (config == null) return;

            // APIタイプが変わった場合はプロバイダーを再生成
            bool typeChanged = _currentConfig == null || _currentConfig.apiType != config.apiType;

            _currentConfig = config;
            _apiEndpoint = config.apiEndpoint;
            _modelName = config.modelName;
            _temperature = config.temperature;
            _timeout = config.timeout;
            _currentApiType = config.apiType.ToString();

            if (typeChanged || _provider == null)
            {
                _provider = CreateProvider(config.apiType);
                Debug.Log($"[LLMClient] Provider created: {config.apiType}");
            }

            OnConfigChanged?.Invoke(config);
        }

        /// <summary>
        /// 設定を保存して適用
        /// </summary>
        public void SaveAndApplyConfig(LLMConfig config)
        {
            LLMConfigManager.Save(config);
            ApplyConfig(config);
        }

        /// <summary>
        /// エンドポイントを更新（簡易メソッド）
        /// </summary>
        public void SetEndpoint(string endpoint)
        {
            if (_currentConfig == null)
            {
                _currentConfig = LLMConfig.GetDefault();
            }
            _currentConfig.apiEndpoint = endpoint;
            SaveAndApplyConfig(_currentConfig);
        }

        /// <summary>
        /// モデル名を更新（簡易メソッド）
        /// </summary>
        public void SetModelName(string modelName)
        {
            if (_currentConfig == null)
            {
                _currentConfig = LLMConfig.GetDefault();
            }
            _currentConfig.modelName = modelName;
            SaveAndApplyConfig(_currentConfig);
        }

        /// <summary>
        /// 現在のプロバイダーがストリーミングをサポートしているか
        /// </summary>
        public bool SupportsStreaming => _provider?.SupportsStreaming ?? false;

        // ===================================================================
        // ブロッキング方式（既存）
        // ===================================================================

        /// <summary>
        /// LLMにリクエストを送信（ブロッキング方式）
        /// </summary>
        /// <param name="systemPrompt">システムプロンプト</param>
        /// <param name="userMessage">ユーザーメッセージ</param>
        /// <param name="imageBase64">画像データ（base64エンコード、null=画像なし）</param>
        public void SendRequest(string systemPrompt, string userMessage, string imageBase64 = null)
        {
            if (_isProcessing)
            {
                Debug.LogWarning("[LLMClient] Already processing a request");
                return;
            }

            if (_provider == null)
            {
                Debug.LogError("[LLMClient] No provider available");
                OnError?.Invoke("No LLM provider configured");
                return;
            }

            _currentCoroutine = StartCoroutine(SendRequestCoroutine(systemPrompt, userMessage, imageBase64));
        }

        private IEnumerator SendRequestCoroutine(string systemPrompt, string userMessage, string imageBase64 = null)
        {
            _isProcessing = true;
            OnRequestStarted?.Invoke();

            Debug.Log($"[LLMClient] Sending request via {_currentConfig.apiType}, hasImage={imageBase64 != null}");

            yield return _provider.SendRequest(_currentConfig, systemPrompt, userMessage,
                onSuccess: (llmOutput) =>
                {
                    ProcessLLMOutput(llmOutput);
                },
                onError: (error) =>
                {
                    if (IsRemoteOriginWithLocalEndpoint(_currentConfig))
                    {
                        error = LocalhostBlockedMessage;
                    }
                    Debug.LogError($"[LLMClient] {error}");
                    OnError?.Invoke(error);
                },
                imageBase64: imageBase64,
                onRequestBody: (body) => { OnRequestBodySent?.Invoke(body); }
            );

            _isProcessing = false;
            _currentCoroutine = null;
            OnRequestCompleted?.Invoke();
        }

        /// <summary>
        /// プロバイダーから受け取ったLLM出力テキストをLLMResponseDataにパース
        /// </summary>
        private void ProcessLLMOutput(string llmOutput)
        {
            OnRawResponseReceived?.Invoke(llmOutput);

            try
            {
                // JSONブロックを抽出（```json...``` で囲まれている場合）
                string json = ExtractJsonFromResponse(llmOutput);

                var responseData = LLMResponseData.FromJson(json);

                if (responseData.Validate())
                {
                    Debug.Log($"[LLMClient] Response: {responseData.message}");
                    OnResponseReceived?.Invoke(responseData);
                }
                else
                {
                    Debug.LogWarning($"[LLMClient] Invalid response, using fallback. Raw: {llmOutput}");
                    OnResponseReceived?.Invoke(LLMResponseData.GetFallback());
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[LLMClient] Failed to process response: {e.Message}");
                OnError?.Invoke(e.Message);
            }
        }

        // ===================================================================
        // ストリーミング方式（新規）
        // ===================================================================

        /// <summary>
        /// LLMにストリーミングリクエストを送信
        /// ヘッダー受信後にテキストが逐次流れてくる
        /// </summary>
        /// <param name="systemPrompt">システムプロンプト</param>
        /// <param name="userMessage">ユーザーメッセージ</param>
        /// <param name="imageBase64">画像データ（base64エンコード、null=画像なし）</param>
        public void SendStreamingRequest(string systemPrompt, string userMessage, string imageBase64 = null)
        {
            if (_isProcessing)
            {
                Debug.LogWarning("[LLMClient] Already processing a request");
                return;
            }

            if (_provider == null)
            {
                Debug.LogError("[LLMClient] No provider available");
                OnError?.Invoke("No LLM provider configured");
                return;
            }

            if (!_provider.SupportsStreaming)
            {
                Debug.LogWarning("[LLMClient] Provider does not support streaming, falling back to blocking");
                SendRequest(systemPrompt, userMessage, imageBase64);
                return;
            }

            _currentCoroutine = StartCoroutine(SendStreamingRequestCoroutine(systemPrompt, userMessage, imageBase64));
        }

        private IEnumerator SendStreamingRequestCoroutine(string systemPrompt, string userMessage, string imageBase64 = null)
        {
            _isProcessing = true;
            OnRequestStarted?.Invoke();

            Debug.Log($"[LLMClient] Streaming request via {_currentConfig.apiType}, hasImage={imageBase64 != null}");

            LlmResponseHeader receivedHeader = null;
            var messageBuilder = new System.Text.StringBuilder();
            bool hasError = false;

            yield return _provider.SendStreamingRequest(_currentConfig, systemPrompt, userMessage,
                onHeader: (header) =>
                {
                    receivedHeader = header;
                    Debug.Log($"[LLMClient] Stream header received: action={header.action}, emote={header.emote}");
                    OnStreamHeaderReceived?.Invoke(header);
                },
                onTextChunk: (text) =>
                {
                    messageBuilder.Append(text);
                    OnStreamTextReceived?.Invoke(text);
                },
                onComplete: () =>
                {
                    // ストリーム完了 → 最終的なLLMResponseDataを構築
                    string fullMessage = messageBuilder.ToString();
                    Debug.Log($"[LLMClient] Stream completed, message length={fullMessage.Length}");

                    var header = receivedHeader ?? LlmResponseHeader.GetFallback();
                    var responseData = header.ToResponseData(fullMessage);

                    // 生レスポンスイベント（デバッグ用）
                    OnRawResponseReceived?.Invoke(fullMessage);

                    // ストリーミング完了イベント
                    OnStreamCompleted?.Invoke(responseData);

                    // 既存のOnResponseReceivedも発火（ChatManagerの既存フローと互換）
                    OnResponseReceived?.Invoke(responseData);
                },
                onError: (error) =>
                {
                    hasError = true;
                    if (IsRemoteOriginWithLocalEndpoint(_currentConfig))
                    {
                        error = LocalhostBlockedMessage;
                    }
                    Debug.LogError($"[LLMClient] Stream error: {error}");
                    OnError?.Invoke(error);
                },
                imageBase64: imageBase64,
                onRequestBody: (body) => { OnRequestBodySent?.Invoke(body); },
                onField: (fieldName, rawValue) =>
                {
                    OnStreamFieldReceived?.Invoke(fieldName, rawValue);
                },
                onParseError: (error, rawText) =>
                {
                    Debug.Log($"[LLMClient] Stream parse error: {error}");
                    OnStreamParseError?.Invoke(error, rawText);
                }
            );

            _isProcessing = false;
            _currentCoroutine = null;
            OnRequestCompleted?.Invoke();
        }

        /// <summary>
        /// 実行中のリクエストを中断する
        /// idleChatリクエスト中にユーザー入力が来た場合等に使用
        /// </summary>
        public void AbortRequest()
        {
            if (_currentCoroutine != null)
            {
                StopCoroutine(_currentCoroutine);
                _currentCoroutine = null;
                Debug.Log("[LLMClient] Request aborted");
            }
            _isProcessing = false;
        }

        // ===================================================================
        // 共通
        // ===================================================================

        /// <summary>
        /// マークダウンコードブロックからJSONを抽出
        /// </summary>
        private string ExtractJsonFromResponse(string response)
        {
            // ```json ... ``` パターンを探す
            int jsonStart = response.IndexOf("```json");
            if (jsonStart >= 0)
            {
                jsonStart = response.IndexOf('\n', jsonStart) + 1;
                int jsonEnd = response.IndexOf("```", jsonStart);
                if (jsonEnd > jsonStart)
                {
                    return response.Substring(jsonStart, jsonEnd - jsonStart).Trim();
                }
            }

            // ``` ... ``` パターンを探す
            int codeStart = response.IndexOf("```");
            if (codeStart >= 0)
            {
                codeStart = response.IndexOf('\n', codeStart) + 1;
                int codeEnd = response.IndexOf("```", codeStart);
                if (codeEnd > codeStart)
                {
                    return response.Substring(codeStart, codeEnd - codeStart).Trim();
                }
            }

            // { で始まり } で終わる部分を探す
            int braceStart = response.IndexOf('{');
            int braceEnd = response.LastIndexOf('}');
            if (braceStart >= 0 && braceEnd > braceStart)
            {
                return response.Substring(braceStart, braceEnd - braceStart + 1);
            }

            return response;
        }

        /// <summary>
        /// 接続テスト
        /// </summary>
        public void TestConnection(Action<bool, string> callback)
        {
            if (_provider == null)
            {
                callback?.Invoke(false, "No provider configured");
                return;
            }

            // 外部ホスティング環境からlocalhostへの接続はブラウザにブロックされる
            if (IsRemoteOriginWithLocalEndpoint(_currentConfig))
            {
                callback?.Invoke(false, LocalhostBlockedMessage);
                return;
            }

            StartCoroutine(_provider.TestConnection(_currentConfig, callback));
        }

        /// <summary>
        /// 外部ホスティングからlocalhost APIへの接続を検出
        /// ブラウザのPrivate Network Access制限によりブロックされるケースを事前判定する
        /// </summary>
        private static bool IsRemoteOriginWithLocalEndpoint(LLMConfig config)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (config == null || string.IsNullOrEmpty(config.apiEndpoint)) return false;
            if (config.apiType == LLMApiType.WebLLM) return false;

            string endpoint = config.apiEndpoint.ToLower();
            bool isLocalEndpoint = endpoint.Contains("localhost") || endpoint.Contains("127.0.0.1");
            if (!isLocalEndpoint) return false;

            string origin = Application.absoluteURL;
            bool isLocalOrigin = string.IsNullOrEmpty(origin) ||
                                 origin.Contains("localhost") ||
                                 origin.Contains("127.0.0.1") ||
                                 origin.StartsWith("file://");
            return !isLocalOrigin;
#else
            return false;
#endif
        }

        /// <summary>
        /// プロバイダーに動的入力変数を設定（Difyのinputsフィールド用）
        /// </summary>
        public void SetRequestInputs(Dictionary<string, string> inputs)
        {
            _provider?.SetInputs(inputs);
        }

        /// <summary>
        /// 会話状態をクリア（Difyのconversation_id等）
        /// </summary>
        public void ClearConversation()
        {
            _provider?.ClearConversation();
        }

        /// <summary>
        /// APIタイプに応じたプロバイダーを生成
        /// </summary>
        private static ILLMProvider CreateProvider(LLMApiType apiType)
        {
            switch (apiType)
            {
                case LLMApiType.Ollama:
                    return new OllamaProvider();

                case LLMApiType.LMStudio:
                    return new LMStudioProvider();

                case LLMApiType.Dify:
                    return new DifyProvider();

                case LLMApiType.OpenAI:
                    return new OpenAIProvider();

                case LLMApiType.Claude:
                    return new ClaudeProvider();

                case LLMApiType.Gemini:
                    return new GeminiProvider();

                case LLMApiType.WebLLM:
                    return new WebLLMProvider();

                default:
                    return new OllamaProvider();
            }
        }
    }
}
