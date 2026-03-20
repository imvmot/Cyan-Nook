using UnityEngine;
using System.Collections.Generic;

namespace CyanNook.Core
{
    /// <summary>
    /// 家具の「種類」を定義するScriptableObject
    /// 椅子全般、ベッド全般などの共通設定を保持
    /// </summary>
    [CreateAssetMenu(fileName = "FurnitureType_", menuName = "CyanNook/Furniture Type Data")]
    public class FurnitureTypeData : ScriptableObject
    {
        [Header("Basic Info")]
        [Tooltip("タイプID (chair, bed, bookshelf, door など)")]
        public string typeId;

        [Tooltip("表示名")]
        public string displayName;

        [TextArea(2, 4)]
        [Tooltip("説明")]
        public string description;

        [Header("Actions")]
        [Tooltip("利用可能なアクション一覧 (sit, sleep, look, exit, enter など)")]
        public string[] availableActions = new string[] { };

        [Tooltip("デフォルトアクション")]
        public string defaultAction;

        [Header("Interaction Points")]
        [Tooltip("インタラクションポイントのプレフィックス (Interact_sit, Interact_sleep など)")]
        public string interactionPointPrefix = "Interact_";

        [Tooltip("視線ターゲットのプレフィックス")]
        public string lookAtPointPrefix = "Interact_lookattarget";

        [Header("Movement Settings")]
        [Tooltip("接近判定半径 (m) - この距離以内でインタラクション開始")]
        public float approachRadius = 0.1f;

        [Tooltip("LookAt有効距離 (m) - この距離以内で視線ターゲットを使用")]
        public float lookAtMaxDistance = 2.0f;

        [Header("Collider Settings")]
        [Tooltip("インタラクション中に家具のコライダーを無効化するか")]
        public bool disableColliderDuringInteract = true;

        [Header("Animation Settings (Legacy - 互換性用)")]
        [Tooltip("対応するアニメーションID一覧 (interact_sit01 など)")]
        public List<string> compatibleAnimations = new List<string>();

        [Tooltip("インタラクション時にキャラクターが向く方向のオフセット（Y軸回転）")]
        public float facingAngleOffset = 0f;

        [Tooltip("インタラクション位置のオフセット")]
        public Vector3 positionOffset = Vector3.zero;

        /// <summary>
        /// 指定されたアクションが利用可能か判定
        /// </summary>
        public bool IsActionAvailable(string action)
        {
            if (availableActions == null || availableActions.Length == 0)
                return false;

            foreach (var a in availableActions)
            {
                if (a == action) return true;
            }
            return false;
        }

        /// <summary>
        /// 有効なアクションを取得（無効な場合はデフォルトを返す）
        /// </summary>
        public string GetValidAction(string requestedAction)
        {
            if (!string.IsNullOrEmpty(requestedAction) && IsActionAvailable(requestedAction))
            {
                return requestedAction;
            }

            if (!string.IsNullOrEmpty(defaultAction))
            {
                return defaultAction;
            }

            // デフォルトも設定されていない場合は最初のアクション
            if (availableActions != null && availableActions.Length > 0)
            {
                return availableActions[0];
            }

            return null;
        }

        /// <summary>
        /// アクションに対応するInteractionPointプレフィックスを取得
        /// </summary>
        public string GetInteractionPointPrefixForAction(string action)
        {
            // アクションに応じたプレフィックスを返す
            // 例: sit -> Interact_sit, sleep -> Interact_sleep
            return $"Interact_{action}";
        }

        /// <summary>
        /// 指定されたアニメーションがこのタイプで使用可能か判定 (互換性用)
        /// </summary>
        public bool IsAnimationCompatible(string animationId)
        {
            return compatibleAnimations.Contains(animationId);
        }

        /// <summary>
        /// アニメーションIDを取得（非対応の場合は最初の互換アニメーションを返す）(互換性用)
        /// </summary>
        public string GetValidAnimation(string requestedAnimation)
        {
            if (IsAnimationCompatible(requestedAnimation))
            {
                return requestedAnimation;
            }

            if (compatibleAnimations.Count > 0)
            {
                Debug.LogWarning($"[FurnitureTypeData] Animation '{requestedAnimation}' is not compatible with type '{typeId}'. Using: {compatibleAnimations[0]}");
                return compatibleAnimations[0];
            }

            return requestedAnimation;
        }
    }
}
