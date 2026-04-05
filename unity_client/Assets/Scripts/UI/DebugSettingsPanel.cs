using UnityEngine;
using UnityEngine.UI;
using TMPro;
using CyanNook.Core;
using CyanNook.DebugTools;
using CyanNook.Character;

namespace CyanNook.UI
{
    /// <summary>
    /// デバッグ設定パネル
    /// デバッグキーON/OFF、JSONモード切替、LLM Raw Text表示
    /// </summary>
    public class DebugSettingsPanel : MonoBehaviour
    {
        [Header("References")]
        public DebugKeyController debugKeyController;
        public UIController uiController;
        public CharacterAnimationController characterAnimationController;
        public SettingsExporter settingsExporter;

        [Header("UI - Debug Key")]
        [Tooltip("デバッグキーON/OFF")]
        public Toggle debugKeyToggle;

        [Tooltip("キーアサイン表示（静的テキスト）")]
        public TMP_Text keyAssignmentsText;

        [Header("UI - JSON Mode")]
        [Tooltip("JSONモードON/OFF")]
        public Toggle jsonModeToggle;

        [Header("UI - LLM Raw Text")]
        [Tooltip("LLM生データ表示ON/OFF")]
        public Toggle rawTextToggle;

        [Header("Raw Text Panel")]
        [Tooltip("画面左半分のRawTextパネル（表示/非表示切替）")]
        public GameObject rawTextPanel;

        [Header("UI - Timeline Debug")]
        [Tooltip("Timeline情報表示ON/OFF")]
        public Toggle timelineDebugToggle;

        [Header("Timeline Debug Panel")]
        [Tooltip("Timeline情報を表示するパネル（表示/非表示切替）")]
        public GameObject timelineDebugPanel;

        [Header("UI - Settings Import/Export")]
        [Tooltip("設定エクスポートボタン")]
        public Button exportSettingsButton;

        [Tooltip("設定インポートボタン")]
        public Button importSettingsButton;

        [Tooltip("インポート/エクスポートのステータス表示")]
        public TMP_Text importExportStatusText;

        [Header("UI - Status Overlay")]
        [Tooltip("ステータス表示ON/OFF")]
        public Toggle statusOverlayToggle;

        [Tooltip("StatusOverlayコンポーネント")]
        public StatusOverlay statusOverlay;

        [Header("UI - Log Export")]
        [Tooltip("ログエクスポートボタン")]
        public Button exportLogButton;

        [Header("UI - License")]
        [Tooltip("ライセンス表示ボタン")]
        public Button licenseButton;

        [Tooltip("ライセンス表示パネル（表示/非表示切替）")]
        public GameObject licensePanel;

        [Tooltip("ライセンステキスト表示先")]
        public TMP_Text licenseText;

        [Tooltip("ライセンスパネル閉じるボタン")]
        public Button licenseCloseButton;

        private void OnEnable()
        {
            // パネル表示時に現在の状態を反映
            if (debugKeyToggle != null)
            {
                debugKeyToggle.isOn = debugKeyController != null && debugKeyController.IsEnabled;
            }

            if (jsonModeToggle != null && uiController != null)
            {
                jsonModeToggle.isOn = uiController.CurrentInputMode == InputMode.Json;
            }

            if (rawTextToggle != null && rawTextPanel != null)
            {
                rawTextToggle.isOn = rawTextPanel.activeSelf;
            }

            if (timelineDebugToggle != null && characterAnimationController != null)
            {
                timelineDebugToggle.isOn = characterAnimationController.IsTimelineDebugEnabled;
            }

            if (statusOverlayToggle != null && statusOverlay != null)
            {
                statusOverlayToggle.isOn = statusOverlay.IsVisible;
            }

            // キーアサイン表示
            if (keyAssignmentsText != null)
            {
                keyAssignmentsText.text =
                    "W : Walk (hold)\n" +
                    "A/D : Turn (while walking)\n" +
                    "C : Start Talk\n" +
                    "V : End Talk\n" +
                    "F : Interact\n" +
                    "G : End Interact\n" +
                    "H : Cancel Interact";
            }
        }

        private void Start()
        {
            // デバッグキートグル
            if (debugKeyToggle != null)
            {
                debugKeyToggle.onValueChanged.AddListener(OnDebugKeyToggleChanged);
            }

            // JSONモードトグル
            if (jsonModeToggle != null)
            {
                jsonModeToggle.onValueChanged.AddListener(OnJsonModeToggleChanged);
            }

            // RawTextトグル
            if (rawTextToggle != null)
            {
                rawTextToggle.onValueChanged.AddListener(OnRawTextToggleChanged);
            }

            // Timeline Debugトグル
            if (timelineDebugToggle != null)
            {
                timelineDebugToggle.onValueChanged.AddListener(OnTimelineDebugToggleChanged);
            }

            // Status Overlayトグル
            if (statusOverlayToggle != null)
            {
                statusOverlayToggle.onValueChanged.AddListener(OnStatusOverlayToggleChanged);
            }

            // Settings Import/Export
            if (exportSettingsButton != null)
            {
                exportSettingsButton.onClick.AddListener(OnExportSettingsClicked);
            }
            if (importSettingsButton != null)
            {
                importSettingsButton.onClick.AddListener(OnImportSettingsClicked);
            }
            if (settingsExporter != null)
            {
                settingsExporter.OnImportComplete += OnSettingsImportComplete;
            }

            // Log Export
            if (exportLogButton != null)
            {
                exportLogButton.onClick.AddListener(OnExportLogClicked);
            }

            // License
            if (licenseButton != null)
            {
                licenseButton.onClick.AddListener(OnLicenseButtonClicked);
            }
            if (licenseCloseButton != null)
            {
                licenseCloseButton.onClick.AddListener(OnLicenseCloseClicked);
            }

            // RawTextパネル初期状態
            if (rawTextPanel != null)
            {
                rawTextPanel.SetActive(false);
            }

            // Timeline Debugパネル初期状態
            if (timelineDebugPanel != null)
            {
                timelineDebugPanel.SetActive(false);
            }

            // Licenseパネル初期状態
            if (licensePanel != null)
            {
                licensePanel.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (debugKeyToggle != null)
                debugKeyToggle.onValueChanged.RemoveListener(OnDebugKeyToggleChanged);
            if (jsonModeToggle != null)
                jsonModeToggle.onValueChanged.RemoveListener(OnJsonModeToggleChanged);
            if (rawTextToggle != null)
                rawTextToggle.onValueChanged.RemoveListener(OnRawTextToggleChanged);
            if (timelineDebugToggle != null)
                timelineDebugToggle.onValueChanged.RemoveListener(OnTimelineDebugToggleChanged);
            if (statusOverlayToggle != null)
                statusOverlayToggle.onValueChanged.RemoveListener(OnStatusOverlayToggleChanged);
            if (exportLogButton != null)
                exportLogButton.onClick.RemoveListener(OnExportLogClicked);
            if (exportSettingsButton != null)
                exportSettingsButton.onClick.RemoveListener(OnExportSettingsClicked);
            if (importSettingsButton != null)
                importSettingsButton.onClick.RemoveListener(OnImportSettingsClicked);
            if (settingsExporter != null)
                settingsExporter.OnImportComplete -= OnSettingsImportComplete;
            if (licenseButton != null)
                licenseButton.onClick.RemoveListener(OnLicenseButtonClicked);
            if (licenseCloseButton != null)
                licenseCloseButton.onClick.RemoveListener(OnLicenseCloseClicked);
        }

        private void OnDebugKeyToggleChanged(bool isOn)
        {
            if (debugKeyController == null)
            {
                Debug.LogWarning("[DebugSettingsPanel] DebugKeyController not available");
                return;
            }

            debugKeyController.SetEnabled(isOn);
            Debug.Log($"[DebugSettingsPanel] Debug keys: {(isOn ? "ON" : "OFF")}");
        }

        private void OnJsonModeToggleChanged(bool isOn)
        {
            if (uiController == null)
            {
                Debug.LogWarning("[DebugSettingsPanel] UIController not available");
                return;
            }

            uiController.SetInputMode(isOn ? InputMode.Json : InputMode.Chat);
            Debug.Log($"[DebugSettingsPanel] JSON mode: {(isOn ? "ON" : "OFF")}");
        }

        private void OnRawTextToggleChanged(bool isOn)
        {
            if (rawTextPanel != null)
            {
                rawTextPanel.SetActive(isOn);
            }

            Debug.Log($"[DebugSettingsPanel] Raw text: {(isOn ? "ON" : "OFF")}");
        }

        private void OnTimelineDebugToggleChanged(bool isOn)
        {
            if (characterAnimationController == null)
            {
                Debug.LogWarning("[DebugSettingsPanel] CharacterAnimationController not available");
                return;
            }

            characterAnimationController.SetTimelineDebugEnabled(isOn);

            if (timelineDebugPanel != null)
            {
                timelineDebugPanel.SetActive(isOn);
            }

            Debug.Log($"[DebugSettingsPanel] Timeline debug: {(isOn ? "ON" : "OFF")}");
        }

        private void OnStatusOverlayToggleChanged(bool isOn)
        {
            if (statusOverlay != null)
                statusOverlay.SetVisible(isOn);
        }

        // ─────────────────────────────────────
        // Settings Import/Export
        // ─────────────────────────────────────

        private void OnExportLogClicked()
        {
            var logger = CyanNook.DebugTools.PerformanceLogger.Instance;
            if (logger != null)
            {
                logger.DownloadLog();
                SetImportExportStatus("Log exported!");
            }
            else
            {
                Debug.LogWarning("[DebugSettingsPanel] PerformanceLogger not found");
                SetImportExportStatus("Logger not found");
            }
        }

        private void OnExportSettingsClicked()
        {
            if (settingsExporter == null)
            {
                Debug.LogWarning("[DebugSettingsPanel] SettingsExporter not available");
                return;
            }

            settingsExporter.ExportAndDownload();
            SetImportExportStatus("Exported!");
        }

        private void OnImportSettingsClicked()
        {
            if (settingsExporter == null)
            {
                Debug.LogWarning("[DebugSettingsPanel] SettingsExporter not available");
                return;
            }

            SetImportExportStatus("Selecting file...");
            settingsExporter.OpenImportDialog();
        }

        private void OnSettingsImportComplete(bool success, string message)
        {
            SetImportExportStatus(success ? $"OK: {message}" : $"Error: {message}");

            if (success)
            {
                Debug.Log($"[DebugSettingsPanel] Settings imported. Reload the page to apply all settings.");
                SetImportExportStatus($"OK: {message} (reload to apply)");
            }
        }

        private void SetImportExportStatus(string message)
        {
            if (importExportStatusText != null)
            {
                importExportStatusText.text = message;
            }
        }

        // ─────────────────────────────────────
        // License
        // ─────────────────────────────────────

        private void OnLicenseButtonClicked()
        {
            if (licensePanel == null) return;

            if (licensePanel.activeSelf)
            {
                licensePanel.SetActive(false);
                return;
            }

            // TextAssetからライセンステキストを読み込み
            if (licenseText != null)
            {
                var textAsset = Resources.Load<TextAsset>("LicenseText");
                licenseText.text = textAsset != null
                    ? textAsset.text
                    : "License text not found.";
            }

            licensePanel.SetActive(true);
        }

        private void OnLicenseCloseClicked()
        {
            if (licensePanel != null)
            {
                licensePanel.SetActive(false);
            }
        }
    }
}
