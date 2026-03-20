using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace CyanNook.Timeline
{
    /// <summary>
    /// 回転補間の方向
    /// </summary>
    public enum RotationDirection
    {
        /// <summary>最短距離で補間</summary>
        Shortest,
        /// <summary>時計回り（右回転）で補間</summary>
        Clockwise,
        /// <summary>反時計回り（左回転）で補間</summary>
        CounterClockwise
    }

    /// <summary>
    /// RotationBlendクリップ
    /// ターゲット回転はPlayableDirectorのバインディングで設定
    /// </summary>
    [System.Serializable]
    public class RotationBlendClip : PlayableAsset, ITimelineClipAsset
    {
        [Tooltip("イージングカーブ")]
        public AnimationCurve blendCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Tooltip("回転方向の指定")]
        public RotationDirection rotationDirection = RotationDirection.Shortest;

        public ClipCaps clipCaps => ClipCaps.Blending;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<RotationBlendBehaviour>.Create(graph);
            var behaviour = playable.GetBehaviour();

            behaviour.blendCurve = blendCurve;
            behaviour.rotationDirection = rotationDirection;

            return playable;
        }
    }
}
