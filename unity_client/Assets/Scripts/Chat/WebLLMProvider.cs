using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CyanNook.Chat
{
    /// <summary>
    /// WebLLM プロバイダー（ILLMProvider実装）
    /// ブラウザ内WebGPU推論を行う。WebLLMBridgeを経由してjslibと通信する。
    /// </summary>
    public class WebLLMProvider : ILLMProvider
    {
        public bool SupportsStreaming => true;

        // デフォルトモデルID
        public const string DefaultModelId = "Qwen3-1.7B-q4f16_1-MLC";

        // 実行中リクエストのBridgeイベント購読解除処理。
        // StopCoroutineで中断されるとコルーチン内の購読解除行に到達せず、
        // 残った購読が次リクエストのチャンクや中断済み生成のComplete通知を
        // 受け取って二重配信されるため、外部から解除できる形で保持する
        private Action _activeCleanup;

        private void CleanupActiveSubscriptions()
        {
            _activeCleanup?.Invoke();
            _activeCleanup = null;
        }

        /// <summary>
        /// 生成を中断する（LLMClient.AbortRequestから呼ばれる）
        /// ブラウザ側の生成停止と、中断で残るBridgeイベント購読の解除を行う。
        /// 必ずコルーチン停止（StopCoroutine）とセットで呼ぶこと。
        /// 単体で呼ぶと実行中コルーチンの待機ループが完了フラグを待ち続けて固まる
        /// </summary>
        public void Abort()
        {
            CleanupActiveSubscriptions();
            WebLLMBridge.Instance?.AbortGeneration();
        }

        public IEnumerator SendRequest(LLMConfig config, string systemPrompt, string userMessage,
            Action<string> onSuccess, Action<string> onError,
            List<string> imagesBase64 = null, Action<string> onRequestBody = null)
        {
            var bridge = WebLLMBridge.Instance;
            if (bridge == null)
            {
                onError?.Invoke("WebLLMBridge not found in scene");
                yield break;
            }

            // モデルがロードされていなければロード待ち
            if (!bridge.IsModelLoaded)
            {
                if (!bridge.IsLoading)
                {
                    bridge.LoadModel(config.modelName ?? DefaultModelId);
                }
                while (bridge.IsLoading)
                    yield return null;

                if (!bridge.IsModelLoaded)
                {
                    onError?.Invoke("Failed to load WebLLM model");
                    yield break;
                }
            }

            // 生成パラメータ適用
            bridge.SetGenerationParams(config.temperature, config.topP, config.numPredict, config.repeatPenalty);

            // リクエスト送信
            bool completed = false;
            string result = null;
            string error = null;

            Action<string> onResp = (r) => { result = r; completed = true; };
            Action<string> onErr = (e) => { error = e; completed = true; };

            // 前回リクエストが中断されていた場合に残った購読を掃除（保険）
            CleanupActiveSubscriptions();

            bridge.OnResponse += onResp;
            bridge.OnError += onErr;
            _activeCleanup = () =>
            {
                bridge.OnResponse -= onResp;
                bridge.OnError -= onErr;
            };

            bridge.SendChatRequest(systemPrompt, userMessage);

            while (!completed)
                yield return null;

            CleanupActiveSubscriptions();

            if (error != null)
                onError?.Invoke(error);
            else
                onSuccess?.Invoke(result);
        }

        public IEnumerator SendStreamingRequest(LLMConfig config, string systemPrompt, string userMessage,
            Action<LlmResponseHeader> onHeader, Action<string> onTextChunk,
            Action onComplete, Action<string> onError,
            List<string> imagesBase64 = null, Action<string> onRequestBody = null,
            Action<string, string> onField = null,
            Action<string, string> onParseError = null)
        {
            var bridge = WebLLMBridge.Instance;
            if (bridge == null)
            {
                onError?.Invoke("WebLLMBridge not found in scene");
                yield break;
            }

            // モデルがロードされていなければロード待ち
            if (!bridge.IsModelLoaded)
            {
                if (!bridge.IsLoading)
                {
                    bridge.LoadModel(config.modelName ?? DefaultModelId);
                }
                while (bridge.IsLoading)
                    yield return null;

                if (!bridge.IsModelLoaded)
                {
                    onError?.Invoke("Failed to load WebLLM model");
                    yield break;
                }
            }

            // 生成パラメータ適用
            bridge.SetGenerationParams(config.temperature, config.topP, config.numPredict, config.repeatPenalty);

            // StreamSeparatorProcessor でJSON逐次パース
            var processor = new StreamSeparatorProcessor
            {
                OnHeaderReceived = onHeader,
                OnTextReceived = onTextChunk,
                OnFieldParsed = onField,
                OnParseError = onParseError
            };

            bool streamCompleted = false;
            string streamError = null;

            Action<string> onChunk = (chunk) => { processor.ProcessChunk(chunk); };
            Action onComp = () => { processor.Complete(); streamCompleted = true; };
            Action<string> onErr = (e) => { streamError = e; streamCompleted = true; };

            // 前回リクエストが中断されていた場合に残った購読を掃除（保険）
            CleanupActiveSubscriptions();

            bridge.OnStreamChunk += onChunk;
            bridge.OnStreamComplete += onComp;
            bridge.OnError += onErr;
            _activeCleanup = () =>
            {
                bridge.OnStreamChunk -= onChunk;
                bridge.OnStreamComplete -= onComp;
                bridge.OnError -= onErr;
            };

            bridge.SendStreamingChatRequest(systemPrompt, userMessage);

            while (!streamCompleted)
                yield return null;

            CleanupActiveSubscriptions();

            if (streamError != null)
                onError?.Invoke(streamError);
            else
                onComplete?.Invoke();
        }

        public IEnumerator TestConnection(LLMConfig config, Action<bool, string> callback)
        {
            var bridge = WebLLMBridge.Instance;
            if (bridge == null)
            {
                callback?.Invoke(false, "WebLLMBridge not found in scene");
                yield break;
            }

            if (!bridge.IsWebGPUSupported())
            {
                callback?.Invoke(false, "WebGPU is not supported in this browser");
                yield break;
            }

            if (bridge.IsModelLoaded)
            {
                callback?.Invoke(true, "Model loaded and ready");
                yield break;
            }

            callback?.Invoke(true, "WebGPU supported. Model not yet loaded.");
        }

        public void SetInputs(Dictionary<string, string> inputs) { }

        public void ClearConversation()
        {
            // WebLLMはステートレス（会話履歴はC#側で管理）
        }
    }
}
