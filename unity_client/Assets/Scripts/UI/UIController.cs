using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using CyanNook.Character;
using CyanNook.Core;
using CyanNook.Chat;
using CyanNook.Voice;
using System;

namespace CyanNook.UI
{
    /// <summary>
    /// UI制御（チャット入出力・ストリーミング表示に特化）
    /// 設定パネル関連は各SettingsPanelクラスに分離
    /// </summary>
    public class UIController : MonoBehaviour
    {
        [Header("References")]
        public CharacterAnimationController animationController;
        public CharacterExpressionController expressionController;
        public ChatManager chatManager;
        public CyanNook.Character.CharacterController characterController;
        public IdleChatController idleChatController;
        public SettingsMenuController settingsMenuController;
        public OutingController outingController;

        [Header("UI Elements - Input")]
        [Tooltip("入力フィールド（Chat/JSON共用）")]
        public TMP_InputField chatInputField;

        [Header("UI Elements - Hide Toggle")]
        [Tooltip("UI全体を格納するコンテナ（トグルで非表示にする対象）")]
        public GameObject uiContainer;

        [Tooltip("UI非表示トグルボタン")]
        public Button uiHideToggleButton;

        [Tooltip("UI表示中アイコン")]
        public Image uiShowIcon;

        [Tooltip("UI非表示中アイコン")]
        public Image uiHideIcon;

        [Header("UI Elements - Mic Button")]
        [Tooltip("マイクミュートボタン")]
        public Button micButton;

        [Tooltip("マイクONアイコン")]
        public Image micOnIcon;

        [Tooltip("マイクOFF（ミュート）アイコン")]
        public Image micOffIcon;

        [Tooltip("VoiceInputController参照")]
        public VoiceInputController voiceInputController;

        [Header("UI Elements - Display")]
        [Tooltip("キャラクターメッセージ表示")]
        public TMP_Text messageText;

        [Tooltip("モード表示ラベル")]
        public TMP_Text modeLabel;

        [Tooltip("TTSクレジット表示（例: VOICEVOX:ずんだもん(ノーマル)）")]
        public TMP_Text ttsCreditText;

        [Tooltip("VoiceSynthesisController参照（TTSクレジット表示用）")]
        public VoiceSynthesisController voiceSynthesisController;

        [Header("UI Elements - Raw Text")]
        [Tooltip("LLM生レスポンス表示（RawTextPanel内に配置）")]
        public TMP_Text llmRawResponseText;

        [Header("Input Mode Settings")]
        [Tooltip("JSONモード時の入力フィールド高さ（ピクセル）")]
        public float jsonModeInputHeight = 500f;

        [Tooltip("Chatモード時の入力フィールド高さ（ピクセル）")]
        public float chatModeInputHeight = 100f;

        [Header("Input Mode Font")]
        [Tooltip("Chat用フォント")]
        public TMP_FontAsset chatFont;

        [Tooltip("JSON用フォント（コード）")]
        public TMP_FontAsset codeFont;

        [Tooltip("Chat用フォントサイズ")]
        public float chatFontSize = 18f;

        [Tooltip("JSON用フォントサイズ")]
        public float codeFontSize = 14f;

        [Header("Status Overlay")]
        [Tooltip("StatusOverlay参照（エラー表示用）")]
        public StatusOverlay statusOverlay;

        [Header("Settings")]
        [Tooltip("テキスト表示完了後、メッセージが消えるまでの時間（秒）。0で無限")]
        public float messageDisplayDuration = 5f;

        [Tooltip("エラーメッセージの表示色")]
        public Color errorMessageColor = new Color(1f, 0.4f, 0.4f, 1f);

        [Tooltip("初期モード")]
        public InputMode initialMode = InputMode.Chat;

        /// <summary>現在の入力モード</summary>
        public InputMode CurrentInputMode => _currentMode;

        private InputMode _currentMode;
        private float _messageTimer = 0f;

        // Enter送信の二重処理防止フラグ
        private bool _isSubmitting;

        // 改行検出用: 前回のテキスト状態を記録
        private int _previousTextLength;
        private int _previousNewlineCount;
        private string _previousText = "";

        // ストリーミングテキスト蓄積用
        private System.Text.StringBuilder _streamingMessageBuilder;
        private string _streamingHeaderJson;
        private string _streamingReaction;        // reaction テキスト
        private bool _isFirstMessageChunk;        // message 最初のチャンクか

        // リクエストボディ表示用
        private string _lastRequestBodyDisplay;

        private void Start()
        {
            // 初期モード設定
            _currentMode = initialMode;
            UpdateModeLabel();
            UpdateInputFieldAppearance();

            // 入力フィールド初期化
            if (chatInputField != null)
            {
                chatInputField.lineType = TMP_InputField.LineType.MultiLineNewline;
                chatInputField.onEndEdit.AddListener(OnInputEndEdit);
                chatInputField.onValueChanged.AddListener(OnInputValueChanged);

                chatInputField.text = _currentMode == InputMode.Json ? GetSampleJson() : "";
                chatInputField.ActivateInputField();
            }

            // メッセージ初期化
            if (messageText != null)
            {
                messageText.text = "";
            }

            // ChatManagerイベント登録
            if (chatManager != null)
            {
                chatManager.OnMessageReceived += OnChatMessageReceived;
                chatManager.OnError += OnChatError;
                chatManager.OnThinkingStarted += OnThinkingStarted;
                chatManager.OnThinkingEnded += OnThinkingEnded;
                chatManager.OnStreamingHeaderReceived += OnStreamingHeader;
                chatManager.OnStreamingTextReceived += OnStreamingText;
                chatManager.OnStreamingReactionReceived += OnStreamingReaction;
                chatManager.OnParseError += OnChatParseError;
            }

            // LLMリクエスト/レスポンスイベント登録
            if (chatManager?.llmClient != null)
            {
                chatManager.llmClient.OnRequestBodySent += OnLLMRequestBodySent;
                chatManager.llmClient.OnRawResponseReceived += OnLLMRawResponse;
            }

            // 設定パネルを閉じたらチャット入力にフォーカスを戻す
            if (settingsMenuController != null)
            {
                settingsMenuController.OnPanelClosed += OnSettingsPanelClosed;
            }

            // 外出イベント登録
            if (chatManager != null)
            {
                chatManager.OnOutingMessageBlocked += OnOutingMessageBlocked;
            }

            // UI非表示トグル
            if (uiHideToggleButton != null)
            {
                uiHideToggleButton.onClick.AddListener(OnUIHideToggleClicked);
            }
            UpdateUIHideToggleVisual(true);

            // マイクボタン
            if (micButton != null)
            {
                micButton.onClick.AddListener(OnMicButtonClicked);
            }
            if (voiceInputController != null)
            {
                voiceInputController.OnEnabledChanged += OnVoiceInputEnabledChanged;
                UpdateMicButtonVisual(voiceInputController.IsEnabled);
            }
            else
            {
                UpdateMicButtonVisual(false);
            }

            // TTSクレジット表示
            if (voiceSynthesisController != null)
            {
                voiceSynthesisController.OnTTSCreditChanged += OnTTSCreditChanged;
                UpdateTTSCreditDisplay(voiceSynthesisController.TTSCreditText);
            }
        }

        private void OnDestroy()
        {
            if (chatInputField != null)
            {
                chatInputField.onEndEdit.RemoveListener(OnInputEndEdit);
                chatInputField.onValueChanged.RemoveListener(OnInputValueChanged);
            }

            if (chatManager != null)
            {
                chatManager.OnMessageReceived -= OnChatMessageReceived;
                chatManager.OnError -= OnChatError;
                chatManager.OnThinkingStarted -= OnThinkingStarted;
                chatManager.OnThinkingEnded -= OnThinkingEnded;
                chatManager.OnStreamingHeaderReceived -= OnStreamingHeader;
                chatManager.OnStreamingTextReceived -= OnStreamingText;
                chatManager.OnStreamingReactionReceived -= OnStreamingReaction;
                chatManager.OnParseError -= OnChatParseError;
            }

            if (chatManager?.llmClient != null)
            {
                chatManager.llmClient.OnRequestBodySent -= OnLLMRequestBodySent;
                chatManager.llmClient.OnRawResponseReceived -= OnLLMRawResponse;
            }

            if (settingsMenuController != null)
            {
                settingsMenuController.OnPanelClosed -= OnSettingsPanelClosed;
            }

            if (chatManager != null)
            {
                chatManager.OnOutingMessageBlocked -= OnOutingMessageBlocked;
            }

            if (voiceSynthesisController != null)
            {
                voiceSynthesisController.OnTTSCreditChanged -= OnTTSCreditChanged;
            }

            if (uiHideToggleButton != null)
            {
                uiHideToggleButton.onClick.RemoveListener(OnUIHideToggleClicked);
            }
            if (micButton != null)
            {
                micButton.onClick.RemoveListener(OnMicButtonClicked);
            }
            if (voiceInputController != null)
            {
                voiceInputController.OnEnabledChanged -= OnVoiceInputEnabledChanged;
            }
        }

        private void OnSettingsPanelClosed()
        {
            if (chatInputField != null)
            {
                chatInputField.ActivateInputField();
            }
        }

        private void Update()
        {
            // メッセージの自動非表示
            if (_messageTimer > 0)
            {
                _messageTimer -= Time.deltaTime;
                if (_messageTimer <= 0 && messageText != null)
                {
                    messageText.text = "";
                }
            }
        }

        // ─────────────────────────────────────
        // 入力モード
        // ─────────────────────────────────────

        /// <summary>
        /// 入力モードを外部から設定（DebugSettingsPanelから呼ばれる）
        /// </summary>
        public void SetInputMode(InputMode mode)
        {
            _currentMode = mode;
            UpdateModeLabel();
            UpdateInputFieldAppearance();

            // モード切替時に入力フィールドをリセット
            if (chatInputField != null)
            {
                chatInputField.text = _currentMode == InputMode.Json ? GetSampleJson() : "";
            }
        }

        private void UpdateModeLabel()
        {
            if (modeLabel != null)
            {
                modeLabel.text = _currentMode == InputMode.Json ? "JSON Mode" : "Chat Mode";
            }
        }

        private void UpdateInputFieldAppearance()
        {
            if (chatInputField == null) return;

            var rect = chatInputField.GetComponent<RectTransform>();
            var textComponent = chatInputField.textComponent;

            if (_currentMode == InputMode.Json)
            {
                if (rect != null)
                {
                    // 高さを直接設定（アンカーはエディター設定を維持）
                    rect.sizeDelta = new Vector2(rect.sizeDelta.x, jsonModeInputHeight);
                }
                if (codeFont != null && textComponent != null)
                {
                    textComponent.font = codeFont;
                }
                if (textComponent != null)
                {
                    textComponent.fontSize = codeFontSize;
                }
            }
            else
            {
                if (rect != null)
                {
                    // 高さを直接設定（アンカーはエディター設定を維持）
                    rect.sizeDelta = new Vector2(rect.sizeDelta.x, chatModeInputHeight);
                }
                if (chatFont != null && textComponent != null)
                {
                    textComponent.font = chatFont;
                }
                if (textComponent != null)
                {
                    textComponent.fontSize = chatFontSize;
                }
            }
        }

        // ─────────────────────────────────────
        // 入力処理
        // ─────────────────────────────────────

        /// <summary>
        /// onValueChangedコールバック（主要なEnter検出メカニズム）
        /// MultiLineNewlineモードではEnter押下時に"\n"がテキスト末尾に追加される。
        /// onEndEditはこのモードでは発火しないため、onValueChangedで"\n"を検出して送信する。
        /// </summary>
        private void OnInputValueChanged(string text)
        {
            if (_isSubmitting) return;
            if (string.IsNullOrEmpty(text))
            {
                _previousTextLength = 0;
                _previousText = "";
                return;
            }

            int currentLength = text.Length;

            // テキストが1文字だけ増え、その増分が\nである = Enterキー押下
            bool enterDetected = false;
            if (currentLength == _previousTextLength + 1)
            {
                int newNewlines = 0;
                for (int i = 0; i < text.Length; i++)
                    if (text[i] == '\n') newNewlines++;

                enterDetected = newNewlines > _previousNewlineCount;
            }

            _previousTextLength = currentLength;
            _previousNewlineCount = 0;
            for (int i = 0; i < text.Length; i++)
                if (text[i] == '\n') _previousNewlineCount++;

            if (!enterDetected)
            {
                _previousText = text;
                return;
            }

            // Shift+Enterのチェック
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.shiftKey.isPressed)
            {
                _previousText = text;
                return; // Shift+Enter: 改行を維持
            }

            _isSubmitting = true;

            // JSONモード: 改行挿入前のテキストをそのまま使用（JSON構造を維持）
            // Chatモード: 改行を除去してトリム
            string cleaned;
            if (_currentMode == InputMode.Json && !string.IsNullOrEmpty(_previousText))
            {
                cleaned = _previousText;
            }
            else
            {
                int caretPos = chatInputField.caretPosition;
                int newlinePos = (caretPos > 0 && caretPos <= text.Length && text[caretPos - 1] == '\n')
                    ? caretPos - 1
                    : text.LastIndexOf('\n');

                cleaned = (newlinePos >= 0)
                    ? text.Remove(newlinePos, 1)
                    : text;
                cleaned = cleaned.TrimEnd('\n', '\r');
            }

            if (string.IsNullOrEmpty(cleaned.Trim()))
            {
                chatInputField.text = "";
                chatInputField.ActivateInputField();
            }
            else
            {
                chatInputField.text = cleaned;
                OnSend();
            }

            _isSubmitting = false;
        }

        /// <summary>
        /// onEndEditコールバック（フォールバック）
        /// フォーカスが外れた時に呼ばれる。
        /// 主にデスクトップ環境でKeyboard.currentが利用可能な場合のEnter検出として機能。
        /// onValueChangedで既に処理済みの場合はスキップ。
        /// </summary>
        public void OnInputEndEdit(string text)
        {
            if (_isSubmitting) return;

            bool enterPressed = false;
            bool shiftPressed = false;

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                enterPressed = keyboard.enterKey.isPressed || keyboard.numpadEnterKey.isPressed;
                shiftPressed = keyboard.shiftKey.isPressed;
            }

            // Fallback: テキスト末尾の"\n"でEnter判定
            if (!enterPressed && !string.IsNullOrEmpty(text) && text.EndsWith("\n"))
            {
                enterPressed = true;
            }

            if (!enterPressed) return;

            _isSubmitting = true;

            // Shift+Enter → 改行を挿入してフォーカスを戻す
            if (shiftPressed)
            {
                int pos = chatInputField.caretPosition;
                chatInputField.text = text.Insert(pos, "\n");
                chatInputField.ActivateInputField();
                StartCoroutine(SetCaretPositionNextFrame(pos + 1));
                _isSubmitting = false;
                return;
            }

            // 末尾の改行を除去
            string cleaned = text.TrimEnd('\n', '\r');

            // テキストが空 → IME変換確定のEnter、フォーカスを戻す
            if (string.IsNullOrEmpty(cleaned))
            {
                chatInputField.text = "";
                chatInputField.ActivateInputField();
                _isSubmitting = false;
                return;
            }

            chatInputField.text = cleaned;
            OnSend();
            _isSubmitting = false;
        }

        private System.Collections.IEnumerator SetCaretPositionNextFrame(int position)
        {
            yield return null;
            if (chatInputField != null)
            {
                chatInputField.caretPosition = position;
            }
        }

        /// <summary>
        /// 音声入力からメッセージを送信
        /// キーボードチェックをスキップして直接送信する
        /// </summary>
        public void SendMessageFromVoice(string text)
        {
            if (chatInputField == null) return;
            if (string.IsNullOrEmpty(text)) return;

            // 入力フィールドにテキストを設定
            chatInputField.text = text.TrimEnd('\n', '\r');

            // 送信処理を実行
            OnSend();
        }

        private void OnSend()
        {
            if (chatInputField == null) return;

            string input = chatInputField.text;

            if (_currentMode == InputMode.Json)
            {
                ProcessJson(input);
            }
            else
            {
                SendChatMessage(input);
            }
        }

        private void SendChatMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                Debug.LogWarning("[UIController] Empty chat message");
                return;
            }

            if (chatManager == null)
            {
                Debug.LogError("[UIController] ChatManager not set");
                ShowMessage("Error: ChatManager not set");
                return;
            }

            Debug.Log($"[UIController] Sending chat: {message}");
            chatManager.SendChatMessage(message);

            if (idleChatController != null)
            {
                idleChatController.OnUserMessageSent();
            }

            if (chatInputField != null)
            {
                chatInputField.text = "";
                chatInputField.ActivateInputField();
            }
        }

        // ─────────────────────────────────────
        // ChatManagerイベント
        // ─────────────────────────────────────

        /// <summary>外出中かどうか（外出中はメッセージ表示を抑制）</summary>
        private bool IsOutingActive => outingController != null && outingController.IsOutside;

        private void OnChatMessageReceived(string message)
        {
            _streamingHeaderJson = null;
            _streamingReaction = null;
            _streamingMessageBuilder = null;
            _isFirstMessageChunk = true;

            // 外出中はメッセージ欄を更新しない（お出かけ中表示を維持）
            if (IsOutingActive) return;

            ShowMessage(message);
        }

        private void OnChatError(string error)
        {
            Debug.LogWarning($"[UIController] Chat error: {error}");
            ShowErrorToOverlay(error);
        }

        /// <summary>
        /// JSONパースエラー時のハンドラ
        /// StatusOverlayにエラーを表示し、生テキストがあればメッセージ欄に表示する
        /// </summary>
        private void OnChatParseError(string error, string rawText)
        {
            ShowErrorToOverlay(error);
            if (!string.IsNullOrEmpty(rawText))
            {
                ShowMessage(rawText);
            }
        }

        private void ShowErrorToOverlay(string error)
        {
            if (statusOverlay != null)
            {
                statusOverlay.ShowError(error);
            }
            else
            {
                // StatusOverlay未設定時はメッセージ欄にフォールバック
                ShowMessage($"<color=#{ColorUtility.ToHtmlStringRGB(errorMessageColor)}>Error: {error}</color>");
            }
        }

        private void OnThinkingStarted()
        {
            // ストリーミング状態を初期化
            _streamingHeaderJson = null;
            _streamingReaction = null;
            _streamingMessageBuilder = null;
            _isFirstMessageChunk = true;

            // 外出中はメッセージ欄を更新しない
            if (IsOutingActive) return;

            ShowMessage("...");
        }

        private void OnThinkingEnded()
        {
        }

        // ─────────────────────────────────────
        // LLM Raw Response
        // ─────────────────────────────────────

        private void OnLLMRequestBodySent(string requestBody)
        {
            _lastRequestBodyDisplay = requestBody;
            if (_lastRequestBodyDisplay != null && _lastRequestBodyDisplay.Length > 2000)
            {
                _lastRequestBodyDisplay = _lastRequestBodyDisplay.Substring(0, 2000) + "\n... (truncated)";
            }

            if (llmRawResponseText != null)
            {
                llmRawResponseText.text = "--- REQUEST ---\n" + _lastRequestBodyDisplay + "\n\n--- RESPONSE ---\n(waiting...)";
            }
        }

        private string GetRequestPrefix()
        {
            if (string.IsNullOrEmpty(_lastRequestBodyDisplay)) return "";
            return "--- REQUEST ---\n" + _lastRequestBodyDisplay + "\n\n--- RESPONSE ---\n";
        }

        private void OnLLMRawResponse(string rawResponse)
        {
            if (llmRawResponseText != null)
            {
                if (_streamingHeaderJson == null)
                {
                    llmRawResponseText.text = GetRequestPrefix() + rawResponse;
                }
            }
        }

        // ─────────────────────────────────────
        // ストリーミング
        // ─────────────────────────────────────

        private void OnStreamingReaction(string reaction)
        {
            _streamingReaction = reaction;

            // 遅延初期化（自律リクエスト等でOnThinkingStartedが呼ばれない場合）
            if (_streamingMessageBuilder == null)
            {
                _streamingMessageBuilder = new System.Text.StringBuilder();
                _isFirstMessageChunk = true;
            }

            // 外出中はメッセージ欄を更新しない
            if (IsOutingActive) return;

            if (messageText != null)
            {
                messageText.text = reaction;
                _messageTimer = float.MaxValue;
            }
        }

        private void OnStreamingHeader(LlmResponseHeader header)
        {
            _streamingHeaderJson = JsonUtility.ToJson(header, true);

            // 遅延初期化
            if (_streamingMessageBuilder == null)
            {
                _streamingMessageBuilder = new System.Text.StringBuilder();
                _isFirstMessageChunk = true;
            }

            // raw表示を更新（ストリーミング中の蓄積テキストがあれば含める）
            if (llmRawResponseText != null)
            {
                string currentMessage = _streamingMessageBuilder.ToString();
                llmRawResponseText.text = GetRequestPrefix() + _streamingHeaderJson;
                if (!string.IsNullOrEmpty(currentMessage))
                {
                    llmRawResponseText.text += "\n" + currentMessage;
                }
            }
        }

        private void OnStreamingText(string textChunk)
        {
            // 遅延初期化
            if (_streamingMessageBuilder == null)
            {
                _streamingMessageBuilder = new System.Text.StringBuilder();
                _isFirstMessageChunk = true;
            }

            // 最初のメッセージチャンク: reaction があれば改行付きでプリペンド
            if (_isFirstMessageChunk)
            {
                _isFirstMessageChunk = false;
                if (!string.IsNullOrEmpty(_streamingReaction))
                {
                    _streamingMessageBuilder.Append(_streamingReaction);
                    _streamingMessageBuilder.Append("\n");
                }
            }

            _streamingMessageBuilder.Append(textChunk);
            string currentMessage = _streamingMessageBuilder.ToString();

            // 外出中はメッセージ欄を更新しない（raw表示は継続）
            if (!IsOutingActive && messageText != null)
            {
                messageText.text = currentMessage;
                _messageTimer = float.MaxValue;
            }

            if (llmRawResponseText != null && _streamingHeaderJson != null)
            {
                llmRawResponseText.text = GetRequestPrefix() + _streamingHeaderJson + "\n" + currentMessage;
            }
        }

        // ─────────────────────────────────────
        // 外出表示
        // ─────────────────────────────────────

        /// <summary>
        /// 外出状態の表示を開始（OutingController.EnterOutingから呼ばれる想定）
        /// </summary>
        public void ShowOutingDisplay()
        {
            string displayText = outingController != null ? outingController.OutingDisplayText : "お出かけ中…";
            if (messageText != null)
            {
                messageText.text = displayText;
                _messageTimer = float.MaxValue; // 消えない
            }
        }

        /// <summary>
        /// 外出状態の表示を解除
        /// </summary>
        public void ClearOutingDisplay()
        {
            if (messageText != null)
            {
                messageText.text = "";
                _messageTimer = 0;
            }
        }

        private void OnOutingMessageBlocked()
        {
            ShowMessage("外出中です");
        }

        // ─────────────────────────────────────
        // メッセージ表示
        // ─────────────────────────────────────

        private void ShowMessage(string message)
        {
            if (messageText != null)
            {
                messageText.text = message;
                _messageTimer = messageDisplayDuration > 0 ? messageDisplayDuration : float.MaxValue;
            }
        }

        // ─────────────────────────────────────
        // JSON処理
        // ─────────────────────────────────────

        /// <summary>
        /// JSONを処理してアニメーション・表情を適用
        /// </summary>
        public void ProcessJson(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                Debug.LogWarning("[UIController] Empty JSON input");
                return;
            }

            LLMResponseData response;
            try
            {
                response = LLMResponseData.FromJson(json);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[UIController] Failed to parse JSON: {e.Message}");
                return;
            }

            Debug.Log($"[UIController] Processing: action={response.action}, emote={response.emote}, message={response.message}");

            if (messageText != null && response.HasMessage)
            {
                messageText.text = response.FullMessage;
                _messageTimer = messageDisplayDuration;
            }

            if (characterController != null)
            {
                characterController.ProcessResponse(response);
            }
            else
            {
                if (animationController != null && response.HasEmote)
                {
                    ApplyAnimation($"emote_{response.emote}");
                }
                if (expressionController != null && response.emotion != null)
                {
                    expressionController.SetEmotion(response.emotion);
                }
            }
        }

        private void ApplyAnimation(string animationId)
        {
            AnimationStateType state = AnimationStateType.Idle;

            if (animationId.Contains("walk"))
            {
                state = AnimationStateType.Walk;
            }
            else if (animationId.Contains("run"))
            {
                state = AnimationStateType.Run;
            }
            else if (animationId.Contains("idle"))
            {
                state = AnimationStateType.Idle;
            }
            else if (animationId.Contains("talk"))
            {
                state = AnimationStateType.Talk;
            }
            else if (animationId.Contains("emote"))
            {
                state = AnimationStateType.Emote;
            }

            animationController.PlayState(state, animationId);
        }

        private string GetSampleJson()
        {
            return @"{
  ""emotion"": {
    ""happy"": 0.5,
    ""relaxed"": 0.0,
    ""angry"": 0.0,
    ""sad"": 0.0,
    ""surprised"": 0.0
  },
  ""reaction"": ""いいね!"",
  ""action"": ""ignore"",
  ""target"": { ""type"": ""talk"" },
  ""emote"": ""Neutral"",
  ""message"": ""こんにちは！""
}";
        }

        // ─────────────────────────────────────
        // UI非表示トグル
        // ─────────────────────────────────────

        private void OnUIHideToggleClicked()
        {
            if (uiContainer == null) return;
            bool newState = !uiContainer.activeSelf;
            uiContainer.SetActive(newState);
            UpdateUIHideToggleVisual(newState);
        }

        private void UpdateUIHideToggleVisual(bool isUIVisible)
        {
            if (uiShowIcon != null)
                uiShowIcon.gameObject.SetActive(isUIVisible);
            if (uiHideIcon != null)
                uiHideIcon.gameObject.SetActive(!isUIVisible);
        }

        // ─────────────────────────────────────
        // マイクボタン
        // ─────────────────────────────────────

        private void OnMicButtonClicked()
        {
            if (voiceInputController == null) return;

            voiceInputController.SetEnabled(!voiceInputController.IsEnabled);
        }

        private void OnVoiceInputEnabledChanged(bool isEnabled)
        {
            UpdateMicButtonVisual(isEnabled);
        }

        private void UpdateMicButtonVisual(bool isEnabled)
        {
            if (micOnIcon != null)
                micOnIcon.gameObject.SetActive(isEnabled);
            if (micOffIcon != null)
                micOffIcon.gameObject.SetActive(!isEnabled);
        }

        // ─────────────────────────────────────
        // TTSクレジット表示
        // ─────────────────────────────────────

        private void OnTTSCreditChanged(string creditText)
        {
            UpdateTTSCreditDisplay(creditText);
        }

        private void UpdateTTSCreditDisplay(string creditText)
        {
            if (ttsCreditText != null)
            {
                ttsCreditText.text = creditText ?? "OFF";
            }
        }
    }

    /// <summary>
    /// 入力モード
    /// </summary>
    public enum InputMode
    {
        Json,  // JSONデバッグモード
        Chat   // LLMチャットモード
    }
}
