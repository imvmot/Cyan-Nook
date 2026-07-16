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
            List<string> imagesBase64 = null, Action<string> onRequestBody = null)
        {
            string jsonBody = BuildRequestJson(config, systemPrompt, userMessage, false, imagesBase64);
            onRequestBody?.Invoke(jsonBody);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

            string url = LLMClient.GetCorsProxyUrl(config.apiEndpoint);
            Debug.Log($"[OpenAIProvider] Sending to {url}, model={config.modelName}");

            using (var request = new UnityWebRequest(url, "POST"))
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
            List<string> imagesBase64 = null, Action<string> onRequestBody = null,
            Action<string, string> onField = null,
            Action<string, string> onParseError = null)
        {
            string jsonBody = BuildRequestJson(config, systemPrompt, userMessage, true, imagesBase64);
            onRequestBody?.Invoke(jsonBody);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

            string url = LLMClient.GetCorsProxyUrl(config.apiEndpoint);
            Debug.Log($"[OpenAIProvider] Streaming request to {url}, model={config.modelName}");

            // OpenAI SSEストリーミング（LMStudioと同じフォーマット）
            var streamHandler = new LMStudioSseStreamHandler(
                new byte[4096], onHeader, onTextChunk, onError, onField, onParseError);

            using (var request = new UnityWebRequest(url, "POST"))
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
            // URL検証: Uri.TryCreate（new Uriだと不正URL入力時に例外でコルーチンが
            // 打ち切られ、callbackが呼ばれずUIが無反応になる）+ スキーム確認
            // （"ttp://"等のタイプミスはURL文法上は合法な未知スキームとして
            // 解析に成功してしまい、通信層のUnknown Errorになる）
            string baseUrl = config.apiEndpoint;
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                callback?.Invoke(false, $"Invalid URL: {baseUrl}");
                yield break;
            }

            // エンドポイントURLから /v1/models APIのURLを構築
            int v1Index = baseUrl.IndexOf("/v1/", StringComparison.OrdinalIgnoreCase);
            string testUrl;
            if (v1Index >= 0)
            {
                testUrl = baseUrl.Substring(0, v1Index) + "/v1/models";
            }
            else
            {
                testUrl = $"{uri.Scheme}://{uri.Authority}/v1/models";
            }

            testUrl = LLMClient.GetCorsProxyUrl(testUrl);
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

            // 生成パラメータ
            sb.Append($",\"temperature\":{config.temperature.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            sb.Append($",\"top_p\":{config.topP.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

            // max_completion_tokens: numPredict > 0 の場合のみ送信
            // 新しいモデル（gpt-4o以降）はmax_tokensではなくmax_completion_tokensを使用
            if (config.numPredict > 0)
            {
                sb.Append($",\"max_completion_tokens\":{config.numPredict}");
            }

            // repeat_penalty → frequency_penalty として送信（近似マッピング）
            if (config.repeatPenalty > 0f && config.repeatPenalty != 1.0f)
            {
                float freqPenalty = config.repeatPenalty - 1.0f;
                sb.Append($",\"frequency_penalty\":{freqPenalty.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            }

            // 追加パラメータ（上級者設定）をトップレベルにマージ
            // UIに無いフィールド（chat_template_kwargs等のサーバー固有設定）を送るための逃げ道
            if (LLMConfig.TryGetExtraParamsBody(config.extraParamsJson, out string extraBody))
            {
                sb.Append(',').Append(extraBody);
            }
            else if (!string.IsNullOrWhiteSpace(config.extraParamsJson))
            {
                // 設定Import経由などで不正な値が入った場合の診断用（黙って捨てない）
                Debug.LogWarning("[OpenAIProvider] extraParamsJson is not a valid JSON object, ignored");
            }

            sb.Append("}");
            return sb.ToString();
        }

    }
}
