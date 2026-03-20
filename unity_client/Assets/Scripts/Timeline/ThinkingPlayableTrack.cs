using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace CyanNook.Timeline
{
    /// <summary>
    /// Thinking再生可能期間を定義するTrack。
    /// ThinkingPlayableClipが配置されている期間中はThinkingアニメーションを再生可能。
    ///
    /// バインディング不要（メタデータトラック）。
    /// CharacterAnimationController が Timeline のトラックを走査して
    /// 現在時刻に ThinkingPlayableClip が存在するかをチェックする。
    /// </summary>
    [TrackColor(0.4f, 0.7f, 0.9f)]  // 青系（Thinking）
    [TrackClipType(typeof(ThinkingPlayableClip))]
    public class ThinkingPlayableTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            return ScriptPlayable<ThinkingPlayableBehaviour>.Create(graph, inputCount);
        }
    }

    /// <summary>
    /// ThinkingPlayableClip/Trackで使用するBehaviour（現在は空実装）
    /// </summary>
    [System.Serializable]
    public class ThinkingPlayableBehaviour : PlayableBehaviour
    {
    }
}
