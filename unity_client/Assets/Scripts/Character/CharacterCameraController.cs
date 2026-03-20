using System;
using UnityEngine;
using UniVRM10;

namespace CyanNook.Character
{
    /// <summary>
    /// キャラクターの視線と連動したカメラ制御
    /// VRMのHeadボーンにコンストレイントし、ロール軸を無効化（常に水平）
    /// 負荷軽減のため通常はオンデマンドレンダリング、デバッグ時は常時レンダリング可能
    ///
    /// 実行順序: CharacterLookAtController(20000) → 本クラス(20001)
    /// LookAtがHeadボーンに回転を適用した後にカメラ位置を更新する必要がある
    /// </summary>
    [DefaultExecutionOrder(20001)]
    public class CharacterCameraController : MonoBehaviour
    {
        // PlayerPrefsキー
        private const string PrefKey_AlwaysRender = "llm_cameraPreview";

        [Header("References")]
        [Tooltip("キャラクター視点カメラ")]
        public Camera characterCamera;

        [Header("Constraint Settings")]
        [Tooltip("Headボーンからのローカル位置オフセット（目の位置調整用）")]
        public Vector3 positionOffset = new Vector3(0f, 0.06f, 0.1f);

        [Tooltip("回転オフセット（度）")]
        public Vector3 rotationOffset = Vector3.zero;

        [Header("Capture Settings")]
        [Tooltip("キャプチャ解像度（幅）")]
        public int captureWidth = 512;

        [Tooltip("キャプチャ解像度（高さ）")]
        public int captureHeight = 512;

        [Tooltip("JPEG品質 (0-100)")]
        [Range(0, 100)]
        public int jpegQuality = 75;

        [Header("Debug")]
        [Tooltip("常時レンダリング（デバッグ用）")]
        public bool alwaysRender = false;

        private Transform _headBone;
        private RenderTexture _renderTexture;
        private Texture2D _captureTexture;
        private bool _isInitialized;

        /// <summary>
        /// VRM Instanceを設定（VRM読み込み時に呼び出し）
        /// Headボーンを取得し、RenderTextureをセットアップする
        /// </summary>
        public void SetVrmInstance(Vrm10Instance vrmInstance)
        {
            if (vrmInstance == null) return;

            var animator = vrmInstance.GetComponent<Animator>();
            if (animator != null)
            {
                _headBone = animator.GetBoneTransform(HumanBodyBones.Head);
            }

            if (_headBone == null)
            {
                Debug.LogWarning("[CharacterCameraController] Head bone not found");
                return;
            }

            SetupRenderTexture();
            _isInitialized = true;

            // PlayerPrefsから設定を読み込み
            LoadSettings();

            // 初期状態ではオンデマンドレンダリング
            if (characterCamera != null)
            {
                characterCamera.enabled = alwaysRender;
            }

            Debug.Log("[CharacterCameraController] Initialized with head bone constraint");
        }

        /// <summary>
        /// PlayerPrefsから設定を読み込み
        /// </summary>
        private void LoadSettings()
        {
            if (PlayerPrefs.HasKey(PrefKey_AlwaysRender))
            {
                alwaysRender = PlayerPrefs.GetInt(PrefKey_AlwaysRender) == 1;
                Debug.Log($"[CharacterCameraController] Loaded saved alwaysRender: {alwaysRender}");
            }
        }

        private void SetupRenderTexture()
        {
            // 既存のRenderTextureを解放
            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
            }

            _renderTexture = new RenderTexture(captureWidth, captureHeight, 16);
            _renderTexture.name = "CharacterCameraRT";

            if (characterCamera != null)
            {
                characterCamera.targetTexture = _renderTexture;
            }

            // キャプチャ用Texture2Dを再利用バッファとして作成
            if (_captureTexture != null)
            {
                Destroy(_captureTexture);
            }
            _captureTexture = new Texture2D(captureWidth, captureHeight, TextureFormat.RGB24, false);
        }

        private void LateUpdate()
        {
            if (!_isInitialized || _headBone == null || characterCamera == null) return;

            // Headボーンの位置にオフセットを加算
            characterCamera.transform.position = _headBone.position + _headBone.rotation * positionOffset;

            // ロールフリー回転: Headの向きを使いつつ、上方向は常にワールドUp
            Vector3 forward = _headBone.rotation * Quaternion.Euler(rotationOffset) * Vector3.forward;
            characterCamera.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        /// <summary>
        /// カメラの映像をキャプチャしてbase64エンコードされたJPEG文字列を返す
        /// </summary>
        public string CaptureImageAsBase64()
        {
            if (!_isInitialized || characterCamera == null || _renderTexture == null)
            {
                Debug.LogWarning("[CharacterCameraController] Not initialized, cannot capture");
                return null;
            }

            // オンデマンドレンダリング（alwaysRender=false時はenabled=falseなので手動Render必要）
            if (!characterCamera.enabled)
            {
                characterCamera.Render();
            }

            // RenderTextureからピクセルを読み取り
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = _renderTexture;

            _captureTexture.ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0);
            _captureTexture.Apply();

            RenderTexture.active = previousActive;

            // JPEGエンコードしてbase64に変換
            byte[] jpegBytes = _captureTexture.EncodeToJPG(jpegQuality);
            string base64 = Convert.ToBase64String(jpegBytes);

            Debug.Log($"[CharacterCameraController] Captured image: {captureWidth}x{captureHeight}, {jpegBytes.Length} bytes, base64 length={base64.Length}");

            return base64;
        }

        /// <summary>
        /// デバッグUI用にRenderTextureを公開
        /// </summary>
        public RenderTexture GetRenderTexture()
        {
            return _renderTexture;
        }

        /// <summary>
        /// 常時レンダリングの有効/無効を切り替え
        /// </summary>
        public void SetAlwaysRender(bool enabled)
        {
            alwaysRender = enabled;

            if (characterCamera != null && _isInitialized)
            {
                characterCamera.enabled = enabled;
            }

            // 設定を保存
            PlayerPrefs.SetInt(PrefKey_AlwaysRender, enabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        private void OnDestroy()
        {
            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
                _renderTexture = null;
            }

            if (_captureTexture != null)
            {
                Destroy(_captureTexture);
                _captureTexture = null;
            }
        }
    }
}
