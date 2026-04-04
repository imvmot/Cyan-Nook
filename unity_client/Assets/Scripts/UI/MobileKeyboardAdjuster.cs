using UnityEngine;
using System.Runtime.InteropServices;

namespace CyanNook.UI
{
    /// <summary>
    /// iOS Safariでソフトウェアキーボード表示時にチャット入力欄を上に移動する
    /// visualViewport APIでキーボード高さを検出し、RectTransformのY位置を調整する。
    /// デスクトップブラウザやエディターでは何も起きない。
    /// </summary>
    public class MobileKeyboardAdjuster : MonoBehaviour
    {
        [Tooltip("移動対象の入力欄コンテナ（画面下部の入力バー）")]
        public RectTransform chatInputContainer;

        [Tooltip("ルートCanvas（座標変換用）")]
        public Canvas rootCanvas;

        private float _originalAnchoredY;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void MobileKeyboard_Register(string callbackObjectName);

        [DllImport("__Internal")]
        private static extern void MobileKeyboard_Unregister();
#endif

        private void Start()
        {
            if (chatInputContainer != null)
            {
                _originalAnchoredY = chatInputContainer.anchoredPosition.y;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            MobileKeyboard_Register(gameObject.name);
            Debug.Log("[MobileKeyboardAdjuster] Registered for keyboard events");
#endif
        }

        private void OnDestroy()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            MobileKeyboard_Unregister();
#endif
        }

        /// <summary>
        /// jslibからのSendMessageコールバック
        /// キーボード高さに応じて入力欄のY位置を調整
        /// </summary>
        public void OnKeyboardHeightChanged(string heightStr)
        {
            if (chatInputContainer == null) return;

            if (!float.TryParse(heightStr, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float keyboardHeight))
            {
                return;
            }

            if (keyboardHeight <= 0f)
            {
                // キーボード非表示: 元の位置に戻す
                chatInputContainer.anchoredPosition = new Vector2(
                    chatInputContainer.anchoredPosition.x,
                    _originalAnchoredY
                );
                Debug.Log("[MobileKeyboardAdjuster] Keyboard hidden, restored position");
            }
            else
            {
                // キーボード表示: 入力欄を上に移動
                float scaleFactor = rootCanvas != null ? rootCanvas.scaleFactor : 1f;
                float adjustedHeight = keyboardHeight / scaleFactor;

                chatInputContainer.anchoredPosition = new Vector2(
                    chatInputContainer.anchoredPosition.x,
                    _originalAnchoredY + adjustedHeight
                );
                Debug.Log($"[MobileKeyboardAdjuster] Keyboard height={keyboardHeight}px, adjusted={adjustedHeight}u");
            }
        }
    }
}
