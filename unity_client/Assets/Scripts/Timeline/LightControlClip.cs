using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Scripting;
using UnityEngine.Timeline;

namespace CyanNook.Timeline
{
    /// <summary>
    /// LightControl PlayableAsset（クリップ）
    /// </summary>
    [System.Serializable]
    [Preserve]
    public class LightControlClip : PlayableAsset, ITimelineClipAsset
    {
        [Tooltip("ONにするかOFFにするか")]
        public bool lightsOn = true;

        public ClipCaps clipCaps => ClipCaps.None;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<LightControlBehaviour>.Create(graph);
            var behaviour = playable.GetBehaviour();
            behaviour.lightsOn = lightsOn;
            return playable;
        }
    }
}
