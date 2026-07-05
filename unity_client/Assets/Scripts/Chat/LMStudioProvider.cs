using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using CyanNook.Core;

namespace CyanNook.Chat
{
    /// <summary>
    /// LM Studio 用プロバイダー（OpenAI互換 Chat Completions API）
    /// endpoint: LM Studio APIの完全URL（例: http://localhost:1234/v1/chat/completions）
    ///
    /// Ollamaとの主な違い:
    /// - messagesフォーマット（role/content配列）
    /// - SSE形式ストリーミング（NDJSONではない）
    /// - パラメータ名が異なる（max_tokens, frequency_penalty等）
    /// - think/num_ctx/top_kは非対応（無視される）
    /// </summary>
    public class LMStudioProvider : ILLMProvider
    {
        public bool SupportsStreaming => true;

        public IEnumerator SendRequest(LLMConfig config, string systemPrompt, string userMessage,
            Action<string> onSuccess, Action<string> onError,
            List<string> imagesBase64 = null, Action<string> onRequestBody = null)
        {
            string jsonBody = BuildRequestJson(config, systemPrompt, userMessage, false, imagesBase64);
            onRequestBody?.Invoke(jsonBody);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

            Debug.Log($"[LMStudioProvider] Sending to {config.apiEndpoint}, model={config.modelName}");

            using (var request = new UnityWebRequest(config.apiEndpoint, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = (int)config.timeout;

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    string responseText = request.downloadHandler.text;
                    try
                    {
                        var response = JsonUtility.FromJson<OpenAICompletionResponse>(responseText);

                        if (response.choices == null || response.choices.Length == 0 ||
                            response.choices[0].message == null ||
                            string.IsNullOrEmpty(response.choices[0].message.content))
                        {
                            onError?.Invoke("Empty response from LM Studio");
                        }
                        else
                        {
                            onSuccess?.Invoke(response.choices[0].message.content.Trim());
                        }
                    }
                    catch (Exception e)
                    {
                        onError?.Invoke($"Failed to parse LM Studio response: {e.Message}");
                    }
                }
                else
                {
                    onError?.Invoke($"Request failed: {request.error}");
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
            string jsonBody = BuildRequestJson(config, systemPrompt, userMessage, true, imagesBase64);
            onRequestBody?.Invoke(jsonBody);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

            Debug.Log($"[LMStudioProvider] Streaming request to {config.apiEndpoint}, model={config.modelName}");

            var streamHandler = new LMStudioSseStreamHandler(
                new byte[4096], onHeader, onTextChunk, onError, onField, onParseError);

            using (var request = new UnityWebRequest(config.apiEndpoint, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = streamHandler;
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = (int)config.timeout;

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke($"Streaming request failed: {request.error}");
                }
            }

            onComplete?.Invoke();
        }

        public IEnumerator TestConnection(LLMConfig config, Action<bool, string> callback)
        {
            // エンドポイントURLから /v1/models APIのURLを構築
            string baseUrl = config.apiEndpoint;
            int v1Index = baseUrl.IndexOf("/v1/", StringComparison.OrdinalIgnoreCase);
            string testUrl;
            if (v1Index >= 0)
            {
                testUrl = baseUrl.Substring(0, v1Index) + "/v1/models";
            }
            else
            {
                var uri = new Uri(baseUrl);
                testUrl = $"{uri.Scheme}://{uri.Authority}/v1/models";
            }

            using (var request = UnityWebRequest.Get(testUrl))
            {
                request.timeout = (int)config.timeout;
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    callback?.Invoke(true, "Connection successful");
                }
                else
                {
                    callback?.Invoke(false, request.error);
                }
            }
        }

        public void SetInputs(Dictionary<string, string> inputs)
        {
            // LM Studioでは不使用
        }

        public void ClearConversation()
        {
            // LM Studioは会話状態を持たないため何もしない
        }

        // ===================================================================
        // リクエストJSON構築
        // ===================================================================

        /// <summary>
        /// OpenAI互換 Chat Completions API のリクエストJSONを構築
        /// messages配列形式、画像はOpenAI Vision形式で埋め込み
        /// </summary>
        private static string BuildRequestJson(LLMConfig config, string systemPrompt,
            string userMessage, bool stream, List<string> imagesBase64 = null)
        {
            string escapedModel = JsonEscape.Escape(config.modelName);
            string escapedSystem = JsonEscape.Escape(systemPrompt);
            string escapedUser = JsonEscape.Escape(userMessage);
            string streamStr = stream ? "true" : "false";

            var sb = new StringBuilder();
            sb.Append($"{{\"model\":\"{escapedModel}\"");
            sb.Append($",\"stream\":{streamStr}");

            // messages配列
            sb.Append(",\"messages\":[");

            // systemメッセージ
            sb.Append($"{{\"role\":\"system\",\"content\":\"{escapedSystem}\"}}");

            // userメッセージ（画像がある場合はcontent配列形式、複数画像対応）
            if (imagesBase64 != null && imagesBase64.Count > 0)
            {
                sb.Append(",{\"role\":\"user\",\"content\":[");
                sb.Append($"{{\"type\":\"text\",\"text\":\"{escapedUser}\"}}");
                foreach (var img in imagesBase64)
                {
                    sb.Append($",{{\"type\":\"image_url\",\"image_url\":{{\"url\":\"data:image/jpeg;base64,{img}\"}}}}");
                }
                sb.Append("]}");
            }
            else
            {
                sb.Append($",{{\"role\":\"user\",\"content\":\"{escapedUser}\"}}");
            }

            sb.Append("]");

            // 生成パラメータ（OpenAI互換）
            sb.Append($",\"temperature\":{config.temperature.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            sb.Append($",\"top_p\":{config.topP.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

            // max_tokens: numPredict > 0 の場合のみ送信（-1=無制限はOpenAI形式では省略）
            if (config.numPredict > 0)
            {
                sb.Append($",\"max_tokens\":{config.numPredict}");
            }

            // repeat_penalty → frequency_penalty として送信（近似マッピング）
            // repeat_penalty 1.0 = 無効 → frequency_penalty 0.0
            if (config.repeatPenalty > 0f && config.repeatPenalty != 1.0f)
            {
                float freqPenalty = config.repeatPenalty - 1.0f;
                sb.Append($",\"frequency_penalty\":{freqPenalty.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            }

            sb.Append("}");
            return sb.ToString();
        }

    }

    // ===================================================================
    // LM Studio SSE ストリーミングハンドラ
    // ===================================================================

    /// <summary>
    /// LM Studio SSE ストリーミング用DownloadHandler
    /// OpenAI互換のSSEストリーミングレスポンスをパースし、
    /// choices[0].delta.contentを抽出してStreamSeparatorProcessorに渡す
    ///
    /// SSEフォーマット:
    ///   data: {"choices":[{"delta":{"content":"token"}}]}
    ///   data: [DONE]
    /// </summary>
    internal class LMStudioSseStreamHandler : SseStreamHandlerBase
    {
        public LMStudioSseStreamHandler(byte[] preallocatedBuffer,
            Action<LlmResponseHeader> onHeader, Action<string> onTextChunk,
            Action<string> onError, Action<string, string> onField = null,
            Action<string, string> onParseError = null)
            : base(preallocatedBuffer, onHeader, onTextChunk, onError, onField, onParseError)
        {
        }

        /// <summary>
        /// SSEのdataフィールド（JSON）を処理
        /// choices[0].delta.content を抽出してStreamSeparatorProcessorに渡す
        /// </summary>
        protected override void ProcessSseData(string jsonData)
        {
            if (jsonData == "[DONE]") return;

            var chunk = JsonUtility.FromJson<OpenAIStreamChunk>(jsonData);
            if (chunk.choices != null && chunk.choices.Length > 0 &&
                chunk.choices[0].delta != null &&
                !string.IsNullOrEmpty(chunk.choices[0].delta.content))
            {
                Processor.ProcessChunk(chunk.choices[0].delta.content);
            }
        }
    }

    // ===================================================================
    // OpenAI互換レスポンスのデータ構造
    // ===================================================================

    /// <summary>
    /// OpenAI Chat Completions API レスポンス（非ストリーミング）
    /// </summary>
    [Serializable]
    internal class OpenAICompletionResponse
    {
        public OpenAIChoice[] choices;
    }

    [Serializable]
    internal class OpenAIChoice
    {
        public OpenAIMessage message;
    }

    [Serializable]
    internal class OpenAIMessage
    {
        public string role;
        public string content;
    }

    /// <summary>
    /// OpenAI Chat Completions API ストリーミングチャンク
    /// </summary>
    [Serializable]
    internal class OpenAIStreamChunk
    {
        public OpenAIStreamChoice[] choices;
    }

    [Serializable]
    internal class OpenAIStreamChoice
    {
        public OpenAIDelta delta;
    }

    [Serializable]
    internal class OpenAIDelta
    {
        public string content;
    }
}
