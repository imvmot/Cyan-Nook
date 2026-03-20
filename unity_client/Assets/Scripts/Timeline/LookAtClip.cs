using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace CyanNook.Timeline
{
    /// <summary>
    /// LookAtクリップ
    /// Eye / Head / Chest の視線制御パラメータを保持
    /// </summary>
    [System.Serializable]
    public class LookAtClip : PlayableAsset, ITimelineClipAsset
    {
        [Header("Eye")]
        [Tooltip("VRM LookAt（目）の追従を有効にする")]
        public bool enableEye = true;

        [Header("Head")]
        [Tooltip("頭ボーンの追従を有効にする")]
        public bool enableHead = true;

        [Tooltip("頭の上下方向の制限角度")]
        public float headAngleLimitX = 45f;

        [Tooltip("頭の左右方向の制限角度")]
        public float headAngleLimitY = 45f;

        [Header("Chest")]
        [Tooltip("胸ボーンの追従を有効にする")]
        public bool enableChest = false;

        [Tooltip("胸の上下方向の制限角度")]
        public float chestAngleLimitX = 10f;

        [Tooltip("胸の左右方向の制限角度")]
        public float chestAngleLimitY = 10f;

        [Header("Blend")]
        [Tooltip("LookAt有効/無効の切り替え時の補間フレーム数")]
        public int blendFrames = 60;

        public ClipCaps clipCaps => ClipCaps.Blending;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<LookAtBehaviour>.Create(graph);
            var behaviour = playable.GetBehaviour();

            behaviour.enableEye = enableEye;
            behaviour.enableHead = enableHead;
            behaviour.headAngleLimitX = headAngleLimitX;
            behaviour.headAngleLimitY = headAngleLimitY;
            behaviour.enableChest = enableChest;
            behaviour.chestAngleLimitX = chestAngleLimitX;
            behaviour.chestAngleLimitY = chestAngleLimitY;
            behaviour.blendFrames = blendFrames;

            return playable;
        }
    }
}
