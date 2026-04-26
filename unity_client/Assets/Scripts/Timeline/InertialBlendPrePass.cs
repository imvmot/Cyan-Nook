using UnityEngine;

namespace CyanNook.Timeline
{
    /// <summary>
    /// InertialBlendHelperのWaitingFirstFrameポーズ補正 + AdditiveOverrideHelperの
    /// 非加算ボーン復元を、FastSpringBoneService(11010)の前にまとめて適用するプリパス。
    ///
    /// 実行順序の問題:
    /// - Vrm10Instance(11000): ControlRigがAnimator出力をボーンに適用
    /// - FastSpringBoneService(11010): SpringBoneがボーン位置で計算
    /// - InertialBlendHelper(20000): WaitingFirstFrameでポーズ補正
    /// - AdditiveOverrideHelper(20050): 非加算ボーンをsnapshotに復元
    ///
    /// IB(20000)・AO(20050)の補正がSpringBone(11010)より後に実行されるため、
    /// SpringBoneが「Animatorの生ポーズ」で物理計算してしまい、最終の「見えているポーズ」との
    /// 乖離が生じる。特に加算thinking/emote中は、非加算ボーン(Hips/Spine/Legs等)が
    /// AO復元前のAnimator raw(thinkingアニメの波形)で動いて見えるため、SpringBoneが
    /// 見えないHipsの動きに反応し、jump時に大きなホップが発生する。
    ///
    /// 解決: PrePass(11005)で以下を順に実行し、SpringBoneに「最終見た目と一致するポーズ」を渡す:
    ///   1. InertialBlendHelper.ApplyPrePassIfNeeded() ... 全52ボーンにIB補間済みポーズを適用
    ///   2. AdditiveOverrideHelper.ApplyRestoreNow()   ... 非加算10ボーンをsnapshotに上書き
    /// </summary>
    [DefaultExecutionOrder(11005)]
    public class InertialBlendPrePass : MonoBehaviour
    {
        [Header("References")]
        public InertialBlendHelper inertialBlendHelper;
        public AdditiveOverrideHelper additiveOverrideHelper;

        private void LateUpdate()
        {
            if (inertialBlendHelper != null)
            {
                inertialBlendHelper.ApplyPrePassIfNeeded();
            }

            // IB適用後にAO復元を適用する。順序が重要:
            // - IB(全52): 加算ボーン=IB補間、非加算ボーン=previous(=Animator raw由来)
            // - AO(10):  非加算ボーンをsnapshot(=見えているsit pose)で上書き
            // これにより FastSpringBone(11010) が見るポーズが最終render時と一致する。
            if (additiveOverrideHelper != null)
            {
                additiveOverrideHelper.ApplyRestoreNow();
            }
        }
    }
}
