using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using CyanNook.Core;

namespace CyanNook.Furniture
{
    /// <summary>
    /// シーン内の個々の家具インスタンス
    /// Nookプレハブ内の各家具にアタッチ
    /// </summary>
    public class FurnitureInstance : MonoBehaviour
    {
        [Header("Identification")]
        [Tooltip("インスタンスID (room01_chair_01 形式)")]
        public string instanceId;

        [Tooltip("家具タイプデータ")]
        public FurnitureTypeData typeData;

        [Header("State")]
        [Tooltip("現在使用中かどうか")]
        public bool isOccupied = false;

        [Tooltip("現在のユーザー（キャラクター）")]
        public Transform currentUser;

        [Header("Colliders")]
        [Tooltip("インタラクション中に無効化するコライダー（空の場合は自動取得）")]
        public Collider[] interactionColliders;

        // 自動収集されたポイント
        private Transform[] _interactionPoints;
        private Transform[] _lookAtPoints;
        private Dictionary<string, Transform[]> _actionToPoints;

        /// <summary>
        /// インタラクションポイント一覧
        /// </summary>
        public Transform[] InteractionPoints => _interactionPoints;

        /// <summary>
        /// 視線ターゲット一覧
        /// </summary>
        public Transform[] LookAtPoints => _lookAtPoints;

        /// <summary>
        /// タイプID
        /// </summary>
        public string TypeId => typeData?.typeId ?? "unknown";

        private void Awake()
        {
            CollectPoints();
            CollectColliders();
        }

        /// <summary>
        /// 子オブジェクトからInteractionPointとLookAtPointを収集
        /// </summary>
        private void CollectPoints()
        {
            _actionToPoints = new Dictionary<string, Transform[]>();

            var allChildren = GetComponentsInChildren<Transform>(true);
            var interactionList = new List<Transform>();
            var lookAtList = new List<Transform>();

            string lookAtPrefix = typeData?.lookAtPointPrefix ?? "Interact_lookattarget";

            foreach (var child in allChildren)
            {
                if (child == transform) continue;

                string name = child.name;

                // LookAtポイント（大文字小文字無視）
                if (name.StartsWith(lookAtPrefix, System.StringComparison.OrdinalIgnoreCase))
                {
                    lookAtList.Add(child);
                    continue;
                }

                // Interactionポイント（Interact_で始まるもの、大文字小文字無視）
                if (name.StartsWith("Interact_", System.StringComparison.OrdinalIgnoreCase))
                {
                    interactionList.Add(child);

                    // アクションごとに分類
                    // 例: Interact_sit01 -> "sit"
                    string actionPart = ExtractActionFromPointName(name);
                    if (!string.IsNullOrEmpty(actionPart))
                    {
                        if (!_actionToPoints.ContainsKey(actionPart))
                        {
                            _actionToPoints[actionPart] = new Transform[] { };
                        }

                        var list = _actionToPoints[actionPart].ToList();
                        list.Add(child);
                        _actionToPoints[actionPart] = list.ToArray();
                    }
                }
            }

            _interactionPoints = interactionList.ToArray();
            _lookAtPoints = lookAtList.ToArray();

            if (_interactionPoints.Length == 0)
            {
                Debug.LogWarning($"[FurnitureInstance] No interaction points found for {instanceId}. Using transform as fallback.");
            }
        }

        /// <summary>
        /// コライダーを収集
        /// </summary>
        private void CollectColliders()
        {
            if (interactionColliders == null || interactionColliders.Length == 0)
            {
                interactionColliders = GetComponentsInChildren<Collider>();
            }
        }

        /// <summary>
        /// ポイント名からアクション名を抽出
        /// Interact_sit01 -> "sit"
        /// </summary>
        private string ExtractActionFromPointName(string pointName)
        {
            if (!pointName.StartsWith("Interact_")) return null;

            // "Interact_"を除去
            string remainder = pointName.Substring(9);

            // 数字を除去してアクション名を取得
            string action = new string(remainder.TakeWhile(c => !char.IsDigit(c)).ToArray());

            return action;
        }

        /// <summary>
        /// コンテキストに応じて最適なアクションを選択
        /// </summary>
        public string SelectBestAction(Transform character)
        {
            if (typeData == null || typeData.availableActions == null || typeData.availableActions.Length == 0)
            {
                return null;
            }

            // アクションが1つしかない場合はそれを返す
            if (typeData.availableActions.Length == 1)
            {
                return typeData.availableActions[0];
            }

            // 家具タイプに応じた判定
            switch (typeData.typeId)
            {
                case "door":
                    return SelectDoorAction(character);

                case "bed":
                    return SelectBedAction(character);

                default:
                    return typeData.defaultAction;
            }
        }

        /// <summary>
        /// ドア用アクション判定
        /// </summary>
        private string SelectDoorAction(Transform character)
        {
            // ドアの向いている方向と、キャラクターの位置関係で判定
            Vector3 toCharacter = character.position - transform.position;
            float dot = Vector3.Dot(transform.forward, toCharacter);

            // キャラクターがドアの前（内側）にいる場合は exit
            // ドアの後ろ（外側）にいる場合は enter
            return dot > 0 ? "exit" : "enter";
        }

        /// <summary>
        /// ベッド用アクション判定
        /// </summary>
        private string SelectBedAction(Transform character)
        {
            // 既にベッドに座っている場合は sleep
            if (isOccupied && currentUser == character)
            {
                return "sleep";
            }

            // 距離で判定（近距離は sit、遠距離は sleep）
            float distance = Vector3.Distance(character.position, transform.position);
            float threshold = typeData.approachRadius * 3f; // 閾値

            return distance < threshold ? "sit" : "sleep";
        }

        /// <summary>
        /// 指定アクション用のInteractionPointを取得
        /// </summary>
        public Transform[] GetInteractionPointsForAction(string action)
        {
            if (_actionToPoints != null && _actionToPoints.TryGetValue(action, out var points))
            {
                return points;
            }

            // 見つからない場合は全てのInteractionPointを返す
            return _interactionPoints;
        }

        /// <summary>
        /// 最寄りのInteractionPointを取得
        /// </summary>
        public Transform GetNearestInteractionPoint(Vector3 position, string action = null)
        {
            Transform[] points;

            if (!string.IsNullOrEmpty(action))
            {
                points = GetInteractionPointsForAction(action);
            }
            else
            {
                points = _interactionPoints;
            }

            if (points == null || points.Length == 0)
            {
                // フォールバック: 自身のTransformを返す
                return transform;
            }

            Transform nearest = null;
            float minDistance = float.MaxValue;

            foreach (var point in points)
            {
                float distance = Vector3.Distance(position, point.position);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearest = point;
                }
            }

            return nearest ?? transform;
        }

        /// <summary>
        /// 最寄りのLookAtPointを取得（距離制限付き）
        /// </summary>
        public Transform GetNearestLookAtPoint(Vector3 position)
        {
            if (_lookAtPoints == null || _lookAtPoints.Length == 0)
            {
                return null;
            }

            float maxDistance = typeData?.lookAtMaxDistance ?? 2.0f;
            Transform nearest = null;
            float minDistance = float.MaxValue;

            foreach (var point in _lookAtPoints)
            {
                float distance = Vector3.Distance(position, point.position);
                if (distance < minDistance && distance <= maxDistance)
                {
                    minDistance = distance;
                    nearest = point;
                }
            }

            return nearest;
        }

        /// <summary>
        /// インタラクション位置を取得（アクション指定）
        /// </summary>
        public Vector3 GetInteractionPosition(string action = null)
        {
            var point = GetNearestInteractionPoint(Vector3.zero, action);
            return point != null ? point.position : transform.position;
        }

        /// <summary>
        /// インタラクション時の回転を取得
        /// </summary>
        public Quaternion GetInteractionRotation(string action = null)
        {
            var point = GetNearestInteractionPoint(Vector3.zero, action);
            float angleOffset = typeData?.facingAngleOffset ?? 0f;

            Quaternion baseRotation = point != null ? point.rotation : transform.rotation;
            return baseRotation * Quaternion.Euler(0, angleOffset, 0);
        }

        /// <summary>
        /// 家具を占有状態にする
        /// </summary>
        public void Occupy(Transform user)
        {
            isOccupied = true;
            currentUser = user;

            Debug.Log($"[FurnitureInstance] {instanceId} occupied by {user?.name}");

            // コライダーを無効化
            if (typeData != null && typeData.disableColliderDuringInteract)
            {
                SetCollidersEnabled(false);
            }
        }

        /// <summary>
        /// 家具を解放する
        /// </summary>
        public void Release()
        {
            Debug.Log($"[FurnitureInstance] {instanceId} released (was occupied: {isOccupied}, user: {currentUser?.name})");

            isOccupied = false;
            currentUser = null;

            // コライダーを有効化
            SetCollidersEnabled(true);
        }

        /// <summary>
        /// コライダーの有効/無効を切り替え
        /// </summary>
        public void SetCollidersEnabled(bool enabled)
        {
            if (interactionColliders == null) return;

            foreach (var col in interactionColliders)
            {
                if (col != null)
                {
                    col.enabled = enabled;
                }
            }
        }

        /// <summary>
        /// 指定位置から接近半径内にいるか判定
        /// </summary>
        public bool IsWithinApproachRadius(Vector3 position, string action = null)
        {
            var targetPoint = GetNearestInteractionPoint(position, action);
            float distance = Vector3.Distance(position, targetPoint.position);
            float radius = typeData?.approachRadius ?? 0.1f;

            return distance <= radius;
        }

        private void OnDrawGizmos()
        {
            // インタラクションポイントを可視化
            Gizmos.color = isOccupied ? Color.red : Color.green;

            if (_interactionPoints != null)
            {
                foreach (var point in _interactionPoints)
                {
                    if (point != null)
                    {
                        Gizmos.DrawWireSphere(point.position, 0.15f);
                        Gizmos.DrawRay(point.position, point.forward * 0.3f);
                    }
                }
            }

            // LookAtポイントを表示
            Gizmos.color = Color.cyan;
            if (_lookAtPoints != null)
            {
                foreach (var point in _lookAtPoints)
                {
                    if (point != null)
                    {
                        Gizmos.DrawWireSphere(point.position, 0.1f);
                    }
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            // 選択時に詳細表示
            Gizmos.color = Color.yellow;

            float radius = typeData?.approachRadius ?? 0.1f;

            if (_interactionPoints != null)
            {
                foreach (var point in _interactionPoints)
                {
                    if (point != null)
                    {
                        Gizmos.DrawWireSphere(point.position, radius);
                    }
                }
            }

#if UNITY_EDITOR
            if (_interactionPoints != null)
            {
                foreach (var point in _interactionPoints)
                {
                    if (point != null)
                    {
                        UnityEditor.Handles.Label(point.position + Vector3.up * 0.3f, point.name);
                    }
                }
            }

            UnityEditor.Handles.Label(transform.position + Vector3.up * 0.6f, instanceId);
#endif
        }
    }
}
