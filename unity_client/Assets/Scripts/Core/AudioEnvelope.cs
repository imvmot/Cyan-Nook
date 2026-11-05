using System.Runtime.CompilerServices;
using UnityEngine;

namespace CyanNook.Core
{
    /// <summary>
    /// AudioClip の音量の変化（エンベロープ）を前もって計算して保持する。
    ///
    /// WebGL では AudioSource.GetOutputData / AudioClip.GetData が使えず（常にゼロ）、
    /// 再生中の音量から口パクする Amplitude リップシンクが動かない。そこで PCM から
    /// AudioClip を組み立てる時点（WavUtility / Gemini TTS）で 1/60 秒ごとの音量を計算し、
    /// クリップに紐づけておく。リップシンクは再生開始からの経過時間でこれを引く。
    ///
    /// 紐づけは ConditionalWeakTable（クリップの参照が無くなれば自動で消える）なので、
    /// 登録側・利用側とも解放処理は不要
    /// </summary>
    public static class AudioEnvelope
    {
        /// <summary>1秒あたりの音量サンプル数</summary>
        public const float FrameRate = 60f;

        // GetOutputData 版（LipSyncController.GetAudioAmplitude）と同じ尺度にそろえる倍率
        // （区間内の振幅の平均 × 10 を 0〜1 にクランプ）
        private const float Gain = 10f;

        private static readonly ConditionalWeakTable<AudioClip, float[]> _table =
            new ConditionalWeakTable<AudioClip, float[]>();

        /// <summary>
        /// PCM サンプル（-1〜1、チャンネルはインターリーブ）から音量の変化を計算してクリップに紐づける
        /// </summary>
        public static void Register(AudioClip clip, float[] samples, int channels, int sampleRate)
        {
            if (clip == null || samples == null || channels <= 0 || sampleRate <= 0) return;

            int frameCount = samples.Length / channels;
            int window = Mathf.Max(1, Mathf.RoundToInt(sampleRate / FrameRate));
            int envelopeLength = (frameCount + window - 1) / window;
            var envelope = new float[envelopeLength];

            for (int e = 0; e < envelopeLength; e++)
            {
                int start = e * window;
                int end = Mathf.Min(frameCount, start + window);
                float sum = 0f;
                int count = 0;
                for (int f = start; f < end; f++)
                {
                    // チャンネルを平均してモノラルの振幅として扱う
                    float mono = 0f;
                    int baseIndex = f * channels;
                    for (int c = 0; c < channels; c++)
                    {
                        mono += samples[baseIndex + c];
                    }
                    sum += Mathf.Abs(mono / channels);
                    count++;
                }
                envelope[e] = count > 0 ? Mathf.Clamp01(sum / count * Gain) : 0f;
            }

            _table.Remove(clip);
            _table.Add(clip, envelope);
        }

        /// <summary>
        /// クリップに紐づく音量の変化を返す。無ければ null
        /// </summary>
        public static float[] Get(AudioClip clip)
        {
            if (clip == null) return null;
            return _table.TryGetValue(clip, out var envelope) ? envelope : null;
        }

        /// <summary>
        /// 再生位置（秒）の音量を返す。範囲外は 0
        /// </summary>
        public static float Sample(float[] envelope, float time)
        {
            if (envelope == null || time < 0f) return 0f;
            int index = (int)(time * FrameRate);
            return index < envelope.Length ? envelope[index] : 0f;
        }
    }
}
