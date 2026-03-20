using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace CyanNook.Furniture
{
    /// <summary>
    /// シーン内の家具を管理するマネージャー
    /// Nook内のFurnitureInstanceを検索・管理
    /// </summary>
    public class FurnitureManager : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("現在の部屋ID")]
        public string currentRoomId = "room01";

        [Header("Current Nook")]
        [Tooltip("現在読み込まれているNook")]
        public GameObject currentNook;

        private Dictionary<string, FurnitureInstance> _furnitureRegistry = new Dictionary<string, FurnitureInstance>();

        // 互換性用
        private Dictionary<string, FurniturePoint> _legacyRegistry = new Dictionary<string, FurniturePoint>();

        private void Awake()
        {
            RefreshRegistry();
        }

        /// <summary>
        /// 現在のNook内の家具を検索してレジストリを更新
        /// </summary>
        public void RefreshRegistry()
        {
            _furnitureRegistry.Clear();
            _legacyRegistry.Clear();

            // FurnitureInstanceを検索
            FurnitureInstance[] instances;
            if (currentNook != null)
            {
                instances = currentNook.GetComponentsInChildren<FurnitureInstance>();
            }
            else
            {
                instances = FindObjectsByType<FurnitureInstance>(FindObjectsSortMode.None);
            }

            foreach (var instance in instances)
            {
                if (string.IsNullOrEmpty(instance.instanceId))
                {
                    Debug.LogWarning($"[FurnitureManager] FurnitureInstance on {instance.gameObject.name} has no instanceId");
                    continue;
                }

                if (!_furnitureRegistry.ContainsKey(instance.instanceId))
                {
                    _furnitureRegistry.Add(instance.instanceId, instance);
                }
                else
                {
                    Debug.LogWarning($"[FurnitureManager] Duplicate furniture ID: {instance.instanceId}");
                }
            }

            // 互換性用: FurniturePointも検索
            FurniturePoint[] legacyPoints;
            if (currentNook != null)
            {
                legacyPoints = currentNook.GetComponentsInChildren<FurniturePoint>();
            }
            else
            {
                legacyPoints = FindObjectsByType<FurniturePoint>(FindObjectsSortMode.None);
            }

            foreach (var point in legacyPoints)
            {
                if (!_legacyRegistry.ContainsKey(point.FurnitureId))
                {
                    _legacyRegistry.Add(point.FurnitureId, point);
                }
            }

            Debug.Log($"[FurnitureManager] Registered {_furnitureRegistry.Count} furniture instances, {_legacyRegistry.Count} legacy points");
        }

        /// <summary>
        /// Nookを設定してレジストリを更新
        /// </summary>
        public void SetNook(GameObject nook)
        {
            currentNook = nook;
            RefreshRegistry();
        }

        /// <summary>
        /// IDで家具インスタンスを取得
        /// </summary>
        public FurnitureInstance GetFurnitureInstance(string instanceId)
        {
            if (_furnitureRegistry.TryGetValue(instanceId, out var instance))
            {
                return instance;
            }

            Debug.LogWarning($"[FurnitureManager] FurnitureInstance not found: {instanceId}");
            return null;
        }

        /// <summary>
        /// IDで家具を取得 (互換性用 - FurniturePoint)
        /// </summary>
        public FurniturePoint GetFurniture(string furnitureId)
        {
            if (_legacyRegistry.TryGetValue(furnitureId, out var furniture))
            {
                return furniture;
            }

            Debug.LogWarning($"[FurnitureManager] Furniture not found: {furnitureId}");
            return null;
        }

        /// <summary>
        /// 現在のNook内の全家具インスタンスを取得
        /// </summary>
        public List<FurnitureInstance> GetAllFurnitureInstances()
        {
            return _furnitureRegistry.Values.ToList();
        }

        /// <summary>
        /// タイプIDで家具をフィルタリング
        /// </summary>
        public List<FurnitureInstance> GetFurnitureByType(string typeId)
        {
            return _furnitureRegistry.Values
                .Where(f => f.TypeId == typeId)
                .ToList();
        }

        /// <summary>
        /// 空いている家具を取得
        /// </summary>
        public List<FurnitureInstance> GetAvailableFurniture()
        {
            return _furnitureRegistry.Values
                .Where(f => !f.isOccupied)
                .ToList();
        }

        /// <summary>
        /// 空いている家具をタイプで取得
        /// </summary>
        public FurnitureInstance GetAvailableFurnitureByType(string typeId)
        {
            return _furnitureRegistry.Values
                .FirstOrDefault(f => f.TypeId == typeId && !f.isOccupied);
        }

        /// <summary>
        /// 指定位置から最も近い家具インスタンスを取得
        /// </summary>
        public FurnitureInstance GetNearestFurnitureInstance(Vector3 position, string typeId = null)
        {
            var candidates = string.IsNullOrEmpty(typeId)
                ? GetAllFurnitureInstances()
                : GetFurnitureByType(typeId);

            FurnitureInstance nearest = null;
            float minDistance = float.MaxValue;

            foreach (var instance in candidates)
            {
                float distance = Vector3.Distance(position, instance.transform.position);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearest = instance;
                }
            }

            return nearest;
        }

        /// <summary>
        /// 指定アクションに対応する最寄りの空き家具を取得
        /// action例: "sit" → availableActionsに"sit"を含む家具を検索
        /// </summary>
        public FurnitureInstance GetNearestAvailableFurniture(Vector3 position, string action)
        {
            FurnitureInstance nearest = null;
            float minDistance = float.MaxValue;

            foreach (var instance in _furnitureRegistry.Values)
            {
                if (instance.isOccupied) continue;

                // アクション対応チェック
                if (instance.typeData?.availableActions != null &&
                    !System.Array.Exists(instance.typeData.availableActions, a => a == action))
                {
                    continue;
                }

                float distance = Vector3.Distance(position, instance.transform.position);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearest = instance;
                }
            }

            return nearest;
        }

        /// <summary>
        /// 指定アクションに対応する空き家具をランダムに1つ取得
        /// exclude を除外して候補を選び、候補がなければnullを返す
        /// 同種インタラクション中に別の家具へ移動する場合に使用
        /// </summary>
        public FurnitureInstance GetRandomAvailableFurniture(string action, FurnitureInstance exclude = null)
        {
            var candidates = new List<FurnitureInstance>();

            foreach (var instance in _furnitureRegistry.Values)
            {
                if (instance.isOccupied) continue;
                if (instance == exclude) continue;

                // アクション対応チェック
                if (instance.typeData?.availableActions != null &&
                    !System.Array.Exists(instance.typeData.availableActions, a => a == action))
                {
                    continue;
                }

                candidates.Add(instance);
            }

            if (candidates.Count == 0) return null;

            return candidates[Random.Range(0, candidates.Count)];
        }

        /// <summary>
        /// FurnitureInstanceとアクションからInteractionRequestを作成
        /// </summary>
        public InteractionRequest CreateInteractionRequest(FurnitureInstance instance, string action, Vector3 characterPosition)
        {
            if (instance == null) return null;

            // 有効なアクションか検証
            if (instance.typeData != null)
            {
                action = instance.typeData.GetValidAction(action);
            }

            return new InteractionRequest
            {
                furniture = instance,
                action = action,
                targetPoint = instance.GetNearestInteractionPoint(characterPosition, action),
                lookAtPoint = instance.GetNearestLookAtPoint(characterPosition),
                approachRadius = instance.typeData?.approachRadius ?? 0.1f
            };
        }

        /// <summary>
        /// LLMプロンプト用の家具リストを生成
        /// </summary>
        public string GenerateFurnitureListForPrompt()
        {
            var instances = GetAllFurnitureInstances();
            var lines = new List<string>();

            foreach (var instance in instances)
            {
                string status = instance.isOccupied ? " (使用中)" : "";
                string displayName = instance.typeData?.displayName ?? instance.TypeId;
                string actions = instance.typeData?.availableActions != null
                    ? string.Join("/", instance.typeData.availableActions)
                    : "";

                lines.Add($"  - {instance.instanceId} ({displayName}) [{actions}]{status}");
            }

            return string.Join("\n", lines);
        }

        /// <summary>
        /// 家具へのインタラクションを開始
        /// </summary>
        public InteractionRequest StartInteraction(string instanceId, Transform character, string requestedAction = null)
        {
            var instance = GetFurnitureInstance(instanceId);
            if (instance == null)
            {
                return null;
            }

            if (instance.isOccupied && instance.currentUser != character)
            {
                Debug.LogWarning($"[FurnitureManager] Furniture {instanceId} is already occupied");
                return null;
            }

            // アクション決定
            string action = requestedAction;
            if (string.IsNullOrEmpty(action))
            {
                action = instance.SelectBestAction(character);
            }

            // 有効なアクションか検証
            if (instance.typeData != null)
            {
                action = instance.typeData.GetValidAction(action);
            }

            // InteractionRequestを作成
            var request = new InteractionRequest
            {
                furniture = instance,
                action = action,
                targetPoint = instance.GetNearestInteractionPoint(character.position, action),
                lookAtPoint = instance.GetNearestLookAtPoint(character.position),
                approachRadius = instance.typeData?.approachRadius ?? 0.1f
            };

            return request;
        }

        // ===========================================
        // 互換性用メソッド
        // ===========================================

        /// <summary>
        /// 現在の部屋の家具一覧を取得 (互換性用)
        /// </summary>
        public List<FurniturePoint> GetFurnitureInCurrentRoom()
        {
            return _legacyRegistry.Values
                .Where(f => f.roomId == currentRoomId)
                .ToList();
        }

        /// <summary>
        /// カテゴリで家具をフィルタリング (互換性用)
        /// </summary>
        public List<FurniturePoint> GetFurnitureByCategory(string categoryId)
        {
            return _legacyRegistry.Values
                .Where(f => f.CategoryId == categoryId && f.roomId == currentRoomId)
                .ToList();
        }

        /// <summary>
        /// 指定位置から最も近い家具を取得 (互換性用)
        /// </summary>
        public FurniturePoint GetNearestFurniture(Vector3 position, string categoryId = null)
        {
            var candidates = string.IsNullOrEmpty(categoryId)
                ? GetFurnitureInCurrentRoom()
                : GetFurnitureByCategory(categoryId);

            FurniturePoint nearest = null;
            float minDistance = float.MaxValue;

            foreach (var furniture in candidates)
            {
                float distance = Vector3.Distance(position, furniture.GetInteractionPosition());
                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearest = furniture;
                }
            }

            return nearest;
        }
    }

    /// <summary>
    /// インタラクションリクエスト
    /// </summary>
    public class InteractionRequest
    {
        public FurnitureInstance furniture;
        public string action;
        public Transform targetPoint;
        public Transform lookAtPoint;
        public float approachRadius;

        /// <summary>
        /// インタラクション開始（家具を占有）
        /// </summary>
        public void Begin(Transform user)
        {
            Debug.Log($"[InteractionRequest] Begin - furniture: {furniture?.instanceId}, user: {user?.name}");
            furniture?.Occupy(user);
        }

        /// <summary>
        /// インタラクション終了（家具を解放）
        /// </summary>
        public void End()
        {
            Debug.Log($"[InteractionRequest] End - furniture: {furniture?.instanceId}");
            furniture?.Release();
        }
    }
}
