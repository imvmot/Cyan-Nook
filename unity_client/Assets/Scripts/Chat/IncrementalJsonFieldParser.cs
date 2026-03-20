using System;
using System.Text;
using UnityEngine;

namespace CyanNook.Chat
{
    /// <summary>
    /// ストリーミングで届くJSONテキストを文字単位で解析し、
    /// トップレベルの各フィールドが完了した時点でイベントを発火する。
    ///
    /// 対応フォーマット例:
    /// {
    ///   "emotion": { "happy": 0.8, ... },
    ///   "reaction": "いいね!",
    ///   "target": { "type": "dynamic", ... },
    ///   "action": "move",
    ///   "emote": "happy01",
    ///   "message": "本文テキスト..."
    /// }
    ///
    /// 各フィールドの値が確定するごとに OnFieldParsed を発火。
    /// StreamingFieldName に指定されたフィールドは文字列値の途中でも
    /// チャンク単位で OnStringValueChunk を発火（ストリーミング表示用）。
    /// JSON全体の } が閉じた時点で OnJsonComplete を発火。
    ///
    /// LLMがシングルクォートを出力する場合にも対応:
    ///   'key': 'value' → "key": "value" として正規化しバッファに格納
    /// </summary>
    public class IncrementalJsonFieldParser
    {
        /// <summary>トップレベルフィールドの値が確定した時に発火</summary>
        /// <param name="fieldName">フィールド名（例: "emotion", "action"）</param>
        /// <param name="rawValue">値の生JSON文字列（オブジェクトなら "{...}", 文字列なら引用符付き）</param>
        public Action<string, string> OnFieldParsed;

        /// <summary>JSON全体のパースが完了した時（外側の } が閉じた時）</summary>
        public Action OnJsonComplete;

        /// <summary>
        /// ストリーミング対象フィールドの文字列値が途中でもチャンク単位で発火
        /// (fieldName, decodedTextChunk) - エスケープシーケンスはデコード済み
        /// </summary>
        public Action<string, string> OnStringValueChunk;

        /// <summary>
        /// ストリーミング対象フィールド名（例: "message"）
        /// このフィールドの文字列値はチャンク単位で OnStringValueChunk を発火する
        /// </summary>
        public string StreamingFieldName { get; set; }

        // パース状態
        private readonly StringBuilder _buffer = new StringBuilder();
        private int _depth;             // ブラケット深度（0=JSON外、1=トップレベル、2+=ネスト）
        private bool _inString;         // 文字列リテラル内か
        private char _stringQuoteChar;  // 文字列を開始した引用符（'"' or '\''）
        private bool _escaped;          // エスケープ文字直後か
        private bool _completed;        // JSON完了済みか

        // フィールドパース用
        private string _currentKey;     // 現在のフィールド名
        private int _valueStartIndex;   // 値の開始位置（_buffer内）
        private bool _parsingKey;       // キーのパース中か
        private int _keyStartIndex;     // キー文字列の開始位置
        private bool _waitingForColon;  // キー後のコロン待ちか
        private bool _parsingValue;     // 値のパース中か
        private bool _valueIsString;    // 値が文字列型か
        private bool _valueIsObject;    // 値がオブジェクト型か
        private int _valueObjectDepth;  // 値がオブジェクトの場合の開始深度

        // ストリーミング文字列値用
        private bool _isStreamingField;                                          // 現在ストリーミング中か
        private readonly StringBuilder _stringStreamBuffer = new StringBuilder(); // チャンク内蓄積バッファ

        // JSON後の残りテキスト
        private string _remainingText;
        public string RemainingText => _remainingText ?? string.Empty;

        /// <summary>
        /// ストリーミングチャンクを処理
        /// </summary>
        public void ProcessChunk(string chunk)
        {
            if (string.IsNullOrEmpty(chunk) || _completed) return;

            _stringStreamBuffer.Clear();

            foreach (char c in chunk)
            {
                if (_completed)
                {
                    // JSON完了後の残りテキストを蓄積
                    if (_remainingText == null)
                        _remainingText = c.ToString();
                    else
                        _remainingText += c;
                    continue;
                }

                // シングルクォート正規化:
                // LLMがシングルクォートを出力した場合、バッファにはダブルクォートとして格納
                // これにより下流のJsonUtility.FromJsonが正しくパースできる
                char bufferChar = NormalizeQuoteForBuffer(c);
                _buffer.Append(bufferChar);
                ProcessChar(c);
            }

            // チャンク処理後: ストリーミングバッファをフラッシュ
            FlushStringStreamBuffer();
        }

        /// <summary>
        /// シングルクォートをバッファ用にダブルクォートに正規化
        /// ダブルクォート文字列内のシングルクォート（例: "it's"）はそのまま維持
        /// </summary>
        private char NormalizeQuoteForBuffer(char c)
        {
            if (c != '\'') return c;

            // ダブルクォート文字列内のシングルクォート → そのまま
            if (_inString && _stringQuoteChar == '"') return c;

            // それ以外（シングルクォート文字列の開始/終了、または文字列外）→ ダブルクォートに変換
            return '"';
        }

        /// <summary>
        /// ストリーム完了を通知（JSON未完了の場合はフォールバック処理）
        /// </summary>
        public void Complete()
        {
            if (!_completed)
            {
                // JSON未完了のまま終了 → 残っている値があれば発火を試みる
                if (_parsingValue && !string.IsNullOrEmpty(_currentKey))
                {
                    TryEmitCurrentField();
                }

                Debug.LogWarning("[IncrementalJsonFieldParser] Stream completed before JSON was fully parsed");
            }
        }

        /// <summary>
        /// 状態をリセット
        /// </summary>
        public void Reset()
        {
            _buffer.Clear();
            _depth = 0;
            _inString = false;
            _stringQuoteChar = '\0';
            _escaped = false;
            _completed = false;
            _currentKey = null;
            _valueStartIndex = 0;
            _parsingKey = false;
            _keyStartIndex = 0;
            _waitingForColon = false;
            _parsingValue = false;
            _valueIsString = false;
            _valueIsObject = false;
            _valueObjectDepth = 0;
            _isStreamingField = false;
            _stringStreamBuffer.Clear();
            _remainingText = null;
        }

        /// <summary>
        /// パース済みフィールドからLlmResponseHeaderを組み立てるためのヘルパー。
        /// _buffer内の全JSONテキストを返す。
        /// </summary>
        public string GetAccumulatedJson()
        {
            return _buffer.ToString();
        }

        public bool IsCompleted => _completed;

        private void ProcessChar(char c)
        {
            // エスケープ処理
            if (_escaped)
            {
                _escaped = false;

                // ストリーミング中: エスケープシーケンスをデコードして蓄積
                if (_isStreamingField && _inString)
                {
                    _stringStreamBuffer.Append(DecodeEscapeChar(c));
                }

                return;
            }

            if (c == '\\' && _inString)
            {
                _escaped = true;
                return;
            }

            // 引用符判定（ダブルクォートとシングルクォートの両方に対応）
            bool isQuote = (c == '"' || c == '\'');

            // 文字列リテラル内
            if (_inString)
            {
                // 開始時と同じ種類の引用符でのみ閉じる
                if (c == _stringQuoteChar)
                {
                    _inString = false;

                    // キーのパース完了
                    if (_parsingKey && _depth == 1)
                    {
                        _currentKey = _buffer.ToString().Substring(_keyStartIndex, _buffer.Length - _keyStartIndex - 1);
                        _parsingKey = false;
                        _waitingForColon = true;
                        return;
                    }

                    // 文字列型の値のパース完了
                    if (_parsingValue && _valueIsString && _depth == 1)
                    {
                        // ストリーミング中なら残りバッファをフラッシュしてから完了
                        if (_isStreamingField)
                        {
                            FlushStringStreamBuffer();
                            _isStreamingField = false;
                        }
                        EmitCurrentField();
                        return;
                    }
                }
                else if (_isStreamingField)
                {
                    // ストリーミング中の通常文字を蓄積
                    _stringStreamBuffer.Append(c);
                }
                return;
            }

            // 文字列リテラル開始（" または '）
            if (isQuote)
            {
                _inString = true;
                _stringQuoteChar = c;

                // depth==1 でキーまたは値の開始
                if (_depth == 1 && !_parsingValue && !_waitingForColon)
                {
                    // キー開始
                    _parsingKey = true;
                    _keyStartIndex = _buffer.Length; // 次の文字からがキー内容
                    return;
                }

                // depth==1 で値の開始（文字列型）
                if (_depth == 1 && _parsingValue && !_valueIsObject)
                {
                    _valueIsString = true;

                    // ストリーミング対象フィールドか判定
                    if (!string.IsNullOrEmpty(StreamingFieldName) && _currentKey == StreamingFieldName)
                    {
                        _isStreamingField = true;
                    }

                    return;
                }

                return;
            }

            // コロン（キーと値の区切り）
            if (c == ':' && _waitingForColon && _depth == 1)
            {
                _waitingForColon = false;
                _parsingValue = true;
                _valueIsString = false;
                _valueIsObject = false;
                _valueStartIndex = _buffer.Length; // 次の文字から値
                return;
            }

            // ブラケット処理
            if (c == '{')
            {
                _depth++;

                // JSON開始
                if (_depth == 1)
                {
                    return;
                }

                // depth==1でのオブジェクト値の開始
                if (_depth == 2 && _parsingValue && !_valueIsString)
                {
                    _valueIsObject = true;
                    _valueObjectDepth = 2;
                    // valueStartIndexを { の位置に調整
                    _valueStartIndex = _buffer.Length - 1;
                    return;
                }

                return;
            }

            if (c == '}')
            {
                _depth--;

                // オブジェクト型の値完了
                if (_parsingValue && _valueIsObject && _depth == 1)
                {
                    EmitCurrentField();
                    return;
                }

                // JSON全体の完了
                if (_depth == 0)
                {
                    _completed = true;
                    OnJsonComplete?.Invoke();
                    return;
                }

                return;
            }

            // カンマ（フィールド区切り） - プリミティブ値の完了
            if (c == ',' && _depth == 1 && _parsingValue && !_valueIsString && !_valueIsObject)
            {
                EmitCurrentField();
                return;
            }

            // 値のパース開始（非文字列、非オブジェクト = 数値/bool/null）
            if (_depth == 1 && _parsingValue && !_valueIsString && !_valueIsObject)
            {
                // 空白は無視
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
                {
                    if (_valueStartIndex == _buffer.Length - 1)
                    {
                        _valueStartIndex = _buffer.Length; // 空白をスキップ
                    }
                }
            }
        }

        /// <summary>
        /// 現在のフィールドの値を抽出してイベント発火
        /// </summary>
        private void EmitCurrentField()
        {
            if (string.IsNullOrEmpty(_currentKey))
            {
                ResetFieldState();
                return;
            }

            string rawValue = _buffer.ToString().Substring(_valueStartIndex, _buffer.Length - _valueStartIndex);

            // 末尾のカンマ・空白を除去
            rawValue = rawValue.TrimEnd(',', ' ', '\t', '\n', '\r');

            // 閉じ}が含まれている場合（JSON全体の}ではなく値の}）
            // オブジェクト型は } まで含まれている
            // 文字列型は " まで含まれている
            // プリミティブ型は , の手前まで

            Debug.Log($"[IncrementalJsonFieldParser] Field parsed: {_currentKey} = {TruncateForLog(rawValue)}");
            OnFieldParsed?.Invoke(_currentKey, rawValue);

            ResetFieldState();
        }

        /// <summary>
        /// フォールバック: 値が不完全でも可能な限り発火を試みる
        /// </summary>
        private void TryEmitCurrentField()
        {
            if (string.IsNullOrEmpty(_currentKey) || _valueStartIndex >= _buffer.Length)
            {
                return;
            }

            string rawValue = _buffer.ToString().Substring(_valueStartIndex);
            rawValue = rawValue.TrimEnd(',', ' ', '\t', '\n', '\r', '}');

            if (!string.IsNullOrEmpty(rawValue))
            {
                Debug.LogWarning($"[IncrementalJsonFieldParser] Emitting incomplete field: {_currentKey} = {TruncateForLog(rawValue)}");
                OnFieldParsed?.Invoke(_currentKey, rawValue);
            }

            ResetFieldState();
        }

        private void ResetFieldState()
        {
            _currentKey = null;
            _parsingValue = false;
            _valueIsString = false;
            _valueIsObject = false;
            _valueStartIndex = 0;
            _isStreamingField = false;
        }

        /// <summary>
        /// ストリーミングバッファをフラッシュしてイベント発火
        /// ProcessChunk の末尾、または文字列値の完了時に呼ばれる
        /// </summary>
        private void FlushStringStreamBuffer()
        {
            if (_stringStreamBuffer.Length > 0 && !string.IsNullOrEmpty(_currentKey))
            {
                OnStringValueChunk?.Invoke(_currentKey, _stringStreamBuffer.ToString());
                _stringStreamBuffer.Clear();
            }
        }

        /// <summary>
        /// JSONエスケープシーケンスをデコード
        /// </summary>
        private static char DecodeEscapeChar(char c)
        {
            switch (c)
            {
                case 'n': return '\n';
                case 't': return '\t';
                case 'r': return '\r';
                case '"': return '"';
                case '\\': return '\\';
                case '/': return '/';
                default: return c;
            }
        }

        private static string TruncateForLog(string text, int maxLength = 80)
        {
            if (text == null) return "null";
            if (text.Length <= maxLength) return text;
            return text.Substring(0, maxLength) + "...";
        }
    }
}
