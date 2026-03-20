using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using System.Collections.Generic;

namespace CyanNook.Timeline
{
    /// <summary>
    /// Thinking再生可能期間を定義するクリップ。
    /// このクリップが配置されている期間中はThinkingアニメーションを再生可能。
    ///
    /// 配置例:
    /// - common_idle01 の lp 区間に配置 → Idle中にThinking再生可能
    /// - talk_idle01 の lp 区間に配置 → Talk中にThinking再生可能
    /// - interact_*_lp に配置し、additiveBones で上半身のみThinkingを加算適用
    /// </summary>
    [System.Serializable]
    public class ThinkingPlayableClip : PlayableAsset, ITimelineClipAsset
    {
        [Header("Additive Bones")]
        [Tooltip("加算アニメーション時に上書きするボーンリスト。空の場合は全身置き換え。")]
        [HumanBoneSelect]
        public List<HumanBodyBones> additiveBones = new List<HumanBodyBones>();

        public ClipCaps clipCaps => ClipCaps.None;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            return ScriptPlayable<ThinkingPlayableBehaviour>.Create(graph);
        }
    }
}
