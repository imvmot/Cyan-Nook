using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace CyanNook.Timeline
{
    /// <summary>
    /// アニメーション切り替え時の慣性補間（Inertial Blending）を行うトラック
    /// バインディング: Animator
    ///
    /// このトラックはTimeline上でのクリップ配置とブレンド時間の定義を担当。
    /// 実際のボーン操作はInertialBlendHelper（MonoBehaviour, LateUpdate）で行う。
    /// </summary>
    [TrackColor(0.8f, 0.4f, 0.2f)]
    [TrackClipType(typeof(InertialBlendClip))]
    [TrackBindingType(typeof(Animator))]
    public class InertialBlendTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            return ScriptPlayable<InertialBlendMixerBehaviour>.Create(graph, inputCount);
        }
    }

    /// <summary>
    /// Inertial Blend PlayableBehaviour
    /// </summary>
    [System.Serializable]
    public class InertialBlendBehaviour : PlayableBehaviour
    {
        public System.Collections.Generic.List<HumanBodyBones> targetBones;
    }

    /// <summary>
    /// Inertial Blend Mixer Behaviour
    /// Timeline再生中にクリップ位置に到達したらInertialBlendHelperを起動する。
    /// director.Evaluate()経由でProcessFrameが呼ばれ、クリップ位置到達時にIBを開始する。
    /// </summary>
    public class InertialBlendMixerBehaviour : PlayableBehaviour
    {
        private bool _clipWasActive;
        private InertialBlendHelper _helper;
        private bool _helperResolved;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            // InertialBlendHelperの参照をバインディングのAnimatorから取得
            if (!_helperResolved)
            {
                _helperResolved = true;
                if (playerData is Animator animator)
                {
                    _helper = animator.GetComponent<InertialBlendHelper>();
                }
            }
            if (_helper == null) return;

            // いずれかのクリップがアクティブか判定
            int inputCount = playable.GetInputCount();
            bool clipActive = false;
            int activeIndex = -1;
            for (int i = 0; i < inputCount; i++)
            {
                if (playable.GetInputWeight(i) > 0f)
                {
                    clipActive = true;
                    activeIndex = i;
                    break;
                }
            }

            // クリップが非アクティブ→アクティブに変わった瞬間にIBを開始
            if (clipActive && !_clipWasActive && activeIndex >= 0)
            {
                var inputPlayable = (ScriptPlayable<InertialBlendBehaviour>)playable.GetInput(activeIndex);
                // クリップのdurationとtargetBonesを取得するため、
                // トラックのクリップ情報はMixerからは直接アクセスできないが、
                // inputPlayableのdurationでクリップ長を取得可能
                float duration = (float)inputPlayable.GetDuration();

                // IBがすでにアクティブなら重複開始しない
                // （同一フレーム内で既にIBが開始されている場合は重複開始しない）
                if (!_helper.IsActive)
                {
                    var behaviour = inputPlayable.GetBehaviour();
                    _helper.StartInertialBlend(duration, behaviour.targetBones);
                    Debug.Log($"[InertialBlendMixerBehaviour] Started IB from Timeline clip, duration={duration:F3}");
                }
            }

            _clipWasActive = clipActive;
        }
    }
}
