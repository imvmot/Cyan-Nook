using System.Collections.Generic;
using UnityEngine;
using UniVRM10;

namespace CyanNook.Character
{
    /// <summary>
    /// モーラタイムラインの1エントリ
    /// VOICEVOX AudioQueryから生成
    /// </summary>
    public struct MoraEntry
    {
        /// <summary>累積開始時間（秒）</summary>
        public float startTime;
        /// <summary>モーラの長さ（consonant_length + vowel_length）</summary>
        public float duration;
        /// <summary>母音: a, i, u, e, o, N, cl, pau</summary>
        public string vowel;
    }

    /// <summary>
    /// VRM 1.0 Expressionベースのリップシンク統合コントローラ
    ///
    /// 4モード:
    /// - Mora: VOICEVOXモーラタイムラインに基づく正確な母音同期
    /// - Simulated: Web Speech API用テキスト長推定ベース口パク
    /// - TextOnly: テキスト表示のみ（音声なし）の簡易口パク
    /// - Amplitude: AudioSource振幅ベース（フォールバック）
    ///
    /// TTS有効時はTextOnlyモードが自動抑制される。
    /// </summary>
    public class LipSyncController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("VRMインスタンス")]
        public Vrm10Instance vrmInstance;

        [Tooltip("音声再生AudioSource")]
        public AudioSource audioSource;

        [Header("Settings")]
        [Tooltip("リップシンクの強度")]
        [Range(0f, 1f)]
        public float intensity = 0.8f;

        [Tooltip("口の動きの速度")]
        public float speed = 10f;

        [Tooltip("振幅のしきい値（これ以下は口を閉じる）")]
        public float amplitudeThreshold = 0.01f;

        [Tooltip("母音切り替え確率（Amplitudeモード用）")]
        [Range(0f, 1f)]
        public float vowelSwitchProbability = 0.1f;

        [Header("Simulated Lip Sync")]
        [Tooltip("シミュレーション時の1モーラの秒数")]
        public float simulatedMoraSpeed = 0.12f;

        // VRM Expression名（VRM 1.0 標準）
        private const string EXPR_AA = "aa";  // 「あ」
        private const string EXPR_IH = "ih";  // 「い」
        private const string EXPR_OU = "ou";  // 「う」
        private const string EXPR_EE = "ee";  // 「え」
        private const string EXPR_OH = "oh";  // 「お」

        // リップシンクモード
        private enum LipSyncMode
        {
            Amplitude,   // AudioSource振幅ベース（従来方式）
            Mora,        // VOICEVOXモーラタイムライン
            Simulated,   // Web Speech API用シミュレーション
            TextOnly     // テキスト表示のみ（音声なし簡易口パク）
        }

        private bool _isSyncing = false;
        private LipSyncMode _mode = LipSyncMode.Amplitude;
        private float _currentWeight = 0f;
        private string _currentVowel = EXPR_AA;

        // TTS有効時はTextOnlyモードを抑制
        private bool _ttsActive;

        // 母音サイクル（Amplitude/Simulated用）
        private readonly string[] _vowels = { EXPR_AA, EXPR_IH, EXPR_OU, EXPR_EE, EXPR_OH };
        private int _vowelIndex = 0;

        // Moraモード用
        private List<MoraEntry> _moraTimeline;
        private float _moraTimer = 0f;
        private int _moraIndex = 0;

        // Simulatedモード用
        private float _simulatedTimer = 0f;
        private float _simulatedDuration = 0f;
        private float _simulatedMoraTimer = 0f;

        // TextOnlyモード用（テキスト表示のみの簡易口パク）
        private float _textOnlyTimer = 0f;
        private float _textOnlyDuration = 0f;
        private float _textOnlyMoraTimer = 0f;

        // ─────────────────────────────────────
        // 公開メソッド
        // ─────────────────────────────────────

        /// <summary>
        /// VRM Instanceを設定（VRM読み込み時に呼び出し）
        /// </summary>
        public void SetVrmInstance(Vrm10Instance instance)
        {
            vrmInstance = instance;
        }

        /// <summary>
        /// テキスト口パクを開始（音声なし・テキスト表示のみ用）
        /// TTS有効時は自動抑制される
        /// </summary>
        public void StartSpeaking(string text)
        {
            if (_ttsActive || vrmInstance == null) return;
            if (string.IsNullOrEmpty(text)) return;

            _mode = LipSyncMode.TextOnly;
            _textOnlyDuration = text.Length * simulatedMoraSpeed;
            _textOnlyTimer = 0f;
            _textOnlyMoraTimer = 0f;
            _vowelIndex = 0;
            _isSyncing = true;

            Debug.Log($"[LipSyncController] StartSpeaking (TextOnly): {text.Length} chars, duration={_textOnlyDuration:F2}s");
        }

        /// <summary>
        /// テキスト口パクを停止
        /// </summary>
        public void StopSpeaking()
        {
            if (!_isSyncing || _mode != LipSyncMode.TextOnly) return;

            _isSyncing = false;
            _currentWeight = 0f;
            ResetMouth();
            Debug.Log("[LipSyncController] StopSpeaking (TextOnly)");
        }

        /// <summary>口パク中かどうか（TextOnlyモード）</summary>
        public bool IsSpeaking => _isSyncing && _mode == LipSyncMode.TextOnly;

        /// <summary>
        /// TTS有効状態を設定
        /// TTS有効時はTextOnlyモードを停止し、新規開始も抑制する
        /// </summary>
        public void SetTtsActive(bool active)
        {
            _ttsActive = active;
            if (active && _isSyncing && _mode == LipSyncMode.TextOnly)
            {
                StopSpeaking();
            }
        }

        /// <summary>
        /// Amplitudeモードでリップシンク開始（従来互換）
        /// </summary>
        public void StartLipSync(AudioClip clip)
        {
            if (vrmInstance == null)
            {
                Debug.LogWarning("[LipSyncController] VRM instance not set");
                return;
            }

            _mode = LipSyncMode.Amplitude;
            _isSyncing = true;
            _vowelIndex = 0;
            Debug.Log("[LipSyncController] Lip sync started (Amplitude)");
        }

        /// <summary>
        /// Moraモードでリップシンク開始（VOICEVOX用）
        /// AudioQueryのモーラデータに基づく正確な母音同期
        /// </summary>
        public void StartMoraLipSync(List<MoraEntry> moraTimeline)
        {
            if (vrmInstance == null)
            {
                Debug.LogWarning("[LipSyncController] VRM instance not set");
                return;
            }

            if (moraTimeline == null || moraTimeline.Count == 0)
            {
                // モーラデータがない場合はAmplitudeモードにフォールバック
                StartLipSync(null);
                return;
            }

            _mode = LipSyncMode.Mora;
            _moraTimeline = moraTimeline;
            _moraTimer = 0f;
            _moraIndex = 0;
            _isSyncing = true;
            Debug.Log($"[LipSyncController] Lip sync started (Mora, {moraTimeline.Count} entries)");
        }

        /// <summary>
        /// Simulatedモードでリップシンク開始（Web Speech API用）
        /// テキスト長推定による時間ベース口パク
        /// </summary>
        public void StartSimulatedLipSync(float estimatedDuration)
        {
            if (vrmInstance == null)
            {
                Debug.LogWarning("[LipSyncController] VRM instance not set");
                return;
            }

            _mode = LipSyncMode.Simulated;
            _simulatedTimer = 0f;
            _simulatedDuration = estimatedDuration;
            _simulatedMoraTimer = 0f;
            _vowelIndex = 0;
            _isSyncing = true;
            Debug.Log($"[LipSyncController] Lip sync started (Simulated, {estimatedDuration:F1}s)");
        }

        /// <summary>
        /// リップシンク停止
        /// </summary>
        public void StopLipSync()
        {
            _isSyncing = false;
            _currentWeight = 0f;
            _moraTimeline = null;
            ResetMouth();
            Debug.Log("[LipSyncController] Lip sync stopped");
        }

        // ─────────────────────────────────────
        // LateUpdate（PlayableDirector評価後に口の形を適用するため）
        // ─────────────────────────────────────

        private void LateUpdate()
        {
            if (!_isSyncing)
            {
                // 口を閉じる（フェードアウト）
                if (_currentWeight > 0f)
                {
                    _currentWeight = Mathf.Lerp(_currentWeight, 0f, Time.deltaTime * speed);
                    ApplyExpression(_currentVowel, _currentWeight);
                }
                return;
            }

            switch (_mode)
            {
                case LipSyncMode.Mora:
                    UpdateMoraLipSync();
                    break;
                case LipSyncMode.Simulated:
                    UpdateSimulatedLipSync();
                    break;
                case LipSyncMode.TextOnly:
                    UpdateTextOnlyLipSync();
                    break;
                case LipSyncMode.Amplitude:
                default:
                    UpdateAmplitudeLipSync();
                    break;
            }
        }

        // ─────────────────────────────────────
        // Moraモード（VOICEVOX）
        // ─────────────────────────────────────

        private void UpdateMoraLipSync()
        {
            if (_moraTimeline == null || _moraTimeline.Count == 0)
            {
                StopLipSync();
                return;
            }

            _moraTimer += Time.deltaTime;

            // タイムライン終了チェック
            var lastMora = _moraTimeline[_moraTimeline.Count - 1];
            if (_moraTimer >= lastMora.startTime + lastMora.duration)
            {
                // タイムライン完了 → 口を閉じて待機（StopLipSyncは外部から呼ばれる）
                float targetWeight = 0f;
                _currentWeight = Mathf.Lerp(_currentWeight, targetWeight, Time.deltaTime * speed);
                ApplyExpression(_currentVowel, _currentWeight);
                return;
            }

            // 現在のモーラを検索
            while (_moraIndex < _moraTimeline.Count - 1 &&
                   _moraTimer >= _moraTimeline[_moraIndex + 1].startTime)
            {
                _moraIndex++;
            }

            var currentMora = _moraTimeline[_moraIndex];
            string vowel = currentMora.vowel;

            // VOICEVOX母音 → VRM Expression マッピング
            string vrmExpression = MapVoicevoxVowelToExpression(vowel);

            if (vrmExpression != null)
            {
                // 母音: 口を開く
                _currentVowel = vrmExpression;
                float targetWeight = intensity;
                _currentWeight = Mathf.Lerp(_currentWeight, targetWeight, Time.deltaTime * speed);
            }
            else
            {
                // N, cl, pau等: 口を閉じる
                float targetWeight = 0f;
                _currentWeight = Mathf.Lerp(_currentWeight, targetWeight, Time.deltaTime * speed);
            }

            ApplyExpression(_currentVowel, _currentWeight);
        }

        /// <summary>
        /// VOICEVOX母音 → VRM Expression名 変換
        /// null返却は閉口を意味する
        /// </summary>
        private string MapVoicevoxVowelToExpression(string vowel)
        {
            return vowel switch
            {
                "a" => EXPR_AA,
                "i" => EXPR_IH,
                "u" => EXPR_OU,
                "e" => EXPR_EE,
                "o" => EXPR_OH,
                "N" => null,   // ん（閉口）
                "cl" => null,  // 促音（閉口）
                "pau" => null, // ポーズ（閉口）
                _ => null
            };
        }

        // ─────────────────────────────────────
        // Simulatedモード（Web Speech API）
        // ─────────────────────────────────────

        private void UpdateSimulatedLipSync()
        {
            _simulatedTimer += Time.deltaTime;

            if (_simulatedTimer >= _simulatedDuration)
            {
                // 推定時間超過 → 口を閉じて待機（StopLipSyncは外部から呼ばれる）
                float closeWeight = 0f;
                _currentWeight = Mathf.Lerp(_currentWeight, closeWeight, Time.deltaTime * speed);
                ApplyExpression(_currentVowel, _currentWeight);
                return;
            }

            // モーラ周期で母音サイクル
            _simulatedMoraTimer += Time.deltaTime;
            if (_simulatedMoraTimer >= simulatedMoraSpeed)
            {
                _simulatedMoraTimer -= simulatedMoraSpeed;
                _vowelIndex = (_vowelIndex + 1) % _vowels.Length;
                _currentVowel = _vowels[_vowelIndex];
            }

            // 60%開口・40%閉口のパターン
            float moraNormalized = _simulatedMoraTimer / simulatedMoraSpeed;
            float targetWeight = moraNormalized < 0.6f ? intensity : 0f;
            _currentWeight = Mathf.Lerp(_currentWeight, targetWeight, Time.deltaTime * speed);

            ApplyExpression(_currentVowel, _currentWeight);
        }

        // ─────────────────────────────────────
        // TextOnlyモード（テキスト表示のみ）
        // ─────────────────────────────────────

        private void UpdateTextOnlyLipSync()
        {
            _textOnlyTimer += Time.deltaTime;

            if (_textOnlyTimer >= _textOnlyDuration)
            {
                StopSpeaking();
                return;
            }

            // モーラ周期で母音サイクル
            _textOnlyMoraTimer += Time.deltaTime;
            if (_textOnlyMoraTimer >= simulatedMoraSpeed)
            {
                _textOnlyMoraTimer -= simulatedMoraSpeed;
                _vowelIndex = (_vowelIndex + 1) % _vowels.Length;
                _currentVowel = _vowels[_vowelIndex];
            }

            // 60%開口・40%閉口のパターン
            float moraNormalized = _textOnlyMoraTimer / simulatedMoraSpeed;
            float targetWeight = moraNormalized < 0.6f ? intensity : 0f;
            _currentWeight = Mathf.Lerp(_currentWeight, targetWeight, Time.deltaTime * speed);

            ApplyExpression(_currentVowel, _currentWeight);
        }

        // ─────────────────────────────────────
        // Amplitudeモード（従来方式）
        // ─────────────────────────────────────

        private void UpdateAmplitudeLipSync()
        {
            if (audioSource == null || !audioSource.isPlaying)
            {
                // 口を閉じる
                if (_currentWeight > 0f)
                {
                    _currentWeight = Mathf.Lerp(_currentWeight, 0f, Time.deltaTime * speed);
                    ApplyExpression(_currentVowel, _currentWeight);
                }
                return;
            }

            // 音声振幅を取得
            float amplitude = GetAudioAmplitude();

            // 振幅に応じて口の開閉
            float targetWeight = amplitude > amplitudeThreshold ? amplitude * intensity : 0f;
            _currentWeight = Mathf.Lerp(_currentWeight, targetWeight, Time.deltaTime * speed);

            // 母音を周期的に切り替え
            if (_currentWeight > 0.3f && Random.value < vowelSwitchProbability)
            {
                _vowelIndex = (_vowelIndex + 1) % _vowels.Length;
                _currentVowel = _vowels[_vowelIndex];
            }

            ApplyExpression(_currentVowel, _currentWeight);
        }

        /// <summary>
        /// 音声の振幅を取得（0.0-1.0）
        /// </summary>
        private float GetAudioAmplitude()
        {
            if (audioSource == null || audioSource.clip == null)
            {
                return 0f;
            }

            float[] samples = new float[256];
            audioSource.GetOutputData(samples, 0);

            float sum = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                sum += Mathf.Abs(samples[i]);
            }

            return Mathf.Clamp01(sum / samples.Length * 10f);
        }

        // ─────────────────────────────────────
        // VRM Expression
        // ─────────────────────────────────────

        /// <summary>
        /// VRM Expressionを適用
        /// </summary>
        private void ApplyExpression(string expressionKey, float weight)
        {
            if (vrmInstance == null) return;

            var expression = vrmInstance.Runtime?.Expression;
            if (expression == null) return;

            // 全ての母音Expressionをリセット
            foreach (var vowel in _vowels)
            {
                var key = ExpressionKey.CreateFromPreset(GetExpressionPreset(vowel));
                expression.SetWeight(key, 0f);
            }

            // 現在の母音を適用
            var currentKey = ExpressionKey.CreateFromPreset(GetExpressionPreset(expressionKey));
            expression.SetWeight(currentKey, weight);
        }

        /// <summary>
        /// 口を閉じる
        /// </summary>
        private void ResetMouth()
        {
            if (vrmInstance == null) return;

            var expression = vrmInstance.Runtime?.Expression;
            if (expression == null) return;

            foreach (var vowel in _vowels)
            {
                var key = ExpressionKey.CreateFromPreset(GetExpressionPreset(vowel));
                expression.SetWeight(key, 0f);
            }
        }

        /// <summary>
        /// 母音名からExpressionPresetに変換
        /// </summary>
        private ExpressionPreset GetExpressionPreset(string vowel)
        {
            return vowel switch
            {
                EXPR_AA => ExpressionPreset.aa,
                EXPR_IH => ExpressionPreset.ih,
                EXPR_OU => ExpressionPreset.ou,
                EXPR_EE => ExpressionPreset.ee,
                EXPR_OH => ExpressionPreset.oh,
                _ => ExpressionPreset.aa
            };
        }
    }
}
