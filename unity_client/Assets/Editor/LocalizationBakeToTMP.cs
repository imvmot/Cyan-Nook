using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Localization;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;
using UnityEngine.Localization.Tables;
using UnityEngine.SceneManagement;

namespace CyanNook.Editor
{
    /// <summary>
    /// 現在開いているシーンの LocalizeStringEvent を巡回し、Japanese String Table の値を
    /// 対象 TMP_Text の text にベイクする (シリアライズデフォルト値として焼き付け)。
    ///
    /// 用途: unityroom版ビルドでは Addressables 初期化が失敗し Localization が機能しない
    /// (settings.json 配信ブロック)。LocalizeStringEvent は実行時に発火しなくなり、
    /// TMP_Text は Inspector に保存されたシリアライズ値のまま表示される。
    /// このスクリプトでその値を日本語に揃えておけば、unityroom版でも日本語が表示される。
    ///
    /// メニュー: CyanNook > Localization > Bake Japanese to Active Scene TMPs
    ///
    /// シーン内のプレハブインスタンスはシーンオーバーライドとしてベイクされる
    /// (元のプレハブアセットは変更しない)。
    /// </summary>
    public static class LocalizationBakeToTMP
    {
        private const string TargetLocaleCode = "ja";

        [MenuItem("CyanNook/Localization/Bake Japanese to Active Scene TMPs")]
        public static void BakeJapaneseToActiveScene()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
            {
                EditorUtility.DisplayDialog("エラー",
                    "アクティブなシーンが保存されていないか無効です。シーンを開いてから再実行してください。",
                    "OK");
                return;
            }

            if (scene.isDirty)
            {
                if (!EditorUtility.DisplayDialog("未保存の変更",
                    $"アクティブシーン「{scene.name}」に未保存の変更があります。\n" +
                    "ベイク前にシーンを保存することを推奨します。\n\n続行しますか？",
                    "続行", "キャンセル"))
                    return;
            }

            if (!EditorUtility.DisplayDialog(
                "日本語ベイク (アクティブシーンのみ)",
                $"対象シーン: {scene.path}\n\n" +
                "このシーン内の LocalizeStringEvent から日本語訳を解決し、\n" +
                "対象 TMP_Text の text に上書き保存します。\n\n" +
                "・プレハブインスタンスはシーンオーバーライドとして焼き付けられます\n" +
                "・元のプレハブアセットは変更されません\n" +
                "・元に戻すには git revert が必要です\n\n" +
                "実行しますか？",
                "実行", "キャンセル"))
                return;

            var collections = LocalizationEditorSettings.GetStringTableCollections().ToList();
            if (collections.Count == 0)
            {
                EditorUtility.DisplayDialog("エラー", "プロジェクト内に StringTableCollection が見つかりません", "OK");
                return;
            }

            var jaIdentifier = new LocaleIdentifier(TargetLocaleCode);
            int totalEvents = 0;
            int totalBaked = 0;
            int totalSkipped = 0;
            var skipReasons = new Dictionary<string, int>();

            foreach (var root in scene.GetRootGameObjects())
            {
                var events = root.GetComponentsInChildren<LocalizeStringEvent>(true);
                foreach (var ev in events)
                {
                    totalEvents++;
                    if (TryBake(ev, jaIdentifier, out string skipReason))
                    {
                        totalBaked++;
                    }
                    else
                    {
                        totalSkipped++;
                        IncrementReason(skipReasons, skipReason);
                    }
                }
            }

            if (totalBaked > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log($"[LocalizationBaker] Saved scene: {scene.path} ({totalBaked} TMP baked)");
            }

            string skipDetail = "";
            foreach (var kv in skipReasons)
                skipDetail += $"  - {kv.Key}: {kv.Value}\n";

            string msg = $"完了\n\n" +
                $"対象シーン: {scene.name}\n" +
                $"総 LocalizeStringEvent: {totalEvents}\n" +
                $"日本語ベイク済: {totalBaked}\n" +
                $"スキップ: {totalSkipped}\n\n" +
                (skipDetail.Length > 0 ? "スキップ内訳:\n" + skipDetail : "");
            Debug.Log("[LocalizationBaker] " + msg.Replace('\n', ' '));
            EditorUtility.DisplayDialog("ベイク完了", msg, "OK");
        }

        private static bool TryBake(LocalizeStringEvent ev, LocaleIdentifier ja, out string skipReason)
        {
            skipReason = null;
            var stringRef = ev.StringReference;
            if (stringRef == null || stringRef.IsEmpty)
            {
                skipReason = "StringReference empty";
                return false;
            }

            var collection = LocalizationEditorSettings.GetStringTableCollection(stringRef.TableReference);
            if (collection == null)
            {
                skipReason = "Table collection not found";
                return false;
            }

            var jaTable = collection.GetTable(ja) as StringTable;
            if (jaTable == null)
            {
                skipReason = "Japanese table not found";
                return false;
            }

            StringTableEntry entry = null;
            var entryRef = stringRef.TableEntryReference;
            if (entryRef.ReferenceType == TableEntryReference.Type.Id && entryRef.KeyId != 0)
                entry = jaTable.GetEntry(entryRef.KeyId);
            else if (entryRef.ReferenceType == TableEntryReference.Type.Name && !string.IsNullOrEmpty(entryRef.Key))
                entry = jaTable.GetEntry(entryRef.Key);

            if (entry == null)
            {
                skipReason = "Entry not in Japanese table";
                return false;
            }

            string jaText = entry.Value;
            if (string.IsNullOrEmpty(jaText))
            {
                skipReason = "Japanese entry empty";
                return false;
            }

            var targets = FindTargetTMPs(ev);
            if (targets.Count == 0)
            {
                skipReason = "No TMP_Text target";
                return false;
            }

            bool any = false;
            foreach (var tmp in targets)
            {
                if (tmp.text != jaText)
                {
                    Undo.RecordObject(tmp, "Bake Japanese");
                    tmp.text = jaText;
                    EditorUtility.SetDirty(tmp);
                    any = true;
                }
            }
            if (!any) skipReason = "Already up to date";
            return any;
        }

        private static List<TMP_Text> FindTargetTMPs(LocalizeStringEvent ev)
        {
            var results = new List<TMP_Text>();
            var unityEvent = ev.OnUpdateString;
            if (unityEvent != null)
            {
                int count = unityEvent.GetPersistentEventCount();
                for (int i = 0; i < count; i++)
                {
                    var target = unityEvent.GetPersistentTarget(i);
                    if (target is TMP_Text tmp && !results.Contains(tmp))
                        results.Add(tmp);
                }
            }
            if (results.Count == 0)
            {
                var tmp = ev.GetComponent<TMP_Text>();
                if (tmp != null) results.Add(tmp);
            }
            return results;
        }

        private static void IncrementReason(Dictionary<string, int> dict, string reason)
        {
            if (string.IsNullOrEmpty(reason)) return;
            if (dict.ContainsKey(reason)) dict[reason]++;
            else dict[reason] = 1;
        }
    }
}
