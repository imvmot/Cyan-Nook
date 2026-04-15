using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CyanNook.Character;

namespace CyanNook.Voice
{
    /// <summary>
    /// Gemini TTS API クライアント（プロトタイプ）
    ///
    /// VOICEVOX が使えない環境向けの代替TTS。Google の Gemini TTS API を使用し、
    /// base64エンコードされた raw PCM16（24kHz / mono）を受け取って AudioClip に変換する。
    ///
    /// 注意事項:
    /// - モーラタイムラインは返されないため、LipSyncはAmplitudeMode/TextOnlyModeのみ
    /// - APIキーはWebGLビルドで実行時露出する（ユーザー自己責任）
    /// - モデルは 2026年4月時点で preview。仕様変更リスクあり
    ///
    /// エンドポイント:
    /// POST https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent
    /// Header: x-goog-api-key: {API_KEY}
    /// </summary>
    public class GeminiTtsClient : MonoBehaviour
    {
        // PlayerPrefsキー
        private const string PrefKey_ApiKey = "gemini_tts_apiKey";
        private const string PrefKey_Model = "gemini_tts_model";
        private const string PrefKey_VoiceName = "gemini_tts_voiceName";
        private const string PrefKey_StylePrompt = "gemini_tts_stylePrompt";

        [Header("API Settings")]
        [Tooltip("Gemini API キー（AI Studio で取得）")]
        public string apiKey = "";

        [Tooltip("使用モデル")]
        public string model = "gemini-2.5-flash-preview-tts";

        [Header("Voice Parameters")]
        [Tooltip("プリセットボイス名（30種類から選択: Kore, Puck, Charon, Zephyr 等）")]
        public string voiceName = "Kore";

        [Tooltip("スタイル指示プロンプト。空欄時はそのまま読み上げ。例: 'Say cheerfully and warmly: '")]
        [TextArea(1, 3)]
        public string stylePrompt = "";

        // Gemini TTS の音声フォーマット（固定値）
        private const int SampleRate = 24000;
        private const int ChannelCount = 1;

        private void Awake()
        {
            LoadSettings();
        }

        public void LoadSettings()
        {
            if (PlayerPrefs.HasKey(PrefKey_ApiKey))
                apiKey = PlayerPrefs.GetString(PrefKey_ApiKey);
            if (PlayerPrefs.HasKey(PrefKey_Model))
                model = PlayerPrefs.GetString(PrefKey_Model);
            if (PlayerPrefs.HasKey(PrefKey_VoiceName))
                voiceName = PlayerPrefs.GetString(PrefKey_VoiceName);
            if (PlayerPrefs.HasKey(PrefKey_StylePrompt))
                stylePrompt = PlayerPrefs.GetString(PrefKey_StylePrompt);

            Debug.Log($"[GeminiTtsClient] Settings loaded - Model: {model}, Voice: {voiceName}");
        }

        public void SaveSettings()
        {
            PlayerPrefs.SetString(PrefKey_ApiKey, apiKey ?? "");
            PlayerPrefs.SetString(PrefKey_Model, model ?? "");
            PlayerPrefs.SetString(PrefKey_VoiceName, voiceName ?? "");
            PlayerPrefs.SetString(PrefKey_StylePrompt, stylePrompt ?? "");
            PlayerPrefs.Save();
            Debug.Log($"[GeminiTtsClient] Settings saved - Model: {model}, Voice: {voiceName}");
        }

        /// <summary>
        /// テキストから音声合成。戻り値は VoicevoxClient と同じシグネチャだが
        /// moraTimeline は常に null（Gemini TTS はモーラ情報を返さない）。
        /// </summary>
        public async Task<(AudioClip clip, List<MoraEntry> moraTimeline)> SynthesizeAsync(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                Debug.LogWarning("[GeminiTtsClient] Empty text");
                return (null, null);
            }

            if (string.IsNullOrEmpty(apiKey))
            {
                Debug.LogError("[GeminiTtsClient] API key is empty");
                return (null, null);
            }

            Debug.Log($"[GeminiTtsClient] SynthesizeAsync - Text: {text.Substring(0, Math.Min(20, text.Length))}..., Voice: {voiceName}");
            Debug.Log("[PERF] Gemini TTS synth start");

            try
            {
                byte[] pcmData = await RequestSynthesis(text);
                if (pcmData == null || pcmData.Length == 0)
                {
                    return (null, null);
                }

                AudioClip clip = PcmToAudioClip(pcmData, "GeminiTtsClip");
                if (clip != null)
                {
                    Debug.Log($"[GeminiTtsClient] Synthesis success: {pcmData.Length} bytes → {clip.length:F2}s");
                }

                Debug.Log("[PERF] Gemini TTS synth complete");
                return (clip, null);
            }
            catch (Exception e)
            {
                Debug.LogError($"[GeminiTtsClient] SynthesizeAsync exception: {e.Message}\n{e.StackTrace}");
                return (null, null);
            }
        }

        /// <summary>
        /// Gemini TTS API にリクエストを送信し、生のPCM16バイト列を取得する。
        /// </summary>
        private async Task<byte[]> RequestSynthesis(string text)
        {
            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";

            // スタイルプロンプトがあればテキスト先頭に付与
            string promptText = string.IsNullOrEmpty(stylePrompt)
                ? text
                : $"{stylePrompt}{text}";

            // リクエストボディ構築（JsonUtilityでは入れ子が扱いにくいので手組み）
            string requestBody = BuildRequestJson(promptText, voiceName);

            Debug.Log($"[GeminiTtsClient] Request URL: {url}");
            Debug.Log($"[GeminiTtsClient] Request body length: {requestBody.Length} chars");

            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(requestBody);

            using (var request = new UnityWebRequest(url, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("x-goog-api-key", apiKey);

                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    await Task.Yield();
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[GeminiTtsClient] Request failed: {request.error}");
                    Debug.LogError($"[GeminiTtsClient] Response Code: {request.responseCode}");
                    if (!string.IsNullOrEmpty(request.downloadHandler?.text))
                    {
                        // 応答本文の先頭のみ（APIキー等の露出防止）
                        string body = request.downloadHandler.text;
                        Debug.LogError($"[GeminiTtsClient] Response Body: {body.Substring(0, Math.Min(1000, body.Length))}");
                    }
                    return null;
                }

                string responseJson = request.downloadHandler.text;

                // base64エンコードされたPCMを抽出
                string base64 = ExtractInlineDataBase64(responseJson);
                if (string.IsNullOrEmpty(base64))
                {
                    Debug.LogError("[GeminiTtsClient] Failed to extract inlineData.data from response");
                    Debug.LogError($"[GeminiTtsClient] Response (first 500): {responseJson.Substring(0, Math.Min(500, responseJson.Length))}");
                    return null;
                }

                try
                {
                    return Convert.FromBase64String(base64);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[GeminiTtsClient] Base64 decode failed: {e.Message}");
                    return null;
                }
            }
        }

        /// <summary>
        /// Gemini TTS API のリクエストJSON文字列を生成。
        /// JsonUtility は入れ子・配列の扱いが苦手なので、エスケープだけ適切にして手組みする。
        /// </summary>
        private static string BuildRequestJson(string text, string voiceName)
        {
            string escapedText = EscapeJsonString(text);
            string escapedVoice = EscapeJsonString(voiceName);

            return
                "{" +
                    "\"contents\":[{" +
                        "\"parts\":[{\"text\":\"" + escapedText + "\"}]" +
                    "}]," +
                    "\"generationConfig\":{" +
                        "\"responseModalities\":[\"AUDIO\"]," +
                        "\"speechConfig\":{" +
                            "\"voiceConfig\":{" +
                                "\"prebuiltVoiceConfig\":{" +
                                    "\"voiceName\":\"" + escapedVoice + "\"" +
                                "}" +
                            "}" +
                        "}" +
                    "}" +
                "}";
        }

        /// <summary>
        /// JSON文字列のエスケープ（", \, 改行、タブ、制御文字）
        /// </summary>
        private static string EscapeJsonString(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            var sb = new System.Text.StringBuilder(input.Length + 16);
            foreach (char c in input)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.AppendFormat("\\u{0:x4}", (int)c);
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// レスポンスJSONから candidates[0].content.parts[0].inlineData.data を抽出。
        /// JsonUtility の入れ子対応が弱いため、文字列検索で値を取り出す。
        /// レスポンス内の data フィールドは複数ある可能性があるため、inlineData直下のものを狙う。
        /// </summary>
        private static string ExtractInlineDataBase64(string responseJson)
        {
            // "inlineData":{...,"data":"..."} の data を抽出
            const string marker = "\"inlineData\"";
            int markerIdx = responseJson.IndexOf(marker, StringComparison.Ordinal);
            if (markerIdx < 0) return null;

            int dataKeyIdx = responseJson.IndexOf("\"data\"", markerIdx, StringComparison.Ordinal);
            if (dataKeyIdx < 0) return null;

            int colonIdx = responseJson.IndexOf(':', dataKeyIdx);
            if (colonIdx < 0) return null;

            int quoteStart = responseJson.IndexOf('"', colonIdx + 1);
            if (quoteStart < 0) return null;

            // 終端クォート（エスケープ考慮は不要、base64は \ を含まない）
            int quoteEnd = responseJson.IndexOf('"', quoteStart + 1);
            if (quoteEnd < 0) return null;

            return responseJson.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
        }

        /// <summary>
        /// raw PCM16 (little-endian, mono, 24kHz) を AudioClip に変換。
        /// VOICEVOX は WAV ヘッダ付きだが Gemini TTS はヘッダなしの生PCMなので、
        /// 直接 Int16 として解釈して float に正規化する。
        /// </summary>
        private static AudioClip PcmToAudioClip(byte[] pcmData, string clipName)
        {
            if (pcmData == null || pcmData.Length < 2)
            {
                Debug.LogError("[GeminiTtsClient] PCM data too small");
                return null;
            }

            int sampleCount = pcmData.Length / 2; // 16bit = 2bytes per sample
            float[] samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                short sample = (short)(pcmData[i * 2] | (pcmData[i * 2 + 1] << 8));
                samples[i] = sample / 32768f;
            }

            AudioClip clip = AudioClip.Create(clipName, sampleCount, ChannelCount, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        // ─────────────────────────────────────
        // Editor/Debug 用テストエントリ
        // ─────────────────────────────────────

        [Header("Debug")]
        [TextArea(2, 4)]
        public string testText = "こんにちは、テスト音声です。";

        [ContextMenu("Test Synthesize")]
        public async void TestSynthesize()
        {
            var (clip, _) = await SynthesizeAsync(testText);
            if (clip == null)
            {
                Debug.LogError("[GeminiTtsClient] Test synthesize failed");
                return;
            }

            // シーンに一時AudioSourceを作って再生
            var go = new GameObject("GeminiTtsTestPlayer");
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.Play();
            Destroy(go, clip.length + 0.5f);

            Debug.Log($"[GeminiTtsClient] Playing test clip ({clip.length:F2}s)");
        }
    }
}
