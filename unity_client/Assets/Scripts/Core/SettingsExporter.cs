using System;
using System.Runtime.InteropServices;
using UnityEngine;
using CyanNook.Chat;

namespace CyanNook.Core
{
    /// <summary>
    /// 全設定のJSON形式エクスポート・インポート
    /// PlayerPrefsの全設定キーを一括管理
    /// APIキー（llm_config内のapiKey / gemini_tts_apiKey）はエクスポートに含めない
    /// （設定ファイル共有時の漏洩防止。インポート時はローカルの既存キーを引き継ぐ）
    /// </summary>
    public class SettingsExporter : MonoBehaviour
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void FileIO_Download(string filename, string content);

        [DllImport("__Internal")]
        private static extern void FileIO_OpenFileDialog(string callbackObjectName, string callbackMethodName, string accept);
#endif

        /// <summary>インポート完了時のコールバック</summary>
        public event Action<bool, string> OnImportComplete;

        // ===================================================================
        // 設定キー定義
        // ===================================================================

        /// <summary>設定エントリの型</summary>
        private enum PrefType { Int, Float, String }

        /// <summary>個別の設定エントリ</summary>
        [Serializable]
        private struct SettingEntry
        {
            public string key;
            public PrefType type;

            public SettingEntry(string key, PrefType type)
            {
                this.key = key;
                this.type = type;
            }
        }

        /// <summary>全設定キーの定義（カテゴリ順）</summary>
        private static readonly SettingEntry[] AllSettings = new[]
        {
            // Avatar
            new SettingEntry(SettingsKeys.VrmFileName, PrefType.String),
            new SettingEntry(SettingsKeys.CharacterPrompt, PrefType.String),
            new SettingEntry(SettingsKeys.ResponseFormat, PrefType.String),
            new SettingEntry("avatar_responseFormatLocked", PrefType.Int),
            new SettingEntry(SettingsKeys.BoredRate, PrefType.Float),
            new SettingEntry(SettingsKeys.BoredFactorHappy, PrefType.Float),
            new SettingEntry(SettingsKeys.BoredFactorRelaxed, PrefType.Float),
            new SettingEntry(SettingsKeys.BoredFactorAngry, PrefType.Float),
            new SettingEntry(SettingsKeys.BoredFactorSad, PrefType.Float),
            new SettingEntry(SettingsKeys.BoredFactorSurprised, PrefType.Float),

            // Camera
            new SettingEntry("camera_height", PrefType.Float),
            new SettingEntry("camera_lookAtEnabled", PrefType.Int),
            new SettingEntry("camera_minFov", PrefType.Float),
            new SettingEntry("camera_maxFov", PrefType.Float),

            // LLM
            new SettingEntry("llm_config", PrefType.String),
            new SettingEntry(SettingsKeys.UseVision, PrefType.Int),
            new SettingEntry(SettingsKeys.MaxHistory, PrefType.Int),
            new SettingEntry(SettingsKeys.CameraPreview, PrefType.Int),
            new SettingEntry(SettingsKeys.WebCam, PrefType.Int),
            new SettingEntry(SettingsKeys.ScreenCapture, PrefType.Int),

            // 定期実行マスター（IdleChat/SleepChat/Outing 一括ON/OFF）
            new SettingEntry("periodic_enabled", PrefType.Int),

            // IdleChat
            // idleChatEnabled は periodic_enabled に統合済みだが、
            // 旧エクスポートファイルのインポート互換（移行フォールバック）のため残す
            new SettingEntry("idleChatEnabled", PrefType.Int),
            new SettingEntry("idleChatCooldown", PrefType.Float),
            new SettingEntry(SettingsKeys.IdleChatMessage, PrefType.String),

            // Cron Scheduler
            new SettingEntry("cronSchedulerEnabled", PrefType.Int),
            new SettingEntry("cronAutoReloadInterval", PrefType.Float),

            // External Action Feed（外部アクションフィード / 受動制御）
            new SettingEntry("feed_enabled", PrefType.Int),
            new SettingEntry("feed_actionUrl", PrefType.String),
            new SettingEntry("feed_subscribeInterval", PrefType.Float),
            new SettingEntry("feed_contextUrl", PrefType.String),
            new SettingEntry("feed_cameraUrl", PrefType.String),
            new SettingEntry("feed_publishInterval", PrefType.Float),
            new SettingEntry("feed_voiceEnabled", PrefType.Int),
            new SettingEntry("feed_voiceUrl", PrefType.String),
            new SettingEntry("feed_thinkingVoiceEnabled", PrefType.Int),
            new SettingEntry("feed_thinkingVoicePitch", PrefType.Float),

            // Sleep
            new SettingEntry("sleep_defaultDuration", PrefType.Int),
            new SettingEntry("sleep_minDuration", PrefType.Int),
            new SettingEntry("sleep_maxDuration", PrefType.Int),
            new SettingEntry("sleep_dreamInterval", PrefType.Float),
            new SettingEntry("sleep_dreamMessage", PrefType.String),
            new SettingEntry("sleep_wakeUpMessage", PrefType.String),

            // Outing
            new SettingEntry("outing_messageInterval", PrefType.Float),
            new SettingEntry("outing_promptMessage", PrefType.String),
            new SettingEntry("outing_entryPromptMessage", PrefType.String),

            // Voice - TTS
            new SettingEntry("voice_ttsEnabled", PrefType.Int),
            new SettingEntry("voice_ttsEngine", PrefType.Int),
            new SettingEntry("voice_echoPrevention", PrefType.Int),

            // Voice - WebSpeech
            new SettingEntry("voice_webSpeechVoiceURI", PrefType.String),
            new SettingEntry("voice_webSpeechRate", PrefType.Float),
            new SettingEntry("voice_webSpeechPitch", PrefType.Float),

            // Voice - VOICEVOX
            new SettingEntry("voice_apiUrl", PrefType.String),
            new SettingEntry("voice_speakerId", PrefType.Int),
            new SettingEntry("voice_speedScale", PrefType.Float),
            new SettingEntry("voice_pitchScale", PrefType.Float),
            new SettingEntry("voice_intonationScale", PrefType.Float),

            // Voice - Gemini TTS
            new SettingEntry("gemini_tts_apiKey", PrefType.String),
            new SettingEntry("gemini_tts_model", PrefType.String),
            new SettingEntry("gemini_tts_voiceName", PrefType.String),
            new SettingEntry("gemini_tts_stylePrompt", PrefType.String),

            // Voice - Input
            new SettingEntry(SettingsKeys.MicEnabled, PrefType.Int),
            new SettingEntry("voice_inputLanguage", PrefType.String),
            new SettingEntry("voice_silenceThreshold", PrefType.Float),

            // UI
            new SettingEntry("ui_locale", PrefType.String),
        };

        // ===================================================================
        // エクスポート
        // ===================================================================

        /// <summary>
        /// 全設定をJSON文字列にエクスポート
        /// </summary>
        public string ExportToJson()
        {
            // 手動でJSON構築（整形済み）
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("{");

            bool first = true;
            string currentCategory = null;

            foreach (var entry in AllSettings)
            {
                if (!PlayerPrefs.HasKey(entry.key)) continue;

                // APIキーはエクスポートしない（ファイル共有時の漏洩防止）
                if (entry.key == "gemini_tts_apiKey") continue;

                // カテゴリコメント（JSONにはコメントがないためスキップ、カテゴリ区切りは空行で）
                string category = entry.key.Split('_')[0];
                if (category != currentCategory)
                {
                    currentCategory = category;
                }

                string value = entry.key == "llm_config"
                    ? EscapeJsonString(StripApiKeyFromLlmConfig(PlayerPrefs.GetString(entry.key)))
                    : GetValueAsJsonString(entry);

                if (!first) sb.AppendLine(",");
                first = false;

                sb.Append($"  \"{entry.key}\": {value}");
            }

            sb.AppendLine();
            sb.AppendLine("}");

            return sb.ToString();
        }

        /// <summary>
        /// エクスポートしてブラウザでダウンロード
        /// </summary>
        public void ExportAndDownload()
        {
            string json = ExportToJson();
            string filename = $"cyan_nook_settings_{DateTime.Now:yyyyMMdd_HHmmss}.json";

#if UNITY_WEBGL && !UNITY_EDITOR
            FileIO_Download(filename, json);
            Debug.Log($"[SettingsExporter] Export triggered: {filename}");
#else
            // エディタ/スタンドアロン: クリップボードにコピー
            GUIUtility.systemCopyBuffer = json;
            // JSON全文はキャラクタープロンプト等の個人設定を含むためログに出さない
            Debug.Log($"[SettingsExporter] Exported to clipboard ({json.Length} chars)");
#endif
        }

        // ===================================================================
        // インポート
        // ===================================================================

        /// <summary>
        /// ブラウザのファイル選択ダイアログを開いてインポート
        /// </summary>
        public void OpenImportDialog()
        {
#if UNITYROOM_BUILD
            // unityroom版（体験版）ではImportを封鎖。
            // 設定ファイル経由で封鎖済み機能（feed/cron/webcam等）の
            // PlayerPrefsを書き戻す抜け道になるため（Exportは残す）
            Debug.LogWarning("[SettingsExporter] Import is disabled in unityroom build");
            OnImportComplete?.Invoke(false, "Import is disabled in this build");
#elif UNITY_WEBGL && !UNITY_EDITOR
            FileIO_OpenFileDialog(gameObject.name, "OnFileImported", ".json");
            Debug.Log("[SettingsExporter] Import dialog opened");
#else
            // エディタ/スタンドアロン: クリップボードから読み込み
            string clipboard = GUIUtility.systemCopyBuffer;
            if (!string.IsNullOrEmpty(clipboard))
            {
                OnFileImported(clipboard);
            }
            else
            {
                Debug.LogWarning("[SettingsExporter] Clipboard is empty");
                OnImportComplete?.Invoke(false, "Clipboard is empty");
            }
#endif
        }

        /// <summary>
        /// ファイル読み込み完了コールバック（WebGLのjslibから呼ばれる）
        /// </summary>
        public void OnFileImported(string jsonContent)
        {
#if UNITYROOM_BUILD
            // ダイアログ側だけでなく実行主体側も封鎖
            // （WebGLのSendMessage経由で直接呼び出せる public コールバックのため）
            Debug.LogWarning("[SettingsExporter] Import is disabled in unityroom build");
            OnImportComplete?.Invoke(false, "Import is disabled in this build");
#else
            if (string.IsNullOrEmpty(jsonContent))
            {
                Debug.LogWarning("[SettingsExporter] Import cancelled or empty file");
                OnImportComplete?.Invoke(false, "Empty file");
                return;
            }

            try
            {
                int count = ImportFromJson(jsonContent);
                string message = $"Imported {count} settings";
                Debug.Log($"[SettingsExporter] {message}");
                OnImportComplete?.Invoke(true, message);
            }
            catch (Exception e)
            {
                string message = $"Import failed: {e.Message}";
                Debug.LogError($"[SettingsExporter] {message}");
                OnImportComplete?.Invoke(false, message);
            }
#endif
        }

        /// <summary>
        /// JSON文字列から設定をインポート
        /// </summary>
        /// <returns>インポートしたキーの数</returns>
        public int ImportFromJson(string json)
        {
            int count = 0;

            // シンプルなJSONパーサー（トップレベルのkey:valueのみ対応）
            // Unity標準のJsonUtilityは動的キーに対応しないため手動パース
            foreach (var entry in AllSettings)
            {
                string value = ExtractJsonValue(json, entry.key);
                if (value == null) continue;

                switch (entry.type)
                {
                    case PrefType.Int:
                        if (int.TryParse(value, out int intVal))
                        {
                            PlayerPrefs.SetInt(entry.key, intVal);
                            count++;
                        }
                        break;

                    case PrefType.Float:
                        if (float.TryParse(value, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float floatVal))
                        {
                            PlayerPrefs.SetFloat(entry.key, floatVal);
                            count++;
                        }
                        break;

                    case PrefType.String:
                        string strVal = UnescapeJsonString(value);
                        if (strVal != null)
                        {
                            // エクスポートファイルにはAPIキーが含まれない（漏洩防止で除外）ため、
                            // 空キーでローカルの既存キーを消さないよう引き継ぐ
                            if (entry.key == "llm_config")
                            {
                                strVal = MergeExistingApiKeyIntoLlmConfig(strVal);
                                if (strVal == null) break; // パース不能 → 既存設定を保護して書き込まない
                            }
                            else if (entry.key == "gemini_tts_apiKey" && string.IsNullOrEmpty(strVal))
                            {
                                break;
                            }

                            PlayerPrefs.SetString(entry.key, strVal);
                            count++;
                        }
                        break;
                }
            }

            // 旧バージョンのエクスポート（idleChatEnabledのみ、periodic_enabledなし）の移行:
            // ローカルに既存のperiodic_enabledが残っているとPeriodicExecutionSettingsの
            // フォールバックが効かず旧設定のON/OFFが無視されるため、明示的に引き継ぐ
            if (ExtractJsonValue(json, "periodic_enabled") == null)
            {
                string legacyEnabled = ExtractJsonValue(json, "idleChatEnabled");
                if (legacyEnabled != null && int.TryParse(legacyEnabled, out int legacyVal))
                {
                    PlayerPrefs.SetInt("periodic_enabled", legacyVal);
                }
            }

            PlayerPrefs.Save();
            return count;
        }

        // ===================================================================
        // APIキー除外・引き継ぎ
        // ===================================================================

        /// <summary>
        /// llm_config JSONからapiKeyを除去する（エクスポート用）
        /// パースできない場合は安全側に倒して内容ごと出力しない（nullを返す）
        /// </summary>
        private static string StripApiKeyFromLlmConfig(string configJson)
        {
            if (string.IsNullOrEmpty(configJson)) return null;

            try
            {
                var config = JsonUtility.FromJson<LLMConfig>(configJson);
                if (config == null) return null;
                config.apiKey = "";
                return JsonUtility.ToJson(config);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// インポートしたllm_configのapiKeyが空の場合、ローカルに保存済みの
        /// 既存キーを引き継ぐ（新形式エクスポートはキーを含まないため）
        /// apiType/apiEndpointの両方が一致する場合のみ引き継ぐ。
        /// エンドポイント一致を要求するのは、細工された設定ファイル
        /// （同一apiType + 攻撃者のエンドポイント）でローカルキーが
        /// 攻撃者サーバーへ送信されるのを防ぐため。
        /// パース不能なllm_configはnullを返し、書き込み自体をスキップさせる
        /// （既存の正常な設定とキーを壊れたデータで上書きしない）
        /// </summary>
        private static string MergeExistingApiKeyIntoLlmConfig(string importedJson)
        {
            try
            {
                var imported = JsonUtility.FromJson<LLMConfig>(importedJson);
                if (imported == null) return null;
                if (!string.IsNullOrEmpty(imported.apiKey)) return importedJson;

                string existingJson = PlayerPrefs.GetString("llm_config", "");
                if (string.IsNullOrEmpty(existingJson)) return importedJson;

                var existing = JsonUtility.FromJson<LLMConfig>(existingJson);
                if (existing == null || string.IsNullOrEmpty(existing.apiKey)) return importedJson;
                if (existing.apiType != imported.apiType) return importedJson;
                if (existing.apiEndpoint != imported.apiEndpoint) return importedJson;

                imported.apiKey = existing.apiKey;
                return JsonUtility.ToJson(imported);
            }
            catch
            {
                return null;
            }
        }

        // ===================================================================
        // JSON ヘルパー
        // ===================================================================

        private string GetValueAsJsonString(SettingEntry entry)
        {
            switch (entry.type)
            {
                case PrefType.Int:
                    return PlayerPrefs.GetInt(entry.key).ToString();

                case PrefType.Float:
                    return PlayerPrefs.GetFloat(entry.key).ToString(
                        System.Globalization.CultureInfo.InvariantCulture);

                case PrefType.String:
                    return EscapeJsonString(PlayerPrefs.GetString(entry.key));

                default:
                    return "null";
            }
        }

        /// <summary>文字列をJSONエスケープ（ダブルクォート付き）</summary>
        private static string EscapeJsonString(string str)
        {
            if (str == null) return "null";

            var sb = new System.Text.StringBuilder();
            sb.Append('"');
            foreach (char c in str)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.Append($"\\u{(int)c:X4}");
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        /// <summary>JSONから指定キーの値を抽出</summary>
        private static string ExtractJsonValue(string json, string key)
        {
            // "key": value のパターンを検索
            string pattern = $"\"{key}\"";
            int keyIndex = json.IndexOf(pattern, StringComparison.Ordinal);
            if (keyIndex < 0) return null;

            // コロンを探す
            int colonIndex = json.IndexOf(':', keyIndex + pattern.Length);
            if (colonIndex < 0) return null;

            // 値の開始位置（空白スキップ）
            int valueStart = colonIndex + 1;
            while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart]))
                valueStart++;

            if (valueStart >= json.Length) return null;

            // 文字列値
            if (json[valueStart] == '"')
            {
                return ExtractQuotedString(json, valueStart);
            }

            // 数値/null/bool
            int valueEnd = valueStart;
            while (valueEnd < json.Length && json[valueEnd] != ',' && json[valueEnd] != '}'
                && json[valueEnd] != '\n' && json[valueEnd] != '\r')
                valueEnd++;

            return json.Substring(valueStart, valueEnd - valueStart).Trim();
        }

        /// <summary>クォート付き文字列を抽出（エスケープ対応、クォート含む）</summary>
        private static string ExtractQuotedString(string json, int startIndex)
        {
            if (json[startIndex] != '"') return null;

            var sb = new System.Text.StringBuilder();
            sb.Append('"');
            int i = startIndex + 1;
            while (i < json.Length)
            {
                char c = json[i];
                if (c == '\\' && i + 1 < json.Length)
                {
                    sb.Append(c);
                    sb.Append(json[i + 1]);
                    i += 2;
                    continue;
                }
                if (c == '"')
                {
                    sb.Append('"');
                    return sb.ToString();
                }
                sb.Append(c);
                i++;
            }
            return null;
        }

        /// <summary>JSONエスケープされた文字列をアンエスケープ（クォート除去）</summary>
        private static string UnescapeJsonString(string jsonStr)
        {
            if (jsonStr == null || jsonStr == "null") return null;
            if (jsonStr.Length < 2 || jsonStr[0] != '"' || jsonStr[jsonStr.Length - 1] != '"')
                return jsonStr; // クォートなし = そのまま返す

            var sb = new System.Text.StringBuilder();
            int i = 1; // 先頭クォートスキップ
            int end = jsonStr.Length - 1; // 末尾クォートスキップ
            while (i < end)
            {
                char c = jsonStr[i];
                if (c == '\\' && i + 1 < end)
                {
                    char next = jsonStr[i + 1];
                    switch (next)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 5 < end)
                            {
                                string hex = jsonStr.Substring(i + 2, 4);
                                if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
                                    null, out int codePoint))
                                {
                                    sb.Append((char)codePoint);
                                    i += 4; // +2 for \u already handled below
                                }
                            }
                            break;
                        default: sb.Append(next); break;
                    }
                    i += 2;
                }
                else
                {
                    sb.Append(c);
                    i++;
                }
            }
            return sb.ToString();
        }
    }
}
