using UnityEngine;

namespace CyanNook.Core
{
    /// <summary>
    /// ユーザーのWebカメラ映像を取得し、シーン内のQuad（MeshRenderer）に表示する
    /// キャラクターカメラがこのQuadを含むシーンをレンダリングすることで、
    /// LLMがユーザーの姿を認識できるようになる
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public class WebCamDisplayController : MonoBehaviour
    {
        // PlayerPrefsキー
        private const string PrefKey_WebCamEnabled = "llm_webCam";

        [Header("WebCam Settings")]
        [Tooltip("使用するWebカメラデバイス名（空欄=デフォルトカメラ）")]
        public string deviceName = "";

        [Tooltip("要求解像度（幅）")]
        public int requestedWidth = 640;

        [Tooltip("要求解像度（高さ）")]
        public int requestedHeight = 480;

        [Tooltip("要求フレームレート")]
        public int requestedFPS = 30;

        [Header("Display Settings")]
        [Tooltip("映像を水平反転する（フロントカメラ用）")]
        public bool mirrorHorizontal = true;

        [Tooltip("起動時に自動再生")]
        public bool autoPlay = true;

        private WebCamTexture _webCamTexture;
        private Renderer _renderer;
        private Material _material;

        /// <summary>
        /// Webカメラが再生中かどうか
        /// </summary>
        public bool IsPlaying => _webCamTexture != null && _webCamTexture.isPlaying;

        private void Start()
        {
            _renderer = GetComponent<Renderer>();

            // PlayerPrefsから設定を読み込み
            LoadSettings();

            if (autoPlay)
            {
                StartWebCam();
            }
            else
            {
                // 自動再生しない場合はRendererを無効化（何も表示しない）
                if (_renderer != null)
                {
                    _renderer.enabled = false;
                }
            }
        }

        /// <summary>
        /// PlayerPrefsから設定を読み込み
        /// </summary>
        private void LoadSettings()
        {
            if (PlayerPrefs.HasKey(PrefKey_WebCamEnabled))
            {
                autoPlay = PlayerPrefs.GetInt(PrefKey_WebCamEnabled) == 1;
                Debug.Log($"[WebCamDisplayController] Loaded saved webCam: {autoPlay}");
            }
        }

        /// <summary>
        /// Webカメラを起動してQuadに表示開始
        /// </summary>
        public void StartWebCam()
        {
            if (IsPlaying)
            {
                Debug.Log("[WebCamDisplayController] WebCam already playing");
                return;
            }

            // WebCamTexture作成
            if (string.IsNullOrEmpty(deviceName))
            {
                _webCamTexture = new WebCamTexture(requestedWidth, requestedHeight, requestedFPS);
            }
            else
            {
                _webCamTexture = new WebCamTexture(deviceName, requestedWidth, requestedHeight, requestedFPS);
            }

            _webCamTexture.Play();

            // Unlitマテリアルを作成してテクスチャを設定
            SetupMaterial();

            Debug.Log($"[WebCamDisplayController] WebCam started: device=\"{_webCamTexture.deviceName}\", " +
                      $"requested={requestedWidth}x{requestedHeight}@{requestedFPS}fps");
        }

        /// <summary>
        /// Webカメラを停止
        /// </summary>
        public void StopWebCam()
        {
            if (_webCamTexture != null)
            {
                _webCamTexture.Stop();
                Destroy(_webCamTexture);
                _webCamTexture = null;
            }

            if (_renderer != null)
            {
                _renderer.enabled = false;
            }

            Debug.Log("[WebCamDisplayController] WebCam stopped");
        }

        private void SetupMaterial()
        {
            if (_renderer == null) return;

            // Unlitマテリアルを作成（ライティングの影響を受けない）
            if (_material == null)
            {
                var shader = Shader.Find("Unlit/Texture");
                if (shader == null)
                {
                    Debug.LogError("[WebCamDisplayController] Shader 'Unlit/Texture' not found");
                    return;
                }
                _material = new Material(shader);
            }

            _material.mainTexture = _webCamTexture;

            // 水平反転（フロントカメラのミラー対応）
            if (mirrorHorizontal)
            {
                _material.mainTextureScale = new Vector2(-1, 1);
                _material.mainTextureOffset = new Vector2(1, 0);
            }
            else
            {
                _material.mainTextureScale = new Vector2(1, 1);
                _material.mainTextureOffset = new Vector2(0, 0);
            }

            _renderer.material = _material;
            _renderer.enabled = true;
        }

        private void OnDestroy()
        {
            if (_webCamTexture != null)
            {
                _webCamTexture.Stop();
                Destroy(_webCamTexture);
                _webCamTexture = null;
            }

            if (_material != null)
            {
                Destroy(_material);
                _material = null;
            }
        }
    }
}
