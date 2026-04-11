using UnityEngine;

namespace CyanNook.Timeline
{
    /// <summary>
    /// InertialBlendHelperのWaitingFirstFrameポーズ補正を
    /// FastSpringBoneService(11010)の前に適用するプリパス。
    ///
    /// 実行順序の問題:
    /// - Vrm10Instance(11000): ControlRigがAnimator出力をボーンに適用（上書き）
    /// - FastSpringBoneService(11010): SpringBoneがボーン位置で計算
    /// - InertialBlendHelper(20000): WaitingFirstFrameでポーズ補正
    ///
    /// IB(20000)の補正がSpringBone(11010)より後に実行されるため、
    /// SpringBoneが新Timelineの未補正ポーズで計算してしまい、髪・揺れ物がポップする。
    /// また、10999ではVrm10Instance(11000)のControlRigが上書きするため無効。
    ///
    /// 解決: Vrm10Instance(11000)とFastSpringBoneService(11010)の間(11005)で実行し、
    /// ControlRig適用後・SpringBone計算前にポーズを補正する。
    /// </summary>
    [DefaultExecutionOrder(11005)]
    public class InertialBlendPrePass : MonoBehaviour
    {
        [Header("References")]
        public InertialBlendHelper inertialBlendHelper;

        private void LateUpdate()
        {
            if (inertialBlendHelper != null)
            {
                inertialBlendHelper.ApplyPrePassIfNeeded();
            }
        }
    }
}
