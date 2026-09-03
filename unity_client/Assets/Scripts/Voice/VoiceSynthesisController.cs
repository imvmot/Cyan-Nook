using System;
using UnityEngine;
using UnityEngine.Serialization;
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
    /// VOICEVOX / Gemini TTS / Web Speech API の3エンジン対応
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

        [Tooltip("GeminiTtsClient参照")]
        public GeminiTtsClient geminiTtsClient;

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
        // 旧名 enabled は MonoBehaviour.enabled を隠蔽していた（CS0108）ためリネーム
        [FormerlySerializedAs("enabled")]
        public bool ttsEnabled = false;

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

        // AudioClip再生バッファ（VOICEVOX / Gemini TTS 共通）
        // moraTimeline は VOICEVOX のみ提供、Gemini TTS は常に null
        // clip == null は「合成失敗した文」のプレースホルダー（再生せず読み飛ばす）
        private struct AudioClipQueueEntry
        {
            public AudioClip clip;
            public List<MoraEntry> moraTimeline;
        }

        // 順序保証バッファ: 合成は文ごとに並列実行されるため、短い文が長い文を
        // 追い越して先に完成することがある（合成完了順に再生すると文の順番が
        // 入れ替わる）。合成依頼時に連番を振り、再生は必ず連番順に消費する。
        // 次に再生すべき番号がまだ合成中なら、後の番号が完成していても待つ
        private Dictionary<int, AudioClipQueueEntry> _orderedClipBuffer = new Dictionary<int, AudioClipQueueEntry>();
        private int _nextSynthesisSequence = 0;
        private int _nextPlaybackSequence = 0;

        /// <summary>
        /// 現在のエンジンがAudioClipベース（VOICEVOX / Gemini TTS）かどうか
        /// </summary>
        private bool IsAudioClipBasedEngine =>
            ttsEngineType == TTSEngineType.VOICEVOX || ttsEngineType == TTSEngineType.GeminiTTS;
        private bool _isPlaying = false;

        // ストリーミングバッファ（両エンジン共通）
        private StringBuilder _streamingBuffer = new StringBuilder();

        // ストリーミング中フラグ（STT抑制の解除判定に使用）
        private bool _isStreaming = false;

        // 合成リクエスト未完了カウント（VOICEVOX API呼び出し中の追跡）
        private int _pendingSynthesisCount = 0;

        // STT再開クールダウン用コルーチン
        private Coroutine _sttResumeCoroutine = null;

        // AudioClip再生終了待ちコルーチン。
        // Stop()で止めないと、停止後に旧クリップの残り時間で発火して
        // _isPlaying=false化（次再生と重なる）・リップシンク停止・STT再開が
        // 新しい再生中に走ってしまう
        private Coroutine _playbackWaitCoroutine = null;

        // 停止世代カウンタ。Stop()で進める。
        // 進行中の合成（await SynthesizeAsync）はStop()ではキャンセルできないため、
        // await後に世代が変わっていたら結果を捨てる（停止済み音声の後追い再生と
        // _pendingSynthesisCountの負値化を防ぐ）
        private int _stopGeneration = 0;

        // Web Speech APIリップシンク用: 現在の文テキスト
        private string _currentWebSpeechText = "";

        // 再生中の外部供給クリップ（PlayExternalClip）。
        // AudioClipはランタイム生成物でGC回収されないため、再生完了・停止・
        // 差し替え時に明示的にDestroyする（放置すると受信のたびにメモリが積み上がる）
        private AudioClip _currentExternalClip;

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
            // AudioClipベースエンジン（VOICEVOX / Gemini TTS）はバッファを自動再生
            if (!IsAudioClipBasedEngine) return;

            // 合成失敗した文（clip==nullプレースホルダー）は再生せず読み飛ばし、
            // 後続の文が永久に待たされないよう連番を進める
            bool skipped = false;
            while (_orderedClipBuffer.TryGetValue(_nextPlaybackSequence, out var entry) && entry.clip == null)
            {
                _orderedClipBuffer.Remove(_nextPlaybackSequence);
                _nextPlaybackSequence++;
                skipped = true;
            }
            if (skipped)
            {
                TryResumeSTT();
            }

            // 次に再生すべき番号の文が完成していれば再生（後の番号が先に完成していても待つ）
            if (!_isPlaying && _orderedClipBuffer.ContainsKey(_nextPlaybackSequence))
            {
                PlayNextAudioClip();
            }
        }

        // ─────────────────────────────────────
        // 公開API（ChatManagerから呼ばれる）
        // ─────────────────────────────────────

        /// <summary>
        /// テキストを即座に音声合成・再生（Blocking Response用）
        /// 実処理はストリーミングと共通のSynthesizeAndEnqueueに委譲する
        /// （これにより_pendingSynthesisCountの追跡もストリーミングと揃い、
        /// 合成中のSTT再開判定が正しく効く）
        /// </summary>
        public void SynthesizeAndPlay(string text)
        {
            if (!ttsEnabled || string.IsNullOrEmpty(text))
            {
                return;
            }

            _ = SynthesizeAndEnqueue(text);
        }

        /// <summary>
        /// ストリーミング応答の追加（ChatManager.OnResponseStreamingから呼ばれる）
        /// 文が完成したタイミングで音声合成開始
        /// </summary>
        public void OnStreamingTextReceived(string chunk)
        {
            if (!ttsEnabled)
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
                    Debug.Log("[PERF] TTS sentence queued");
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
            if (!ttsEnabled)
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
            StopInternal(resumeStt: true);
        }

        /// <summary>
        /// 停止の実体。resumeStt=falseは「直後に別の再生を始める」場合用で、
        /// STTの再開→即抑制の空振り（ブラウザSpeechRecognitionの0ms start/stop）を避ける
        /// </summary>
        private void StopInternal(bool resumeStt)
        {
            // VOICEVOX停止
            if (audioSource != null)
            {
                audioSource.Stop();
            }

            // 外部供給クリップの破棄（AudioSourceから外してから）
            DestroyExternalClipIfAny();
            _orderedClipBuffer.Clear();
            _nextSynthesisSequence = 0;
            _nextPlaybackSequence = 0;

            // Web Speech API停止
            if (webSpeechSynthesis != null)
            {
                webSpeechSynthesis.Cancel();
            }

            _streamingBuffer.Clear();
            _isPlaying = false;
            _isStreaming = false;
            _pendingSynthesisCount = 0;
            _stopGeneration++;

            CancelPlaybackWait();
            CancelSTTResumeCooldown();

            if (lipSyncController != null)
            {
                lipSyncController.StopLipSync();
            }

            // TTS停止時はSTT抑制を即座に解除（ユーザー操作による停止）
            if (resumeStt)
            {
                voiceInputController?.ResumeFromTTS();
            }

            Debug.Log("[VoiceSynthesisController] Stopped");
        }

        /// <summary>
        /// 外部供給クリップが残っていれば破棄する（AudioSourceへの割り当ても外す）
        /// </summary>
        private void DestroyExternalClipIfAny()
        {
            if (_currentExternalClip == null) return;

            if (audioSource != null && audioSource.clip == _currentExternalClip)
            {
                audioSource.clip = null;
            }
            Destroy(_currentExternalClip);
            _currentExternalClip = null;
        }

        /// <summary>
        /// 外部から供給された合成済みAudioClipを再生する（外部アクションフィードのvoice.wav用）。
        /// ttsEnabled（自前合成の有効/無効）とは独立に動作する。
        /// 進行中の合成・再生は打ち切り、新しいクリップを優先する
        /// </summary>
        public void PlayExternalClip(AudioClip clip)
        {
            if (clip == null || audioSource == null) return;

            // 進行中の合成・再生・キューを打ち切る（前回の外部クリップもここで破棄される。
            // 直後に再生を始めるためSTTは再開しない）
            StopInternal(resumeStt: false);

            _currentExternalClip = clip;
            audioSource.clip = clip;
            audioSource.Play();
            _isPlaying = true;

            if (echoPreventionEnabled)
            {
                CancelSTTResumeCooldown();
                voiceInputController?.SuppressForTTS();
            }

            // 外部wavはモーラ情報を持たないためAmplitude（波形振幅）リップシンク
            if (lipSyncController != null)
            {
                lipSyncController.StartLipSync(clip);
            }

            CancelPlaybackWait();
            _playbackWaitCoroutine = StartCoroutine(WaitForPlaybackEnd(clip.length));

            Debug.Log($"[VoiceSynthesisController] Playing external clip ({clip.length:F1}s)");
        }

        /// <summary>
        /// 音声合成の有効/無効を設定
        /// </summary>
        public void SetEnabled(bool enable)
        {
            ttsEnabled = enable;
            if (!ttsEnabled)
            {
                Stop();
            }
            SaveTTSEnabledPreference();
            Debug.Log($"[VoiceSynthesisController] Enabled: {ttsEnabled}");
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
            if (!ttsEnabled)
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
            else if (ttsEngineType == TTSEngineType.GeminiTTS)
            {
                string voiceName = geminiTtsClient != null ? geminiTtsClient.voiceName : null;
                TTSCreditText = string.IsNullOrEmpty(voiceName)
                    ? "Gemini TTS"
                    : $"Gemini TTS:{voiceName}";
            }

            OnTTSCreditChanged?.Invoke(TTSCreditText);
        }

        // ─────────────────────────────────────
        // 内部: 音声合成キュー追加
        // ─────────────────────────────────────

        /// <summary>
        /// 音声合成してキューに追加（エンジン分岐、Blocking/ストリーミング共通）
        /// 呼び出し元はTaskを破棄する（_ = ...）ため、例外はここで処理する
        /// </summary>
        private async Task SynthesizeAndEnqueue(string text)
        {
            int gen = _stopGeneration;
            bool counted = false;
            // 再生順の連番。awaitより前（呼び出し順が保たれる同期区間）で採番することが重要
            int seq = -1;

            try
            {
                if (ttsEngineType == TTSEngineType.VOICEVOX)
                {
                    if (voicevoxClient == null) return;

                    seq = _nextSynthesisSequence++;
                    _pendingSynthesisCount++;
                    counted = true;
                    var (clip, moraTimeline) = await voicevoxClient.SynthesizeAsync(text);

                    // Stop()済み: 結果を捨てる（カウンタ・連番はStopでリセット済みのため触らない）
                    if (gen != _stopGeneration) return;
                    _pendingSynthesisCount--;
                    counted = false;

                    // 合成失敗（clip==null）でもプレースホルダーを登録して連番の穴を空けない
                    // （穴が空くと後続の文が永久に再生されない）。読み飛ばしはUpdateで行う
                    _orderedClipBuffer[seq] = new AudioClipQueueEntry { clip = clip, moraTimeline = moraTimeline };

                    if (clip != null)
                    {
                        Debug.Log($"[VoiceSynthesisController] Enqueued VOICEVOX: {text.Substring(0, Mathf.Min(20, text.Length))}... (seq={seq}, Buffered: {_orderedClipBuffer.Count})");
                    }
                }
                else if (ttsEngineType == TTSEngineType.GeminiTTS)
                {
                    if (geminiTtsClient == null) return;

                    seq = _nextSynthesisSequence++;
                    _pendingSynthesisCount++;
                    counted = true;
                    var (clip, _) = await geminiTtsClient.SynthesizeAsync(text);

                    if (gen != _stopGeneration) return;
                    _pendingSynthesisCount--;
                    counted = false;

                    _orderedClipBuffer[seq] = new AudioClipQueueEntry { clip = clip, moraTimeline = null };

                    if (clip != null)
                    {
                        Debug.Log($"[VoiceSynthesisController] Enqueued Gemini TTS: {text.Substring(0, Mathf.Min(20, text.Length))}... (seq={seq}, Buffered: {_orderedClipBuffer.Count})");
                    }
                }
                else // WebSpeechAPI
                {
                    if (webSpeechSynthesis == null) return;

                    _currentWebSpeechText = text;
                    webSpeechSynthesis.Enqueue(text);
                    Debug.Log($"[VoiceSynthesisController] Enqueued WebSpeech: {text.Substring(0, Mathf.Min(20, text.Length))}...");
                }
            }
            catch (Exception ex)
            {
                // 例外時のカウンタ回収（Stop()済みなら0リセット済みのため触らない）
                if (counted && gen == _stopGeneration)
                {
                    _pendingSynthesisCount--;
                }

                // 採番済み・現世代・未登録ならプレースホルダーで連番の穴を塞ぐ
                // （countedの状態に依存させず、どの経路で例外が起きても穴を残さない）
                if (seq >= 0 && gen == _stopGeneration && !_orderedClipBuffer.ContainsKey(seq))
                {
                    _orderedClipBuffer[seq] = new AudioClipQueueEntry { clip = null, moraTimeline = null };
                }
                Debug.LogError($"[VoiceSynthesisController] Synthesis failed: {ex.Message}");

                // 他に何も残っていなければSTTを再開する
                TryResumeSTT();
            }
        }

        // ─────────────────────────────────────
        // 内部: AudioClip再生キュー（VOICEVOX / Gemini TTS 共通）
        // ─────────────────────────────────────

        /// <summary>
        /// 順序保証バッファから次の連番のAudioClipを再生（VOICEVOX / Gemini TTS）
        /// </summary>
        private void PlayNextAudioClip()
        {
            if (audioSource == null ||
                !_orderedClipBuffer.TryGetValue(_nextPlaybackSequence, out var entry) ||
                entry.clip == null)
            {
                return;
            }

            _orderedClipBuffer.Remove(_nextPlaybackSequence);
            _nextPlaybackSequence++;

            audioSource.clip = entry.clip;
            audioSource.Play();

            _isPlaying = true;

            // エコー防止: クールダウンキャンセル + STT抑制
            if (echoPreventionEnabled)
            {
                CancelSTTResumeCooldown();
                voiceInputController?.SuppressForTTS();
            }

            // モーラリップシンク（VOICEVOX）、なければAmplitudeフォールバック（Gemini TTS）
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

            // 再生終了を監視（前の監視が残っていれば止めてから）
            CancelPlaybackWait();
            _playbackWaitCoroutine = StartCoroutine(WaitForPlaybackEnd(entry.clip.length));

            Debug.Log($"[VoiceSynthesisController] Playing AudioClip ({entry.clip.length:F1}s, Buffered: {_orderedClipBuffer.Count}, engine={ttsEngineType})");
        }

        /// <summary>
        /// AudioClip再生終了を待機
        /// </summary>
        private IEnumerator WaitForPlaybackEnd(float duration)
        {
            yield return new WaitForSeconds(duration);

            _playbackWaitCoroutine = null;
            _isPlaying = false;

            // 外部供給クリップの再生完了ならここで破棄（合成クリップの場合はnullでno-op）
            DestroyExternalClipIfAny();

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

        private void CancelPlaybackWait()
        {
            if (_playbackWaitCoroutine != null)
            {
                StopCoroutine(_playbackWaitCoroutine);
                _playbackWaitCoroutine = null;
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
            if (_orderedClipBuffer.Count > 0) return;

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
            if (_isPlaying || _isStreaming || _pendingSynthesisCount > 0 || _orderedClipBuffer.Count > 0)
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
                ttsEnabled = PlayerPrefs.GetInt(PrefKey_TTSEnabled) == 1;
            }
            Debug.Log($"[VoiceSynthesisController] TTS Enabled: {ttsEnabled}");
        }

        private void SaveTTSEnabledPreference()
        {
            PlayerPrefs.SetInt(PrefKey_TTSEnabled, ttsEnabled ? 1 : 0);
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
