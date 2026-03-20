namespace CyanNook.Core
{
    /// <summary>
    /// TTS（音声合成）エンジンの種類
    /// </summary>
    public enum TTSEngineType
    {
        /// <summary>ブラウザ標準 Web Speech API（設定不要）</summary>
        WebSpeechAPI = 0,

        /// <summary>VOICEVOX（高品質、要サーバー）</summary>
        VOICEVOX = 1
    }
}
