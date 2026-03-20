using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using CyanNook.Core;
using CyanNook.Character;

namespace CyanNook.Voice
{
    /// <summary>
    /// 音声合成・再生キュー管理
    /// VOICEVOX / Web Speech API の2エンジン対応
    /// ストリーミング応答に対応し、文単位で順次再生
    /// </summary>
    public class VoiceSynthesisController : MonoBehaviour
    {
        [Header("TTS Engine")]
        [Tooltip("使用するTTSエンジン")]
        public TTSEngineType ttsEngineType = TTSEngineType.WebSpeechAPI;

        [Header("References")]
        [Tooltip("VoicevoxClient参照")]
        public VoicevoxClient voicevoxClient;

        [Tooltip("Web Speech Synthesis参照")]
        public WebSpeechSynthesis webSpeechSynthesis;

        [Tooltip("音声再生用AudioSource")]
        public AudioSource audioSource;

        [Tooltip("リップシンクコントローラー")]
        public LipSyncController lipSyncController;

        [Tooltip("音声入力コントローラー（TTS再生中のSTT抑制用）")]
        public VoiceInputController voiceInputController;

        [Header("Settings")]
        [Tooltip("音声合成を有効化")]
        public bool enabled = false;

        [Tooltip("TTS再生中のSTTエコー防止を有効化（ヘッドセット使用時はOFFでよい）")]
        public bool echoPreventionEnabled = true;

        [Tooltip("TTS再生完了後、STT再開までの待機時間（秒）。残響によるエコー検出を防止")]
        public float sttResumeCooldown = 1.0f;

        [Tooltip("ストリーミング時の文区切り文字")]
        public char[] sentenceDelimiters = new char[] { '。', '！', '？', '…', '\n' };

        /// <summary>
        /// TTSクレジット文字列（例: "VOICEVOX:ずんだもん(ノーマル)"）
        /// UIに表示する用途
        /// </summary>
        public string TTSCreditText { get; private set; } = "OFF";

        /// <summary>
        /// TTSクレジット文字列が変更された時に発火
        /// </summary>
        public event System.Action<string> OnTTSCreditChanged;

        // PlayerPrefsキー
        private const string PrefKey_TTSEngine = "voice_ttsEngine";
        private const string PrefKey_TTSEnabled = "voice_ttsEnabled";
        private const string PrefKey_EchoPrevention = "voice_echoPrevention";

        // VOICEVOX用再生キュー（AudioClip + モーラタイムライン）
        private struct VoicevoxQueueEntry
        {
            public AudioClip clip;
            public List<MoraEntry> moraTimeline;
        }
        private Queue<VoicevoxQueueEntry> _voicevoxQueue = new Queue<VoicevoxQueueEntry>();
        private bool _isPlaying = false;

        // ストリーミングバッファ（両エンジン共通）
        private StringBuilder _streamingBuffer = new StringBuilder();

        // ストリーミング中フラグ（STT抑制の解除判定に使用）
        private bool _isStreaming = false;

        // 合成リクエスト未完了カウント（VOICEVOX API呼び出し中の追跡）
        private int _pendingSynthesisCount = 0;

        // STT再開クールダウン用コルーチン
        private Coroutine _sttResumeCoroutine = null;

        // Web Speech APIリップシンク用: 現在の文テキスト
        private string _currentWebSpeechText = "";

        private void Awake()
        {
            LoadTTSEnabledPreference();
            LoadTTSEnginePreference();
            LoadEchoPreventionPreference();
        }

        private void Start()
        {
            // Web Speech API 初期化 + イベント購読
            if (webSpeechSynthesis != null)
            {
                webSpeechSynthesis.LoadSettings();
                webSpeechSynthesis.Initialize();
                webSpeechSynthesis.OnSpeechStartedEvent.AddListener(OnWebSpeechStarted);
                webSpeechSynthesis.OnSpeechEndedEvent.AddListener(OnWebSpeechEnded);
                webSpeechSynthesis.OnQueueEmptyEvent.AddListener(OnWebSpeechQueueEmpty);
            }
        }

        private void OnDestroy()
        {
            if (webSpeechSynthesis != null)
            {
                webSpeechSynthesis.OnSpeechStartedEvent.RemoveListener(OnWebSpeechStarted);
                webSpeechSynthesis.OnSpeechEndedEvent.RemoveListener(OnWebSpeechEnded);
                webSpeechSynthesis.OnQueueEmptyEvent.RemoveListener(OnWebSpeechQueueEmpty);
            }
        }

        private void Update()
        {
            // VOICEVOX時のみキューの自動再生
            if (ttsEngineType == TTSEngineType.VOICEVOX && !_isPlaying && _voicevoxQueue.Count > 0)
            {
                PlayNextVoicevox();
            }
        }

        // ─────────────────────────────────────
        // 公開API（ChatManagerから呼ばれる）
        // ─────────────────────────────────────

        /// <summary>
        /// テキストを即座に音声合成・再生（Blocking Response用）
        /// </summary>
        public async void SynthesizeAndPlay(string text)
        {
            if (!enabled || string.IsNullOrEmpty(text))
            {
                return;
            }

            try
            {
                if (ttsEngineType == TTSEngineType.VOICEVOX)
                {
                    if (voicevoxClient == null) return;

                    var (clip, moraTimeline) = await voicevoxClient.SynthesizeAsync(text);
                    if (clip != null)
                    {
                        _voicevoxQueue.Enqueue(new VoicevoxQueueEntry { clip = clip, moraTimeline = moraTimeline });
                        Debug.Log($"[VoiceSynthesisController] Enqueued VOICEVOX (blocking): {text.Substring(0, Mathf.Min(20, text.Length))}...");
                    }
                }
                else // WebSpeechAPI
                {
                    if (webSpeechSynthesis == null) return;

                    _currentWebSpeechText = text;
                    webSpeechSynthesis.Enqueue(text);
                    Debug.Log($"[VoiceSynthesisController] Enqueued WebSpeech (blocking): {text.Substring(0, Mathf.Min(20, text.Length))}...");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VoiceSynthesisController] SynthesizeAndPlay failed: {ex.Message}");
            }
        }

        /// <summary>
        /// ストリーミング応答の追加（ChatManager.OnResponseStreamingから呼ばれる）
        /// 文が完成したタイミングで音声合成開始
        /// </summary>
        public void OnStreamingTextReceived(string chunk)
        {
            if (!enabled)
            {
                return;
            }

            _isStreaming = true;
            _streamingBuffer.Append(chunk);

            // 文区切りを検出
            string buffer = _streamingBuffer.ToString();
            int lastDelimiterIndex = buffer.LastIndexOfAny(sentenceDelimiters);

            if (lastDelimiterIndex >= 0)
            {
                // 完成した文を抽出
                string completeSentence = buffer.Substring(0, lastDelimiterIndex + 1).Trim();

                if (!string.IsNullOrEmpty(completeSentence))
                {
                    // 音声合成開始
                    _ = SynthesizeAndEnqueue(completeSentence);
                }

                // バッファから削除
                _streamingBuffer.Clear();
                _streamingBuffer.Append(buffer.Substring(lastDelimiterIndex + 1));
            }
        }

        /// <summary>
        /// ストリーミング応答終了時（最後の文を処理）
        /// </summary>
        public void OnStreamingComplete()
        {
            if (!enabled)
            {
                _isStreaming = false;
                return;
            }

            string remaining = _streamingBuffer.ToString().Trim();
            if (!string.IsNullOrEmpty(remaining))
            {
                _ = SynthesizeAndEnqueue(remaining);
            }
            _streamingBuffer.Clear();
            _isStreaming = false;

            TryResumeSTT();
        }

        /// <summary>
        /// 再生を停止してキューをクリア
        /// </summary>
        public void Stop()
        {
            // VOICEVOX停止
            if (audioSource != null)
            {
                audioSource.Stop();
            }
            _voicevoxQueue.Clear();

            // Web Speech API停止
            if (webSpeechSynthesis != null)
            {
                webSpeechSynthesis.Cancel();
            }

            _streamingBuffer.Clear();
            _isPlaying = false;
            _isStreaming = false;
            _pendingSynthesisCount = 0;

            CancelSTTResumeCooldown();

            if (lipSyncController != null)
            {
                lipSyncController.StopLipSync();
            }

            // TTS停止時はSTT抑制を即座に解除（ユーザー操作による停止）
            voiceInputController?.ResumeFromTTS();

            Debug.Log("[VoiceSynthesisController] Stopped");
        }

        /// <summary>
        /// 音声合成の有効/無効を設定
        /// </summary>
        public void SetEnabled(bool enable)
        {
            enabled = enable;
            if (!enabled)
            {
                Stop();
            }
            SaveTTSEnabledPreference();
            Debug.Log($"[VoiceSynthesisController] Enabled: {enabled}");
        }

        /// <summary>
        /// TTSエンジンを切り替え
        /// </summary>
        public void SetTTSEngine(TTSEngineType type)
        {
            if (ttsEngineType == type) return;

            // 現在の再生を停止
            Stop();

            ttsEngineType = type;
            SaveTTSEnginePreference();

            Debug.Log($"[VoiceSynthesisController] TTS Engine changed to: {type}");
        }

        /// <summary>
        /// 現在のTTSエンジン
        /// </summary>
        public TTSEngineType CurrentEngine => ttsEngineType;

        /// <summary>
        /// エコー防止の有効/無効を設定
        /// OFFにするとTTS再生中もSTTを停止しない（ヘッドセット使用時向け）
        /// </summary>
        public void SetEchoPreventionEnabled(bool enable)
        {
            echoPreventionEnabled = enable;
            SaveEchoPreventionPreference();

            // OFF切替時: 抑制中なら即座にSTTを再開
            if (!enable)
            {
                CancelSTTResumeCooldown();
                voiceInputController?.ResumeFromTTS();
            }

            Debug.Log($"[VoiceSynthesisController] Echo prevention: {(enable ? "ON" : "OFF")}");
        }

        /// <summary>
        /// エコー防止が有効かどうか
        /// </summary>
        public bool IsEchoPreventionEnabled => echoPreventionEnabled;

        /// <summary>
        /// TTSクレジット文字列を更新
        /// VoiceSettingsPanelからTTS設定変更時に呼ばれる
        /// </summary>
        public void UpdateTTSCredit(string speakerName = null, string styleName = null)
        {
            if (!enabled)
            {
                TTSCreditText = "OFF";
            }
            else if (ttsEngineType == TTSEngineType.WebSpeechAPI)
            {
                TTSCreditText = "Web Speech API";
            }
            else if (ttsEngineType == TTSEngineType.VOICEVOX)
            {
                if (!string.IsNullOrEmpty(speakerName))
                {
                    TTSCreditText = string.IsNullOrEmpty(styleName)
                        ? $"VOICEVOX:{speakerName}"
                        : $"VOICEVOX:{speakerName}({styleName})";
                }
                else
                {
                    TTSCreditText = "VOICEVOX";
                }
            }

            OnTTSCreditChanged?.Invoke(TTSCreditText);
        }

        // ─────────────────────────────────────
        // 内部: 音声合成キュー追加
        // ─────────────────────────────────────

        /// <summary>
        /// 音声合成してキューに追加（エンジン分岐）
        /// </summary>
        private async Task SynthesizeAndEnqueue(string text)
        {
            if (ttsEngineType == TTSEngineType.VOICEVOX)
            {
                if (voicevoxClient == null) return;

                _pendingSynthesisCount++;
                var (clip, moraTimeline) = await voicevoxClient.SynthesizeAsync(text);
                _pendingSynthesisCount--;

                if (clip != null)
                {
                    _voicevoxQueue.Enqueue(new VoicevoxQueueEntry { clip = clip, moraTimeline = moraTimeline });
                    Debug.Log($"[VoiceSynthesisController] Enqueued VOICEVOX (streaming): {text.Substring(0, Mathf.Min(20, text.Length))}... (Queue: {_voicevoxQueue.Count})");
                }
                else
                {
                    // 合成失敗時: 他に何も残っていなければSTT再開
                    TryResumeSTT();
                }
            }
            else // WebSpeechAPI
            {
                if (webSpeechSynthesis == null) return;

                _currentWebSpeechText = text;
                webSpeechSynthesis.Enqueue(text);
                Debug.Log($"[VoiceSynthesisController] Enqueued WebSpeech (streaming): {text.Substring(0, Mathf.Min(20, text.Length))}...");
            }
        }

        // ─────────────────────────────────────
        // 内部: VOICEVOX再生キュー
        // ─────────────────────────────────────

        /// <summary>
        /// VOICEVOXキューから次の音声を再生
        /// </summary>
        private void PlayNextVoicevox()
        {
            if (_voicevoxQueue.Count == 0 || audioSource == null)
            {
                return;
            }

            var entry = _voicevoxQueue.Dequeue();
            audioSource.clip = entry.clip;
            audioSource.Play();

            _isPlaying = true;

            // エコー防止: クールダウンキャンセル + STT抑制
            if (echoPreventionEnabled)
            {
                CancelSTTResumeCooldown();
                voiceInputController?.SuppressForTTS();
            }

            // モーラリップシンク（データがあれば）、なければAmplitudeフォールバック
            if (lipSyncController != null)
            {
                if (entry.moraTimeline != null && entry.moraTimeline.Count > 0)
                {
                    lipSyncController.StartMoraLipSync(entry.moraTimeline);
                }
                else
                {
                    lipSyncController.StartLipSync(entry.clip);
                }
            }

            // 再生終了を監視
            StartCoroutine(WaitForPlaybackEnd(entry.clip.length));

            Debug.Log($"[VoiceSynthesisController] Playing VOICEVOX ({entry.clip.length:F1}s, Queue: {_voicevoxQueue.Count})");
        }

        /// <summary>
        /// VOICEVOX再生終了を待機
        /// </summary>
        private IEnumerator WaitForPlaybackEnd(float duration)
        {
            yield return new WaitForSeconds(duration);

            _isPlaying = false;

            // リップシンク停止
            if (lipSyncController != null)
            {
                lipSyncController.StopLipSync();
            }

            TryResumeSTT();
        }

        // ─────────────────────────────────────
        // 内部: Web Speech APIイベント
        // ─────────────────────────────────────

        /// <summary>
        /// Web Speech API発話開始 → シミュレーションリップシンク
        /// </summary>
        private void OnWebSpeechStarted()
        {
            if (ttsEngineType != TTSEngineType.WebSpeechAPI) return;

            _isPlaying = true;

            // エコー防止: クールダウンキャンセル + STT抑制
            if (echoPreventionEnabled)
            {
                CancelSTTResumeCooldown();
                voiceInputController?.SuppressForTTS();
            }

            if (lipSyncController != null)
            {
                float estimatedDuration = EstimateSpeechDuration(_currentWebSpeechText);
                lipSyncController.StartSimulatedLipSync(estimatedDuration);
            }
        }

        /// <summary>
        /// Web Speech API発話終了
        /// </summary>
        private void OnWebSpeechEnded()
        {
            if (ttsEngineType != TTSEngineType.WebSpeechAPI) return;

            if (lipSyncController != null)
            {
                lipSyncController.StopLipSync();
            }
        }

        /// <summary>
        /// Web Speech APIキュー空（全発話完了）
        /// </summary>
        private void OnWebSpeechQueueEmpty()
        {
            if (ttsEngineType != TTSEngineType.WebSpeechAPI) return;

            _isPlaying = false;
            TryResumeSTT();
        }

        private void CancelSTTResumeCooldown()
        {
            if (_sttResumeCoroutine != null)
            {
                StopCoroutine(_sttResumeCoroutine);
                _sttResumeCoroutine = null;
            }
        }

        /// <summary>
        /// STT再開条件を一元判定
        /// 再生中でなく、キュー空、合成リクエストなし、ストリーミング完了の全条件を満たした時のみ
        /// クールダウン後にSTTを再開する
        /// </summary>
        private void TryResumeSTT()
        {
            if (!echoPreventionEnabled) return;
            if (_isPlaying) return;
            if (_isStreaming) return;
            if (_pendingSynthesisCount > 0) return;
            if (_voicevoxQueue.Count > 0) return;

            // 既にクールダウン中なら再スケジュールしない
            if (_sttResumeCoroutine != null) return;

            if (sttResumeCooldown > 0f)
            {
                _sttResumeCoroutine = StartCoroutine(ResumeSTTAfterCooldown());
            }
            else
            {
                voiceInputController?.ResumeFromTTS();
            }
        }

        /// <summary>
        /// クールダウン後にSTTを再開（残響によるエコー検出を防止）
        /// クールダウン中にTTSが再開された場合はキャンセルする
        /// </summary>
        private IEnumerator ResumeSTTAfterCooldown()
        {
            yield return new WaitForSeconds(sttResumeCooldown);

            _sttResumeCoroutine = null;

            // クールダウン中にTTSが再開されていないか再チェック
            if (_isPlaying || _isStreaming || _pendingSynthesisCount > 0 || _voicevoxQueue.Count > 0)
            {
                yield break;
            }

            voiceInputController?.ResumeFromTTS();
        }

        /// <summary>
        /// テキストからおおよその発話時間を推定
        /// 日本語: 約6文字/秒（rate=1.0時）
        /// </summary>
        private float EstimateSpeechDuration(string text)
        {
            if (string.IsNullOrEmpty(text)) return 1f;

            float charsPerSecond = 6f;
            float rate = webSpeechSynthesis != null ? webSpeechSynthesis.rate : 1f;
            return Mathf.Max(0.5f, text.Length / (charsPerSecond * rate));
        }

        // ─────────────────────────────────────
        // 設定の読み書き
        // ─────────────────────────────────────

        private void LoadTTSEnabledPreference()
        {
            if (PlayerPrefs.HasKey(PrefKey_TTSEnabled))
            {
                enabled = PlayerPrefs.GetInt(PrefKey_TTSEnabled) == 1;
            }
            Debug.Log($"[VoiceSynthesisController] TTS Enabled: {enabled}");
        }

        private void SaveTTSEnabledPreference()
        {
            PlayerPrefs.SetInt(PrefKey_TTSEnabled, enabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        private void LoadTTSEnginePreference()
        {
            if (PlayerPrefs.HasKey(PrefKey_TTSEngine))
            {
                ttsEngineType = (TTSEngineType)PlayerPrefs.GetInt(PrefKey_TTSEngine);
            }
            Debug.Log($"[VoiceSynthesisController] TTS Engine: {ttsEngineType}");
        }

        private void SaveTTSEnginePreference()
        {
            PlayerPrefs.SetInt(PrefKey_TTSEngine, (int)ttsEngineType);
            PlayerPrefs.Save();
        }

        private void LoadEchoPreventionPreference()
        {
            if (PlayerPrefs.HasKey(PrefKey_EchoPrevention))
            {
                echoPreventionEnabled = PlayerPrefs.GetInt(PrefKey_EchoPrevention) == 1;
            }
            Debug.Log($"[VoiceSynthesisController] Echo prevention: {echoPreventionEnabled}");
        }

        private void SaveEchoPreventionPreference()
        {
            PlayerPrefs.SetInt(PrefKey_EchoPrevention, echoPreventionEnabled ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
