using System;
using UnityEngine;
using CyanNook.Core;

namespace CyanNook.Chat
{
    /// <summary>
    /// ストリーミングレスポンスのヘッダー情報
    /// JSON全体からパースされ、メタデータ + reaction を含む
    ///
    /// フォーマット例:
    /// {
    ///   "emotion": { "happy": 1.0, "relaxed": 0.5, "angry": 0.0, "sad": 0.0, "surprised": 0.0 },
    ///   "reaction": "いいね!",
    ///   "target": { "type": "talk" },
    ///   "action": "move",
    ///   "emote": "happy01",
    ///   "message": "本文..."
    /// }
    /// </summary>
    [Serializable]
    public class LlmResponseHeader
    {
        public string reaction;  // 短い相槌・リアクション（省略可）
        public string message;   // メイン本文（JSON全体パース時に含まれる）
        public string emote;     // "Neutral", "happy01", "relaxed01", etc.
        public string action;    // "move", "interact_sit", "interact_sleep", "interact_exit", "ignore"
        public TargetData target;
        public EmotionData emotion;

        /// <summary>
        /// ヘッダーからLLMResponseDataを生成
        /// streamingMessage が指定された場合はそちらを優先（ストリーミング蓄積テキスト）
        /// </summary>
        public LLMResponseData ToResponseData(string streamingMessage = null)
        {
            return new LLMResponseData
            {
                character = LLMResponseData.DefaultCharacterId,
                reaction = this.reaction ?? "",
                message = streamingMessage ?? this.message ?? "",
                emote = this.emote ?? LLMResponseData.DefaultEmote,
                action = this.action ?? LLMResponseData.DefaultAction,
                target = this.target ?? new TargetData
                {
                    type = LLMResponseData.DefaultTargetType
                },
                emotion = this.emotion ?? new EmotionData()
            };
        }

        /// <summary>
        /// フォールバック用デフォルトヘッダー
        /// </summary>
        public static LlmResponseHeader GetFallback()
        {
            return new LlmResponseHeader
            {
                reaction = "",
                message = "",
                emote = LLMResponseData.DefaultEmote,
                action = LLMResponseData.DefaultAction,
                target = new TargetData
                {
                    type = LLMResponseData.DefaultTargetType
                },
                emotion = new EmotionData()
            };
        }
    }
}
