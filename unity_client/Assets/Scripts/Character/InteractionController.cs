using System.Collections;
using UnityEngine;
using UnityEngine.Timeline;
using CyanNook.Furniture;
using CyanNook.Timeline;

namespace CyanNook.Character
{
    /// <summary>
    /// インタラクションの状態管理とTimeline制御
    /// ループ制御・キャンセル判定はCharacterAnimationControllerに委譲
    /// </summary>
    public class InteractionController : MonoBehaviour
    {
        [Header("References")]
        public CharacterAnimationController animationController;
        public CharacterNavigationController navigationController;

        [Tooltip("回転補間用のピボット（VRMの親）")]
        public Transform blendPivot;

        [Header("Inertial Blend")]
        [Tooltip("慣性補間ヘルパー（VRMインスタンスにアタッチ）")]
        public InertialBlendHelper inertialBlendHelper;

        [Header("State")]
        [SerializeField]
        private InteractionState _currentState = InteractionState.None;
        public InteractionState CurrentState => _currentState;

        [SerializeField]
        private InteractionRequest _currentRequest;

        // ループ突入時のコールバック（Sleep等で使用）
        private System.Action _onLoopEnteredCallback;

        // インタラクション完了時のコールバック（Outing等で使用）
        private System.Action _onInteractionCompleteCallback;

        // インタラクション中の位置固定用
        private Vector3 _interactionTargetPosition;
        private Quaternion _interactionTargetRotation;
        private Transform _characterTransform;

        // 家具アニメーション連動
        private FurnitureAnimationController _activeFurnitureAnimController;

        /// <summary>
        /// CharacterAnimationControllerのイベント購読をセットアップ
        /// VrmLoaderから呼び出される
        /// </summary>
        public void SetupEventSubscriptions()
        {
            if (animationController == null)
            {
                Debug.LogWarning("[InteractionController] AnimationController is null, cannot setup events");
                return;
            }

            // 既存の購読を解除（重複防止）
            UnsubscribeEvents();

            // イベント購読
            animationController.OnLoopEntered += OnAnimLoopEntered;
            animationController.OnEndPhaseComplete += OnAnimEndPhaseComplete;
            animationController.OnInteractionEndReached += OnAnimInteractionEndReached;
            animationController.OnCancelExecuted += OnAnimCancelExecuted;
            animationController.OnCancelRegionReached += OnAnimCancelRegionReached;

            Debug.Log("[InteractionController] Event subscriptions setup complete");
        }

        private void UnsubscribeEvents()
        {
            if (animationController != null)
            {
                animationController.OnLoopEntered -= OnAnimLoopEntered;
                animationController.OnEndPhaseComplete -= OnAnimEndPhaseComplete;
                animationController.OnInteractionEndReached -= OnAnimInteractionEndReached;
                animationController.OnCancelExecuted -= OnAnimCancelExecuted;
                animationController.OnCancelRegionReached -= OnAnimCancelRegionReached;
            }
        }

        private void OnDestroy()
        {
            UnsubscribeEvents();
            CancelPendingActionCoroutine();
        }

        // --- CharacterAnimationControllerイベントハンドラ ---

        /// <summary>
        /// LoopRegionのLoopStart到達時
        /// </summary>
        private void OnAnimLoopEntered()
        {
            if (_currentState != InteractionState.StartingInteraction) return;

            _currentState = InteractionState.InLoop;
            Debug.Log("[InteractionController] Entered loop (OnLoopEntered)");

            // ループ突入コールバック（Sleep等で使用）
            var callback = _onLoopEnteredCallback;
            _onLoopEnteredCallback = null;
            callback?.Invoke();
        }

        /// <summary>
        /// ed再生完了時
        /// </summary>
        private void OnAnimEndPhaseComplete()
        {
            if (_currentState != InteractionState.Ending && _currentState != InteractionState.StartingInteraction) return;

            Debug.Log("[InteractionController] End phase complete (OnEndPhaseComplete)");
            OnInteractionComplete();
        }

        /// <summary>
        /// InteractionEndClip到達時（LoopRegionなしInteractの完了検知）
        /// Timeline上に配置したInteractionEndClipの位置で確実に発火する
        /// </summary>
        private void OnAnimInteractionEndReached()
        {
            if (_currentState != InteractionState.StartingInteraction) return;

            Debug.Log("[InteractionController] InteractionEnd reached (OnInteractionEndReached)");
            OnInteractionComplete();
        }

        /// <summary>
        /// キャンセル実行時（CharacterAnimationControllerが遷移を実行した後）
        /// </summary>
        private void OnAnimCancelExecuted(TimelineAsset targetTimeline)
        {
            if (_currentState == InteractionState.None) return;

            Debug.Log($"[InteractionController] Cancel executed → {targetTimeline.name}");

            // 家具アニメーション停止
            StopFurnitureAnimation();

            // 家具を解放
            _currentRequest?.End();

            // BlendPivotリセット・位置復元
            ResetBlendPivotAndPosition();

            // 状態リセット
            _currentState = InteractionState.None;
            _currentRequest = null;
        }

        /// <summary>
        /// CancelRegion到達時（edフェーズスキップでインタラクション終了）
        /// </summary>
        private void OnAnimCancelRegionReached()
        {
            if (_currentState != InteractionState.Ending) return;

            Debug.Log("[InteractionController] CancelRegion reached, skipping ed phase");

            // Timeline停止（_isEnding=falseなのでCompleteEndPhaseは発火しない）
            animationController?.Stop();

            // 家具アニメーション停止
            StopFurnitureAnimation();

            // --- OnInteractionCompleteと同等のクリーンアップ（Idle経由なし） ---
            // キャンセル時はIdle中間遷移をスキップして直接次のアクションを実行する。
            // 理由: Idle経由 + 2フレーム遅延するとボーンが標準ポーズにリセットされ、
            // 次のTimeline（Walk等）のInertialBlendClipがインタラクトポーズからの
            // ブレンドを計算できなくなり、1フレームのポーズポップが発生する。

            // インタラクション終了位置を保存
            Vector3 endPosition = _characterTransform != null ? _characterTransform.position : _interactionTargetPosition;
            Quaternion endRotation = _characterTransform != null ? _characterTransform.rotation : _interactionTargetRotation;

            // 家具を解放
            if (_currentRequest != null)
            {
                _currentRequest.End();
            }

            // 状態リセット
            _currentState = InteractionState.None;
            _currentRequest = null;

            // BlendPivotリセット・位置復元
            ResetBlendPivotAndPosition(endPosition, endRotation);

            var pendingAction = _pendingAction;
            _pendingAction = null;

            if (pendingAction != null)
            {
                // ボーンがインタラクト中のポーズを保持した状態で次のTimelineを開始。
                // InertialBlendClipがインタラクトポーズ→次のポーズを正しくブレンドする。
                animationController?.SetPreservedPosition(endPosition, endRotation);
                Debug.Log("[InteractionController] Cancel: directly executing pending action (preserving pose for IB)");
                pendingAction.Invoke();
            }
            else
            {
                // pendingActionがない場合はIdleに戻る
                animationController?.SetPreservedPosition(endPosition, endRotation);
                animationController?.ReturnToIdle();
            }
        }

        // --- インタラクション開始 ---

        /// <summary>
        /// インタラクションを開始
        /// </summary>
        public void StartInteraction(InteractionRequest request)
        {
            StartInteraction(request, null, null);
        }

        /// <summary>
        /// インタラクション開始（ループ突入時コールバック付き）
        /// </summary>
        /// <param name="request">インタラクションリクエスト</param>
        /// <param name="onLoopEntered">ループリージョン突入時のコールバック（null可）</param>
        public void StartInteraction(InteractionRequest request, System.Action onLoopEntered)
        {
            StartInteraction(request, onLoopEntered, null);
        }

        /// <summary>
        /// インタラクション開始（ループ突入時 + 完了時コールバック付き）
        /// </summary>
        /// <param name="request">インタラクションリクエスト</param>
        /// <param name="onLoopEntered">ループリージョン突入時のコールバック（null可）</param>
        /// <param name="onComplete">インタラクション完了時のコールバック（null可）</param>
        public void StartInteraction(InteractionRequest request, System.Action onLoopEntered, System.Action onComplete)
        {
            if (request == null)
            {
                Debug.LogWarning("[InteractionController] Invalid interaction request");
                return;
            }

            // 遅延pendingActionコルーチンをキャンセル（新しいインタラクション開始で不要になる）
            CancelPendingActionCoroutine();

            _currentRequest = request;
            _currentState = InteractionState.Approaching;
            _onLoopEnteredCallback = onLoopEntered;
            _onInteractionCompleteCallback = onComplete;

            // ナビゲーションを開始
            navigationController.MoveToInteraction(request, OnApproachComplete);
        }

        /// <summary>
        /// 接近完了時のコールバック
        /// </summary>
        private void OnApproachComplete(InteractionRequest request)
        {
            if (request != _currentRequest)
            {
                Debug.LogWarning("[InteractionController] Request mismatch");
                return;
            }

            _currentState = InteractionState.StartingInteraction;

            // キャラクターのTransformを取得（NavigationControllerから）
            _characterTransform = navigationController?.characterTransform;

            // BlendPivot方式のセットアップ
            if (request.targetPoint != null && _characterTransform != null)
            {
                // ターゲット座標を記録
                _interactionTargetPosition = request.targetPoint.position;
                _interactionTargetRotation = request.targetPoint.rotation;

                // VRMの現在ワールド座標を記録
                Vector3 currentPosition = _characterTransform.position;
                Quaternion currentRotation = _characterTransform.rotation;

                // BlendPivotをVRMの現在ワールド位置・回転に移動
                if (blendPivot != null)
                {
                    blendPivot.position = currentPosition;
                    blendPivot.rotation = currentRotation;
                    Debug.Log($"[InteractionController] BlendPivot moved to current: pos={currentPosition}, rot={currentRotation.eulerAngles}");
                }

                // VRMのローカル座標をリセット
                _characterTransform.localPosition = Vector3.zero;
                _characterTransform.localRotation = Quaternion.identity;

                Debug.Log($"[InteractionController] VRM local reset. Target: pos={_interactionTargetPosition}, rot={_interactionTargetRotation.eulerAngles}");
            }
            else if (_characterTransform != null)
            {
                _interactionTargetPosition = _characterTransform.position;
                _interactionTargetRotation = _characterTransform.rotation;
                Debug.Log($"[InteractionController] Using current position: {_interactionTargetPosition}");
            }

            // 家具を占有
            request.Begin(transform);

            // インタラクションアニメーション再生
            string animId = GetInteractionAnimationId(request);
            if (!string.IsNullOrEmpty(animId))
            {
                PlayInteractionTimeline(animId, request);
            }
            else
            {
                Debug.LogWarning($"[InteractionController] No animation found for action: {request.action}");
                _currentState = InteractionState.InLoop;
            }
        }

        /// <summary>
        /// インタラクションアニメーションIDを取得
        /// アクション名（sit, sleep等）からアニメーションIDを生成
        /// 家具のTypeId（bed等）ではなくアクション名を使用することで、
        /// 1つの家具が複数アクションに対応できる（例: bedがsitとsleepの両方に対応）
        /// </summary>
        private string GetInteractionAnimationId(InteractionRequest request)
        {
            string action = request.action ?? "unknown";
            return $"interact_{action}01";
        }

        /// <summary>
        /// インタラクションTimelineを再生
        /// LoopRegion/CancelRegionのセットアップはCharacterAnimationController側で自動実行
        /// 家具にFurnitureAnimationControllerがある場合は同時に家具アニメーションも再生する
        /// </summary>
        private void PlayInteractionTimeline(string animId, InteractionRequest request)
        {
            if (animationController == null) return;

            // TimelineBindingDataから対応するTimelineを取得
            var timeline = animationController.GetTimelineForAnimation(animId);
            if (timeline == null)
            {
                Debug.LogWarning($"[InteractionController] Timeline not found for: {animId}");
                return;
            }

            // Blend/InertialBlendトラックの設定
            SetupBlendTracks(timeline, request);

            // 家具アニメーションの同期再生を準備
            _activeFurnitureAnimController = null;
            if (request.furniture != null)
            {
                var furnitureAnim = request.furniture.GetComponent<FurnitureAnimationController>();
                if (furnitureAnim != null && furnitureAnim.HasTimeline(animId))
                {
                    _activeFurnitureAnimController = furnitureAnim;
                }
            }

            // キャラクターTimeline再生（LoopRegion/CancelRegionはCharacterAnimationController側で自動セットアップ）
            animationController.PlayTimeline(timeline, animId, false);

            // 家具Timelineを同一フレームで再生（フレーム同期）
            if (_activeFurnitureAnimController != null)
            {
                _activeFurnitureAnimController.Play(animId);
                Debug.Log($"[InteractionController] Playing furniture animation in sync: {animId}");
            }

            Debug.Log($"[InteractionController] Playing interaction timeline: {animId}");
        }

        // --- Blendトラック設定 ---

        /// <summary>
        /// PositionBlend/RotationBlendトラックにターゲットを設定し、BlendPivotをバインド
        /// </summary>
        private void SetupBlendTracks(TimelineAsset timeline, InteractionRequest request)
        {
            if (timeline == null || request == null) return;

            var director = animationController.director;

            Vector3 targetPos = request.targetPoint != null
                ? request.targetPoint.position
                : transform.position;

            Quaternion targetRot = request.targetPoint != null
                ? request.targetPoint.rotation
                : transform.rotation;

            foreach (var track in timeline.GetOutputTracks())
            {
                if (track is PositionBlendTrack posTrack)
                {
                    posTrack.targetPosition = targetPos;
                    posTrack.hasTarget = true;
                    if (director != null && blendPivot != null)
                    {
                        director.SetGenericBinding(posTrack, blendPivot);
                    }
                    Debug.Log($"[InteractionController] Set PositionBlendTrack target: {targetPos}");
                }
                else if (track is RotationBlendTrack rotTrack)
                {
                    rotTrack.targetRotation = targetRot;
                    rotTrack.hasTarget = true;
                    if (director != null && blendPivot != null)
                    {
                        director.SetGenericBinding(rotTrack, blendPivot);
                        Debug.Log($"[InteractionController] Set RotationBlendTrack target: {targetRot.eulerAngles}");
                    }
                    else
                    {
                        Debug.LogWarning("[InteractionController] BlendPivot is not assigned, RotationBlendTrack will not work");
                    }
                }
                // InertialBlendTrackはCharacterAnimationController.PlayTimeline()で自動セットアップされる
            }
        }

        // --- ループ終了・キャンセル ---

        // ed完了後に実行する保留アクション
        private System.Action _pendingAction;

        /// <summary>
        /// ループを終了して終了フェーズへ
        /// CharacterAnimationControllerのRequestEndPhase()に委譲
        /// </summary>
        public void ExitLoop()
        {
            ExitLoopWithCallback(null);
        }

        /// <summary>
        /// ループを終了して終了フェーズへ。完了後にコールバックを実行。
        /// ed再生 → OnInteractionComplete → pendingAction実行
        /// </summary>
        /// <param name="onComplete">完了後のコールバック</param>
        /// <param name="skipCancelRegion">trueの場合、CancelRegionをスキップしてed全体を再生する（Sleep起床時等）</param>
        public void ExitLoopWithCallback(System.Action onComplete, bool skipCancelRegion = false)
        {
            if (_currentState == InteractionState.None || _currentState == InteractionState.Approaching)
            {
                // インタラクション中でない場合は即座にコールバック実行
                Debug.LogWarning("[InteractionController] Not in interactable state");
                onComplete?.Invoke();
                return;
            }

            _pendingAction = onComplete;

            Debug.Log($"[InteractionController] ExitLoop requested (state: {_currentState}, hasPendingAction: {onComplete != null}, skipCancel: {skipCancelRegion})");
            _currentState = InteractionState.Ending;

            // Thinking/Emote再生中の場合の処理:
            // ForceStopEmote/Thinking は PlayState(resumeAtLoop:true) でループ開始地点に戻すため、
            // 直後のRequestEndPhaseでJumpToEndPhaseが走り、2段階のInertialBlendが発生して
            // ポーズが飛ぶ問題がある（IB#1: emote→loop, IB#2: loop→ed で#2が#1を上書き）。
            // *ToEnd() は PlayState(resumeAtEnd:true) で_ed開始位置に直接ジャンプし、
            // 1回のIBでスムーズに遷移する。
            bool alreadyInEndPhase = false;
            if (animationController != null)
            {
                if (animationController.IsThinkingActive)
                {
                    Debug.Log("[InteractionController] ForceStopThinkingToEnd before ExitLoop");
                    animationController.ForceStopThinkingToEnd();
                    alreadyInEndPhase = true;
                }
                else if (animationController.CurrentState == AnimationStateType.Emote)
                {
                    Debug.Log("[InteractionController] ForceStopEmoteToEnd before ExitLoop");
                    animationController.ForceStopEmoteToEnd();
                    alreadyInEndPhase = true;
                }

                // ForceStop後にInteract状態に復帰していない場合（ThinkingがIdle等に復帰していた場合）
                // ed再生はスキップして直接インタラクション完了処理へ
                if (animationController.CurrentState != AnimationStateType.Interact)
                {
                    Debug.Log($"[InteractionController] Animation state is {animationController.CurrentState} (not Interact), completing immediately");
                    OnInteractionComplete();
                    return;
                }
            }

            // *ToEnd()で既にed開始位置にジャンプ済みの場合はRequestEndPhase不要
            if (!alreadyInEndPhase)
            {
                // CancelRegionがある場合: edフェーズ中のキャンセル遷移を有効化
                // RequestCancelAtRegionが即座にキャンセルを実行した場合（既にCancelRegion内）、
                // OnInteractionCompleteで_currentStateがNoneになるためRequestEndPhaseはスキップ
                // skipCancelRegion=true の場合はCancelRegionをスキップしてed全体を再生する
                if (!skipCancelRegion && animationController != null && animationController.HasCancelRegions)
                {
                    Debug.Log("[InteractionController] Cancel regions exist, requesting cancel at region");
                    animationController.RequestCancelAtRegion();
                    if (_currentState == InteractionState.None) return;
                }

                // edフェーズ再生（既にedが再生中の場合でも安全: _shouldExitLoopが設定されるのみ）
                animationController?.RequestEndPhase();
            }
        }

        /// <summary>
        /// キャンセル可能かチェックしてキャンセル
        /// CharacterAnimationControllerのCanCancelに委譲
        /// </summary>
        public bool TryCancel()
        {
            if (animationController == null || !animationController.CanCancel)
            {
                Debug.Log("[InteractionController] Cannot cancel at this time");
                return false;
            }

            CancelInteraction();
            return true;
        }

        /// <summary>
        /// 強制キャンセル（タイミング無視）
        /// </summary>
        public void ForceCancelInteraction()
        {
            CancelInteraction();
        }

        /// <summary>
        /// インタラクションをキャンセル
        /// </summary>
        private void CancelInteraction()
        {
            Debug.Log($"[InteractionController] Cancelling interaction (request: {(_currentRequest != null ? "valid" : "NULL")})");

            // 家具アニメーション停止
            StopFurnitureAnimation();

            // 保留アクション・遅延コルーチンをクリア
            _pendingAction = null;
            CancelPendingActionCoroutine();

            // VRMの現在ワールド位置を保存（BlendPivotリセット前に）
            Vector3 endPosition = _characterTransform != null ? _characterTransform.position : _interactionTargetPosition;
            Quaternion endRotation = _characterTransform != null ? _characterTransform.rotation : _interactionTargetRotation;

            // Timeline停止
            animationController?.Stop();

            // 家具を解放
            _currentRequest?.End();

            // 状態リセット
            _currentState = InteractionState.None;
            _currentRequest = null;

            // BlendPivotリセット・位置復元
            ResetBlendPivotAndPosition(endPosition, endRotation);

            // Idle再生
            animationController?.SetPreservedPosition(endPosition, endRotation);
            animationController?.ReturnToIdle();
        }

        // pendingAction遅延実行中のコルーチン
        private Coroutine _pendingActionCoroutine;

        /// <summary>
        /// pendingAction遅延実行用の待機フレーム数。
        /// interact edアニメーションのボーンローカル座標（Root Motionの影響でHips Yが低い等）と
        /// walkアニメーションのボーンローカル座標が大きく異なる場合、直接遷移すると
        /// InertialBlendが差分をブレンドし「座り戻し」が発生する。
        /// Idleに一旦遷移してAnimatorにボーンを標準ポーズに戻させてから
        /// pendingActionを実行することで、IBオフセットを最小化する。
        /// </summary>
        private const int PENDING_ACTION_DELAY_FRAMES = 2;

        /// <summary>
        /// インタラクション完了時（ed再生完了 / OnEndPhaseComplete）
        /// </summary>
        public void OnInteractionComplete()
        {
            // 既にNoneの場合は何もしない（二重呼び出し防止）
            if (_currentState == InteractionState.None)
            {
                Debug.Log("[InteractionController] OnInteractionComplete called but already in None state, ignoring");
                return;
            }

            Debug.Log($"[InteractionController] OnInteractionComplete called! State: {_currentState}");

            // 家具アニメーション停止（キャラクターより長い場合はここで止める）
            StopFurnitureAnimation();

            // インタラクション終了位置を保存
            Vector3 endPosition = _characterTransform != null ? _characterTransform.position : _interactionTargetPosition;
            Quaternion endRotation = _characterTransform != null ? _characterTransform.rotation : _interactionTargetRotation;

            // 家具を解放
            if (_currentRequest != null)
            {
                _currentRequest.End();
            }
            else
            {
                Debug.LogWarning("[InteractionController] _currentRequest is null, cannot release furniture!");
            }

            // 状態リセット
            _currentState = InteractionState.None;
            _currentRequest = null;

            // BlendPivotリセット・位置復元
            ResetBlendPivotAndPosition(endPosition, endRotation);

            // 完了コールバック（Outing等で使用）
            var completeCallback = _onInteractionCompleteCallback;
            _onInteractionCompleteCallback = null;

            var pendingAction = _pendingAction;
            _pendingAction = null;

            // 保留アクションがある場合: まずIdleに遷移し、数フレーム後にpendingActionを実行
            // 理由: interact edアニメーションはRoot Motionで縦移動を行うため、
            // ボーンのローカルY座標（Hips等）が標準の立ちポーズと大きく異なる場合がある。
            // 直接Walkに遷移するとInertialBlendが大きなオフセットをブレンドし、
            // 一瞬「座り戻し」のような動きが見える。
            // Idle経由にすることでAnimatorがボーンを標準ポーズにリセットし、
            // _lastCleanPoseが更新されてからWalk遷移のIBが走るため、オフセットが最小化される。
            if (pendingAction != null)
            {
                animationController?.SetPreservedPosition(endPosition, endRotation);
                animationController?.ReturnToIdle();
                Debug.Log("[InteractionController] Returning to Idle before pending action (sit-back prevention)");

                // 既存の遅延コルーチンをキャンセル
                if (_pendingActionCoroutine != null)
                {
                    StopCoroutine(_pendingActionCoroutine);
                }
                _pendingActionCoroutine = StartCoroutine(ExecutePendingActionDelayed(pendingAction));
            }
            else
            {
                // Idle再生
                animationController?.SetPreservedPosition(endPosition, endRotation);
                animationController?.ReturnToIdle();
            }

            // 完了コールバック呼び出し（pendingAction/Idle遷移の後）
            completeCallback?.Invoke();
        }

        /// <summary>
        /// pendingActionを数フレーム遅延して実行するコルーチン。
        /// Idle遷移後にAnimatorが標準ポーズを評価し、_lastCleanPoseが更新されるのを待つ。
        /// </summary>
        private IEnumerator ExecutePendingActionDelayed(System.Action action)
        {
            for (int i = 0; i < PENDING_ACTION_DELAY_FRAMES; i++)
            {
                yield return null;
            }
            _pendingActionCoroutine = null;

            Debug.Log("[InteractionController] Executing delayed pending action after Idle transition");
            action.Invoke();
        }

        // --- ヘルパー ---

        /// <summary>
        /// 連動中の家具アニメーションを停止
        /// </summary>
        private void StopFurnitureAnimation()
        {
            if (_activeFurnitureAnimController != null)
            {
                _activeFurnitureAnimController.StopWithCharacter();
                _activeFurnitureAnimController = null;
            }
        }

        /// <summary>
        /// 遅延pendingActionコルーチンをキャンセル
        /// </summary>
        private void CancelPendingActionCoroutine()
        {
            if (_pendingActionCoroutine != null)
            {
                StopCoroutine(_pendingActionCoroutine);
                _pendingActionCoroutine = null;
                Debug.Log("[InteractionController] Cancelled pending action coroutine");
            }
        }

        /// <summary>
        /// BlendPivotをリセットし、NavMeshAgentをインタラクション終了位置にワープ
        /// </summary>
        private void ResetBlendPivotAndPosition(Vector3? endPosition = null, Quaternion? endRotation = null)
        {
            if (blendPivot != null)
            {
                blendPivot.localPosition = Vector3.zero;
                blendPivot.localRotation = Quaternion.identity;
            }

            if (endPosition.HasValue && endRotation.HasValue)
            {
                // NavMeshAgentをインタラクション終了位置にワープ
                // CharacterRoot → endPosition, VRMローカル → identity
                if (navigationController != null)
                {
                    navigationController.Warp(endPosition.Value, endRotation.Value);
                    Debug.Log($"[InteractionController] Warped agent to: {endPosition.Value}, rot: {endRotation.Value.eulerAngles}");
                }
                else if (_characterTransform != null)
                {
                    // フォールバック: navigationControllerがない場合は直接設定
                    _characterTransform.position = endPosition.Value;
                    _characterTransform.rotation = endRotation.Value;
                    Debug.Log($"[InteractionController] Set character position to: {endPosition.Value}, rot: {endRotation.Value.eulerAngles}");
                }
            }
        }

        /// <summary>
        /// Sleep復元時のインタラクション状態を復元
        /// ExitLoopWithCallback が正しく動作するための最小状態をセットアップ
        /// </summary>
        public void RestoreInteractionState(FurnitureInstance furniture, string action,
            Vector3 targetPosition, Quaternion targetRotation)
        {
            _currentState = InteractionState.InLoop;
            _characterTransform = navigationController?.characterTransform;
            _interactionTargetPosition = targetPosition;
            _interactionTargetRotation = targetRotation;

            // InteractionRequest作成（End()で家具解放するため）
            _currentRequest = new InteractionRequest
            {
                furniture = furniture,
                action = action
            };

            // 家具を占有
            furniture?.Occupy(navigationController != null ? navigationController.transform : transform);

            // BlendPivotセットアップ（ed再生時のRoot Motion基準点）
            if (blendPivot != null && _characterTransform != null)
            {
                blendPivot.position = targetPosition;
                blendPivot.rotation = targetRotation;
                _characterTransform.localPosition = Vector3.zero;
                _characterTransform.localRotation = Quaternion.identity;
            }

            Debug.Log($"[InteractionController] Restored interaction state: action={action}, pos={targetPosition}");
        }

        /// <summary>
        /// 現在のインタラクション情報を取得
        /// </summary>
        public InteractionRequest GetCurrentInteraction()
        {
            return _currentRequest;
        }

        /// <summary>
        /// 現在インタラクト中の家具インスタンス
        /// </summary>
        public FurnitureInstance CurrentFurniture => _currentRequest?.furniture;

        /// <summary>
        /// 現在インタラクト中のアクション名（"sit", "sleep"等）
        /// </summary>
        public string CurrentAction => _currentRequest?.action;

        /// <summary>
        /// インタラクション中か
        /// </summary>
        public bool IsInteracting()
        {
            return _currentState != InteractionState.None;
        }
    }

    /// <summary>
    /// インタラクション状態
    /// </summary>
    public enum InteractionState
    {
        None,               // インタラクションなし
        Approaching,        // 家具に接近中
        StartingInteraction,// インタラクション開始アニメーション中
        InLoop,             // ループ中
        Ending              // 終了アニメーション中
    }
}
