using UnityEngine;
using UniVRM10;

namespace CyanNook.Character
{
    /// <summary>
    /// キャラクターの顔を照らすライトをHeadボーンに追従させるコントローラー
    /// CharacterLookAtController(20000)の後に実行し、視線反映済みのHead位置を使用する
    /// </summary>
    [DefaultExecutionOrder(20001)]
    public class CharacterFaceLightController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("顔用ライト")]
        public Light faceLight;

        [Header("Constraint Settings")]
        [Tooltip("Headボーンからのローカル位置オフセット")]
        public Vector3 positionOffset = new Vector3(0f, 0.06f, 0.3f);

        [Tooltip("回転オフセット（度）")]
        public Vector3 rotationOffset = Vector3.zero;

        [Tooltip("Headの回転に追従するか（falseの場合はワールド固定方向）")]
        public bool followHeadRotation = true;

        private Transform _headBone;
        private bool _isInitialized;

        /// <summary>
        /// VRM Instanceを設定（VRM読み込み時に呼び出し）
        /// </summary>
        public void SetVrmInstance(Vrm10Instance vrmInstance)
        {
            if (vrmInstance == null) return;

            var animator = vrmInstance.GetComponent<Animator>();
            if (animator != null)
            {
                _headBone = animator.GetBoneTransform(HumanBodyBones.Head);
            }

            if (_headBone == null)
            {
                Debug.LogWarning("[CharacterFaceLightController] Head bone not found");
                return;
            }

            _isInitialized = true;
            Debug.Log("[CharacterFaceLightController] Initialized with head bone constraint");
        }

        private void LateUpdate()
        {
            if (!_isInitialized || _headBone == null || faceLight == null) return;

            // Headボーンの位置にオフセットを加算
            faceLight.transform.position = _headBone.position + _headBone.rotation * positionOffset;

            if (followHeadRotation)
            {
                // Headの向きに追従（ロールフリー）+ 回転オフセット
                Vector3 forward = _headBone.rotation * Quaternion.Euler(rotationOffset) * Vector3.forward;
                Vector3 up = _headBone.rotation * Quaternion.Euler(rotationOffset) * Vector3.up;
                faceLight.transform.rotation = Quaternion.LookRotation(forward, up);
            }
        }
    }
}
