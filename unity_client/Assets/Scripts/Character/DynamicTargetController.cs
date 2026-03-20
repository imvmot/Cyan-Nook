using UnityEngine;
using UnityEngine.AI;

namespace CyanNook.Character
{
    /// <summary>
    /// DynamicTarget: LLMが自由に指定できる移動・LookAtターゲット
    ///
    /// シーンに1つ配置し、clock/distance/height パラメータに基づいて
    /// キャラクターの前方を基準にNavMeshで先行移動する。
    ///
    /// clock: 時計の方向（12=正面, 3=右, 6=背後, 9=左）
    /// distance: near/mid/far（Inspector設定値）
    /// height: high/mid/low（Inspector設定値、LookAt用Y座標）
    /// </summary>
    public class DynamicTargetController : MonoBehaviour
    {
        [Header("Distance Settings")]
        [Tooltip("near の距離")]
        public float nearDistance = 1.5f;
        [Tooltip("mid の距離")]
        public float midDistance = 3.0f;
        [Tooltip("far の距離")]
        public float farDistance = 5.0f;

        [Header("Height Settings")]
        [Tooltip("high の高さ")]
        public float highHeight = 2.0f;
        [Tooltip("mid の高さ")]
        public float midHeight = 1.0f;
        [Tooltip("low の高さ")]
        public float lowHeight = 0.0f;

        [Header("LookAt Target")]
        [Tooltip("LookAt用子オブジェクトのローカルZ方向オフセット")]
        public float lookAtForwardOffset = 1.0f;

        private NavMeshAgent _agent;

        /// <summary>
        /// LookAt用の子オブジェクト（height に応じてローカルY座標が変化）
        /// CharacterLookAtController が毎フレーム追跡する
        /// </summary>
        public Transform LookAtTarget { get; private set; }

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            if (_agent == null)
            {
                Debug.LogWarning("[DynamicTargetController] NavMeshAgent not found. Adding one.");
                _agent = gameObject.AddComponent<NavMeshAgent>();
            }

            // DynamicTargetは見えないオブジェクト、高速移動
            _agent.speed = 10f;
            _agent.angularSpeed = 720f;
            _agent.acceleration = 50f;
            _agent.stoppingDistance = 0.1f;
            _agent.radius = 0.1f;
            _agent.height = 0.1f;
            // 回避システムから除外（不可視マーカーなので他Agentと衝突回避しない）
            _agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
            _agent.avoidancePriority = 99;

            // LookAt用子オブジェクトを生成
            var lookAtGo = new GameObject("DynamicLookAtTarget");
            lookAtGo.transform.SetParent(transform);
            lookAtGo.transform.localPosition = new Vector3(0f, midHeight, lookAtForwardOffset);
            LookAtTarget = lookAtGo.transform;
        }

        [Header("NavMesh Sampling")]
        [Tooltip("NavMesh.SamplePositionの最大検索半径")]
        public float maxSearchRadius = 10f;

        /// <summary>
        /// clock/distance/height に基づいてターゲット位置を計算し、NavMeshで移動
        /// </summary>
        /// <param name="clock">方向（1-12、キャラ基準。12=正面、3=右、6=背後、9=左）</param>
        /// <param name="distance">距離プリセット（"near", "mid", "far"）</param>
        /// <param name="height">高さプリセット（"high", "mid", "low"）</param>
        /// <param name="characterTransform">基準となるキャラクターのTransform</param>
        /// <returns>キャラクターの移動先として使用する目標位置</returns>
        public Vector3 MoveTo(int clock, string distance, string height, Transform characterTransform)
        {
            if (characterTransform == null) return transform.position;

            // clock → 角度（キャラクターのforward基準）
            // 12時=0°, 1時=30°, 2時=60°, 3時=90°（右）...
            float angle = ClockToAngle(clock);

            // キャラクターの向きに対する相対方向を計算
            Vector3 direction = Quaternion.Euler(0f, angle, 0f) * characterTransform.forward;

            // 距離
            float dist = ResolveDistance(distance);

            // 目標位置
            Vector3 targetPosition = characterTransform.position + direction * dist;

            // 高さ（Y座標）はLookAt用に設定
            float h = ResolveHeight(height);

            // NavMesh上の有効位置を検索して移動先を確定
            Vector3 resolvedPosition = FindValidNavMeshPosition(targetPosition, dist, characterTransform.position);

            if (_agent != null && _agent.isOnNavMesh)
            {
                _agent.SetDestination(resolvedPosition);
            }
            else
            {
                transform.position = resolvedPosition;
            }

            // LookAt子オブジェクトの高さを更新
            if (LookAtTarget != null)
            {
                LookAtTarget.localPosition = new Vector3(0f, h, lookAtForwardOffset);
            }

            Debug.Log($"[DynamicTargetController] MoveTo: clock={clock}({angle}°), distance={distance}({dist}), height={height}({h}), target={resolvedPosition}");

            return resolvedPosition;
        }

        /// <summary>
        /// 目標位置からNavMesh上の有効な位置を段階的に検索
        /// 1. 目標位置付近（距離プリセット範囲内）
        /// 2. 拡大検索（maxSearchRadius）
        /// 3. キャラクター現在位置付近（フォールバック）
        /// </summary>
        private Vector3 FindValidNavMeshPosition(Vector3 targetPosition, float initialRadius, Vector3 characterPosition)
        {
            // 1. 目標位置付近で検索
            if (NavMesh.SamplePosition(targetPosition, out NavMeshHit hit, initialRadius, NavMesh.AllAreas))
            {
                return hit.position;
            }

            // 2. 拡大範囲で検索
            if (initialRadius < maxSearchRadius)
            {
                if (NavMesh.SamplePosition(targetPosition, out hit, maxSearchRadius, NavMesh.AllAreas))
                {
                    Debug.LogWarning($"[DynamicTargetController] Target required extended search: {targetPosition} → {hit.position}");
                    return hit.position;
                }
            }

            // 3. キャラクター位置付近にフォールバック（到達不能な位置にテレポートしない）
            if (NavMesh.SamplePosition(characterPosition, out hit, 2f, NavMesh.AllAreas))
            {
                Debug.LogWarning($"[DynamicTargetController] No valid NavMesh near target, falling back to character position: {characterPosition}");
                return hit.position;
            }

            // 4. 最終フォールバック: キャラクター位置そのまま
            Debug.LogError($"[DynamicTargetController] No valid NavMesh found at all, using character position: {characterPosition}");
            return characterPosition;
        }

        // LookAt用の高さは子オブジェクト LookAtTarget のローカルYで管理するため
        // 親（DynamicTarget自体）のY座標はNavMeshに委ねる

        /// <summary>
        /// clock値（1-12）をキャラクター基準の角度に変換
        /// 12時=0°（正面）、3時=90°（右）、6時=180°（背後）、9時=270°（左）
        /// </summary>
        private static float ClockToAngle(int clock)
        {
            // 12時を0°として時計回りに30°刻み
            int normalized = ((clock - 1) % 12) + 1; // 1-12に正規化
            return (normalized % 12) * 30f;
        }

        private float ResolveDistance(string distance)
        {
            return distance switch
            {
                "near" => nearDistance,
                "mid" => midDistance,
                "far" => farDistance,
                _ => midDistance
            };
        }

        /// <summary>
        /// LookAt子オブジェクトの高さのみを更新（MoveTo を伴わない場合に使用）
        /// </summary>
        public void SetLookAtHeight(string height)
        {
            if (LookAtTarget == null) return;
            float h = ResolveHeight(height);
            var pos = LookAtTarget.localPosition;
            pos.y = h;
            LookAtTarget.localPosition = pos;
        }

        private float ResolveHeight(string height)
        {
            return height switch
            {
                "high" => highHeight,
                "mid" => midHeight,
                "low" => lowHeight,
                _ => midHeight
            };
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(transform.position, 0.3f);

            if (_agent != null && _agent.hasPath)
            {
                Gizmos.color = Color.magenta * 0.5f;
                var corners = _agent.path.corners;
                for (int i = 0; i < corners.Length - 1; i++)
                {
                    Gizmos.DrawLine(corners[i], corners[i + 1]);
                }
            }
        }
    }
}
