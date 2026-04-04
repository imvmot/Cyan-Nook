using UnityEngine;
using System.Runtime.InteropServices;
using TMPro;

namespace CyanNook.UI
{
    /// <summary>
    /// ブラウザClipboard APIとUnityの橋渡し
    /// iOS Safariではユーザージェスチャー（ボタンタップ）からのAPI呼び出しが必要なため、
    /// ペーストボタン経由��クリップボード読み取りを行う。
    /// </summary>
    public class ClipboardBridge : MonoBehaviour
    {
        [Tooltip("ペースト先のTMP_InputField")]
        public TMP_InputField targetInputField;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void Clipboard_CopyText(string text);

        [DllImport("__Internal")]
        private static extern void Clipboard_RequestPaste(string callbackObjectName, string callbackMethodName);
#endif

        /// <summary>
        /// テキストをクリップボードにコピー
        /// </summary>
        public void CopyToClipboard(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

#if UNITY_WEBGL && !UNITY_EDITOR
            Clipboard_CopyText(text);
#else
            GUIUtility.systemCopyBuffer = text;
            Debug.Log($"[ClipboardBridge] Copied to clipboard ({text.Length} chars)");
#endif
        }

        /// <summary>
        /// クリップボードからテキストを読み取りリクエスト
        /// ボタンのonClickから呼び出すこと（ユーザージェスチャー必須）
        /// </summary>
        public void RequestPaste()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            Clipboard_RequestPaste(gameObject.name, "OnClipboardPasteReceived");
#else
            // エディター: systemCopyBuffer��ら直接ペースト
            string text = GUIUtility.systemCopyBuffer;
            OnClipboardPasteReceived(text);
#endif
        }

        /// <summary>
        /// jslibからのSendMessageコールバック
        /// クリップボードの内容をInputFieldのキャレット位置に挿入
        /// </summary>
        public void OnClipboardPasteReceived(string text)
        {
            if (string.IsNullOrEmpty(text) || targetInputField == null) return;

            int caretPos = targetInputField.caretPosition;
            string current = targetInputField.text;

            // キャレット位置にテキストを挿入
            caretPos = Mathf.Clamp(caretPos, 0, current.Length);
            targetInputField.text = current.Insert(caretPos, text);

            // キャレットを挿入テキストの末尾に移動
            int newCaretPos = caretPos + text.Length;
            targetInputField.caretPosition = newCaretPos;
            targetInputField.selectionAnchorPosition = newCaretPos;
            targetInputField.selectionFocusPosition = newCaretPos;

            targetInputField.ActivateInputField();

            Debug.Log($"[ClipboardBridge] Pasted {text.Length} chars at position {caretPos}");
        }
    }
}
