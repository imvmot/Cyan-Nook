namespace CyanNook.Core
{
    /// <summary>
    /// 複数クラスから参照されるPlayerPrefsキーの集約定義。
    /// 同じキー文字列を各クラスがローカルconstで二重定義すると、
    /// 片方だけリネームした際に静かに不整合になる（保存側と読込側が別キーになる）ため、
    /// 2箇所以上で使うキーは必ずここに定義して両側から参照する。
    /// 1クラスでしか使わないキーは従来どおり各クラスのローカルconstでよい。
    /// キーを追加・変更した場合はSettingsExporterのAllSettingsへの登録も確認すること。
    /// </summary>
    public static class SettingsKeys
    {
        /// <summary>Vision: キャラクターカメラのプレビュー常時レンダリング（LLMSettingsPanel / CharacterCameraController）</summary>
        public const string CameraPreview = "llm_cameraPreview";

        /// <summary>Vision: WebCam入力（LLMSettingsPanel / WebCamDisplayController）</summary>
        public const string WebCam = "llm_webCam";

        /// <summary>Vision: 画面キャプチャ（LLMSettingsPanel / ScreenCaptureDisplayController）</summary>
        public const string ScreenCapture = "llm_screenCapture";

        /// <summary>IdleChat: 自律メッセージのプロンプト（LLMSettingsPanel / IdleChatController）</summary>
        public const string IdleChatMessage = "idleChat_message";
    }
}
