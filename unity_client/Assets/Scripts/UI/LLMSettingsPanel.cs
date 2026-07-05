using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using CyanNook.Chat;
using CyanNook.Character;
using CyanNook.Core;

namespace CyanNook.UI
{
    /// <summary>
    /// LLM設定パネル
    /// API設定、Vision、IdleChat、WebCam設定を管理
    /// </summary>
    public class LLMSettingsPanel : MonoBehaviour
    {
        /// <summary>
        /// LLM設定が正常に保存されたときに発火（初回起動時のEntry開始トリガー用）
        /// </summary>
        public event Action OnLLMConfigured;

        // PlayerPrefsキー
        private const string PrefKey_UseVision = SettingsKeys.UseVision;
        private const string PrefKey_MaxHistory = SettingsKeys.MaxHistory;
        private const string PrefKey_CameraPreview = SettingsKeys.CameraPreview;
        private const string PrefKey_WebCam = SettingsKeys.WebCam;
        private const string PrefKey_ScreenCapture = SettingsKeys.ScreenCapture;
        private const string PrefKey_IdleChatMessage = SettingsKeys.IdleChatMessage;

        [Header("References")]
        public ChatManager chatManager;
        public WebCamDisplayController webCamDisplayController;
        public ScreenCaptureDisplayController screenCaptureDisplayController;
        public IdleChatController idleChatController;
        public CronScheduler cronScheduler;
        public SleepController sleepController;
        public OutingController outingController;
        public FirstRunController firstRunController;
        public ExternalActionFeedController externalActionFeedController;

        [Header("UI - API Config")]
        [Tooltip("AI Service選択（Ollama, LM Studio, Dify, OpenAI）")]
        public TMP_Dropdown apiTypeDropdown;

        [Tooltip("エンドポイントURL入力")]
        public TMP_InputField endpointInputField;

        [Tooltip("APIキー入力（Dify/OpenAI用）")]
        public TMP_InputField apiKeyInputField;

        [Tooltip("モデル名入力")]
        public TMP_InputField modelNameInputField;

        [Header("UI - Generation Parameters")]
        [Tooltip("生成パラメータセクション（Dify時非表示）")]
        public GameObject generationParamsSection;

        [Tooltip("Temperature入力")]
        public TMP_InputField temperatureInputField;

        [Tooltip("Top P入力")]
        public TMP_InputField topPInputField;

        [Tooltip("Top K入力")]
        public TMP_InputField topKInputField;

        [Tooltip("最大応答トークン数入力")]
        public TMP_InputField numPredictInputField;

        [Tooltip("コンテキスト長入力")]
        public TMP_InputField numCtxInputField;

        [Tooltip("繰り返しペナルティ入力")]
        public TMP_InputField repeatPenaltyInputField;

        [Tooltip("Thinkingモードトグル")]
        public Toggle thinkToggle;

        [Header("UI - Conversation")]
        [Tooltip("会話履歴最大保持数")]
        public TMP_InputField maxHistoryInputField;

        [Header("UI - Vision")]
        [Tooltip("ビジョン機能ON/OFF")]
        public Toggle useVisionToggle;

        [Tooltip("カメラプレビュー表示用RawImage")]
        public RawImage cameraPreviewImage;

        [Tooltip("カメラプレビュー表示切替トグル")]
        public Toggle cameraPreviewToggle;

        [Header("UI - Periodic Execution（定期実行）")]
        [Tooltip("定期実行（IdleChat/SleepChat/Outing）マスターON/OFF")]
        public Toggle periodicToggle;

        [Tooltip("定期実行の詳細設定グループ（ON時のみ表示。IdleChat/Sleep/Outingセクションの親）")]
        public GameObject periodicDetailsGroup;

        [Header("UI - IdleChat")]
        [Tooltip("クールダウン秒数（0で個別無効）")]
        public TMP_InputField cooldownInputField;

        [Tooltip("自律リクエストメッセージ")]
        public TMP_InputField idleChatMessageInputField;

        [Header("UI - External Action Feed（外部アクションフィード）")]
        [Tooltip("外部アクションフィードON/OFF")]
        public Toggle feedToggle;

        [Tooltip("フィードの詳細設定グループ（ON時のみ表示）")]
        public GameObject feedDetailsGroup;

        [Tooltip("応答JSON購読URL")]
        public TMP_InputField feedActionUrlInputField;

        [Tooltip("購読間隔（秒、0で無効）")]
        public TMP_InputField feedIntervalInputField;

        [Tooltip("context JSON公開URL")]
        public TMP_InputField feedContextUrlInputField;

        [Tooltip("カメラ画像公開URL")]
        public TMP_InputField feedCameraUrlInputField;

        [Tooltip("公開間隔（秒、0で無効）")]
        public TMP_InputField feedPublishIntervalInputField;

        [Tooltip("解説ページを開くボタン")]
        public Button feedHelpButton;

        [Tooltip("解説ページのURL")]
        public string feedHelpUrl = "";

        [Header("UI - Cron Scheduler")]
        [Tooltip("CronスケジューラーON/OFF")]
        public Toggle cronSchedulerToggle;

        [Tooltip("Cronジョブ再読み込みボタン")]
        public Button cronReloadButton;

        [Tooltip("自動リロード間隔（分）。0=無効")]
        public TMP_InputField cronAutoReloadInputField;

        [Header("UI - Sleep")]
        [Tooltip("デフォルト睡眠時間（分）")]
        public TMP_InputField defaultSleepDurationInputField;

        [Tooltip("最小睡眠時間（分）")]
        public TMP_InputField minSleepDurationInputField;

        [Tooltip("最大睡眠時間（分）")]
        public TMP_InputField maxSleepDurationInputField;

        [Tooltip("夢メッセージ間隔（分）")]
        public TMP_InputField dreamIntervalInputField;

        [Tooltip("夢メッセージプロンプト")]
        public TMP_InputField dreamPromptInputField;

        [Tooltip("起床時システムメッセージ")]
        public TMP_InputField wakeUpMessageInputField;

        [Header("UI - Outing")]
        [Tooltip("外出中メッセージ間隔（分）")]
        public TMP_InputField outingIntervalInputField;

        [Tooltip("外出中プロンプトメッセージ")]
        public TMP_InputField outingPromptInputField;

        [Tooltip("入室時プロンプトメッセージ")]
        public TMP_InputField entryPromptInputField;

        [Header("UI - WebCam")]
        [Tooltip("Webカメラ表示切替")]
        public Toggle webCamToggle;

        [Header("UI - Screen Capture / Mobile Camera")]
        [Tooltip("画面キャプチャ/カメラ表示切替")]
        public Toggle screenCaptureToggle;

        [Tooltip("デスクトップ時に表示するラベル GameObject (LocalizeStringEvent でローカライズ可)")]
        public GameObject screenCaptureLabelDesktop;

        [Tooltip("モバイル時に表示するラベル GameObject (LocalizeStringEvent でローカライズ可)")]
        public GameObject screenCaptureLabelMobile;

        [Tooltip("画面キャプチャ/カメラプレビュー表示用RawImage")]
        public RawImage screenCapturePreviewImage;

        [Header("UI - Annotations")]
        [Tooltip("Dify選択時の注釈テキスト")]
        public TMP_Text difyAnnotationText;

        [Header("UI - Buttons")]
        [Tooltip("設定保存ボタン")]
        public Button saveButton;

        [Tooltip("接続テストボタン")]
        public Button testConnectionButton;

        [Header("UI - Status")]
        [Tooltip("ステータス表示")]
        public TMP_Text statusText;

        // 初期化フラグ（イベントハンドラの重複登録を防ぐ）
        private bool _isCameraPreviewInitialized = false;
        private Coroutine _cameraPreviewRetryCoroutine = null;

        // ドロップダウンindex ↔ LLMApiType の間接マッピング
        // UNITYROOM_BUILD では対応プロバイダのみ表示する
        private System.Collections.Generic.List<LLMApiType> _availableApiTypes;

        private void Awake()
        {
            // 利用可能プロバイダリストを構築
            _availableApiTypes = new System.Collections.Generic.List<LLMApiType>();
#if UNITYROOM_BUILD
            _availableApiTypes.Add(LLMApiType.Gemini);
            _availableApiTypes.Add(LLMApiType.WebLLM);
#else
            _availableApiTypes.Add(LLMApiType.Ollama);
            _availableApiTypes.Add(LLMApiType.LMStudio);
            _availableApiTypes.Add(LLMApiType.Dify);
            _availableApiTypes.Add(LLMApiType.OpenAI);
            _availableApiTypes.Add(LLMApiType.Claude);
            _availableApiTypes.Add(LLMApiType.Gemini);
            _availableApiTypes.Add(LLMApiType.WebLLM);
#endif

            // APIキー入力欄のマスク表示
            if (apiKeyInputField != null)
            {
                apiKeyInputField.contentType = TMP_InputField.ContentType.Password;
            }

            // ドロップダウンオプション初期化（OnEnable()のLoadConfigToUI()より先に実行する必要がある）
            if (apiTypeDropdown != null)
            {
                apiTypeDropdown.ClearOptions();
                var labels = new System.Collections.Generic.List<string>();
                foreach (var apiType in _availableApiTypes)
                {
                    labels.Add(GetApiTypeLabel(apiType));
                }
                apiTypeDropdown.AddOptions(labels);
            }

            // 保存済み設定の復元は各コントローラー側で行う
            // （Vision/MaxHistory=ChatManager.Awake、IdleChatメッセージ=IdleChatController、
            // カメラ系=各コントローラー。パネルの初期アクティブ状態に依存させないため）
        }

        /// <summary>
        /// ドロップダウンindexから対応するLLMApiTypeを取得
        /// </summary>
        private LLMApiType GetApiTypeFromDropdownIndex(int index)
        {
            if (_availableApiTypes != null && index >= 0 && index < _availableApiTypes.Count)
                return _availableApiTypes[index];
            return LLMApiType.Gemini; // フォールバック
        }

        /// <summary>
        /// LLMApiTypeからドロップダウンindexを取得（見つからなければ0）
        /// </summary>
        private int GetDropdownIndexFromApiType(LLMApiType apiType)
        {
            if (_availableApiTypes != null)
            {
                int idx = _availableApiTypes.IndexOf(apiType);
                if (idx >= 0) return idx;
            }
            return 0;
        }

        /// <summary>
        /// LLMApiType → ドロップダウン表示ラベル
        /// </summary>
        private static string GetApiTypeLabel(LLMApiType apiType)
        {
            switch (apiType)
            {
                case LLMApiType.Ollama: return "Ollama";
                case LLMApiType.LMStudio: return "LM Studio";
                case LLMApiType.Dify: return "Dify";
                case LLMApiType.OpenAI: return "OpenAI";
                case LLMApiType.Claude: return "Claude";
                case LLMApiType.Gemini: return "Gemini";
                case LLMApiType.WebLLM: return "WebLLM (Browser)";
                default: return apiType.ToString();
            }
        }

        private void OnEnable()
        {
            // パネルが表示されるたびに現在の設定を反映
            LoadConfigToUI();
            LoadPeriodicToUI();
            LoadIdleChatToUI();
            LoadCronSchedulerToUI();
            LoadSleepToUI();
            LoadOutingToUI();
            LoadFeedToUI();
            LoadVisionToUI();

            // カメラプレビューの初期化（パネル初回表示時にも実行）
            InitializeCameraPreview();
        }

        private void Start()
        {
            // APIタイプドロップダウンイベント登録
            if (apiTypeDropdown != null)
            {
                apiTypeDropdown.onValueChanged.AddListener(OnApiTypeChanged);
            }

            // 保存ボタン
            if (saveButton != null)
            {
                saveButton.onClick.AddListener(OnSaveClicked);
            }

            // 接続テストボタン
            if (testConnectionButton != null)
            {
                testConnectionButton.onClick.AddListener(OnTestConnectionClicked);
            }

            // カメラプレビュー（OnEnable()で初期化されるためStart()では不要）

            // WebCam
            InitializeWebCamToggle();

            // Screen Capture
            InitializeScreenCaptureToggle();

            // 定期実行マスター
            InitializePeriodicSettings();

            // IdleChat
            InitializeIdleChatSettings();

            // Cron Scheduler
            InitializeCronSchedulerToggle();

            // 外部アクションフィード
            InitializeFeedSettings();

            // Sleep
            InitializeSleepSettings();

            // Outing
            InitializeOutingSettings();

            // Vision
            InitializeVisionToggle();

            // MaxHistory
            if (maxHistoryInputField != null)
            {
                maxHistoryInputField.onEndEdit.AddListener(OnMaxHistoryChanged);
            }

            // IdleChatメッセージ
            if (idleChatMessageInputField != null)
            {
                idleChatMessageInputField.onEndEdit.AddListener(OnIdleChatMessageChanged);
            }

            // 全Awake()完了後にUIを再反映（LLMClient.Awake()の実行順に依存しないようにする）
            LoadConfigToUI();
        }

        private void OnDestroy()
        {
            // コルーチンをキャンセル
            if (_cameraPreviewRetryCoroutine != null)
            {
                StopCoroutine(_cameraPreviewRetryCoroutine);
                _cameraPreviewRetryCoroutine = null;
            }

            if (apiTypeDropdown != null)
                apiTypeDropdown.onValueChanged.RemoveListener(OnApiTypeChanged);
            if (cameraPreviewToggle != null)
                cameraPreviewToggle.onValueChanged.RemoveListener(OnCameraPreviewToggleChanged);
            if (webCamToggle != null)
                webCamToggle.onValueChanged.RemoveListener(OnWebCamToggleChanged);
            if (screenCaptureToggle != null)
                screenCaptureToggle.onValueChanged.RemoveListener(OnScreenCaptureToggleChanged);
            if (periodicToggle != null)
                periodicToggle.onValueChanged.RemoveListener(OnPeriodicToggleChanged);
            if (feedToggle != null)
                feedToggle.onValueChanged.RemoveListener(OnFeedToggleChanged);
            if (feedActionUrlInputField != null)
                feedActionUrlInputField.onEndEdit.RemoveListener(OnFeedActionUrlChanged);
            if (feedIntervalInputField != null)
                feedIntervalInputField.onEndEdit.RemoveListener(OnFeedIntervalChanged);
            if (feedContextUrlInputField != null)
                feedContextUrlInputField.onEndEdit.RemoveListener(OnFeedContextUrlChanged);
            if (feedCameraUrlInputField != null)
                feedCameraUrlInputField.onEndEdit.RemoveListener(OnFeedCameraUrlChanged);
            if (feedPublishIntervalInputField != null)
                feedPublishIntervalInputField.onEndEdit.RemoveListener(OnFeedPublishIntervalChanged);
            if (feedHelpButton != null)
                feedHelpButton.onClick.RemoveListener(OnFeedHelpClicked);
            if (cronSchedulerToggle != null)
                cronSchedulerToggle.onValueChanged.RemoveListener(OnCronSchedulerToggleChanged);
            if (cronReloadButton != null)
                cronReloadButton.onClick.RemoveListener(OnCronReloadClicked);
            if (cronAutoReloadInputField != null)
                cronAutoReloadInputField.onEndEdit.RemoveListener(OnCronAutoReloadChanged);
            if (cooldownInputField != null)
                cooldownInputField.onEndEdit.RemoveListener(OnCooldownChanged);
            if (useVisionToggle != null)
                useVisionToggle.onValueChanged.RemoveListener(OnVisionToggleChanged);
            if (maxHistoryInputField != null)
                maxHistoryInputField.onEndEdit.RemoveListener(OnMaxHistoryChanged);
            if (idleChatMessageInputField != null)
                idleChatMessageInputField.onEndEdit.RemoveListener(OnIdleChatMessageChanged);
            if (defaultSleepDurationInputField != null)
                defaultSleepDurationInputField.onEndEdit.RemoveListener(OnDefaultSleepDurationChanged);
            if (minSleepDurationInputField != null)
                minSleepDurationInputField.onEndEdit.RemoveListener(OnMinSleepDurationChanged);
            if (maxSleepDurationInputField != null)
                maxSleepDurationInputField.onEndEdit.RemoveListener(OnMaxSleepDurationChanged);
            if (dreamIntervalInputField != null)
                dreamIntervalInputField.onEndEdit.RemoveListener(OnDreamIntervalChanged);
            if (dreamPromptInputField != null)
                dreamPromptInputField.onEndEdit.RemoveListener(OnDreamPromptChanged);
            if (wakeUpMessageInputField != null)
                wakeUpMessageInputField.onEndEdit.RemoveListener(OnWakeUpMessageChanged);
            if (outingIntervalInputField != null)
                outingIntervalInputField.onEndEdit.RemoveListener(OnOutingIntervalChanged);
            if (outingPromptInputField != null)
                outingPromptInputField.onEndEdit.RemoveListener(OnOutingPromptChanged);
            if (entryPromptInputField != null)
                entryPromptInputField.onEndEdit.RemoveListener(OnEntryPromptChanged);
        }

        // ─────────────────────────────────────
        // API設定
        // ─────────────────────────────────────

        private void LoadConfigToUI()
        {
            var llmClient = chatManager?.llmClient;
            if (llmClient == null) return;

            var config = llmClient.CurrentConfig ?? LLMConfig.GetDefault();

            if (apiTypeDropdown != null)
                apiTypeDropdown.value = GetDropdownIndexFromApiType(config.apiType);
            if (endpointInputField != null)
                endpointInputField.text = config.apiEndpoint;
            if (modelNameInputField != null)
                modelNameInputField.text = config.modelName;
            if (apiKeyInputField != null)
                apiKeyInputField.text = config.apiKey ?? "";

            // 生成パラメータ
            if (temperatureInputField != null)
                temperatureInputField.text = config.temperature.ToString("F2");
            if (topPInputField != null)
                topPInputField.text = config.topP.ToString("F2");
            if (topKInputField != null)
                topKInputField.text = config.topK.ToString();
            if (numPredictInputField != null)
                numPredictInputField.text = config.numPredict.ToString();
            if (numCtxInputField != null)
                numCtxInputField.text = config.numCtx.ToString();
            if (repeatPenaltyInputField != null)
                repeatPenaltyInputField.text = config.repeatPenalty.ToString("F1");
            if (thinkToggle != null)
                thinkToggle.isOn = config.think;

            UpdateApiKeyVisibility(config.apiType);
            SetStatus("");
        }

        private void OnApiTypeChanged(int index)
        {
            // indexの直接キャストは不可（UNITYROOM_BUILDではドロップダウンの選択肢が
            // [Gemini, WebLLM] のみで、enum値とindexがずれる）
            var newApiType = GetApiTypeFromDropdownIndex(index);
            UpdateApiKeyVisibility(newApiType);

            // エンドポイントが別のAPIタイプのデフォルト値の場合、新しいデフォルトに自動切替
            if (endpointInputField != null)
            {
                string currentEndpoint = endpointInputField.text.Trim();
                bool isKnownDefault = false;

                // 現在の値が既知のデフォルトエンドポイントか確認
                foreach (LLMApiType type in Enum.GetValues(typeof(LLMApiType)))
                {
                    if (type != newApiType && currentEndpoint == LLMConfig.GetDefaultEndpoint(type))
                    {
                        isKnownDefault = true;
                        break;
                    }
                }

                // 空欄 or 別APIタイプのデフォルト → 新しいデフォルトに切替
                if (string.IsNullOrEmpty(currentEndpoint) || isKnownDefault)
                {
                    endpointInputField.text = LLMConfig.GetDefaultEndpoint(newApiType);
                }
            }
        }

        private void UpdateApiKeyVisibility(LLMApiType apiType)
        {
            bool isWebLLM = apiType == LLMApiType.WebLLM;
            bool isDify = apiType == LLMApiType.Dify;

            if (apiKeyInputField != null)
            {
                bool needsApiKey = apiType == LLMApiType.Dify || apiType == LLMApiType.OpenAI || apiType == LLMApiType.Claude || apiType == LLMApiType.Gemini;
                apiKeyInputField.gameObject.SetActive(needsApiKey);
            }

            // Dify注釈
            if (difyAnnotationText != null)
            {
                difyAnnotationText.gameObject.SetActive(isDify);
            }

            // WebLLM/Dify選択時はモデル名を非表示
            if (modelNameInputField != null)
            {
                modelNameInputField.gameObject.SetActive(!isDify && !isWebLLM);
            }

            // Dify選択時は生成パラメータセクションを非表示（WebLLMは表示する）
            if (generationParamsSection != null)
            {
                generationParamsSection.SetActive(!isDify);
            }

            // WebLLM選択時はエンドポイントを非表示
            if (endpointInputField != null)
            {
                endpointInputField.gameObject.SetActive(!isWebLLM);
            }
        }

        private void OnSaveClicked()
        {
            var llmClient = chatManager?.llmClient;
            if (llmClient == null)
            {
                SetStatus("Error: LLMClient not found");
                return;
            }

            // LLM API設定
            var defaults = LLMConfig.GetDefault();
            var config = new LLMConfig
            {
                apiType = apiTypeDropdown != null ? GetApiTypeFromDropdownIndex(apiTypeDropdown.value) : LLMApiType.Gemini,
                apiEndpoint = endpointInputField != null ? endpointInputField.text : "",
                modelName = modelNameInputField != null ? modelNameInputField.text : "",
                apiKey = apiKeyInputField != null ? apiKeyInputField.text : "",
                temperature = ParseFloat(temperatureInputField, llmClient.CurrentConfig?.temperature ?? defaults.temperature),
                topP = ParseFloat(topPInputField, llmClient.CurrentConfig?.topP ?? defaults.topP),
                topK = ParseInt(topKInputField, llmClient.CurrentConfig?.topK ?? defaults.topK),
                numPredict = ParseInt(numPredictInputField, llmClient.CurrentConfig?.numPredict ?? defaults.numPredict),
                numCtx = ParseInt(numCtxInputField, llmClient.CurrentConfig?.numCtx ?? defaults.numCtx),
                repeatPenalty = ParseFloat(repeatPenaltyInputField, llmClient.CurrentConfig?.repeatPenalty ?? defaults.repeatPenalty),
                think = thinkToggle != null ? thinkToggle.isOn : (llmClient.CurrentConfig?.think ?? false),
                timeout = llmClient.CurrentConfig?.timeout ?? defaults.timeout
            };

            // WebLLM選択時はモデルIDとエンドポイントを自動設定
            if (config.apiType == LLMApiType.WebLLM)
            {
                config.modelName = WebLLMProvider.DefaultModelId;
                config.apiEndpoint = "";
            }

            if (!config.IsValid())
            {
                SetStatus("Error: Endpoint and Model are required");
                return;
            }

            llmClient.SaveAndApplyConfig(config);

            // SaveボタンはLLM API設定 (llm_config) の検証+適用+保存専用。
            // Vision/カメラ系トグル/MaxHistory/IdleChatメッセージ等の他の項目は
            // 変更した瞬間に各ハンドラ/コントローラー側で即保存される
            SetStatus("Saved!");
            Debug.Log($"[LLMSettingsPanel] LLM config saved: API={config.apiType}");

            // WebLLM選択時: モデル未ロードならダウンロードフローを開始
            if (config.apiType == LLMApiType.WebLLM && firstRunController != null
                && firstRunController.webLLMBridge != null && !firstRunController.webLLMBridge.IsModelLoaded)
            {
                Debug.Log("[LLMSettingsPanel] WebLLM selected but model not loaded, showing download UI");
                firstRunController.ShowDownloadOnly(() =>
                {
                    Debug.Log("[LLMSettingsPanel] WebLLM model download complete");
                    OnLLMConfigured?.Invoke();
                });
                return;
            }

            OnLLMConfigured?.Invoke();
        }

        private void OnTestConnectionClicked()
        {
            var llmClient = chatManager?.llmClient;
            if (llmClient == null)
            {
                SetStatus("Error: LLMClient not found");
                return;
            }

            SetStatus("Testing...");

            // テスト前にUIの値を一時的に適用
            var testDefaults = LLMConfig.GetDefault();
            llmClient.ApplyConfig(new LLMConfig
            {
                apiType = apiTypeDropdown != null ? GetApiTypeFromDropdownIndex(apiTypeDropdown.value) : LLMApiType.Gemini,
                apiEndpoint = endpointInputField != null ? endpointInputField.text : "",
                modelName = modelNameInputField != null ? modelNameInputField.text : "",
                apiKey = apiKeyInputField != null ? apiKeyInputField.text : "",
                temperature = ParseFloat(temperatureInputField, llmClient.CurrentConfig?.temperature ?? testDefaults.temperature),
                topP = ParseFloat(topPInputField, llmClient.CurrentConfig?.topP ?? testDefaults.topP),
                topK = ParseInt(topKInputField, llmClient.CurrentConfig?.topK ?? testDefaults.topK),
                numPredict = ParseInt(numPredictInputField, llmClient.CurrentConfig?.numPredict ?? testDefaults.numPredict),
                numCtx = ParseInt(numCtxInputField, llmClient.CurrentConfig?.numCtx ?? testDefaults.numCtx),
                repeatPenalty = ParseFloat(repeatPenaltyInputField, llmClient.CurrentConfig?.repeatPenalty ?? testDefaults.repeatPenalty),
                think = thinkToggle != null ? thinkToggle.isOn : (llmClient.CurrentConfig?.think ?? false),
                timeout = llmClient.CurrentConfig?.timeout ?? testDefaults.timeout
            });

            llmClient.TestConnection((success, message) =>
            {
                SetStatus(success ? "OK: Connected!" : $"Failed: {message}");
            });
        }

        // ─────────────────────────────────────
        // Max History
        // ─────────────────────────────────────

        private void OnMaxHistoryChanged(string value)
        {
            if (chatManager == null) return;

            if (int.TryParse(value, out int count) && count > 0)
            {
                chatManager.maxHistoryLength = count;
                PlayerPrefs.SetInt(PrefKey_MaxHistory, count);
                PlayerPrefs.Save();
                Debug.Log($"[LLMSettingsPanel] Max history: {count}");
            }
        }

        // ─────────────────────────────────────
        // Vision
        // ─────────────────────────────────────

        private void InitializeVisionToggle()
        {
            if (useVisionToggle != null)
            {
                useVisionToggle.onValueChanged.AddListener(OnVisionToggleChanged);
            }
        }

        private void LoadVisionToUI()
        {
            if (useVisionToggle != null && chatManager != null)
            {
                useVisionToggle.isOn = chatManager.useVision;
            }

            if (maxHistoryInputField != null && chatManager != null)
            {
                maxHistoryInputField.text = chatManager.maxHistoryLength.ToString();
            }
        }

        private void OnVisionToggleChanged(bool isOn)
        {
            if (chatManager != null)
            {
                chatManager.useVision = isOn;
                PlayerPrefs.SetInt(PrefKey_UseVision, isOn ? 1 : 0);
                PlayerPrefs.Save();
                Debug.Log($"[LLMSettingsPanel] Vision: {(isOn ? "ON" : "OFF")}");
            }
        }

        // ─────────────────────────────────────
        // カメラプレビュー
        // ─────────────────────────────────────

        private void InitializeCameraPreview()
        {
            if (cameraPreviewToggle != null)
            {
                // CharacterCameraControllerが既に設定を復元しているので、その状態をUIに反映
                var cameraCtrl = chatManager?.cameraController;
                if (cameraCtrl != null)
                {
                    cameraPreviewToggle.isOn = cameraCtrl.alwaysRender;

                    // プレビュー表示が必要な場合、RenderTextureを設定
                    if (cameraCtrl.alwaysRender && cameraPreviewImage != null)
                    {
                        var rt = cameraCtrl.GetRenderTexture();
                        if (rt != null)
                        {
                            cameraPreviewImage.texture = rt;
                            cameraPreviewImage.gameObject.SetActive(true);

                            // 再試行コルーチンがあればキャンセル
                            if (_cameraPreviewRetryCoroutine != null)
                            {
                                StopCoroutine(_cameraPreviewRetryCoroutine);
                                _cameraPreviewRetryCoroutine = null;
                            }
                        }
                        else
                        {
                            // RenderTextureがまだ準備できていない（VRM読み込み中など）
                            // → 少し待ってから再試行
                            cameraPreviewImage.gameObject.SetActive(false);

                            if (_cameraPreviewRetryCoroutine == null)
                            {
                                _cameraPreviewRetryCoroutine = StartCoroutine(RetryCameraPreview());
                            }
                        }
                    }
                    else if (cameraPreviewImage != null)
                    {
                        cameraPreviewImage.gameObject.SetActive(false);
                    }
                }
                else
                {
                    cameraPreviewToggle.isOn = false;
                    if (cameraPreviewImage != null)
                    {
                        cameraPreviewImage.gameObject.SetActive(false);
                    }
                }

                // カメラプレビューOFF時は共有画面プレビューも非表示
                if (screenCapturePreviewImage != null)
                {
                    if (cameraCtrl != null && cameraCtrl.alwaysRender)
                    {
                        UpdateScreenCapturePreview();
                    }
                    else
                    {
                        screenCapturePreviewImage.gameObject.SetActive(false);
                    }
                }

                // イベントハンドラは初回のみ登録
                if (!_isCameraPreviewInitialized)
                {
                    cameraPreviewToggle.onValueChanged.AddListener(OnCameraPreviewToggleChanged);
                    _isCameraPreviewInitialized = true;
                }
            }
        }

        /// <summary>
        /// カメラプレビューの再試行（VRM読み込み待ち）
        /// </summary>
        private System.Collections.IEnumerator RetryCameraPreview()
        {
            int maxRetries = 10;
            int retryCount = 0;
            float retryInterval = 0.5f;

            while (retryCount < maxRetries)
            {
                yield return new WaitForSeconds(retryInterval);
                retryCount++;

                var cameraCtrl = chatManager?.cameraController;
                if (cameraCtrl != null && cameraCtrl.alwaysRender && cameraPreviewImage != null)
                {
                    var rt = cameraCtrl.GetRenderTexture();
                    if (rt != null)
                    {
                        cameraPreviewImage.texture = rt;
                        cameraPreviewImage.gameObject.SetActive(true);
                        Debug.Log("[LLMSettingsPanel] Camera preview initialized successfully after retry");
                        _cameraPreviewRetryCoroutine = null;
                        yield break;
                    }
                }
            }

            Debug.LogWarning("[LLMSettingsPanel] Camera preview initialization failed after retries");
            _cameraPreviewRetryCoroutine = null;
        }

        private void OnCameraPreviewToggleChanged(bool isOn)
        {
            var cameraCtrl = chatManager?.cameraController;
            if (cameraCtrl == null)
            {
                Debug.LogWarning("[LLMSettingsPanel] CharacterCameraController not available");
                return;
            }

            cameraCtrl.SetAlwaysRender(isOn);

            if (cameraPreviewImage != null)
            {
                if (isOn)
                {
                    var rt = cameraCtrl.GetRenderTexture();
                    if (rt != null)
                    {
                        cameraPreviewImage.texture = rt;
                        cameraPreviewImage.gameObject.SetActive(true);
                    }
                }
                else
                {
                    cameraPreviewImage.gameObject.SetActive(false);
                }
            }

            // 共有画面プレビューもカメラプレビュートグルに連動
            if (screenCapturePreviewImage != null)
            {
                if (isOn)
                {
                    UpdateScreenCapturePreview();
                }
                else
                {
                    screenCapturePreviewImage.gameObject.SetActive(false);
                }
            }

            Debug.Log($"[LLMSettingsPanel] Camera preview: {(isOn ? "ON" : "OFF")}");
        }

        // ─────────────────────────────────────
        // Webカメラ
        // ─────────────────────────────────────

        private void InitializeWebCamToggle()
        {
            if (webCamToggle != null)
            {
                // WebCamDisplayControllerが起動時に自動復元している場合、その状態を反映
                webCamToggle.isOn = webCamDisplayController != null && webCamDisplayController.IsPlaying;
                webCamToggle.onValueChanged.AddListener(OnWebCamToggleChanged);
            }
        }

        private void OnWebCamToggleChanged(bool isOn)
        {
            if (webCamDisplayController == null)
            {
                Debug.LogWarning("[LLMSettingsPanel] WebCamDisplayController not available");
                return;
            }

            if (isOn)
                webCamDisplayController.StartWebCam();
            else
                webCamDisplayController.StopWebCam();

            // 動作は即反映されるため保存も即時に行う
            // （Saveボタン待ちだと「動いている=保存された」という認識とズレる）
            PlayerPrefs.SetInt(PrefKey_WebCam, isOn ? 1 : 0);
            PlayerPrefs.Save();

            Debug.Log($"[LLMSettingsPanel] WebCam: {(isOn ? "ON" : "OFF")}");
        }

        // ─────────────────────────────────────
        // 画面キャプチャ
        // ─────────────────────────────────────

        private void InitializeScreenCaptureToggle()
        {
            if (screenCaptureToggle != null)
            {
                screenCaptureToggle.isOn = screenCaptureDisplayController != null && screenCaptureDisplayController.IsPlaying;
                screenCaptureToggle.onValueChanged.AddListener(OnScreenCaptureToggleChanged);
            }

            // モバイル/デスクトップでラベル GameObject を切替（LocalizeStringEvent を維持するため）
            bool isMobile = screenCaptureDisplayController != null && screenCaptureDisplayController.IsMobileMode;
            if (screenCaptureLabelDesktop != null)
                screenCaptureLabelDesktop.SetActive(!isMobile);
            if (screenCaptureLabelMobile != null)
                screenCaptureLabelMobile.SetActive(isMobile);

            // 既にキャプチャ中の場合はプレビューを表示
            UpdateScreenCapturePreview();
        }

        private void OnScreenCaptureToggleChanged(bool isOn)
        {
            if (screenCaptureDisplayController == null)
            {
                Debug.LogWarning("[LLMSettingsPanel] ScreenCaptureDisplayController not available");
                return;
            }

            if (isOn)
            {
                screenCaptureDisplayController.StartCapture();
                // キャプチャ開始は非同期（ブラウザダイアログ後）なのでリトライでプレビュー表示
                if (_screenCapturePreviewRetryCoroutine == null)
                {
                    _screenCapturePreviewRetryCoroutine = StartCoroutine(RetryScreenCapturePreview());
                }
            }
            else
            {
                screenCaptureDisplayController.StopCapture();
                UpdateScreenCapturePreview();
            }

            // 動作は即反映されるため保存も即時に行う
            PlayerPrefs.SetInt(PrefKey_ScreenCapture, isOn ? 1 : 0);
            PlayerPrefs.Save();

            Debug.Log($"[LLMSettingsPanel] ScreenCapture: {(isOn ? "ON" : "OFF")}");
        }

        private Coroutine _screenCapturePreviewRetryCoroutine;

        private void UpdateScreenCapturePreview()
        {
            if (screenCapturePreviewImage == null) return;

            if (screenCaptureDisplayController != null && screenCaptureDisplayController.IsPlaying)
            {
                var tex = screenCaptureDisplayController.GetPreviewTexture();
                if (tex != null)
                {
                    screenCapturePreviewImage.texture = tex;
                    // PC: ブラウザCanvasのY軸反転を補正。モバイル: WebCamTextureは反転不要
                    screenCapturePreviewImage.uvRect = screenCaptureDisplayController.IsMobileMode
                        ? new Rect(0, 0, 1, 1)
                        : new Rect(0, 1, 1, -1);
                    screenCapturePreviewImage.gameObject.SetActive(true);
                    return;
                }
            }
            screenCapturePreviewImage.gameObject.SetActive(false);
        }

        private System.Collections.IEnumerator RetryScreenCapturePreview()
        {
            int maxRetries = 20;
            int retryCount = 0;

            while (retryCount < maxRetries)
            {
                yield return new WaitForSeconds(0.5f);
                retryCount++;

                if (screenCaptureDisplayController != null && screenCaptureDisplayController.IsPlaying)
                {
                    var tex = screenCaptureDisplayController.GetPreviewTexture();
                    if (tex != null && screenCapturePreviewImage != null)
                    {
                        screenCapturePreviewImage.texture = tex;
                        screenCapturePreviewImage.uvRect = screenCaptureDisplayController.IsMobileMode
                            ? new Rect(0, 0, 1, 1)
                            : new Rect(0, 1, 1, -1);
                        screenCapturePreviewImage.gameObject.SetActive(true);
                        Debug.Log("[LLMSettingsPanel] Screen capture preview initialized");
                        _screenCapturePreviewRetryCoroutine = null;
                        yield break;
                    }
                }
                else if (screenCaptureDisplayController == null || !screenCaptureDisplayController.IsPlaying)
                {
                    // キャプチャが中断された場合はリトライ終了
                    break;
                }
            }

            _screenCapturePreviewRetryCoroutine = null;
        }

        // ─────────────────────────────────────
        // IdleChat設定
        // ─────────────────────────────────────

        // ─────────────────────────────────────
        // 定期実行マスター（IdleChat/SleepChat/Outing 一括ON/OFF）
        // ─────────────────────────────────────

        private void InitializePeriodicSettings()
        {
            if (periodicToggle != null)
            {
                periodicToggle.onValueChanged.AddListener(OnPeriodicToggleChanged);
            }
        }

        private void LoadPeriodicToUI()
        {
            bool enabled = PeriodicExecutionSettings.IsEnabled();

            if (periodicToggle != null)
            {
                // 表示への反映のみ（onValueChangedを発火させない）
                periodicToggle.SetIsOnWithoutNotify(enabled);
            }

            // トグルが未割当でも詳細グループの表示状態は同期する
            if (periodicDetailsGroup != null)
            {
                periodicDetailsGroup.SetActive(enabled);
            }
        }

        private void OnPeriodicToggleChanged(bool isOn)
        {
            // 保存（共有マスターキー）
            PeriodicExecutionSettings.SetEnabled(isOn);

            // 参照未割当のコントローラーには反映されない（再起動時のLoadSettingsで反映される）
            if (idleChatController == null || sleepController == null || outingController == null)
            {
                Debug.LogWarning("[LLMSettingsPanel] Periodic toggle: unassigned controller reference(s), " +
                    "change applies to them after restart only");
            }

            // 3コントローラーへランタイム反映
            if (idleChatController != null)
            {
                idleChatController.SetEnabled(isOn);
            }
            sleepController?.SetPeriodicEnabled(isOn);
            outingController?.SetPeriodicEnabled(isOn);

            // 詳細設定グループの表示切替
            if (periodicDetailsGroup != null)
            {
                periodicDetailsGroup.SetActive(isOn);
            }

            Debug.Log($"[LLMSettingsPanel] Periodic execution: {(isOn ? "ON" : "OFF")}");
        }

        // ─────────────────────────────────────
        // IdleChat設定
        // ─────────────────────────────────────

        private void InitializeIdleChatSettings()
        {
            if (cooldownInputField != null)
            {
                cooldownInputField.onEndEdit.AddListener(OnCooldownChanged);
            }
        }

        private void LoadIdleChatToUI()
        {
            if (cooldownInputField != null)
            {
                cooldownInputField.text = idleChatController != null
                    ? idleChatController.cooldownDuration.ToString("F0") : "10";
            }

            if (idleChatMessageInputField != null && idleChatController != null)
            {
                idleChatMessageInputField.text = idleChatController.idlePromptMessage;
            }
        }

        private void OnCooldownChanged(string value)
        {
            if (idleChatController == null) return;
            if (float.TryParse(value, out float seconds))
            {
                idleChatController.SetCooldownDuration(seconds);
                Debug.Log($"[LLMSettingsPanel] IdleChat cooldown: {seconds}s");
            }
        }

        private void OnIdleChatMessageChanged(string value)
        {
            if (idleChatController == null) return;
            idleChatController.SetIdlePromptMessage(value);
            Debug.Log("[LLMSettingsPanel] IdleChat message updated");
        }

        // ─────────────────────────────────────
        // Cron Scheduler
        // ─────────────────────────────────────

        private void InitializeCronSchedulerToggle()
        {
            if (cronSchedulerToggle != null)
            {
                cronSchedulerToggle.onValueChanged.AddListener(OnCronSchedulerToggleChanged);
            }
            if (cronReloadButton != null)
            {
                cronReloadButton.onClick.AddListener(OnCronReloadClicked);
            }
            if (cronAutoReloadInputField != null)
            {
                cronAutoReloadInputField.onEndEdit.AddListener(OnCronAutoReloadChanged);
            }
        }

        private void LoadCronSchedulerToUI()
        {
            if (cronSchedulerToggle != null)
            {
                cronSchedulerToggle.isOn = cronScheduler != null && cronScheduler.schedulerEnabled;
            }
            if (cronAutoReloadInputField != null && cronScheduler != null)
            {
                cronAutoReloadInputField.text = cronScheduler.autoReloadInterval.ToString("F0");
            }
        }

        private void OnCronSchedulerToggleChanged(bool isOn)
        {
            if (cronScheduler == null) return;
            cronScheduler.SetEnabled(isOn);
            Debug.Log($"[LLMSettingsPanel] CronScheduler: {(isOn ? "ON" : "OFF")}");
        }

        private void OnCronReloadClicked()
        {
            if (cronScheduler == null) return;
            cronScheduler.Reload();
            Debug.Log("[LLMSettingsPanel] CronScheduler: Reload requested");
        }

        private void OnCronAutoReloadChanged(string value)
        {
            if (cronScheduler == null) return;
            if (float.TryParse(value, out float minutes))
            {
                cronScheduler.SetAutoReloadInterval(minutes);
                Debug.Log($"[LLMSettingsPanel] CronScheduler auto-reload: {(minutes > 0f ? $"{minutes}min" : "OFF")}");
            }
        }

        // ─────────────────────────────────────
        // 外部アクションフィード（受動制御・上級者向け）
        // ─────────────────────────────────────

        private void InitializeFeedSettings()
        {
            if (feedToggle != null)
            {
                feedToggle.onValueChanged.AddListener(OnFeedToggleChanged);
            }
            if (feedActionUrlInputField != null)
            {
                feedActionUrlInputField.onEndEdit.AddListener(OnFeedActionUrlChanged);
            }
            if (feedIntervalInputField != null)
            {
                feedIntervalInputField.onEndEdit.AddListener(OnFeedIntervalChanged);
            }
            if (feedContextUrlInputField != null)
            {
                feedContextUrlInputField.onEndEdit.AddListener(OnFeedContextUrlChanged);
            }
            if (feedCameraUrlInputField != null)
            {
                feedCameraUrlInputField.onEndEdit.AddListener(OnFeedCameraUrlChanged);
            }
            if (feedPublishIntervalInputField != null)
            {
                feedPublishIntervalInputField.onEndEdit.AddListener(OnFeedPublishIntervalChanged);
            }
            if (feedHelpButton != null)
            {
                feedHelpButton.onClick.AddListener(OnFeedHelpClicked);
            }
        }

        private void LoadFeedToUI()
        {
            bool enabled = externalActionFeedController != null && externalActionFeedController.feedEnabled;

            if (feedToggle != null)
            {
                // 表示への反映のみ（onValueChangedを発火させない）
                feedToggle.SetIsOnWithoutNotify(enabled);
            }

            if (feedDetailsGroup != null)
            {
                feedDetailsGroup.SetActive(enabled);
            }

            if (feedActionUrlInputField != null && externalActionFeedController != null)
            {
                feedActionUrlInputField.text = externalActionFeedController.actionSubscribeUrl;
            }

            if (feedIntervalInputField != null && externalActionFeedController != null)
            {
                feedIntervalInputField.text = externalActionFeedController.subscribeInterval.ToString("F0");
            }

            if (feedContextUrlInputField != null && externalActionFeedController != null)
            {
                feedContextUrlInputField.text = externalActionFeedController.contextPublishUrl;
            }

            if (feedCameraUrlInputField != null && externalActionFeedController != null)
            {
                feedCameraUrlInputField.text = externalActionFeedController.cameraPublishUrl;
            }

            if (feedPublishIntervalInputField != null && externalActionFeedController != null)
            {
                feedPublishIntervalInputField.text = externalActionFeedController.publishInterval.ToString("F0");
            }
        }

        private void OnFeedToggleChanged(bool isOn)
        {
            if (externalActionFeedController != null)
            {
                externalActionFeedController.SetEnabled(isOn);
            }

            if (feedDetailsGroup != null)
            {
                feedDetailsGroup.SetActive(isOn);
            }

            Debug.Log($"[LLMSettingsPanel] External action feed: {(isOn ? "ON" : "OFF")}");
        }

        private void OnFeedActionUrlChanged(string value)
        {
            if (externalActionFeedController == null) return;
            externalActionFeedController.SetActionSubscribeUrl(value);
            Debug.Log("[LLMSettingsPanel] Feed action URL updated");
        }

        private void OnFeedIntervalChanged(string value)
        {
            if (externalActionFeedController == null) return;
            if (float.TryParse(value, out float seconds))
            {
                externalActionFeedController.SetSubscribeInterval(seconds);
                Debug.Log($"[LLMSettingsPanel] Feed subscribe interval: {(seconds > 0f ? $"{seconds}s" : "OFF")}");
            }
        }

        private void OnFeedContextUrlChanged(string value)
        {
            if (externalActionFeedController == null) return;
            externalActionFeedController.SetContextPublishUrl(value);
            Debug.Log("[LLMSettingsPanel] Feed context URL updated");
        }

        private void OnFeedCameraUrlChanged(string value)
        {
            if (externalActionFeedController == null) return;
            externalActionFeedController.SetCameraPublishUrl(value);
            Debug.Log("[LLMSettingsPanel] Feed camera URL updated");
        }

        private void OnFeedPublishIntervalChanged(string value)
        {
            if (externalActionFeedController == null) return;
            if (float.TryParse(value, out float seconds))
            {
                externalActionFeedController.SetPublishInterval(seconds);
                Debug.Log($"[LLMSettingsPanel] Feed publish interval: {(seconds > 0f ? $"{seconds}s" : "OFF")}");
            }
        }

        private void OnFeedHelpClicked()
        {
            if (string.IsNullOrEmpty(feedHelpUrl)) return;
            Application.OpenURL(feedHelpUrl);
        }

        // ─────────────────────────────────────
        // Sleep設定
        // ─────────────────────────────────────

        private void InitializeSleepSettings()
        {
            if (defaultSleepDurationInputField != null)
                defaultSleepDurationInputField.onEndEdit.AddListener(OnDefaultSleepDurationChanged);
            if (minSleepDurationInputField != null)
                minSleepDurationInputField.onEndEdit.AddListener(OnMinSleepDurationChanged);
            if (maxSleepDurationInputField != null)
                maxSleepDurationInputField.onEndEdit.AddListener(OnMaxSleepDurationChanged);
            if (dreamIntervalInputField != null)
                dreamIntervalInputField.onEndEdit.AddListener(OnDreamIntervalChanged);
            if (dreamPromptInputField != null)
                dreamPromptInputField.onEndEdit.AddListener(OnDreamPromptChanged);
            if (wakeUpMessageInputField != null)
                wakeUpMessageInputField.onEndEdit.AddListener(OnWakeUpMessageChanged);
        }

        private void LoadSleepToUI()
        {
            if (sleepController == null) return;

            if (defaultSleepDurationInputField != null)
                defaultSleepDurationInputField.text = sleepController.defaultSleepDuration.ToString();
            if (minSleepDurationInputField != null)
                minSleepDurationInputField.text = sleepController.minSleepDuration.ToString();
            if (maxSleepDurationInputField != null)
                maxSleepDurationInputField.text = sleepController.maxSleepDuration.ToString();
            if (dreamIntervalInputField != null)
                dreamIntervalInputField.text = sleepController.dreamInterval.ToString("F0");
            if (dreamPromptInputField != null)
                dreamPromptInputField.text = sleepController.dreamPromptMessage;
            if (wakeUpMessageInputField != null)
                wakeUpMessageInputField.text = sleepController.wakeUpSystemMessage;
        }

        private void OnDefaultSleepDurationChanged(string value)
        {
            if (sleepController == null) return;
            if (int.TryParse(value, out int minutes))
            {
                sleepController.SetDefaultSleepDuration(minutes);
                Debug.Log($"[LLMSettingsPanel] Sleep default duration: {minutes}min");
            }
        }

        private void OnMinSleepDurationChanged(string value)
        {
            if (sleepController == null) return;
            if (int.TryParse(value, out int minutes))
            {
                sleepController.SetMinSleepDuration(minutes);
                Debug.Log($"[LLMSettingsPanel] Sleep min duration: {minutes}min");
            }
        }

        private void OnMaxSleepDurationChanged(string value)
        {
            if (sleepController == null) return;
            if (int.TryParse(value, out int minutes))
            {
                sleepController.SetMaxSleepDuration(minutes);
                Debug.Log($"[LLMSettingsPanel] Sleep max duration: {minutes}min");
            }
        }

        private void OnDreamIntervalChanged(string value)
        {
            if (sleepController == null) return;
            if (float.TryParse(value, out float minutes))
            {
                sleepController.SetDreamInterval(minutes);
                Debug.Log($"[LLMSettingsPanel] Sleep dream interval: {minutes}min");
            }
        }

        private void OnDreamPromptChanged(string value)
        {
            if (sleepController == null) return;
            sleepController.SetDreamPromptMessage(value);
            Debug.Log("[LLMSettingsPanel] Sleep dream prompt updated");
        }

        private void OnWakeUpMessageChanged(string value)
        {
            if (sleepController == null) return;
            sleepController.SetWakeUpSystemMessage(value);
            Debug.Log("[LLMSettingsPanel] Sleep wake-up message updated");
        }

        // ─────────────────────────────────────
        // Outing設定
        // ─────────────────────────────────────

        private void InitializeOutingSettings()
        {
            if (outingIntervalInputField != null)
                outingIntervalInputField.onEndEdit.AddListener(OnOutingIntervalChanged);
            if (outingPromptInputField != null)
                outingPromptInputField.onEndEdit.AddListener(OnOutingPromptChanged);
            if (entryPromptInputField != null)
                entryPromptInputField.onEndEdit.AddListener(OnEntryPromptChanged);
        }

        private void LoadOutingToUI()
        {
            if (outingController == null) return;

            if (outingIntervalInputField != null)
                outingIntervalInputField.text = outingController.outingMessageInterval.ToString("F0");
            if (outingPromptInputField != null)
                outingPromptInputField.text = outingController.outingPromptMessage;
            if (entryPromptInputField != null)
                entryPromptInputField.text = outingController.entryPromptMessage;
        }

        private void OnOutingIntervalChanged(string value)
        {
            if (outingController == null) return;
            if (float.TryParse(value, out float minutes))
            {
                outingController.SetOutingMessageInterval(minutes);
                Debug.Log($"[LLMSettingsPanel] Outing interval: {minutes}min");
            }
        }

        private void OnOutingPromptChanged(string value)
        {
            if (outingController == null) return;
            outingController.SetOutingPromptMessage(value);
            Debug.Log("[LLMSettingsPanel] Outing prompt updated");
        }

        private void OnEntryPromptChanged(string value)
        {
            if (outingController == null) return;
            outingController.SetEntryPromptMessage(value);
            Debug.Log("[LLMSettingsPanel] Entry prompt updated");
        }

        // ─────────────────────────────────────
        // ステータス表示
        // ─────────────────────────────────────

        private void SetStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }
        }

        // ─────────────────────────────────────
        // ヘルパー
        // ─────────────────────────────────────

        private static float ParseFloat(TMP_InputField field, float fallback)
        {
            if (field != null && float.TryParse(field.text, out float value))
                return value;
            return fallback;
        }

        private static int ParseInt(TMP_InputField field, int fallback)
        {
            if (field != null && int.TryParse(field.text, out int value))
                return value;
            return fallback;
        }
    }
}
