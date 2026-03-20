using UnityEngine;
using CyanNook.Core;

namespace CyanNook.Furniture
{
    /// <summary>
    /// 家具のインタラクションポイントを定義するコンポーネント
    /// シーン内の家具オブジェクトにアタッチして使用
    /// </summary>
    public class FurniturePoint : MonoBehaviour
    {
        [Header("Identification")]
        [Tooltip("部屋ID (room01 など)")]
        public string roomId = "room01";

        [Tooltip("家具カテゴリデータ")]
        public FurnitureCategoryData categoryData;

        [Tooltip("同カテゴリ内のインデックス (01, 02, ...)")]
        [Range(1, 99)]
        public int index = 1;

        [Header("Interaction Locator")]
        [Tooltip("キャラクターがインタラクション時に立つ位置")]
        public Transform interactionLocator;

        [Header("State")]
        [Tooltip("現在使用中かどうか")]
        public bool isOccupied = false;

        /// <summary>
        /// 家具の完全ID (room01_chair_01 形式)
        /// </summary>
        public string FurnitureId
        {
            get
            {
                if (categoryData == null) return $"{roomId}_unknown_{index:D2}";
                return $"{roomId}_{categoryData.categoryId}_{index:D2}";
            }
        }

        /// <summary>
        /// カテゴリID
        /// </summary>
        public string CategoryId => categoryData?.categoryId ?? "unknown";

        /// <summary>
        /// インタラクション位置を取得
        /// </summary>
        public Vector3 GetInteractionPosition()
        {
            if (interactionLocator != null)
            {
                return interactionLocator.position + (categoryData?.positionOffset ?? Vector3.zero);
            }
            return transform.position + (categoryData?.positionOffset ?? Vector3.zero);
        }

        /// <summary>
        /// インタラクション時の回転を取得
        /// </summary>
        public Quaternion GetInteractionRotation()
        {
            float angleOffset = categoryData?.facingAngleOffset ?? 0f;

            if (interactionLocator != null)
            {
                return interactionLocator.rotation * Quaternion.Euler(0, angleOffset, 0);
            }
            return transform.rotation * Quaternion.Euler(0, angleOffset, 0);
        }

        /// <summary>
        /// 指定されたアニメーションを検証し、有効なものを返す
        /// </summary>
        public string ValidateAnimation(string requestedAnimation)
        {
            if (categoryData == null)
            {
                Debug.LogWarning($"[FurniturePoint] No category data assigned to {gameObject.name}");
                return requestedAnimation;
            }

            return categoryData.GetValidAnimation(requestedAnimation);
        }

        /// <summary>
        /// 家具を占有状態にする
        /// </summary>
        public void Occupy()
        {
            isOccupied = true;
        }

        /// <summary>
        /// 家具を解放する
        /// </summary>
        public void Release()
        {
            isOccupied = false;
        }

        private void OnDrawGizmos()
        {
            // インタラクション位置を可視化
            Gizmos.color = isOccupied ? Color.red : Color.green;
            Vector3 pos = GetInteractionPosition();
            Gizmos.DrawWireSphere(pos, 0.2f);

            // 向きを表示
            Gizmos.color = Color.blue;
            Quaternion rot = GetInteractionRotation();
            Gizmos.DrawRay(pos, rot * Vector3.forward * 0.5f);
        }

        private void OnDrawGizmosSelected()
        {
            // 選択時に詳細表示
            Gizmos.color = Color.yellow;
            Vector3 pos = GetInteractionPosition();
            Gizmos.DrawWireSphere(pos, 0.3f);

#if UNITY_EDITOR
            UnityEditor.Handles.Label(pos + Vector3.up * 0.5f, FurnitureId);
#endif
        }
    }
}
