using System;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace CyanNook.Chat
{
    /// <summary>
    /// LLMストリーミングレスポンス用DownloadHandler
    /// WebGLビルドでも動作するDownloadHandlerScript継承
    ///
    /// 想定フォーマット:
    ///   {
    ///     "emotion": {...}, "reaction": "...", "action": "...",
    ///     "target": {...}, "emote": "...", "message": "..."
    ///   }
    ///
    /// JSONフィールド単位で逐次パースされ、各フィールド完了時にOnFieldParsedを発火。
    /// "message"フィールドはストリーミング表示のため、値の途中でもOnTextReceivedを発火。
    /// UTF-8マルチバイト文字の境界分割にも対応（System.Text.Decoder使用）
    /// </summary>
    public class LlmStreamHandler : DownloadHandlerScript
    {
        private readonly Decoder _utf8Decoder;
        private readonly StreamSeparatorProcessor _processor;

        // デバッグ用: 受信した全テキスト
        private readonly StringBuilder _fullResponse = new StringBuilder();
        public string FullResponse => _fullResponse.ToString();

        // StreamSeparatorProcessorのイベントをそのまま公開
        public Action<LlmResponseHeader> OnHeaderReceived
        {
            get => _processor.OnHeaderReceived;
            set => _processor.OnHeaderReceived = value;
        }

        /// <summary>JSONフィールドが逐次パースされた時（fieldName, rawJsonValue）</summary>
        public Action<string, string> OnFieldParsed
        {
            get => _processor.OnFieldParsed;
            set => _processor.OnFieldParsed = value;
        }

        public Action<string> OnTextReceived
        {
            get => _processor.OnTextReceived;
            set => _processor.OnTextReceived = value;
        }

        public Action OnComplete
        {
            get => _processor.OnComplete;
            set => _processor.OnComplete = value;
        }

        public Action<string> OnError
        {
            get => _processor.OnError;
            set => _processor.OnError = value;
        }

        /// <summary>JSONパースエラー時（エラーメッセージ, 生レスポンステキスト）</summary>
        public Action<string, string> OnParseError
        {
            get => _processor.OnParseError;
            set => _processor.OnParseError = value;
        }

        public bool IsHeaderParsed => _processor.IsHeaderParsed;

        public LlmStreamHandler(byte[] preallocatedBuffer) : base(preallocatedBuffer)
        {
            _utf8Decoder = Encoding.UTF8.GetDecoder();
            _processor = new StreamSeparatorProcessor();
        }

        /// <summary>
        /// データ受信コールバック（UnityWebRequestから呼ばれる）
        /// </summary>
        protected override bool ReceiveData(byte[] data, int dataLength)
        {
            if (data == null || dataLength < 1) return false;

            // UTF-8デコーダで安全に文字列に変換
            // flush=false: 不完全なマルチバイトシーケンスは次回に持ち越し
            int charCount = _utf8Decoder.GetCharCount(data, 0, dataLength, false);
            if (charCount == 0) return true;

            char[] chars = new char[charCount];
            _utf8Decoder.GetChars(data, 0, dataLength, chars, 0, false);
            string chunk = new string(chars);

            _fullResponse.Append(chunk);
            _processor.ProcessChunk(chunk);

            return true;
        }

        /// <summary>
        /// レスポンス完了時に呼ばれる
        /// </summary>
        protected override void CompleteContent()
        {
            // デコーダに残っている不完全バイトをフラッシュ
            int charCount = _utf8Decoder.GetCharCount(new byte[0], 0, 0, true);
            if (charCount > 0)
            {
                char[] chars = new char[charCount];
                _utf8Decoder.GetChars(new byte[0], 0, 0, chars, 0, true);
                string remaining = new string(chars);
                _fullResponse.Append(remaining);
                _processor.ProcessChunk(remaining);
            }

            _processor.Complete();
        }
    }

    /// <summary>
    /// JSONストリームの逐次パース処理
    /// LlmStreamHandler以外のプロバイダー固有ハンドラからも再利用可能
    ///
    /// JSONフィールドが確定するごとにOnFieldParsedを発火し、
    /// "message"フィールドの文字列値は途中でもOnTextReceivedを発火する。
    /// JSON全体の完了後にOnHeaderReceivedを発火する。
    ///
    /// 使い方:
    ///   var processor = new StreamSeparatorProcessor();
    ///   processor.OnFieldParsed = (name, value) => { ... };
    ///   processor.OnHeaderReceived = header => { ... };
    ///   processor.OnTextReceived = text => { ... };
    ///   processor.ProcessChunk("受信したテキスト");
    ///   processor.Complete(); // 完了時
    /// </summary>
    public class StreamSeparatorProcessor
    {
        private bool _headerEmitted;         // OnHeaderReceived発火済み（二重発火防止）
        private readonly IncrementalJsonFieldParser _fieldParser = new IncrementalJsonFieldParser();
        private bool _jsonCompleted;         // JSON全体のパース完了フラグ

        public bool IsHeaderParsed => _headerEmitted;

        // コールバック
        /// <summary>JSON全体のパース完了時</summary>
        public Action<LlmResponseHeader> OnHeaderReceived;
        /// <summary>JSONフィールドが逐次パースされた時（fieldName, rawJsonValue）</summary>
        public Action<string, string> OnFieldParsed;
        /// <summary>"message"フィールドのストリーミングテキストチャンク</summary>
        public Action<string> OnTextReceived;
        public Action OnComplete;
        public Action<string> OnError;
        /// <summary>JSONパースエラー時（エラーメッセージ, 生レスポンステキスト）。OnErrorの代わりに発火</summary>
        public Action<string, string> OnParseError;

        public StreamSeparatorProcessor()
        {
            _fieldParser.OnFieldParsed = HandleFieldParsed;
            _fieldParser.OnJsonComplete = HandleJsonComplete;
            _fieldParser.StreamingFieldName = "message";
            _fieldParser.OnStringValueChunk = HandleStringValueChunk;
        }

        /// <summary>
        /// テキストチャンクを処理
        /// IncrementalJsonFieldParserに転送
        /// </summary>
        public void ProcessChunk(string chunk)
        {
            if (string.IsNullOrEmpty(chunk)) return;

            _fieldParser.ProcessChunk(chunk);
        }

        /// <summary>
        /// ストリーム完了を通知
        /// </summary>
        public void Complete()
        {
            // IncrementalJsonFieldParserの完了処理
            _fieldParser.Complete();

            if (!_jsonCompleted)
            {
                // JSON未完了のまま終了 → 修復を試みてからフォールバックパース
                string raw = _fieldParser.GetAccumulatedJson().Trim();
                if (!string.IsNullOrEmpty(raw))
                {
                    try
                    {
                        // まず修復なしで試行
                        string cleanJson = ExtractJson(raw);
                        var header = JsonUtility.FromJson<LlmResponseHeader>(cleanJson);

                        if (!_headerEmitted)
                        {
                            _headerEmitted = true;
                            Debug.Log($"[StreamSeparatorProcessor] Fallback: parsed incomplete JSON (action={header.action})");
                            OnHeaderReceived?.Invoke(header);
                        }
                    }
                    catch (Exception)
                    {
                        // 修復を試みる: 途切れたJSONの閉じ括弧を補完
                        try
                        {
                            string repairedJson = RepairIncompleteJson(raw);
                            var header = JsonUtility.FromJson<LlmResponseHeader>(repairedJson);

                            if (!_headerEmitted)
                            {
                                _headerEmitted = true;
                                Debug.Log($"[StreamSeparatorProcessor] Repaired incomplete JSON (action={header.action})");
                                OnHeaderReceived?.Invoke(header);
                            }
                        }
                        catch (Exception)
                        {
                            // 修復後もパース不可 → パースエラー通知
                            OnParseError?.Invoke("Stream completed with incomplete JSON", raw);
                        }
                    }
                }
                else
                {
                    OnParseError?.Invoke("Stream completed with empty content", "");
                }
            }
            else if (!_headerEmitted)
            {
                // JSON完了したがヘッダー未発火（通常はHandleJsonCompleteで発火済み）
                BuildAndEmitHeader();
            }

            OnComplete?.Invoke();
        }

        /// <summary>
        /// 状態をリセット
        /// </summary>
        public void Reset()
        {
            _headerEmitted = false;
            _jsonCompleted = false;
            _fieldParser.Reset();
        }

        /// <summary>
        /// IncrementalJsonFieldParserからのフィールドパース完了コールバック
        /// </summary>
        private void HandleFieldParsed(string fieldName, string rawValue)
        {
            OnFieldParsed?.Invoke(fieldName, rawValue);
        }

        /// <summary>
        /// IncrementalJsonFieldParserからのJSON完了コールバック
        /// </summary>
        private void HandleJsonComplete()
        {
            _jsonCompleted = true;

            // LlmResponseHeaderを組み立ててOnHeaderReceivedを発火
            BuildAndEmitHeader();
        }

        /// <summary>
        /// "message"フィールドの文字列ストリーミングチャンクを転送
        /// </summary>
        private void HandleStringValueChunk(string fieldName, string chunk)
        {
            if (fieldName == "message")
            {
                OnTextReceived?.Invoke(chunk);
            }
        }

        /// <summary>
        /// パース済みJSONからLlmResponseHeaderを組み立てて発火
        /// </summary>
        private void BuildAndEmitHeader()
        {
            if (_headerEmitted) return; // 二重発火防止
            _headerEmitted = true;

            string accumulatedJson = _fieldParser.GetAccumulatedJson();
            string cleanJson = ExtractJson(accumulatedJson);

            try
            {
                var header = JsonUtility.FromJson<LlmResponseHeader>(cleanJson);

                Debug.Log($"[StreamSeparatorProcessor] Header complete: action={header.action}, emote={header.emote}");
                OnHeaderReceived?.Invoke(header);
            }
            catch (Exception e)
            {
                Debug.LogError($"[StreamSeparatorProcessor] Header JSON parse error: {e.Message}\nJSON: {cleanJson}");

                // フィールド単位では届いているので、フォールバックヘッダーで続行
                OnHeaderReceived?.Invoke(LlmResponseHeader.GetFallback());
            }
        }

        /// <summary>
        /// マークダウンコードブロックやBOMを除去してJSONを抽出
        /// </summary>
        private static string ExtractJson(string text)
        {
            string trimmed = text.Trim();

            // ```json ... ``` パターン
            if (trimmed.StartsWith("```"))
            {
                int firstNewline = trimmed.IndexOf('\n');
                if (firstNewline >= 0)
                {
                    int endBlock = trimmed.LastIndexOf("```", StringComparison.Ordinal);
                    if (endBlock > firstNewline)
                    {
                        return trimmed.Substring(firstNewline + 1, endBlock - firstNewline - 1).Trim();
                    }
                }
            }

            // { ... } を探す
            int braceStart = trimmed.IndexOf('{');
            int braceEnd = trimmed.LastIndexOf('}');
            if (braceStart >= 0 && braceEnd > braceStart)
            {
                return trimmed.Substring(braceStart, braceEnd - braceStart + 1);
            }

            return trimmed;
        }

        /// <summary>
        /// 途切れた不完全なJSONを修復する
        /// 途中まで正しい形であれば、閉じられていない文字列・オブジェクトを補完して
        /// JsonUtilityでパース可能な形にする
        /// </summary>
        private static string RepairIncompleteJson(string raw)
        {
            // { を探す
            int braceStart = raw.IndexOf('{');
            if (braceStart < 0) throw new Exception("No JSON object start found");

            string json = raw.Substring(braceStart);
            var sb = new StringBuilder(json);

            // 末尾のゴミ（途中で切れた値）を除去
            // 最後に正常に完了したフィールドの後のカンマ以降を切り取る
            string current = sb.ToString();

            // 未閉じの文字列を閉じる
            bool inString = false;
            bool escaped = false;
            int depth = 0;
            int lastCompleteFieldEnd = -1; // 最後に完了したフィールドの終端位置

            for (int i = 0; i < current.Length; i++)
            {
                char c = current[i];

                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\' && inString)
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString) continue;

                if (c == '{') depth++;
                else if (c == '}') depth--;
                else if (c == ',' && depth == 1)
                {
                    // トップレベルのカンマ = フィールド区切り
                    lastCompleteFieldEnd = i;
                }
            }

            // 不完全なフィールドがある場合、最後のカンマまで切り詰める
            if (depth != 0 && lastCompleteFieldEnd > 0)
            {
                sb.Length = 0;
                sb.Append(current, 0, lastCompleteFieldEnd);
            }

            // 閉じ括弧を補完
            // 改めてdepthを計算
            string repaired = sb.ToString();
            inString = false;
            escaped = false;
            depth = 0;
            for (int i = 0; i < repaired.Length; i++)
            {
                char c = repaired[i];
                if (escaped) { escaped = false; continue; }
                if (c == '\\' && inString) { escaped = true; continue; }
                if (c == '"') { inString = !inString; continue; }
                if (inString) continue;
                if (c == '{') depth++;
                else if (c == '}') depth--;
            }

            // 未閉じの } を補完
            for (int i = 0; i < depth; i++)
            {
                sb.Append('}');
            }

            string result = sb.ToString();
            Debug.Log($"[StreamSeparatorProcessor] Repaired JSON: {(result.Length > 100 ? result.Substring(0, 100) + "..." : result)}");
            return result;
        }
    }
}
