using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;

namespace CyanNook.Chat
{
    /// <summary>
    /// Google Gemini API 用プロバイダー（Generative Language API）
    /// endpoint（ベースURL）: https://generativelanguage.googleapis.com/v1beta
    ///
    /// OpenAI/Claudeとの主な違い:
    /// - エンドポイントURLにモデル名を含む（models/{model}:generateContent）
    /// - ストリーミングは別エンドポイント（:streamGenerateContent?alt=sse）
    /// - 認証: x-goog-api-key ヘッダー
    /// - メッセージ形式: contents配列 + parts配列（role: "user"/"model"）
    /// - systemはsystemInstructionトップレベルフィールド
    /// - パラメータはgenerationConfigオブジェクト内
    /// - maxOutputTokens（max_tokens相当）、topP/topK対応
    /// - repeatPenalty/numCtx/think 非対応
    /// </summary>
    public class GeminiProvider : ILLMProvider
    {
        private const int DefaultMaxOutputTokens = 4096;

        public bool SupportsStreaming => true;

        public IEnumerator SendRequest(LLMConfig config, string systemPrompt, string userMessage,
            Action<string> onSuccess, Action<string> onError,
            List<string> imagesBase64 = null, Action<string> onRequestBody = null)
        {
            string url = BuildEndpointUrl(config, false);
            string jsonBody = BuildRequestJson(config, systemPrompt, userMessage, imagesBase64);
            onRequestBody?.Invoke(jsonBody);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

            Debug.Log($"[GeminiProvider] Sending to {url}, model={config.modelName}");

            using (var request = new UnityWebRequest(url, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                SetHeaders(request, config.ResolveApiKey());
                request.timeout = (int)config.timeout;

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    string responseText = request.downloadHandler.text;
                    try
                    {
                        var response = JsonUtility.FromJson<GeminiResponse>(responseText);

                        if (response.candidates == null || response.candidates.Length == 0 ||
                            response.candidates[0].content == null ||
                            response.candidates[0].content.parts == null ||
                            response.candidates[0].content.parts.Length == 0 ||
                            string.IsNullOrEmpty(response.candidates[0].content.parts[0].text))
                        {
                            onError?.Invoke("Empty response from Gemini");
                        }
                        else
                        {
                            onSuccess?.Invoke(response.candidates[0].content.parts[0].text.Trim());
                        }
                    }
                    catch (Exception e)
                    {
                        onError?.Invoke($"Failed to parse Gemini response: {e.Message}");
                    }
                }
                else
                {
                    string errorDetail = request.downloadHandler?.text ?? "";
                    onError?.Invoke($"Request failed: {request.error} {errorDetail}");
                }
            }
        }

        public IEnumerator SendStreamingRequest(LLMConfig config, string systemPrompt, string userMessage,
            Action<LlmResponseHeader> onHeader, Action<string> onTextChunk,
            Action onComplete, Action<string> onError,
            List<string> imagesBase64 = null, Action<string> onRequestBody = null,
            Action<string, string> onField = null,
            Action<string, string> onParseError = null)
        {
            string url = BuildEndpointUrl(config, true);
            string jsonBody = BuildRequestJson(config, systemPrompt, userMessage, imagesBase64);
            onRequestBody?.Invoke(jsonBody);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

            Debug.Log($"[GeminiProvider] Streaming request to {url}, model={config.modelName}");

            var streamHandler = new GeminiSseStreamHandler(
                new byte[4096], onHeader, onTextChunk, onError, onField, onParseError);

            using (var request = new UnityWebRequest(url, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = streamHandler;
                SetHeaders(request, config.ResolveApiKey());
                request.timeout = (int)config.timeout;

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string errorDetail = "";
                    try { errorDetail = request.downloadHandler?.data != null ? Encoding.UTF8.GetString(request.downloadHandler.data) : ""; }
                    catch { /* ignore */ }
                    Debug.LogError($"[GeminiProvider] Streaming error {request.responseCode}: {request.error}\n{errorDetail}");
                    onError?.Invoke($"Streaming request failed: {request.error}");
                }
            }

            onComplete?.Invoke();
        }

        public IEnumerator TestConnection(LLMConfig config, Action<bool, string> callback)
        {
            // モデル情報取得APIで接続確認
            string baseUrl = config.apiEndpoint.TrimEnd('/');
            string testUrl = $"{baseUrl}/models/{Uri.EscapeDataString(config.modelName)}";

            using (var request = UnityWebRequest.Get(testUrl))
            {
                SetHeaders(request, config.ResolveApiKey());
                request.timeout = (int)config.timeout;

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    callback?.Invoke(true, "Connection successful");
                }
                else
                {
                    string errorDetail = request.downloadHandler?.text ?? "";
                    if (request.responseCode == 400 || request.responseCode == 403)
                    {
                        callback?.Invoke(false, "Authentication failed (invalid API Key)");
                    }
                    else if (request.responseCode == 404)
                    {
                        callback?.Invoke(false, $"Model not found: {config.modelName}");
                    }
                    else
                    {
                        callback?.Invoke(false, $"{request.error} {errorDetail}");
                    }
                }
            }
        }

        public void SetInputs(Dictionary<string, string> inputs)
        {
            // Geminiでは不使用
        }

        public void ClearConversation()
        {
            // Geminiは会話状態を持たないため何もしない
        }

        // ===================================================================
        // エンドポイントURL構築
        // ===================================================================

        /// <summary>
        /// ベースURL + モデル名 + アクションからエンドポイントURLを構築
        /// 非ストリーミング: {base}/models/{model}:generateContent
        /// ストリーミング:   {base}/models/{model}:streamGenerateContent?alt=sse
        /// </summary>
        private static string BuildEndpointUrl(LLMConfig config, bool stream)
        {
            string baseUrl = config.apiEndpoint.TrimEnd('/');
            string model = Uri.EscapeDataString(config.modelName);

            if (stream)
            {
                return $"{baseUrl}/models/{model}:streamGenerateContent?alt=sse";
            }
            else
            {
                return $"{baseUrl}/models/{model}:generateContent";
            }
        }

        // ===================================================================
        // ヘッダー設定
        // ===================================================================

        private static void SetHeaders(UnityWebRequest request, string apiKey)
        {
            request.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrEmpty(apiKey))
            {
                request.SetRequestHeader("x-goog-api-key", apiKey);
            }
        }

        // ===================================================================
        // リクエストJSON構築
        // ===================================================================

        /// <summary>
        /// Gemini Generative Language API のリクエストJSONを構築
        /// contents: parts配列形式、systemInstruction: トップレベル
        /// generationConfig: パラメータオブジェクト
        /// </summary>
        private static string BuildRequestJson(LLMConfig config, string systemPrompt,
            string userMessage, List<string> imagesBase64 = null)
        {
            string escapedSystem = EscapeJsonString(systemPrompt);
            string escapedUser = EscapeJsonString(userMessage);

            // maxOutputTokens: numPredict <= 0 の場合はデフォルト値を使用
            int maxOutputTokens = config.numPredict > 0 ? config.numPredict : DefaultMaxOutputTokens;

            var sb = new StringBuilder();
            sb.Append("{");

            // contents配列（userメッセージ）
            sb.Append("\"contents\":[{\"role\":\"user\",\"parts\":[");

            // テキストパート
            sb.Append($"{{\"text\":\"{escapedUser}\"}}");

            // 画像パート（あれば、複数画像対応）
            if (imagesBase64 != null)
            {
                foreach (var img in imagesBase64)
                {
                    sb.Append($",{{\"inline_data\":{{\"mime_type\":\"image/jpeg\",\"data\":\"{img}\"}}}}");
                }
            }

            sb.Append("]}]");

            // systemInstruction（トップレベル）
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                sb.Append($",\"systemInstruction\":{{\"parts\":{{\"text\":\"{escapedSystem}\"}}}}");
            }

            // generationConfig
            sb.Append(",\"generationConfig\":{");
            sb.Append($"\"temperature\":{config.temperature.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            sb.Append($",\"topP\":{config.topP.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

            if (config.topK > 0)
            {
                sb.Append($",\"topK\":{config.topK}");
            }

            sb.Append($",\"maxOutputTokens\":{maxOutputTokens}");
            sb.Append("}");

            sb.Append("}");
            return sb.ToString();
        }

        private static string EscapeJsonString(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }
    }

    // ===================================================================
    // Gemini SSE ストリーミングハンドラ
    // ===================================================================

    /// <summary>
    /// Google Gemini SSE ストリーミング用DownloadHandler
    /// SSEフォーマット:
    ///   data: {"candidates":[{"content":{"parts":[{"text":"token"}],"role":"model"}}],...}
    ///
    /// テキスト抽出: candidates[0].content.parts[0].text
    /// 終了判定: finishReasonフィールド（"STOP"等）の存在
    /// </summary>
    internal class GeminiSseStreamHandler : DownloadHandlerScript
    {
        private readonly Decoder _utf8Decoder;
        private readonly StreamSeparatorProcessor _processor;
        private readonly StringBuilder _lineBuffer = new StringBuilder();

        public GeminiSseStreamHandler(byte[] preallocatedBuffer,
            Action<LlmResponseHeader> onHeader, Action<string> onTextChunk,
            Action<string> onError, Action<string, string> onField = null,
            Action<string, string> onParseError = null) : base(preallocatedBuffer)
        {
            _utf8Decoder = Encoding.UTF8.GetDecoder();
            _processor = new StreamSeparatorProcessor
            {
                OnHeaderReceived = onHeader,
                OnTextReceived = onTextChunk,
                OnError = onError,
                OnFieldParsed = onField,
                OnParseError = onParseError
            };
        }

        protected override bool ReceiveData(byte[] data, int dataLength)
        {
            if (data == null || dataLength < 1) return false;

            int charCount = _utf8Decoder.GetCharCount(data, 0, dataLength, false);
            if (charCount == 0) return true;

            char[] chars = new char[charCount];
            _utf8Decoder.GetChars(data, 0, dataLength, chars, 0, false);
            string chunk = new string(chars);

            _lineBuffer.Append(chunk);
            ProcessSseLines();

            return true;
        }

        protected override void CompleteContent()
        {
            // 残りをフラッシュ
            int charCount = _utf8Decoder.GetCharCount(new byte[0], 0, 0, true);
            if (charCount > 0)
            {
                char[] chars = new char[charCount];
                _utf8Decoder.GetChars(new byte[0], 0, 0, chars, 0, true);
                _lineBuffer.Append(new string(chars));
                ProcessSseLines();
            }

            _processor.Complete();
        }

        /// <summary>
        /// SSEイベント行を処理
        /// Gemini SSEは "data:" 行のみ（event:行なし）
        /// </summary>
        private void ProcessSseLines()
        {
            string content = _lineBuffer.ToString();
            int lastNewline = content.LastIndexOf('\n');

            if (lastNewline < 0) return;

            string completedPart = content.Substring(0, lastNewline);
            string remaining = content.Substring(lastNewline + 1);

            _lineBuffer.Clear();
            _lineBuffer.Append(remaining);

            string[] lines = completedPart.Split('\n');
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("data:", StringComparison.Ordinal))
                {
                    string jsonData = trimmed.Substring(5).Trim();
                    ProcessSseData(jsonData);
                }
            }
        }

        /// <summary>
        /// SSEのdataフィールド（JSON）を処理
        /// candidates[0].content.parts[0].text を抽出
        /// </summary>
        private void ProcessSseData(string jsonData)
        {
            if (string.IsNullOrEmpty(jsonData)) return;

            try
            {
                var chunk = JsonUtility.FromJson<GeminiResponse>(jsonData);
                if (chunk.candidates != null && chunk.candidates.Length > 0 &&
                    chunk.candidates[0].content != null &&
                    chunk.candidates[0].content.parts != null &&
                    chunk.candidates[0].content.parts.Length > 0 &&
                    !string.IsNullOrEmpty(chunk.candidates[0].content.parts[0].text))
                {
                    _processor.ProcessChunk(chunk.candidates[0].content.parts[0].text);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GeminiSseStreamHandler] Failed to parse SSE data: {e.Message}\nData: {jsonData}");
            }
        }
    }

    // ===================================================================
    // Gemini API レスポンスのデータ構造
    // ===================================================================

    /// <summary>
    /// Gemini GenerateContent レスポンス（非ストリーミング・ストリーミング共通）
    /// </summary>
    [Serializable]
    internal class GeminiResponse
    {
        public GeminiCandidate[] candidates;
    }

    [Serializable]
    internal class GeminiCandidate
    {
        public GeminiContent content;
        public string finishReason;
    }

    [Serializable]
    internal class GeminiContent
    {
        public GeminiPart[] parts;
        public string role;
    }

    [Serializable]
    internal class GeminiPart
    {
        public string text;
    }
}
