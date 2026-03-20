using UnityEngine;
using System;
using System.IO;

namespace CyanNook.Voice
{
    /// <summary>
    /// WAVファイル（byte配列）をUnityのAudioClipに変換するユーティリティ
    /// </summary>
    public static class WavUtility
    {
        // 16bit PCM正規化定数
        private const float PCM16_MAX_PLUS_ONE = 32768f; // 2^15 (読み込み: short → -1.0~1.0)
        private const float PCM16_MAX = 32767f;          // 2^15-1 (書き込み: float → short)
        private const float PCM8_OFFSET = 128f;          // 8bit unsigned → signed変換
        /// <summary>
        /// WAVデータ（byte配列）をAudioClipに変換
        /// </summary>
        public static AudioClip ToAudioClip(byte[] wavData, string clipName = "VoicevoxClip")
        {
            if (wavData == null || wavData.Length < 44)
            {
                Debug.LogError("[WavUtility] Invalid WAV data");
                return null;
            }

            try
            {
                // WAVヘッダー解析
                int channels = BitConverter.ToInt16(wavData, 22);
                int sampleRate = BitConverter.ToInt32(wavData, 24);
                int bitsPerSample = BitConverter.ToInt16(wavData, 34);

                // データチャンク位置検索（"data"の後の4バイトがサイズ）
                int dataStartIndex = FindDataChunk(wavData);
                if (dataStartIndex == -1)
                {
                    Debug.LogError("[WavUtility] Could not find data chunk");
                    return null;
                }

                int dataSize = BitConverter.ToInt32(wavData, dataStartIndex + 4);
                int dataStart = dataStartIndex + 8;

                // サンプル数計算
                int bytesPerSample = bitsPerSample / 8;
                int sampleCount = dataSize / (bytesPerSample * channels);

                // float配列に変換
                float[] samples = new float[sampleCount * channels];

                if (bitsPerSample == 16)
                {
                    for (int i = 0; i < sampleCount * channels; i++)
                    {
                        int byteIndex = dataStart + i * 2;
                        short sample16 = BitConverter.ToInt16(wavData, byteIndex);
                        samples[i] = sample16 / PCM16_MAX_PLUS_ONE; // -1.0 ~ 1.0に正規化
                    }
                }
                else if (bitsPerSample == 8)
                {
                    for (int i = 0; i < sampleCount * channels; i++)
                    {
                        byte sample8 = wavData[dataStart + i];
                        samples[i] = (sample8 - PCM8_OFFSET) / PCM8_OFFSET; // -1.0 ~ 1.0に正規化
                    }
                }
                else
                {
                    Debug.LogError($"[WavUtility] Unsupported bits per sample: {bitsPerSample}");
                    return null;
                }

                // AudioClip生成
                AudioClip clip = AudioClip.Create(clipName, sampleCount, channels, sampleRate, false);
                clip.SetData(samples, 0);

                Debug.Log($"[WavUtility] AudioClip created - Samples: {sampleCount}, Channels: {channels}, SampleRate: {sampleRate}");

                return clip;
            }
            catch (Exception e)
            {
                Debug.LogError($"[WavUtility] Exception: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// "data"チャンクの位置を検索
        /// </summary>
        private static int FindDataChunk(byte[] wavData)
        {
            // RIFFヘッダー後（12バイト目以降）から検索
            for (int i = 12; i < wavData.Length - 4; i++)
            {
                if (wavData[i] == 'd' &&
                    wavData[i + 1] == 'a' &&
                    wavData[i + 2] == 't' &&
                    wavData[i + 3] == 'a')
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// AudioClipをWAVファイルとして保存（デバッグ用）
        /// </summary>
        public static void SaveAudioClipToWav(AudioClip clip, string filePath)
        {
            if (clip == null)
            {
                Debug.LogError("[WavUtility] Clip is null");
                return;
            }

            using (var fileStream = File.Create(filePath))
            using (var writer = new BinaryWriter(fileStream))
            {
                int sampleCount = clip.samples * clip.channels;
                float[] samples = new float[sampleCount];
                clip.GetData(samples, 0);

                // WAVヘッダー書き込み
                writer.Write(new char[4] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + sampleCount * 2); // ファイルサイズ - 8
                writer.Write(new char[4] { 'W', 'A', 'V', 'E' });

                // fmtチャンク
                writer.Write(new char[4] { 'f', 'm', 't', ' ' });
                writer.Write(16); // fmtチャンクサイズ
                writer.Write((short)1); // PCM
                writer.Write((short)clip.channels);
                writer.Write(clip.frequency);
                writer.Write(clip.frequency * clip.channels * 2); // byte/sec
                writer.Write((short)(clip.channels * 2)); // block align
                writer.Write((short)16); // bits per sample

                // dataチャンク
                writer.Write(new char[4] { 'd', 'a', 't', 'a' });
                writer.Write(sampleCount * 2); // dataサイズ

                // サンプルデータ書き込み
                for (int i = 0; i < sampleCount; i++)
                {
                    short sample = (short)(Mathf.Clamp(samples[i], -1f, 1f) * PCM16_MAX);
                    writer.Write(sample);
                }
            }

            Debug.Log($"[WavUtility] Saved to {filePath}");
        }
    }
}
