using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Threading.Tasks;
using CyanNook.Core;
using CyanNook.Voice;

namespace CyanNook.UI
{
    /// <summary>
    /// 音声設定パネル
    /// TTSエンジン選択（Web Speech API / VOICEVOX / Gemini TTS）、ボイスパラメータ、音声入力
    /// </summary>
    public class VoiceSettingsPanel : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("VoicevoxClient参照")]
        public VoicevoxClient voicevoxClient;

        [Tooltip("GeminiTtsClient参照")]
        public GeminiTtsClient geminiTtsClient;

        [Tooltip("WebSpeechSynthesis参照")]
        public WebSpeechSynthesis webSpeechSynthesis;

        [Tooltip("VoiceSynthesisController参照")]
        public VoiceSynthesisController voiceSynthesisController;

        [Tooltip("音声テスト用AudioSource")]
        public AudioSource testAudioSource;

        [Tooltip("VoiceInputController参照（音声入力用）")]
        public VoiceInputController voiceInputController;

        [Header("UI - TTS ON/OFF")]
        [Tooltip("TTS有効/無効トグル")]
        public Toggle ttsEnabledToggle;

        [Header("UI - TTS Engine Selection")]
        [Tooltip("TTSエンジン選択ドロップダウン")]
        public TMP_Dropdown ttsEngineDropdown;

        [Header("UI - Section Containers")]
        [Tooltip("Web Speech API設定セクション")]
        public GameObject webSpeechSettingsSection;

        [Tooltip("VOICEVOX設定セクション")]
        public GameObject voicevoxSettingsSection;

        [Tooltip("Gemini TTS設定セクション")]
        public GameObject geminiTtsSettingsSection;

        [Header("UI - Web Speech API")]
        [Tooltip("Web Speech APIボイス選択")]
        public TMP_Dropdown webSpeechVoiceDropdown;

        [Tooltip("Web Speech API話速スライダー (0.5-3.0)")]
        public Slider webSpeechRateSlider;
        [Tooltip("Web Speech API話速値テキスト")]
        public TMP_Text webSpeechRateValueText;

        [Tooltip("Web Speech APIピッチスライダー (0.0-2.0)")]
        public Slider webSpeechPitchSlider;
        [Tooltip("Web Speech APIピッチ値テキスト")]
        public TMP_Text webSpeechPitchValueText;

        [Tooltip("Web Speech APIテスト再生ボタン")]
        public Button webSpeechTestButton;

        [Header("UI - VOICEVOX API")]
        [Tooltip("API URL入力")]
        public TMP_InputField apiUrlInputField;

        [Tooltip("スピーカー選択ドロップダウン")]
        public TMP_Dropdown speakerDropdown;

        [Header("UI - VOICEVOX Voice Parameters")]
        [Tooltip("話速スライダー (0.5-2.0)")]
        public Slider speedSlider;
        [Tooltip("話速表示テキスト")]
        public TMP_Text speedValueText;

        [Tooltip("音高スライダー (-0.15-0.15)")]
        public Slider pitchSlider;
        [Tooltip("音高表示テキスト")]
        public TMP_Text pitchValueText;

        [Tooltip("抑揚スライダー (0.0-2.0)")]
        public Slider intonationSlider;
        [Tooltip("抑揚表示テキスト")]
        public TMP_Text intonationValueText;

        [Header("UI - Gemini TTS")]
        [Tooltip("Gemini APIキー入力")]
        public TMP_InputField geminiApiKeyInputField;

        [Tooltip("Gemini TTSモデル選択ドロップダウン (Flash/Pro)")]
        public TMP_Dropdown geminiModelDropdown;

        [Tooltip("Gemini TTSボイス選択ドロップダウン")]
        public TMP_Dropdown geminiVoiceDropdown;

        [Tooltip("Gemini TTSスタイル指示プロンプト入力")]
        public TMP_InputField geminiStylePromptInputField;

        [Tooltip("Gemini TTS音声テスト再生ボタン")]
        public Button geminiTtsTestButton;

        [Header("UI - Test")]
        [Tooltip("テスト用テキスト入力")]
        public TMP_InputField testTextInputField;

        [Tooltip("VOICEVOX音声テスト再生ボタン")]
        public Button testPlayButton;

        [Header("UI - VOICEVOX Buttons")]
        [Tooltip("設定保存ボタン")]
        public Button saveButton;

        [Tooltip("VOICEVOX接続テストボタン")]
        public Button testConnectionButton;

        [Header("UI - Voice Input (Speech to Text)")]
        [Tooltip("マイクON/OFF")]
        public Toggle microphoneToggle;

        [Tooltip("エコー防止ON/OFF（ヘッドセット使用時はOFFでよい）")]
        public Toggle echoPreventionToggle;

        [Tooltip("認識言語選択")]
        public TMP_Dropdown voiceInputLanguageDropdown;

        [Tooltip("自動送信までの無音秒数")]
        public TMP_InputField silenceThresholdInputField;

        [Header("UI - Status")]
        [Tooltip("ステータス表示")]
        public TMP_Text statusText;

        [Tooltip("音声入力ステータス表示")]
        public TMP_Text voiceInputStatusText;

        // スピーカーリスト（VOICEVOX）
        private List<VoicevoxSpeaker> _speakers = new List<VoicevoxSpeaker>();
        private List<SpeakerDropdownEntry> _speakerEntries = new List<SpeakerDropdownEntry>();

        // Web Speech APIボイスリスト
        private List<WebSpeechVoice> _webSpeechVoices = new List<WebSpeechVoice>();

        // Gemini TTS の固定リスト（公式30種類のうち代表的なもの）
        // ref: https://ai.google.dev/gemini-api/docs/speech-generation
        private static readonly string[] GeminiVoiceNames = new[]
        {
            "Zephyr", "Puck", "Charon", "Kore", "Fenrir", "Leda", "Orus", "Aoede",
            "Callirrhoe", "Autonoe", "Enceladus", "Iapetus", "Umbriel", "Algieba",
            "Despina", "Erinome", "Algenib", "Rasalgethi", "Laomedeia", "Achernar",
            "Alnilam", "Schedar", "Gacrux", "Pulcherrima", "Achird", "Zubenelgenubi",
            "Vindemiatrix", "Sadachbia", "Sadaltager", "Sulafat"
        };

        private static readonly string[] GeminiModelNames = new[]
        {
            "gemini-2.5-flash-preview-tts",
            "gemini-2.5-pro-preview-tts"
        };

        // PlayerPrefs キー（音声入力用）
        private const string PREF_MIC_ENABLED = "voice_micEnabled";
        private const string PREF_VOICE_INPUT_LANGUAGE = "voice_inputLanguage";
        private const string PREF_SILENCE_THRESHOLD = "voice_silenceThreshold";

        private void Start()
        {
            // TTS ON/OFFトグル
            if (ttsEnabledToggle != null)
            {
                ttsEnabledToggle.onValueChanged.AddListener(OnTTSEnabledChanged);
            }

            // TTSエンジンドロップダウン
            InitializeTTSEngineDropdown();

            // Web Speech API設定
            InitializeWebSpeechSettings();

            // Gemini TTS設定
            InitializeGeminiTtsSettings();

            // VOICEVOXスライダーイベント
            if (speedSlider != null)
            {
                speedSlider.minValue = 0.5f;
                speedSlider.maxValue = 2.0f;
                speedSlider.onValueChanged.AddListener(OnSpeedChanged);
            }
            if (pitchSlider != null)
            {
                pitchSlider.minValue = -0.15f;
                pitchSlider.maxValue = 0.15f;
                pitchSlider.onValueChanged.AddListener(OnPitchChanged);
            }
            if (intonationSlider != null)
            {
                intonationSlider.minValue = 0.0f;
                intonationSlider.maxValue = 2.0f;
                intonationSlider.onValueChanged.AddListener(OnIntonationChanged);
            }

            // VOICEVOXボタンイベント
            if (saveButton != null)
            {
                saveButton.onClick.AddListener(OnSaveClicked);
            }
            if (testConnectionButton != null)
            {
                testConnectionButton.onClick.AddListener(OnTestConnectionClicked);
            }
            if (testPlayButton != null)
            {
                testPlayButton.onClick.AddListener(OnVoicevoxTestPlayClicked);
            }

            // テスト用デフォルトテキスト
            if (testTextInputField != null)
            {
                testTextInputField.text = "こんにちは、音声合成のテストです。";
            }

            // 音声入力設定
            InitializeVoiceInput();

            // 起動時に保存済みマイク設定を適用
            // （OnEnable→LoadVoiceInputSettingsはリスナー登録前に実行されるため、ここで明示的に適用）
            ApplySavedMicrophoneSetting();

            // Unityライフサイクル: 初回アクティブ化時はOnEnable → Startの順で実行されるため、
            // OnEnableのLoad*ToUI()はInitialize*()より先に走っている。この時点でドロップダウンの
            // optionsはまだPrefabデフォルトのまま（"Option A/B/C"等）で、.valueを設定しても
            // 正しく反映されない。Initialize*()が終わった今、静的ドロップダウン系のLoadを
            // 再実行して正しい値を反映する。
            // 対象:
            //   - LoadTTSEngineToUI: ttsEngineDropdown（3択）
            //   - LoadGeminiTtsSettingsToUI: geminiModelDropdown（2択）+ geminiVoiceDropdown（30択）
            // 動的dropdown（speakerDropdown, webSpeechVoiceDropdown）は別経路で非同期populate
            // されるのでここでの再Loadは不要。
            LoadTTSEngineToUI();
            LoadGeminiTtsSettingsToUI();
            UpdateTTSSettingsVisibility();

            // 起動時のTTSクレジット初期化
            NotifyTTSCreditChanged();
        }

        private void OnEnable()
        {
            LoadTTSEnabledToUI();
            LoadTTSEngineToUI();
            LoadVoicevoxSettingsToUI();
            LoadWebSpeechSettingsToUI();
            LoadGeminiTtsSettingsToUI();
            LoadVoiceInputSettings();
            UpdateTTSSettingsVisibility();

            // VOICEVOX選択時のみスピーカーリスト取得
            if (voiceSynthesisController != null && voiceSynthesisController.CurrentEngine == TTSEngineType.VOICEVOX)
            {
                _ = RefreshSpeakerList();
            }
        }

        private void OnDisable()
        {
            // テスト再生中の音声を停止
            if (testAudioSource != null)
            {
                testAudioSource.Stop();
                testAudioSource.clip = null;
            }
        }

        private void Update()
        {
            UpdateVoiceInputStatus();
        }

        private void OnDestroy()
        {
            // TTS ON/OFF
            if (ttsEnabledToggle != null)
                ttsEnabledToggle.onValueChanged.RemoveListener(OnTTSEnabledChanged);

            // TTSエンジン
            if (ttsEngineDropdown != null)
                ttsEngineDropdown.onValueChanged.RemoveListener(OnTTSEngineChanged);

            // Web Speech API
            if (webSpeechRateSlider != null)
                webSpeechRateSlider.onValueChanged.RemoveListener(OnWebSpeechRateChanged);
            if (webSpeechPitchSlider != null)
                webSpeechPitchSlider.onValueChanged.RemoveListener(OnWebSpeechPitchChanged);
            if (webSpeechTestButton != null)
                webSpeechTestButton.onClick.RemoveListener(OnWebSpeechTestPlayClicked);
            if (webSpeechSynthesis != null)
                webSpeechSynthesis.OnVoicesLoadedEvent.RemoveListener(OnWebSpeechVoicesLoaded);

            // Gemini TTS
            if (geminiModelDropdown != null)
                geminiModelDropdown.onValueChanged.RemoveListener(OnGeminiModelChanged);
            if (geminiVoiceDropdown != null)
                geminiVoiceDropdown.onValueChanged.RemoveListener(OnGeminiVoiceChanged);
            if (geminiApiKeyInputField != null)
                geminiApiKeyInputField.onEndEdit.RemoveListener(OnGeminiApiKeyChanged);
            if (geminiStylePromptInputField != null)
                geminiStylePromptInputField.onEndEdit.RemoveListener(OnGeminiStylePromptChanged);
            if (geminiTtsTestButton != null)
                geminiTtsTestButton.onClick.RemoveListener(OnGeminiTtsTestPlayClicked);

            // VOICEVOX
            if (speedSlider != null)
                speedSlider.onValueChanged.RemoveListener(OnSpeedChanged);
            if (pitchSlider != null)
                pitchSlider.onValueChanged.RemoveListener(OnPitchChanged);
            if (intonationSlider != null)
                intonationSlider.onValueChanged.RemoveListener(OnIntonationChanged);
            if (saveButton != null)
                saveButton.onClick.RemoveListener(OnSaveClicked);
            if (testConnectionButton != null)
                testConnectionButton.onClick.RemoveListener(OnTestConnectionClicked);
            if (testPlayButton != null)
                testPlayButton.onClick.RemoveListener(OnVoicevoxTestPlayClicked);

            // 音声入力
            if (microphoneToggle != null)
                microphoneToggle.onValueChanged.RemoveListener(OnMicrophoneToggleChanged);
            if (echoPreventionToggle != null)
                echoPreventionToggle.onValueChanged.RemoveListener(OnEchoPreventionToggleChanged);
            if (voiceInputLanguageDropdown != null)
                voiceInputLanguageDropdown.onValueChanged.RemoveListener(OnVoiceInputLanguageChanged);
            if (silenceThresholdInputField != null)
                silenceThresholdInputField.onEndEdit.RemoveListener(OnSilenceThresholdChanged);
            if (voiceInputController != null)
                voiceInputController.OnEnabledChanged -= OnVoiceInputEnabledChanged;
        }

        // ─────────────────────────────────────
        // TTS ON/OFF
        // ─────────────────────────────────────

        private void LoadTTSEnabledToUI()
        {
            if (ttsEnabledToggle != null && voiceSynthesisController != null)
            {
                ttsEnabledToggle.isOn = voiceSynthesisController.enabled;
            }
        }

        private void OnTTSEnabledChanged(bool isOn)
        {
            if (voiceSynthesisController != null)
            {
                voiceSynthesisController.SetEnabled(isOn);
            }

            UpdateTTSSettingsVisibility();
            NotifyTTSCreditChanged();
            Debug.Log($"[VoiceSettingsPanel] TTS: {(isOn ? "ON" : "OFF")}");
        }

        /// <summary>
        /// TTS ON/OFFに応じてエンジン設定セクション全体の表示を切替
        /// </summary>
        private void UpdateTTSSettingsVisibility()
        {
            bool ttsEnabled = ttsEnabledToggle != null && ttsEnabledToggle.isOn;

            // エンジン選択ドロップダウンの操作可否
            if (ttsEngineDropdown != null)
                ttsEngineDropdown.interactable = ttsEnabled;

            if (!ttsEnabled)
            {
                // OFFの場合はエンジン設定セクションをすべて非表示
                if (webSpeechSettingsSection != null)
                    webSpeechSettingsSection.SetActive(false);
                if (voicevoxSettingsSection != null)
                    voicevoxSettingsSection.SetActive(false);
                if (geminiTtsSettingsSection != null)
                    geminiTtsSettingsSection.SetActive(false);
            }
            else
            {
                // ONの場合は選択エンジンに応じて表示
                UpdateTTSEngineUI();
            }
        }

        // ─────────────────────────────────────
        // TTSエンジン選択
        // ─────────────────────────────────────

        // ドロップダウンindex ↔ TTSEngineType の間接マッピング
        private List<TTSEngineType> _availableTtsEngines;

        private void InitializeTTSEngineDropdown()
        {
            // 利用可能TTSエンジンリストを構築
            _availableTtsEngines = new List<TTSEngineType>();
#if UNITYROOM_BUILD
            _availableTtsEngines.Add(TTSEngineType.WebSpeechAPI);
            _availableTtsEngines.Add(TTSEngineType.GeminiTTS);
#else
            _availableTtsEngines.Add(TTSEngineType.WebSpeechAPI);
            _availableTtsEngines.Add(TTSEngineType.VOICEVOX);
            _availableTtsEngines.Add(TTSEngineType.GeminiTTS);
#endif

            if (ttsEngineDropdown != null)
            {
                ttsEngineDropdown.ClearOptions();
                var labels = new List<string>();
                foreach (var engine in _availableTtsEngines)
                {
                    labels.Add(GetTtsEngineLabel(engine));
                }
                ttsEngineDropdown.AddOptions(labels);
                ttsEngineDropdown.onValueChanged.AddListener(OnTTSEngineChanged);
            }
        }

        private TTSEngineType GetTtsEngineFromDropdownIndex(int index)
        {
            if (_availableTtsEngines != null && index >= 0 && index < _availableTtsEngines.Count)
                return _availableTtsEngines[index];
            return TTSEngineType.WebSpeechAPI;
        }

        private int GetTtsDropdownIndexFromEngine(TTSEngineType engine)
        {
            if (_availableTtsEngines != null)
            {
                int idx = _availableTtsEngines.IndexOf(engine);
                if (idx >= 0) return idx;
            }
            return 0;
        }

        private static string GetTtsEngineLabel(TTSEngineType engine)
        {
            switch (engine)
            {
                case TTSEngineType.WebSpeechAPI: return "Web Speech API";
                case TTSEngineType.VOICEVOX: return "VOICEVOX";
                case TTSEngineType.GeminiTTS: return "Gemini TTS";
                default: return engine.ToString();
            }
        }

        private void LoadTTSEngineToUI()
        {
            if (ttsEngineDropdown != null && voiceSynthesisController != null)
            {
                ttsEngineDropdown.value = GetTtsDropdownIndexFromEngine(voiceSynthesisController.CurrentEngine);
            }
        }

        private void OnTTSEngineChanged(int index)
        {
            var engine = GetTtsEngineFromDropdownIndex(index);

            if (voiceSynthesisController != null)
            {
                voiceSynthesisController.SetTTSEngine(engine);
            }

            UpdateTTSSettingsVisibility();
            NotifyTTSCreditChanged();

            // VOICEVOX選択時はスピーカーリストを取得
            if (engine == TTSEngineType.VOICEVOX)
            {
                _ = RefreshSpeakerList();
            }

            Debug.Log($"[VoiceSettingsPanel] TTS Engine: {engine}");
        }

        /// <summary>
        /// TTSエンジンに応じた設定セクションの表示切替
        /// </summary>
        private void UpdateTTSEngineUI()
        {
            int idx = ttsEngineDropdown != null ? ttsEngineDropdown.value : 0;
            var engine = GetTtsEngineFromDropdownIndex(idx);

            if (webSpeechSettingsSection != null)
                webSpeechSettingsSection.SetActive(engine == TTSEngineType.WebSpeechAPI);
            if (voicevoxSettingsSection != null)
                voicevoxSettingsSection.SetActive(engine == TTSEngineType.VOICEVOX);
            if (geminiTtsSettingsSection != null)
                geminiTtsSettingsSection.SetActive(engine == TTSEngineType.GeminiTTS);
        }

        // ─────────────────────────────────────
        // Web Speech API設定
        // ─────────────────────────────────────

        private void InitializeWebSpeechSettings()
        {
            // スライダー
            if (webSpeechRateSlider != null)
            {
                webSpeechRateSlider.minValue = 0.5f;
                webSpeechRateSlider.maxValue = 3.0f;
                webSpeechRateSlider.onValueChanged.AddListener(OnWebSpeechRateChanged);
            }
            if (webSpeechPitchSlider != null)
            {
                webSpeechPitchSlider.minValue = 0.0f;
                webSpeechPitchSlider.maxValue = 2.0f;
                webSpeechPitchSlider.onValueChanged.AddListener(OnWebSpeechPitchChanged);
            }

            // テスト再生ボタン
            if (webSpeechTestButton != null)
            {
                webSpeechTestButton.onClick.AddListener(OnWebSpeechTestPlayClicked);
            }

            // 音声リスト読み込みイベント
            if (webSpeechSynthesis != null)
            {
                webSpeechSynthesis.OnVoicesLoadedEvent.AddListener(OnWebSpeechVoicesLoaded);

                // Start()はOnEnable()の後に実行される
                // OnEnable()時点でまだInitializeされていなかった場合のために
                // ここでも音声リストを再取得する（既に読み込み済みならドロップダウン更新）
                RefreshWebSpeechVoiceDropdown();
            }
        }

        private void LoadWebSpeechSettingsToUI()
        {
            if (webSpeechSynthesis == null) return;

            if (webSpeechRateSlider != null)
            {
                webSpeechRateSlider.value = webSpeechSynthesis.rate;
            }
            if (webSpeechPitchSlider != null)
            {
                webSpeechPitchSlider.value = webSpeechSynthesis.pitch;
            }

            // ボイスドロップダウンの復元
            RefreshWebSpeechVoiceDropdown();
        }

        private void OnWebSpeechRateChanged(float value)
        {
            if (webSpeechSynthesis != null)
            {
                webSpeechSynthesis.rate = value;
            }
            if (webSpeechRateValueText != null)
            {
                webSpeechRateValueText.text = value.ToString("F2");
            }
        }

        private void OnWebSpeechPitchChanged(float value)
        {
            if (webSpeechSynthesis != null)
            {
                webSpeechSynthesis.pitch = value;
            }
            if (webSpeechPitchValueText != null)
            {
                webSpeechPitchValueText.text = value.ToString("F2");
            }
        }

        private void OnWebSpeechTestPlayClicked()
        {
            if (webSpeechSynthesis == null)
            {
                SetStatusForEngine(TTSEngineType.WebSpeechAPI, "Error: WebSpeechSynthesis not available");
                return;
            }

            string text = testTextInputField != null ? testTextInputField.text : "こんにちは、音声合成のテストです。";
            if (string.IsNullOrEmpty(text))
            {
                SetStatusForEngine(TTSEngineType.WebSpeechAPI, "Please enter test text");
                return;
            }

            // ボイス選択を反映
            if (webSpeechVoiceDropdown != null && _webSpeechVoices.Count > 0)
            {
                int index = webSpeechVoiceDropdown.value;
                if (index >= 0 && index < _webSpeechVoices.Count)
                {
                    webSpeechSynthesis.voiceURI = _webSpeechVoices[index].voiceURI;
                }
            }

            webSpeechSynthesis.Speak(text);
            SetStatusForEngine(TTSEngineType.WebSpeechAPI, "Speaking...");
            Debug.Log($"[VoiceSettingsPanel] Web Speech test: {text}");
        }

        /// <summary>
        /// ブラウザから音声リスト読み込み完了
        /// </summary>
        private void OnWebSpeechVoicesLoaded(string voicesJson)
        {
            if (webSpeechSynthesis != null)
            {
                _webSpeechVoices = webSpeechSynthesis.AvailableVoices;
            }
            RefreshWebSpeechVoiceDropdown();
        }

        /// <summary>
        /// Web Speech APIボイスドロップダウンを更新
        /// </summary>
        private void RefreshWebSpeechVoiceDropdown()
        {
            if (webSpeechVoiceDropdown == null) return;

            // 常にwebSpeechSynthesisから最新の音声リストを同期
            // （イベント発火前にvoicesが既に読み込まれている場合への対応）
            if (webSpeechSynthesis != null)
            {
                var voices = webSpeechSynthesis.AvailableVoices;
                if (voices != null && voices.Count > 0)
                {
                    _webSpeechVoices = voices;
                }
            }

            webSpeechVoiceDropdown.ClearOptions();

            if (_webSpeechVoices == null || _webSpeechVoices.Count == 0)
            {
                webSpeechVoiceDropdown.AddOptions(new List<string> { "(No voices available)" });
                webSpeechVoiceDropdown.interactable = false;
                return;
            }

            var options = new List<string>();
            int selectedIndex = 0;
            string savedVoiceURI = webSpeechSynthesis?.voiceURI ?? "";

            for (int i = 0; i < _webSpeechVoices.Count; i++)
            {
                var voice = _webSpeechVoices[i];
                options.Add($"{voice.name} ({voice.lang})");

                if (voice.voiceURI == savedVoiceURI)
                {
                    selectedIndex = i;
                }
            }

            webSpeechVoiceDropdown.AddOptions(options);
            webSpeechVoiceDropdown.interactable = true;
            webSpeechVoiceDropdown.value = selectedIndex;

            Debug.Log($"[VoiceSettingsPanel] Web Speech voices: {_webSpeechVoices.Count}");
        }

        // ─────────────────────────────────────
        // VOICEVOXスピーカーリスト
        // ─────────────────────────────────────

        private async Task RefreshSpeakerList()
        {
            if (voicevoxClient == null)
            {
                Debug.LogWarning("[VoiceSettingsPanel] VoicevoxClient not assigned");
                return;
            }

            SetStatusForEngine(TTSEngineType.VOICEVOX, "Loading speakers...");

            _speakers = await voicevoxClient.GetSpeakers();

            if (_speakers == null || _speakers.Count == 0)
            {
                SetStatusForEngine(TTSEngineType.VOICEVOX, "Failed to load speakers");
                if (speakerDropdown != null)
                {
                    speakerDropdown.ClearOptions();
                    speakerDropdown.AddOptions(new List<string> { "(No speakers available)" });
                    speakerDropdown.interactable = false;
                }
                return;
            }

            _speakerEntries.Clear();
            var options = new List<string>();

            foreach (var speaker in _speakers)
            {
                foreach (var style in speaker.styles)
                {
                    _speakerEntries.Add(new SpeakerDropdownEntry
                    {
                        speakerName = speaker.name,
                        styleName = style.name,
                        styleId = style.id
                    });
                    options.Add($"{speaker.name} ({style.name})");
                }
            }

            if (speakerDropdown != null)
            {
                speakerDropdown.ClearOptions();
                speakerDropdown.AddOptions(options);
                speakerDropdown.interactable = true;

                int currentId = voicevoxClient.speakerId;
                int index = GetSpeakerIndex(currentId);
                if (index >= 0)
                {
                    speakerDropdown.value = index;
                }
            }

            NotifyTTSCreditChanged();
            SetStatusForEngine(TTSEngineType.VOICEVOX, $"Loaded {_speakerEntries.Count} voices");
            Debug.Log($"[VoiceSettingsPanel] Loaded {_speakerEntries.Count} speaker styles");
        }

        private int GetSpeakerIndex(int speakerId)
        {
            for (int i = 0; i < _speakerEntries.Count; i++)
            {
                if (_speakerEntries[i].styleId == speakerId)
                {
                    return i;
                }
            }
            return -1;
        }

        private int GetSpeakerIdFromIndex(int index)
        {
            if (index >= 0 && index < _speakerEntries.Count)
            {
                int styleId = _speakerEntries[index].styleId;
                Debug.Log($"[VoiceSettingsPanel] GetSpeakerIdFromIndex({index}) = {styleId} (from {_speakerEntries[index].speakerName} - {_speakerEntries[index].styleName})");
                return styleId;
            }
            Debug.LogWarning($"[VoiceSettingsPanel] GetSpeakerIdFromIndex({index}) out of range! Count: {_speakerEntries.Count}, returning 0");
            return 0;
        }

        // ─────────────────────────────────────
        // VOICEVOXパラメータ変更
        // ─────────────────────────────────────

        private void OnSpeedChanged(float value)
        {
            if (voicevoxClient == null) return;
            voicevoxClient.speedScale = value;
            if (speedValueText != null)
            {
                speedValueText.text = value.ToString("F2");
            }
        }

        private void OnPitchChanged(float value)
        {
            if (voicevoxClient == null) return;
            voicevoxClient.pitchScale = value;
            if (pitchValueText != null)
            {
                pitchValueText.text = value >= 0 ? $"+{value:F2}" : value.ToString("F2");
            }
        }

        private void OnIntonationChanged(float value)
        {
            if (voicevoxClient == null) return;
            voicevoxClient.intonationScale = value;
            if (intonationValueText != null)
            {
                intonationValueText.text = value.ToString("F2");
            }
        }

        // ─────────────────────────────────────
        // VOICEVOX設定の読み書き
        // ─────────────────────────────────────

        private void LoadVoicevoxSettingsToUI()
        {
            if (voicevoxClient == null) return;

            if (apiUrlInputField != null)
            {
                apiUrlInputField.text = voicevoxClient.apiUrl;
            }

            if (speedSlider != null)
            {
                speedSlider.value = voicevoxClient.speedScale;
            }
            if (pitchSlider != null)
            {
                pitchSlider.value = voicevoxClient.pitchScale;
            }
            if (intonationSlider != null)
            {
                intonationSlider.value = voicevoxClient.intonationScale;
            }
        }

        // ─────────────────────────────────────
        // ボタンアクション
        // ─────────────────────────────────────

        private void OnSaveClicked()
        {
            // TTSエンジン設定は即時保存済み（SetTTSEngine内）

            // VOICEVOX設定保存
            if (voicevoxClient != null)
            {
                if (apiUrlInputField != null)
                {
                    voicevoxClient.apiUrl = apiUrlInputField.text.TrimEnd('/');
                }

                // スピーカー設定はVOICEVOX選択時のみ検証・保存
                bool isVoicevoxSelected = voiceSynthesisController != null
                    && voiceSynthesisController.CurrentEngine == TTSEngineType.VOICEVOX;

                if (speakerDropdown != null && isVoicevoxSelected)
                {
                    int dropdownValue = speakerDropdown.value;
                    int speakerId = GetSpeakerIdFromIndex(dropdownValue);

                    Debug.Log($"[VoiceSettingsPanel] OnSaveClicked - Dropdown: {dropdownValue}, SpeakerId: {speakerId}, Entries: {_speakerEntries.Count}");

                    if (_speakerEntries.Count == 0)
                    {
                        Debug.LogWarning("[VoiceSettingsPanel] Speaker list is empty! Please click Test Connection first.");
                        SetStatusForEngine(TTSEngineType.VOICEVOX, "Error: Speaker list not loaded");
                        return;
                    }

                    voicevoxClient.speakerId = speakerId;
                }

                voicevoxClient.SaveSettings();
            }

            // Web Speech API設定保存
            if (webSpeechSynthesis != null)
            {
                // ボイス選択を反映
                if (webSpeechVoiceDropdown != null && _webSpeechVoices.Count > 0)
                {
                    int index = webSpeechVoiceDropdown.value;
                    if (index >= 0 && index < _webSpeechVoices.Count)
                    {
                        webSpeechSynthesis.voiceURI = _webSpeechVoices[index].voiceURI;
                    }
                }

                webSpeechSynthesis.SaveSettings();
            }

            // Gemini TTS設定保存
            if (geminiTtsClient != null)
            {
                ApplyGeminiUiToClient();
                geminiTtsClient.SaveSettings();
            }

            // 音声入力設定保存
            SaveVoiceInputSettings();

            NotifyTTSCreditChanged();
            SetStatus("Saved!");
            Debug.Log("[VoiceSettingsPanel] Settings saved");
        }

        private async void OnTestConnectionClicked()
        {
            if (voicevoxClient == null) return;

            SetStatusForEngine(TTSEngineType.VOICEVOX, "Testing connection...");

            if (apiUrlInputField != null)
            {
                voicevoxClient.apiUrl = apiUrlInputField.text.TrimEnd('/');
            }

            bool success = await voicevoxClient.TestConnection();

            if (success)
            {
                SetStatusForEngine(TTSEngineType.VOICEVOX, "Connection OK!");
                await RefreshSpeakerList();
            }
            else
            {
                SetStatusForEngine(TTSEngineType.VOICEVOX, "Connection Failed");
            }
        }

        private async void OnVoicevoxTestPlayClicked()
        {
            if (voicevoxClient == null || testAudioSource == null)
            {
                SetStatusForEngine(TTSEngineType.VOICEVOX, "Error: Missing references");
                return;
            }

            if (testTextInputField == null || string.IsNullOrEmpty(testTextInputField.text))
            {
                SetStatusForEngine(TTSEngineType.VOICEVOX, "Please enter test text");
                return;
            }

            // スピーカーID更新
            if (speakerDropdown != null)
            {
                int dropdownValue = speakerDropdown.value;
                int speakerId = GetSpeakerIdFromIndex(dropdownValue);

                Debug.Log($"[VoiceSettingsPanel] Dropdown value: {dropdownValue}, SpeakerId: {speakerId}, Entries count: {_speakerEntries.Count}");

                if (speakerId == 0)
                {
                    Debug.LogWarning($"[VoiceSettingsPanel] SpeakerId is 0! Dropdown value: {dropdownValue}, Entries: {_speakerEntries.Count}");
                }

                voicevoxClient.speakerId = speakerId;
                Debug.Log($"[VoiceSettingsPanel] Updated voicevoxClient.speakerId to: {voicevoxClient.speakerId}");
            }
            else
            {
                Debug.LogWarning("[VoiceSettingsPanel] speakerDropdown is null!");
            }

            SetStatusForEngine(TTSEngineType.VOICEVOX, "Synthesizing...");

            string text = testTextInputField.text;
            var (clip, _) = await voicevoxClient.SynthesizeAsync(text);

            if (clip == null)
            {
                SetStatusForEngine(TTSEngineType.VOICEVOX, "Synthesis failed");
                return;
            }

            testAudioSource.clip = clip;
            testAudioSource.Play();

            SetStatusForEngine(TTSEngineType.VOICEVOX, $"Playing... ({clip.length:F1}s)");
            Debug.Log($"[VoiceSettingsPanel] Playing test audio: {text}");
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }
        }

        /// <summary>
        /// エンジン別のstatus書き込み。
        /// 現在選択中のエンジンと一致しない場合はログのみで表示は更新しない。
        /// 非同期処理が完了するまでの間にユーザーが別エンジンに切り替えた場合、
        /// 残像のような古いメッセージで混乱するのを防ぐ。
        /// </summary>
        private void SetStatusForEngine(TTSEngineType engine, string message)
        {
            if (voiceSynthesisController != null && voiceSynthesisController.CurrentEngine != engine)
            {
                Debug.Log($"[VoiceSettingsPanel] Status suppressed (engine={engine}, current={voiceSynthesisController.CurrentEngine}): {message}");
                return;
            }
            SetStatus(message);
        }

        // ─────────────────────────────────────
        // 内部クラス
        // ─────────────────────────────────────

        /// <summary>
        /// 現在のTTS設定に基づいてクレジット文字列を更新
        /// </summary>
        private void NotifyTTSCreditChanged()
        {
            if (voiceSynthesisController == null) return;

            string speakerName = null;
            string styleName = null;

            if (voiceSynthesisController.CurrentEngine == TTSEngineType.VOICEVOX
                && speakerDropdown != null
                && _speakerEntries.Count > 0)
            {
                int index = speakerDropdown.value;
                if (index >= 0 && index < _speakerEntries.Count)
                {
                    speakerName = _speakerEntries[index].speakerName;
                    styleName = _speakerEntries[index].styleName;
                }
            }

            voiceSynthesisController.UpdateTTSCredit(speakerName, styleName);
        }

        private class SpeakerDropdownEntry
        {
            public string speakerName;
            public string styleName;
            public int styleId;
        }

        // ─────────────────────────────────────
        // 音声入力（Speech to Text）
        // ─────────────────────────────────────

        private void InitializeVoiceInput()
        {
            if (microphoneToggle != null)
            {
                microphoneToggle.onValueChanged.AddListener(OnMicrophoneToggleChanged);
            }

            if (echoPreventionToggle != null)
            {
                echoPreventionToggle.onValueChanged.AddListener(OnEchoPreventionToggleChanged);
            }

            // VoiceInputControllerの状態変更イベントを購読（外部ボタンとの同期用）
            if (voiceInputController != null)
            {
                voiceInputController.OnEnabledChanged += OnVoiceInputEnabledChanged;
            }

            if (voiceInputLanguageDropdown != null)
            {
                voiceInputLanguageDropdown.ClearOptions();
                voiceInputLanguageDropdown.AddOptions(new List<string>
                {
                    "日本語 (ja-JP)",
                    "English (en-US)",
                    "中文 (zh-CN)",
                    "한국어 (ko-KR)"
                });
                voiceInputLanguageDropdown.onValueChanged.AddListener(OnVoiceInputLanguageChanged);
            }

            if (silenceThresholdInputField != null)
            {
                silenceThresholdInputField.onEndEdit.AddListener(OnSilenceThresholdChanged);
            }
        }

        private void LoadVoiceInputSettings()
        {
            if (microphoneToggle != null)
            {
                microphoneToggle.isOn = PlayerPrefs.GetInt(PREF_MIC_ENABLED, 0) == 1;
            }

            if (echoPreventionToggle != null && voiceSynthesisController != null)
            {
                echoPreventionToggle.isOn = voiceSynthesisController.IsEchoPreventionEnabled;
            }

            if (voiceInputLanguageDropdown != null)
            {
                string savedLanguage = PlayerPrefs.GetString(PREF_VOICE_INPUT_LANGUAGE, "ja-JP");
                int index = savedLanguage switch
                {
                    "ja-JP" => 0,
                    "en-US" => 1,
                    "zh-CN" => 2,
                    "ko-KR" => 3,
                    _ => 0
                };
                voiceInputLanguageDropdown.value = index;
            }

            if (silenceThresholdInputField != null)
            {
                float threshold = PlayerPrefs.GetFloat(PREF_SILENCE_THRESHOLD, 2.0f);
                silenceThresholdInputField.text = threshold.ToString("F1");
            }
        }

        private void SaveVoiceInputSettings()
        {
            if (microphoneToggle != null)
            {
                PlayerPrefs.SetInt(PREF_MIC_ENABLED, microphoneToggle.isOn ? 1 : 0);
            }

            if (voiceInputLanguageDropdown != null)
            {
                string languageCode = voiceInputLanguageDropdown.value switch
                {
                    0 => "ja-JP",
                    1 => "en-US",
                    2 => "zh-CN",
                    3 => "ko-KR",
                    _ => "ja-JP"
                };
                PlayerPrefs.SetString(PREF_VOICE_INPUT_LANGUAGE, languageCode);
            }

            if (silenceThresholdInputField != null)
            {
                if (float.TryParse(silenceThresholdInputField.text, out float threshold))
                {
                    PlayerPrefs.SetFloat(PREF_SILENCE_THRESHOLD, threshold);
                }
            }

            PlayerPrefs.Save();
        }

        private void OnMicrophoneToggleChanged(bool isOn)
        {
            if (voiceInputController == null)
            {
                Debug.LogWarning("[VoiceSettingsPanel] VoiceInputController not available");
                return;
            }

            voiceInputController.SetEnabled(isOn);
            SaveVoiceInputSettings();
            Debug.Log($"[VoiceSettingsPanel] Microphone: {(isOn ? "ON" : "OFF")}");
        }

        /// <summary>
        /// VoiceInputControllerの状態変更イベントハンドラ
        /// 外部（マイクボタン等）からの変更をトグルUIに反映
        /// </summary>
        private void OnVoiceInputEnabledChanged(bool isEnabled)
        {
            if (microphoneToggle != null)
            {
                // リスナーを発火させずにUI表示だけ更新
                microphoneToggle.SetIsOnWithoutNotify(isEnabled);
            }

            // 外部ボタンからの変更時もPlayerPrefsに保存
            PlayerPrefs.SetInt(PREF_MIC_ENABLED, isEnabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        private void OnEchoPreventionToggleChanged(bool isOn)
        {
            if (voiceSynthesisController != null)
            {
                voiceSynthesisController.SetEchoPreventionEnabled(isOn);
            }
            Debug.Log($"[VoiceSettingsPanel] Echo prevention: {(isOn ? "ON" : "OFF")}");
        }

        /// <summary>
        /// 起動時に保存済みマイク設定を適用
        /// OnEnable→LoadVoiceInputSettingsでトグルUIは更新されるが、
        /// リスナー登録前のため VoiceInputController.SetEnabled() が呼ばれない問題を修正
        /// </summary>
        private void ApplySavedMicrophoneSetting()
        {
            if (voiceInputController == null) return;

            bool savedMicEnabled = PlayerPrefs.GetInt(PREF_MIC_ENABLED, 0) == 1;
            if (savedMicEnabled)
            {
                voiceInputController.SetEnabled(true);
                Debug.Log("[VoiceSettingsPanel] Applied saved microphone setting: ON");
            }
        }

        private void OnVoiceInputLanguageChanged(int index)
        {
            if (voiceInputController == null) return;

            string languageCode = index switch
            {
                0 => "ja-JP",
                1 => "en-US",
                2 => "zh-CN",
                3 => "ko-KR",
                _ => "ja-JP"
            };

            voiceInputController.SetLanguage(languageCode);
            SaveVoiceInputSettings();
            Debug.Log($"[VoiceSettingsPanel] Voice input language: {languageCode}");
        }

        private void OnSilenceThresholdChanged(string value)
        {
            if (voiceInputController == null) return;

            if (float.TryParse(value, out float threshold))
            {
                threshold = Mathf.Clamp(threshold, 0.5f, 10f);
                voiceInputController.SetSilenceThreshold(threshold);
                silenceThresholdInputField.text = threshold.ToString("F1");
                SaveVoiceInputSettings();
                Debug.Log($"[VoiceSettingsPanel] Silence threshold: {threshold}s");
            }
        }

        private void UpdateVoiceInputStatus()
        {
            if (voiceInputStatusText == null) return;

            if (voiceInputController != null && voiceInputController.IsEnabled)
            {
                voiceInputStatusText.text = "Recording...";
            }
            else
            {
                voiceInputStatusText.text = "Standby";
            }
        }

        // ─────────────────────────────────────
        // Gemini TTS 設定
        // ─────────────────────────────────────

        private void InitializeGeminiTtsSettings()
        {
            // モデルドロップダウン
            if (geminiModelDropdown != null)
            {
                geminiModelDropdown.ClearOptions();
                geminiModelDropdown.AddOptions(new List<string>(GeminiModelNames));
                geminiModelDropdown.onValueChanged.AddListener(OnGeminiModelChanged);
            }

            // ボイスドロップダウン
            if (geminiVoiceDropdown != null)
            {
                geminiVoiceDropdown.ClearOptions();
                geminiVoiceDropdown.AddOptions(new List<string>(GeminiVoiceNames));
                geminiVoiceDropdown.onValueChanged.AddListener(OnGeminiVoiceChanged);
            }

            // APIキー入力
            if (geminiApiKeyInputField != null)
            {
                geminiApiKeyInputField.contentType = TMP_InputField.ContentType.Password;
                geminiApiKeyInputField.onEndEdit.AddListener(OnGeminiApiKeyChanged);
            }

            // スタイルプロンプト入力
            if (geminiStylePromptInputField != null)
            {
                geminiStylePromptInputField.onEndEdit.AddListener(OnGeminiStylePromptChanged);
            }

            // テスト再生ボタン
            if (geminiTtsTestButton != null)
            {
                geminiTtsTestButton.onClick.AddListener(OnGeminiTtsTestPlayClicked);
            }
        }

        private void LoadGeminiTtsSettingsToUI()
        {
            if (geminiTtsClient == null) return;

            if (geminiApiKeyInputField != null)
            {
                geminiApiKeyInputField.text = geminiTtsClient.apiKey ?? "";
            }

            if (geminiModelDropdown != null)
            {
                int idx = System.Array.IndexOf(GeminiModelNames, geminiTtsClient.model);
                geminiModelDropdown.value = idx >= 0 ? idx : 0;
            }

            if (geminiVoiceDropdown != null)
            {
                int idx = System.Array.IndexOf(GeminiVoiceNames, geminiTtsClient.voiceName);
                geminiVoiceDropdown.value = idx >= 0 ? idx : 0;
            }

            if (geminiStylePromptInputField != null)
            {
                geminiStylePromptInputField.text = geminiTtsClient.stylePrompt ?? "";
            }
        }

        /// <summary>
        /// UIの値をGeminiTtsClientに反映（保存前/テスト再生前に呼ぶ）
        /// </summary>
        private void ApplyGeminiUiToClient()
        {
            if (geminiTtsClient == null) return;

            if (geminiApiKeyInputField != null)
                geminiTtsClient.apiKey = geminiApiKeyInputField.text;

            if (geminiModelDropdown != null && geminiModelDropdown.value < GeminiModelNames.Length)
                geminiTtsClient.model = GeminiModelNames[geminiModelDropdown.value];

            if (geminiVoiceDropdown != null && geminiVoiceDropdown.value < GeminiVoiceNames.Length)
                geminiTtsClient.voiceName = GeminiVoiceNames[geminiVoiceDropdown.value];

            if (geminiStylePromptInputField != null)
                geminiTtsClient.stylePrompt = geminiStylePromptInputField.text;
        }

        private void OnGeminiModelChanged(int index)
        {
            if (geminiTtsClient == null || index < 0 || index >= GeminiModelNames.Length) return;
            geminiTtsClient.model = GeminiModelNames[index];
            NotifyTTSCreditChanged();
        }

        private void OnGeminiVoiceChanged(int index)
        {
            if (geminiTtsClient == null || index < 0 || index >= GeminiVoiceNames.Length) return;
            geminiTtsClient.voiceName = GeminiVoiceNames[index];
            NotifyTTSCreditChanged();
        }

        private void OnGeminiApiKeyChanged(string value)
        {
            if (geminiTtsClient == null) return;
            geminiTtsClient.apiKey = value;
        }

        private void OnGeminiStylePromptChanged(string value)
        {
            if (geminiTtsClient == null) return;
            geminiTtsClient.stylePrompt = value;
        }

        private async void OnGeminiTtsTestPlayClicked()
        {
            if (geminiTtsClient == null || testAudioSource == null)
            {
                SetStatusForEngine(TTSEngineType.GeminiTTS, "Error: Missing references");
                return;
            }

            if (testTextInputField == null || string.IsNullOrEmpty(testTextInputField.text))
            {
                SetStatusForEngine(TTSEngineType.GeminiTTS, "Please enter test text");
                return;
            }

            // UIの最新値をクライアントに反映してから合成
            ApplyGeminiUiToClient();

            if (string.IsNullOrEmpty(geminiTtsClient.apiKey))
            {
                SetStatusForEngine(TTSEngineType.GeminiTTS, "Error: Gemini API key is empty");
                return;
            }

            SetStatusForEngine(TTSEngineType.GeminiTTS, "Synthesizing (Gemini TTS)...");

            string text = testTextInputField.text;
            var (clip, _) = await geminiTtsClient.SynthesizeAsync(text);

            if (clip == null)
            {
                SetStatusForEngine(TTSEngineType.GeminiTTS, "Gemini TTS synthesis failed");
                return;
            }

            testAudioSource.clip = clip;
            testAudioSource.Play();

            SetStatusForEngine(TTSEngineType.GeminiTTS, $"Playing... ({clip.length:F1}s)");
            Debug.Log($"[VoiceSettingsPanel] Gemini TTS test playing: {text}");
        }
    }
}
