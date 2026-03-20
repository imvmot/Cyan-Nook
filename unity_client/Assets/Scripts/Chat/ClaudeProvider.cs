using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;

namespace CyanNook.Chat
{
    /// <summary>
    /// Anthropic Claude API 用プロバイダー（Messages API）
    /// endpoint: https://api.anthropic.com/v1/messages
    ///
    /// OpenAI APIとの主な違い:
    /// - 認証: x-api-key ヘッダー（Bearer形式ではない）
    /// - anthropic-version ヘッダー必須
    /// - system promptはトップレベルフィールド（messages配列外）
    /// - max_tokens 必須
    /// - SSEイベント形式が異なる（content_block_delta）
    /// - frequency_penalty/num_ctx 非対応、top_k 対応
    /// </summary>
    public class ClaudeProvider : ILLMProvider
    {
        private const string AnthropicVersion = "2023-06-01";
        private const int DefaultMaxTokens = 4096;

        public bool SupportsStreaming => true;

        public IEnumerator SendRequest(LLMConfig config, string systemPrompt, string userMessage,
            Action<string> onSuccess, Action<string> onError,
            string imageBase64 = null, Action<string> onRequestBody = null)
        {
            string jsonBody = BuildRequestJson(config, systemPrompt, userMessage, false, imageBase64);
            onRequestBody?.Invoke(jsonBody);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

            Debug.Log($"[ClaudeProvider] Sending to {config.apiEndpoint}, model={config.modelName}");

            using (var request = new UnityWebRequest(config.apiEndpoint, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                SetHeaders(request, config.apiKey);
                request.timeout = (int)config.timeout;

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    string responseText = request.downloadHandler.text;
                    try
                    {
                        var response = JsonUtility.FromJson<ClaudeMessageResponse>(responseText);

                        if (response.content == null || response.content.Length == 0 ||
                            string.IsNullOrEmpty(response.content[0].text))
                        {
                            onError?.Invoke("Empty response from Claude");
                        }
                        else
                        {
                            onSuccess?.Invoke(response.content[0].text.Trim());
                        }
                    }
                    catch (Exception e)
                    {
                        onError?.Invoke($"Failed to parse Claude response: {e.Message}");
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
            string imageBase64 = null, Action<string> onRequestBody = null,
            Action<string, string> onField = null,
            Action<string, string> onParseError = null)
        {
            string jsonBody = BuildRequestJson(config, systemPrompt, userMessage, true, imageBase64);
            onRequestBody?.Invoke(jsonBody);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

            Debug.Log($"[ClaudeProvider] Streaming request to {config.apiEndpoint}, model={config.modelName}");

            var streamHandler = new ClaudeSseStreamHandler(
                new byte[4096], onHeader, onTextChunk, onError, onField, onParseError);

            using (var request = new UnityWebRequest(config.apiEndpoint, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = streamHandler;
                SetHeaders(request, config.apiKey);
                request.timeout = (int)config.timeout;

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    // ストリーミングエラー時もレスポンスボディからエラー詳細を取得
                    string errorDetail = "";
                    try { errorDetail = request.downloadHandler?.data != null ? Encoding.UTF8.GetString(request.downloadHandler.data) : ""; }
                    catch { /* ignore */ }
                    Debug.LogError($"[ClaudeProvider] Streaming error {request.responseCode}: {request.error}\n{errorDetail}");
                    onError?.Invoke($"Streaming request failed: {request.error}");
                }
            }

            onComplete?.Invoke();
        }

        public IEnumerator TestConnection(LLMConfig config, Action<bool, string> callback)
        {
            // Claude APIにはモデル一覧エンドポイントがないため、
            // 最小限のリクエストを送信して接続確認
            string jsonBody = "{\"model\":\"" + EscapeJsonString(config.modelName) +
                "\",\"max_tokens\":1,\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}]}";
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

            using (var request = new UnityWebRequest(config.apiEndpoint, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                SetHeaders(request, config.apiKey);
                request.timeout = (int)config.timeout;

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    callback?.Invoke(true, "Connection successful");
                }
                else
                {
                    string errorDetail = request.downloadHandler?.text ?? "";
                    if (request.responseCode == 401)
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
            // Claudeでは不使用
        }

        public void ClearConversation()
        {
            // Claudeは会話状態を持たないため何もしない
        }

        // ===================================================================
        // ヘッダー設定
        // ===================================================================

        private static void SetHeaders(UnityWebRequest request, string apiKey)
        {
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("anthropic-version", AnthropicVersion);
            if (!string.IsNullOrEmpty(apiKey))
            {
                request.SetRequestHeader("x-api-key", apiKey);
            }
        }

        // ===================================================================
        // リクエストJSON構築
        // ===================================================================

        /// <summary>
        /// Anthropic Messages API のリクエストJSONを構築
        /// systemはトップレベル、messagesにはuserメッセージのみ
        /// </summary>
        private static string BuildRequestJson(LLMConfig config, string systemPrompt,
            string userMessage, bool stream, string imageBase64 = null)
        {
            string escapedModel = EscapeJsonString(config.modelName);
            string escapedSystem = EscapeJsonString(systemPrompt);
            string escapedUser = EscapeJsonString(userMessage);
            string streamStr = stream ? "true" : "false";

            // max_tokens: Claude APIでは必須。numPredict <= 0 の場合はデフォルト値を使用
            int maxTokens = config.numPredict > 0 ? config.numPredict : DefaultMaxTokens;

            var sb = new StringBuilder();
            sb.Append($"{{\"model\":\"{escapedModel}\"");
            sb.Append($",\"max_tokens\":{maxTokens}");
            sb.Append($",\"stream\":{streamStr}");

            // system prompt（トップレベル）
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                sb.Append($",\"system\":\"{escapedSystem}\"");
            }

            // messages配列（userメッセージのみ）
            sb.Append(",\"messages\":[");

            if (!string.IsNullOrEmpty(imageBase64))
            {
                // Vision: content配列形式
                sb.Append("{\"role\":\"user\",\"content\":[");
                sb.Append($"{{\"type\":\"text\",\"text\":\"{escapedUser}\"}}");
                sb.Append($",{{\"type\":\"image\",\"source\":{{\"type\":\"base64\",\"media_type\":\"image/jpeg\",\"data\":\"{imageBase64}\"}}}}");
                sb.Append("]}");
            }
            else
            {
                sb.Append($"{{\"role\":\"user\",\"content\":\"{escapedUser}\"}}");
            }

            sb.Append("]");

            // 生成パラメータ
            // Claude APIではtemperatureとtop_pは排他的（同時指定するとエラー）
            // temperatureを優先し、top_pはtemperatureがデフォルト(1.0)の場合のみ送信
            float clampedTemp = Mathf.Clamp(config.temperature, 0f, 1f); // Claude: 0.0-1.0
            sb.Append($",\"temperature\":{clampedTemp.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

            // top_k: Claude APIでは対応（OpenAIでは非対応）
            if (config.topK > 0)
            {
                sb.Append($",\"top_k\":{config.topK}");
            }

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
    // Claude SSE ストリーミングハンドラ
    // ===================================================================

    /// <summary>
    /// Anthropic Claude SSE ストリーミング用DownloadHandler
    /// Claude固有のSSEイベント形式をパースし、
    /// content_block_deltaイベントからテキストを抽出してStreamSeparatorProcessorに渡す
    ///
    /// Claude SSEフォーマット:
    ///   event: message_start
    ///   data: {"type":"message_start","message":{...}}
    ///
    ///   event: content_block_delta
    ///   data: {"type":"content_block_delta","delta":{"type":"text_delta","text":"token"}}
    ///
    ///   event: message_stop
    ///   data: {"type":"message_stop"}
    /// </summary>
    internal class ClaudeSseStreamHandler : DownloadHandlerScript
    {
        private readonly Decoder _utf8Decoder;
        private readonly StreamSeparatorProcessor _processor;
        private readonly StringBuilder _lineBuffer = new StringBuilder();

        public ClaudeSseStreamHandler(byte[] preallocatedBuffer,
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
        /// Claude SSEは "event:" 行と "data:" 行のペアで構成される
        /// テキスト抽出に必要なのは "data:" 行のみ
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
        /// content_block_delta イベントから delta.text を抽出
        /// </summary>
        private void ProcessSseData(string jsonData)
        {
            if (string.IsNullOrEmpty(jsonData)) return;

            try
            {
                // typeフィールドを簡易抽出して分岐
                // JsonUtility.FromJsonは型が一致しないフィールドを無視するため、
                // まず共通のtypeフィールドだけで判定
                var baseEvent = JsonUtility.FromJson<ClaudeSseEventBase>(jsonData);

                if (baseEvent.type == "content_block_delta")
                {
                    var deltaEvent = JsonUtility.FromJson<ClaudeContentBlockDelta>(jsonData);
                    if (deltaEvent.delta != null && !string.IsNullOrEmpty(deltaEvent.delta.text))
                    {
                        _processor.ProcessChunk(deltaEvent.delta.text);
                    }
                }
                // message_start, content_block_start, content_block_stop, message_delta, message_stop
                // は無視（テキスト抽出には不要）
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ClaudeSseStreamHandler] Failed to parse SSE data: {e.Message}\nData: {jsonData}");
            }
        }
    }

    // ===================================================================
    // Claude API レスポンスのデータ構造
    // ===================================================================

    /// <summary>
    /// Claude Messages API レスポンス（非ストリーミング）
    /// </summary>
    [Serializable]
    internal class ClaudeMessageResponse
    {
        public string id;
        public string type;
        public string model;
        public ClaudeContentBlock[] content;
    }

    [Serializable]
    internal class ClaudeContentBlock
    {
        public string type;
        public string text;
    }

    /// <summary>
    /// Claude SSEイベントの基底（type判定用）
    /// </summary>
    [Serializable]
    internal class ClaudeSseEventBase
    {
        public string type;
    }

    /// <summary>
    /// Claude SSE content_block_delta イベント
    /// </summary>
    [Serializable]
    internal class ClaudeContentBlockDelta
    {
        public string type;
        public ClaudeTextDelta delta;
    }

    [Serializable]
    internal class ClaudeTextDelta
    {
        public string type;
        public string text;
    }
}
