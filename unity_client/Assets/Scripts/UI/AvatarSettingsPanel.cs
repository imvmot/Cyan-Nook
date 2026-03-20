using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using TMPro;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using CyanNook.Character;
using CyanNook.Chat;
using CyanNook.CameraControl;

namespace CyanNook.UI
{
    /// <summary>
    /// アバター設定パネル
    /// VRMモデル選択、アニメーションセット、カメラ高さ、プロンプト編集
    /// </summary>
    public class AvatarSettingsPanel : MonoBehaviour
    {
        // PlayerPrefsキー
        private const string PrefKey_VrmFileName = "avatar_vrmFileName";
        private const string PrefKey_CharacterPrompt = "avatar_characterPrompt";
        private const string PrefKey_ResponseFormat = "avatar_responseFormat";
        private const string PrefKey_ResponseFormatLocked = "avatar_responseFormatLocked";
        private const string PrefKey_BoredRate = "avatar_boredRate";
        private const string PrefKey_BoredFactorHappy = "avatar_boredFactorHappy";
        private const string PrefKey_BoredFactorRelaxed = "avatar_boredFactorRelaxed";
        private const string PrefKey_BoredFactorAngry = "avatar_boredFactorAngry";
        private const string PrefKey_BoredFactorSad = "avatar_boredFactorSad";
        private const string PrefKey_BoredFactorSurprised = "avatar_boredFactorSurprised";

        [Header("References")]
        public CharacterSetup characterSetup;
        public ChatManager chatManager;
        public DynamicCameraController cameraController;
        public BoredomController boredomController;

        [Header("UI - Model")]
        [Tooltip("VRMモデル選択ドロップダウン")]
        public TMP_Dropdown modelDropdown;

        [Header("UI - Animation Set")]
        [Tooltip("アニメーションセット選択ドロップダウン（現在はplaceholder）")]
        public TMP_Dropdown animationSetDropdown;

        [Header("UI - Camera")]
        [Tooltip("カメラ高さ入力")]
        public TMP_InputField cameraHeightInputField;

        [Tooltip("カメラルックアットON/OFF")]
        public Toggle cameraLookAtToggle;

        [Tooltip("最小FOV（近距離時・望遠）")]
        public TMP_InputField minFovInputField;

        [Tooltip("最大FOV（遠距離時・広角）")]
        public TMP_InputField maxFovInputField;

        [Header("UI - Boredom")]
        [Tooltip("退屈ポイント自然増加レート入力（ポイント/分、0で機能OFF）")]
        public TMP_InputField boredRateInputField;

        [Header("UI - Emotion Factors")]
        [Tooltip("happy感情の退屈度係数")]
        public TMP_InputField happyFactorInputField;

        [Tooltip("relaxed感情の退屈度係数")]
        public TMP_InputField relaxedFactorInputField;

        [Tooltip("angry感情の退屈度係数")]
        public TMP_InputField angryFactorInputField;

        [Tooltip("sad感情の退屈度係数")]
        public TMP_InputField sadFactorInputField;

        [Tooltip("surprised感情の増幅係数")]
        public TMP_InputField surprisedFactorInputField;

        [Header("UI - Prompt")]
        [Tooltip("キャラクター設定プロンプト入力")]
        public TMP_InputField characterPromptInputField;

        [Tooltip("レスポンスフォーマットプロンプト入力")]
        public TMP_InputField responseFormatInputField;

        [Tooltip("レスポンスフォーマットのロックトグル")]
        public Toggle responseFormatLockToggle;

        [Header("UI - Buttons")]
        [Tooltip("設定保存ボタン")]
        public Button saveButton;

        [Tooltip("アバター再読み込みボタン")]
        public Button reloadButton;

        [Header("UI - Status")]
        [Tooltip("ステータス表示")]
        public TMP_Text statusText;

        // VRMファイル一覧
        private List<string> _vrmFileNames = new List<string>();

        // マニフェスト読み込み用データクラス
        [System.Serializable]
        private class FileManifest
        {
            public string[] files;
        }

        private void Awake()
        {
            // OnEnable()より先に保存済み設定を復元
            LoadSavedSettings();
        }

        private void OnEnable()
        {
            // パネル表示時に最新の状態を反映
            RefreshVrmFileList();
            LoadSettingsToUI();

            // カメラルックアット状態を反映
            if (cameraLookAtToggle != null && cameraController != null)
            {
                cameraLookAtToggle.isOn = cameraController.IsLookAtEnabled;
            }
        }

        private void Start()
        {

            // ボタンイベント
            if (saveButton != null)
            {
                saveButton.onClick.AddListener(OnSaveClicked);
            }
            if (reloadButton != null)
            {
                reloadButton.onClick.AddListener(OnReloadClicked);
            }

            // カメラ高さ変更
            if (cameraHeightInputField != null)
            {
                cameraHeightInputField.onEndEdit.AddListener(OnCameraHeightChanged);
            }

            // カメラルックアットトグル
            if (cameraLookAtToggle != null)
            {
                cameraLookAtToggle.onValueChanged.AddListener(OnCameraLookAtToggleChanged);
            }

            // FOV設定変更
            if (minFovInputField != null)
            {
                minFovInputField.onEndEdit.AddListener(OnMinFovChanged);
            }

            if (maxFovInputField != null)
            {
                maxFovInputField.onEndEdit.AddListener(OnMaxFovChanged);
            }

            // 退屈ポイントレート変更
            if (boredRateInputField != null)
            {
                boredRateInputField.onEndEdit.AddListener(OnBoredRateChanged);
            }

            // 感情係数変更
            if (happyFactorInputField != null)
                happyFactorInputField.onEndEdit.AddListener((v) => OnEmotionFactorChanged(v, happyFactorInputField, f => boredomController.happyFactor = f));
            if (relaxedFactorInputField != null)
                relaxedFactorInputField.onEndEdit.AddListener((v) => OnEmotionFactorChanged(v, relaxedFactorInputField, f => boredomController.relaxedFactor = f));
            if (angryFactorInputField != null)
                angryFactorInputField.onEndEdit.AddListener((v) => OnEmotionFactorChanged(v, angryFactorInputField, f => boredomController.angryFactor = f));
            if (sadFactorInputField != null)
                sadFactorInputField.onEndEdit.AddListener((v) => OnEmotionFactorChanged(v, sadFactorInputField, f => boredomController.sadFactor = f));
            if (surprisedFactorInputField != null)
                surprisedFactorInputField.onEndEdit.AddListener((v) => OnEmotionFactorChanged(v, surprisedFactorInputField, f => boredomController.surprisedFactor = f));

            // レスポンスフォーマットのロックトグル
            if (responseFormatLockToggle != null)
            {
                responseFormatLockToggle.onValueChanged.AddListener(OnResponseFormatLockChanged);
            }

            // プロンプトのマルチライン対応は MultiLineInputFieldFix コンポーネントで行う

            // アニメーションセット（placeholder）
            if (animationSetDropdown != null)
            {
                animationSetDropdown.ClearOptions();
                animationSetDropdown.AddOptions(new List<string> { "Chr001" });
                animationSetDropdown.interactable = false; // 現在は固定
            }
        }

        private void OnDestroy()
        {
            if (saveButton != null)
                saveButton.onClick.RemoveListener(OnSaveClicked);
            if (reloadButton != null)
                reloadButton.onClick.RemoveListener(OnReloadClicked);
            if (cameraHeightInputField != null)
                cameraHeightInputField.onEndEdit.RemoveListener(OnCameraHeightChanged);
            if (cameraLookAtToggle != null)
                cameraLookAtToggle.onValueChanged.RemoveListener(OnCameraLookAtToggleChanged);
            if (minFovInputField != null)
                minFovInputField.onEndEdit.RemoveListener(OnMinFovChanged);
            if (maxFovInputField != null)
                maxFovInputField.onEndEdit.RemoveListener(OnMaxFovChanged);
            if (boredRateInputField != null)
                boredRateInputField.onEndEdit.RemoveListener(OnBoredRateChanged);
            if (responseFormatLockToggle != null)
                responseFormatLockToggle.onValueChanged.RemoveListener(OnResponseFormatLockChanged);
        }

        // ─────────────────────────────────────
        // VRMファイル一覧
        // ─────────────────────────────────────

        /// <summary>
        /// StreamingAssets/VRM/ からVRMファイル一覧を取得してDropdownに反映
        /// WebGLではマニフェストから非同期読み込み、Editor/Standaloneではローカルファイルシステムから取得
        /// </summary>
        private void RefreshVrmFileList()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            _ = RefreshVrmFileListFromManifestAsync();
#else
            RefreshVrmFileListLocal();
#endif
        }

        /// <summary>
        /// Editor/Standalone: ローカルファイルシステムからVRM一覧を取得
        /// </summary>
        private void RefreshVrmFileListLocal()
        {
            _vrmFileNames.Clear();

            string vrmFolder = Path.Combine(Application.streamingAssetsPath, "VRM");

            if (Directory.Exists(vrmFolder))
            {
                string[] files = Directory.GetFiles(vrmFolder, "*.vrm");
                foreach (string filePath in files)
                {
                    _vrmFileNames.Add(Path.GetFileName(filePath));
                }
            }

            UpdateModelDropdown();
        }

        /// <summary>
        /// WebGL: マニフェストからVRM一覧を非同期取得
        /// </summary>
        private async Task RefreshVrmFileListFromManifestAsync()
        {
            _vrmFileNames.Clear();

            // ドロップダウンに読み込み中表示
            if (modelDropdown != null)
            {
                modelDropdown.ClearOptions();
                modelDropdown.AddOptions(new List<string> { "(Loading...)" });
                modelDropdown.interactable = false;
            }

            string manifestUrl = Path.Combine(Application.streamingAssetsPath, "file_manifest.json");

            try
            {
                using (var request = UnityWebRequest.Get(manifestUrl))
                {
                    var operation = request.SendWebRequest();
                    while (!operation.isDone)
                    {
                        await Task.Yield();
                    }

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        Debug.LogError($"[AvatarSettingsPanel] Failed to load manifest: {request.error}");
                        UpdateModelDropdown();
                        return;
                    }

                    var manifest = JsonUtility.FromJson<FileManifest>(request.downloadHandler.text);
                    if (manifest?.files != null)
                    {
                        foreach (string filePath in manifest.files)
                        {
                            // "VRM/*.vrm" にマッチするエントリのみ
                            if (filePath.StartsWith("VRM/") && filePath.EndsWith(".vrm"))
                            {
                                _vrmFileNames.Add(filePath.Substring("VRM/".Length));
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[AvatarSettingsPanel] Manifest load error: {ex.Message}");
            }

            UpdateModelDropdown();
        }

        /// <summary>
        /// VRMファイル一覧をDropdownに反映（共通処理）
        /// </summary>
        private void UpdateModelDropdown()
        {
            if (modelDropdown == null) return;

            modelDropdown.ClearOptions();

            if (_vrmFileNames.Count > 0)
            {
                modelDropdown.interactable = true;
                modelDropdown.AddOptions(_vrmFileNames);

                // 現在のファイル名に対応するインデックスを選択
                string currentFileName = characterSetup != null ? characterSetup.vrmFileName : "";
                int currentIndex = _vrmFileNames.IndexOf(currentFileName);
                if (currentIndex >= 0)
                {
                    modelDropdown.value = currentIndex;
                }
            }
            else
            {
                modelDropdown.AddOptions(new List<string> { "(No VRM files found)" });
                modelDropdown.interactable = false;
            }
        }

        /// <summary>
        /// 選択中のVRMファイル名を取得
        /// </summary>
        private string GetSelectedVrmFileName()
        {
            if (modelDropdown == null || _vrmFileNames.Count == 0) return "";
            int index = modelDropdown.value;
            return index >= 0 && index < _vrmFileNames.Count ? _vrmFileNames[index] : "";
        }

        // ─────────────────────────────────────
        // 設定の読み書き
        // ─────────────────────────────────────

        /// <summary>
        /// 現在の設定をUIに反映
        /// </summary>
        private void LoadSettingsToUI()
        {
            // カメラ高さ
            if (cameraHeightInputField != null && cameraController != null)
            {
                cameraHeightInputField.text = cameraController.GetCameraHeight().ToString("F2");
            }

            // FOV設定
            if (minFovInputField != null && cameraController != null)
            {
                minFovInputField.text = cameraController.GetMinFov().ToString("F1");
            }

            if (maxFovInputField != null && cameraController != null)
            {
                maxFovInputField.text = cameraController.GetMaxFov().ToString("F1");
            }

            // 退屈ポイントレート
            if (boredRateInputField != null && boredomController != null)
            {
                boredRateInputField.text = boredomController.increaseRate.ToString("F1");
            }

            // 感情係数
            if (boredomController != null)
            {
                if (happyFactorInputField != null)
                    happyFactorInputField.text = boredomController.happyFactor.ToString("F1");
                if (relaxedFactorInputField != null)
                    relaxedFactorInputField.text = boredomController.relaxedFactor.ToString("F1");
                if (angryFactorInputField != null)
                    angryFactorInputField.text = boredomController.angryFactor.ToString("F1");
                if (sadFactorInputField != null)
                    sadFactorInputField.text = boredomController.sadFactor.ToString("F1");
                if (surprisedFactorInputField != null)
                    surprisedFactorInputField.text = boredomController.surprisedFactor.ToString("F1");
            }

            // キャラクター設定プロンプト
            if (characterPromptInputField != null && chatManager != null)
            {
                characterPromptInputField.text = chatManager.characterPrompt ?? "";
            }

            // レスポンスフォーマットプロンプト
            if (responseFormatInputField != null && chatManager != null)
            {
                responseFormatInputField.text = chatManager.responseFormatPrompt ?? "";
            }

            // レスポンスフォーマットのロック状態
            if (responseFormatLockToggle != null)
            {
                bool locked = PlayerPrefs.GetInt(PrefKey_ResponseFormatLocked, 1) == 1;
                responseFormatLockToggle.isOn = locked;
                if (responseFormatInputField != null)
                {
                    responseFormatInputField.interactable = !locked;
                }
            }
        }

        /// <summary>
        /// 保存された設定を復元（起動時）
        /// VRMファイル名、システムプロンプト
        /// ※ カメラ設定は DynamicCameraController が自動復元
        /// </summary>
        private void LoadSavedSettings()
        {
            if (characterSetup == null) return;

            // VRMファイル名
            if (PlayerPrefs.HasKey(PrefKey_VrmFileName))
            {
                string savedFileName = PlayerPrefs.GetString(PrefKey_VrmFileName);
                if (!string.IsNullOrEmpty(savedFileName))
                {
                    characterSetup.vrmFileName = savedFileName;
                    Debug.Log($"[AvatarSettingsPanel] Loaded saved VRM: {savedFileName}");
                }
            }

            // キャラクター設定プロンプト
            if (PlayerPrefs.HasKey(PrefKey_CharacterPrompt) && chatManager != null)
            {
                string saved = PlayerPrefs.GetString(PrefKey_CharacterPrompt);
                if (!string.IsNullOrEmpty(saved))
                {
                    chatManager.characterPrompt = saved;
                    Debug.Log("[AvatarSettingsPanel] Loaded saved character prompt");
                }
            }

            // レスポンスフォーマットプロンプト
            if (PlayerPrefs.HasKey(PrefKey_ResponseFormat) && chatManager != null)
            {
                string saved = PlayerPrefs.GetString(PrefKey_ResponseFormat);
                if (!string.IsNullOrEmpty(saved))
                {
                    chatManager.responseFormatPrompt = saved;
                    Debug.Log("[AvatarSettingsPanel] Loaded saved response format prompt");
                }
            }

            // 退屈ポイントレート
            if (PlayerPrefs.HasKey(PrefKey_BoredRate) && boredomController != null)
            {
                boredomController.increaseRate = PlayerPrefs.GetFloat(PrefKey_BoredRate);
                Debug.Log($"[AvatarSettingsPanel] Loaded saved bored rate: {boredomController.increaseRate}");
            }

            // 感情係数
            if (boredomController != null)
            {
                if (PlayerPrefs.HasKey(PrefKey_BoredFactorHappy))
                    boredomController.happyFactor = PlayerPrefs.GetFloat(PrefKey_BoredFactorHappy);
                if (PlayerPrefs.HasKey(PrefKey_BoredFactorRelaxed))
                    boredomController.relaxedFactor = PlayerPrefs.GetFloat(PrefKey_BoredFactorRelaxed);
                if (PlayerPrefs.HasKey(PrefKey_BoredFactorAngry))
                    boredomController.angryFactor = PlayerPrefs.GetFloat(PrefKey_BoredFactorAngry);
                if (PlayerPrefs.HasKey(PrefKey_BoredFactorSad))
                    boredomController.sadFactor = PlayerPrefs.GetFloat(PrefKey_BoredFactorSad);
                if (PlayerPrefs.HasKey(PrefKey_BoredFactorSurprised))
                    boredomController.surprisedFactor = PlayerPrefs.GetFloat(PrefKey_BoredFactorSurprised);
                Debug.Log("[AvatarSettingsPanel] Loaded saved emotion factors");
            }
        }

        // ─────────────────────────────────────
        // カメラ高さ
        // ─────────────────────────────────────

        private void OnCameraHeightChanged(string value)
        {
            if (cameraController == null) return;

            if (float.TryParse(value, out float height))
            {
                cameraController.SetCameraHeight(height);
                Debug.Log($"[AvatarSettingsPanel] Camera height: {height}m");
            }
        }

        /// <summary>
        /// カメラルックアットトグル変更
        /// </summary>
        private void OnCameraLookAtToggleChanged(bool isOn)
        {
            if (cameraController == null)
            {
                Debug.LogWarning("[AvatarSettingsPanel] CameraController not available");
                return;
            }

            cameraController.SetLookAtEnabled(isOn);
            Debug.Log($"[AvatarSettingsPanel] Camera Look At: {(isOn ? "ON" : "OFF")}");
        }

        /// <summary>
        /// 最小FOV変更
        /// </summary>
        private void OnMinFovChanged(string value)
        {
            if (cameraController == null) return;

            if (float.TryParse(value, out float fov))
            {
                cameraController.SetMinFov(fov);
                // 実際に適用された値（Clamp後）を表示
                minFovInputField.text = cameraController.GetMinFov().ToString("F1");
                Debug.Log($"[AvatarSettingsPanel] Min FOV: {cameraController.GetMinFov()}");
            }
        }

        /// <summary>
        /// 最大FOV変更
        /// </summary>
        private void OnMaxFovChanged(string value)
        {
            if (cameraController == null) return;

            if (float.TryParse(value, out float fov))
            {
                cameraController.SetMaxFov(fov);
                // 実際に適用された値（Clamp後）を表示
                maxFovInputField.text = cameraController.GetMaxFov().ToString("F1");
                Debug.Log($"[AvatarSettingsPanel] Max FOV: {cameraController.GetMaxFov()}");
            }
        }

        // ─────────────────────────────────────
        // 退屈ポイントレート
        // ─────────────────────────────────────

        private void OnBoredRateChanged(string value)
        {
            if (boredomController == null) return;

            if (float.TryParse(value, out float rate))
            {
                rate = Mathf.Clamp(rate, 0f, 10f);
                boredomController.increaseRate = rate;
                boredRateInputField.text = rate.ToString("F1");
                Debug.Log($"[AvatarSettingsPanel] Bored rate: {rate} pt/min");
            }
        }

        // ─────────────────────────────────────
        // 感情係数
        // ─────────────────────────────────────

        private void OnEmotionFactorChanged(string value, TMP_InputField field, System.Action<float> setter)
        {
            if (boredomController == null) return;

            if (float.TryParse(value, out float factor))
            {
                setter(factor);
                field.text = factor.ToString("F1");
                Debug.Log($"[AvatarSettingsPanel] Emotion factor updated: {factor}");
            }
        }

        // ─────────────────────────────────────
        // レスポンスフォーマットロック
        // ─────────────────────────────────────

        private void OnResponseFormatLockChanged(bool isLocked)
        {
            if (responseFormatInputField != null)
            {
                responseFormatInputField.interactable = !isLocked;
            }
        }

        // ─────────────────────────────────────
        // ボタンアクション
        // ─────────────────────────────────────

        /// <summary>
        /// 設定保存
        /// </summary>
        private void OnSaveClicked()
        {
            // VRMファイル名
            string selectedVrm = GetSelectedVrmFileName();
            if (!string.IsNullOrEmpty(selectedVrm))
            {
                PlayerPrefs.SetString(PrefKey_VrmFileName, selectedVrm);
            }

            // カメラ設定（高さ、ルックアット）
            if (cameraController != null)
            {
                cameraController.SaveSettings();
            }

            // キャラクター設定プロンプト
            if (characterPromptInputField != null && chatManager != null)
            {
                chatManager.characterPrompt = characterPromptInputField.text;
                PlayerPrefs.SetString(PrefKey_CharacterPrompt, characterPromptInputField.text);
            }

            // レスポンスフォーマットプロンプト
            if (responseFormatInputField != null && chatManager != null)
            {
                chatManager.responseFormatPrompt = responseFormatInputField.text;
                PlayerPrefs.SetString(PrefKey_ResponseFormat, responseFormatInputField.text);
            }

            // レスポンスフォーマットのロック状態
            if (responseFormatLockToggle != null)
            {
                PlayerPrefs.SetInt(PrefKey_ResponseFormatLocked, responseFormatLockToggle.isOn ? 1 : 0);
            }

            // 退屈ポイントレート
            if (boredomController != null)
            {
                PlayerPrefs.SetFloat(PrefKey_BoredRate, boredomController.increaseRate);
            }

            // 感情係数
            if (boredomController != null)
            {
                PlayerPrefs.SetFloat(PrefKey_BoredFactorHappy, boredomController.happyFactor);
                PlayerPrefs.SetFloat(PrefKey_BoredFactorRelaxed, boredomController.relaxedFactor);
                PlayerPrefs.SetFloat(PrefKey_BoredFactorAngry, boredomController.angryFactor);
                PlayerPrefs.SetFloat(PrefKey_BoredFactorSad, boredomController.sadFactor);
                PlayerPrefs.SetFloat(PrefKey_BoredFactorSurprised, boredomController.surprisedFactor);
            }

            PlayerPrefs.Save();
            SetStatus("Saved!");
            Debug.Log("[AvatarSettingsPanel] Settings saved");
        }

        /// <summary>
        /// アバター再読み込み
        /// </summary>
        private void OnReloadClicked()
        {
            if (characterSetup == null)
            {
                SetStatus("Error: CharacterSetup not found");
                return;
            }

            string selectedVrm = GetSelectedVrmFileName();
            if (string.IsNullOrEmpty(selectedVrm))
            {
                SetStatus("Error: No VRM selected");
                return;
            }

            SetStatus("Reloading...");
            characterSetup.ReloadVrm(selectedVrm);
            SetStatus($"Loaded: {selectedVrm}");
            Debug.Log($"[AvatarSettingsPanel] Reload VRM: {selectedVrm}");
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }
        }
    }
}
