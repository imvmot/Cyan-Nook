using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace CyanNook.Core
{
    /// <summary>
    /// 全設定のJSON形式エクスポート・インポート
    /// PlayerPrefsの全設定キーを一括管理
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
            new SettingEntry("avatar_vrmFileName", PrefType.String),
            new SettingEntry("avatar_characterPrompt", PrefType.String),
            new SettingEntry("avatar_responseFormat", PrefType.String),
            new SettingEntry("avatar_responseFormatLocked", PrefType.Int),
            new SettingEntry("avatar_boredRate", PrefType.Float),
            new SettingEntry("avatar_boredFactorHappy", PrefType.Float),
            new SettingEntry("avatar_boredFactorRelaxed", PrefType.Float),
            new SettingEntry("avatar_boredFactorAngry", PrefType.Float),
            new SettingEntry("avatar_boredFactorSad", PrefType.Float),
            new SettingEntry("avatar_boredFactorSurprised", PrefType.Float),

            // Camera
            new SettingEntry("camera_height", PrefType.Float),
            new SettingEntry("camera_lookAtEnabled", PrefType.Int),
            new SettingEntry("camera_minFov", PrefType.Float),
            new SettingEntry("camera_maxFov", PrefType.Float),

            // LLM
            new SettingEntry("llm_config", PrefType.String),
            new SettingEntry("llm_useVision", PrefType.Int),
            new SettingEntry("llm_maxHistory", PrefType.Int),
            new SettingEntry("llm_cameraPreview", PrefType.Int),
            new SettingEntry("llm_webCam", PrefType.Int),
            new SettingEntry("llm_screenCapture", PrefType.Int),

            // IdleChat
            new SettingEntry("idleChatEnabled", PrefType.Int),
            new SettingEntry("idleChatCooldown", PrefType.Float),
            new SettingEntry("idleChat_message", PrefType.String),

            // Cron Scheduler
            new SettingEntry("cronSchedulerEnabled", PrefType.Int),
            new SettingEntry("cronAutoReloadInterval", PrefType.Float),

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

            // Voice - Input
            new SettingEntry("voice_micEnabled", PrefType.Int),
            new SettingEntry("voice_inputLanguage", PrefType.String),
            new SettingEntry("voice_silenceThreshold", PrefType.Float),
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

                // カテゴリコメント（JSONにはコメントがないためスキップ、カテゴリ区切りは空行で）
                string category = entry.key.Split('_')[0];
                if (category != currentCategory)
                {
                    currentCategory = category;
                }

                if (!first) sb.AppendLine(",");
                first = false;

                string value = GetValueAsJsonString(entry);
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
            Debug.Log($"[SettingsExporter] Exported to clipboard:\n{json}");
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
#if UNITY_WEBGL && !UNITY_EDITOR
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
                            PlayerPrefs.SetString(entry.key, strVal);
                            count++;
                        }
                        break;
                }
            }

            PlayerPrefs.Save();
            return count;
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
