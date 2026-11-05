using UnityEngine;
using System;
using System.IO;
using CyanNook.Core;

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
        // WAV ヘッダの解析結果
        private struct WavHeader
        {
            public int audioFormat;
            public int channels;
            public int sampleRate;
            public int bitsPerSample;
            public int dataStart;   // 音声データの先頭位置
            public int dataSize;    // 音声データのバイト数（実データ長に切り詰め済み）
        }

        /// <summary>
        /// RIFF/WAVE のチャンク（「4文字ID + 4バイトサイズ + 本体」の区画）を先頭から順にたどり、
        /// fmt と data を見つける。ID が完全一致する区画だけを採用するため、LIST/INFO 等の
        /// 注釈の中の文字列 "data" を誤検出しない。fmt が先頭以外にある wav にも対応する
        /// </summary>
        private static bool TryParseHeader(byte[] wavData, out WavHeader header)
        {
            header = default;
            if (wavData == null || wavData.Length < 12) return false;
            if (!MatchId(wavData, 0, "RIFF") || !MatchId(wavData, 8, "WAVE")) return false;

            bool hasFmt = false;
            int pos = 12;
            while (pos + 8 <= wavData.Length)
            {
                int chunkSize = BitConverter.ToInt32(wavData, pos + 4);
                int body = pos + 8;

                if (MatchId(wavData, pos, "fmt "))
                {
                    if (chunkSize < 16 || body + 16 > wavData.Length) return false;
                    header.audioFormat = BitConverter.ToInt16(wavData, body);
                    header.channels = BitConverter.ToInt16(wavData, body + 2);
                    header.sampleRate = BitConverter.ToInt32(wavData, body + 4);
                    header.bitsPerSample = BitConverter.ToInt16(wavData, body + 14);
                    hasFmt = true;
                }
                else if (MatchId(wavData, pos, "data"))
                {
                    if (!hasFmt) return false;

                    // ヘッダのサイズが実データより大きい（ストリーミング書き出しの未確定値等）場合は実データに合わせる
                    int available = wavData.Length - body;
                    header.dataStart = body;
                    header.dataSize = (chunkSize < 0 || chunkSize > available) ? available : chunkSize;
                    return header.channels > 0 && header.sampleRate > 0 && header.dataSize > 0;
                }

                // 次の区画へ（サイズが奇数なら1バイトのパディングがある）
                if (chunkSize < 0) return false;
                long next = (long)body + chunkSize + (chunkSize & 1);
                if (next > wavData.Length) return false;
                pos = (int)next;
            }
            return false;
        }

        private static bool MatchId(byte[] data, int offset, string id)
        {
            if (offset + 4 > data.Length) return false;
            return data[offset] == id[0] && data[offset + 1] == id[1] &&
                   data[offset + 2] == id[2] && data[offset + 3] == id[3];
        }

        /// <summary>
        /// 自前で読める WAV（RIFF/WAVE、リニアPCM、8bit または 16bit）か。
        /// 外部から受け取る wav を自前変換するか、ブラウザ側の解読に任せるかの判定に使う
        /// </summary>
        public static bool IsSupportedPcm(byte[] wavData)
        {
            return TryParseHeader(wavData, out var h) &&
                   h.audioFormat == 1 && (h.bitsPerSample == 16 || h.bitsPerSample == 8);
        }

        /// <summary>
        /// WAVデータ（byte配列）をAudioClipに変換。
        /// 音量の変化（AudioEnvelope）も計算してクリップに紐づける（WebGL の Amplitude リップシンク用）
        /// </summary>
        public static AudioClip ToAudioClip(byte[] wavData, string clipName = "VoicevoxClip")
        {
            try
            {
                // WAVヘッダー解析（チャンクをたどって fmt / data を探す）
                if (!TryParseHeader(wavData, out var header))
                {
                    Debug.LogError("[WavUtility] Invalid WAV data (fmt/data chunk not found)");
                    return null;
                }

                int channels = header.channels;
                int sampleRate = header.sampleRate;
                int bitsPerSample = header.bitsPerSample;
                int dataStart = header.dataStart;
                int dataSize = header.dataSize;

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
                AudioEnvelope.Register(clip, samples, channels, sampleRate);

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
