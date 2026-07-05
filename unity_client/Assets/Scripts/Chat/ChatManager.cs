using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CyanNook.Core;
using CyanNook.Furniture;
using CyanNook.Character;

namespace CyanNook.Chat
{
    /// <summary>
    /// チャット機能全体を管理するマネージャー
    /// プロンプト生成、会話履歴管理、LLMとの通信を統合
    /// TalkController連携でThinking状態を制御
    /// ブロッキング方式とストリーミング方式の両方をサポート
    /// </summary>
    public class ChatManager : MonoBehaviour
    {
        private const string PrefKey_ConversationHistory = "conversation_history";

        [Header("References")]
        public LLMClient llmClient;
        public FurnitureManager furnitureManager;
        public RoomTargetManager roomTargetManager;
        public SpatialContextProvider spatialContextProvider;
        public TalkController talkController;
        public CharacterExpressionController expressionController;

        [Header("Boredom")]
        [Tooltip("退屈ポイントコントローラー")]
        public BoredomController boredomController;

        [Header("Sleep")]
        [Tooltip("睡眠状態コントローラー")]
        public SleepController sleepController;

        [Header("Outing")]
        [Tooltip("外出状態コントローラー")]
        public OutingController outingController;

        [Header("Voice Synthesis")]
        [Tooltip("音声合成コントローラー（VOICEVOX TTS）")]
        public CyanNook.Voice.VoiceSynthesisController voiceSynthesisController;

        [Header("Character Settings")]
        public CharacterTemplateData characterTemplate;

        [Header("Prompt Settings")]
        [TextArea(5, 10)]
        [Tooltip("キャラクター設定プロンプト（人格・性格・世界観）")]
        public string characterPrompt;

        [TextArea(5, 10)]
        [Tooltip("レスポンスフォーマットプロンプト（JSON出力形式の指示）")]
        public string responseFormatPrompt;

        [Header("Conversation")]
        [Tooltip("会話履歴の最大保持数（ローカルLLMデフォルトコンテキスト長を考慮し6推奨）")]
        public int maxHistoryLength = 6;

        [Header("Streaming")]
        [Tooltip("ストリーミングモードを使用する")]
        public bool useStreaming = false;

        [Header("Error Recovery")]
        [Tooltip("LLMエラー後にIdle状態へ自動復帰するまでの秒数。復帰しないとIdleChat/Cron/夢/外出メッセージ等の自律動作が全停止する")]
        public float errorRecoveryDelay = 3f;

        [Header("Vision")]
        [Tooltip("キャラクターカメラの画像をリクエストに含める")]
        public bool useVision = false;
        public CharacterCameraController cameraController;
        public ScreenCaptureDisplayController screenCaptureController;
        public VisibleObjectsProvider visibleObjectsProvider;

        [Header("State")]
        [SerializeField]
        private ChatState _currentState = ChatState.Idle;
        public ChatState CurrentState => _currentState;

        [SerializeField]
        private string _currentPose = "idle";

        [SerializeField]
        private string _currentEmotion = "neutral";

        private List<ConversationEntry> _conversationHistory = new List<ConversationEntry>();

        // 自律リクエスト（IdleChat）用フラグ
        private bool _isAutoRequest;

        // 現在のリクエストがストリーミングかどうか（音声合成の二重実行防止用）
        private bool _isStreamingRequest;

        // 起床メッセージリクエスト中フラグ（ed再生中のThinking抑制・レスポンスキュー用）
        private bool _isWakeUpRequest;

        // Cron帰宅リクエスト中フラグ（Entry再生中のThinking抑制・レスポンスキュー用）
        private bool _isCronEntryRequest;

        // Entry再生中に到着したLLMレスポンスを保持（Cron帰宅・通常Entry共用）。
        // OutingController.OnEntryAnimationCompletedで排出される。
        private LLMResponseData _entryQueuedResponse;

        // 初回Entry（起動時の入室演出）が完了したか。
        // 完了前はVRMロード中/Entry再生中でキャラクター側が未準備のため、
        // 外部アクションフィードの適用を抑制する（ApplyExternalResponseで使用）。
        private bool _hasInitialEntryCompleted;

        /// <summary>
        /// sleep/outing中はVision画像キャプチャを抑制
        /// （睡眠中に部屋が見える・外出中はUnity背景のみで無意味なため）
        /// </summary>
        private bool IsVisionSuppressed =>
            (sleepController != null && sleepController.IsSleeping) ||
            (outingController != null && outingController.IsOutside);

        /// <summary>
        /// Vision画像をキャプチャしてリストで��す
        /// キャラクターカメラ + 共有画面（アクティブ時のみ）
        /// </summary>
        private List<string> CaptureVisionImages()
        {
            if (!useVision || IsVisionSuppressed) return null;

            var images = new List<string>();

            if (cameraController != null)
            {
                string img = cameraController.CaptureImageAsBase64();
                if (img != null) images.Add(img);
            }

            if (screenCaptureController != null && screenCaptureController.IsPlaying)
            {
                string img = screenCaptureController.CaptureImageAsBase64();
                if (img != null) images.Add(img);
            }

            if (images.Count == 0) return null;

            Debug.Log($"[ChatManager] Vision images captured: {images.Count} (camera={cameraController != null}, screenCapture={screenCaptureController != null && screenCaptureController.IsPlaying})");
            return images;
        }

        // --- 共通イベント ---
        public event Action<LLMResponseData> OnChatResponseReceived;
        public event Action<string> OnMessageReceived;
        public event Action<ChatState> OnStateChanged;
        public event Action<string> OnError;
        public event Action OnThinkingStarted;
        public event Action OnThinkingEnded;

        // --- ストリーミング専用イベント ---
        /// <summary>ストリーミングのテキストチャンク受信時（逐次表示用）</summary>
        public event Action<string> OnStreamingTextReceived;

        /// <summary>ストリーミングのヘッダー（メタデータ）受信時</summary>
        public event Action<LlmResponseHeader> OnStreamingHeaderReceived;

        /// <summary>reactionフィールド受信時（短い相槌の即座表示用）</summary>
        public event Action<string> OnStreamingReactionReceived;

        /// <summary>逐次フィールドの反映をCharacterControllerに通知（fieldName, rawJsonValue）</summary>
        public event Action<string, string> OnStreamFieldApplied;

        /// <summary>JSONパースエラー時（errorMessage, rawText）。UIでのエラー表示用</summary>
        public event Action<string, string> OnParseError;

        /// <summary>外出中にユーザーメッセージがブロックされた時</summary>
        public event Action OnOutingMessageBlocked;

        // 逐次フィールド反映の状態管理
        private bool _incrementalFieldsApplied;
        private bool _isThinkingActive;
        private bool _parseErrorHandled;

        // Error状態からIdleへ自動復帰するコルーチン（HandleLLMErrorで開始）
        private Coroutine _errorRecoveryCoroutine;

        // Difyモード判定（Dify時はconversation_idで履歴管理、inputs で動的変数送信）
        private bool IsDifyMode => llmClient?.CurrentConfig?.apiType == LLMApiType.Dify;

        private void Start()
        {
            if (llmClient != null)
            {
                // ブロッキング方式イベント
                llmClient.OnResponseReceived += HandleLLMResponse;
                llmClient.OnError += HandleLLMError;
                llmClient.OnRequestStarted += HandleRequestStarted;
                llmClient.OnRequestCompleted += HandleRequestCompleted;

                // ストリーミング方式イベント
                llmClient.OnStreamHeaderReceived += HandleStreamHeader;
                llmClient.OnStreamTextReceived += HandleStreamText;

                // フィールド逐次受信
                llmClient.OnStreamFieldReceived += HandleStreamField;

                // JSONパースエラー
                llmClient.OnStreamParseError += HandleStreamParseError;
            }

            if (outingController != null)
            {
                outingController.OnEntryAnimationCompleted += FlushEntryQueuedResponse;
            }

            LoadHistory();
        }

        /// <summary>
        /// Entry再生中にキューしたLLMレスポンスを発火する（OutingController.OnEntryAnimationCompleted購読）。
        /// 通常Entry / Cron帰宅 共通の経路。
        /// </summary>
        private void FlushEntryQueuedResponse()
        {
            // 初回Entry完了を記録（外部アクションフィードの適用開始ゲート）
            _hasInitialEntryCompleted = true;

            if (_entryQueuedResponse == null) return;
            var response = _entryQueuedResponse;
            _entryQueuedResponse = null;
            Debug.Log("[ChatManager] Entry complete: dispatching queued response");
            OnChatResponseReceived?.Invoke(response);
        }

        private void HandleRequestStarted()
        {
            // 自律リクエスト時はSendAutoRequest側でSetStateを呼んでいるため
            // Thinking状態もスキップ（突然話しかける演出）
            if (_isAutoRequest) return;

            SetState(ChatState.WaitingForResponse);

            // 起床リクエスト中: ed再生中なのでThinkingアニメーションは抑制
            // ed完了後にレスポンス未到着ならThinkingを開始する（WakeUpWithMessageコールバック内）
            if (_isWakeUpRequest)
            {
                _incrementalFieldsApplied = false;
                return;
            }

            // Cron帰宅リクエスト中: Entry再生中なのでThinkingアニメーションは抑制
            if (_isCronEntryRequest)
            {
                _incrementalFieldsApplied = false;
                return;
            }

            // Thinking状態に切り替え
            // TalkController内部でCanPlayThinking()を判定するため、IsInTalkModeガード不要
            if (talkController != null)
            {
                talkController.StartThinking();
                _isThinkingActive = true;
            }

            _incrementalFieldsApplied = false;

            OnThinkingStarted?.Invoke();
        }

        private void HandleRequestCompleted()
        {
            // フォールバック: フィールド逐次受信でThinkingが解除されなかった場合
            if (_isThinkingActive && talkController != null)
            {
                talkController.StopThinking();
                _isThinkingActive = false;
            }

            // ストリーミング音声合成完了通知（Sleep/Outing中は抑制）
            bool suppressStreamingTTS =
                (sleepController != null && sleepController.IsSleeping && !_isWakeUpRequest) ||
                (outingController != null && outingController.IsOutside && !_isCronEntryRequest);
            if (voiceSynthesisController != null && !suppressStreamingTTS)
            {
                voiceSynthesisController.OnStreamingComplete();
            }

            OnThinkingEnded?.Invoke();
        }

        private void OnDestroy()
        {
            if (llmClient != null)
            {
                llmClient.OnResponseReceived -= HandleLLMResponse;
                llmClient.OnError -= HandleLLMError;
                llmClient.OnRequestStarted -= HandleRequestStarted;
                llmClient.OnRequestCompleted -= HandleRequestCompleted;
                llmClient.OnStreamHeaderReceived -= HandleStreamHeader;
                llmClient.OnStreamTextReceived -= HandleStreamText;
                llmClient.OnStreamFieldReceived -= HandleStreamField;
                llmClient.OnStreamParseError -= HandleStreamParseError;
            }

            if (outingController != null)
            {
                outingController.OnEntryAnimationCompleted -= FlushEntryQueuedResponse;
            }
        }

        /// <summary>
        /// ユーザーメッセージを送信
        /// useStreamingフラグに応じてブロッキング/ストリーミングを切り替え
        /// </summary>
        public void SendChatMessage(string userMessage)
        {
            if (string.IsNullOrWhiteSpace(userMessage))
            {
                Debug.LogWarning("[ChatManager] Empty message");
                return;
            }

            // 外出中: ユーザーからのメッセージ送信を抑制
            if (outingController != null && outingController.IsOutside)
            {
                Debug.Log("[ChatManager] Character is outside, ignoring user message");
                OnOutingMessageBlocked?.Invoke();
                return;
            }

            // Sleep中: ed再生と同時にメッセージを送信し、LLM処理を並行実行
            if (sleepController != null && sleepController.IsSleeping)
            {
                Debug.Log("[ChatManager] User message during sleep, initiating wake-up with parallel LLM request");
                _isWakeUpRequest = true;

                // 保留中のdreamリクエストを中断
                if (_currentState == ChatState.WaitingForResponse && _isAutoRequest)
                {
                    llmClient?.AbortRequest();
                    _isAutoRequest = false;
                    SetState(ChatState.Idle);
                }

                // wakeUpSystemMessageを付与
                string wakeUpPrefix = sleepController.WakeUpSystemMessage;
                if (!string.IsNullOrEmpty(wakeUpPrefix))
                {
                    userMessage = wakeUpPrefix + "\n" + userMessage;
                }

                // ed再生開始（LLMリクエストと並行）
                sleepController.WakeUpWithMessage((queuedResponse) =>
                {
                    // ed完了後: キューにレスポンスがあれば直接処理、なければThinking開始
                    _isWakeUpRequest = false;
                    if (queuedResponse != null)
                    {
                        Debug.Log("[ChatManager] Wake-up: response already received, dispatching to CharacterController");
                        OnChatResponseReceived?.Invoke(queuedResponse);
                    }
                    else if (_currentState == ChatState.WaitingForResponse)
                    {
                        Debug.Log("[ChatManager] Wake-up: no response yet, starting Thinking");
                        if (talkController != null)
                        {
                            talkController.StartThinking();
                            _isThinkingActive = true;
                        }
                    }
                    else
                    {
                        // リクエストが既にエラー等で終了している → 応答は来ないためThinkingを開始しない
                        Debug.Log("[ChatManager] Wake-up: request no longer in flight, skipping Thinking");
                    }
                });

                // fall through: ed再生と並行してメッセージを即送信
                // （将来Timeline Signalでタイミング制御する場合はここを変更）
            }

            if (_currentState == ChatState.WaitingForResponse)
            {
                if (_isAutoRequest)
                {
                    // idleChatリクエスト中 → 中断してユーザー入力を優先
                    Debug.Log("[ChatManager] Cancelling auto-request for user input");
                    llmClient?.AbortRequest();
                    _isAutoRequest = false;
                    SetState(ChatState.Idle);
                }
                else
                {
                    Debug.LogWarning("[ChatManager] Already waiting for response");
                    return;
                }
            }

            // Talk状態への遷移はLLM応答のaction:"move"+target.type:"talk"で制御
            // （ChatManagerからの自動EnterTalkは行わない）

            Debug.Log($"[ChatManager] Sending message (streaming={useStreaming}, vision={useVision}): {userMessage}");

            // 会話履歴に追加
            _conversationHistory.Add(new ConversationEntry
            {
                role = "user",
                content = userMessage,
                timestamp = DateTime.Now
            });
            TrimHistory();
            SaveHistory();

            // Vision: カメラ画像をキャプチャ（sleep/outing中は抑制）
            List<string> imagesBase64 = CaptureVisionImages();

            // LLMに送信
            DispatchLlmRequest(userMessage, imagesBase64, appendToPrompt: false);
        }

        /// <summary>
        /// 自律リクエスト（IdleChat）を送信
        /// ユーザー入力とは異なり、履歴に追加せず、Thinking状態も表示しない
        /// </summary>
        public void SendAutoRequest(string idleMessage)
        {
            if (string.IsNullOrWhiteSpace(idleMessage))
            {
                Debug.LogWarning("[ChatManager] Empty idle message");
                return;
            }

            if (_currentState != ChatState.Idle)
            {
                Debug.LogWarning("[ChatManager] Cannot send auto-request: not idle");
                return;
            }

            _isAutoRequest = true;

            Debug.Log($"[ChatManager] Sending auto-request (streaming={useStreaming}, vision={useVision})");

            // Vision: カメラ画像をキャプチャ（sleep/outing中は抑制）
            List<string> imagesBase64 = CaptureVisionImages();

            // 状態を更新（HandleRequestStartedはスキップされるため手動で設定）
            SetState(ChatState.WaitingForResponse);

            DispatchLlmRequest(idleMessage, imagesBase64, appendToPrompt: true);
        }

        /// <summary>
        /// Cronジョブによる起床リクエスト
        /// Sleep中にcronジョブがSleepをキャンセルして起床させる
        /// wakeUpSystemMessage + cronPromptを合体してLLMに送信
        /// ユーザー起床と同様にed再生と並行してLLMリクエストを行う
        /// </summary>
        public void SendCronWakeUpRequest(string cronPrompt)
        {
            if (sleepController == null || !sleepController.IsSleeping) return;
            if (_isWakeUpRequest) return; // 既に起床処理中

            Debug.Log("[ChatManager] Cron wake-up request, initiating wake-up with parallel LLM request");
            _isWakeUpRequest = true;

            // 保留中のdreamリクエストを中断
            if (_currentState == ChatState.WaitingForResponse && _isAutoRequest)
            {
                llmClient?.AbortRequest();
                _isAutoRequest = false;
                SetState(ChatState.Idle);
            }

            // 非Idle状態なら中断（ユーザーリクエスト処理中）
            if (_currentState != ChatState.Idle)
            {
                Debug.LogWarning("[ChatManager] Cannot send cron wake-up: not idle");
                _isWakeUpRequest = false;
                return;
            }

            // wakeUpSystemMessage + cronPrompt を合体
            string wakeUpPrefix = sleepController.WakeUpSystemMessage;
            string combinedPrompt = !string.IsNullOrEmpty(wakeUpPrefix)
                ? wakeUpPrefix + "\n" + cronPrompt
                : cronPrompt;

            // ed再生開始（LLMリクエストと並行）
            sleepController.WakeUpWithMessage((queuedResponse) =>
            {
                _isWakeUpRequest = false;
                if (queuedResponse != null)
                {
                    Debug.Log("[ChatManager] Cron wake-up: response already received, dispatching");
                    OnChatResponseReceived?.Invoke(queuedResponse);
                }
                else if (_currentState == ChatState.WaitingForResponse)
                {
                    Debug.Log("[ChatManager] Cron wake-up: no response yet, starting Thinking");
                    if (talkController != null)
                    {
                        talkController.StartThinking();
                        _isThinkingActive = true;
                    }
                }
                else
                {
                    // リクエストが既にエラー等で終了している → 応答は来ないためThinkingを開始しない
                    Debug.Log("[ChatManager] Cron wake-up: request no longer in flight, skipping Thinking");
                }
            });

            // LLMに送信（履歴には追加しない = auto-request同等だがフラグは立てない）
            // _isAutoRequest = false のまま → HandleLLMResponseでレスポンスは履歴に追加される
            SetState(ChatState.WaitingForResponse);

            DispatchLlmRequest(combinedPrompt, null, appendToPrompt: true);
        }

        /// <summary>
        /// Cronジョブによる帰宅リクエスト
        /// Outing中にcronジョブがOutingをキャンセルして帰宅させる
        /// entryPromptMessage + cronPromptを合体してLLMに送信
        /// Entry再生と並行してLLMリクエストを行い、Entry完了後にレスポンスを処理
        /// </summary>
        public void SendCronEntryRequest(string cronPrompt)
        {
            if (outingController == null || !outingController.IsOutside) return;
            if (outingController.IsPlayingEntry) return; // 既にEntry再生中

            Debug.Log("[ChatManager] Cron entry request, initiating entry with parallel LLM request");
            _isCronEntryRequest = true;
            _entryQueuedResponse = null;

            // 保留中のoutingリクエストを中断
            if (_currentState == ChatState.WaitingForResponse && _isAutoRequest)
            {
                llmClient?.AbortRequest();
                _isAutoRequest = false;
                SetState(ChatState.Idle);
            }

            // 非Idle状態なら中断（ユーザーリクエスト処理中）
            if (_currentState != ChatState.Idle)
            {
                Debug.LogWarning("[ChatManager] Cannot send cron entry: not idle");
                _isCronEntryRequest = false;
                return;
            }

            // entryPromptMessage + cronPrompt を合体
            string entryPrefix = outingController.EntryPromptMessage;
            string combinedPrompt = !string.IsNullOrEmpty(entryPrefix)
                ? entryPrefix + "\n" + cronPrompt
                : cronPrompt;

            // Entry再生開始（通常のentryPromptは抑制）
            // キュー排出はOnEntryAnimationCompleted購読のFlushEntryQueuedResponseに任せる。
            // ここでは応答未着の場合のThinking開始のみハンドル。
            outingController.PlayEntry(() =>
            {
                _isCronEntryRequest = false;
                if (_entryQueuedResponse == null)
                {
                    // リクエストが既にエラー等で終了している場合は応答が来ないためThinkingを開始しない
                    if (_currentState == ChatState.WaitingForResponse)
                    {
                        Debug.Log("[ChatManager] Cron entry: no response yet, starting Thinking");
                        if (talkController != null)
                        {
                            talkController.StartThinking();
                            _isThinkingActive = true;
                        }
                    }
                    else
                    {
                        Debug.Log("[ChatManager] Cron entry: request no longer in flight, skipping Thinking");
                    }
                }
            }, skipBlend: false, suppressEntryPrompt: true);

            // LLMに送信（履歴には追加しない）
            SetState(ChatState.WaitingForResponse);

            DispatchLlmRequest(combinedPrompt, null, appendToPrompt: true);
        }

        /// <summary>
        /// LLMへリクエストを送出する（送信4メソッド共通のDify/非Dify分岐）
        /// Dify: メッセージのみ送信し、動的値はinputsで送信。
        ///       会話履歴とシステムプロンプトはDify側で管理（conversation_id）
        /// 非Dify: システムプロンプト + 会話履歴付きフルプロンプトを構築して送信
        /// </summary>
        /// <param name="message">送信メッセージ（Difyはそのまま、非Difyはプロンプト構築に使用）</param>
        /// <param name="imagesBase64">Vision画像（null=なし）</param>
        /// <param name="appendToPrompt">非Dify時のプロンプト構築方法。
        /// true=会話履歴の末尾にシステム指示として追記（自律・Cron系）、false=ユーザー発言として構築</param>
        private void DispatchLlmRequest(string message, List<string> imagesBase64, bool appendToPrompt)
        {
            _isStreamingRequest = useStreaming;
            if (IsDifyMode)
            {
                llmClient.SetRequestInputs(BuildDynamicInputs());
                if (useStreaming)
                    llmClient.SendStreamingRequest("", message, imagesBase64);
                else
                    llmClient.SendRequest("", message, imagesBase64);
            }
            else
            {
                llmClient.SetRequestInputs(null);
                string systemPrompt = GenerateSystemPrompt();
                string basePrompt = appendToPrompt
                    ? GenerateFullPromptWithAppend(message)
                    : GenerateFullPrompt(message);
                string fullPrompt = ReplaceDynamicPlaceholders(basePrompt);
                if (useStreaming)
                    llmClient.SendStreamingRequest(systemPrompt, fullPrompt, imagesBase64);
                else
                    llmClient.SendRequest(systemPrompt, fullPrompt, imagesBase64);
            }
        }

        /// <summary>
        /// システムプロンプトを生成
        /// </summary>
        private string GenerateSystemPrompt()
        {
            // キャラクター設定 + レスポンスフォーマットを結合
            string prompt = characterPrompt;
            if (!string.IsNullOrEmpty(responseFormatPrompt))
            {
                prompt += "\n\n" + responseFormatPrompt;
            }

            // プレースホルダを置換
            if (characterTemplate != null)
            {
                prompt = prompt.Replace("{character_name}", characterTemplate.characterName);
                prompt = prompt.Replace("{character_description}", characterTemplate.characterDescription);
                prompt = prompt.Replace("{character_id}", characterTemplate.templateId);
            }

            return ReplaceDynamicPlaceholders(prompt);
        }

        /// <summary>
        /// 動的プレースホルダを置換（システムプロンプト・フルプロンプト共通）
        /// Difyなどシステムプロンプトを送信しないプロバイダーでも、
        /// fullPrompt内のプレースホルダが正しく置換されるようにするため共通化
        /// </summary>
        private string ReplaceDynamicPlaceholders(string text)
        {
            text = text.Replace("{current_datetime}", DateTime.Now.ToString("yyyy-MM-dd HH:mm (ddd)", CultureInfo.InvariantCulture));
            text = text.Replace("{current_pose}", _currentPose);
            text = text.Replace("{current_emotion}", _currentEmotion);

            // 利用可能な家具リストを生成
            string furnitureList = furnitureManager?.GenerateFurnitureListForPrompt() ?? "  - なし";
            text = text.Replace("{available_furniture}", furnitureList);

            // 利用可能なルームターゲットリストを生成
            string roomTargetList = roomTargetManager?.GenerateTargetListForPrompt() ?? "  - なし";
            text = text.Replace("{available_room_targets}", roomTargetList);

            // 空間認識コンテキストを生成（方向・距離付き）
            string spatialContext = spatialContextProvider?.GenerateSpatialContextJson() ?? "{}";
            text = text.Replace("{spatial_context}", spatialContext);

            // 退屈ポイント
            int bored = boredomController?.BoredInt ?? 0;
            text = text.Replace("{bored}", bored.ToString());

            // 視界内オブジェクト（Vision有効 かつ 抑制されていない場合のみ）
            string visibleObjects = "";
            if (useVision && !IsVisionSuppressed && visibleObjectsProvider != null)
            {
                visibleObjects = visibleObjectsProvider.GenerateVisibleObjectsText();
            }
            text = text.Replace("{visible_objects}", visibleObjects);

            return text;
        }

        /// <summary>
        /// Dify用の動的入力変数を辞書形式で生成
        /// ReplaceDynamicPlaceholders()と同じ値をinputsフィールドとして送信する
        /// </summary>
        private Dictionary<string, string> BuildDynamicInputs()
        {
            var inputs = new Dictionary<string, string>
            {
                ["current_datetime"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm (ddd)", CultureInfo.InvariantCulture),
                ["current_pose"] = _currentPose,
                ["current_emotion"] = _currentEmotion,
                ["available_furniture"] = furnitureManager?.GenerateFurnitureListForPrompt() ?? "なし",
                ["available_room_targets"] = roomTargetManager?.GenerateTargetListForPrompt() ?? "なし",
                ["spatial_context"] = spatialContextProvider?.GenerateSpatialContextJson() ?? "{}",
                ["bored"] = (boredomController?.BoredInt ?? 0).ToString(),
                ["visible_objects"] = (useVision && !IsVisionSuppressed && visibleObjectsProvider != null)
                    ? visibleObjectsProvider.GenerateVisibleObjectsText() : ""
            };

            return inputs;
        }

        /// <summary>
        /// 会話履歴を含めたフルプロンプトを生成
        /// </summary>
        private string GenerateFullPrompt(string currentMessage)
        {
            var sb = new System.Text.StringBuilder();

            // 最近の会話履歴を追加（最新のメッセージは除く）
            for (int i = 0; i < _conversationHistory.Count - 1; i++)
            {
                var entry = _conversationHistory[i];
                string role = entry.role == "user" ? "ユーザー" : characterTemplate?.characterName ?? "キャラクター";
                sb.AppendLine($"{role}: {entry.content}");
            }

            // 現在のメッセージ
            sb.AppendLine($"ユーザー: {currentMessage}");

            return sb.ToString();
        }

        /// <summary>
        /// 会話履歴＋追加メッセージでプロンプトを生成（履歴に追加せず）
        /// 自律リクエスト（IdleChat）で使用
        /// </summary>
        private string GenerateFullPromptWithAppend(string appendMessage)
        {
            var sb = new System.Text.StringBuilder();

            // 全会話履歴を含める
            for (int i = 0; i < _conversationHistory.Count; i++)
            {
                var entry = _conversationHistory[i];
                string role = entry.role == "user" ? "ユーザー" : characterTemplate?.characterName ?? "キャラクター";
                sb.AppendLine($"{role}: {entry.content}");
            }

            // 追加メッセージ（履歴には保存しない）
            sb.AppendLine(appendMessage);

            return sb.ToString();
        }

        // ===================================================================
        // ストリーミング方式ハンドラ
        // ===================================================================

        /// <summary>
        /// ストリーミングのヘッダー受信時
        /// 逐次パースで各フィールドは既に反映済みのため、ここでは状態更新のみ
        /// </summary>
        private void HandleStreamHeader(LlmResponseHeader header)
        {
            // Sleep中はヘッダー処理を抑制（起床リクエスト中は通す）
            if (sleepController != null && sleepController.IsSleeping && !_isWakeUpRequest) return;

            Debug.Log($"[ChatManager] Stream header: action={header.action}, emote={header.emote}");

            // 状態を更新
            _currentPose = header.action ?? "ignore";
            _currentEmotion = header.emotion?.GetDominantEmotion().ToString().ToLower() ?? "neutral";

            OnStreamingHeaderReceived?.Invoke(header);
        }

        /// <summary>
        /// ストリーミングのテキストチャンク受信時
        /// </summary>
        private void HandleStreamText(string textChunk)
        {
            // Sleep中はテキスト表示・音声合成を抑制（起床リクエスト中は通す）
            if (sleepController != null && sleepController.IsSleeping && !_isWakeUpRequest) return;
            // Outing中はテキスト表示・音声合成を抑制（Cron帰宅リクエスト中は通す）
            if (outingController != null && outingController.IsOutside && !_isCronEntryRequest) return;

            OnStreamingTextReceived?.Invoke(textChunk);

            // 音声合成コントローラーに転送
            if (voiceSynthesisController != null)
            {
                voiceSynthesisController.OnStreamingTextReceived(textChunk);
            }
        }

        /// <summary>
        /// JSONフィールドが逐次的に届いた時のハンドラ
        /// IncrementalJsonFieldParserが各フィールド完了ごとに発火
        /// </summary>
        private void HandleStreamField(string fieldName, string rawValue)
        {
            // Sleep中はフィールド処理を抑制（起床リクエスト中は通す：表情・リアクション等をed中に表示）
            if (sleepController != null && sleepController.IsSleeping && !_isWakeUpRequest) return;
            // Outing中はフィールド処理を抑制（Cron帰宅リクエスト中は通す）
            if (outingController != null && outingController.IsOutside && !_isCronEntryRequest) return;

            _incrementalFieldsApplied = true;

            // Thinking解除判定（フィールド種別による）
            HandleThinkingExitOnField(fieldName);

            switch (fieldName)
            {
                case "emotion":
                    // 表情を即座に適用
                    ApplyEmotionField(rawValue);
                    break;
                case "reaction":
                    // 短い相槌を即座に表示 + 音声合成
                    ApplyReactionField(rawValue);
                    break;
                case "target":
                case "action":
                case "emote":
                    // CharacterController側で処理するためイベント転送
                    OnStreamFieldApplied?.Invoke(fieldName, rawValue);
                    break;
            }
        }

        /// <summary>
        /// Thinking解除のフィールド依存ロジック
        /// emotion/target/reaction/emote → graceful exit（think_ed再生あり）
        /// action → Thinking継続（targetまたはemoteが届くまで待機）
        ///
        /// 過去はemoteのみForceStopThinkingで即時キャンセルしていたが、これだと
        /// think_ed上に配置したAdditiveCancelClipが発火する前にthinking directorが
        /// 停止されるため、加算解除時の補間が機能しなくなっていた。
        /// 現在はemoteもgraceful exitに統一し、think_edを自然再生させることで
        /// Clip経由の補間パス（ForceCompleteAdditiveTimelineWithBlend）を必ず経由する。
        /// 加算/非加算どちらのthinking timelineでも、think_edにAdditiveCancelClipが
        /// 配置されていればそこで早期補間開始。未配置ならthink_edを完走する。
        /// emote本体の再生はCharacterControllerのFlushAfterThinkExit（Think Exit Delay）
        /// で_isThinkingActive=false後に遅延実行される。
        /// </summary>
        private void HandleThinkingExitOnField(string fieldName)
        {
            if (talkController == null) return;
            if (!_isThinkingActive) return;

            switch (fieldName)
            {
                case "emotion":
                case "target":
                case "reaction":
                case "emote":
                    // graceful exit: think_ed再生ありで復帰
                    talkController.StopThinking();
                    _isThinkingActive = false;
                    break;
                case "action":
                    // アクション単体ではThinkingを解除しない（target/emote待ち）
                    break;
            }
        }

        /// <summary>
        /// emotionフィールドの逐次適用
        /// rawValueは生JSON（例: {"happy":0.8,"relaxed":0.5,...}）
        /// </summary>
        private void ApplyEmotionField(string rawValue)
        {
            if (expressionController == null) return;

            try
            {
                var emotion = JsonUtility.FromJson<EmotionData>(rawValue);
                if (emotion != null)
                {
                    expressionController.SetEmotion(emotion);
                    _currentEmotion = emotion.GetDominantEmotion().ToString().ToLower();
                    Debug.Log($"[ChatManager] Emotion applied incrementally: {_currentEmotion}");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ChatManager] Failed to parse emotion field: {e.Message}");
            }
        }

        /// <summary>
        /// reactionフィールドの逐次適用
        /// rawValueは生JSON文字列（例: "いいね、それ!"）
        /// </summary>
        private void ApplyReactionField(string rawValue)
        {
            string reactionText = ParseJsonStringValue(rawValue);
            if (string.IsNullOrEmpty(reactionText)) return;

            Debug.Log($"[ChatManager] Reaction received: {reactionText}");

            OnStreamingReactionReceived?.Invoke(reactionText);

            // 音声合成にも転送
            if (voiceSynthesisController != null)
            {
                voiceSynthesisController.OnStreamingTextReceived(reactionText);
            }
        }

        /// <summary>
        /// JSON生文字列値（引用符付き）からテキストを抽出
        /// 例: "\"いいね!\"" → "いいね!"
        /// </summary>
        [Serializable]
        private class StringFieldWrapper { public string v; }

        private static string ParseJsonStringValue(string rawJsonValue)
        {
            if (string.IsNullOrEmpty(rawJsonValue)) return "";

            try
            {
                var wrapper = JsonUtility.FromJson<StringFieldWrapper>("{\"v\":" + rawJsonValue + "}");
                return wrapper?.v ?? "";
            }
            catch
            {
                // フォールバック: 引用符を手動で除去
                string trimmed = rawJsonValue.Trim();
                if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"')
                {
                    return trimmed.Substring(1, trimmed.Length - 2);
                }
                return trimmed;
            }
        }

        // ===================================================================
        // ブロッキング方式ハンドラ（既存 + ストリーミング完了時の共通処理）
        // ===================================================================

        /// <summary>
        /// LLMレスポンスを処理（ブロッキング完了 / ストリーミング完了 共通）
        /// </summary>
        private void HandleLLMResponse(LLMResponseData response)
        {
            // パースエラーで既に処理済みの場合はスキップ
            if (_parseErrorHandled)
            {
                _parseErrorHandled = false;
                _incrementalFieldsApplied = false;
                _isStreamingRequest = false;
                _isAutoRequest = false;
                return;
            }

            Debug.Log($"[ChatManager] Response received: action={response?.action}, message={response?.message}");

            // Sleep中の応答処理
            if (sleepController != null && sleepController.IsSleeping)
            {
                if (_isWakeUpRequest)
                {
                    // 起床リクエスト: 通常処理するがCharacterControllerへの通知はキューに入れる
                    Debug.Log("[ChatManager] Wake-up response during ed, will queue for CharacterController");
                    // fall through to normal processing below
                }
                else
                {
                    // 夢メッセージ応答: 履歴に追加せず、Zzz...表示、action/emotion無視
                    Debug.Log("[ChatManager] Sleep mode: suppressing response");
                    _isAutoRequest = false;
                    _incrementalFieldsApplied = false;
                    _isStreamingRequest = false;
                    SetState(ChatState.Idle);

                    // Zzz...表示（メッセージがある場合のみ）
                    if (response.HasMessage)
                    {
                        OnMessageReceived?.Invoke(sleepController.SleepDisplayText);
                    }

                    // OnChatResponseReceivedは発火しない（CharacterControllerで処理させない）
                    return;
                }
            }

            // 自律リクエストで "ignore" → 履歴に追加せず
            if (_isAutoRequest && response.IsIgnore)
            {
                Debug.Log("[ChatManager] Auto-request ignored by LLM");
                _isAutoRequest = false;
                _incrementalFieldsApplied = false;
                _isStreamingRequest = false;
                SetState(ChatState.Idle);
                OnChatResponseReceived?.Invoke(response);
                // ストリーミング表示のリセットのためOnMessageReceivedを発火
                if (response.HasMessage)
                {
                    OnMessageReceived?.Invoke(response.FullMessage);
                }
                return;
            }

            if (_isAutoRequest)
            {
                Debug.Log($"[ChatManager] Auto-request response: {response.message}");
            }

            _isAutoRequest = false;

            // メッセージがある場合のみ会話履歴に追加（reaction + message 結合）
            if (response.HasMessage)
            {
                _conversationHistory.Add(new ConversationEntry
                {
                    role = "assistant",
                    content = response.FullMessage,
                    timestamp = DateTime.Now
                });
                TrimHistory();
                SaveHistory();
            }

            // 現在の状態を更新
            _currentPose = response.action ?? "ignore";
            _currentEmotion = response.emotion?.GetDominantEmotion().ToString().ToLower() ?? "neutral";

            // 表情適用（ブロッキング時用。ストリーミング逐次反映済みの場合はスキップ）
            if (!_incrementalFieldsApplied && expressionController != null && response.emotion != null)
            {
                expressionController.SetEmotion(response.emotion);
            }

            // 感情ベースの退屈度変動
            if (boredomController != null && response.emotion != null)
            {
                boredomController.ApplyEmotionDelta(response.emotion);
            }

            _incrementalFieldsApplied = false;

            SetState(ChatState.Idle);

            // ブロッキング応答時のみ音声合成
            // ストリーミング時はHandleStreamText→OnStreamingTextReceivedで文単位合成済み
            // Outing中は抑制（Cron帰宅リクエスト中は通す）
            bool suppressBlockingTTS = outingController != null && outingController.IsOutside && !_isCronEntryRequest;
            if (!_isStreamingRequest && voiceSynthesisController != null && response.HasMessage && !suppressBlockingTTS)
            {
                voiceSynthesisController.SynthesizeAndPlay(response.FullMessage);
            }

            _isStreamingRequest = false;

            // 起床リクエスト中: CharacterControllerへの通知をキューに入れる
            // ed完了後にWakeUpWithMessageコールバックで処理される
            if (_isWakeUpRequest && sleepController != null && sleepController.IsWakingUp)
            {
                sleepController.QueueWakeUpResponse(response);
            }
            // Entry再生中: レスポンスをキューに入れ、Entry完了後の
            // FlushEntryQueuedResponse（OnEntryAnimationCompleted購読）で発火する。
            // 通常Entry / Cron帰宅 共通。
            // Entry TimelineにActionCancelClipがあれば、CancelRegion到達時点で
            // 早期完了させて応答発火を前倒しする。
            else if (outingController != null && outingController.IsPlayingEntry)
            {
                _entryQueuedResponse = response;
                Debug.Log($"[ChatManager] Entry: response queued during entry animation");
                outingController.RequestEarlyEntryComplete();
            }
            else
            {
                OnChatResponseReceived?.Invoke(response);
            }
            OnMessageReceived?.Invoke(response.FullMessage);
        }

        // ===================================================================
        // 外部アクションフィード（受動制御）用の応答注入
        // ===================================================================

        /// <summary>
        /// 外部アクションフィードから受信した完成済み応答を適用する。
        /// LLMへリクエストは送らず、通常のブロッキング応答と同じ確定処理
        /// （会話履歴・感情・退屈度・TTS・UI表示・CharacterControllerルーティング）を通す。
        /// これによりアニメ・emote・視線・口パク・メッセージ表示・音声が一気通貫で駆動される。
        /// 外部フィードは「その場でアクションを見せる」用途のため、
        /// 進行中リクエスト（応答待ち/Thinking）・睡眠中・外出中はスキップする（falseを返す）。
        /// ※ ChatState は Idle/WaitingForResponse/Error の3値のみで睡眠中・外出中を表現しないため、
        ///    SleepController.IsSleeping / OutingController.IsOutside を明示的にチェックする必要がある。
        /// </summary>
        /// <param name="response">適用するLLMResponseData（LLMResponseData.FromJsonでパース済みを想定）</param>
        /// <returns>適用した場合true、ビジー等でスキップした場合false</returns>
        public bool ApplyExternalResponse(LLMResponseData response)
        {
            if (response == null)
            {
                Debug.LogWarning("[ChatManager] ApplyExternalResponse: response is null");
                return false;
            }

            // 進行中のリクエスト（応答待ち）がある場合は適用しない
            // （HandleLLMResponseがSetState(Idle)＋イベント発火するため、実リクエストと競合する）
            if (_currentState != ChatState.Idle)
            {
                Debug.Log($"[ChatManager] ApplyExternalResponse skipped (state={_currentState})");
                return false;
            }

            // Thinking演出中（起床ed/Entry再生中はIdleでも_isThinkingActive=trueのことがある）はスキップ。
            // HandleLLMResponseはThinkingを止めないため、割り込むと考え中演出が残る恐れがある。
            if (_isThinkingActive)
            {
                Debug.Log("[ChatManager] ApplyExternalResponse skipped (thinking active)");
                return false;
            }

            // 睡眠中・外出中はスキップ。_currentStateはこれらの状態でもIdleに戻っているため
            // 別コントローラーのフラグで明示的に判定する必要がある。
            if (sleepController != null && sleepController.IsSleeping)
            {
                Debug.Log("[ChatManager] ApplyExternalResponse skipped (sleeping)");
                return false;
            }
            if (outingController != null && outingController.IsOutside)
            {
                Debug.Log("[ChatManager] ApplyExternalResponse skipped (outside)");
                return false;
            }

            // Entry（入室演出）再生中はスキップ。
            // Entry中はNavMeshAgentが無効化されており、move等を適用すると
            // ナビゲーションが無効なAgentに対して走り出してしまう。
            if (outingController != null && outingController.IsPlayingEntry)
            {
                Debug.Log("[ChatManager] ApplyExternalResponse skipped (entry playing)");
                return false;
            }

            // 起動時の初回Entry完了前はスキップ。
            // 起動直後はVRMロード〜入室演出の準備中で、この間に適用すると
            // PlayableDirector未割当のままナビゲーション開始→Entry開始でAgent無効化→
            // 「"Move" can only be called on an active agent」エラー連発＋瞬間移動が発生する。
            if (outingController != null && !_hasInitialEntryCompleted)
            {
                Debug.Log("[ChatManager] ApplyExternalResponse skipped (initial entry not completed)");
                return false;
            }

            // null/空フィールドにデフォルト補填（手動構築されたresponseへの安全策。
            // FromJson経由なら既に補填済みだがFillDefaultsは冪等なので害はない）
            response.FillDefaults();

            // リクエスト種別フラグを全てクリアし、通常のブロッキング応答として確定処理へ流す
            _isAutoRequest = false;
            _isStreamingRequest = false;
            _isWakeUpRequest = false;
            _isCronEntryRequest = false;
            _incrementalFieldsApplied = false;
            _parseErrorHandled = false;

            Debug.Log($"[ChatManager] Applying external feed response: action={response.action}, message={response.message}");
            HandleLLMResponse(response);
            return true;
        }

        /// <summary>
        /// ストリーミング中のJSONパースエラーを処理
        /// エラーメッセージと生レスポンステキストをUIに表示し、生テキストをTTS対象にする
        /// </summary>
        private void HandleStreamParseError(string error, string rawText)
        {
            _parseErrorHandled = true;
            _isAutoRequest = false;

            // Thinking解除
            if (_isThinkingActive && talkController != null)
            {
                talkController.StopThinking();
                _isThinkingActive = false;
            }

            _incrementalFieldsApplied = false;
            SetState(ChatState.Idle);

            // UIにエラー + 生テキストを表示
            OnParseError?.Invoke(error, rawText);

            // 生テキストをTTSに送信（エラーメッセージは送信しない）
            if (voiceSynthesisController != null && !string.IsNullOrEmpty(rawText))
            {
                voiceSynthesisController.SynthesizeAndPlay(rawText);
            }

            // CharacterControllerに通知（逐次反映済みフィールドのクリーンアップ用）
            var fallbackResponse = LLMResponseData.GetFallback();
            fallbackResponse.message = rawText;
            OnChatResponseReceived?.Invoke(fallbackResponse);
        }

        /// <summary>
        /// LLMエラーを処理
        /// Errorのまま放置するとIdleChat/Cron/夢/外出メッセージ等の自律動作が
        /// 全停止するため、リクエスト種別フラグをリセットし数秒後にIdleへ自動復帰する
        /// </summary>
        private void HandleLLMError(string error)
        {
            // リクエスト種別フラグをリセット
            // （残留すると次回リクエストがauto扱いになり、Thinking遷移や履歴追加が壊れる）
            // 注意: _isWakeUpRequest / _isCronEntryRequest はここでリセットしない。
            // OnErrorの直後に発火するHandleRequestCompletedがsuppressStreamingTTS判定で
            // これらを参照しており、先にリセットするとTTSのストリーミング状態が閉じられず
            // 次応答への断片混入やSTT再開ブロックが起きる。
            // この2つはed完了/Entry完了コールバックで必ずfalseに戻る。
            _isAutoRequest = false;
            _isStreamingRequest = false;
            _incrementalFieldsApplied = false;
            _parseErrorHandled = false;

            SetState(ChatState.Error);
            OnError?.Invoke(error);

            if (_errorRecoveryCoroutine != null)
            {
                StopCoroutine(_errorRecoveryCoroutine);
            }
            _errorRecoveryCoroutine = StartCoroutine(RecoverFromErrorAfterDelay());
        }

        /// <summary>
        /// Error状態から一定時間後にIdleへ復帰する
        /// エラー表示中に新しいリクエストが始まっていた場合は何もしない
        /// </summary>
        private IEnumerator RecoverFromErrorAfterDelay()
        {
            yield return new WaitForSeconds(errorRecoveryDelay);
            _errorRecoveryCoroutine = null;

            if (_currentState != ChatState.Error) yield break;

            // Thinking解除の保険
            // （通常はHandleRequestCompletedで解除済みだが、起床ed/Entry完了コールバックが
            // エラー後にThinkingを開始してしまった場合はここで回収する）
            if (_isThinkingActive && talkController != null)
            {
                talkController.StopThinking();
                _isThinkingActive = false;
            }

            Debug.Log("[ChatManager] Recovered from Error state to Idle");
            SetState(ChatState.Idle);
        }

        /// <summary>
        /// 会話履歴をトリム
        /// </summary>
        private void TrimHistory()
        {
            while (_conversationHistory.Count > maxHistoryLength)
            {
                _conversationHistory.RemoveAt(0);
            }
        }

        /// <summary>
        /// 会話履歴をPlayerPrefsに保存
        /// </summary>
        private void SaveHistory()
        {
            var data = new SerializableConversationHistory
            {
                entries = _conversationHistory.Select(e => new SerializableConversationEntry
                {
                    role = e.role,
                    content = e.content,
                    timestamp = e.timestamp.ToString("o")
                }).ToArray()
            };
            string json = JsonUtility.ToJson(data);
            PlayerPrefs.SetString(PrefKey_ConversationHistory, json);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// PlayerPrefsから会話履歴を復元
        /// </summary>
        private void LoadHistory()
        {
            if (!PlayerPrefs.HasKey(PrefKey_ConversationHistory)) return;

            string json = PlayerPrefs.GetString(PrefKey_ConversationHistory);
            if (string.IsNullOrEmpty(json)) return;

            try
            {
                var data = JsonUtility.FromJson<SerializableConversationHistory>(json);
                if (data?.entries == null) return;

                _conversationHistory.Clear();
                foreach (var e in data.entries)
                {
                    _conversationHistory.Add(new ConversationEntry
                    {
                        role = e.role,
                        content = e.content,
                        timestamp = DateTime.TryParse(e.timestamp, out var dt) ? dt : DateTime.Now
                    });
                }
                Debug.Log($"[ChatManager] Loaded {_conversationHistory.Count} history entries");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ChatManager] Failed to load history: {e.Message}");
            }
        }

        /// <summary>
        /// 会話履歴をクリア
        /// </summary>
        public void ClearHistory()
        {
            _conversationHistory.Clear();
            llmClient?.ClearConversation();
            PlayerPrefs.DeleteKey(PrefKey_ConversationHistory);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// 状態を更新
        /// </summary>
        private void SetState(ChatState newState)
        {
            if (_currentState != newState)
            {
                _currentState = newState;
                OnStateChanged?.Invoke(newState);
            }
        }

    }

    [Serializable]
    public class ConversationEntry
    {
        public string role;      // "user" or "assistant"
        public string content;
        public DateTime timestamp;
    }

    /// <summary>
    /// PlayerPrefs保存用のシリアライズ可能な会話エントリ。
    /// DateTimeはISO 8601文字列として保持する。
    /// </summary>
    [Serializable]
    public class SerializableConversationEntry
    {
        public string role;
        public string content;
        public string timestamp;
    }

    [Serializable]
    public class SerializableConversationHistory
    {
        public SerializableConversationEntry[] entries;
    }

    public enum ChatState
    {
        Idle,               // 待機中
        WaitingForResponse, // LLM応答待ち
        Error               // エラー状態
    }
}
