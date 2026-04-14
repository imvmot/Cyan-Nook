using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using CyanNook.Character;

namespace CyanNook.Timeline
{
    /// <summary>
    /// 加算元タイムライン（talk_thinking01 / emote_happy01 等）に配置する
    /// 「加算キャンセル＋補間」トラック。
    ///
    /// クリップ配置フレームで加算タイムラインを完全キャンセルし、
    /// 復帰先ステートに即時ループ再開しつつ、加算ボーンにIBを掛ける。
    /// クリップの長さ = 補間時間。
    ///
    /// 通常のタイムラインの「アクションキャンセル＋補間」の加算版。
    /// クリップ位置以降のタイムライン再生は破棄される。
    ///
    /// バインディング: Animator（CharacterAnimationControllerを親から取得）
    /// </summary>
    [TrackColor(0.2f, 0.6f, 0.9f)]
    [TrackClipType(typeof(AdditiveCancelClip))]
    [TrackBindingType(typeof(Animator))]
    public class AdditiveCancelTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            return ScriptPlayable<AdditiveCancelMixerBehaviour>.Create(graph, inputCount);
        }
    }

    /// <summary>
    /// AdditiveCancelClip から Playable に渡されるデータ
    /// </summary>
    [System.Serializable]
    public class AdditiveCancelBehaviour : PlayableBehaviour
    {
        public List<HumanBodyBones> overrideBones;
    }

    /// <summary>
    /// Mixer: クリップのエッジ（inactive→active）検出で1回だけ
    /// CharacterAnimationController.ForceCompleteAdditiveTimelineWithBlend を呼ぶ。
    /// </summary>
    public class AdditiveCancelMixerBehaviour : PlayableBehaviour
    {
        private bool _clipWasActive;
        private CharacterAnimationController _charCtrl;
        private AdditiveOverrideHelper _aoHelper;
        private InertialBlendHelper _ibHelper;
        private bool _resolved;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            if (!_resolved)
            {
                _resolved = true;
                if (playerData is Animator animator)
                {
                    _charCtrl = animator.GetComponentInParent<CharacterAnimationController>();
                    _aoHelper = animator.GetComponent<AdditiveOverrideHelper>();
                    _ibHelper = animator.GetComponent<InertialBlendHelper>();
                }
            }

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

            if (clipActive && !_clipWasActive && activeIndex >= 0)
            {
                var inputPlayable = (ScriptPlayable<AdditiveCancelBehaviour>)playable.GetInput(activeIndex);
                float duration = (float)inputPlayable.GetDuration();
                var behaviour = inputPlayable.GetBehaviour();

                // 補間対象ボーンを決定：
                //   クリップで明示指定があればそれを使用
                //   無ければ現在AOが管理している加算ボーンをそのまま流用
                List<HumanBodyBones> bones = null;
                if (behaviour.overrideBones != null && behaviour.overrideBones.Count > 0)
                {
                    bones = behaviour.overrideBones;
                }
                else if (_aoHelper != null && _aoHelper.CurrentAdditiveBones != null)
                {
                    bones = new List<HumanBodyBones>(_aoHelper.CurrentAdditiveBones);
                }

                if (_charCtrl != null)
                {
                    Debug.Log($"[AdditiveCancelMixerBehaviour] Cancel additive timeline, duration={duration:F3}, bones={bones?.Count ?? 0}");
                    _charCtrl.ForceCompleteAdditiveTimelineWithBlend(duration, bones);
                }
                else if (_aoHelper != null && _aoHelper.IsActive)
                {
                    // CharacterAnimationControllerが見つからない場合の保険
                    Debug.LogWarning("[AdditiveCancelMixerBehaviour] CharacterAnimationController not found, falling back to raw StopOverride");
                    _aoHelper.StopOverride(invalidateCleanPose: false);
                    if (_ibHelper != null && bones != null && bones.Count > 0)
                    {
                        _ibHelper.StartInertialBlend(duration, bones);
                    }
                }
                else
                {
                    Debug.Log("[AdditiveCancelMixerBehaviour] Clip active but no additive state to cancel — no-op");
                }
            }

            _clipWasActive = clipActive;
        }
    }
}
