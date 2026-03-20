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
    }

    /// <summary>
    /// Inertial Blend Mixer Behaviour
    /// Timeline上でのクリップ状態を管理する。ボーン操作は行わない。
    /// </summary>
    public class InertialBlendMixerBehaviour : PlayableBehaviour
    {
    }
}
