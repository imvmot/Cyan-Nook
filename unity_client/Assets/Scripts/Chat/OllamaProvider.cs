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
    /// Ollama / LM Studio 用プロバイダー
    /// endpoint: Ollama APIの完全URL（例: http://localhost:11434/api/generate）
    /// </summary>
    public class OllamaProvider : ILLMProvider
    {
        public bool SupportsStreaming => true;

        public IEnumerator SendRequest(LLMConfig config, string systemPrompt, string userMessage,
            Action<string> onSuccess, Action<string> onError,
            List<string> imagesBase64 = null, Action<string> onRequestBody = null)
        {
            var requestBody = new OllamaRequest
            {
                model = config.modelName,
                prompt = userMessage,
                system = systemPrompt,
                stream = false,
                options = BuildOptions(config)
            };

            string jsonBody = BuildRequestJson(requestBody, config.think, imagesBase64);
            onRequestBody?.Invoke(jsonBody);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

            Debug.Log($"[OllamaProvider] Sending to {config.apiEndpoint}, model={config.modelName}");

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
                        var ollamaResponse = JsonUtility.FromJson<OllamaResponse>(responseText);

                        if (string.IsNullOrEmpty(ollamaResponse.response))
                        {
                            onError?.Invoke("Empty response from Ollama");
                        }
                        else
                        {
                            onSuccess?.Invoke(ollamaResponse.response.Trim());
                        }
                    }
                    catch (Exception e)
                    {
                        onError?.Invoke($"Failed to parse Ollama response: {e.Message}");
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
            var requestBody = new OllamaRequest
            {
                model = config.modelName,
                prompt = userMessage,
                system = systemPrompt,
                stream = true,
                options = BuildOptions(config)
            };

            string jsonBody = BuildRequestJson(requestBody, config.think, imagesBase64);
            onRequestBody?.Invoke(jsonBody);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

            Debug.Log($"[OllamaProvider] Streaming request to {config.apiEndpoint}, model={config.modelName}");

            // Ollama NDJSON用ストリーミングハンドラ
            var streamHandler = new OllamaNdjsonStreamHandler(
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

        /// <summary>
        /// LLMConfigからOllamaOptionsを構築
        /// </summary>
        private static OllamaOptions BuildOptions(LLMConfig config)
        {
            return new OllamaOptions
            {
                temperature = config.temperature,
                top_p = config.topP,
                top_k = config.topK,
                num_predict = config.numPredict,
                num_ctx = config.numCtx,
                repeat_penalty = config.repeatPenalty
            };
        }

        /// <summary>
        /// リクエストJSON文字列を構築
        /// think（トップレベル）、images（条件付き）はJsonUtilityでは扱えないため手動構築
        /// </summary>
        private static string BuildRequestJson(OllamaRequest request, bool think, List<string> imagesBase64 = null)
        {
            string escapedModel = JsonEscape.Escape(request.model);
            string escapedPrompt = JsonEscape.Escape(request.prompt);
            string escapedSystem = JsonEscape.Escape(request.system);
            string streamStr = request.stream ? "true" : "false";
            string thinkStr = think ? "true" : "false";

            var sb = new StringBuilder();
            sb.Append($"{{\"model\":\"{escapedModel}\"");
            sb.Append($",\"prompt\":\"{escapedPrompt}\"");
            sb.Append($",\"system\":\"{escapedSystem}\"");
            sb.Append($",\"stream\":{streamStr}");
            sb.Append($",\"think\":{thinkStr}");

            // options
            var opt = request.options;
            sb.Append($",\"options\":{{\"temperature\":{opt.temperature}");
            sb.Append($",\"top_p\":{opt.top_p}");
            sb.Append($",\"top_k\":{opt.top_k}");
            sb.Append($",\"num_predict\":{opt.num_predict}");
            sb.Append($",\"num_ctx\":{opt.num_ctx}");
            sb.Append($",\"repeat_penalty\":{opt.repeat_penalty}}}");

            // images（あれば、複数画像対応）
            if (imagesBase64 != null && imagesBase64.Count > 0)
            {
                sb.Append(",\"images\":[");
                for (int i = 0; i < imagesBase64.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append($"\"{imagesBase64[i]}\"");
                }
                sb.Append("]");
            }

            sb.Append("}");
            return sb.ToString();
        }

        public IEnumerator TestConnection(LLMConfig config, Action<bool, string> callback)
        {
            // エンドポイントのベースURLからタグ一覧APIのURLを構築
            var uri = new Uri(config.apiEndpoint);
            string testUrl = $"{uri.Scheme}://{uri.Authority}/api/tags";

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
            // Ollama/LM Studioでは不使用
        }

        public void ClearConversation()
        {
            // Ollamaは会話状態を持たないため何もしない
        }
    }

    /// <summary>
    /// Ollama NDJSON ストリーミング用DownloadHandler
    /// Ollamaのstream=trueレスポンス（各行がJSON）をパースし、
    /// responseフィールドを抽出してStreamSeparatorProcessorに渡す
    /// </summary>
    internal class OllamaNdjsonStreamHandler : LineStreamHandlerBase
    {
        public OllamaNdjsonStreamHandler(byte[] preallocatedBuffer,
            Action<LlmResponseHeader> onHeader, Action<string> onTextChunk,
            Action<string> onError, Action<string, string> onField = null,
            Action<string, string> onParseError = null)
            : base(preallocatedBuffer, onHeader, onTextChunk, onError, onField, onParseError)
        {
        }

        // NDJSONは最終行が\nで終わらないことがある
        protected override bool ProcessTrailingLineOnComplete => true;

        /// <summary>
        /// 1行のNDJSON（Ollamaレスポンス）を処理し、responseフィールドを抽出
        /// </summary>
        protected override void ProcessLine(string line)
        {
            try
            {
                var ollamaChunk = JsonUtility.FromJson<OllamaResponse>(line);
                if (!string.IsNullOrEmpty(ollamaChunk.response))
                {
                    Processor.ProcessChunk(ollamaChunk.response);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[OllamaNdjsonStreamHandler] Failed to parse line: {e.Message}\nLine: {line}");
            }
        }
    }

    [Serializable]
    public class OllamaRequest
    {
        public string model;
        public string prompt;
        public string system;
        public bool stream;
        public OllamaOptions options;
        // thinkはJsonUtilityでbool条件付きシリアライズが困難なため手動JSON構築で対応
    }

    [Serializable]
    public class OllamaOptions
    {
        public float temperature;
        public float top_p;
        public int top_k;
        public int num_predict;
        public int num_ctx;
        public float repeat_penalty;
    }

    [Serializable]
    public class OllamaResponse
    {
        public string model;
        public string response;
        public bool done;
    }
}
