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
    /// Dify Chat Messages API 用プロバイダー
    /// endpoint: Dify APIのベースURL（例: http://localhost/v1）
    /// 会話履歴はDify側で管理（conversation_idで紐付け）
    /// システムプロンプトはDifyアプリ側で設定（query には含めない）
    /// 動的変数（bored, current_pose等）はinputsフィールドで送信
    /// </summary>
    public class DifyProvider : ILLMProvider
    {
        private const string ChatEndpoint = "/chat-messages";
        private const string FilesUploadEndpoint = "/files/upload";
        private const string ParametersEndpoint = "/parameters";
        private const string DefaultUser = "unity-user";

        // Difyが返すconversation_idを保持し、以降のリクエストに使用
        private string _conversationId = "";

        // Dify APIのinputsフィールドに送信する動的変数
        private Dictionary<string, string> _inputs;

        public bool SupportsStreaming => true;

        public IEnumerator SendRequest(LLMConfig config, string systemPrompt, string userMessage,
            Action<string> onSuccess, Action<string> onError,
            List<string> imagesBase64 = null, Action<string> onRequestBody = null)
        {
            // 画像がある場合はアップロードしてfileIdを取得
            List<string> fileIds = null;
            if (imagesBase64 != null && imagesBase64.Count > 0)
            {
                fileIds = new List<string>();
                foreach (var imgBase64 in imagesBase64)
                {
                    string uploadedId = null;
                    string uploadError = null;

                    yield return UploadImage(config, imgBase64,
                        (id) => { uploadedId = id; },
                        (err) => { uploadError = err; });

                    if (!string.IsNullOrEmpty(uploadError))
                    {
                        Debug.LogWarning($"[DifyProvider] Image upload failed: {uploadError}, skipping");
                    }
                    else if (!string.IsNullOrEmpty(uploadedId))
                    {
                        fileIds.Add(uploadedId);
                    }
                }
                if (fileIds.Count == 0) fileIds = null;
            }

            string url = config.apiEndpoint.TrimEnd('/') + ChatEndpoint;
            string jsonBody = BuildRequestJson(systemPrompt, userMessage, "blocking", fileIds);
            onRequestBody?.Invoke(jsonBody);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

            Debug.Log($"[DifyProvider] Sending to {url}, conversation_id={_conversationId}, imageCount={fileIds?.Count ?? 0}");

            using (var request = new UnityWebRequest(url, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {config.apiKey}");
                request.timeout = (int)config.timeout;

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    string responseText = request.downloadHandler.text;
                    try
                    {
                        var difyResponse = JsonUtility.FromJson<DifyChatResponse>(responseText);

                        // conversation_idを保存（次回リクエストで使用）
                        if (!string.IsNullOrEmpty(difyResponse.conversation_id))
                        {
                            _conversationId = difyResponse.conversation_id;
                        }

                        if (string.IsNullOrEmpty(difyResponse.answer))
                        {
                            onError?.Invoke("Empty answer from Dify");
                        }
                        else
                        {
                            Debug.Log($"[DifyProvider] Response received, conversation_id={_conversationId}");
                            onSuccess?.Invoke(difyResponse.answer.Trim());
                        }
                    }
                    catch (Exception e)
                    {
                        onError?.Invoke($"Failed to parse Dify response: {e.Message}");
                    }
                }
                else
                {
                    string errorDetail = request.downloadHandler?.text ?? "";
                    onError?.Invoke($"Dify request failed: {request.error} {errorDetail}");
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
            // 画像がある場合はアップロードしてfileIdを取得
            List<string> fileIds = null;
            if (imagesBase64 != null && imagesBase64.Count > 0)
            {
                fileIds = new List<string>();
                foreach (var imgBase64 in imagesBase64)
                {
                    string uploadedId = null;
                    string uploadError = null;

                    yield return UploadImage(config, imgBase64,
                        (id) => { uploadedId = id; },
                        (err) => { uploadError = err; });

                    if (!string.IsNullOrEmpty(uploadError))
                    {
                        Debug.LogWarning($"[DifyProvider] Image upload failed: {uploadError}, skipping");
                    }
                    else if (!string.IsNullOrEmpty(uploadedId))
                    {
                        fileIds.Add(uploadedId);
                    }
                }
                if (fileIds.Count == 0) fileIds = null;
            }

            string url = config.apiEndpoint.TrimEnd('/') + ChatEndpoint;
            string jsonBody = BuildRequestJson(systemPrompt, userMessage, "streaming", fileIds);
            onRequestBody?.Invoke(jsonBody);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

            Debug.Log($"[DifyProvider] Streaming request to {url}, conversation_id={_conversationId}, imageCount={fileIds?.Count ?? 0}");

            // Dify SSE用ストリーミングハンドラ
            var streamHandler = new DifySseStreamHandler(
                new byte[4096], onHeader, onTextChunk, onError,
                (conversationId) =>
                {
                    if (!string.IsNullOrEmpty(conversationId))
                    {
                        _conversationId = conversationId;
                    }
                },
                onField, onParseError);

            using (var request = new UnityWebRequest(url, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = streamHandler;
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {config.apiKey}");
                request.SetRequestHeader("Accept", "text/event-stream");
                request.timeout = (int)config.timeout;

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke($"Dify streaming request failed: {request.error}");
                }
            }

            onComplete?.Invoke();
        }

        public IEnumerator TestConnection(LLMConfig config, Action<bool, string> callback)
        {
            string url = config.apiEndpoint.TrimEnd('/') + ParametersEndpoint;

            using (var request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("Authorization", $"Bearer {config.apiKey}");
                request.timeout = (int)config.timeout;
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    callback?.Invoke(true, "Dify connection successful");
                }
                else
                {
                    callback?.Invoke(false, $"{request.error} (API Key may be invalid)");
                }
            }
        }

        public void SetInputs(Dictionary<string, string> inputs)
        {
            _inputs = inputs;
        }

        public void ClearConversation()
        {
            _conversationId = "";
            Debug.Log("[DifyProvider] Conversation cleared");
        }

        /// <summary>
        /// Dify Files Upload API で画像をアップロード
        /// base64文字列をバイトに戻してmultipart/form-dataで送信
        /// </summary>
        private IEnumerator UploadImage(LLMConfig config, string imageBase64,
            Action<string> onFileId, Action<string> onError)
        {
            string url = config.apiEndpoint.TrimEnd('/') + FilesUploadEndpoint;

            byte[] imageBytes;
            try
            {
                imageBytes = Convert.FromBase64String(imageBase64);
            }
            catch (Exception e)
            {
                onError?.Invoke($"Failed to decode base64 image: {e.Message}");
                yield break;
            }

            Debug.Log($"[DifyProvider] Uploading image to {url}, size={imageBytes.Length} bytes");

            // WWWFormでmultipart/form-dataを構築
            var form = new WWWForm();
            form.AddBinaryData("file", imageBytes, "character_view.jpg", "image/jpeg");
            form.AddField("user", DefaultUser);

            using (var request = UnityWebRequest.Post(url, form))
            {
                request.SetRequestHeader("Authorization", $"Bearer {config.apiKey}");
                request.timeout = Mathf.Max(30, (int)config.timeout);

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    string responseText = request.downloadHandler.text;
                    try
                    {
                        var uploadResponse = JsonUtility.FromJson<DifyFileUploadResponse>(responseText);

                        if (!string.IsNullOrEmpty(uploadResponse.id))
                        {
                            Debug.Log($"[DifyProvider] Image uploaded, file_id={uploadResponse.id}");
                            onFileId?.Invoke(uploadResponse.id);
                        }
                        else
                        {
                            onError?.Invoke("Empty file_id in upload response");
                        }
                    }
                    catch (Exception e)
                    {
                        onError?.Invoke($"Failed to parse upload response: {e.Message}");
                    }
                }
                else
                {
                    string errorDetail = request.downloadHandler?.text ?? "";
                    onError?.Invoke($"Image upload failed: {request.error} {errorDetail}");
                }
            }
        }

        /// <summary>
        /// Dify Chat Messages API のリクエストJSONを構築
        /// queryにはuserMessageのみ、動的変数はinputsフィールドで送信
        /// </summary>
        private string BuildRequestJson(string systemPrompt, string userMessage, string responseMode, List<string> fileIds = null)
        {
            string escapedQuery = JsonEscape.Escape(userMessage);
            string escapedConvId = JsonEscape.Escape(_conversationId);

            // 動的変数をinputsフィールドに設定
            string inputsJson = BuildInputsJson();

            string filesField = "";
            if (fileIds != null && fileIds.Count > 0)
            {
                var sb = new StringBuilder();
                sb.Append(",\"files\":[");
                for (int i = 0; i < fileIds.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    string escapedFileId = JsonEscape.Escape(fileIds[i]);
                    sb.Append($"{{\"type\":\"image\",\"transfer_method\":\"local_file\",\"upload_file_id\":\"{escapedFileId}\"}}");
                }
                sb.Append("]");
                filesField = sb.ToString();
            }

            return $"{{\"inputs\":{inputsJson},\"query\":\"{escapedQuery}\",\"response_mode\":\"{responseMode}\",\"conversation_id\":\"{escapedConvId}\",\"user\":\"{DefaultUser}\"{filesField}}}";
        }

        /// <summary>
        /// _inputsをJSON形式に変換
        /// </summary>
        private string BuildInputsJson()
        {
            if (_inputs == null || _inputs.Count == 0) return "{}";

            var sb = new StringBuilder();
            sb.Append("{");
            bool first = true;
            foreach (var kv in _inputs)
            {
                if (!first) sb.Append(",");
                sb.Append($"\"{JsonEscape.Escape(kv.Key)}\":\"{JsonEscape.Escape(kv.Value)}\"");
                first = false;
            }
            sb.Append("}");
            return sb.ToString();
        }

    }

    /// <summary>
    /// Dify SSE ストリーミング用DownloadHandler
    /// Difyのstreaming応答（SSE形式）をパースし、
    /// answerフィールドを抽出してStreamSeparatorProcessorに渡す
    ///
    /// Dify SSEフォーマット:
    ///   data: {"event": "message", "answer": "token", "conversation_id": "...", ...}
    ///   data: {"event": "message_end", "conversation_id": "...", ...}
    /// </summary>
    internal class DifySseStreamHandler : SseStreamHandlerBase
    {
        private readonly Action<string> _onConversationId;

        private bool _conversationIdCaptured;

        public DifySseStreamHandler(byte[] preallocatedBuffer,
            Action<LlmResponseHeader> onHeader, Action<string> onTextChunk,
            Action<string> onError, Action<string> onConversationId,
            Action<string, string> onField = null,
            Action<string, string> onParseError = null)
            : base(preallocatedBuffer, onHeader, onTextChunk, onError, onField, onParseError)
        {
            _onConversationId = onConversationId;
        }

        /// <summary>
        /// SSEのdataフィールド（JSON）を処理
        /// messageイベントのanswerフィールドを抽出。conversation_idは最初の1回だけ通知
        /// </summary>
        protected override void ProcessSseData(string jsonData)
        {
            var sseEvent = JsonUtility.FromJson<DifySseEvent>(jsonData);

            // conversation_idを保存（最初の1回）
            if (!_conversationIdCaptured && !string.IsNullOrEmpty(sseEvent.conversation_id))
            {
                _conversationIdCaptured = true;
                _onConversationId?.Invoke(sseEvent.conversation_id);
            }

            // messageイベント: answerフィールドをセパレータ処理に渡す
            if (sseEvent.@event == "message" && !string.IsNullOrEmpty(sseEvent.answer))
            {
                Processor.ProcessChunk(sseEvent.answer);
            }
        }
    }

    [Serializable]
    public class DifyChatResponse
    {
        public string message_id;
        public string conversation_id;
        public string answer;
        public int created_at;
    }

    /// <summary>
    /// Dify SSEイベントのデータ構造
    /// </summary>
    [Serializable]
    internal class DifySseEvent
    {
        // JsonUtilityはC#キーワードのフィールド名に@プレフィックスが必要
        // シリアライズ時は "event" として認識される
        public string @event;
        public string task_id;
        public string message_id;
        public string conversation_id;
        public string answer;
    }

    /// <summary>
    /// Dify Files Upload API のレスポンス
    /// </summary>
    [Serializable]
    internal class DifyFileUploadResponse
    {
        public string id;
        public string name;
    }
}
