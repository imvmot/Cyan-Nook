using UnityEngine;
using System.Collections.Generic;

namespace CyanNook.Core
{
    /// <summary>
    /// 家具カテゴリの定義データ
    /// カテゴリごとに対応するアニメーションを管理する
    /// </summary>
    [CreateAssetMenu(fileName = "FurnitureCategory_", menuName = "CyanNook/Furniture Category")]
    public class FurnitureCategoryData : ScriptableObject
    {
        [Header("Basic Info")]
        [Tooltip("カテゴリID (chair, bed, bookshelf, door など)")]
        public string categoryId;

        [Tooltip("表示名")]
        public string displayName;

        [TextArea(2, 4)]
        [Tooltip("説明")]
        public string description;

        [Header("Animation Settings")]
        [Tooltip("対応するアニメーションID一覧 (interact_sit01 など)")]
        public List<string> compatibleAnimations = new List<string>();

        [Tooltip("デフォルトで使用するアニメーションID")]
        public string defaultAnimation;

        [Header("Interaction Settings")]
        [Tooltip("インタラクション時にキャラクターが向く方向のオフセット（Y軸回転）")]
        public float facingAngleOffset = 0f;

        [Tooltip("インタラクション位置のオフセット")]
        public Vector3 positionOffset = Vector3.zero;

        /// <summary>
        /// 指定されたアニメーションがこのカテゴリで使用可能か判定
        /// </summary>
        public bool IsAnimationCompatible(string animationId)
        {
            return compatibleAnimations.Contains(animationId);
        }

        /// <summary>
        /// アニメーションIDを取得（非対応の場合はデフォルトを返す）
        /// </summary>
        public string GetValidAnimation(string requestedAnimation)
        {
            if (IsAnimationCompatible(requestedAnimation))
            {
                return requestedAnimation;
            }

            Debug.LogWarning($"[FurnitureCategory] Animation '{requestedAnimation}' is not compatible with category '{categoryId}'. Using default: {defaultAnimation}");
            return defaultAnimation;
        }
    }
}
