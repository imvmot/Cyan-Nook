using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace CyanNook.Chat
{
    /// <summary>
    /// JSONストリームの逐次パース処理
    /// プロバイダー固有のDownloadHandlerから再利用される
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

            // 未閉じ判定（{ } と [ ] の両方を追跡。配列を数えないと
            // 配列内のカンマをトップレベルのフィールド区切りと誤認する）
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

                if (c == '{' || c == '[') depth++;
                else if (c == '}' || c == ']') depth--;
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

            // 未閉じの文字列・括弧を検出（開き括弧の種類をスタックで記憶し、
            // 対応する閉じ括弧を内側から補完する）
            string repaired = sb.ToString();
            inString = false;
            escaped = false;
            var openBrackets = new Stack<char>();
            for (int i = 0; i < repaired.Length; i++)
            {
                char c = repaired[i];
                if (escaped) { escaped = false; continue; }
                if (c == '\\' && inString) { escaped = true; continue; }
                if (c == '"') { inString = !inString; continue; }
                if (inString) continue;
                if (c == '{' || c == '[') openBrackets.Push(c);
                else if ((c == '}' || c == ']') && openBrackets.Count > 0) openBrackets.Pop();
            }

            // 未閉じの文字列を閉じる（文字列値の途中で切れた場合）
            if (inString)
            {
                sb.Append('"');
            }

            // 未閉じの括弧を対応する種類で補完
            while (openBrackets.Count > 0)
            {
                sb.Append(openBrackets.Pop() == '[' ? ']' : '}');
            }

            string result = sb.ToString();
            Debug.Log($"[StreamSeparatorProcessor] Repaired JSON: {(result.Length > 100 ? result.Substring(0, 100) + "..." : result)}");
            return result;
        }
    }
}
