using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;

namespace CyanNook.Chat
{
    /// <summary>
    /// OpenAI API 用プロバイダー（Chat Completions API）
    /// endpoint: https://api.openai.com/v1/chat/completions（または互換サービスのURL）
    ///
    /// LMStudioProviderと同じOpenAI Chat Completions API形式だが、
    /// Authorization: Bearer ヘッダーによるAPI Key認証が必須。
    /// </summary>
    public class OpenAIProvider : ILLMProvider
    {
        public bool SupportsStreaming => true;

        public IEnumerator SendRequest(LLMConfig config, string systemPrompt, string userMessage,
            Action<string> onSuccess, Action<string> onError,
            string imageBase64 = null, Action<string> onRequestBody = null)
        {
            string jsonBody = BuildRequestJson(config, systemPrompt, userMessage, false, imageBase64);
            onRequestBody?.Invoke(jsonBody);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

            Debug.Log($"[OpenAIProvider] Sending to {config.apiEndpoint}, model={config.modelName}");

            using (var request = new UnityWebRequest(config.apiEndpoint, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                SetAuthHeader(request, config.apiKey);
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
                            onError?.Invoke("Empty response from OpenAI");
                        }
                        else
                        {
                            onSuccess?.Invoke(response.choices[0].message.content.Trim());
                        }
                    }
                    catch (Exception e)
                    {
                        onError?.Invoke($"Failed to parse OpenAI response: {e.Message}");
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

            Debug.Log($"[OpenAIProvider] Streaming request to {config.apiEndpoint}, model={config.modelName}");

            // OpenAI SSEストリーミング（LMStudioと同じフォーマット）
            var streamHandler = new LMStudioSseStreamHandler(
                new byte[4096], onHeader, onTextChunk, onError, onField, onParseError);

            using (var request = new UnityWebRequest(config.apiEndpoint, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = streamHandler;
                request.SetRequestHeader("Content-Type", "application/json");
                SetAuthHeader(request, config.apiKey);
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
                SetAuthHeader(request, config.apiKey);
                request.timeout = (int)config.timeout;
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    callback?.Invoke(true, "Connection successful");
                }
                else
                {
                    string errorDetail = request.downloadHandler?.text ?? "";
                    // API Key関連のエラーをわかりやすく表示
                    if (request.responseCode == 401)
                    {
                        callback?.Invoke(false, "Authentication failed (invalid API Key)");
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
            // OpenAIでは不使用
        }

        public void ClearConversation()
        {
            // OpenAIは会話状態を持たないため何もしない
        }

        // ===================================================================
        // 認証ヘッダー
        // ===================================================================

        /// <summary>
        /// Authorization: Bearer ヘッダーを設定
        /// </summary>
        private static void SetAuthHeader(UnityWebRequest request, string apiKey)
        {
            if (!string.IsNullOrEmpty(apiKey))
            {
                request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
            }
        }

        // ===================================================================
        // リクエストJSON構築
        // ===================================================================

        /// <summary>
        /// OpenAI Chat Completions API のリクエストJSONを構築
        /// messages配列形式、画像はOpenAI Vision形式で埋め込み
        /// </summary>
        private static string BuildRequestJson(LLMConfig config, string systemPrompt,
            string userMessage, bool stream, string imageBase64 = null)
        {
            string escapedModel = EscapeJsonString(config.modelName);
            string escapedSystem = EscapeJsonString(systemPrompt);
            string escapedUser = EscapeJsonString(userMessage);
            string streamStr = stream ? "true" : "false";

            var sb = new StringBuilder();
            sb.Append($"{{\"model\":\"{escapedModel}\"");
            sb.Append($",\"stream\":{streamStr}");

            // messages配列
            sb.Append(",\"messages\":[");

            // systemメッセージ
            sb.Append($"{{\"role\":\"system\",\"content\":\"{escapedSystem}\"}}");

            // userメッセージ（画像がある場合はcontent配列形式）
            if (!string.IsNullOrEmpty(imageBase64))
            {
                sb.Append(",{\"role\":\"user\",\"content\":[");
                sb.Append($"{{\"type\":\"text\",\"text\":\"{escapedUser}\"}}");
                sb.Append($",{{\"type\":\"image_url\",\"image_url\":{{\"url\":\"data:image/jpeg;base64,{imageBase64}\"}}}}");
                sb.Append("]}");
            }
            else
            {
                sb.Append($",{{\"role\":\"user\",\"content\":\"{escapedUser}\"}}");
            }

            sb.Append("]");

            // 生成パラメータ
            sb.Append($",\"temperature\":{config.temperature.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            sb.Append($",\"top_p\":{config.topP.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

            // max_tokens: numPredict > 0 の場合のみ送信
            if (config.numPredict > 0)
            {
                sb.Append($",\"max_tokens\":{config.numPredict}");
            }

            // repeat_penalty → frequency_penalty として送信（近似マッピング）
            if (config.repeatPenalty > 0f && config.repeatPenalty != 1.0f)
            {
                float freqPenalty = config.repeatPenalty - 1.0f;
                sb.Append($",\"frequency_penalty\":{freqPenalty.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
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
}
