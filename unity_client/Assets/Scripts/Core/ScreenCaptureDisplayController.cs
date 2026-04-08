using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace CyanNook.Core
{
    /// <summary>
    /// ユーザーのデスクトップ/ウィンドウ画面をキャプチャし、シーン内のQuad（MeshRenderer）に表示する
    ///
    /// PC: ブラウザのScreen Capture API（getDisplayMedia）を使用
    /// モバイル: Screen Capture APIが非対応のため、背面カメラ（WebCamTexture）にフォールバック
    ///
    /// WebCamDisplayControllerと同様に、キャラクターカメラがこのQuadを含むシーンをレンダリングすることで
    /// LLMがユーザーの画面を認識できるようになる
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public class ScreenCaptureDisplayController : MonoBehaviour
    {
        // PlayerPrefsキー
        private const string PrefKey_ScreenCaptureEnabled = "llm_screenCapture";

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void ScreenCapture_Start(int maxWidth, int maxHeight, string callbackObjectName, string callbackMethodName);

        [DllImport("__Internal")]
        private static extern void ScreenCapture_Stop();

        [DllImport("__Internal")]
        private static extern int ScreenCapture_IsCapturing();

        [DllImport("__Internal")]
        private static extern int ScreenCapture_IsFrameReady();

        [DllImport("__Internal")]
        private static extern int ScreenCapture_GetWidth();

        [DllImport("__Internal")]
        private static extern int ScreenCapture_GetHeight();

        [DllImport("__Internal")]
        private static extern int ScreenCapture_UpdateBuffer();

        [DllImport("__Internal")]
        private static extern System.IntPtr ScreenCapture_GetBufferPtr();

        [DllImport("__Internal")]
        private static extern int ScreenCapture_GetBufferSize();

        [DllImport("__Internal")]
        private static extern void MobileCamera_FindRearCamera();

        [DllImport("__Internal")]
        private static extern int MobileCamera_IsReady();

        [DllImport("__Internal")]
        private static extern string MobileCamera_GetLabel();
#endif

        [Header("Capture Settings")]
        [Tooltip("キャプチャ解像度の最大幅")]
        public int maxWidth = 640;

        [Tooltip("キャプチャ解像度の最大高さ")]
        public int maxHeight = 480;

        [Header("Display Settings")]
        [Tooltip("起動時に自動再生（前回の設定を復元）")]
        public bool autoPlay = false;

        private Renderer _renderer;
        private Material _material;
        private Texture2D _texture;
        private bool _isCapturing;
        private bool _waitingForCapture;
        private int _captureWidth;
        private int _captureHeight;

        // モバイル背面カメラ用
        private WebCamTexture _webCamTexture;
        private bool _isMobileMode;
        private float _mobileCameraTimeout;
        private const float MOBILE_CAMERA_TIMEOUT_SEC = 5f;

        /// <summary>
        /// モバイルデバイスかどうか（Screen Capture API非対応 → 背面カメラにフォールバック）
        /// </summary>
        public bool IsMobileMode => _isMobileMode;

        /// <summary>
        /// 画面キャプチャ（またはモバイル背面カメラ）が再生中かどうか
        /// </summary>
        public bool IsPlaying => _isCapturing;

        private void Start()
        {
            _renderer = GetComponent<Renderer>();
            _isMobileMode = Application.isMobilePlatform;

            if (_isMobileMode)
            {
                Debug.Log("[ScreenCaptureDisplayController] Mobile detected, using rear camera mode");
            }

            LoadSettings();

            if (autoPlay)
            {
                StartCapture();
            }
            else
            {
                if (_renderer != null)
                {
                    _renderer.enabled = false;
                }
            }
        }

        private void LoadSettings()
        {
            if (PlayerPrefs.HasKey(PrefKey_ScreenCaptureEnabled))
            {
                autoPlay = PlayerPrefs.GetInt(PrefKey_ScreenCaptureEnabled) == 1;
                Debug.Log($"[ScreenCaptureDisplayController] Loaded saved screenCapture: {autoPlay}");
            }
        }

        /// <summary>
        /// キャプチャを開始
        /// PC: ブラウザの画面共有ダイアログが表示される
        /// モバイル: 背面カメラを起動
        /// </summary>
        public void StartCapture()
        {
            if (_isCapturing || _waitingForCapture)
            {
                Debug.Log("[ScreenCaptureDisplayController] Already capturing");
                return;
            }

            if (_isMobileMode)
            {
                StartMobileCamera();
                return;
            }

            _waitingForCapture = true;

#if UNITY_WEBGL && !UNITY_EDITOR
            ScreenCapture_Start(maxWidth, maxHeight, gameObject.name, "OnScreenCaptureStartResult");
#else
            Debug.LogWarning("[ScreenCaptureDisplayController] Screen capture is only supported in WebGL builds");
#endif
        }

        /// <summary>
        /// キャプチャを停止
        /// </summary>
        public void StopCapture()
        {
            if (_isMobileMode)
            {
                StopMobileCamera();
                return;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            ScreenCapture_Stop();
#endif
            _isCapturing = false;
            _waitingForCapture = false;

            if (_renderer != null)
            {
                _renderer.enabled = false;
            }

            Debug.Log("[ScreenCaptureDisplayController] Capture stopped");
        }

        /// <summary>
        /// jslib からのキャプチャ開始結果コールバック
        /// </summary>
        public void OnScreenCaptureStartResult(string result)
        {
            if (result == "ok")
            {
                InitializeCapture();
            }
            else
            {
                _waitingForCapture = false;
                _isCapturing = false;
                Debug.LogWarning($"[ScreenCaptureDisplayController] Capture failed: {result}");
            }
        }

        private void InitializeCapture()
        {
            if (_isCapturing) return;

#if UNITY_WEBGL && !UNITY_EDITOR
            _captureWidth = ScreenCapture_GetWidth();
            _captureHeight = ScreenCapture_GetHeight();
#endif
            _waitingForCapture = false;
            _isCapturing = true;
            SetupTexture();
            Debug.Log($"[ScreenCaptureDisplayController] Capture started: {_captureWidth}x{_captureHeight}");
        }

        /// <summary>
        /// ユーザーがブラウザ側で共有を停止した場合のコールバック
        /// </summary>
        public void OnScreenCaptureStopped(string unused)
        {
            _isCapturing = false;
            _waitingForCapture = false;

            if (_renderer != null)
            {
                _renderer.enabled = false;
            }

            Debug.Log("[ScreenCaptureDisplayController] Capture stopped by user (browser)");
        }

        private void Update()
        {
            // モバイルカメラ: jslibのenumerateDevices結果をポーリング
            if (_isMobileMode && _waitingForCapture)
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                if (MobileCamera_IsReady() == 1)
                {
                    string label = MobileCamera_GetLabel();
                    Debug.Log($"[ScreenCaptureDisplayController] Rear camera search complete: \"{label}\"");
                    StartMobileCameraWithDevice(label);
                    return;
                }
#endif
                _mobileCameraTimeout -= Time.deltaTime;
                if (_mobileCameraTimeout <= 0f)
                {
                    Debug.LogWarning("[ScreenCaptureDisplayController] Mobile camera search timeout, using default");
                    StartMobileCameraWithDevice("");
                }
                return;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            // SendMessageが届かなかった場合のフォールバック：jslib側の状態をポーリング
            if (_waitingForCapture && !_isCapturing)
            {
                if (ScreenCapture_IsCapturing() == 1 && ScreenCapture_GetWidth() > 0)
                {
                    Debug.Log("[ScreenCaptureDisplayController] Detected capture via polling (SendMessage fallback)");
                    InitializeCapture();
                }
                return;
            }
#endif

            if (!_isCapturing || _texture == null) return;

#if UNITY_WEBGL && !UNITY_EDITOR
            // jslib内部バッファにフレームデータを書き込み
            if (ScreenCapture_UpdateBuffer() == 0) return;

            // jslib内部バッファのポインタをそのままTexture2Dに渡す
            System.IntPtr bufferPtr = ScreenCapture_GetBufferPtr();
            int bufferSize = ScreenCapture_GetBufferSize();
            if (bufferPtr != System.IntPtr.Zero && bufferSize > 0)
            {
                _texture.LoadRawTextureData(bufferPtr, bufferSize);
                _texture.Apply();
            }
#endif
        }

        private void SetupTexture()
        {
            if (_captureWidth <= 0 || _captureHeight <= 0) return;

            // テクスチャ作成（RGBA32）
            if (_texture != null)
            {
                Destroy(_texture);
            }
            _texture = new Texture2D(_captureWidth, _captureHeight, TextureFormat.RGBA32, false);
            _texture.filterMode = FilterMode.Bilinear;

            // Unlitマテリアルを作成
            if (_material == null)
            {
                var shader = Shader.Find("Unlit/Texture");
                if (shader == null)
                {
                    Debug.LogError("[ScreenCaptureDisplayController] Shader 'Unlit/Texture' not found");
                    return;
                }
                _material = new Material(shader);
            }

            _material.mainTexture = _texture;

            // 画面キャプチャはCanvasのY軸が反転しているため上下反転
            _material.mainTextureScale = new Vector2(1, -1);
            _material.mainTextureOffset = new Vector2(0, 1);

            if (_renderer != null)
            {
                _renderer.material = _material;
                _renderer.enabled = true;
            }
        }

        /// <summary>
        /// 現在のキャプチャフレームをJPEGエンコードしてbase64文字列を返す
        /// </summary>
        public string CaptureImageAsBase64(int jpegQuality = 75)
        {
            if (!_isCapturing) return null;

            // モバイル背面カメラモード
            if (_isMobileMode)
            {
                return CaptureMobileCameraAsBase64(jpegQuality);
            }

            if (_texture == null) return null;

            // ブラウザCanvasのY座標系がUnity Texture2Dと逆のため、上下反転してからエンコード
            int width = _texture.width;
            int height = _texture.height;
            Color32[] pixels = _texture.GetPixels32();
            for (int y = 0; y < height / 2; y++)
            {
                int topRow = y * width;
                int bottomRow = (height - 1 - y) * width;
                for (int x = 0; x < width; x++)
                {
                    var tmp = pixels[topRow + x];
                    pixels[topRow + x] = pixels[bottomRow + x];
                    pixels[bottomRow + x] = tmp;
                }
            }

            var flipped = new Texture2D(width, height, TextureFormat.RGBA32, false);
            flipped.SetPixels32(pixels);
            byte[] jpegBytes = flipped.EncodeToJPG(jpegQuality);
            Destroy(flipped);

            string base64 = Convert.ToBase64String(jpegBytes);
            Debug.Log($"[ScreenCaptureDisplayController] Captured image: {width}x{height}, base64 length={base64.Length}");
            return base64;
        }

        /// <summary>
        /// 現在のキャプチャテクスチャを取得（UIプレビュー用）
        /// モバイルモードではWebCamTextureを直接返す（自動更新されるためリアルタイムプレビュー可能）
        /// PC: Texture2D（Update()で毎フレーム更新）
        /// </summary>
        public Texture GetPreviewTexture()
        {
            if (_isMobileMode)
                return _webCamTexture;
            return _texture;
        }

        // ─────────────────────────────────────
        // モバイル背面カメラ
        // ─────────────────────────────────────

        private void StartMobileCamera()
        {
            _waitingForCapture = true;
            _mobileCameraTimeout = MOBILE_CAMERA_TIMEOUT_SEC;

#if UNITY_WEBGL && !UNITY_EDITOR
            // jslib経由でenumerateDevices()を使って背面カメラを検索（結果はポーリング取得）
            MobileCamera_FindRearCamera();
#else
            // エディタではisFrontFacingベースのフォールバック
            StartMobileCameraWithDevice(FindRearCameraDeviceFallback() ?? "");
#endif
        }

        /// <summary>
        /// 背面カメラデバイスラベルでWebCamTextureを起動する
        /// </summary>
        private void StartMobileCameraWithDevice(string deviceLabel)
        {
            _waitingForCapture = false;

            if (string.IsNullOrEmpty(deviceLabel))
            {
                // 背面カメラが見つからない場合はデフォルトカメラを使用
                _webCamTexture = new WebCamTexture(maxWidth, maxHeight, 15);
                Debug.Log("[ScreenCaptureDisplayController] Rear camera not found, using default camera");
            }
            else
            {
                _webCamTexture = new WebCamTexture(deviceLabel, maxWidth, maxHeight, 15);
                Debug.Log($"[ScreenCaptureDisplayController] Using rear camera: {deviceLabel}");
            }

            _webCamTexture.Play();
            _isCapturing = true;

            SetupMobileCameraMaterial();

            Debug.Log($"[ScreenCaptureDisplayController] Mobile camera started: {_webCamTexture.deviceName}");
        }

        private void StopMobileCamera()
        {
            if (_webCamTexture != null)
            {
                _webCamTexture.Stop();
                Destroy(_webCamTexture);
                _webCamTexture = null;
            }

            _isCapturing = false;

            if (_renderer != null)
            {
                _renderer.enabled = false;
            }

            Debug.Log("[ScreenCaptureDisplayController] Mobile camera stopped");
        }

        /// <summary>
        /// isFrontFacingベースの背面カメラ検索（エディタ用フォールバック）
        /// iOS WebGLではisFrontFacingが信頼できないため、本番ではjslib経由で検索する
        /// </summary>
        private string FindRearCameraDeviceFallback()
        {
            foreach (var device in WebCamTexture.devices)
            {
                if (!device.isFrontFacing)
                {
                    return device.name;
                }
            }
            return null;
        }

        private void SetupMobileCameraMaterial()
        {
            if (_renderer == null || _webCamTexture == null) return;

            if (_material == null)
            {
                var shader = Shader.Find("Unlit/Texture");
                if (shader == null)
                {
                    Debug.LogError("[ScreenCaptureDisplayController] Shader 'Unlit/Texture' not found");
                    return;
                }
                _material = new Material(shader);
            }

            _material.mainTexture = _webCamTexture;
            // 背面カメラはミラー不要、Y軸反転もなし
            _material.mainTextureScale = new Vector2(1, 1);
            _material.mainTextureOffset = new Vector2(0, 0);

            _renderer.material = _material;
            _renderer.enabled = true;
        }

        private string CaptureMobileCameraAsBase64(int jpegQuality)
        {
            if (_webCamTexture == null || !_webCamTexture.isPlaying) return null;

            int width = _webCamTexture.width;
            int height = _webCamTexture.height;

            var captureTex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            captureTex.SetPixels(_webCamTexture.GetPixels());
            byte[] jpegBytes = captureTex.EncodeToJPG(jpegQuality);
            Destroy(captureTex);

            string base64 = Convert.ToBase64String(jpegBytes);
            Debug.Log($"[ScreenCaptureDisplayController] Mobile camera captured: {width}x{height}, base64 length={base64.Length}");
            return base64;
        }

        // ─────────────────────────────────────

        private void OnDestroy()
        {
            // モバイルカメラのクリーンアップ
            if (_webCamTexture != null)
            {
                _webCamTexture.Stop();
                Destroy(_webCamTexture);
                _webCamTexture = null;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            if (_isCapturing && !_isMobileMode)
            {
                ScreenCapture_Stop();
            }
#endif

            if (_texture != null)
            {
                Destroy(_texture);
                _texture = null;
            }

            if (_material != null)
            {
                Destroy(_material);
                _material = null;
            }
        }
    }
}
