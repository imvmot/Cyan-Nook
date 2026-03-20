using System;
using System.Collections;
using System.Collections.Generic;

namespace CyanNook.Chat
{
    /// <summary>
    /// LLM APIプロバイダーのインターフェース
    /// 各API（Ollama, Dify等）はこのインターフェースを実装する
    /// プロバイダーはIEnumeratorを返し、LLMClient側でStartCoroutineする
    /// </summary>
    public interface ILLMProvider
    {
        /// <summary>
        /// LLMにリクエストを送信（ブロッキング方式）
        /// </summary>
        /// <param name="config">API設定</param>
        /// <param name="systemPrompt">システムプロンプト（プロバイダーによっては無視される）</param>
        /// <param name="userMessage">ユーザーメッセージ</param>
        /// <param name="onSuccess">成功コールバック（LLMの応答テキスト）</param>
        /// <param name="onError">エラーコールバック</param>
        /// <param name="imageBase64">画像データ（base64エンコード、null=画像なし）</param>
        /// <param name="onRequestBody">リクエストボディ通知コールバック（デバッグ表示用）</param>
        IEnumerator SendRequest(LLMConfig config, string systemPrompt, string userMessage,
            Action<string> onSuccess, Action<string> onError,
            string imageBase64 = null, Action<string> onRequestBody = null);

        /// <summary>
        /// LLMにストリーミングリクエストを送信
        /// ヘッダー（メタデータ）受信後にテキストが逐次流れてくる
        /// </summary>
        /// <param name="config">API設定</param>
        /// <param name="systemPrompt">システムプロンプト</param>
        /// <param name="userMessage">ユーザーメッセージ</param>
        /// <param name="onHeader">ヘッダー受信コールバック（演出・感情データ）</param>
        /// <param name="onTextChunk">テキストチャンク受信コールバック</param>
        /// <param name="onComplete">完了コールバック</param>
        /// <param name="onError">エラーコールバック</param>
        /// <param name="imageBase64">画像データ（base64エンコード、null=画像なし）</param>
        /// <param name="onRequestBody">リクエストボディ通知コールバック（デバッグ表示用）</param>
        /// <param name="onField">JSONフィールド逐次受信コールバック（fieldName, rawJsonValue）</param>
        /// <param name="onParseError">JSONパースエラー時コールバック（errorMessage, rawText）</param>
        IEnumerator SendStreamingRequest(LLMConfig config, string systemPrompt, string userMessage,
            Action<LlmResponseHeader> onHeader, Action<string> onTextChunk,
            Action onComplete, Action<string> onError,
            string imageBase64 = null, Action<string> onRequestBody = null,
            Action<string, string> onField = null,
            Action<string, string> onParseError = null);

        /// <summary>
        /// ストリーミング対応かどうか
        /// </summary>
        bool SupportsStreaming { get; }

        /// <summary>
        /// 接続テスト
        /// </summary>
        IEnumerator TestConnection(LLMConfig config, Action<bool, string> callback);

        /// <summary>
        /// リクエスト前に動的変数を設定（Difyのinputsフィールド等で使用）
        /// </summary>
        void SetInputs(Dictionary<string, string> inputs);

        /// <summary>
        /// 会話状態をリセット（Difyのconversation_id等）
        /// </summary>
        void ClearConversation();
    }
}
