using UnityEngine;
using UnityEngine.UI;
using TMPro;
using CyanNook.Chat;
using System;

namespace CyanNook.UI
{
    /// <summary>
    /// 初回起動時のポップアップUI + シーンライト制御
    /// LLM未設定状態で表示され、WebLLM利用またはスキップを選択させる。
    /// ライトをOFFにして暗い部屋を演出し、選択完了後にONに戻す。
    /// </summary>
    public class FirstRunController : MonoBehaviour
    {
        [Header("Popup UI")]
        [Tooltip("ポップアップパネル全体（デフォルト非表示）")]
        public GameObject popupPanel;

        [Tooltip("Yesボタン（ブラウザAIで試す）")]
        public Button yesButton;

        [Tooltip("Noボタン（スキップ）")]
        public Button noButton;

        [Tooltip("ボタンコンテナ（Yes/No選択中のみ表示）")]
        public GameObject buttonContainer;

        [Tooltip("ダウンロード進捗テキスト")]
        public TMP_Text progressText;

        [Header("WebLLM")]
        [Tooltip("WebLLMBridge参照（シーンに配置されたMonoBehaviour）")]
        public WebLLMBridge webLLMBridge;

        [Tooltip("LLMClient参照（WebLLM設定の自動適用に使用）")]
        public LLMClient llmClient;

        [Header("WebLLM Default Generation Params")]
        [Tooltip("Temperature (0.0-2.0)")]
        [Range(0f, 2f)]
        public float defaultTemperature = 0.7f;

        [Tooltip("Top P (0.0-1.0)")]
        [Range(0f, 1f)]
        public float defaultTopP = 0.9f;

        [Tooltip("最大応答トークン数 (-1=無制限)")]
        public int defaultNumPredict = 512;

        [Tooltip("繰り返しペナルティ (1.0=無効)")]
        public float defaultRepeatPenalty = 1.1f;

        // --- コールバック ---
        private Action _onComplete;
        private bool _isShowing;
        private bool _webLLMSelected;
        private bool _downloadOnlyMode;

        // --- イベント ---
        /// <summary>
        /// WebLLMが選択されたときに発火（モデルDL開始のトリガー）
        /// </summary>
        public event Action OnWebLLMRequested;

        /// <summary>
        /// Noが選択されたときに発火（手動設定モード）
        /// </summary>
        public event Action OnSkipped;

        private void Awake()
        {
            if (popupPanel != null)
                popupPanel.SetActive(false);

            if (progressText != null)
                progressText.gameObject.SetActive(false);
        }

        private void Start()
        {
            if (yesButton != null)
                yesButton.onClick.AddListener(OnYesClicked);
            if (noButton != null)
                noButton.onClick.AddListener(OnNoClicked);
        }

        private void OnDestroy()
        {
            if (yesButton != null)
                yesButton.onClick.RemoveListener(OnYesClicked);
            if (noButton != null)
                noButton.onClick.RemoveListener(OnNoClicked);
        }

        // ─────────────────────────────────────
        // Public API
        // ─────────────────────────────────────

        /// <summary>
        /// ポップアップを表示（ライトOFF + UI表示）
        /// onCompleteはEntry開始のコールバック
        /// </summary>
        public void Show(Action onComplete)
        {
            _onComplete = onComplete;
            _isShowing = true;
            IsPending = true;
            _webLLMSelected = false;
            _downloadOnlyMode = false;

            if (popupPanel != null)
                popupPanel.SetActive(true);
            if (buttonContainer != null)
                buttonContainer.SetActive(true);
            if (progressText != null)
                progressText.gameObject.SetActive(false);

            Debug.Log("[FirstRunController] Popup shown");
        }

        /// <summary>
        /// ダウンロードのみ表示（設定パネルからWebLLM選択時に使用）
        /// ライト制御なし、ボタンなし、進捗表示のみ
        /// </summary>
        public void ShowDownloadOnly(Action onComplete)
        {
            _onComplete = onComplete;
            _isShowing = true;
            _webLLMSelected = true;
            _downloadOnlyMode = true;

            if (popupPanel != null)
                popupPanel.SetActive(true);
            if (buttonContainer != null)
                buttonContainer.SetActive(false);
            if (progressText != null)
            {
                progressText.gameObject.SetActive(true);
                progressText.text = "Downloading...";
            }

            Debug.Log("[FirstRunController] Download-only mode shown");
            StartWebLLMDownload();
        }

        /// <summary>
        /// 外部から完了を通知（WebLLM DL完了 or 設定パネルから保存完了）
        /// ポップアップ消去 + Entry開始コールバック（ライト制御はLightControlTrackで行う）
        /// </summary>
        public void Complete()
        {
            if (!_isShowing && !IsPending) return;
            _isShowing = false;
            IsPending = false;

            if (popupPanel != null)
                popupPanel.SetActive(false);

            Debug.Log("[FirstRunController] Complete, triggering entry");
            _onComplete?.Invoke();
        }

        /// <summary>
        /// ダウンロード進捗を更新（WebLLMBridgeから呼ばれる）
        /// </summary>
        public void UpdateDownloadProgress(long loadedBytes, long totalBytes, string statusText = null)
        {
            if (progressText == null) return;

            // web-llmのstatusTextをそのまま表示（フェーズ名を含む）
            if (!string.IsNullOrEmpty(statusText))
            {
                progressText.text = statusText;
            }
            else if (totalBytes > 0)
            {
                float loadedGB = loadedBytes / (1024f * 1024f * 1024f);
                float totalGB = totalBytes / (1024f * 1024f * 1024f);
                progressText.text = $"Downloading... ({loadedGB:F1}GB / {totalGB:F1}GB)";
            }
            else
            {
                progressText.text = "Downloading...";
            }
        }

        /// <summary>
        /// 表示中かどうか
        /// </summary>
        public bool IsShowing => _isShowing;

        /// <summary>
        /// 初期設定が完了していない（Show()後、Complete()前）。
        /// IsShowingと異なり、Noスキップ後（ポップアップ非表示だが設定未完了）もtrueを返す。
        /// IdleChatController等で初期設定完了前のリクエスト抑制に使用する。
        /// </summary>
        public bool IsPending { get; private set; }

        /// <summary>
        /// WebLLMが選択されたかどうか
        /// </summary>
        public bool IsWebLLMSelected => _webLLMSelected;

        // ─────────────────────────────────────
        // Button Handlers
        // ─────────────────────────────────────

        private void OnYesClicked()
        {
            _webLLMSelected = true;

            // ボタンを非表示にして進捗表示に切り替え
            if (buttonContainer != null)
                buttonContainer.SetActive(false);
            if (progressText != null)
            {
                progressText.gameObject.SetActive(true);
                progressText.text = "Downloading...";
            }

            Debug.Log("[FirstRunController] WebLLM selected");
            OnWebLLMRequested?.Invoke();

            // WebLLMモデルのダウンロード開始
            StartWebLLMDownload();
        }

        private void StartWebLLMDownload()
        {
            if (webLLMBridge == null)
            {
                Debug.LogError("[FirstRunController] WebLLMBridge not set");
                return;
            }

            // 初期化
            webLLMBridge.Initialize();

            // 進捗・完了コールバック登録
            webLLMBridge.OnLoadProgress += OnWebLLMLoadProgress;
            webLLMBridge.OnModelLoaded += OnWebLLMModelLoaded;
            webLLMBridge.OnError += OnWebLLMError;

            // モデルDL開始
            webLLMBridge.LoadModel(WebLLMProvider.DefaultModelId);
        }

        private void OnWebLLMLoadProgress(long loaded, long total, float progress, string text)
        {
            UpdateDownloadProgress(loaded, total, text);
        }

        private void OnWebLLMModelLoaded()
        {
            // コールバック解除
            if (webLLMBridge != null)
            {
                webLLMBridge.OnLoadProgress -= OnWebLLMLoadProgress;
                webLLMBridge.OnModelLoaded -= OnWebLLMModelLoaded;
                webLLMBridge.OnError -= OnWebLLMError;
            }

            // 初回起動Yesルート: LLM設定をWebLLMに切り替えて保存
            // ダウンロードのみモード: LLMSettingsPanelが既に保存済みなのでスキップ
            if (!_downloadOnlyMode && llmClient != null)
            {
                var config = new LLMConfig
                {
                    apiType = LLMApiType.WebLLM,
                    apiEndpoint = "",
                    modelName = WebLLMProvider.DefaultModelId,
                    temperature = defaultTemperature,
                    topP = defaultTopP,
                    topK = 40,
                    numPredict = defaultNumPredict,
                    numCtx = 4096,
                    repeatPenalty = defaultRepeatPenalty,
                    think = false,
                    timeout = 120f,
                    apiKey = ""
                };
                llmClient.SaveAndApplyConfig(config);
                Debug.Log("[FirstRunController] WebLLM config saved and applied");
            }

            // 完了 → ポップアップ消去 + コールバック
            Complete();
        }

        private void OnWebLLMError(string error)
        {
            Debug.LogError($"[FirstRunController] WebLLM error: {error}");
            if (progressText != null)
            {
                progressText.text = $"Error: {error}";
            }

            // コールバック解除
            if (webLLMBridge != null)
            {
                webLLMBridge.OnLoadProgress -= OnWebLLMLoadProgress;
                webLLMBridge.OnModelLoaded -= OnWebLLMModelLoaded;
                webLLMBridge.OnError -= OnWebLLMError;
            }
        }

        private void OnNoClicked()
        {
            // ポップアップを閉じるが、ライトはOFFのまま
            // 設定パネルから正しく設定されたらComplete()が呼ばれる
            if (popupPanel != null)
                popupPanel.SetActive(false);

            Debug.Log("[FirstRunController] Skipped, waiting for manual LLM configuration");
            OnSkipped?.Invoke();
        }

    }
}
