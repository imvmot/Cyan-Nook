using System;
using System.Collections.Generic;
using UnityEngine;

namespace CyanNook.Character
{
    [CreateAssetMenu(
        fileName = "TransitionRules",
        menuName = "CyanNook/Animation/Transition Rule Data",
        order = 50)]
    public class TransitionRuleData : ScriptableObject
    {
        [TextArea(2, 4)]
        public string description;

        public List<TransitionRuleEntry> rules = new List<TransitionRuleEntry>();
    }

    [Serializable]
    public class TransitionRuleEntry
    {
        [Tooltip("グルーピング用カテゴリ (Common/Talk/Emote/Interact/Sleep/Boredom等)")]
        public string category = "Common";

        [Header("From")]
        public AnimationStateType fromState;
        [Tooltip("具体的なAnimation ID (例: talk_idle01, interact_sleep01)。空なら状態全般")]
        public string fromAnimationId;

        [Header("To")]
        public AnimationStateType toState;
        public string toAnimationId;

        [Header("Trigger")]
        [Tooltip("どのコンポーネント/メソッドが遷移を発火させるか (例: TalkController.EnterTalk)")]
        public string orchestrator;
        [Tooltip("発火条件の自然言語説明 (例: LLM action=move / ユーザー入力)")]
        public string trigger;
        [Tooltip("備考・実装ファイル:行番号など")]
        [TextArea(1, 3)]
        public string notes;
    }
}
