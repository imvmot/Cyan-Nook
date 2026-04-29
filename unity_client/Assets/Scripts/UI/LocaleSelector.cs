using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization.Settings;
using TMPro;

namespace CyanNook.UI
{
    /// <summary>
    /// 言語選択プルダウン。LocalizationSettings.SelectedLocale を切替え、
    /// 選択された Locale Code を PlayerPrefs に永続化する。
    /// </summary>
    public class LocaleSelector : MonoBehaviour
    {
        public const string PrefKey = "ui_locale";

        [Tooltip("言語選択プルダウン（TMP_Dropdown）")]
        public TMP_Dropdown localeDropdown;

        [Tooltip("unityroom版で非表示にするコンテナ（言語選択行のラベル+ドロップダウンを含む親など）。未設定なら localeDropdown のGameObjectのみを非アクティブ化")]
        public GameObject containerToHideOnUnityroom;

        private bool _suppressCallback;

        private IEnumerator Start()
        {
#if UNITYROOM_BUILD
            // unityroomは "settings.json" 配信ブロックで Localization が初期化できないため、
            // 日本語固定運用とし言語選択UIを非表示にする。
            // このコンポーネントが他のUI/ロジックと同居するルートGameObjectに付いている場合
            // gameObject を無効化すると同居コンポーネントも止まってしまうため、
            // containerToHideOnUnityroom か localeDropdown のGameObjectだけを対象にする。
            GameObject hideTarget = null;
            if (containerToHideOnUnityroom != null)
                hideTarget = containerToHideOnUnityroom;
            else if (localeDropdown != null)
                hideTarget = localeDropdown.gameObject;

            if (hideTarget != null)
                hideTarget.SetActive(false);
            yield break;
#else
            yield return LocalizationSettings.InitializationOperation;

            var locales = LocalizationSettings.AvailableLocales.Locales;
            if (locales == null || locales.Count == 0)
            {
                Debug.LogWarning("[LocaleSelector] No available locales configured.");
                yield break;
            }

            // 保存済み Locale Code を優先選択
            string savedCode = PlayerPrefs.GetString(PrefKey, null);
            int currentIdx = 0;
            for (int i = 0; i < locales.Count; i++)
            {
                if (!string.IsNullOrEmpty(savedCode) &&
                    locales[i].Identifier.Code == savedCode)
                {
                    LocalizationSettings.SelectedLocale = locales[i];
                    currentIdx = i;
                    break;
                }
                if (locales[i] == LocalizationSettings.SelectedLocale)
                {
                    currentIdx = i;
                }
            }

            if (localeDropdown == null) yield break;

            _suppressCallback = true;
            localeDropdown.ClearOptions();
            var options = new List<string>(locales.Count);
            foreach (var l in locales) options.Add(l.LocaleName);
            localeDropdown.AddOptions(options);
            localeDropdown.value = currentIdx;
            localeDropdown.RefreshShownValue();
            _suppressCallback = false;

            localeDropdown.onValueChanged.AddListener(OnDropdownChanged);
#endif
        }

        private void OnDestroy()
        {
            if (localeDropdown != null)
                localeDropdown.onValueChanged.RemoveListener(OnDropdownChanged);
        }

        private void OnDropdownChanged(int index)
        {
            if (_suppressCallback) return;

            var locales = LocalizationSettings.AvailableLocales.Locales;
            if (index < 0 || index >= locales.Count) return;

            LocalizationSettings.SelectedLocale = locales[index];
            PlayerPrefs.SetString(PrefKey, locales[index].Identifier.Code);
            PlayerPrefs.Save();
            Debug.Log($"[LocaleSelector] Locale set to {locales[index].Identifier.Code}");
        }
    }
}
