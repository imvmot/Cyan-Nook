using UnityEngine;
using CyanNook.Core;

namespace CyanNook.Character
{
    /// <summary>
    /// 手に持つアイテム用のツールポイントを制御
    /// VRM読み込み後に動的にアタッチポイントを生成
    /// </summary>
    public class ToolPointController : MonoBehaviour
    {
        [Header("References")]
        public CharacterTemplateData templateData;
        public Animator animator;

        [Header("Tool Points")]
        [SerializeField]
        private Transform _toolPointLeft;
        public Transform ToolPointLeft => _toolPointLeft;

        [SerializeField]
        private Transform _toolPointRight;
        public Transform ToolPointRight => _toolPointRight;

        [Header("Current Items")]
        [SerializeField]
        private GameObject _leftHandItem;

        [SerializeField]
        private GameObject _rightHandItem;

        [Header("Animation")]
        [SerializeField]
        private Animation _toolAnimationLeft;

        [SerializeField]
        private Animation _toolAnimationRight;

        /// <summary>
        /// ツールポイントを初期化（VRM読み込み後に呼び出し）
        /// </summary>
        public void Initialize(Animator vrmAnimator)
        {
            animator = vrmAnimator;

            if (animator == null)
            {
                Debug.LogError("[ToolPointController] Animator is null");
                return;
            }

            // 左手ツールポイント作成
            Transform leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            if (leftHand != null)
            {
                _toolPointLeft = CreateToolPoint(leftHand, "ToolPoint_L",
                    templateData?.toolPointLeftOffset ?? new Vector3(0.05f, 0, 0),
                    templateData?.toolPointLeftRotation ?? Vector3.zero);
            }

            // 右手ツールポイント作成
            Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (rightHand != null)
            {
                _toolPointRight = CreateToolPoint(rightHand, "ToolPoint_R",
                    templateData?.toolPointRightOffset ?? new Vector3(-0.05f, 0, 0),
                    templateData?.toolPointRightRotation ?? Vector3.zero);
            }

            Debug.Log("[ToolPointController] Tool points initialized");
        }

        private Transform CreateToolPoint(Transform parent, string name, Vector3 offset, Vector3 rotation)
        {
            GameObject toolPoint = new GameObject(name);
            toolPoint.transform.SetParent(parent);
            toolPoint.transform.localPosition = offset;
            toolPoint.transform.localRotation = Quaternion.Euler(rotation);
            toolPoint.transform.localScale = Vector3.one;

            // Animation コンポーネントを追加（ツールアニメーション用）
            Animation anim = toolPoint.AddComponent<Animation>();
            if (name.Contains("L"))
            {
                _toolAnimationLeft = anim;
            }
            else
            {
                _toolAnimationRight = anim;
            }

            return toolPoint.transform;
        }

        /// <summary>
        /// 左手にアイテムを持たせる
        /// </summary>
        public void AttachToLeftHand(GameObject item)
        {
            if (_toolPointLeft == null)
            {
                Debug.LogWarning("[ToolPointController] Left tool point not initialized");
                return;
            }

            DetachFromLeftHand();

            _leftHandItem = item;
            item.transform.SetParent(_toolPointLeft);
            item.transform.localPosition = Vector3.zero;
            item.transform.localRotation = Quaternion.identity;
        }

        /// <summary>
        /// 右手にアイテムを持たせる
        /// </summary>
        public void AttachToRightHand(GameObject item)
        {
            if (_toolPointRight == null)
            {
                Debug.LogWarning("[ToolPointController] Right tool point not initialized");
                return;
            }

            DetachFromRightHand();

            _rightHandItem = item;
            item.transform.SetParent(_toolPointRight);
            item.transform.localPosition = Vector3.zero;
            item.transform.localRotation = Quaternion.identity;
        }

        /// <summary>
        /// 左手のアイテムを外す
        /// </summary>
        public void DetachFromLeftHand()
        {
            if (_leftHandItem != null)
            {
                _leftHandItem.transform.SetParent(null);
                _leftHandItem = null;
            }
        }

        /// <summary>
        /// 右手のアイテムを外す
        /// </summary>
        public void DetachFromRightHand()
        {
            if (_rightHandItem != null)
            {
                _rightHandItem.transform.SetParent(null);
                _rightHandItem = null;
            }
        }

        /// <summary>
        /// ツールアニメーションを再生（左手）
        /// </summary>
        public void PlayToolAnimationLeft(AnimationClip clip)
        {
            if (_toolAnimationLeft == null || clip == null) return;

            _toolAnimationLeft.clip = clip;
            _toolAnimationLeft.Play();
        }

        /// <summary>
        /// ツールアニメーションを再生（右手）
        /// </summary>
        public void PlayToolAnimationRight(AnimationClip clip)
        {
            if (_toolAnimationRight == null || clip == null) return;

            _toolAnimationRight.clip = clip;
            _toolAnimationRight.Play();
        }

        /// <summary>
        /// オフセットを調整（実行時）
        /// </summary>
        public void AdjustLeftHandOffset(Vector3 offset, Vector3 rotation)
        {
            if (_toolPointLeft != null)
            {
                _toolPointLeft.localPosition = offset;
                _toolPointLeft.localRotation = Quaternion.Euler(rotation);
            }
        }

        /// <summary>
        /// オフセットを調整（実行時）
        /// </summary>
        public void AdjustRightHandOffset(Vector3 offset, Vector3 rotation)
        {
            if (_toolPointRight != null)
            {
                _toolPointRight.localPosition = offset;
                _toolPointRight.localRotation = Quaternion.Euler(rotation);
            }
        }
    }
}
