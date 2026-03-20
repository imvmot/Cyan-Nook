using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace CyanNook.Timeline
{
    /// <summary>
    /// BlendPivotのワールド回転を補間するトラック
    /// バインディング: BlendPivot Transform
    ///
    /// BlendPivot方式（新）:
    /// - BlendPivotはVRMの親として機能
    /// - インタラクション開始時、BlendPivotをVRMの現在位置に移動
    /// - VRMのローカル座標をリセット（0,0,0 / identity）
    /// - BlendPivotのワールド回転を current → target に補間
    /// - VRMはローカル座標でRootMotionを適用
    /// </summary>
    [TrackColor(0.2f, 0.2f, 0.8f)]
    [TrackClipType(typeof(RotationBlendClip))]
    [TrackBindingType(typeof(Transform))]
    public class RotationBlendTrack : TrackAsset
    {
        /// <summary>
        /// ターゲット回転（実行時に設定）
        /// </summary>
        [System.NonSerialized]
        public Quaternion targetRotation;

        /// <summary>
        /// ターゲットが設定されているか
        /// </summary>
        [System.NonSerialized]
        public bool hasTarget;

        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            var mixer = ScriptPlayable<RotationBlendMixerBehaviour>.Create(graph, inputCount);
            var behaviour = mixer.GetBehaviour();
            behaviour.targetRotation = targetRotation;
            behaviour.hasTarget = hasTarget;
            return mixer;
        }
    }

    /// <summary>
    /// RotationBlend PlayableBehaviour
    /// BlendPivotのワールド回転を current → target に補間
    /// Y軸回転の方向を指定可能（最短/時計回り/反時計回り）
    /// </summary>
    [System.Serializable]
    public class RotationBlendBehaviour : PlayableBehaviour
    {
        [HideInInspector]
        public AnimationCurve blendCurve;

        [HideInInspector]
        public Quaternion targetRotation;

        [HideInInspector]
        public bool hasTarget;

        [HideInInspector]
        public RotationDirection rotationDirection;

        // 内部状態
        private bool _initialized;
        private Transform _blendPivot;
        private Vector3 _startEuler;
        private Vector3 _targetEuler;
        private float _yDelta; // Y軸の回転差分（方向考慮済み）

        public override void OnGraphStart(Playable playable)
        {
            _initialized = false;
        }

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            var blendPivot = playerData as Transform;
            if (blendPivot == null) return;
            if (!hasTarget) return;

            // 初回のみ初期化
            if (!_initialized)
            {
                _blendPivot = blendPivot;
                _startEuler = blendPivot.rotation.eulerAngles;
                _targetEuler = targetRotation.eulerAngles;

                // Y軸の回転差分を計算（回転方向を考慮）
                _yDelta = CalculateYDelta(_startEuler.y, _targetEuler.y, rotationDirection);

                _initialized = true;
                Debug.Log($"[RotationBlendBehaviour] Initialized: start={_startEuler}, target={_targetEuler}, yDelta={_yDelta}, direction={rotationDirection}");
            }

            // 正規化時間を取得
            double time = playable.GetTime();
            double duration = playable.GetDuration();
            float normalizedTime = duration > 0 ? (float)(time / duration) : 1f;

            // カーブからブレンド値を取得
            float blend = blendCurve != null ? blendCurve.Evaluate(normalizedTime) : normalizedTime;

            // Y軸は回転方向を考慮した補間
            float newY = _startEuler.y + _yDelta * blend;

            // X/Z軸は線形補間（通常は小さい値）
            float newX = Mathf.LerpAngle(_startEuler.x, _targetEuler.x, blend);
            float newZ = Mathf.LerpAngle(_startEuler.z, _targetEuler.z, blend);

            _blendPivot.rotation = Quaternion.Euler(newX, newY, newZ);
        }

        public override void OnBehaviourPause(Playable playable, FrameData info)
        {
            // クリップ終了時、ターゲット回転に確実に設定
            if (_initialized && _blendPivot != null && hasTarget)
            {
                _blendPivot.rotation = targetRotation;
                Debug.Log($"[RotationBlendBehaviour] Clip ended: BlendPivot set to target {targetRotation.eulerAngles}");
            }
        }

        /// <summary>
        /// Y軸の回転差分を計算（回転方向を考慮）
        /// </summary>
        /// <param name="startY">開始角度（0-360）</param>
        /// <param name="targetY">終了角度（0-360）</param>
        /// <param name="direction">回転方向</param>
        /// <returns>回転差分（正:時計回り、負:反時計回り）</returns>
        private float CalculateYDelta(float startY, float targetY, RotationDirection direction)
        {
            // 基本の差分を計算
            float delta = targetY - startY;

            // -180〜180の範囲に正規化（最短距離の計算）
            while (delta > 180f) delta -= 360f;
            while (delta < -180f) delta += 360f;

            switch (direction)
            {
                case RotationDirection.Shortest:
                    // そのまま（-180〜180の範囲）
                    break;

                case RotationDirection.Clockwise:
                    // 時計回り（正の値）に強制
                    // delta <= 0 の場合、360を足して正にする
                    if (delta <= 0f)
                    {
                        delta += 360f;
                    }
                    break;

                case RotationDirection.CounterClockwise:
                    // 反時計回り（負の値）に強制
                    // delta >= 0 の場合、360を引いて負にする
                    if (delta >= 0f)
                    {
                        delta -= 360f;
                    }
                    break;
            }

            return delta;
        }
    }

    /// <summary>
    /// RotationBlend Mixer Behaviour
    /// </summary>
    public class RotationBlendMixerBehaviour : PlayableBehaviour
    {
        public Quaternion targetRotation;
        public bool hasTarget;

        public override void OnGraphStart(Playable playable)
        {
            // 子のBehaviourにパラメータを伝播
            int inputCount = playable.GetInputCount();
            for (int i = 0; i < inputCount; i++)
            {
                var inputPlayable = playable.GetInput(i);
                if (inputPlayable.IsValid() && inputPlayable.GetPlayableType() == typeof(RotationBlendBehaviour))
                {
                    var behaviour = ((ScriptPlayable<RotationBlendBehaviour>)inputPlayable).GetBehaviour();
                    behaviour.targetRotation = targetRotation;
                    behaviour.hasTarget = hasTarget;
                    // rotationDirection は CreatePlayable 時に設定済み（Clip側で設定）
                }
            }
        }

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            // 処理はBehaviour側で行う
        }
    }
}
