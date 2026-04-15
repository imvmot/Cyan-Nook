namespace CyanNook.Core
{
    /// <summary>
    /// TTS（音声合成）エンジンの種類
    /// </summary>
    public enum TTSEngineType
    {
        /// <summary>ブラウザ標準 Web Speech API（設定不要、低品質）</summary>
        WebSpeechAPI = 0,

        /// <summary>VOICEVOX（高品質、要ローカル/LANサーバー、キャラクター性あり）</summary>
        VOICEVOX = 1,

        /// <summary>Gemini TTS（高品質、要APIキー、ナチュラル系）</summary>
        GeminiTTS = 2
    }
}
