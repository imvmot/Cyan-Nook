using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace CyanNook.Timeline
{
    /// <summary>
    /// PositionBlendクリップ
    /// ターゲット位置はPlayableDirectorのバインディングで設定
    /// </summary>
    [System.Serializable]
    public class PositionBlendClip : PlayableAsset, ITimelineClipAsset
    {
        [Tooltip("イージングカーブ")]
        public AnimationCurve blendCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        public ClipCaps clipCaps => ClipCaps.Blending;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<PositionBlendBehaviour>.Create(graph);
            var behaviour = playable.GetBehaviour();

            behaviour.blendCurve = blendCurve;

            return playable;
        }
    }
}
