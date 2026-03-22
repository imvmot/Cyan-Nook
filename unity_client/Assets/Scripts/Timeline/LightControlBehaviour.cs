using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Scripting;
using CyanNook.Furniture;

namespace CyanNook.Timeline
{
    /// <summary>
    /// LightControl PlayableBehaviour（クリップ単位のデータ）
    /// </summary>
    [System.Serializable]
    [Preserve]
    public class LightControlBehaviour : PlayableBehaviour
    {
        public bool lightsOn;
    }

    /// <summary>
    /// LightControl Mixer Behaviour
    /// クリップ終了後も状態を維持する（復元しない）
    /// </summary>
    [Preserve]
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
