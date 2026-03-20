using UnityEngine;
using CyanNook.Core;

namespace CyanNook.Character
{
    /// <summary>
    /// 退屈ポイント（bored）を管理するコントローラー
    /// 時間経過で自然増加し、LLMレスポンスの感情値で増減する。
    /// 値はLLMリクエスト時にプロンプトのプレースホルダ {bored} として渡される。
    /// LLMはこの値を参照するのみで管理はしない。
    ///
    /// 感情ベース変動の計算式:
    ///   delta = ((happy×happyFactor) + (sad×sadFactor) + (relaxed×relaxedFactor) + (angry×angryFactor))
    ///           × ((surprised+1.0) × |surprisedFactor|)
    /// </summary>
    public class BoredomController : MonoBehaviour
    {
        [Header("Natural Increase")]
        [Tooltip("退屈ポイントの自然増加レート（ポイント/分）。0で自然増加OFF")]
        [Range(0f, 10f)]
        public float increaseRate = 1f;

        [Header("Emotion Factors")]
        [Tooltip("happy感情の退屈度係数（負=退屈が減る）")]
        public float happyFactor = -3f;

        [Tooltip("relaxed感情の退屈度係数（負=退屈が減る）")]
        public float relaxedFactor = -1.5f;

        [Tooltip("angry感情の退屈度係数（正=退屈が増える）")]
        public float angryFactor = 2f;

        [Tooltip("sad感情の退屈度係数（正=退屈が増える）")]
        public float sadFactor = 3f;

        [Tooltip("surprised感情の増幅係数（常に絶対値として使用）")]
        public float surprisedFactor = 1f;

        [Header("State")]
        [SerializeField]
        private float _bored = 0f;

        private bool _isPaused = false;

        /// <summary>現在の退屈ポイント（0-100）</summary>
        public float Bored => _bored;

        /// <summary>プロンプト用の整数値</summary>
        public int BoredInt => Mathf.RoundToInt(_bored);

        private void Update()
        {
            if (_isPaused || increaseRate <= 0f) return;
            _bored = Mathf.Min(100f, _bored + (increaseRate / 60f) * Time.deltaTime);
        }

        /// <summary>蓄積の一時停止/再開（Sleep/Outing中に使用）</summary>
        public void SetPaused(bool paused)
        {
            _isPaused = paused;
            Debug.Log($"[BoredomController] Paused: {paused}");
        }

        /// <summary>
        /// LLMレスポンスの感情データに基づいて退屈度を変動させる
        /// 計算式: ((happy×Nh)+(sad×Ns)+(relaxed×Nr)+(angry×Na)) × ((surprised+1)×|Nsu|)
        /// </summary>
        public void ApplyEmotionDelta(EmotionData emotion)
        {
            if (emotion == null) return;

            float baseDelta = (emotion.happy * happyFactor)
                            + (emotion.sad * sadFactor)
                            + (emotion.relaxed * relaxedFactor)
                            + (emotion.angry * angryFactor);

            float surpriseMultiplier = (emotion.surprised + 1f) * Mathf.Abs(surprisedFactor);

            float delta = baseDelta * surpriseMultiplier;

            float before = _bored;
            _bored = Mathf.Clamp(_bored + delta, 0f, 100f);
            Debug.Log($"[BoredomController] EmotionDelta: {before:F1} → {_bored:F1} (delta={delta:F2}, " +
                      $"h={emotion.happy:F1}×{happyFactor}, r={emotion.relaxed:F1}×{relaxedFactor}, " +
                      $"a={emotion.angry:F1}×{angryFactor}, s={emotion.sad:F1}×{sadFactor}, " +
                      $"su={emotion.surprised:F1}×{Mathf.Abs(surprisedFactor)})");
        }

        /// <summary>0にリセット（Outing帰還時等に使用）</summary>
        public void Reset()
        {
            float before = _bored;
            _bored = 0f;
            Debug.Log($"[BoredomController] Reset: {before:F1} → 0");
        }
    }
}
