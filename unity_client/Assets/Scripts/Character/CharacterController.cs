using UnityEngine;
using System.Collections;
using CyanNook.Core;
using CyanNook.Chat;
using CyanNook.Furniture;

namespace CyanNook.Character
{
    /// <summary>
    /// キャラクターの統合制御を担当
    /// LLM応答の action/target/emote をルーティングし、
    /// アニメーション、表情、視線、移動を統合管理
    /// </summary>
    public class CharacterController : MonoBehaviour
    {
        [Header("References")]
        public CharacterTemplateData templateData;
        public ChatManager chatManager;
        public FurnitureManager furnitureManager;

        [Header("Sub Controllers")]
        public CharacterAnimationController animationController;
        public CharacterExpressionController expressionController;
        public CharacterLookAtController lookAtController;
        public CharacterNavigationController navigationController;
        public TalkController talkController;
        public InteractionController interactionController;
        public DynamicTargetController dynamicTargetController;
        public RoomTargetManager roomTargetManager;
        public LipSyncController lipSyncController;

        [Header("Boredom")]
        public BoredomController boredomController;

        [Header("Sleep")]
        public SleepController sleepController;

        [Header("Outing")]
        public OutingController outingController;

        [Header("State")]
        [SerializeField]
        private CharacterState _currentState = CharacterState.Idle;
        public CharacterState CurrentState => _currentState;

        [Header("Thinking Exit")]
        [SerializeField, Range(0f, 2f)]
        [Tooltip("Thinking終了後、次のアクション/emote実行までの待機秒数。0で無効。" +
                 "sit中に加算thinkが解除される際のIB重複による揺れものポップを防ぐ。")]
        private float _thinkExitDelay = 0.3f;

        // Thinking終了タイミング（animationController.OnThinkingExitedで記録）
        private float _thinkExitTime = float.NegativeInfinity;

        // Think終了 grace期間中に deferされた action/emote
        // FlushAfterThinkExit コルーチンが単一で両方処理し、emote→actionの順で発火する
        private LLMResponseData _deferredAction;
        private string _deferredEmote;
        private Coroutine _thinkExitFlushCoroutine;

        // 逐次反映の待ち合わせ用
        private TargetData _pendingTarget;
        private string _pendingAction;
        private bool _hasIncrementalFields;

        // Entry再生中にストリーミングフィールドを破棄したかどうか。
        // trueの間、Entry完了後に届く後続フィールドも逐次反映せずブロック処理に委ねる
        // （フィールドが時系列で半分破棄／半分逐次反映される straddle 状態を防止）
        private bool _streamFieldsDroppedDuringEntry;

        // LookAt動的再評価用
        private TargetType _lookAtTargetType;
        private string _lookAtParam; // Talk/Named: targetName, Interact: action名

        // Sleep用: interact_sleep時のsleep_duration一時保持
        private int _pendingSleepDuration;

        private void Start()
        {
            if (chatManager != null)
            {
                chatManager.OnChatResponseReceived += HandleChatResponse;
                chatManager.OnStateChanged += HandleChatStateChanged;

                // フィールド逐次反映
                chatManager.OnStreamFieldApplied += HandleStreamField;
            }

            if (animationController != null)
            {
                animationController.OnThinkingExited += HandleThinkingExited;
            }
        }

        private void HandleThinkingExited()
        {
            _thinkExitTime = Time.time;
        }

        /// <summary>
        /// Thinking終了直後の grace期間中か判定。
        /// この期間中はIsThinkingActive=falseでも action/emote を defer して
        /// IB発火タイミングを sit_ed 等から時間方向に分離する。
        /// </summary>
        private bool IsInThinkExitGrace()
        {
            return (Time.time - _thinkExitTime) < _thinkExitDelay;
        }

        private void Update()
        {
            UpdateLookAt();
        }

        private void OnDestroy()
        {
            if (chatManager != null)
            {
                chatManager.OnChatResponseReceived -= HandleChatResponse;
                chatManager.OnStateChanged -= HandleChatStateChanged;
                chatManager.OnStreamFieldApplied -= HandleStreamField;
            }

            if (animationController != null)
            {
                animationController.OnThinkingExited -= HandleThinkingExited;
            }

            if (_thinkExitFlushCoroutine != null)
            {
                StopCoroutine(_thinkExitFlushCoroutine);
                _thinkExitFlushCoroutine = null;
            }
            if (_pendingEmoteCoroutine != null)
            {
                StopCoroutine(_pendingEmoteCoroutine);
                _pendingEmoteCoroutine = null;
            }
        }

        // ===================================================================
        // LLM応答処理
        // ===================================================================

        /// <summary>
        /// LLMレスポンスを処理（外部からの直接呼び出し用）
        /// DebugUI等、ChatManager経由でない場合に使用
        /// </summary>
        public void ProcessResponse(LLMResponseData response)
        {
            HandleChatResponse(response);
        }

        /// <summary>
        /// LLMレスポンスを処理
        /// action/target/emote/emotion を順次ルーティング
        /// </summary>
        private void HandleChatResponse(LLMResponseData response)
        {
            Debug.Log($"[CharacterController] Response: action={response.action}, target.type={response.target?.type}, emote={response.emote}");

            // Sleep中: action/target/emote/emotionを無視（夢メッセージ応答含む）
            if (sleepController != null && sleepController.IsSleeping)
            {
                Debug.Log("[CharacterController] Sleeping, ignoring response actions");
                return;
            }

            // 外出中: interact_entry以外の全レスポンスを無視
            if (outingController != null && outingController.IsOutside)
            {
                // 例外: LLMが action:"interact_entry" を返した場合のみ帰還処理
                if (response.action == "interact_entry")
                {
                    Debug.Log("[CharacterController] Outing: interact_entry selected by LLM, processing return");
                    ProcessEntryFromOuting(response);
                    return;
                }

                Debug.Log("[CharacterController] Outside, ignoring response actions");
                return;
            }

            // Entry再生中: action/target/emoteを無視（entryタイムラインの上書き防止）
            // Entry Promptの応答が到着してもentry完了まで処理を遅延しない
            if (outingController != null && outingController.IsPlayingEntry)
            {
                Debug.Log("[CharacterController] Entry animation playing, ignoring response actions");
                return;
            }

            // interact_sleep アクション時: SleepControllerへ通知
            if (response.action == "interact_sleep" && sleepController != null)
            {
                _pendingSleepDuration = response.sleep_duration;
            }

            // レスポンス境界で事前到着emoteをクリア（古いemoteが次のwalkで誤再生されないように）
            _pendingWalkEmote = null;

            // テキスト表示完了を通知（表情decay/emoteループ解除のタイマー開始）
            expressionController?.NotifyTextDisplayComplete();
            animationController?.NotifyTextDisplayComplete();

            if (_hasIncrementalFields)
            {
                // ストリーミング逐次反映済み → 口パクのみ
                Debug.Log("[CharacterController] Incremental fields already applied, processing lip sync only");
                _hasIncrementalFields = false;
                _pendingTarget = null;
                _pendingAction = null;

                if (response.HasMessage && lipSyncController != null)
                {
                    lipSyncController.StartSpeaking(response.message);
                }
                return;
            }

            // ブロッキングモード: 既存の全フィールド一括処理

            // 1. emotion 適用（常に反映）
            if (response.emotion != null)
            {
                expressionController?.SetEmotion(response.emotion);
            }

            // 2. action 処理（移動・インタラクト・ignore）
            // インタラクション中の場合はed再生後に遅延実行される（emote含む）
            bool actionDeferred = ProcessAction(response);

            // 3. target → LookAt 解決（常に反映）
            ProcessLookAt(response);

            // 4. emote 処理（アクションが遅延実行の場合はコールバック内で処理されるためスキップ）
            if (!actionDeferred && response.HasEmote)
            {
                ProcessEmote(response.emote);
            }

            // 5. 口パク開始（メッセージがある場合）
            if (response.HasMessage && lipSyncController != null)
            {
                lipSyncController.StartSpeaking(response.message);
            }
        }

        /// <summary>
        /// チャット状態変更を処理
        /// </summary>
        private void HandleChatStateChanged(ChatState state)
        {
            switch (state)
            {
                case ChatState.WaitingForResponse:
                    // 新規リクエスト開始 → Entry時の破棄フラグをリセット（ガード前に必ず実施）
                    _streamFieldsDroppedDuringEntry = false;
                    // 起床ed再生中はInteracting状態を維持（Thinking遷移しない）
                    if (sleepController != null && sleepController.IsWakingUp)
                    {
                        break;
                    }
                    // Entry再生中はThinking遷移しない（entryタイムラインの上書き防止）
                    if (outingController != null && outingController.IsPlayingEntry)
                    {
                        break;
                    }
                    SetState(CharacterState.Thinking);
                    // テキスト表示完了フラグをリセット（応答待ち開始）
                    // 表情decay/emoteループ解除のタイマーをテキスト完了まで停止する
                    expressionController?.OnResponseStarted();
                    animationController?.OnResponseStarted();
                    break;

                case ChatState.Idle:
                    if (_currentState == CharacterState.Thinking)
                    {
                        // Thinking完了 → 直前の状態に応じて復帰
                        if (talkController != null && talkController.IsInTalkMode)
                        {
                            SetState(CharacterState.TalkIdle);
                        }
                        else
                        {
                            SetState(CharacterState.Idle);
                        }
                    }
                    break;

                case ChatState.Error:
                    expressionController?.SetEmotion(new EmotionData { sad = 0.5f });
                    break;
            }
        }

        // ===================================================================
        // フィールド逐次反映（ストリーミング時）
        // ===================================================================

        /// <summary>
        /// JSONフィールドが逐次的に届いた時のハンドラ
        /// ChatManagerからtarget/action/emoteが転送される（emotionはChatManagerで直接適用済み）
        /// </summary>
        private void HandleStreamField(string fieldName, string rawValue)
        {
            // Sleep中: sleep_duration以外のフィールドは無視
            if (sleepController != null && sleepController.IsSleeping)
            {
                return;
            }

            // 外出中: 全フィールドを無視（定期メッセージの応答はHandleChatResponseで処理）
            if (outingController != null && outingController.IsOutside)
            {
                return;
            }

            // Entry再生中: 全フィールドを無視（entryタイムラインの上書き防止）。
            // 破棄したことを記録し、後続フィールドも逐次反映させない（straddle回避）
            if (outingController != null && outingController.IsPlayingEntry)
            {
                _streamFieldsDroppedDuringEntry = true;
                return;
            }

            // Entry中に破棄した分があれば、Entry完了後の後続フィールドも逐次反映せず
            // 最終のHandleChatResponse（ChatManagerがキュー排出）でブロック処理させる
            if (_streamFieldsDroppedDuringEntry)
            {
                return;
            }

            _hasIncrementalFields = true;

            switch (fieldName)
            {
                case "target":
                    try
                    {
                        var target = JsonUtility.FromJson<TargetData>(rawValue);
                        _pendingTarget = target;
                        ProcessLookAtFromTarget(target);
                        TryExecutePendingAction();
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning($"[CharacterController] Failed to parse target field: {e.Message}");
                    }
                    break;

                case "action":
                    _pendingAction = rawValue.Trim('"', ' ');
                    // interact_sleep時のsleep_durationを記録
                    if (_pendingAction == "interact_sleep")
                    {
                        // sleep_durationフィールドは後から届く可能性がある
                        _pendingSleepDuration = 0;
                    }
                    TryExecutePendingAction();
                    break;

                case "sleep_duration":
                    // sleep_durationフィールドを記録（interact_sleep実行時に使用）
                    if (int.TryParse(rawValue.Trim(), out int duration))
                    {
                        _pendingSleepDuration = duration;
                    }
                    break;

                case "emote":
                    string emote = rawValue.Trim('"', ' ');
                    if (!string.IsNullOrEmpty(emote) && emote != "Neutral")
                    {
                        ProcessEmote(emote);
                    }
                    break;
            }
        }

        /// <summary>
        /// targetとactionが両方揃っている場合にアクションを実行
        /// actionが先に届いた場合はtargetを待つ（targetが届いた時に再呼出される）
        /// Thinking中、またはThinking終了直後のgrace期間中の場合は統合コルーチンで
        /// 遅延実行し、IB発火タイミングを sit_ed 等から時間方向に分離する。
        /// </summary>
        private void TryExecutePendingAction()
        {
            if (_pendingAction == null || _pendingTarget == null) return;

            // LLMResponseDataを組み立ててExecuteActionに渡す
            var response = new LLMResponseData
            {
                action = _pendingAction,
                target = _pendingTarget
            };

            // 使用済みクリア（遅延実行する場合はローカル変数にキャプチャ済み）
            _pendingAction = null;
            _pendingTarget = null;

            if (response.IsIgnore) return;

            // Thinking中、またはThinking終了直後のgrace期間中はdefer
            // （emoteによるForceStopThinkingでIsThinkingActiveが即座にfalseになっても
            //  grace期間でキャッチして emote と同じタイミングで発火させる）
            if (animationController != null &&
                (animationController.IsThinkingActive || IsInThinkExitGrace()))
            {
                Debug.Log($"[CharacterController] Deferring action until think exit grace completes ({_thinkExitDelay:F2}s)");
                _deferredAction = response;
                StartThinkExitFlushIfNeeded();
                return;
            }

            ProcessActionFromField(response);
        }

        /// <summary>
        /// Think終了後のdeferred action/emote を順序同期で発火させる統合コルーチンを起動。
        /// 既に動作中なら何もしない（action/emoteそれぞれが一度だけ起動を試みる）。
        /// </summary>
        private void StartThinkExitFlushIfNeeded()
        {
            if (_thinkExitFlushCoroutine == null)
            {
                _thinkExitFlushCoroutine = StartCoroutine(FlushAfterThinkExit());
            }
        }

        /// <summary>
        /// Thinking終了＋grace期間経過を待ち、deferred emote → action の順に発火する。
        /// emoteを先に発火することで、後続のactionが既存のExitLoopWithCallback経由で
        /// Emote状態を ForceStopEmoteToEnd で正しくクリーンアップできる。
        /// </summary>
        private IEnumerator FlushAfterThinkExit()
        {
            // Thinking完全終了＋grace期間経過を待機
            while (animationController != null &&
                   (animationController.IsThinkingActive || IsInThinkExitGrace()))
            {
                yield return null;
            }

            // deferredを取り出してクリア
            string emote = _deferredEmote;
            LLMResponseData action = _deferredAction;
            _deferredEmote = null;
            _deferredAction = null;
            _thinkExitFlushCoroutine = null;

            // 順序: emote → action（actionがExitLoopWithCallbackでEmoteを正規に停止できるように）
            if (!string.IsNullOrEmpty(emote) && animationController != null)
            {
                PlayEmoteIfPossible(emote);
            }

            if (action != null && animationController != null)
            {
                ProcessActionFromField(action);
            }
        }

        /// <summary>
        /// 逐次反映用のアクション処理
        /// ProcessActionと同じロジックだが、emoteは別途HandleStreamFieldで処理されるため含めない
        /// </summary>
        private void ProcessActionFromField(LLMResponseData response)
        {
            // Talk状態から移動/インタラクトする場合はTalkを終了
            if (talkController != null && talkController.IsInTalkMode)
            {
                var targetType = ResolveTargetType(response.target);
                bool isNonTalkAction = response.IsInteract ||
                    (response.IsMove && targetType != TargetType.Talk);

                if (isNonTalkAction)
                {
                    Debug.Log("[CharacterController] Exiting talk mode for non-talk action (incremental)");
                    talkController.ForceExitTalk();
                }
            }

            // インタラクション中に別のアクションが来た場合: ed再生 → 完了後に実行
            if (interactionController != null && interactionController.IsInteracting())
            {
                // 同種インタラクションの場合: 現在の家具を除外してランダム選択するため参照を保持
                FurnitureInstance excludeFurniture = null;
                if (response.IsInteract)
                {
                    var newAction = response.GetInteractAction();
                    if (newAction == interactionController.CurrentAction)
                    {
                        excludeFurniture = interactionController.CurrentFurniture;
                        Debug.Log($"[CharacterController] Same interaction type ({newAction}), will exclude current furniture (incremental): {excludeFurniture?.instanceId}");
                    }
                }

                Debug.Log("[CharacterController] Exiting interaction before processing new action (incremental)");
                interactionController.ExitLoopWithCallback(() =>
                {
                    ExecuteAction(response, excludeFurniture);
                });
                return;
            }

            ExecuteAction(response);
        }

        /// <summary>
        /// TargetDataのみからLookAtコンテキストを設定し即時反映（逐次反映用）
        /// </summary>
        private void ProcessLookAtFromTarget(TargetData target)
        {
            if (target == null || lookAtController == null) return;

            var targetType = ResolveTargetType(target);
            SetLookAtContext(targetType, target);
            UpdateLookAt();
        }

        // ===================================================================
        // ターゲットタイプ解決
        // ===================================================================

        /// <summary>
        /// RoomTarget名を考慮してターゲットタイプを解決
        /// </summary>
        private TargetType ResolveTargetType(TargetData target)
        {
            if (target == null) return TargetType.Talk;
            return target.GetTargetType(roomTargetManager?.TargetNames);
        }

        // ===================================================================
        // action 処理
        // ===================================================================

        /// <summary>
        /// アクションを処理。遅延実行された場合はtrueを返す。
        /// trueの場合、emote処理はコールバック内で行われるため呼び出し元でスキップすること。
        /// </summary>
        private bool ProcessAction(LLMResponseData response)
        {
            if (response.IsIgnore) return false;

            // Talk状態から移動/インタラクトする場合はTalkを終了
            // （move+talkは別途ProcessMoveAction内で再接近処理）
            if (talkController != null && talkController.IsInTalkMode)
            {
                var targetType = ResolveTargetType(response.target);
                bool isNonTalkAction = response.IsInteract ||
                    (response.IsMove && targetType != TargetType.Talk);

                if (isNonTalkAction)
                {
                    Debug.Log("[CharacterController] Exiting talk mode for non-talk action");
                    talkController.ForceExitTalk();
                }
            }

            // インタラクション中に別のアクションが来た場合:
            // ed再生 → 完了後に次のアクション（+emote）を実行
            if (interactionController != null && interactionController.IsInteracting())
            {
                // 同種インタラクションの場合: 現在の家具を除外してランダム選択するため参照を保持
                FurnitureInstance excludeFurniture = null;
                if (response.IsInteract)
                {
                    var newAction = response.GetInteractAction();
                    if (newAction == interactionController.CurrentAction)
                    {
                        excludeFurniture = interactionController.CurrentFurniture;
                        Debug.Log($"[CharacterController] Same interaction type ({newAction}), will exclude current furniture: {excludeFurniture?.instanceId}");
                    }
                }

                Debug.Log("[CharacterController] Exiting interaction before processing new action");
                interactionController.ExitLoopWithCallback(() =>
                {
                    ExecuteAction(response, excludeFurniture);
                    if (response.HasEmote)
                    {
                        ProcessEmote(response.emote);
                    }
                });
                return true; // 遅延実行
            }

            ExecuteAction(response);
            return false;
        }

        /// <summary>
        /// アクションを実行（インタラクション終了後の遅延実行にも使用）
        /// excludeFurniture: 同種インタラクション時に除外する家具（別の家具をランダム選択するため）
        /// </summary>
        private void ExecuteAction(LLMResponseData response, FurnitureInstance excludeFurniture = null)
        {
            if (response.IsMove)
            {
                ProcessMoveAction(response);
            }
            else if (response.IsInteract)
            {
                ProcessInteractAction(response, excludeFurniture);
            }
        }

        /// <summary>
        /// action:move の処理
        /// target.type に応じて移動先を決定
        /// </summary>
        private void ProcessMoveAction(LLMResponseData response)
        {
            var targetType = ResolveTargetType(response.target);

            switch (targetType)
            {
                case TargetType.Talk:
                    // talk → RoomTarget "talk" 位置へ移動 → Talk状態に遷移
                    if (talkController != null)
                    {
                        if (talkController.IsInTalkMode)
                        {
                            // 既にInTalkだがtalk_positionから離れている場合は再接近
                            if (talkController.IsAwayFromTalkPosition())
                            {
                                Debug.Log("[CharacterController] Re-approaching talk position");
                                talkController.ForceExitTalk();
                                talkController.EnterTalk();
                                SetState(CharacterState.Walking);
                                DeferPendingWalkEmote();
                            }
                            // talk_position付近にいれば何もしない（正常）
                        }
                        else
                        {
                            talkController.EnterTalk();
                            SetState(CharacterState.Walking);
                            DeferPendingWalkEmote();
                        }
                    }
                    break;

                case TargetType.Interact:
                    // interact_* → 最寄り家具付近に移動（座らない）
                    var interactAction = response.target.GetInteractAction();
                    MoveToNearestFurniture(interactAction);
                    break;

                case TargetType.Dynamic:
                    // dynamic → DynamicTarget 位置に移動
                    if (dynamicTargetController != null && response.target != null)
                    {
                        var targetPosition = dynamicTargetController.MoveTo(
                            response.target.clock,
                            response.target.distance,
                            response.target.height,
                            transform
                        );
                        // NavMesh移動用にY座標をキャラクターの高さに合わせる
                        // （height はLookAt用でNavMeshの高さとは異なる）
                        var movePosition = targetPosition;
                        movePosition.y = transform.position.y;
                        // 移動先方向を向く回転を計算
                        var lookDirection = movePosition - transform.position;
                        lookDirection.y = 0f;
                        var targetRotation = lookDirection.sqrMagnitude > 0.001f
                            ? Quaternion.LookRotation(lookDirection)
                            : transform.rotation;

                        navigationController?.MoveTo(
                            movePosition,
                            targetRotation,
                            () => SetState(CharacterState.Idle)
                        );
                        SetState(CharacterState.Walking);
                        DeferPendingWalkEmote();
                    }
                    break;

                case TargetType.Named:
                    // RoomTarget → 名前付きターゲット位置に移動
                    MoveToRoomTarget(response.target.type);
                    break;
            }
        }

        /// <summary>
        /// action:interact_* の処理
        /// 対応家具を探してインタラクト開始
        /// excludeFurniture: 同種インタラクション時に除外する家具（別の家具をランダム選択）
        /// </summary>
        private void ProcessInteractAction(LLMResponseData response, FurnitureInstance excludeFurniture = null)
        {
            var furnitureAction = response.GetInteractAction();
            if (string.IsNullOrEmpty(furnitureAction))
            {
                Debug.LogWarning($"[CharacterController] Invalid interact action: {response.action}");
                return;
            }

            if (interactionController != null && furnitureManager != null)
            {
                FurnitureInstance furniture;

                if (excludeFurniture != null)
                {
                    // 同種インタラクション: 現在の家具を除外してランダム選択
                    furniture = furnitureManager.GetRandomAvailableFurniture(furnitureAction, excludeFurniture);
                    if (furniture != null)
                    {
                        Debug.Log($"[CharacterController] Same interaction, randomly selected different furniture: {furniture.instanceId}");
                    }
                    else
                    {
                        // 他に候補がない場合は同じ家具で再インタラクト（最寄りで選択）
                        furniture = furnitureManager.GetNearestAvailableFurniture(transform.position, furnitureAction);
                        Debug.Log($"[CharacterController] No alternative furniture, reusing: {furniture?.instanceId}");
                    }
                }
                else
                {
                    // 通常フロー: 最寄りの利用可能な家具を検索
                    furniture = furnitureManager.GetNearestAvailableFurniture(transform.position, furnitureAction);
                }

                if (furniture != null)
                {
                    var request = furnitureManager.CreateInteractionRequest(furniture, furnitureAction, transform.position);
                    if (request != null)
                    {
                        // interact_sleep: ループ突入時にSleep状態へ遷移するためコールバック登録
                        if (furnitureAction == "sleep" && sleepController != null)
                        {
                            interactionController.StartInteraction(request, () =>
                            {
                                sleepController.EnterSleep(_pendingSleepDuration, furniture.instanceId);
                                _pendingSleepDuration = 0;
                            });
                        }
                        // interact_exit: インタラクション完了時に外出状態へ遷移
                        else if (furnitureAction == "exit" && outingController != null)
                        {
                            interactionController.StartInteraction(request, null, () =>
                            {
                                outingController.EnterOuting();
                            });
                        }
                        else
                        {
                            interactionController.StartInteraction(request);
                        }
                        SetState(CharacterState.Interacting);
                    }
                }
                else
                {
                    Debug.LogWarning($"[CharacterController] No available furniture for action: {furnitureAction}");
                }
            }
        }

        /// <summary>
        /// RoomTarget位置に移動（action:move + target:{name}）
        /// lookattargetがあればそちらを向いて停止
        /// </summary>
        private void MoveToRoomTarget(string targetName)
        {
            if (roomTargetManager == null || navigationController == null) return;

            var roomTarget = roomTargetManager.GetTarget(targetName);
            if (roomTarget == null)
            {
                Debug.LogWarning($"[CharacterController] Room target not found: {targetName}");
                return;
            }

            var position = roomTarget.transform.position;

            // lookattargetがあればその方向を向く回転を計算
            Quaternion rotation;
            if (roomTarget.lookAtTarget != null)
            {
                var lookDir = roomTarget.lookAtTarget.position - position;
                lookDir.y = 0f;
                rotation = lookDir.sqrMagnitude > 0.001f
                    ? Quaternion.LookRotation(lookDir)
                    : roomTarget.transform.rotation;
            }
            else
            {
                rotation = roomTarget.transform.rotation;
            }

            navigationController.MoveTo(position, rotation, () => SetState(CharacterState.Idle));
            SetState(CharacterState.Walking);
            DeferPendingWalkEmote();
            Debug.Log($"[CharacterController] Moving to room target: {targetName}");
        }

        /// <summary>
        /// 最寄り家具付近に移動（action:move + target:interact_*）
        /// </summary>
        private void MoveToNearestFurniture(string furnitureAction)
        {
            if (furnitureManager == null || navigationController == null) return;

            var furniture = furnitureManager.GetNearestAvailableFurniture(transform.position, furnitureAction);
            if (furniture != null)
            {
                var position = furniture.GetInteractionPosition(furnitureAction);
                var rotation = furniture.GetInteractionRotation(furnitureAction);
                navigationController.MoveTo(position, rotation, () => SetState(CharacterState.Idle));
                SetState(CharacterState.Walking);
                DeferPendingWalkEmote();
            }
            else
            {
                Debug.LogWarning($"[CharacterController] No available furniture for move target: {furnitureAction}");
            }
        }

        // ===================================================================
        // LookAt 処理（動的再評価）
        // ===================================================================

        /// <summary>
        /// LLMレスポンスからLookAtコンテキストを設定し即時反映
        /// </summary>
        private void ProcessLookAt(LLMResponseData response)
        {
            if (response.target == null || lookAtController == null) return;

            var targetType = ResolveTargetType(response.target);
            SetLookAtContext(targetType, response.target);
            UpdateLookAt();
        }

        /// <summary>
        /// LookAtコンテキストを設定（毎フレーム再評価の基準）
        /// </summary>
        private void SetLookAtContext(TargetType targetType, TargetData target)
        {
            _lookAtTargetType = targetType;

            switch (targetType)
            {
                case TargetType.Talk:
                    _lookAtParam = "talk";
                    break;

                case TargetType.Interact:
                    _lookAtParam = target.GetInteractAction();
                    break;

                case TargetType.Dynamic:
                    _lookAtParam = target.height;
                    // DynamicTarget: height更新 + LookAtTransform設定
                    if (dynamicTargetController != null && dynamicTargetController.LookAtTarget != null)
                    {
                        dynamicTargetController.SetLookAtHeight(target.height);
                        lookAtController.LookAtTransform(dynamicTargetController.LookAtTarget);
                    }
                    break;

                case TargetType.Named:
                    _lookAtParam = target.type;
                    break;
            }
        }

        /// <summary>
        /// 毎フレームLookAtを再評価
        /// キャラクターの位置変化に応じて距離判定やターゲット追跡を更新
        /// </summary>
        private void UpdateLookAt()
        {
            if (lookAtController == null) return;

            switch (_lookAtTargetType)
            {
                case TargetType.Talk:
                case TargetType.Named:
                    UpdateLookAtRoomTarget(_lookAtParam);
                    break;

                case TargetType.Interact:
                    UpdateLookAtFurniture(_lookAtParam);
                    break;

                case TargetType.Dynamic:
                    // 既にLookAtTransformで動的追跡中 → 何もしない
                    break;
            }
        }

        /// <summary>
        /// RoomTargetのTransformを動的に追跡
        /// </summary>
        private void UpdateLookAtRoomTarget(string targetName)
        {
            if (roomTargetManager == null) return;

            var roomTarget = roomTargetManager.GetTarget(targetName);
            if (roomTarget == null) return;

            Transform target = roomTarget.lookAtTarget != null
                ? roomTarget.lookAtTarget
                : roomTarget.transform;
            lookAtController.LookAtTransform(target);
        }

        /// <summary>
        /// 家具のlookAtPointを動的に再評価（距離判定を毎フレーム実行）
        /// </summary>
        private void UpdateLookAtFurniture(string furnitureAction)
        {
            if (furnitureManager == null) return;

            var furniture = furnitureManager.GetNearestAvailableFurniture(transform.position, furnitureAction);
            if (furniture != null)
            {
                var lookAtPoint = furniture.GetNearestLookAtPoint(transform.position);
                if (lookAtPoint != null)
                {
                    lookAtController.LookAtTransform(lookAtPoint);
                }
                else
                {
                    // lookAtMaxDistance外 → LookAt解除
                    lookAtController.LookForward();
                }
            }
        }

        // ===================================================================
        // emote 処理
        // ===================================================================

        /// <summary>
        /// emote を処理（再生可能であれば再生、完了後に自動復帰）
        /// </summary>
        // Walking待ちEmoteコルーチン（Thinking待ちは _thinkExitFlushCoroutine を使用）
        private Coroutine _pendingEmoteCoroutine;

        // 歩行後に再生するemote（emoteが移動開始前に到着した場合用）
        private string _pendingWalkEmote;

        private void ProcessEmote(string emote)
        {
            if (animationController == null) return;

            string emoteAnimationId = $"emote_{emote}";

            // Thinking中、またはThinking終了直後のgrace期間中は統合コルーチンでdefer
            // （ForceStopThinkingでIsThinkingActiveが即座にfalseになってもgrace期間で捕捉）
            if (animationController.IsThinkingActive || IsInThinkExitGrace())
            {
                Debug.Log($"[CharacterController] Deferring emote until think exit grace completes: {emoteAnimationId}");
                _deferredEmote = emoteAnimationId;
                StartThinkExitFlushIfNeeded();
                return;
            }

            // Walking/Running中の場合は移動完了後にemoteを再生
            if (_currentState == CharacterState.Walking || _currentState == CharacterState.Running)
            {
                Debug.Log($"[CharacterController] Deferring emote until walk completes: {emoteAnimationId}");
                if (_pendingEmoteCoroutine != null)
                {
                    StopCoroutine(_pendingEmoteCoroutine);
                }
                _pendingEmoteCoroutine = StartCoroutine(PlayEmoteAfterWalk(emoteAnimationId));
                return;
            }

            // emoteを再生しつつ、後からwalkが始まった場合の再再生用に保存
            _pendingWalkEmote = emoteAnimationId;
            PlayEmoteIfPossible(emoteAnimationId);
        }

        /// <summary>
        /// 歩行開始時に呼び出し、事前に到着していたemoteを移動完了後に再生するコルーチンを起動
        /// </summary>
        private void DeferPendingWalkEmote()
        {
            if (string.IsNullOrEmpty(_pendingWalkEmote)) return;

            string emote = _pendingWalkEmote;
            _pendingWalkEmote = null;

            Debug.Log($"[CharacterController] Queued pre-walk emote for after-walk playback: {emote}");

            if (_pendingEmoteCoroutine != null)
            {
                StopCoroutine(_pendingEmoteCoroutine);
            }
            _pendingEmoteCoroutine = StartCoroutine(PlayEmoteAfterWalk(emote));
        }

        private IEnumerator PlayEmoteAfterWalk(string emoteAnimationId)
        {
            // NavigationControllerの移動完了を待機（NavigationStateで判定）
            // CharacterStateはTalkターゲット時にWalkingのまま変わらないため使用しない
            if (navigationController != null)
            {
                while (navigationController.CurrentState != NavigationState.Idle)
                {
                    yield return null;
                }
            }
            else
            {
                // フォールバック: CharacterStateで待機
                while (_currentState == CharacterState.Walking || _currentState == CharacterState.Running)
                {
                    yield return null;
                }
            }

            _pendingEmoteCoroutine = null;

            // CanPlayEmoteがtrueになるまでポーリング
            // Idle/TalkIdleタイムラインのEmotePlayableClipが再生位置に到達するまで待つ
            float timeout = 3f;
            float elapsed = 0f;
            while (elapsed < timeout)
            {
                // 新しいアクション（移動等）が開始された場合はキャンセル
                if (navigationController != null && navigationController.CurrentState != NavigationState.Idle)
                {
                    Debug.Log($"[CharacterController] Deferred emote cancelled - new navigation started: {emoteAnimationId}");
                    yield break;
                }

                // walk_ed（終了フェーズ）は中断せず最後まで再生させる。
                // walk_ed完了後にOnWalkEndPhaseCompleteがReturnToIdleを呼び、
                // IdleタイムラインのEmotePlayableClipがCanPlayEmote()=trueにすると
                // 下のチェックでemoteが発火する。
                if (animationController != null && animationController.CanPlayEmote())
                {
                    animationController.PlayEmoteWithReturn(emoteAnimationId);
                    Debug.Log($"[CharacterController] Playing deferred emote after walk: {emoteAnimationId}");
                    yield break;
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            Debug.Log($"[CharacterController] Deferred emote timed out (CanPlayEmote not ready within {timeout}s): {emoteAnimationId}");
        }

        private void PlayEmoteIfPossible(string emoteAnimationId)
        {
            if (animationController.CanPlayEmote())
            {
                animationController.PlayEmoteWithReturn(emoteAnimationId);
                Debug.Log($"[CharacterController] Playing emote: {emoteAnimationId}");
            }
            else
            {
                Debug.Log($"[CharacterController] Emote not playable in current state: {emoteAnimationId}");
            }
        }

        // ===================================================================
        // Outing → Entry 処理
        // ===================================================================

        /// <summary>
        /// 外出中にLLMがentry animationを選定した場合の帰還処理
        /// </summary>
        private void ProcessEntryFromOuting(LLMResponseData response)
        {
            if (outingController == null) return;

            outingController.PlayEntry(() =>
            {
                // 入室完了 → Idle状態に遷移
                SetState(CharacterState.Idle);
                animationController?.ReturnToIdle();
                Debug.Log("[CharacterController] Entry from outing complete, returned to Idle");
            });
        }

        // ===================================================================
        // 状態管理
        // ===================================================================

        private void SetState(CharacterState newState)
        {
            if (_currentState != newState)
            {
                Debug.Log($"[CharacterController] State changed: {_currentState} -> {newState}");
                _currentState = newState;
            }
        }
    }

    public enum CharacterState
    {
        Idle,           // 通常待機
        Walking,        // 歩行中
        Running,        // 走行中
        Interacting,    // 家具インタラクション中
        Emote,          // 感情表現中
        TalkIdle,       // 会話中待機
        Thinking,       // 考え中（API待ち）
        TalkEmote       // 会話中の感情表現
    }
}
