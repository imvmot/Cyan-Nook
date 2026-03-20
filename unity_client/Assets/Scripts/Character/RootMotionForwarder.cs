using UnityEngine;

namespace CyanNook.Character
{
    /// <summary>
    /// VRMインスタンスのOnAnimatorMoveをNavigationControllerに転送
    /// AnimatorがあるGameObjectに配置する必要がある
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class RootMotionForwarder : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Root Motion処理を委譲するNavigationController")]
        public CharacterNavigationController navigationController;

        private Animator _animator;

#if UNITY_EDITOR
        // デバッグ用：ログ出力間隔制御
        private int _frameCount;
        private const int LOG_INTERVAL = 30;
#endif

        private void Awake()
        {
            _animator = GetComponent<Animator>();
        }

        /// <summary>
        /// UnityがAnimatorのRoot Motionを適用する代わりに呼び出す
        /// </summary>
        private void OnAnimatorMove()
        {
            if (_animator == null) return;

            Vector3 deltaPos = _animator.deltaPosition;
            Quaternion deltaRot = _animator.deltaRotation;

#if UNITY_EDITOR
            _frameCount++;
            if (_frameCount % LOG_INTERVAL == 0)
            {
                Debug.Log($"[RootMotionForwarder] applyRootMotion: {_animator.applyRootMotion}, deltaPos: {deltaPos}, deltaRot: {deltaRot.eulerAngles}");
            }
#endif

            if (navigationController == null) return;

            // NavigationControllerに処理を委譲
            navigationController.ApplyRootMotion(deltaPos, deltaRot);
        }
    }
}
