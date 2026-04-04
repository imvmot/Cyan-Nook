using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using System.Runtime.InteropServices;
using TMPro;

namespace CyanNook.UI
{
    /// <summary>
    /// ブラウザClipboard APIとUnityの橋渡し + コンテキストメニュー表示
    /// 右クリック（PC）/ 長押し（モバイル）でカーソル位置にCopy/Pasteメニューを表示する。
    /// 全てのTMP_InputFieldに対して自動で動作する（フォーカス中のInputFieldを自動検出）。
    /// </summary>
    public class ClipboardBridge : MonoBehaviour
    {
        [Header("Context Menu")]
        [Tooltip("コンテキストメニューパネル（Copy/Pasteボタンを含む）")]
        public RectTransform contextMenuPanel;

        [Tooltip("コピーボタン")]
        public Button copyButton;

        [Tooltip("ペーストボタン")]
        public Button pasteButton;

        [Tooltip("ルートCanvas（座標変換用）")]
        public Canvas rootCanvas;

        [Header("Long Press Settings")]
        [Tooltip("長押し判定時間（秒）")]
        public float longPressDuration = 0.5f;

        [Tooltip("長押し判定の移動許容量（ピクセル）。モバイルタッチでは指のブレがあるため大きめに設定")]
        public float longPressMoveThreshold = 30f;

        // 現在操作中のInputField（フォーカスから自動検出）
        private TMP_InputField _activeInputField;

        // 長押し検出用
        private bool _isPointerDown;
        private float _pointerDownTime;
        private Vector2 _pointerDownPosition;
        private bool _longPressTriggered;

        // メニュー表示時のポインター位置（フォールバック用）
        private Vector2 _lastPointerPosition;

        // メニュー表示時刻（表示直後のタップで誤って閉じるのを防止）
        private float _menuShownTime;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void Clipboard_CopyText(string text);

        [DllImport("__Internal")]
        private static extern void Clipboard_RequestPaste(string callbackObjectName, string callbackMethodName);
#endif

        private void Start()
        {
            if (copyButton != null)
                copyButton.onClick.AddListener(OnCopyClicked);
            if (pasteButton != null)
                pasteButton.onClick.AddListener(OnPasteClicked);

            HideContextMenu();
        }

        private void OnDestroy()
        {
            if (copyButton != null)
                copyButton.onClick.RemoveListener(OnCopyClicked);
            if (pasteButton != null)
                pasteButton.onClick.RemoveListener(OnPasteClicked);
        }

        private void Update()
        {
            // Pointer: Mouse/Touch/Pen を統一的に扱う（WebGLモバイルでの互換性確保）
            var pointer = Pointer.current;
            if (pointer == null) return;

            bool leftDown = pointer.press.wasPressedThisFrame;
            bool leftUp = pointer.press.wasReleasedThisFrame;
            bool rightDown = Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;

            Vector2 pointerPos = pointer.position.ReadValue();

            // --- 右クリック検出（PC） ---
            if (rightDown)
            {
                var inputField = GetInputFieldUnderPointer(pointerPos);
                if (inputField != null)
                {
                    _activeInputField = inputField;
                    _lastPointerPosition = pointerPos;
                    ShowContextMenuAtCaret();
                    return;
                }
            }

            // --- 長押し検出（モバイル/PC共用） ---
            if (leftDown)
            {
                // メニュー表示中にメニュー外をクリック → 閉じる
                // 表示直後（0.5秒以内）はボタンタップを優先するため閉じない
                if (contextMenuPanel != null && contextMenuPanel.gameObject.activeSelf)
                {
                    if (Time.unscaledTime - _menuShownTime < 0.5f)
                    {
                        // 表示直後: 閉じずにボタンのonClickに委ねる
                        return;
                    }
                    if (!IsPointerOverContextMenu(pointerPos))
                    {
                        HideContextMenu();
                        return;
                    }
                }

                // InputField上ならば長押し計測開始
                var inputField = GetInputFieldUnderPointer(pointerPos);
                if (inputField != null)
                {
                    _isPointerDown = true;
                    _pointerDownTime = Time.unscaledTime;
                    _pointerDownPosition = pointerPos;
                    _longPressTriggered = false;
                    _activeInputField = inputField;
                    Debug.Log($"[ClipboardBridge] Pointer down on InputField: {inputField.name} at {pointerPos}");
                }
            }

            if (leftUp)
            {
                _isPointerDown = false;
            }

            // 長押し判定: 時間経過 & 移動量が許容範囲内
            if (_isPointerDown && !_longPressTriggered)
            {
                float elapsed = Time.unscaledTime - _pointerDownTime;
                float moved = Vector2.Distance(pointerPos, _pointerDownPosition);

                if (moved > longPressMoveThreshold)
                {
                    // 移動した → 長押しキャンセル（ドラッグ選択等）
                    _isPointerDown = false;
                }
                else if (elapsed >= longPressDuration)
                {
                    _longPressTriggered = true;
                    _isPointerDown = false;
                    _lastPointerPosition = pointerPos;
                    Debug.Log($"[ClipboardBridge] Long press detected on {_activeInputField?.name}");
                    ShowContextMenuAtCaret();
                }
            }
        }

        // --- InputField検出 ---

        /// <summary>
        /// 指定スクリーン位置にあるTMP_InputFieldを取得
        /// </summary>
        private TMP_InputField GetInputFieldUnderPointer(Vector2 screenPosition)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null) return null;

            var pointerData = new PointerEventData(eventSystem)
            {
                position = screenPosition
            };

            var results = new System.Collections.Generic.List<RaycastResult>();
            eventSystem.RaycastAll(pointerData, results);

            foreach (var result in results)
            {
                var inputField = result.gameObject.GetComponentInParent<TMP_InputField>();
                if (inputField != null)
                    return inputField;
            }

            return null;
        }

        /// <summary>
        /// 現在フォーカス中のTMP_InputFieldを取得
        /// </summary>
        private TMP_InputField GetFocusedInputField()
        {
            var selected = EventSystem.current?.currentSelectedGameObject;
            if (selected != null)
            {
                var inputField = selected.GetComponent<TMP_InputField>();
                if (inputField != null && inputField.isFocused)
                    return inputField;
            }
            return null;
        }

        // --- Context Menu ---

        private void ShowContextMenuAtCaret()
        {
            if (contextMenuPanel == null || _activeInputField == null) return;

            Vector2 menuPosition = GetCaretScreenPosition(_activeInputField);
            PositionContextMenu(menuPosition);
            contextMenuPanel.gameObject.SetActive(true);
            _menuShownTime = Time.unscaledTime;
        }

        public void HideContextMenu()
        {
            if (contextMenuPanel != null)
                contextMenuPanel.gameObject.SetActive(false);
        }

        private bool IsPointerOverContextMenu(Vector2 screenPosition)
        {
            if (contextMenuPanel == null) return false;

            Camera cam = rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? rootCanvas.worldCamera : null;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                contextMenuPanel, screenPosition, cam, out Vector2 localPoint))
            {
                return contextMenuPanel.rect.Contains(localPoint);
            }
            return false;
        }

        private Vector2 GetCaretScreenPosition(TMP_InputField inputField)
        {
            var textComponent = inputField.textComponent;
            var textInfo = textComponent.textInfo;
            int caretPos = inputField.caretPosition;

            Camera cam = rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? rootCanvas.worldCamera : null;

            if (textInfo.characterCount > 0 && caretPos > 0)
            {
                int charIndex = Mathf.Clamp(caretPos - 1, 0, textInfo.characterCount - 1);
                var charInfo = textInfo.characterInfo[charIndex];

                Vector3 localPos = new Vector3(charInfo.topRight.x, charInfo.topRight.y, 0);
                Vector3 worldPos = textComponent.transform.TransformPoint(localPos);
                return RectTransformUtility.WorldToScreenPoint(cam, worldPos);
            }

            // フォールバック: クリック/長押し位置を使用
            return _lastPointerPosition;
        }

        private void PositionContextMenu(Vector2 screenPosition)
        {
            if (contextMenuPanel == null || rootCanvas == null) return;

            Camera cam = rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? rootCanvas.worldCamera : null;

            RectTransform parentRect = contextMenuPanel.parent as RectTransform;
            if (parentRect == null) parentRect = rootCanvas.GetComponent<RectTransform>();

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentRect, screenPosition, cam, out Vector2 localPoint);

            // メニューをカーソルの上に配置
            float menuHeight = contextMenuPanel.rect.height;
            localPoint.y += menuHeight * 0.5f + 10f;

            contextMenuPanel.anchoredPosition = localPoint;
        }

        // --- Copy / Paste Actions ---

        private void OnCopyClicked()
        {
            if (_activeInputField == null) return;

            string textToCopy;
            int anchor = _activeInputField.selectionAnchorPosition;
            int focus = _activeInputField.selectionFocusPosition;
            if (anchor != focus)
            {
                int start = Mathf.Min(anchor, focus);
                int end = Mathf.Max(anchor, focus);
                textToCopy = _activeInputField.text.Substring(start, end - start);
            }
            else
            {
                textToCopy = _activeInputField.text;
            }

            CopyToClipboard(textToCopy);
            HideContextMenu();
        }

        private void OnPasteClicked()
        {
            RequestPaste();
            // HideはOnClipboardPasteReceived後に行う
        }

        // --- Clipboard Operations ---

        public void CopyToClipboard(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

#if UNITY_WEBGL && !UNITY_EDITOR
            Clipboard_CopyText(text);
#else
            GUIUtility.systemCopyBuffer = text;
#endif
            Debug.Log($"[ClipboardBridge] Copied {text.Length} chars");
        }

        public void RequestPaste()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            Clipboard_RequestPaste(gameObject.name, "OnClipboardPasteReceived");
#else
            string text = GUIUtility.systemCopyBuffer;
            OnClipboardPasteReceived(text);
#endif
        }

        /// <summary>
        /// jslibからのSendMessageコールバック
        /// </summary>
        public void OnClipboardPasteReceived(string text)
        {
            if (string.IsNullOrEmpty(text) || _activeInputField == null) return;

            int anchor = _activeInputField.selectionAnchorPosition;
            int focus = _activeInputField.selectionFocusPosition;
            string current = _activeInputField.text;

            int insertPos;
            if (anchor != focus)
            {
                int start = Mathf.Min(anchor, focus);
                int end = Mathf.Max(anchor, focus);
                current = current.Remove(start, end - start);
                current = current.Insert(start, text);
                insertPos = start;
            }
            else
            {
                insertPos = Mathf.Clamp(_activeInputField.caretPosition, 0, current.Length);
                current = current.Insert(insertPos, text);
            }

            _activeInputField.text = current;

            int newCaretPos = insertPos + text.Length;
            _activeInputField.caretPosition = newCaretPos;
            _activeInputField.selectionAnchorPosition = newCaretPos;
            _activeInputField.selectionFocusPosition = newCaretPos;
            _activeInputField.ActivateInputField();

            HideContextMenu();
            Debug.Log($"[ClipboardBridge] Pasted {text.Length} chars at position {insertPos}");
        }
    }
}
