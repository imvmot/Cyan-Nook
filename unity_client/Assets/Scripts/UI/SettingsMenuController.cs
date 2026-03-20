using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections;
using System.Collections.Generic;

namespace CyanNook.UI
{
    /// <summary>
    /// 上部アイコンメニューバーとパネル開閉を管理するコントローラー
    /// アイコンホバーでツールチップ表示、クリックで設定パネルを展開
    /// </summary>
    public class SettingsMenuController : MonoBehaviour
    {
        [System.Serializable]
        public class MenuEntry
        {
            [Tooltip("アイコンボタン")]
            public Button iconButton;

            [Tooltip("ホバー時に表示するツールチップ（ラベル＋背景）")]
            public CanvasGroup tooltipGroup;

            [Tooltip("ツールチップのRectTransform（スライドアニメ用）")]
            public RectTransform tooltipRect;

            [Tooltip("対応する設定パネルのGameObject")]
            public GameObject panel;
        }

        [Header("Menu Entries")]
        [Tooltip("アイコン順: Avatar, LLM, Voice, Other, Debug")]
        public MenuEntry[] entries;

        [Header("Panel Area")]
        [Tooltip("パネル表示エリアのCanvasGroup（フェード用）")]
        public CanvasGroup panelAreaGroup;

        [Tooltip("パネル表示エリアのRectTransform（展開アニメ用）")]
        public RectTransform panelAreaRect;

        [Header("Backdrop")]
        [Tooltip("パネル外クリックで閉じるための全画面背景（CanvasGroup）")]
        public CanvasGroup backdropGroup;

        [Tooltip("Backdrop上のボタン（クリックでパネルを閉じる）")]
        public Button backdropButton;

        [Header("Animation")]
        [Tooltip("ツールチップのフェード時間")]
        public float hoverFadeDuration = 0.15f;

        [Tooltip("パネル展開/閉じの時間")]
        public float panelExpandDuration = 0.2f;

        [Tooltip("ツールチップのスライド距離（下方向ピクセル）")]
        public float tooltipSlideDistance = 10f;

        /// <summary>現在開いているパネルのインデックス（-1 = 全閉）</summary>
        public int ActivePanelIndex => _activeIndex;

        /// <summary>パネルが開いているかどうか</summary>
        public bool IsPanelOpen => _activeIndex >= 0;

        /// <summary>パネルが閉じられた時に発火</summary>
        public event System.Action OnPanelClosed;

        private int _activeIndex = -1;
        private Coroutine _expandCoroutine;

        // ツールチップアニメーション管理
        private Coroutine[] _tooltipCoroutines;
        private Vector2[] _tooltipOriginalPositions;

        // パネルエリアの展開状態管理
        private float _panelAreaExpandedHeight;

        private void Start()
        {
            if (entries == null || entries.Length == 0) return;

            _tooltipCoroutines = new Coroutine[entries.Length];
            _tooltipOriginalPositions = new Vector2[entries.Length];

            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                int index = i; // ローカルコピー（クロージャ用）

                // ツールチップ初期状態: 非表示
                if (entry.tooltipGroup != null)
                {
                    entry.tooltipGroup.alpha = 0f;
                    entry.tooltipGroup.blocksRaycasts = false;
                }

                // ツールチップの元位置を記録
                if (entry.tooltipRect != null)
                {
                    _tooltipOriginalPositions[i] = entry.tooltipRect.anchoredPosition;
                }

                // パネル初期状態: 非表示
                if (entry.panel != null)
                {
                    entry.panel.SetActive(false);
                }

                // ボタンクリック
                if (entry.iconButton != null)
                {
                    entry.iconButton.onClick.AddListener(() => OnIconClicked(index));

                    // ホバーイベント登録（EventTrigger使用）
                    SetupHoverEvents(entry.iconButton.gameObject, index);
                }
            }

            // パネルエリア初期状態
            if (panelAreaGroup != null)
            {
                panelAreaGroup.alpha = 0f;
                panelAreaGroup.blocksRaycasts = false;
                panelAreaGroup.interactable = false;
            }

            // 展開時の高さを記録してから非表示にする
            if (panelAreaRect != null)
            {
                _panelAreaExpandedHeight = panelAreaRect.sizeDelta.y;
                panelAreaRect.gameObject.SetActive(false);
            }

            // Backdrop初期状態: 非表示
            if (backdropGroup != null)
            {
                backdropGroup.alpha = 0f;
                backdropGroup.blocksRaycasts = false;
                backdropGroup.interactable = false;
                backdropGroup.gameObject.SetActive(false);
            }

            // BackdropボタンにClosePanel()を登録
            if (backdropButton != null)
            {
                backdropButton.onClick.AddListener(ClosePanel);
            }
        }

        private void OnDestroy()
        {
            if (entries == null) return;

            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].iconButton != null)
                {
                    entries[i].iconButton.onClick.RemoveAllListeners();
                }
            }

            // Backdropボタンのリスナーをクリーンアップ
            if (backdropButton != null)
            {
                backdropButton.onClick.RemoveAllListeners();
            }
        }

        /// <summary>
        /// ホバーイベントをEventTriggerで登録
        /// </summary>
        private void SetupHoverEvents(GameObject target, int index)
        {
            var trigger = target.GetComponent<EventTrigger>();
            if (trigger == null)
            {
                trigger = target.AddComponent<EventTrigger>();
            }

            // PointerEnter
            var enterEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enterEntry.callback.AddListener((_) => OnIconHoverEnter(index));
            trigger.triggers.Add(enterEntry);

            // PointerExit
            var exitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exitEntry.callback.AddListener((_) => OnIconHoverExit(index));
            trigger.triggers.Add(exitEntry);
        }

        // ─────────────────────────────────────
        // ホバー
        // ─────────────────────────────────────

        private void OnIconHoverEnter(int index)
        {
            if (index < 0 || index >= entries.Length) return;
            var entry = entries[index];
            if (entry.tooltipGroup == null) return;

            if (_tooltipCoroutines[index] != null)
            {
                StopCoroutine(_tooltipCoroutines[index]);
            }

            _tooltipCoroutines[index] = StartCoroutine(
                AnimateTooltip(index, targetAlpha: 1f, slideDown: true));
        }

        private void OnIconHoverExit(int index)
        {
            if (index < 0 || index >= entries.Length) return;
            var entry = entries[index];
            if (entry.tooltipGroup == null) return;

            if (_tooltipCoroutines[index] != null)
            {
                StopCoroutine(_tooltipCoroutines[index]);
            }

            _tooltipCoroutines[index] = StartCoroutine(
                AnimateTooltip(index, targetAlpha: 0f, slideDown: false));
        }

        /// <summary>
        /// ツールチップのフェード＋スライドアニメーション
        /// </summary>
        private IEnumerator AnimateTooltip(int index, float targetAlpha, bool slideDown)
        {
            var entry = entries[index];
            var group = entry.tooltipGroup;
            var rect = entry.tooltipRect;
            var originalPos = _tooltipOriginalPositions[index];

            float startAlpha = group.alpha;
            Vector2 startPos = rect != null ? rect.anchoredPosition : Vector2.zero;
            Vector2 targetPos = slideDown
                ? originalPos + new Vector2(0, -tooltipSlideDistance)
                : originalPos;

            float elapsed = 0f;
            while (elapsed < hoverFadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / hoverFadeDuration);
                // EaseOut
                t = 1f - (1f - t) * (1f - t);

                group.alpha = Mathf.Lerp(startAlpha, targetAlpha, t);
                if (rect != null)
                {
                    rect.anchoredPosition = Vector2.Lerp(startPos, targetPos, t);
                }

                yield return null;
            }

            group.alpha = targetAlpha;
            if (rect != null)
            {
                rect.anchoredPosition = targetPos;
            }

            group.blocksRaycasts = targetAlpha > 0.5f;
        }

        // ─────────────────────────────────────
        // パネル開閉
        // ─────────────────────────────────────

        private void OnIconClicked(int index)
        {
            if (_activeIndex == index)
            {
                // 同じアイコンをクリック → 閉じる
                ClosePanel();
            }
            else
            {
                // 別のアイコン → 切り替え
                OpenPanel(index);
            }
        }

        /// <summary>
        /// 指定インデックスのパネルを開く
        /// </summary>
        public void OpenPanel(int index)
        {
            if (index < 0 || index >= entries.Length) return;

            // 現在のパネルを非表示
            if (_activeIndex >= 0 && _activeIndex < entries.Length)
            {
                if (entries[_activeIndex].panel != null)
                {
                    entries[_activeIndex].panel.SetActive(false);
                }
            }

            _activeIndex = index;

            // 新しいパネルを表示
            if (entries[index].panel != null)
            {
                entries[index].panel.SetActive(true);
            }

            // パネルエリアの展開アニメーション
            if (_expandCoroutine != null)
            {
                StopCoroutine(_expandCoroutine);
            }
            _expandCoroutine = StartCoroutine(AnimatePanelArea(open: true));
        }

        /// <summary>
        /// パネルを閉じる
        /// </summary>
        public void ClosePanel()
        {
            if (_expandCoroutine != null)
            {
                StopCoroutine(_expandCoroutine);
            }
            _expandCoroutine = StartCoroutine(AnimatePanelArea(open: false));
        }

        /// <summary>
        /// パネルエリアの展開/折り畳みアニメーション
        /// </summary>
        private IEnumerator AnimatePanelArea(bool open)
        {
            if (panelAreaRect == null || panelAreaGroup == null)
            {
                // アニメーション不要の場合は即座に切り替え
                if (!open && _activeIndex >= 0 && _activeIndex < entries.Length)
                {
                    if (entries[_activeIndex].panel != null)
                    {
                        entries[_activeIndex].panel.SetActive(false);
                    }
                    _activeIndex = -1;
                }
                yield break;
            }

            // 開く場合はGameObjectをアクティブにする
            if (open)
            {
                panelAreaRect.gameObject.SetActive(true);
                if (backdropGroup != null)
                {
                    backdropGroup.gameObject.SetActive(true);
                }
            }

            float startAlpha = panelAreaGroup.alpha;
            float targetAlpha = open ? 1f : 0f;

            // Backdropの開始/終了alpha
            float backdropStartAlpha = backdropGroup != null ? backdropGroup.alpha : 0f;
            float backdropTargetAlpha = targetAlpha; // パネルと同期

            float elapsed = 0f;
            while (elapsed < panelExpandDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / panelExpandDuration);
                // EaseOut
                t = 1f - (1f - t) * (1f - t);

                panelAreaGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, t);

                // Backdropもフェード
                if (backdropGroup != null)
                {
                    backdropGroup.alpha = Mathf.Lerp(backdropStartAlpha, backdropTargetAlpha, t);
                }

                yield return null;
            }

            panelAreaGroup.alpha = targetAlpha;
            panelAreaGroup.blocksRaycasts = open;
            panelAreaGroup.interactable = open;

            // Backdrop最終状態
            if (backdropGroup != null)
            {
                backdropGroup.alpha = backdropTargetAlpha;
                backdropGroup.blocksRaycasts = open;
                backdropGroup.interactable = open;
            }

            // 閉じる場合はパネルを非表示にする
            if (!open)
            {
                if (_activeIndex >= 0 && _activeIndex < entries.Length)
                {
                    if (entries[_activeIndex].panel != null)
                    {
                        entries[_activeIndex].panel.SetActive(false);
                    }
                }
                _activeIndex = -1;
                panelAreaRect.gameObject.SetActive(false);

                if (backdropGroup != null)
                {
                    backdropGroup.gameObject.SetActive(false);
                }

                OnPanelClosed?.Invoke();
            }

            _expandCoroutine = null;
        }
    }
}
