using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace CyanNook.Timeline
{
    /// <summary>
    /// ColliderControlクリップ
    /// </summary>
    [System.Serializable]
    public class ColliderControlClip : PlayableAsset, ITimelineClipAsset
    {
        [Tooltip("コライダーを無効化するか")]
        public bool disableCollider = true;

        public ClipCaps clipCaps => ClipCaps.None;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<ColliderControlBehaviour>.Create(graph);
            var behaviour = playable.GetBehaviour();

            behaviour.disableCollider = disableCollider;

            return playable;
        }
    }
}
