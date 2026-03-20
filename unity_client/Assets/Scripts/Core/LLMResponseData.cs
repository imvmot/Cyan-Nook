using System;
using System.Collections.Generic;
using UnityEngine;

namespace CyanNook.Core
{
    /// <summary>
    /// LLMからのレスポンスをパースするためのデータクラス
    ///
    /// JSONフォーマット:
    /// {
    ///   "emotion": { "happy": 0.5, "relaxed": 0.0, "angry": 0.0, "sad": 0.0, "surprised": 0.0 },
    ///   "reaction": "短い相槌",
    ///   "action": "move",
    ///   "target": { "type": "talk" },
    ///   "emote": "Neutral",
    ///   "message": "メッセージ本文"
    /// }
    ///
    /// reaction: 即座リアクション（短い相槌、省略可）
    /// message: メイン本文（ストリーミング表示、省略可）
    /// </summary>
    [Serializable]
    public class LLMResponseData
    {
        // フォールバック用デフォルト値
        public const string DefaultCharacterId = "chr001";
        public const string DefaultAction = "ignore";
        public const string DefaultTargetType = "talk";
        public const string DefaultEmote = "Neutral";
        public const string FallbackMessage = "...";
        public const string InteractPrefix = "interact_";

        public string character;
        public string reaction;  // 短い相槌・リアクション（省略可）
        public string message;   // メイン本文（省略可）
        public string emote;     // "Neutral", "happy01", "relaxed01", "angry01", "sad01", "surprised01"
        public string action;    // "move", "interact_sit", "interact_sleep", "interact_exit", "ignore"
        public int sleep_duration; // 睡眠時間（分）。action が interact_sleep の時のみ有効。0=未指定
        public TargetData target;
        public EmotionData emotion;

        /// <summary>
        /// action が "ignore" かどうか
        /// </summary>
        public bool IsIgnore => action == "ignore" || string.IsNullOrEmpty(action);

        /// <summary>
        /// action が "move" かどうか
        /// </summary>
        public bool IsMove => action == "move";

        /// <summary>
        /// action が interact 系かどうか
        /// </summary>
        public bool IsInteract => action != null && action.StartsWith(InteractPrefix);

        /// <summary>
        /// interact action から家具アクション名を取得（例: "interact_sit" → "sit"）
        /// </summary>
        public string GetInteractAction()
        {
            if (!IsInteract) return null;
            return action.Substring(InteractPrefix.Length);
        }

        /// <summary>
        /// メッセージが存在するかどうか（speak判定）
        /// reaction または message のいずれかがあればtrue
        /// </summary>
        public bool HasMessage => !string.IsNullOrWhiteSpace(reaction) || !string.IsNullOrWhiteSpace(message);

        /// <summary>
        /// reaction + message を結合した全文テキスト（会話履歴・表示用）
        /// </summary>
        public string FullMessage
        {
            get
            {
                bool hasReaction = !string.IsNullOrWhiteSpace(reaction);
                bool hasMsg = !string.IsNullOrWhiteSpace(message);

                if (hasReaction && hasMsg)
                    return reaction + "\n" + message;
                if (hasReaction)
                    return reaction;
                return message;
            }
        }

        /// <summary>
        /// emote が Neutral 以外かどうか
        /// </summary>
        public bool HasEmote => !string.IsNullOrEmpty(emote) && emote != "Neutral";

        /// <summary>
        /// JSONからパース
        /// </summary>
        public static LLMResponseData FromJson(string json)
        {
            try
            {
                return JsonUtility.FromJson<LLMResponseData>(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[LLMResponseData] Failed to parse JSON: {e.Message}");
                return GetFallback();
            }
        }

        /// <summary>
        /// パース失敗時のフォールバック
        /// </summary>
        public static LLMResponseData GetFallback()
        {
            return new LLMResponseData
            {
                character = DefaultCharacterId,
                reaction = "",
                message = FallbackMessage,
                emote = DefaultEmote,
                action = DefaultAction,
                target = new TargetData
                {
                    type = DefaultTargetType
                },
                emotion = new EmotionData()
            };
        }

        /// <summary>
        /// データの妥当性を検証
        /// </summary>
        public bool Validate()
        {
            if (string.IsNullOrEmpty(character)) return false;
            if (target == null) return false;
            if (emotion == null) return false;
            return true;
        }
    }

    /// <summary>
    /// LookAt/移動ターゲット情報
    ///
    /// type: ターゲットの種類
    ///   - "talk": LookAt→talk_lookattarget、Move→RoomTarget "talk" 位置（Talk状態遷移）
    ///   - "interact_sit/sleep/exit": LookAt→家具lookattarget、Move→家具位置
    ///   - "dynamic": LookAt/Move→DynamicTarget位置
    ///   - "{name}": LookAt/Move→RoomTarget位置（登録済み名前と一致時）
    ///
    /// clock/distance/height: dynamic時のみ有効
    /// </summary>
    [Serializable]
    public class TargetData
    {
        public string type;       // "talk", "interact_sit", "interact_sleep", "interact_exit", "dynamic", "{room_target_name}"
        public int clock;         // 1-12（dynamic時のみ、キャラ基準 12=正面）
        public string distance;   // "near", "mid", "far"（dynamic時のみ）
        public string height;     // "high", "mid", "low"（dynamic時のみ）

        /// <summary>
        /// ターゲットタイプを解決
        /// roomTargetNames が指定された場合、登録済みの名前と一致すれば Named を返す
        /// 未知のtypeは Dynamic にフォールバック（clock/distance/height未設定なら実質無操作）
        /// </summary>
        public TargetType GetTargetType(ICollection<string> roomTargetNames = null)
        {
            if (string.IsNullOrEmpty(type)) return TargetType.Talk;

            if (type == "player" || type == "talk") return TargetType.Talk;
            if (type == "dynamic") return TargetType.Dynamic;
            if (type.StartsWith(LLMResponseData.InteractPrefix)) return TargetType.Interact;
            if (roomTargetNames != null && roomTargetNames.Contains(type.ToLower())) return TargetType.Named;

            // 未知のtype → Dynamic フォールバック
            return TargetType.Dynamic;
        }

        /// <summary>
        /// interact系ターゲットから家具アクション名を取得（例: "interact_sit" → "sit"）
        /// </summary>
        public string GetInteractAction()
        {
            if (!type.StartsWith(LLMResponseData.InteractPrefix)) return null;
            return type.Substring(LLMResponseData.InteractPrefix.Length);
        }
    }

    [Serializable]
    public class EmotionData
    {
        [Range(0f, 1f)] public float happy = 0f;
        [Range(0f, 1f)] public float relaxed = 0f;
        [Range(0f, 1f)] public float angry = 0f;
        [Range(0f, 1f)] public float sad = 0f;
        [Range(0f, 1f)] public float surprised = 0f;

        /// <summary>
        /// 最も強い感情を取得
        /// </summary>
        public EmotionType GetDominantEmotion()
        {
            float max = 0f;
            EmotionType dominant = EmotionType.Neutral;

            if (happy > max) { max = happy; dominant = EmotionType.Happy; }
            if (relaxed > max) { max = relaxed; dominant = EmotionType.Relaxed; }
            if (angry > max) { max = angry; dominant = EmotionType.Angry; }
            if (sad > max) { max = sad; dominant = EmotionType.Sad; }
            if (surprised > max) { max = surprised; dominant = EmotionType.Surprised; }

            return dominant;
        }

        /// <summary>
        /// 感情の合計値（0に近いほどニュートラル）
        /// </summary>
        public float GetTotalIntensity()
        {
            return happy + relaxed + angry + sad + surprised;
        }

        /// <summary>
        /// 上位2感情を抽出し、正規化された比率を返す
        /// ラッセルの感情円環モデルに基づくブレンドTimeline選択に使用
        /// </summary>
        /// <returns>
        /// primary: 最も強い感情, secondary: 2番目に強い感情,
        /// primaryRatio: primary / (primary + secondary) の正規化値 (0.5～1.0)
        /// </returns>
        public (EmotionType primary, EmotionType secondary, float primaryRatio) GetTopTwoEmotions()
        {
            var emotions = new (float value, EmotionType type)[]
            {
                (happy, EmotionType.Happy),
                (relaxed, EmotionType.Relaxed),
                (angry, EmotionType.Angry),
                (sad, EmotionType.Sad),
                (surprised, EmotionType.Surprised),
            };

            // 1位を探す
            int firstIdx = 0;
            for (int i = 1; i < emotions.Length; i++)
            {
                if (emotions[i].value > emotions[firstIdx].value)
                    firstIdx = i;
            }

            if (emotions[firstIdx].value <= 0f)
                return (EmotionType.Neutral, EmotionType.Neutral, 1f);

            // 2位を探す
            int secondIdx = -1;
            for (int i = 0; i < emotions.Length; i++)
            {
                if (i == firstIdx) continue;
                if (secondIdx < 0 || emotions[i].value > emotions[secondIdx].value)
                    secondIdx = i;
            }

            if (secondIdx < 0 || emotions[secondIdx].value <= 0f)
                return (emotions[firstIdx].type, EmotionType.Neutral, 1f);

            float total = emotions[firstIdx].value + emotions[secondIdx].value;
            float primaryRatio = emotions[firstIdx].value / total;

            return (emotions[firstIdx].type, emotions[secondIdx].type, primaryRatio);
        }

        /// <summary>
        /// 2つの感情がブレンド可能なペアかどうかを判定
        /// ラッセルの感情円環モデルで隣接する感情のみブレンド可能:
        /// Happy+Relaxed, Relaxed+Sad, Sad+Angry
        /// </summary>
        public static bool IsBlendablePair(EmotionType a, EmotionType b)
        {
            if (a > b) (a, b) = (b, a);

            return (a == EmotionType.Happy && b == EmotionType.Relaxed)
                || (a == EmotionType.Relaxed && b == EmotionType.Sad)
                || (a == EmotionType.Angry && b == EmotionType.Sad);
        }
    }

    public enum TargetType
    {
        Talk,       // 旧Player。RoomTarget "talk" → talk位置移動、Talk状態遷移
        Interact,
        Dynamic,
        Named       // RoomTarget（mirror, window 等のシーン内名前付きターゲット）
    }

    public enum EmotionType
    {
        Neutral,
        Happy,
        Relaxed,
        Angry,
        Sad,
        Surprised
    }
}
