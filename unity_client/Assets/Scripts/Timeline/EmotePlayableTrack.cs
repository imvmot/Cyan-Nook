using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace CyanNook.Timeline
{
    /// <summary>
    /// エモート再生可能期間を定義するTrack。
    /// EmotePlayableClipが配置されている期間中はエモートアニメーションを再生可能。
    ///
    /// バインディング不要（メタデータトラック）。
    /// CharacterAnimationController が Timeline のトラックを走査して
    /// 現在時刻に EmotePlayableClip が存在するかをチェックする。
    /// </summary>
    [TrackColor(0.9f, 0.6f, 0.2f)]  // オレンジ系
    [TrackClipType(typeof(EmotePlayableClip))]
    public class EmotePlayableTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            return ScriptPlayable<EmotePlayableBehaviour>.Create(graph, inputCount);
        }
    }

    /// <summary>
    /// EmotePlayableClip/Trackで使用するBehaviour（現在は空実装）
    /// </summary>
    [System.Serializable]
    public class EmotePlayableBehaviour : PlayableBehaviour
    {
    }
}
