using UnityEngine;
using UnityEngine.AI;
using System.Text;
using System.Collections.Generic;
using CyanNook.Furniture;
using CyanNook.Character;

namespace CyanNook.Chat
{
    /// <summary>
    /// キャラクターの一人称視点から見た空間情報をJSON化してLLMに提供する
    /// 時計方向（1-12）でオブジェクトの方向を、メートルで距離を表現
    /// </summary>
    public class SpatialContextProvider : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("キャラクターのCharacterRoot（NavMeshAgent所在）")]
        public Transform characterTransform;

        [Tooltip("家具マネージャー")]
        public FurnitureManager furnitureManager;

        [Tooltip("ルームターゲットマネージャー")]
        public RoomTargetManager roomTargetManager;

        [Header("Settings")]
        [Tooltip("NavMesh境界レイキャストの最大距離")]
        public float maxRaycastDistance = 20f;

        /// <summary>
        /// 空間認識JSONを生成
        /// キャラクターの位置・向きを基準に、周囲の家具・ターゲット・部屋境界を記述
        /// </summary>
        public string GenerateSpatialContextJson()
        {
            if (characterTransform == null)
            {
                return "{}";
            }

            Vector3 charPos = characterTransform.position;
            Vector3 charForward = characterTransform.forward;

            var sb = new StringBuilder();
            sb.Append("{\n");

            // room_bounds: 4方向のNavMesh端までの距離
            AppendRoomBounds(sb, charPos, charForward);

            sb.Append(",\n");

            // furniture: 家具の方向・距離・アクション・使用状態
            AppendFurniture(sb, charPos, charForward);

            sb.Append(",\n");

            // room_targets: ルームターゲットの方向・距離
            AppendRoomTargets(sb, charPos, charForward);

            sb.Append("\n}");

            return sb.ToString();
        }

        /// <summary>
        /// 4方向（前/右/後/左）のNavMesh端までの距離を生成
        /// </summary>
        private void AppendRoomBounds(StringBuilder sb, Vector3 charPos, Vector3 charForward)
        {
            sb.Append("  \"room_bounds\": { ");

            // 12時（前）、3時（右）、6時（後）、9時（左）
            int[] clocks = { 12, 3, 6, 9 };
            float[] angles = { 0f, 90f, 180f, 270f };

            for (int i = 0; i < clocks.Length; i++)
            {
                Vector3 direction = Quaternion.Euler(0f, angles[i], 0f) * charForward;
                float distance = RaycastNavMeshEdge(charPos, direction);

                if (i > 0) sb.Append(", ");
                sb.Append($"\"{clocks[i]}\": {distance:F1}");
            }

            sb.Append(" }");
        }

        /// <summary>
        /// 指定方向へのNavMesh端までの距離を計算
        /// </summary>
        private float RaycastNavMeshEdge(Vector3 origin, Vector3 direction)
        {
            Vector3 targetPos = origin + direction * maxRaycastDistance;

            if (NavMesh.Raycast(origin, targetPos, out NavMeshHit hit, NavMesh.AllAreas))
            {
                return hit.distance;
            }

            return maxRaycastDistance;
        }

        /// <summary>
        /// 全家具の空間情報を生成
        /// </summary>
        private void AppendFurniture(StringBuilder sb, Vector3 charPos, Vector3 charForward)
        {
            sb.Append("  \"furniture\": [");

            if (furnitureManager != null)
            {
                var instances = furnitureManager.GetAllFurnitureInstances();
                for (int i = 0; i < instances.Count; i++)
                {
                    var inst = instances[i];
                    if (i > 0) sb.Append(",");
                    sb.Append("\n    ");
                    AppendFurnitureEntry(sb, inst, charPos, charForward);
                }
            }

            sb.Append("\n  ]");
        }

        /// <summary>
        /// 個別家具のJSON生成
        /// </summary>
        private void AppendFurnitureEntry(StringBuilder sb, FurnitureInstance inst, Vector3 charPos, Vector3 charForward)
        {
            int clock = CalculateClock(charPos, inst.transform.position, charForward);
            float distance = CalculateHorizontalDistance(charPos, inst.transform.position);
            string displayName = inst.typeData?.displayName ?? inst.instanceId;
            string[] actions = inst.typeData?.availableActions ?? new string[0];

            sb.Append("{ ");
            sb.Append($"\"id\": \"{EscapeJson(inst.instanceId)}\", ");
            sb.Append($"\"name\": \"{EscapeJson(displayName)}\", ");
            sb.Append($"\"clock\": {clock}, ");
            sb.Append($"\"distance\": {distance:F1}, ");
            sb.Append("\"actions\": [");
            for (int j = 0; j < actions.Length; j++)
            {
                if (j > 0) sb.Append(", ");
                sb.Append($"\"{EscapeJson(actions[j])}\"");
            }
            sb.Append("], ");
            sb.Append($"\"occupied\": {(inst.isOccupied ? "true" : "false")}");
            sb.Append(" }");
        }

        /// <summary>
        /// 全ルームターゲットの空間情報を生成
        /// </summary>
        private void AppendRoomTargets(StringBuilder sb, Vector3 charPos, Vector3 charForward)
        {
            sb.Append("  \"room_targets\": [");

            if (roomTargetManager != null)
            {
                int index = 0;
                foreach (string targetName in roomTargetManager.TargetNames)
                {
                    var target = roomTargetManager.GetTarget(targetName);
                    if (target == null || target.transform == null) continue;

                    if (index > 0) sb.Append(",");
                    sb.Append("\n    ");

                    int clock = CalculateClock(charPos, target.transform.position, charForward);
                    float distance = CalculateHorizontalDistance(charPos, target.transform.position);

                    sb.Append("{ ");
                    sb.Append($"\"name\": \"{EscapeJson(targetName)}\", ");
                    sb.Append($"\"clock\": {clock}, ");
                    sb.Append($"\"distance\": {distance:F1}");
                    sb.Append(" }");

                    index++;
                }
            }

            sb.Append("\n  ]");
        }

        /// <summary>
        /// 2点間の時計方向を計算（キャラクターのforward=12時基準）
        /// </summary>
        private int CalculateClock(Vector3 fromPos, Vector3 toPos, Vector3 forward)
        {
            Vector3 dir = toPos - fromPos;
            dir.y = 0f;

            if (dir.sqrMagnitude < 0.01f) return 12;

            float angle = Vector3.SignedAngle(forward, dir, Vector3.up);
            if (angle < 0f) angle += 360f;

            int clock = Mathf.RoundToInt(angle / 30f);
            if (clock == 0 || clock > 12) clock = 12;
            return clock;
        }

        /// <summary>
        /// 2点間のXZ平面上の距離を計算
        /// </summary>
        private float CalculateHorizontalDistance(Vector3 from, Vector3 to)
        {
            Vector3 diff = to - from;
            diff.y = 0f;
            return diff.magnitude;
        }

        /// <summary>
        /// JSON文字列エスケープ（最低限）
        /// </summary>
        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
