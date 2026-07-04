using UnityEngine;
using CyanNook.Core;
using CyanNook.UI;

namespace CyanNook.Chat
{
    /// <summary>
    /// 自律リクエスト（IdleChat）制御
    /// 応答完了後、クールダウン時間が経過したらLLMに自動でリクエストを送り
    /// キャラクターが自発的に話しかけてくる体験を実現する
    /// </summary>
    public class IdleChatController : MonoBehaviour
    {
        // PlayerPrefsキー
        // ON/OFFは定期実行マスター（PeriodicExecutionSettings）に統合済み
        private const string PrefKey_Cooldown = "idleChatCooldown";
        private const string PrefKey_Message = "idleChat_message";

        [Header("References")]
        public ChatManager chatManager;

        [Tooltip("初期設定コントローラー（表示中はidleChatを抑制）")]
        public FirstRunController firstRunController;

        [Header("Settings")]
        [Tooltip("自律リクエストを有効にする")]
        public bool autoRequestEnabled = false;

        [Tooltip("応答完了後、次のリクエストまでの待機秒数")]
        public float cooldownDuration = 10f;

        [Header("Prompt")]
        [TextArea(2, 4)]
        [Tooltip("自律リクエスト時にLLMに送るメッセージ")]
        public string idlePromptMessage = "[SYSTEM]: ユーザーは黙っています。何か話しかけますか？\n話さない場合は action を \"ignore\" にしてください。";

        private enum IdleChatState
        {
            Inactive,           // 機能OFF or 初期状態
            Cooldown,           // 応答後のクールダウン中
            WaitingForResponse  // 自律リクエスト送信済み、応答待ち
        }

        private IdleChatState _state = IdleChatState.Inactive;
        private float _timer;
        private bool _isPaused = false;
        private bool _wasEnabledBeforePause = false;

        /// <summary>
        /// 現在の状態（デバッグ用）
        /// </summary>
        public string CurrentStateInfo
        {
            get
            {
                if (!autoRequestEnabled) return "OFF";
                return _state switch
                {
                    IdleChatState.Inactive => "Inactive",
                    IdleChatState.Cooldown => $"Cooldown ({_timer:F0}s)",
                    IdleChatState.WaitingForResponse => "Requesting...",
                    _ => "Unknown"
                };
            }
        }

        private void Start()
        {
            LoadSettings();

            if (chatManager != null)
            {
                chatManager.OnChatResponseReceived += OnChatResponseReceived;
                chatManager.OnStateChanged += OnChatStateChanged;
                chatManager.OnError += OnChatError;
            }

            if (autoRequestEnabled)
            {
                StartCooldown();
            }
        }

        private void OnDestroy()
        {
            if (chatManager != null)
            {
                chatManager.OnChatResponseReceived -= OnChatResponseReceived;
                chatManager.OnStateChanged -= OnChatStateChanged;
                chatManager.OnError -= OnChatError;
            }
        }

        private void Update()
        {
            if (_isPaused || !autoRequestEnabled || chatManager == null) return;

            // interval 0以下 = この機能のみ個別に無効（定期実行マスターONのまま）
            if (cooldownDuration <= 0f) return;

            if (_state == IdleChatState.Cooldown)
            {
                _timer -= Time.deltaTime;
                if (_timer <= 0f)
                {
                    SendAutoRequest();
                }
            }
        }

        // ─────────────────────────────────────
        // 公開メソッド
        // ─────────────────────────────────────

        /// <summary>
        /// 自律リクエストのON/OFF切替
        /// </summary>
        public void SetEnabled(bool enabled)
        {
            autoRequestEnabled = enabled;

            if (enabled)
            {
                StartCooldown();
            }
            else
            {
                _state = IdleChatState.Inactive;
            }

            SaveSettings();
            Debug.Log($"[IdleChatController] AutoRequest: {(enabled ? "ON" : "OFF")}");
        }

        /// <summary>
        /// 一時停止/再開（Sleep中に使用）
        /// 停止前のEnabled状態を保持し、再開時に復元する
        /// </summary>
        public void SetPaused(bool paused)
        {
            if (paused)
            {
                _wasEnabledBeforePause = autoRequestEnabled;
                _isPaused = true;
                _state = IdleChatState.Inactive;
                Debug.Log("[IdleChatController] Paused");
            }
            else
            {
                _isPaused = false;
                if (_wasEnabledBeforePause)
                {
                    StartCooldown();
                }
                Debug.Log($"[IdleChatController] Resumed (enabled={_wasEnabledBeforePause})");
            }
        }

        /// <summary>
        /// ユーザーがメッセージを送信した時に呼び出し
        /// タイマーをリセットして待機状態に戻す
        /// </summary>
        public void OnUserMessageSent()
        {
            if (!autoRequestEnabled) return;

            // ユーザー入力があったのでタイマーリセット
            // WaitingForResponse中でもリセット（ChatManager側で中断処理済み）
            StartCooldown();
        }

        /// <summary>
        /// クールダウン時間を設定。0で個別無効
        /// </summary>
        public void SetCooldownDuration(float seconds)
        {
            cooldownDuration = Mathf.Max(0f, seconds);

            // タイマーを新しい値で再セット（0→正値に戻した瞬間の即時発火を防ぐ）
            if (autoRequestEnabled && cooldownDuration > 0f)
            {
                StartCooldown();
            }

            SaveSettings();
        }

        /// <summary>
        /// アイドルプロンプトメッセージを設定
        /// </summary>
        public void SetIdlePromptMessage(string message)
        {
            idlePromptMessage = message;
            SaveSettings();
        }

        // ─────────────────────────────────────
        // 内部メソッド
        // ─────────────────────────────────────

        private void StartCooldown()
        {
            _state = IdleChatState.Cooldown;
            _timer = cooldownDuration;
        }

        private void SendAutoRequest()
        {
            if (chatManager == null || chatManager.CurrentState != ChatState.Idle)
            {
                // チャットがビジー状態ならリトライ（次フレームで再チェック）
                return;
            }

            // 初期設定が完了していない場合はリクエストを抑制
            // （Noスキップ後もIsPending=trueのため、設定完了まで抑制される）
            if (firstRunController != null && firstRunController.IsPending)
            {
                return;
            }

            _state = IdleChatState.WaitingForResponse;
            Debug.Log("[IdleChatController] Sending auto-request to LLM");
            chatManager.SendAutoRequest(idlePromptMessage);
        }

        // ─────────────────────────────────────
        // イベントハンドラ
        // ─────────────────────────────────────

        private void OnChatResponseReceived(LLMResponseData response)
        {
            if (!autoRequestEnabled) return;

            // LLMが応答した/ignoreした → クールダウン開始
            StartCooldown();
        }

        private void OnChatStateChanged(ChatState state)
        {
            if (!autoRequestEnabled) return;

            // ユーザー入力によるリクエスト開始時（自律リクエスト以外）
            if (state == ChatState.WaitingForResponse && _state != IdleChatState.WaitingForResponse)
            {
                // ユーザーが入力したのでタイマー停止（OnChatResponseReceivedで再開される）
                _state = IdleChatState.Inactive;
            }
        }

        private void OnChatError(string error)
        {
            if (!autoRequestEnabled) return;

            // エラー時はクールダウンから再開
            StartCooldown();
        }

        // ─────────────────────────────────────
        // 設定の保存/読み込み
        // ─────────────────────────────────────

        private void SaveSettings()
        {
            // ON/OFFは定期実行マスターとして保存（Sleep/Outingと共有）
            PeriodicExecutionSettings.SetEnabled(autoRequestEnabled);
            PlayerPrefs.SetFloat(PrefKey_Cooldown, cooldownDuration);
            PlayerPrefs.SetString(PrefKey_Message, idlePromptMessage);
            PlayerPrefs.Save();
        }

        private void LoadSettings()
        {
            // ON/OFFは定期実行マスターから読み込む（旧idleChatEnabledからの移行も内部で処理）
            autoRequestEnabled = PeriodicExecutionSettings.IsEnabled();

            if (PlayerPrefs.HasKey(PrefKey_Cooldown))
            {
                cooldownDuration = PlayerPrefs.GetFloat(PrefKey_Cooldown);
            }
            if (PlayerPrefs.HasKey(PrefKey_Message))
            {
                idlePromptMessage = PlayerPrefs.GetString(PrefKey_Message);
            }
        }
    }
}
