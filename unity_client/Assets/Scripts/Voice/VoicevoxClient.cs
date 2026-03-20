using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CyanNook.Character;

namespace CyanNook.Voice
{
    /// <summary>
    /// VOICEVOX REST API クライアント
    /// ローカルまたはLAN内のVOICEVOX APIサーバーと通信
    /// </summary>
    public class VoicevoxClient : MonoBehaviour
    {
        // PlayerPrefsキー
        private const string PrefKey_ApiUrl = "voice_apiUrl";
        private const string PrefKey_SpeakerId = "voice_speakerId";
        private const string PrefKey_SpeedScale = "voice_speedScale";
        private const string PrefKey_PitchScale = "voice_pitchScale";
        private const string PrefKey_IntonationScale = "voice_intonationScale";

        [Header("API Settings")]
        [Tooltip("VOICEVOX API URL")]
        public string apiUrl = "http://localhost:50021";

        [Header("Voice Parameters")]
        [Tooltip("スピーカー/スタイルID")]
        public int speakerId = 0;

        [Tooltip("話速 (0.5-2.0)")]
        [Range(0.5f, 2.0f)]
        public float speedScale = 1.0f;

        [Tooltip("音高 (-0.15-0.15)")]
        [Range(-0.15f, 0.15f)]
        public float pitchScale = 0.0f;

        [Tooltip("抑揚 (0.0-2.0)")]
        [Range(0.0f, 2.0f)]
        public float intonationScale = 1.0f;

        private void Awake()
        {
            LoadSettings();
        }

        /// <summary>
        /// PlayerPrefsから設定を読み込み
        /// </summary>
        public void LoadSettings()
        {
            if (PlayerPrefs.HasKey(PrefKey_ApiUrl))
            {
                apiUrl = PlayerPrefs.GetString(PrefKey_ApiUrl).TrimEnd('/');
            }

            if (PlayerPrefs.HasKey(PrefKey_SpeakerId))
            {
                speakerId = PlayerPrefs.GetInt(PrefKey_SpeakerId);
            }

            if (PlayerPrefs.HasKey(PrefKey_SpeedScale))
            {
                speedScale = PlayerPrefs.GetFloat(PrefKey_SpeedScale);
            }

            if (PlayerPrefs.HasKey(PrefKey_PitchScale))
            {
                pitchScale = PlayerPrefs.GetFloat(PrefKey_PitchScale);
            }

            if (PlayerPrefs.HasKey(PrefKey_IntonationScale))
            {
                intonationScale = PlayerPrefs.GetFloat(PrefKey_IntonationScale);
            }

            Debug.Log($"[VoicevoxClient] Settings loaded - URL: {apiUrl}, Speaker: {speakerId}");
        }

        /// <summary>
        /// PlayerPrefsに設定を保存
        /// </summary>
        public void SaveSettings()
        {
            // 末尾スラッシュをトリム
            apiUrl = apiUrl.TrimEnd('/');
            PlayerPrefs.SetString(PrefKey_ApiUrl, apiUrl);
            PlayerPrefs.SetInt(PrefKey_SpeakerId, speakerId);
            PlayerPrefs.SetFloat(PrefKey_SpeedScale, speedScale);
            PlayerPrefs.SetFloat(PrefKey_PitchScale, pitchScale);
            PlayerPrefs.SetFloat(PrefKey_IntonationScale, intonationScale);
            PlayerPrefs.Save();

            Debug.Log($"[VoicevoxClient] Settings saved - URL: {apiUrl}, Speaker: {speakerId}");
        }

        /// <summary>
        /// スピーカー一覧を取得
        /// GET /speakers
        /// </summary>
        public async Task<List<VoicevoxSpeaker>> GetSpeakers()
        {
            string url = $"{apiUrl}/speakers";

            Debug.Log($"[VoicevoxClient] Getting speakers from: {url}");

            using (var request = UnityWebRequest.Get(url))
            {
                var operation = request.SendWebRequest();

                while (!operation.isDone)
                {
                    await Task.Yield();
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[VoicevoxClient] GetSpeakers failed: {request.error}");
                    Debug.LogError($"[VoicevoxClient] Response Code: {request.responseCode}");
                    Debug.LogError($"[VoicevoxClient] URL: {url}");
                    if (!string.IsNullOrEmpty(request.downloadHandler?.text))
                    {
                        Debug.LogError($"[VoicevoxClient] Response Body: {request.downloadHandler.text}");
                    }
                    return null;
                }

                try
                {
                    string json = request.downloadHandler.text;
                    Debug.Log($"[VoicevoxClient] Raw JSON (first 1000 chars): {json.Substring(0, Math.Min(1000, json.Length))}");

                    var wrapper = JsonUtility.FromJson<VoicevoxSpeakerListWrapper>($"{{\"speakers\":{json}}}");
                    Debug.Log($"[VoicevoxClient] Loaded {wrapper.speakers.Count} speakers");

                    // 最初のスピーカーの情報をログ出力
                    if (wrapper.speakers.Count > 0)
                    {
                        var firstSpeaker = wrapper.speakers[0];
                        Debug.Log($"[VoicevoxClient] First speaker: {firstSpeaker.name}, styles count: {firstSpeaker.styles?.Count ?? 0}");
                        if (firstSpeaker.styles != null && firstSpeaker.styles.Count > 0)
                        {
                            for (int i = 0; i < Math.Min(3, firstSpeaker.styles.Count); i++)
                            {
                                var style = firstSpeaker.styles[i];
                                Debug.Log($"[VoicevoxClient]   Style[{i}]: name={style.name}, id={style.id}");
                            }
                        }
                    }

                    return wrapper.speakers;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[VoicevoxClient] Failed to parse speakers: {e.Message}");
                    return null;
                }
            }
        }

        /// <summary>
        /// テキストから音声合成（WAVデータ + モーラタイムライン取得）
        /// POST /audio_query → POST /synthesis
        /// </summary>
        public async Task<(AudioClip clip, List<MoraEntry> moraTimeline)> SynthesizeAsync(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                Debug.LogWarning("[VoicevoxClient] Empty text");
                return (null, null);
            }

            Debug.Log($"[VoicevoxClient] SynthesizeAsync called - Text: {text.Substring(0, Math.Min(20, text.Length))}..., SpeakerId: {speakerId}");

            try
            {
                // 1. audio_query 作成（JSON文字列として取得）
                string queryJson = await CreateAudioQueryJson(text);
                if (queryJson == null)
                {
                    return (null, null);
                }

                // 2. モーラタイムライン抽出（パラメータ変更前のJSONから）
                List<MoraEntry> moraTimeline = ExtractMoraTimeline(queryJson);

                // 3. パラメータ変更
                queryJson = ModifyAudioQueryParameters(queryJson);

                // 4. 音声合成
                byte[] wavData = await SynthesizeWav(queryJson);
                if (wavData == null)
                {
                    return (null, null);
                }

                // 5. AudioClip生成
                AudioClip clip = WavUtility.ToAudioClip(wavData);
                if (clip != null)
                {
                    Debug.Log($"[VoicevoxClient] Synthesis success: {text.Substring(0, Math.Min(20, text.Length))}... ({wavData.Length} bytes, {moraTimeline?.Count ?? 0} moras)");
                }

                return (clip, moraTimeline);
            }
            catch (Exception e)
            {
                Debug.LogError($"[VoicevoxClient] SynthesizeAsync exception: {e.Message}");
                return (null, null);
            }
        }

        /// <summary>
        /// AudioQuery JSONからモーラタイムラインを抽出
        /// </summary>
        private List<MoraEntry> ExtractMoraTimeline(string queryJson)
        {
            try
            {
                var audioQuery = JsonUtility.FromJson<AudioQuery>(queryJson);
                if (audioQuery?.accent_phrases == null)
                {
                    return null;
                }

                var timeline = new List<MoraEntry>();
                float currentTime = audioQuery.prePhonemeLength;

                foreach (var phrase in audioQuery.accent_phrases)
                {
                    if (phrase.moras != null)
                    {
                        foreach (var mora in phrase.moras)
                        {
                            float consonantLen = mora.consonant_length > 0 ? mora.consonant_length : 0f;
                            float vowelLen = mora.vowel_length > 0 ? mora.vowel_length : 0f;
                            float totalDuration = consonantLen + vowelLen;

                            timeline.Add(new MoraEntry
                            {
                                startTime = currentTime,
                                duration = totalDuration,
                                vowel = mora.vowel ?? "a"
                            });

                            currentTime += totalDuration;
                        }
                    }

                    // ポーズモーラ（句読点間のポーズ）
                    if (phrase.pause_mora != null)
                    {
                        float pauseLen = phrase.pause_mora.vowel_length > 0 ? phrase.pause_mora.vowel_length : 0f;

                        timeline.Add(new MoraEntry
                        {
                            startTime = currentTime,
                            duration = pauseLen,
                            vowel = "pau"
                        });

                        currentTime += pauseLen;
                    }
                }

                Debug.Log($"[VoicevoxClient] Extracted {timeline.Count} mora entries, total: {currentTime:F2}s");
                return timeline;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VoicevoxClient] Failed to extract mora timeline: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// 接続テスト
        /// GET /version
        /// </summary>
        public async Task<bool> TestConnection()
        {
            string url = $"{apiUrl}/version";

            using (var request = UnityWebRequest.Get(url))
            {
                var operation = request.SendWebRequest();

                while (!operation.isDone)
                {
                    await Task.Yield();
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[VoicevoxClient] Connection test failed: {request.error}");
                    return false;
                }

                Debug.Log($"[VoicevoxClient] Connection OK - Version: {request.downloadHandler.text}");
                return true;
            }
        }

        /// <summary>
        /// audio_query作成（JSON文字列として取得）
        /// </summary>
        private async Task<string> CreateAudioQueryJson(string text)
        {
            string url = $"{apiUrl}/audio_query?text={UnityWebRequest.EscapeURL(text)}&speaker={speakerId}";

            Debug.Log($"[VoicevoxClient] Creating audio_query: {url}");

            using (var request = UnityWebRequest.PostWwwForm(url, ""))
            {
                var operation = request.SendWebRequest();

                while (!operation.isDone)
                {
                    await Task.Yield();
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[VoicevoxClient] audio_query failed: {request.error}");
                    Debug.LogError($"[VoicevoxClient] Response Code: {request.responseCode}");
                    Debug.LogError($"[VoicevoxClient] URL: {url}");
                    if (!string.IsNullOrEmpty(request.downloadHandler?.text))
                    {
                        Debug.LogError($"[VoicevoxClient] Response Body: {request.downloadHandler.text}");
                    }
                    return null;
                }

                string json = request.downloadHandler.text;
                Debug.Log($"[VoicevoxClient] audio_query response length: {json.Length} chars");

                return json;
            }
        }

        /// <summary>
        /// audio_query JSON内のパラメータを書き換え（正規表現による文字列操作）
        /// JsonUtilityの再シリアライズを避けるため、文字列置換で対応
        /// </summary>
        private string ModifyAudioQueryParameters(string queryJson)
        {
            try
            {
                // 正規表現で各パラメータを検索・置換
                // "speedScale":数値 の形式を探して置換
                queryJson = System.Text.RegularExpressions.Regex.Replace(
                    queryJson,
                    @"""speedScale"":\s*[\d.]+",
                    $"\"speedScale\":{speedScale.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

                queryJson = System.Text.RegularExpressions.Regex.Replace(
                    queryJson,
                    @"""pitchScale"":\s*-?[\d.]+",
                    $"\"pitchScale\":{pitchScale.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

                queryJson = System.Text.RegularExpressions.Regex.Replace(
                    queryJson,
                    @"""intonationScale"":\s*[\d.]+",
                    $"\"intonationScale\":{intonationScale.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

                Debug.Log($"[VoicevoxClient] Modified parameters - speed: {speedScale}, pitch: {pitchScale}, intonation: {intonationScale}");

                return queryJson;
            }
            catch (Exception e)
            {
                Debug.LogError($"[VoicevoxClient] Failed to modify parameters: {e.Message}");
                // 失敗時は元のJSONをそのまま返す
                return queryJson;
            }
        }

        /// <summary>
        /// 音声合成実行（JSON文字列を直接送信）
        /// </summary>
        private async Task<byte[]> SynthesizeWav(string queryJson)
        {
            string url = $"{apiUrl}/synthesis?speaker={speakerId}";
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(queryJson);

            Debug.Log($"[VoicevoxClient] Synthesis request to: {url}");
            Debug.Log($"[VoicevoxClient] Request body length: {bodyRaw.Length} bytes");

            using (var request = new UnityWebRequest(url, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");

                var operation = request.SendWebRequest();

                while (!operation.isDone)
                {
                    await Task.Yield();
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[VoicevoxClient] synthesis failed: {request.error}");
                    Debug.LogError($"[VoicevoxClient] Response Code: {request.responseCode}");
                    Debug.LogError($"[VoicevoxClient] URL: {url}");
                    Debug.LogError($"[VoicevoxClient] Request Body (first 500 chars): {queryJson.Substring(0, Math.Min(500, queryJson.Length))}");
                    if (!string.IsNullOrEmpty(request.downloadHandler?.text))
                    {
                        Debug.LogError($"[VoicevoxClient] Response Body: {request.downloadHandler.text}");
                    }
                    return null;
                }

                return request.downloadHandler.data;
            }
        }
    }

    // ─────────────────────────────────────
    // データクラス
    // ─────────────────────────────────────

    [System.Serializable]
    public class VoicevoxSpeaker
    {
        public string name;
        public string speaker_uuid;
        public List<VoicevoxStyle> styles;
        public string version;
    }

    [System.Serializable]
    public class VoicevoxStyle
    {
        public string name;
        public int id;
    }

    [System.Serializable]
    public class VoicevoxSpeakerListWrapper
    {
        public List<VoicevoxSpeaker> speakers;
    }

    [System.Serializable]
    public class AudioQuery
    {
        public List<AccentPhrase> accent_phrases;
        public float speedScale = 1.0f;
        public float pitchScale = 0.0f;
        public float intonationScale = 1.0f;
        public float volumeScale = 1.0f;
        public float prePhonemeLength = 0.1f;
        public float postPhonemeLength = 0.1f;
        public int outputSamplingRate = 24000;
        public bool outputStereo = false;
        public string kana;
    }

    [System.Serializable]
    public class AccentPhrase
    {
        public List<Mora> moras;
        public int accent;
        public Mora pause_mora;
        public bool is_interrogative;
    }

    [System.Serializable]
    public class Mora
    {
        public string text;
        public string consonant;
        public float consonant_length;
        public string vowel;
        public float vowel_length;
        public float pitch;
    }
}
