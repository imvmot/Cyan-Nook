using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace CyanNook.Timeline
{
    /// <summary>
    /// BlendPivotのワールド位置を補間するトラック
    /// バインディング: BlendPivot Transform
    /// ターゲット位置は実行時にInteractionControllerから設定
    ///
    /// BlendPivot方式（新）:
    /// - BlendPivotはVRMの親として機能
    /// - インタラクション開始時、BlendPivotをVRMの現在位置に移動
    /// - VRMのローカル座標をリセット（0,0,0 / identity）
    /// - BlendPivotのワールド位置を current → target に補間
    /// - VRMはローカル座標でRootMotionを適用
    /// </summary>
    [TrackColor(0.2f, 0.8f, 0.2f)]
    [TrackClipType(typeof(PositionBlendClip))]
    [TrackBindingType(typeof(Transform))]
    public class PositionBlendTrack : TrackAsset
    {
        /// <summary>
        /// ターゲット位置（実行時に設定）
        /// </summary>
        [System.NonSerialized]
        public Vector3 targetPosition;

        /// <summary>
        /// ターゲットが設定されているか
        /// </summary>
        [System.NonSerialized]
        public bool hasTarget;

        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            var mixer = ScriptPlayable<PositionBlendMixerBehaviour>.Create(graph, inputCount);
            var behaviour = mixer.GetBehaviour();
            behaviour.targetPosition = targetPosition;
            behaviour.hasTarget = hasTarget;
            return mixer;
        }
    }

    /// <summary>
    /// PositionBlend PlayableBehaviour
    /// BlendPivotのワールド位置を current → target に補間
    /// </summary>
    [System.Serializable]
    public class PositionBlendBehaviour : PlayableBehaviour
    {
        [HideInInspector]
        public AnimationCurve blendCurve;

        [HideInInspector]
        public Vector3 targetPosition;

        [HideInInspector]
        public bool hasTarget;

        // 内部状態
        private Vector3 _startPosition;
        private bool _initialized;
        private Transform _blendPivot;

        public override void OnGraphStart(Playable playable)
        {
            _initialized = false;
        }

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            var blendPivot = playerData as Transform;
            if (blendPivot == null) return;
            if (!hasTarget) return;

            // 初回のみ開始位置を記録
            if (!_initialized)
            {
                _blendPivot = blendPivot;
                _startPosition = blendPivot.position;
                _initialized = true;
                Debug.Log($"[PositionBlendBehaviour] Initialized: start={_startPosition}, target={targetPosition}");
            }

            // 正規化時間を取得
            double time = playable.GetTime();
            double duration = playable.GetDuration();
            float normalizedTime = duration > 0 ? (float)(time / duration) : 1f;

            // カーブからブレンド値を取得
            float blend = blendCurve != null ? blendCurve.Evaluate(normalizedTime) : normalizedTime;

            // ワールド位置を補間
            Vector3 newPosition = Vector3.Lerp(_startPosition, targetPosition, blend);
            blendPivot.position = newPosition;
        }

        public override void OnBehaviourPause(Playable playable, FrameData info)
        {
            // クリップ終了時、ターゲット位置に確実に配置
            if (_initialized && _blendPivot != null && hasTarget)
            {
                _blendPivot.position = targetPosition;
                Debug.Log($"[PositionBlendBehaviour] Clip ended: BlendPivot set to target {targetPosition}");
            }
        }
    }

    /// <summary>
    /// PositionBlend Mixer Behaviour
    /// </summary>
    public class PositionBlendMixerBehaviour : PlayableBehaviour
    {
        public Vector3 targetPosition;
        public bool hasTarget;

        public override void OnGraphStart(Playable playable)
        {
            // 子のBehaviourにターゲットを伝播
            int inputCount = playable.GetInputCount();
            for (int i = 0; i < inputCount; i++)
            {
                var inputPlayable = playable.GetInput(i);
                if (inputPlayable.IsValid() && inputPlayable.GetPlayableType() == typeof(PositionBlendBehaviour))
                {
                    var behaviour = ((ScriptPlayable<PositionBlendBehaviour>)inputPlayable).GetBehaviour();
                    behaviour.targetPosition = targetPosition;
                    behaviour.hasTarget = hasTarget;
                }
            }
        }

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            // 処理はBehaviour側で行う
        }
    }
}
