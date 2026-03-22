using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Scripting;
using UnityEngine.Timeline;
using CyanNook.Furniture;

namespace CyanNook.Timeline
{
    /// <summary>
    /// RoomLightControllerのON/OFFをTimelineクリップで制御するトラック
    /// </summary>
    [TrackColor(1.0f, 0.9f, 0.3f)]
    [TrackClipType(typeof(LightControlClip))]
    [TrackBindingType(typeof(RoomLightController))]
    [Preserve]
    public class LightControlTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            return ScriptPlayable<LightControlMixerBehaviour>.Create(graph, inputCount);
        }
    }
}
