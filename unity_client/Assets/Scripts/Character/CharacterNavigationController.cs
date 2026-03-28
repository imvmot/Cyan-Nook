using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Playables;
using System;
using CyanNook.Furniture;

namespace CyanNook.Character
{
    /// <summary>
    /// キャラクターの移動制御を担当
    /// NavMeshAgentが位置を直接制御し、アニメーション再生速度をAgent速度に合わせる方式
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class CharacterNavigationController : MonoBehaviour
    {
        [Header("References")]
        public CharacterAnimationController animationController;
        public Animator animator;

        [Tooltip("移動対象のTransform（VRMインスタンス）")]
        public Transform characterTransform;

        [Header("NavMesh Settings")]
        [Tooltip("NavMeshAgent（自動取得）")]
        public NavMeshAgent agent;

        [Header("Movement Settings")]
        [Tooltip("歩行速度")]
        public float walkSpeed = 1.5f;

        [Tooltip("走行速度")]
        public float runSpeed = 4f;

        [Tooltip("回転速度")]
        public float rotationSpeed = 360f;

        [Tooltip("走りに切り替える距離")]
        public float runThreshold = 5f;

        [Tooltip("到着判定距離")]
        public float arrivalThreshold = 0.1f;

        [Tooltip("角度差がこれ以上なら回転モーションを使用")]
        public float turnAnimationThreshold = 45f;

        [Tooltip("この距離以下でtarget方向への直接回転に切替（到着時ジグザグ防止）")]
        public float finalApproachDistance = 1.0f;

        [Header("Animation Speed Matching")]
        [Tooltip("歩行アニメーションのRoot Motion速度（units/sec）")]
        public float walkAnimationSpeed = 1.0f;

        [Tooltip("走行アニメーションのRoot Motion速度（units/sec）")]
        public float runAnimationSpeed = 4.0f;

        [Header("Stuck Detection")]
        [Tooltip("この時間（秒）以上velocityが低い場合、スタックと判定して移動中止")]
        public float stuckTimeout = 3f;

        [Tooltip("全体の移動タイムアウト（秒）")]
        public float movementTimeout = 30f;

        [Tooltip("スタック判定の速度閾値（units/sec）")]
        public float stuckVelocityThreshold = 0.05f;

        [Header("State")]
        [SerializeField]
        private NavigationState _currentState = NavigationState.Idle;
        public NavigationState CurrentState => _currentState;

        [SerializeField]
        private Vector3 _targetPosition;

        [SerializeField]
        private Quaternion _targetRotation;

        [SerializeField]
        private bool _useRun = false;

        // 前方歩行モード（Wキー歩行等）
        private bool _isForwardWalking;

        // 現在のインタラクションリクエスト
        private InteractionRequest _currentInteraction;
        private Action _onArrivalCallback;
        private Action<InteractionRequest> _onInteractionReadyCallback;

        // MoveSpeedTrackからの制御値
        private float _moveSpeedMultiplier = 1f;
        private bool _adjustAnimatorSpeedEnabled = true;

        // スタック検出用
        private float _stuckTimer;
        private float _movementTimer;
        private bool _pathChecked;

        // 最終接近フェーズ（agent.Move()による直接移動モード）
        private bool _inFinalApproach;

        private void Awake()
        {
            // NavMeshAgent取得
            if (agent == null)
            {
                agent = GetComponent<NavMeshAgent>();
            }

            // Animator取得
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            SetupNavMeshAgent();
        }

        /// <summary>
        /// NavMeshAgentの初期設定
        /// NavMeshAgentが位置を直接制御し、回転は手動制御
        /// </summary>
        /// <summary>
        /// MoveSpeedTrackから移動速度の乗算値を設定
        /// </summary>
        /// <param name="multiplier">速度乗算値（0.0〜1.0等）</param>
        /// <param name="adjustAnimatorSpeed">アニメーション再生速度をAgent速度に合わせるか</param>
        public void SetMoveSpeedMultiplier(float multiplier, bool adjustAnimatorSpeed)
        {
            _moveSpeedMultiplier = multiplier;
            _adjustAnimatorSpeedEnabled = adjustAnimatorSpeed;
        }

        private void SetupNavMeshAgent()
        {
            if (agent == null) return;

            // NavMeshAgentがCharacterRootの位置を制御
            agent.updatePosition = true;
            // 回転は手動制御（agent.desiredVelocityで滑らかに回転するため）
            agent.updateRotation = false;

            agent.speed = walkSpeed;
            agent.angularSpeed = rotationSpeed;
            agent.stoppingDistance = arrivalThreshold;
        }

        private void Update()
        {
            switch (_currentState)
            {
                case NavigationState.TurningToTarget:
                    UpdateTurning();
                    break;

                case NavigationState.Moving:
                    UpdateMoving();
                    break;

                case NavigationState.FinalTurning:
                    UpdateFinalTurning();
                    break;

                case NavigationState.ApproachingInteraction:
                    UpdateApproachingInteraction();
                    break;
            }
        }

        /// <summary>
        /// Root Motionの適用（RootMotionForwarderから呼ばれる）
        /// OnAnimatorMoveはAnimatorと同じGameObjectでしか呼ばれないため、
        /// VRMインスタンスに配置したRootMotionForwarderから委譲される
        ///
        /// NavMeshAgent駆動方式:
        /// - 移動中（Moving/ApproachingInteraction/Turning/FinalTurning）: Root Motionを無視（agentが制御）
        /// - 位置保持モード（Idle/Talk/Emote）: Root Motionを無視（位置保持が優先）
        /// - Interact等: Root Motionをローカル座標で適用（BlendPivot相対の微調整）
        /// </summary>
        public void ApplyRootMotion(Vector3 deltaPosition, Quaternion deltaRotation)
        {
            if (characterTransform == null) return;

            // 位置保持モード（Idle/Talk/Emote）ではRoot Motionを適用しない
            if (animationController != null && animationController.ShouldPreservePosition)
            {
                bool hasPosition = deltaPosition.sqrMagnitude > 0.0001f;
                bool hasRotation = Quaternion.Angle(Quaternion.identity, deltaRotation) > 0.01f;

                if (hasPosition || hasRotation)
                {
                    Debug.Log($"[CharacterNavigationController] Root Motion SKIPPED (position preserved): deltaPos={deltaPosition}, deltaRot={deltaRotation.eulerAngles}");
                }
                return;
            }

            // 移動中はNavMeshAgentが位置を制御するため、Root Motionを適用しない
            if (_currentState == NavigationState.Moving ||
                _currentState == NavigationState.ApproachingInteraction ||
                _currentState == NavigationState.TurningToTarget ||
                _currentState == NavigationState.FinalTurning)
            {
                return;
            }

            // Walk/Runアニメーション中はRoot Motionを適用しない（安全策）
            // NavMeshAgentを経由しない歩行開始（JSON入力等）でもRoot Motionドリフトを防止
            if (animationController != null &&
                (animationController.CurrentState == AnimationStateType.Walk ||
                 animationController.CurrentState == AnimationStateType.Run))
            {
                return;
            }

            // ループジャンプフラグのチェック（Interact等のループ再生時の巻き戻しデルタを除外）
            if (animationController != null && animationController.ConsumeLoopJumpFlag())
            {
                Debug.Log("[CharacterNavigationController] Root Motion SKIPPED (loop jump detected)");
                return;
            }

            // Interact等: Root Motionをローカル座標で適用
            // BlendPivot方式：VRMはローカル座標でアニメーション、BlendPivotがワールド補間を担当
            Transform parent = characterTransform.parent;
            if (parent != null)
            {
                Vector3 localDelta = Quaternion.Inverse(parent.rotation) * deltaPosition;
                characterTransform.localPosition += localDelta;
            }
            else
            {
                characterTransform.position += deltaPosition;
            }
            characterTransform.localRotation *= deltaRotation;

            // デバッグ: 有意なdeltaがある場合のみログ出力
            bool hasPos = deltaPosition.sqrMagnitude > 0.0001f;
            bool hasRot = Quaternion.Angle(Quaternion.identity, deltaRotation) > 0.01f;

            if (hasPos || hasRot)
            {
                Debug.Log($"[CharacterNavigationController] Root Motion applied: state={_currentState}, deltaPos={deltaPosition}, deltaRot={deltaRotation.eulerAngles}, newPos={characterTransform.position}, newRot={characterTransform.rotation.eulerAngles}");
            }
        }

        /// <summary>
        /// 指定位置・回転に移動
        /// </summary>
        public void MoveTo(Vector3 position, Quaternion rotation, Action onArrival = null)
        {
            _targetPosition = position;
            _targetRotation = rotation;
            _onArrivalCallback = onArrival;
            _currentInteraction = null;

            StartNavigation();
        }

        /// <summary>
        /// 家具へのインタラクションを開始
        /// </summary>
        public void MoveToInteraction(InteractionRequest request, Action<InteractionRequest> onReady)
        {
            if (request == null || request.targetPoint == null)
            {
                Debug.LogWarning("[CharacterNavigationController] Invalid interaction request");
                return;
            }

            _currentInteraction = request;
            _targetPosition = request.targetPoint.position;
            _targetRotation = request.targetPoint.rotation;
            _onInteractionReadyCallback = onReady;
            _onArrivalCallback = null;

            StartNavigation();
        }

        /// <summary>
        /// ナビゲーション開始
        /// </summary>
        private void StartNavigation()
        {
            // スタック検出タイマーリセット
            _stuckTimer = 0f;
            _movementTimer = 0f;
            _pathChecked = false;
            _inFinalApproach = false;

            // CharacterRoot（NavMeshAgent所在）の位置を基準に計算
            Vector3 currentPos = transform.position;

            // 距離に応じて走りか歩きか決定
            float distance = Vector3.Distance(currentPos, _targetPosition);
            _useRun = distance > runThreshold;

            // NavMeshAgentに目的地を設定（パス計算のみ、移動はStartMoving()で開始）
            if (agent != null && agent.isOnNavMesh)
            {
                agent.ResetPath();
                agent.isStopped = true;
                agent.velocity = Vector3.zero;
                agent.speed = _useRun ? runSpeed : walkSpeed;
                agent.SetDestination(_targetPosition);
            }

            // 角度差を計算（Y成分を除外してから正規化）
            Vector3 direction = _targetPosition - currentPos;
            direction.y = 0;

            // CharacterRoot（transform）の向きで判定
            Vector3 currentForward = transform.forward;

            if (direction.sqrMagnitude > 0.01f)
            {
                float angle = Vector3.SignedAngle(currentForward, direction.normalized, Vector3.up);
                Debug.Log($"[CharacterNavigationController] StartNavigation: angle={angle:F1}°, threshold={turnAnimationThreshold}, direction={direction}");

                // 角度差が大きい場合は回転アニメーションを使用
                if (Mathf.Abs(angle) > turnAnimationThreshold)
                {
                    StartTurning(angle);
                }
                else
                {
                    StartMoving();
                }
            }
            else
            {
                // すでに目標位置にいる場合
                if (_currentInteraction != null)
                {
                    // インタラクション: 歩行を経由せず直接準備完了
                    Debug.Log("[CharacterNavigationController] Already at target, skipping walk for interaction");
                    OnInteractionReady();
                }
                else
                {
                    // 通常移動: 回転のみ
                    StartFinalTurning();
                }
            }
        }

        /// <summary>
        /// 移動をキャンセル
        /// </summary>
        public void StopMoving()
        {
            _currentState = NavigationState.Idle;
            _isForwardWalking = false;
            _inFinalApproach = false;
            _onArrivalCallback = null;
            _onInteractionReadyCallback = null;
            _currentInteraction = null;
            _stuckTimer = 0f;
            _movementTimer = 0f;
            _moveSpeedMultiplier = 1f;
            _adjustAnimatorSpeedEnabled = true;

            ResetAnimatorSpeed();

            if (agent != null && agent.isOnNavMesh)
            {
                agent.ResetPath();
                agent.isStopped = true;
            }

            animationController?.ReturnToIdle();
        }

        /// <summary>
        /// 前方歩行開始（デバッグ用: Wキー歩行等）
        /// agent.Move()で前方に直接移動（NavMesh境界を尊重）
        /// アニメーション終了制御（walk_ed等）は呼び出し元で処理する
        /// </summary>
        public void StartForwardWalk()
        {
            _isForwardWalking = true;
            _useRun = false;
            _currentInteraction = null;
            _onArrivalCallback = null;
            _onInteractionReadyCallback = null;
            _currentState = NavigationState.Moving;

            // パス追従ではなくagent.Move()で手動制御するため、パスをクリア
            if (agent != null && agent.isOnNavMesh)
            {
                agent.ResetPath();
                agent.isStopped = true;
            }

            animationController?.PlayAnimation("common_walk01");
            Debug.Log("[CharacterNavigationController] StartForwardWalk");
        }

        /// <summary>
        /// 前方歩行停止（NavMeshAgent側のみ停止）
        /// アニメーション制御（walk_ed→Idle等）は呼び出し元で処理する
        /// </summary>
        public void StopForwardWalk()
        {
            _isForwardWalking = false;
            _inFinalApproach = false;
            _currentState = NavigationState.Idle;

            ResetAnimatorSpeed();

            if (agent != null && agent.isOnNavMesh)
            {
                agent.ResetPath();
                agent.isStopped = true;
            }

            Debug.Log("[CharacterNavigationController] StopForwardWalk");
        }

        private void StartTurning(float angle)
        {
            _currentState = NavigationState.TurningToTarget;

            // ターン中はagentの移動を停止
            if (agent != null && agent.isOnNavMesh)
            {
                agent.isStopped = true;
            }

            // 左右どちらに回転するか
            string turnAnim = angle < 0
                ? (_useRun ? "common_runturn01" : "common_walkturn01")  // 左回転
                : (_useRun ? "common_runturn02" : "common_walkturn02"); // 右回転

            animationController?.PlayAnimation(turnAnim);
        }

        private void UpdateTurning()
        {
            // CharacterRoot（transform）を回転（VRMは子として追従）
            Vector3 direction = _targetPosition - transform.position;
            direction.y = 0;

            if (direction.sqrMagnitude > 0.01f)
            {
                Quaternion targetRot = Quaternion.LookRotation(direction.normalized);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    targetRot,
                    rotationSpeed * Time.deltaTime
                );

                // 十分に向いたら移動開始
                float remainingAngle = Quaternion.Angle(transform.rotation, targetRot);
                if (remainingAngle < 5f)
                {
                    StartMoving();
                }
            }
            else
            {
                StartMoving();
            }
        }

        private void StartMoving()
        {
            _currentState = _currentInteraction != null
                ? NavigationState.ApproachingInteraction
                : NavigationState.Moving;

            // 回転フェーズ中のvelocity=0をスタックと誤判定しないようリセット
            _stuckTimer = 0f;

            // agentの移動を再開
            if (agent != null && agent.isOnNavMesh)
            {
                agent.isStopped = false;
            }

            string moveAnim = _useRun ? "common_run01" : "common_walk01";
            animationController?.PlayAnimation(moveAnim);
        }

        /// <summary>
        /// NavMeshAgent駆動の移動更新
        /// agentが位置を制御し、desiredVelocityで回転、再生速度を調整
        /// </summary>
        private void UpdateMoving()
        {
            // 前方歩行中: agent.Move()で前方に直接移動（NavMesh境界を尊重）
            if (_isForwardWalking)
            {
                if (agent != null && agent.isOnNavMesh)
                {
                    Vector3 move = transform.forward * walkSpeed * Time.deltaTime;
                    agent.Move(move);
                }
                return;
            }

            // --- 以下、通常のNavMeshAgent経路追従移動 ---

            // 最終接近フェーズ: agent.Move()で直接移動（ステアリング揺れ防止）
            if (_inFinalApproach)
            {
                UpdateMovementRotation();
                if (UpdateFinalApproach())
                {
                    ResetAnimatorSpeed();
                    StartFinalTurning();
                }
                return;
            }

            // パス有効性チェック & スタック検出
            if (!CheckNavigationHealth()) return;

            // 最終接近フェーズへの切替判定
            if (agent != null && agent.isOnNavMesh && !agent.pathPending)
            {
                if (agent.remainingDistance <= finalApproachDistance)
                {
                    EnterFinalApproach();
                    UpdateMovementRotation();
                    return;
                }
            }

            // 移動方向に合わせてCharacterRootを回転
            UpdateMovementRotation();

            // MoveSpeedTrackからの速度乗算を適用
            if (agent != null && agent.isOnNavMesh)
            {
                float baseSpeed = _useRun ? runSpeed : walkSpeed;
                agent.speed = baseSpeed * _moveSpeedMultiplier;
            }

            // アニメーション再生速度をagent速度に合わせる（足滑り防止）
            // MoveSpeedClipでadjustAnimatorSpeed=falseの場合は定速再生（グラフ速度1.0）
            if (_adjustAnimatorSpeedEnabled)
            {
                AdjustAnimatorSpeed();
            }
            else
            {
                ResetAnimatorSpeed();
            }

            // 到着判定
            if (agent != null && agent.isOnNavMesh)
            {
                if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
                {
                    ResetAnimatorSpeed();
                    StartFinalTurning();
                }
            }
        }

        /// <summary>
        /// インタラクション接近中の移動更新
        /// </summary>
        private void UpdateApproachingInteraction()
        {
            // 最終接近フェーズ: agent.Move()で直接移動（ステアリング揺れ防止）
            if (_inFinalApproach)
            {
                UpdateMovementRotation();

                // インタラクション接近半径チェック（arrivalThreshold到着前に到達する場合）
                if (_currentInteraction != null)
                {
                    float dist = Vector3.Distance(transform.position, _targetPosition);
                    if (dist <= _currentInteraction.approachRadius)
                    {
                        _inFinalApproach = false;
                        ResetAnimatorSpeed();
                        OnInteractionReady();
                        return;
                    }
                }

                if (UpdateFinalApproach())
                {
                    ResetAnimatorSpeed();
                    if (_currentInteraction != null)
                    {
                        OnInteractionReady();
                    }
                    else
                    {
                        StartFinalTurning();
                    }
                }
                return;
            }

            // パス有効性チェック & スタック検出
            if (!CheckNavigationHealth()) return;

            // 最終接近フェーズへの切替判定
            if (agent != null && agent.isOnNavMesh && !agent.pathPending)
            {
                if (agent.remainingDistance <= finalApproachDistance)
                {
                    EnterFinalApproach();
                    UpdateMovementRotation();
                    return;
                }
            }

            // 移動方向に合わせてCharacterRootを回転
            UpdateMovementRotation();

            // MoveSpeedTrackからの速度乗算を適用
            if (agent != null && agent.isOnNavMesh)
            {
                float baseSpeed = _useRun ? runSpeed : walkSpeed;
                agent.speed = baseSpeed * _moveSpeedMultiplier;
            }

            // アニメーション再生速度をagent速度に合わせる
            if (_adjustAnimatorSpeedEnabled)
            {
                AdjustAnimatorSpeed();
            }
            else
            {
                ResetAnimatorSpeed();
            }

            // インタラクション接近判定（CharacterRootの位置で判定）
            if (_currentInteraction != null)
            {
                float distance = Vector3.Distance(transform.position, _targetPosition);
                float approachRadius = _currentInteraction.approachRadius;

                if (distance <= approachRadius)
                {
                    ResetAnimatorSpeed();
                    OnInteractionReady();
                    return;
                }
            }

            // 通常の到着判定
            if (agent != null && agent.isOnNavMesh)
            {
                if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
                {
                    ResetAnimatorSpeed();
                    if (_currentInteraction != null)
                    {
                        OnInteractionReady();
                    }
                    else
                    {
                        StartFinalTurning();
                    }
                }
            }
        }

        /// <summary>
        /// インタラクション準備完了
        /// </summary>
        private void OnInteractionReady()
        {
            _currentState = NavigationState.Idle;

            ResetAnimatorSpeed();

            if (agent != null && agent.isOnNavMesh)
            {
                agent.ResetPath();
                agent.isStopped = true;
            }

            // コールバック呼び出し
            var callback = _onInteractionReadyCallback;
            var interaction = _currentInteraction;

            _onInteractionReadyCallback = null;
            _currentInteraction = null;

            callback?.Invoke(interaction);
        }

        private void StartFinalTurning()
        {
            _currentState = NavigationState.FinalTurning;

            if (agent != null && agent.isOnNavMesh)
            {
                agent.ResetPath();
                agent.isStopped = true;
            }
        }

        private void UpdateFinalTurning()
        {
            // CharacterRoot（transform）を目標回転に向ける
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                _targetRotation,
                rotationSpeed * Time.deltaTime
            );

            float remainingAngle = Quaternion.Angle(transform.rotation, _targetRotation);
            if (remainingAngle < 1f)
            {
                // 到着完了
                _currentState = NavigationState.Idle;

                var callback = _onArrivalCallback;
                _onArrivalCallback = null;

                // Walk終了フェーズ（walk_ed）を再生してからIdleに遷移
                // LoopRegionがない場合は即座にIdle遷移
                animationController?.StopWalkWithEndPhase();

                callback?.Invoke();
            }
        }

        // --- 最終接近フェーズ ---

        /// <summary>
        /// 最終接近フェーズに移行。
        /// NavMeshAgentの自動ステアリングを停止し、agent.Move()による手動移動に切り替える。
        /// NavMeshAgentのステアリングによる到着付近の軌跡揺れを防止する。
        /// </summary>
        private void EnterFinalApproach()
        {
            _inFinalApproach = true;
            if (agent != null && agent.isOnNavMesh)
            {
                agent.isStopped = true;
            }
        }

        /// <summary>
        /// 最終接近フェーズの移動処理。
        /// NavMeshAgentのステアリングを使わず、ターゲットへ直接移動する。
        /// agent.Move()はNavMesh境界を尊重するため、NavMesh外への移動は防止される。
        /// </summary>
        /// <returns>true: arrivalThreshold以内に到着</returns>
        private bool UpdateFinalApproach()
        {
            Vector3 direction = _targetPosition - transform.position;
            direction.y = 0;
            float distance = direction.magnitude;

            if (distance <= arrivalThreshold)
            {
                _inFinalApproach = false;
                return true;
            }

            float moveSpeed = (_useRun ? runSpeed : walkSpeed) * _moveSpeedMultiplier;
            float step = moveSpeed * Time.deltaTime;
            Vector3 move = direction.normalized * Mathf.Min(step, distance);
            agent.Move(move);

            // アニメーション再生速度を意図した移動速度で調整
            // （agent.velocityは最終接近フェーズ中は0になるため使用不可）
            if (_adjustAnimatorSpeedEnabled)
            {
                AdjustAnimatorSpeed(moveSpeed);
            }
            else
            {
                ResetAnimatorSpeed();
            }

            return false;
        }

        // --- 移動中の回転制御 ---

        /// <summary>
        /// 移動中のキャラクター回転を更新
        /// 直線パス or 近距離ではtarget方向への直接回転を使用し、
        /// desiredVelocityのノイズによるジグザグを防止する
        /// </summary>
        private void UpdateMovementRotation()
        {
            if (agent == null || !agent.isOnNavMesh) return;
            if (!_inFinalApproach && !agent.hasPath) return;

            Vector3 rotDirection;

            if (_inFinalApproach ||
                (!agent.pathPending &&
                (agent.remainingDistance <= finalApproachDistance || agent.path.corners.Length <= 2)))
            {
                // 近距離 or 直線パス: target方向への直接回転
                // desiredVelocityは近距離で小さくノイジーになり、方向が不安定になるため
                rotDirection = _targetPosition - transform.position;
            }
            else
            {
                // 遠距離かつ曲がりパス: NavMeshAgentのdesiredVelocityに追従
                rotDirection = agent.desiredVelocity;
            }

            rotDirection.y = 0;
            if (rotDirection.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(rotDirection.normalized);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    targetRot,
                    rotationSpeed * Time.deltaTime
                );
            }
        }

        // --- アニメーション速度制御 ---

        /// <summary>
        /// NavMeshAgentの実速度に合わせてTimeline再生速度を調整（足滑り防止）
        /// </summary>
        private void AdjustAnimatorSpeed(float overrideSpeed = -1f)
        {
            if (animationController == null || animationController.director == null) return;

            float agentSpeed = overrideSpeed >= 0f ? overrideSpeed : (agent != null ? agent.velocity.magnitude : 0f);
            float animSpeed = _useRun ? runAnimationSpeed : walkAnimationSpeed;

            if (animSpeed > 0.001f)
            {
                float targetSpeed = agentSpeed / animSpeed;
                targetSpeed = Mathf.Clamp(targetSpeed, 0.1f, 2.0f);

                // PlayableGraphの再生速度を調整
                var graph = animationController.director.playableGraph;
                if (graph.IsValid())
                {
                    graph.GetRootPlayable(0).SetSpeed(targetSpeed);
                }
            }
        }

        /// <summary>
        /// Timeline再生速度を1.0にリセット
        /// </summary>
        private void ResetAnimatorSpeed()
        {
            if (animationController == null || animationController.director == null) return;

            var graph = animationController.director.playableGraph;
            if (graph.IsValid())
            {
                graph.GetRootPlayable(0).SetSpeed(1.0);
            }
        }

        // --- ナビゲーションヘルスチェック ---

        /// <summary>
        /// パスの有効性チェックとスタック検出
        /// Moving / ApproachingInteraction のUpdate内で毎フレーム呼び出す
        /// </summary>
        /// <returns>true: 移動続行, false: 移動を中止した</returns>
        private bool CheckNavigationHealth()
        {
            if (agent == null || !agent.isOnNavMesh) return true;

            // パス有効性チェック（pathPending解消後に1回だけ）
            if (!_pathChecked && !agent.pathPending)
            {
                _pathChecked = true;

                switch (agent.pathStatus)
                {
                    case NavMeshPathStatus.PathInvalid:
                        Debug.LogWarning("[CharacterNavigationController] Path is invalid, aborting movement");
                        HandleNavigationFailure();
                        return false;

                    case NavMeshPathStatus.PathPartial:
                        Debug.LogWarning("[CharacterNavigationController] Path is partial, moving to nearest reachable point");
                        break;

                    // PathComplete: 正常続行
                }
            }

            // スタック検出（velocity監視）
            if (agent.velocity.magnitude < stuckVelocityThreshold)
            {
                _stuckTimer += Time.deltaTime;
                if (_stuckTimer >= stuckTimeout)
                {
                    Debug.LogWarning($"[CharacterNavigationController] Stuck detected (velocity < {stuckVelocityThreshold} for {stuckTimeout}s)");
                    HandleNavigationFailure();
                    return false;
                }
            }
            else
            {
                _stuckTimer = 0f;
            }

            // 全体タイムアウト
            _movementTimer += Time.deltaTime;
            if (_movementTimer >= movementTimeout)
            {
                Debug.LogWarning($"[CharacterNavigationController] Movement timeout ({movementTimeout}s exceeded)");
                HandleNavigationFailure();
                return false;
            }

            return true;
        }

        /// <summary>
        /// ナビゲーション失敗時の処理
        /// 移動を中止してIdleに戻り、コールバックを呼び出す
        /// </summary>
        private void HandleNavigationFailure()
        {
            ResetAnimatorSpeed();

            _currentState = NavigationState.Idle;
            _isForwardWalking = false;
            _inFinalApproach = false;
            _stuckTimer = 0f;
            _movementTimer = 0f;
            _moveSpeedMultiplier = 1f;
            _adjustAnimatorSpeedEnabled = true;

            if (agent != null && agent.isOnNavMesh)
            {
                agent.ResetPath();
                agent.isStopped = true;
            }

            animationController?.ReturnToIdle();

            // コールバック呼び出し（現在位置で完了扱い）
            var arrivalCallback = _onArrivalCallback;
            var interactionCallback = _onInteractionReadyCallback;
            var interaction = _currentInteraction;

            _onArrivalCallback = null;
            _onInteractionReadyCallback = null;
            _currentInteraction = null;

            arrivalCallback?.Invoke();
            if (interaction != null)
            {
                interactionCallback?.Invoke(interaction);
            }
        }

        // --- ユーティリティ ---

        /// <summary>
        /// 現在の移動速度を取得（アニメーションブレンド用）
        /// </summary>
        public float GetCurrentSpeed()
        {
            if (_currentState != NavigationState.Moving &&
                _currentState != NavigationState.ApproachingInteraction)
            {
                return 0f;
            }

            if (agent != null && agent.isOnNavMesh)
            {
                return agent.velocity.magnitude;
            }

            return _useRun ? runSpeed : walkSpeed;
        }

        /// <summary>
        /// NavMeshAgent上にいるか確認
        /// </summary>
        public bool IsOnNavMesh()
        {
            return agent != null && agent.isOnNavMesh;
        }

        /// <summary>
        /// 指定位置にワープ（NavMesh対応）
        /// agentをワープし、VRMローカルをリセット
        /// </summary>
        public bool Warp(Vector3 position)
        {
            _currentState = NavigationState.Idle;
            _isForwardWalking = false;
            _inFinalApproach = false;

            if (agent != null)
            {
                agent.ResetPath();
                bool result = agent.Warp(position);
                if (result && characterTransform != null)
                {
                    characterTransform.localPosition = Vector3.zero;
                    characterTransform.localRotation = Quaternion.identity;
                }
                return result;
            }

            transform.position = position;
            if (characterTransform != null)
            {
                characterTransform.localPosition = Vector3.zero;
                characterTransform.localRotation = Quaternion.identity;
            }
            return true;
        }

        /// <summary>
        /// 指定位置・回転にワープ（NavMesh対応）
        /// </summary>
        public bool Warp(Vector3 position, Quaternion rotation)
        {
            bool result = Warp(position);
            if (result)
            {
                transform.rotation = rotation;
            }
            return result;
        }

        private void OnDrawGizmosSelected()
        {
            if (_currentState == NavigationState.Idle) return;

            Vector3 currentPos = transform.position;

            // 目標位置を表示
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(_targetPosition, 0.2f);
            Gizmos.DrawLine(currentPos, _targetPosition);

            // 目標方向を表示
            Gizmos.color = Color.blue;
            Gizmos.DrawRay(_targetPosition, _targetRotation * Vector3.forward * 0.5f);

            // インタラクション接近半径を表示
            if (_currentInteraction != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(_targetPosition, _currentInteraction.approachRadius);
            }

            // NavMeshパスを表示
            if (agent != null && agent.hasPath)
            {
                Gizmos.color = Color.cyan;
                var path = agent.path;
                for (int i = 0; i < path.corners.Length - 1; i++)
                {
                    Gizmos.DrawLine(path.corners[i], path.corners[i + 1]);
                }
            }
        }
    }

    public enum NavigationState
    {
        Idle,                   // 停止中
        TurningToTarget,        // 目標方向に回転中
        Moving,                 // 移動中
        FinalTurning,           // 最終回転中
        ApproachingInteraction  // インタラクション接近中
    }
}
