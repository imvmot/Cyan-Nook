using System.Runtime.InteropServices;
using UnityEngine;

namespace CyanNook.Core
{
    /// <summary>
    /// ユーザーのデスクトップ/ウィンドウ画面をキャプチャし、シーン内のQuad（MeshRenderer）に表示する
    /// ブラウザのScreen Capture API（getDisplayMedia）を使用
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

        /// <summary>
        /// 画面キャプチャが再生中かどうか
        /// </summary>
        public bool IsPlaying => _isCapturing;

        private void Start()
        {
            _renderer = GetComponent<Renderer>();

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
        /// 画面キャプチャを開始（ブラウザの画面共有ダイアログが表示される）
        /// </summary>
        public void StartCapture()
        {
            if (_isCapturing || _waitingForCapture)
            {
                Debug.Log("[ScreenCaptureDisplayController] Already capturing");
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
        /// 画面キャプチャを停止
        /// </summary>
        public void StopCapture()
        {
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

        private void OnDestroy()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (_isCapturing)
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
