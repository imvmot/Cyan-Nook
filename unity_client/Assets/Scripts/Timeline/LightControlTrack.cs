using UnityEngine;
using UnityEngine.Playables;
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
    public class LightControlTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            return ScriptPlayable<LightControlMixerBehaviour>.Create(graph, inputCount);
        }
    }

    /// <summary>
    /// LightControl PlayableAsset（クリップ）
    /// </summary>
    [System.Serializable]
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

    /// <summary>
    /// LightControl PlayableBehaviour
    /// </summary>
    [System.Serializable]
    public class LightControlBehaviour : PlayableBehaviour
    {
        public bool lightsOn;
    }

    /// <summary>
    /// LightControl Mixer Behaviour
    /// クリップ終了後も状態を維持する（復元しない）
    /// </summary>
    public class LightControlMixerBehaviour : PlayableBehaviour
    {
        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            var controller = playerData as RoomLightController;
            if (controller == null) return;

            int inputCount = playable.GetInputCount();

            for (int i = 0; i < inputCount; i++)
            {
                float inputWeight = playable.GetInputWeight(i);
                if (inputWeight > 0.5f)
                {
                    var inputPlayable = (ScriptPlayable<LightControlBehaviour>)playable.GetInput(i);
                    var behaviour = inputPlayable.GetBehaviour();

                    controller.SetLights(behaviour.lightsOn);
                    break;
                }
            }
        }
    }
}
