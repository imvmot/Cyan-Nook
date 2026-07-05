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

        /// <summary>Vision: 有効/無効（LLMSettingsPanelが保存 / ChatManagerが復元）</summary>
        public const string UseVision = "llm_useVision";

        /// <summary>会話履歴の最大保持数（LLMSettingsPanelが保存 / ChatManagerが復元）</summary>
        public const string MaxHistory = "llm_maxHistory";

        /// <summary>音声入力マイクON/OFF（VoiceSettingsPanelが保存 / VoiceInputControllerが復元）</summary>
        public const string MicEnabled = "voice_micEnabled";

        /// <summary>アバター: VRMファイル名（AvatarSettingsPanelが保存 / CharacterSetupが復元）</summary>
        public const string VrmFileName = "avatar_vrmFileName";

        /// <summary>アバター: キャラクター設定プロンプト（AvatarSettingsPanelが保存 / ChatManagerが復元）</summary>
        public const string CharacterPrompt = "avatar_characterPrompt";

        /// <summary>アバター: レスポンスフォーマットプロンプト（AvatarSettingsPanelが保存 / ChatManagerが復元）</summary>
        public const string ResponseFormat = "avatar_responseFormat";

        /// <summary>退屈度: 自然増加レート（AvatarSettingsPanelが保存 / BoredomControllerが復元）</summary>
        public const string BoredRate = "avatar_boredRate";

        /// <summary>退屈度: 感情係数（AvatarSettingsPanelが保存 / BoredomControllerが復元）</summary>
        public const string BoredFactorHappy = "avatar_boredFactorHappy";
        public const string BoredFactorRelaxed = "avatar_boredFactorRelaxed";
        public const string BoredFactorAngry = "avatar_boredFactorAngry";
        public const string BoredFactorSad = "avatar_boredFactorSad";
        public const string BoredFactorSurprised = "avatar_boredFactorSurprised";
    }
}
