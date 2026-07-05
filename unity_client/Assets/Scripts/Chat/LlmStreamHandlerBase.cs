using System;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace CyanNook.Chat
{
    /// <summary>
    /// 行単位ストリーミング用DownloadHandlerの基底クラス
    /// UTF-8安全デコード（チャンク境界で分断されたマルチバイト文字の保持）と
    /// 行バッファリングを共通化し、1行の解釈のみ派生クラスに委ねる。
    /// 抽出したテキストはStreamSeparatorProcessor（Processor）に供給し、
    /// ストリーム終了時のComplete()通知もここで行う
    /// </summary>
    internal abstract class LineStreamHandlerBase : DownloadHandlerScript
    {
        private readonly Decoder _utf8Decoder;
        private readonly StringBuilder _lineBuffer = new StringBuilder();

        /// <summary>抽出したテキストチャンクの供給先（JSON逐次パース）</summary>
        protected readonly StreamSeparatorProcessor Processor;

        /// <summary>
        /// ストリーム終端で改行なしの最終行を処理するか
        /// NDJSON（Ollama）は最終行が\nで終わらないことがあるためtrue。
        /// SSEは行が必ず\nで終わるためfalse
        /// </summary>
        protected virtual bool ProcessTrailingLineOnComplete => false;

        protected LineStreamHandlerBase(byte[] preallocatedBuffer,
            Action<LlmResponseHeader> onHeader, Action<string> onTextChunk,
            Action<string> onError, Action<string, string> onField = null,
            Action<string, string> onParseError = null) : base(preallocatedBuffer)
        {
            _utf8Decoder = Encoding.UTF8.GetDecoder();
            Processor = new StreamSeparatorProcessor
            {
                OnHeaderReceived = onHeader,
                OnTextReceived = onTextChunk,
                OnError = onError,
                OnFieldParsed = onField,
                OnParseError = onParseError
            };
        }

        protected override bool ReceiveData(byte[] data, int dataLength)
        {
            if (data == null || dataLength < 1) return false;

            // UTF-8安全デコード
            int charCount = _utf8Decoder.GetCharCount(data, 0, dataLength, false);
            if (charCount == 0) return true;

            char[] chars = new char[charCount];
            _utf8Decoder.GetChars(data, 0, dataLength, chars, 0, false);
            _lineBuffer.Append(chars);

            ProcessBufferedLines();
            return true;
        }

        protected override void CompleteContent()
        {
            // デコーダに残ったバイトをフラッシュ
            int charCount = _utf8Decoder.GetCharCount(new byte[0], 0, 0, true);
            if (charCount > 0)
            {
                char[] chars = new char[charCount];
                _utf8Decoder.GetChars(new byte[0], 0, 0, chars, 0, true);
                _lineBuffer.Append(chars);
                ProcessBufferedLines();
            }

            // NDJSON: 改行で終わらない最終行を処理
            if (ProcessTrailingLineOnComplete && _lineBuffer.Length > 0)
            {
                string trailing = _lineBuffer.ToString().Trim();
                _lineBuffer.Clear();
                if (!string.IsNullOrEmpty(trailing))
                {
                    ProcessLine(trailing);
                }
            }

            OnBeforeComplete();
            Processor.Complete();
        }

        /// <summary>Processor.Complete()直前のフック（Geminiのthoughtフォールバック等）</summary>
        protected virtual void OnBeforeComplete() { }

        /// <summary>
        /// バッファから完全な行（\n区切り）を取り出してProcessLineに渡す
        /// </summary>
        private void ProcessBufferedLines()
        {
            string content = _lineBuffer.ToString();
            int lastNewline = content.LastIndexOf('\n');
            if (lastNewline < 0) return;

            string completedPart = content.Substring(0, lastNewline);
            string remaining = content.Substring(lastNewline + 1);

            _lineBuffer.Clear();
            _lineBuffer.Append(remaining);

            string[] lines = completedPart.Split('\n');
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    ProcessLine(trimmed);
                }
            }
        }

        /// <summary>1行（trim済み・非空）を解釈する</summary>
        protected abstract void ProcessLine(string line);
    }

    /// <summary>
    /// SSE（Server-Sent Events）ストリーミング用DownloadHandlerの基底クラス
    /// "data:" 行のペイロード抽出とパース失敗時のログまでを共通化し、
    /// dataペイロード（JSON）の解釈のみ派生クラスに委ねる
    /// </summary>
    internal abstract class SseStreamHandlerBase : LineStreamHandlerBase
    {
        protected SseStreamHandlerBase(byte[] preallocatedBuffer,
            Action<LlmResponseHeader> onHeader, Action<string> onTextChunk,
            Action<string> onError, Action<string, string> onField = null,
            Action<string, string> onParseError = null)
            : base(preallocatedBuffer, onHeader, onTextChunk, onError, onField, onParseError)
        {
        }

        protected sealed override void ProcessLine(string line)
        {
            // "event:" 行等はスキップ（テキスト抽出に必要なのは "data:" 行のみ）
            if (!line.StartsWith("data:", StringComparison.Ordinal)) return;

            string jsonData = line.Substring(5).Trim();
            if (string.IsNullOrEmpty(jsonData)) return;

            try
            {
                ProcessSseData(jsonData);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[{GetType().Name}] Failed to parse SSE data: {e.Message}\nData: {jsonData}");
            }
        }

        /// <summary>SSEのdataペイロード（JSON）を解釈する。例外は基底側でログされる</summary>
        protected abstract void ProcessSseData(string jsonData);
    }
}
