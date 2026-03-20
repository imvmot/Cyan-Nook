using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace CyanNook.Timeline
{
    /// <summary>
    /// インタラクション中のコライダー制御トラック
    /// </summary>
    [TrackColor(0.8f, 0.2f, 0.2f)]
    [TrackClipType(typeof(ColliderControlClip))]
    [TrackBindingType(typeof(Collider))]
    public class ColliderControlTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            return ScriptPlayable<ColliderControlMixerBehaviour>.Create(graph, inputCount);
        }
    }

    /// <summary>
    /// ColliderControl PlayableBehaviour
    /// </summary>
    [System.Serializable]
    public class ColliderControlBehaviour : PlayableBehaviour
    {
        public bool disableCollider = true;

        private Collider _targetCollider;
        private bool _originalEnabled;
        private bool _applied;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            var collider = playerData as Collider;
            if (collider == null) return;

            // 初回のみ元の状態を記録
            if (!_applied)
            {
                _targetCollider = collider;
                _originalEnabled = collider.enabled;
                _applied = true;
            }

            // コライダーの有効/無効を設定
            float weight = info.weight;
            if (weight > 0.5f)
            {
                collider.enabled = !disableCollider;
            }
        }

        public override void OnBehaviourPause(Playable playable, FrameData info)
        {
            // クリップ終了時、元の状態に戻す
            if (_applied && _targetCollider != null)
            {
                _targetCollider.enabled = _originalEnabled;
                _applied = false;
            }
        }

        public override void OnPlayableDestroy(Playable playable)
        {
            // Playable破棄時にも元の状態に戻す
            if (_applied && _targetCollider != null)
            {
                _targetCollider.enabled = _originalEnabled;
            }
        }
    }

    /// <summary>
    /// ColliderControl Mixer Behaviour
    /// </summary>
    public class ColliderControlMixerBehaviour : PlayableBehaviour
    {
        private Collider _boundCollider;
        private bool _originalEnabled;
        private bool _hasStoredOriginal;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            var collider = playerData as Collider;
            if (collider == null) return;

            // 元の状態を記録
            if (!_hasStoredOriginal)
            {
                _boundCollider = collider;
                _originalEnabled = collider.enabled;
                _hasStoredOriginal = true;
            }

            int inputCount = playable.GetInputCount();
            bool shouldDisable = false;

            for (int i = 0; i < inputCount; i++)
            {
                float inputWeight = playable.GetInputWeight(i);
                if (inputWeight > 0.5f)
                {
                    var inputPlayable = (ScriptPlayable<ColliderControlBehaviour>)playable.GetInput(i);
                    var behaviour = inputPlayable.GetBehaviour();

                    if (behaviour.disableCollider)
                    {
                        shouldDisable = true;
                        break;
                    }
                }
            }

            collider.enabled = !shouldDisable;
        }

        public override void OnPlayableDestroy(Playable playable)
        {
            // 元の状態に戻す
            if (_hasStoredOriginal && _boundCollider != null)
            {
                _boundCollider.enabled = _originalEnabled;
            }
        }
    }
}
