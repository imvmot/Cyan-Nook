using UnityEngine;
using UnityEngine.AI;
using CyanNook.Chat;
using CyanNook.Furniture;
using CyanNook.UI;

namespace CyanNook.Character
{
    /// <summary>
    /// 外出状態の管理コントローラー
    /// - interact_exit完了後に外出状態に遷移
    /// - 外出中は定期メッセージをLLMに送信（Dream方式）
    /// - LLMがinteract_entry01を選定した場合のみ帰還
    /// - ゲーム開始時にinteract_entry01を再生（入室演出）
    /// - 外出状態は非永続化（再起動で必ず解除）
    /// </summary>
    public class OutingController : MonoBehaviour
    {
        // PlayerPrefsキー
        private const string PrefKey_OutingInterval = "outing_messageInterval";
        private const string PrefKey_OutingPrompt = "outing_promptMessage";
        private const string PrefKey_EntryPrompt = "outing_entryPromptMessage";

        [Header("References")]
        public ChatManager chatManager;
        public CharacterAnimationController animationController;
        public FurnitureManager furnitureManager;
        public IdleChatController idleChatController;
        public BoredomController boredomController;
        public VrmLoader vrmLoader;
        public CharacterNavigationController navigationController;
        public UIController uiController;

        [Header("Outing Messages")]
        [Tooltip("外出中定期メッセージの送信間隔（分）")]
        public float outingMessageInterval = 5f;

        [TextArea(2, 4)]
        [Tooltip("外出中定期メッセージのプロンプト")]
        public string outingPromptMessage = "[system]キャラクターは外出中です。外での活動を考えてください。";

        [Header("Entry")]
        [TextArea(2, 4)]
        [Tooltip("入室時にユーザーメッセージに付与するプロンプト")]
        public string entryPromptMessage = "[system]あなたは部屋に戻ってきました。";

        [Header("Display")]
        [Tooltip("外出中にメッセージ欄に表示するテキスト")]
        public string outingDisplayText = "お出かけ中…";

        [Header("Animation")]
        [Tooltip("入室アニメーションのID")]
        public string entryAnimationId = "interact_entry01";

        [Header("Door Furniture")]
        [Tooltip("入退室に使用するドア家具のinstanceId")]
        public string doorFurnitureId = "room01_door_01";

        [Header("State")]
        [SerializeField]
        private bool _isOutside = false;

        private float _messageTimer;

        // Entry再生中フラグ
        private bool _isPlayingEntry;

        // Entry完了コールバック
        private System.Action _onEntryComplete;

        // 連動中の家具アニメーション
        private FurnitureAnimationController _entryFurnitureAnimController;

        // Entry早期完了リクエスト済みか（ActionCancelClipでの早期完了用）
        private bool _earlyCompleteRequested;

        /// <summary>外出中かどうか</summary>
        public bool IsOutside => _isOutside;

        /// <summary>入室アニメーション再生中か</summary>
        public bool IsPlayingEntry => _isPlayingEntry;

        /// <summary>外出中の表示テキスト</summary>
        public string OutingDisplayText => outingDisplayText;

        /// <summary>入室時のプロンプトメッセージ</summary>
        public string EntryPromptMessage => entryPromptMessage;

        /// <summary>
        /// Entryアニメーション完了時に発火（per-callコールバック実行後）。
        /// ChatManager等が、Entry中にキューしたLLMレスポンスを発火させる用途で購読する。
        /// </summary>
        public event System.Action OnEntryAnimationCompleted;

        private void Start()
        {
            LoadSettings();
        }

        private void Update()
        {
            if (!_isOutside) return;

            // 定期メッセージタイマー
            _messageTimer -= Time.deltaTime;
            if (_messageTimer <= 0f)
            {
                SendOutingMessage();
                _messageTimer = outingMessageInterval * 60f; // 分→秒変換
            }
        }

        // ===================================================================
        // 公開API
        // ===================================================================

        /// <summary>
        /// 外出状態に遷移（interact_exit完了後に呼ばれる）
        /// </summary>
        public void EnterOuting()
        {
            if (_isOutside)
            {
                Debug.LogWarning("[OutingController] Already outside");
                return;
            }

            _isOutside = true;
            _messageTimer = outingMessageInterval * 60f; // 2回目以降はInterval経過後（分→秒変換）

            // 初回Outing PromptをChatManager Idle待ちで送信
            if (chatManager != null)
            {
                StartCoroutine(SendOutingPromptWhenReady());
            }

            // キャラクター非表示
            if (vrmLoader != null)
            {
                vrmLoader.SetMeshVisibility(false);
            }

            // IdleChat停止
            if (idleChatController != null)
            {
                idleChatController.SetPaused(true);
            }

            // Boredom蓄積停止
            if (boredomController != null)
            {
                boredomController.SetPaused(true);
            }

            // UI表示
            if (uiController != null)
            {
                uiController.ShowOutingDisplay();
            }

            Debug.Log("[OutingController] EnterOuting: character hidden, outing messages started");
        }

        /// <summary>
        /// 外出状態を解除（入室アニメーション再生前に呼ばれる）
        /// </summary>
        public void ExitOuting()
        {
            if (!_isOutside) return;

            _isOutside = false;

            // IdleChat再開
            if (idleChatController != null)
            {
                idleChatController.SetPaused(false);
            }

            // Boredom蓄積再開
            if (boredomController != null)
            {
                boredomController.SetPaused(false);
            }

            // UI表示解除
            if (uiController != null)
            {
                uiController.ClearOutingDisplay();
            }

            Debug.Log("[OutingController] ExitOuting: outing state cleared");
        }

        /// <summary>
        /// 入室アニメーションを再生（ゲーム開始時 / 外出中からの帰還時）
        /// ドアのInteract_entryポイントに吸着 → entry再生 → 完了後コールバック
        /// </summary>
        /// <param name="onComplete">アニメーション完了後のコールバック</param>
        /// <param name="skipBlend">初期ブレンドをスキップするか（起動時はtrue）</param>
        public void PlayEntry(System.Action onComplete, bool skipBlend = false, bool suppressEntryPrompt = false)
        {
            if (_isPlayingEntry)
            {
                Debug.LogWarning("[OutingController] Entry animation already playing");
                return;
            }

            // 外出中なら解除
            if (_isOutside)
            {
                ExitOuting();
            }

            _onEntryComplete = onComplete;
            _isPlayingEntry = true;
            _earlyCompleteRequested = false;

            // ドア家具を検索
            FurnitureInstance doorFurniture = null;
            if (furnitureManager != null)
            {
                doorFurniture = furnitureManager.GetFurnitureInstance(doorFurnitureId);
            }

            if (doorFurniture == null)
            {
                Debug.LogWarning($"[OutingController] Door furniture not found: {doorFurnitureId}, playing entry without door sync");
            }

            // ドアのentry pointに配置（NavMesh外の可能性があるためAgentを一時無効化）
            if (doorFurniture != null && navigationController != null)
            {
                // NavMeshAgentを無効化（NavMesh外への配置＋Root Motion移動を妨げないため）
                if (navigationController.agent != null)
                {
                    navigationController.agent.enabled = false;
                }

                var entryPoints = doorFurniture.GetInteractionPointsForAction("entry");
                if (entryPoints != null && entryPoints.Length > 0)
                {
                    // NavMeshAgentを使わず直接transform移動（NavMesh外対応）
                    navigationController.transform.position = entryPoints[0].position;
                    navigationController.transform.rotation = entryPoints[0].rotation;
                    if (navigationController.characterTransform != null)
                    {
                        navigationController.characterTransform.localPosition = Vector3.zero;
                        navigationController.characterTransform.localRotation = Quaternion.identity;
                    }
                    Debug.Log($"[OutingController] Placed at door entry point (NavMesh off): {entryPoints[0].position}");
                }
                else
                {
                    navigationController.transform.position = doorFurniture.transform.position;
                    if (navigationController.characterTransform != null)
                    {
                        navigationController.characterTransform.localPosition = Vector3.zero;
                        navigationController.characterTransform.localRotation = Quaternion.identity;
                    }
                    Debug.Log("[OutingController] Placed at door position (no entry point found)");
                }
            }

            // キャラクターアニメーション再生
            if (animationController != null)
            {
                // Interactステートで再生: Root Motion有効 + 位置保持OFF + ループなし（WrapMode.None）
                // entryはcommonカテゴリだが、Root Motion移動＋1回再生が必要なためInteractを使用
                animationController.PlayState(AnimationStateType.Interact, entryAnimationId, skipBlend: skipBlend);

                // InteractionEndClip到達イベントを購読（PlayState後に購読する）
                // PlayState内部のStopDirectorForAssetChangeが前のTimelineを停止し、
                // イベントが発火するため、先に購読するとEntry開始前に呼ばれてしまう
                animationController.OnInteractionEndReached += OnEntryAnimationComplete;

                Debug.Log($"[OutingController] Playing entry animation: {entryAnimationId}");
            }

            // ドア家具アニメーション同期再生
            _entryFurnitureAnimController = null;
            if (doorFurniture != null)
            {
                var furnitureAnim = doorFurniture.GetComponent<FurnitureAnimationController>();
                if (furnitureAnim != null && furnitureAnim.HasTimeline(entryAnimationId))
                {
                    _entryFurnitureAnimController = furnitureAnim;
                    furnitureAnim.Play(entryAnimationId);
                    Debug.Log("[OutingController] Playing door entry animation in sync");
                }
            }

            // キャラクター表示（ShowModelAfterAnimationと同等の遅延付き）
            if (vrmLoader != null)
            {
                StartCoroutine(ShowModelAfterFrames());
            }

            // Entry Prompt送信（ChatManagerがIdle状態になるまで待機）
            // suppressEntryPrompt: Cron帰宅時は合体プロンプトをChatManagerが送信するため抑制
            if (!suppressEntryPrompt && chatManager != null && !string.IsNullOrEmpty(entryPromptMessage))
            {
                StartCoroutine(SendEntryPromptWhenReady());
            }
        }

        /// <summary>
        /// Entryの早期完了をリクエスト（Entry中にLLMレスポンスがキューされた時にChatManagerから呼ばれる）。
        /// Entry TimelineにActionCancelClipがあれば、そのCancelRegion到達時点で
        /// OnEntryAnimationCompleteを発火する。CancelRegionが無い場合は通常通り
        /// InteractionEndClipまで待つ（既存挙動と同じ）。
        /// </summary>
        public void RequestEarlyEntryComplete()
        {
            if (!_isPlayingEntry) return;
            if (_earlyCompleteRequested) return;
            if (animationController == null) return;
            if (!animationController.HasCancelRegions) return;

            _earlyCompleteRequested = true;
            animationController.OnCancelRegionReached += OnCancelRegionReached_HandleEarlyComplete;
            animationController.RequestCancelAtRegion();
            Debug.Log("[OutingController] RequestEarlyEntryComplete: subscribed to CancelRegionReached");
        }

        /// <summary>
        /// CancelRegion到達 → Entry早期完了
        /// </summary>
        private void OnCancelRegionReached_HandleEarlyComplete()
        {
            if (animationController != null)
            {
                animationController.OnCancelRegionReached -= OnCancelRegionReached_HandleEarlyComplete;
            }

            if (!_isPlayingEntry) return;

            Debug.Log("[OutingController] Entry early-complete via CancelRegion");
            OnEntryAnimationComplete();
        }

        // ===================================================================
        // 内部メソッド
        // ===================================================================

        /// <summary>
        /// Entry アニメーション完了（InteractionEndClip到達時に呼ばれる）
        /// </summary>
        private void OnEntryAnimationComplete()
        {
            if (!_isPlayingEntry) return;

            Debug.Log("[OutingController] Entry animation InteractionEnd reached");

            // イベント解除
            if (animationController != null)
            {
                animationController.OnInteractionEndReached -= OnEntryAnimationComplete;
                animationController.OnCancelRegionReached -= OnCancelRegionReached_HandleEarlyComplete;
            }

            // 家具アニメーション停止
            if (_entryFurnitureAnimController != null)
            {
                _entryFurnitureAnimController.StopWithCharacter();
                _entryFurnitureAnimController = null;
            }

            _isPlayingEntry = false;

            // NavMeshAgentを再有効化（entry開始時に無効化していた場合）
            if (navigationController != null && navigationController.agent != null && !navigationController.agent.enabled)
            {
                // Root Motionで移動した実際のキャラクター位置を取得
                // characterTransform.localPositionにRoot Motionの累積移動量が入っている
                Vector3 finalWorldPos = navigationController.transform.position;
                Quaternion finalWorldRot = navigationController.transform.rotation;
                if (navigationController.characterTransform != null)
                {
                    finalWorldPos = navigationController.characterTransform.position;
                    finalWorldRot = navigationController.characterTransform.rotation;
                }

                // NavMesh上の有効な位置を検索（Root Motionの最終位置がNavMesh外の場合のフォールバック）
                Vector3 warpPos = finalWorldPos;
                if (!NavMesh.SamplePosition(finalWorldPos, out NavMeshHit hit, 1f, NavMesh.AllAreas))
                {
                    // 1m以内に見つからない場合、より広い範囲で検索
                    if (NavMesh.SamplePosition(finalWorldPos, out hit, 5f, NavMesh.AllAreas))
                    {
                        warpPos = hit.position;
                        Debug.LogWarning($"[OutingController] Final position outside NavMesh, snapped to nearest: {finalWorldPos} → {warpPos}");
                    }
                    else
                    {
                        Debug.LogWarning($"[OutingController] No NavMesh found near final position: {finalWorldPos}, using as-is");
                    }
                }
                else
                {
                    warpPos = hit.position;
                }

                // 親transformをNavMesh上の有効位置に移動
                navigationController.transform.position = warpPos;
                navigationController.transform.rotation = finalWorldRot;

                // Agent再有効化 → NavMesh上にWarp
                var agent = navigationController.agent;
                agent.enabled = true;
                agent.Warp(warpPos);

                // characterTransformのローカル座標をリセット
                if (navigationController.characterTransform != null)
                {
                    navigationController.characterTransform.localPosition = Vector3.zero;
                    navigationController.characterTransform.localRotation = Quaternion.identity;
                }
                Debug.Log($"[OutingController] NavMeshAgent re-enabled at position: {warpPos}");
            }

            Debug.Log("[OutingController] Entry animation complete");

            var callback = _onEntryComplete;
            _onEntryComplete = null;
            callback?.Invoke();

            OnEntryAnimationCompleted?.Invoke();
        }

        /// <summary>
        /// 外出中定期メッセージを送信
        /// </summary>
        private void SendOutingMessage()
        {
            if (chatManager == null || chatManager.CurrentState != ChatState.Idle) return;

            Debug.Log("[OutingController] Sending outing message");
            chatManager.SendAutoRequest(outingPromptMessage);
        }

        /// <summary>
        /// ChatManagerがIdle状態になるまで待ってから初回Outing Promptを送信
        /// interact_exit完了直後はChatManagerがまだIdle状態でない可能性があるため待機する
        /// </summary>
        private System.Collections.IEnumerator SendOutingPromptWhenReady()
        {
            float timeout = 10f;
            float elapsed = 0f;
            while (chatManager.CurrentState != ChatState.Idle && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            // 外出中でなくなっていたら送信しない（短時間で帰還した場合）
            if (!_isOutside) yield break;

            if (chatManager.CurrentState == ChatState.Idle)
            {
                Debug.Log("[OutingController] Sending initial outing prompt");
                chatManager.SendAutoRequest(outingPromptMessage);
            }
            else
            {
                Debug.LogWarning("[OutingController] ChatManager not idle after timeout, skipping initial outing prompt");
            }
        }

        /// <summary>
        /// ChatManagerがIdle状態になるまで待ってからEntry Promptを送信
        /// ゲーム起動直後はChatManagerが初期化中の可能性があるため待機する
        /// </summary>
        private System.Collections.IEnumerator SendEntryPromptWhenReady()
        {
            // ChatManagerがIdle状態になるまで最大10秒待機
            float timeout = 10f;
            float elapsed = 0f;
            while (chatManager.CurrentState != ChatState.Idle && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (chatManager.CurrentState == ChatState.Idle)
            {
                Debug.Log("[OutingController] Sending entry prompt");
                chatManager.SendAutoRequest(entryPromptMessage);
            }
            else
            {
                Debug.LogWarning("[OutingController] ChatManager not idle after timeout, skipping entry prompt");
            }
        }

        /// <summary>
        /// 数フレーム待ってからモデルを表示（T-pose回避）
        /// </summary>
        private System.Collections.IEnumerator ShowModelAfterFrames()
        {
            yield return null;
            yield return null;
            yield return null;

            if (vrmLoader != null)
            {
                vrmLoader.SetMeshVisibility(true);
                Debug.Log("[OutingController] Model revealed after entry animation evaluation");
            }
        }

        // ===================================================================
        // 設定
        // ===================================================================

        private void LoadSettings()
        {
            if (PlayerPrefs.HasKey(PrefKey_OutingInterval))
                outingMessageInterval = PlayerPrefs.GetFloat(PrefKey_OutingInterval);
            if (PlayerPrefs.HasKey(PrefKey_OutingPrompt))
                outingPromptMessage = PlayerPrefs.GetString(PrefKey_OutingPrompt);
            if (PlayerPrefs.HasKey(PrefKey_EntryPrompt))
                entryPromptMessage = PlayerPrefs.GetString(PrefKey_EntryPrompt);
        }

        public void SetOutingMessageInterval(float minutes)
        {
            outingMessageInterval = Mathf.Max(1f, minutes);
            PlayerPrefs.SetFloat(PrefKey_OutingInterval, outingMessageInterval);
            PlayerPrefs.Save();
        }

        public void SetOutingPromptMessage(string message)
        {
            outingPromptMessage = message;
            PlayerPrefs.SetString(PrefKey_OutingPrompt, outingPromptMessage);
            PlayerPrefs.Save();
        }

        public void SetEntryPromptMessage(string message)
        {
            entryPromptMessage = message;
            PlayerPrefs.SetString(PrefKey_EntryPrompt, entryPromptMessage);
            PlayerPrefs.Save();
        }
    }
}
