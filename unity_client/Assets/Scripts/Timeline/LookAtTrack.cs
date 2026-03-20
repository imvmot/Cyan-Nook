using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using CyanNook.Character;

namespace CyanNook.Timeline
{
    /// <summary>
    /// 視線制御トラック
    /// Clipが存在する期間のみLookAtを有効化する
    /// </summary>
    [TrackColor(0.2f, 0.8f, 0.8f)]
    [TrackClipType(typeof(LookAtClip))]
    [TrackBindingType(typeof(CharacterLookAtController))]
    public class LookAtTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            return ScriptPlayable<LookAtMixerBehaviour>.Create(graph, inputCount);
        }
    }

    /// <summary>
    /// LookAt PlayableBehaviour（クリップごとのパラメータ保持）
    /// </summary>
    [System.Serializable]
    public class LookAtBehaviour : PlayableBehaviour
    {
        public bool enableEye;
        public bool enableHead;
        public float headAngleLimitX;
        public float headAngleLimitY;
        public bool enableChest;
        public float chestAngleLimitX;
        public float chestAngleLimitY;
        public int blendFrames;
    }

    /// <summary>
    /// LookAt Mixer Behaviour
    /// 全クリップのweightを評価し、CharacterLookAtControllerに設定を渡す
    /// </summary>
    public class LookAtMixerBehaviour : PlayableBehaviour
    {
        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            var lookAtController = playerData as CharacterLookAtController;
            if (lookAtController == null) return;

            int inputCount = playable.GetInputCount();
            float totalWeight = 0f;

            bool eyeEnabled = false;
            bool headEnabled = false;
            bool chestEnabled = false;
            float headLimitX = 0f, headLimitY = 0f;
            float chestLimitX = 0f, chestLimitY = 0f;
            int blendFrames = 60;

            // 最もweightの高いクリップの設定を採用
            float maxWeight = 0f;
            for (int i = 0; i < inputCount; i++)
            {
                float weight = playable.GetInputWeight(i);
                totalWeight += weight;

                if (weight > maxWeight)
                {
                    maxWeight = weight;
                    var input = (ScriptPlayable<LookAtBehaviour>)playable.GetInput(i);
                    var behaviour = input.GetBehaviour();

                    eyeEnabled = behaviour.enableEye;
                    headEnabled = behaviour.enableHead;
                    headLimitX = behaviour.headAngleLimitX;
                    headLimitY = behaviour.headAngleLimitY;
                    chestEnabled = behaviour.enableChest;
                    chestLimitX = behaviour.chestAngleLimitX;
                    chestLimitY = behaviour.chestAngleLimitY;
                    blendFrames = behaviour.blendFrames;
                }
            }

            float clampedWeight = Mathf.Clamp01(totalWeight);

            lookAtController.SetTimelineLookAtState(
                active: clampedWeight > 0f,
                weight: clampedWeight,
                eye: eyeEnabled,
                head: headEnabled,
                headLimitX: headLimitX,
                headLimitY: headLimitY,
                chest: chestEnabled,
                chestLimitX: chestLimitX,
                chestLimitY: chestLimitY,
                blendFrames: blendFrames
            );
        }
    }
}
