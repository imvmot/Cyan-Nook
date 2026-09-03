namespace CyanNook.Core
{
    /// <summary>
    /// JSON文字列値のエスケープユーティリティ
    /// リクエストJSONを手動構築するLLMプロバイダー / TTSクライアント共通
    /// （SettingsExporter内のEscapeJsonStringはクォート付き・null許容で仕様が異なるため対象外）
    /// </summary>
    public static class JsonEscape
    {
        /// <summary>
        /// JSON文字列値としてエスケープする（クォートは含まない）
        /// ", \, 改行、タブ、その他の制御文字（\uXXXX）に対応。null/空は "" を返す
        /// </summary>
        public static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";

            var sb = new System.Text.StringBuilder(value.Length + 16);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.AppendFormat("\\u{0:x4}", (int)c);
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
