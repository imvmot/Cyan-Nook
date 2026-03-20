using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

namespace CyanNook.UI
{
    /// <summary>
    /// TMP_InputFieldでEnterキーによる改行入力を有効にするコンポーネント。
    /// New Input SystemのSubmitアクションがEnterキーを横取りし、
    /// MultiLineNewline設定が正常に機能しない問題のワークアラウンド。
    ///
    /// 仕組み:
    /// - lineTypeをMultiLineSubmitに設定（TMPの内部改行処理を無効化）
    /// - Update()でフォーカス中のキャレット位置を記録（onEndEdit時には0になるため）
    /// - onEndEditでEnterキーを検出し、記録位置に改行を手動挿入してフォーカスを維持
    ///
    /// 使い方: マルチライン対応したいTMP_InputFieldと同じGameObjectにAddComponentする。
    /// </summary>
    [RequireComponent(typeof(TMP_InputField))]
    public class MultiLineInputFieldFix : MonoBehaviour
    {
        private TMP_InputField _inputField;
        private int _lastCaretPosition;

        private void Awake()
        {
            _inputField = GetComponent<TMP_InputField>();
            _inputField.lineType = TMP_InputField.LineType.MultiLineSubmit;
            _inputField.onEndEdit.AddListener(OnEndEdit);
        }

        private void OnDestroy()
        {
            if (_inputField != null)
            {
                _inputField.onEndEdit.RemoveListener(OnEndEdit);
            }
        }

        private void Update()
        {
            // フォーカス中のキャレット位置を毎フレーム記録
            // onEndEdit発火時にはフィールドが非アクティブでcaretPositionが0になるため
            if (_inputField != null && _inputField.isFocused)
            {
                _lastCaretPosition = _inputField.caretPosition;
            }
        }

        private void OnEndEdit(string text)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            // Enterキー以外での終了（クリック等）は無視
            if (!keyboard.enterKey.isPressed && !keyboard.numpadEnterKey.isPressed) return;

            // Shift+Enter: 改行を挿入してフォーカスを維持
            if (keyboard.shiftKey.isPressed)
            {
                int pos = Mathf.Clamp(_lastCaretPosition, 0, text.Length);
                _inputField.text = text.Insert(pos, "\n");
                _inputField.ActivateInputField();
                StartCoroutine(SetCaretPositionNextFrame(pos + 1));
                return;
            }

            // Enter単体: 改行を挿入せず、UIControllerのOnInputEndEditに委ねる
            // （末尾に\nを追加してEnter検出を可能にする）
            _inputField.text = text + "\n";
        }

        private System.Collections.IEnumerator SetCaretPositionNextFrame(int position)
        {
            yield return null;
            if (_inputField != null)
            {
                // キャレット位置を設定し、全選択状態を解除
                _inputField.caretPosition = position;
                _inputField.selectionAnchorPosition = position;
                _inputField.selectionFocusPosition = position;
            }
        }
    }
}
