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
        private const string PrefKey_UseVision = "llm_useVision";
        private const string PrefKey_MaxHistory = "llm_maxHistory";
        private const string PrefKey_CameraPreview = "llm_cameraPreview";
        private const string PrefKey_WebCam = "llm_webCam";
        private const string PrefKey_ScreenCapture = "llm_screenCapture";
        private const string PrefKey_IdleChatMessage = "idleChat_message";

        [Header("References")]
        public ChatManager chatManager;
        public WebCamDisplayController webCamDisplayController;
        public ScreenCaptureDisplayController screenCaptureDisplayController;
        public IdleChatController idleChatController;
        public CronScheduler cronScheduler;
        public SleepController sleepController;
        public OutingController outingController;
        public FirstRunController firstRunController;

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

        [Header("UI - IdleChat")]
        [Tooltip("自律リクエストON/OFF")]
        public Toggle idleChatToggle;

        [Tooltip("クールダウン秒数")]
        public TMP_InputField cooldownInputField;

        [Tooltip("自律リクエストメッセージ")]
        public TMP_InputField idleChatMessageInputField;

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

        [Header("UI - Screen Capture")]
        [Tooltip("画面キャプチャ表示切替")]
        public Toggle screenCaptureToggle;

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

        private void Awake()
        {
            // ドロップダウンオプション初期化（OnEnable()のLoadConfigToUI()より先に実行する必要がある）
            if (apiTypeDropdown != null)
            {
                apiTypeDropdown.ClearOptions();
                apiTypeDropdown.AddOptions(new System.Collections.Generic.List<string>
                {
                    "Ollama", "LM Studio", "Dify", "OpenAI", "Claude", "Gemini", "WebLLM (Browser)"
                });
            }

            // OnEnable()より先に保存済み設定を復元
            LoadSavedSettings();
        }

        private void OnEnable()
        {
            // パネルが表示されるたびに現在の設定を反映
            LoadConfigToUI();
            LoadIdleChatToUI();
            LoadCronSchedulerToUI();
            LoadSleepToUI();
            LoadOutingToUI();
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

            // IdleChat
            InitializeIdleChatSettings();

            // Cron Scheduler
            InitializeCronSchedulerToggle();

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
            if (idleChatToggle != null)
                idleChatToggle.onValueChanged.RemoveListener(OnIdleChatToggleChanged);
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
                apiTypeDropdown.value = (int)config.apiType;
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
            var newApiType = (LLMApiType)index;
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
                apiType = apiTypeDropdown != null ? (LLMApiType)apiTypeDropdown.value : LLMApiType.Ollama,
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

            // UseVision
            if (chatManager != null)
            {
                PlayerPrefs.SetInt(PrefKey_UseVision, chatManager.useVision ? 1 : 0);
            }

            // MaxHistory
            if (chatManager != null)
            {
                PlayerPrefs.SetInt(PrefKey_MaxHistory, chatManager.maxHistoryLength);
            }

            // CameraPreview
            if (cameraPreviewToggle != null)
            {
                PlayerPrefs.SetInt(PrefKey_CameraPreview, cameraPreviewToggle.isOn ? 1 : 0);
            }

            // WebCam
            if (webCamToggle != null)
            {
                PlayerPrefs.SetInt(PrefKey_WebCam, webCamToggle.isOn ? 1 : 0);
            }

            // Screen Capture
            if (screenCaptureToggle != null)
            {
                PlayerPrefs.SetInt(PrefKey_ScreenCapture, screenCaptureToggle.isOn ? 1 : 0);
            }

            // IdleChat Message
            if (idleChatMessageInputField != null && idleChatController != null)
            {
                PlayerPrefs.SetString(PrefKey_IdleChatMessage, idleChatMessageInputField.text);
            }

            PlayerPrefs.Save();
            SetStatus("Saved!");
            Debug.Log($"[LLMSettingsPanel] Settings saved: API={config.apiType}, Vision={chatManager?.useVision}, MaxHistory={chatManager?.maxHistoryLength}");

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
                apiType = apiTypeDropdown != null ? (LLMApiType)apiTypeDropdown.value : LLMApiType.Ollama,
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

        /// <summary>
        /// 保存された設定を復元（起動時）
        /// UseVision、MaxHistory、IdleChatMessage
        /// ※ WebCamとCameraPreviewは各コントローラーで自動復元
        /// </summary>
        private void LoadSavedSettings()
        {
            // UseVision
            if (PlayerPrefs.HasKey(PrefKey_UseVision) && chatManager != null)
            {
                chatManager.useVision = PlayerPrefs.GetInt(PrefKey_UseVision) == 1;
                Debug.Log($"[LLMSettingsPanel] Loaded saved useVision: {chatManager.useVision}");
            }

            // MaxHistory
            if (PlayerPrefs.HasKey(PrefKey_MaxHistory) && chatManager != null)
            {
                chatManager.maxHistoryLength = PlayerPrefs.GetInt(PrefKey_MaxHistory);
                Debug.Log($"[LLMSettingsPanel] Loaded saved maxHistory: {chatManager.maxHistoryLength}");
            }

            // IdleChat Message
            if (PlayerPrefs.HasKey(PrefKey_IdleChatMessage) && idleChatController != null)
            {
                string savedMessage = PlayerPrefs.GetString(PrefKey_IdleChatMessage);
                if (!string.IsNullOrEmpty(savedMessage))
                {
                    idleChatController.idlePromptMessage = savedMessage;
                    Debug.Log("[LLMSettingsPanel] Loaded saved idleChat message");
                }
            }
        }

        private void OnVisionToggleChanged(bool isOn)
        {
            if (chatManager != null)
            {
                chatManager.useVision = isOn;
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

            Debug.Log($"[LLMSettingsPanel] Camera preview: {(isOn ? "ON" : "OFF")}");
        }

        // ─────────────────────────────────────
        // Webカメラ
        // ─────────────────────────────────────

        private void InitializeWebCamToggle()
        {
            if (webCamToggle != null)
            {
                // LoadSavedSettings()でWebCamが起動されている場合、その状態を反映
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
        }

        private void OnScreenCaptureToggleChanged(bool isOn)
        {
            if (screenCaptureDisplayController == null)
            {
                Debug.LogWarning("[LLMSettingsPanel] ScreenCaptureDisplayController not available");
                return;
            }

            if (isOn)
                screenCaptureDisplayController.StartCapture();
            else
                screenCaptureDisplayController.StopCapture();

            Debug.Log($"[LLMSettingsPanel] ScreenCapture: {(isOn ? "ON" : "OFF")}");
        }

        // ─────────────────────────────────────
        // IdleChat設定
        // ─────────────────────────────────────

        private void InitializeIdleChatSettings()
        {
            if (idleChatToggle != null)
            {
                idleChatToggle.onValueChanged.AddListener(OnIdleChatToggleChanged);
            }

            if (cooldownInputField != null)
            {
                cooldownInputField.onEndEdit.AddListener(OnCooldownChanged);
            }
        }

        private void LoadIdleChatToUI()
        {
            if (idleChatToggle != null)
            {
                idleChatToggle.isOn = idleChatController != null && idleChatController.autoRequestEnabled;
            }

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

        private void OnIdleChatToggleChanged(bool isOn)
        {
            if (idleChatController == null) return;
            idleChatController.SetEnabled(isOn);
            Debug.Log($"[LLMSettingsPanel] IdleChat: {(isOn ? "ON" : "OFF")}");
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
