using System.Collections.Generic;
using System.IO;
using System.Linq;
using CyanNook.Character;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace CyanNook.Editor
{
    /// <summary>
    /// Timeline遷移フローをカテゴリ別の階層リストで可視化するEditorWindow。
    /// 遷移エッジは TransitionRuleData（手書きScriptableObject）から読み込み、
    /// 状態→Timelineの対応は TimelineBindingData から補助的に引く。
    /// </summary>
    public class TransitionViewerWindow : EditorWindow
    {
        private const string PrefRulesPath = "CyanNook.TransitionViewer.RulesPath";
        private const string PrefBindingsPath = "CyanNook.TransitionViewer.BindingsPath";

        private TransitionRuleData _rules;
        private TimelineBindingData _bindings;

        private string _orchestratorFilter = "All";
        private string _search = string.Empty;
        private bool _showIncoming = false;

        private Vector2 _scroll;
        private readonly Dictionary<string, bool> _foldouts = new Dictionary<string, bool>();

        [MenuItem("CyanNook/Animation/Transition Viewer")]
        public static void Open()
        {
            var w = GetWindow<TransitionViewerWindow>("Transition Viewer");
            w.minSize = new Vector2(480f, 360f);
        }

        private void OnEnable()
        {
            string rulesPath = EditorPrefs.GetString(PrefRulesPath, string.Empty);
            if (!string.IsNullOrEmpty(rulesPath))
            {
                _rules = AssetDatabase.LoadAssetAtPath<TransitionRuleData>(rulesPath);
            }
            string bindingsPath = EditorPrefs.GetString(PrefBindingsPath, string.Empty);
            if (!string.IsNullOrEmpty(bindingsPath))
            {
                _bindings = AssetDatabase.LoadAssetAtPath<TimelineBindingData>(bindingsPath);
            }
        }

        private void OnDisable()
        {
            if (_rules != null)
            {
                EditorPrefs.SetString(PrefRulesPath, AssetDatabase.GetAssetPath(_rules));
            }
            if (_bindings != null)
            {
                EditorPrefs.SetString(PrefBindingsPath, AssetDatabase.GetAssetPath(_bindings));
            }
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (_rules == null)
            {
                EditorGUILayout.HelpBox(
                    "TransitionRuleData アセットを指定してください。\n" +
                    "未作成の場合は、上部ツールバーの「Create Default for chr001」ボタン、または " +
                    "Project ウィンドウで Create > CyanNook > Animation > Transition Rule Data から作成できます。",
                    MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawGroupedRules();
            EditorGUILayout.EndScrollView();
        }

        // ────────────────────────────────────────────────
        // Toolbar
        // ────────────────────────────────────────────────

        private void DrawToolbar()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUI.BeginChangeCheck();
            _rules = (TransitionRuleData)EditorGUILayout.ObjectField(
                "Transition Rule Data", _rules, typeof(TransitionRuleData), false);
            _bindings = (TimelineBindingData)EditorGUILayout.ObjectField(
                "Timeline Binding Data", _bindings, typeof(TimelineBindingData), false);
            if (EditorGUI.EndChangeCheck())
            {
                // フィルタ再計算のため再描画
                Repaint();
            }

            EditorGUILayout.BeginHorizontal();
            var orchestrators = CollectOrchestrators();
            int current = Mathf.Max(0, orchestrators.IndexOf(_orchestratorFilter));
            int next = EditorGUILayout.Popup("Orchestrator", current, orchestrators.ToArray());
            _orchestratorFilter = orchestrators[Mathf.Clamp(next, 0, orchestrators.Count - 1)];
            _search = EditorGUILayout.TextField("Search", _search);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            _showIncoming = EditorGUILayout.ToggleLeft(
                "Show Incoming (入ってくる遷移を逆向き表示)", _showIncoming, GUILayout.Width(320f));

            if (GUILayout.Button("Create Default for chr001", GUILayout.Width(200f)))
            {
                CreateDefaultAssetForChr001();
            }

            if (_rules != null && GUILayout.Button("Ping Asset", GUILayout.Width(90f)))
            {
                EditorGUIUtility.PingObject(_rules);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private List<string> CollectOrchestrators()
        {
            var list = new List<string> { "All" };
            if (_rules == null) return list;
            foreach (var r in _rules.rules)
            {
                if (string.IsNullOrWhiteSpace(r.orchestrator)) continue;
                if (!list.Contains(r.orchestrator)) list.Add(r.orchestrator);
            }
            return list;
        }

        // ────────────────────────────────────────────────
        // Body
        // ────────────────────────────────────────────────

        private void DrawGroupedRules()
        {
            if (!string.IsNullOrEmpty(_rules.description))
            {
                EditorGUILayout.HelpBox(_rules.description, MessageType.None);
            }

            var filtered = _rules.rules.Where(PassFilter).ToList();
            if (filtered.Count == 0)
            {
                EditorGUILayout.HelpBox("条件に一致する遷移がありません。", MessageType.Info);
                return;
            }

            var groups = filtered
                .GroupBy(r => string.IsNullOrEmpty(r.category) ? "(uncategorized)" : r.category)
                .OrderBy(g => g.Key, System.StringComparer.Ordinal);

            foreach (var group in groups)
            {
                DrawCategory(group.Key, group.ToList());
            }
        }

        private bool PassFilter(TransitionRuleEntry r)
        {
            if (_orchestratorFilter != "All" && r.orchestrator != _orchestratorFilter) return false;
            if (!string.IsNullOrEmpty(_search))
            {
                string hay = string.Join(" ",
                    r.category, r.fromState, r.fromAnimationId, r.toState, r.toAnimationId,
                    r.orchestrator, r.trigger, r.notes).ToLowerInvariant();
                if (!hay.Contains(_search.ToLowerInvariant())) return false;
            }
            return true;
        }

        private void DrawCategory(string category, List<TransitionRuleEntry> entries)
        {
            string key = $"cat::{category}";
            if (!_foldouts.TryGetValue(key, out bool expanded)) expanded = true;
            expanded = EditorGUILayout.Foldout(expanded, $"{category}  ({entries.Count})", true, EditorStyles.foldoutHeader);
            _foldouts[key] = expanded;
            if (!expanded) return;

            // 状態(AnimationID)ごとにさらにグループ化
            var nodeKey = _showIncoming
                ? (System.Func<TransitionRuleEntry, string>)((e) => FormatNode(e.toState, e.toAnimationId))
                : (e) => FormatNode(e.fromState, e.fromAnimationId);

            var stateGroups = entries
                .GroupBy(nodeKey)
                .OrderBy(g => g.Key, System.StringComparer.Ordinal);

            EditorGUI.indentLevel++;
            foreach (var sg in stateGroups)
            {
                DrawStateNode(category, sg.Key, sg.ToList(), entries.First(e => nodeKey(e) == sg.Key));
            }
            EditorGUI.indentLevel--;

            EditorGUILayout.Space(2f);
        }

        private void DrawStateNode(string category, string nodeKey, List<TransitionRuleEntry> edges, TransitionRuleEntry sample)
        {
            string foldKey = $"node::{category}::{nodeKey}::{_showIncoming}";
            if (!_foldouts.TryGetValue(foldKey, out bool expanded)) expanded = true;

            EditorGUILayout.BeginHorizontal();
            expanded = EditorGUILayout.Foldout(expanded, $"{nodeKey}  ({edges.Count})", true);

            // この nodeKey に対応する Timeline を Bindings から引いて Ping できるようにする
            if (_bindings != null)
            {
                var tl = TryResolveTimeline(_showIncoming ? sample.toState : sample.fromState,
                                            _showIncoming ? sample.toAnimationId : sample.fromAnimationId);
                using (new EditorGUI.DisabledScope(tl == null))
                {
                    if (GUILayout.Button(tl != null ? "Ping TL" : "—", GUILayout.Width(60f)))
                    {
                        if (tl != null) EditorGUIUtility.PingObject(tl);
                    }
                }
            }
            EditorGUILayout.EndHorizontal();
            _foldouts[foldKey] = expanded;

            if (!expanded) return;

            EditorGUI.indentLevel++;
            foreach (var e in edges)
            {
                DrawEdge(e);
            }
            EditorGUI.indentLevel--;
        }

        private void DrawEdge(TransitionRuleEntry e)
        {
            string arrow = _showIncoming ? "←" : "→";
            string otherSide = _showIncoming
                ? FormatNode(e.fromState, e.fromAnimationId)
                : FormatNode(e.toState, e.toAnimationId);

            string orch = string.IsNullOrEmpty(e.orchestrator) ? "-" : e.orchestrator;
            string trig = string.IsNullOrEmpty(e.trigger) ? string.Empty : $" / {e.trigger}";

            var rect = EditorGUILayout.GetControlRect();
            string label = $"{arrow} {otherSide}    [{orch}{trig}]";
            EditorGUI.LabelField(rect, label);

            // 行をクリックしたら notes をツールチップ風に表示する代わりに、ログ出力+選択
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                if (!string.IsNullOrEmpty(e.notes))
                {
                    Debug.Log($"[TransitionViewer] {FormatNode(e.fromState, e.fromAnimationId)} → " +
                              $"{FormatNode(e.toState, e.toAnimationId)}\n  Trigger: {e.trigger}\n  Notes: {e.notes}");
                }
                EditorGUIUtility.PingObject(_rules);
                Event.current.Use();
            }
        }

        // ────────────────────────────────────────────────
        // Helpers
        // ────────────────────────────────────────────────

        private static string FormatNode(AnimationStateType state, string animationId)
        {
            return string.IsNullOrEmpty(animationId) ? state.ToString() : $"{state}({animationId})";
        }

        private TimelineAsset TryResolveTimeline(AnimationStateType state, string animationId)
        {
            if (_bindings == null) return null;
            if (!string.IsNullOrEmpty(animationId))
            {
                var byId = _bindings.GetTimelineByAnimationId(animationId);
                if (byId != null) return byId;
            }
            return _bindings.GetTimeline(state);
        }

        // ────────────────────────────────────────────────
        // Default asset generator (chr001)
        // ────────────────────────────────────────────────

        [MenuItem("CyanNook/Animation/Create Default Transition Rules (chr001)")]
        public static void CreateDefaultAssetForChr001Menu()
        {
            CreateDefaultAssetForChr001();
        }

        private static void CreateDefaultAssetForChr001()
        {
            const string folder = "Assets/Animations/chr001";
            const string path = folder + "/chr001_TransitionRules.asset";

            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
                AssetDatabase.Refresh();
            }

            var existing = AssetDatabase.LoadAssetAtPath<TransitionRuleData>(path);
            if (existing != null)
            {
                if (!EditorUtility.DisplayDialog(
                    "Already Exists",
                    $"{path} は既に存在します。デフォルトエントリで上書きしますか？（手動で追加したエントリは失われます）",
                    "Overwrite", "Cancel"))
                {
                    Selection.activeObject = existing;
                    EditorGUIUtility.PingObject(existing);
                    return;
                }
            }

            var data = existing != null ? existing : ScriptableObject.CreateInstance<TransitionRuleData>();
            data.description = "chr001 のアニメーション遷移ルール一覧。" +
                               "Editorウィンドウ（CyanNook > Animation > Transition Viewer）で閲覧用。" +
                               "ランタイム挙動には影響しない（閲覧専用）。";
            data.rules = BuildDefaultRules();

            if (existing == null)
            {
                AssetDatabase.CreateAsset(data, path);
            }
            else
            {
                EditorUtility.SetDirty(data);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = data;
            EditorGUIUtility.PingObject(data);

            EditorUtility.DisplayDialog("Created",
                $"TransitionRuleData を作成しました:\n{path}\n\nエントリ数: {data.rules.Count}",
                "OK");
        }

        private static List<TransitionRuleEntry> BuildDefaultRules()
        {
            // Phase 1 調査で判明した主要遷移。デザイナーが後から追加可能。
            var list = new List<TransitionRuleEntry>();

            // ─── Common ──────────────────────────────
            list.Add(New("Common", AnimationStateType.Idle, "", AnimationStateType.Walk, "",
                "TalkController / ChatManager", "LLM action=move / ユーザー移動要求",
                "CharacterController が action=move を受け、CharacterNavigationController 経由で Walk 開始"));
            list.Add(New("Common", AnimationStateType.Walk, "", AnimationStateType.Idle, "",
                "CharacterNavigationController", "目的地到達 / 移動キャンセル",
                "NavMeshAgent の停止を検知して ReturnToIdle"));
            list.Add(New("Common", AnimationStateType.Idle, "", AnimationStateType.Talk, "talk_idle01",
                "TalkController.EnterTalk", "会話開始（移動→対面後）",
                "EnterTalk 内で PlayState(Talk, \"talk_idle01\")"));

            // ─── Talk ────────────────────────────────
            list.Add(New("Talk", AnimationStateType.Talk, "talk_idle01", AnimationStateType.Emote, "emote_happy01",
                "ChatManager", "LLMレスポンスで emote フィールド受信",
                "CharacterController.ProcessEmoteFromField → PlayEmoteWithReturn"));
            list.Add(New("Talk", AnimationStateType.Emote, "", AnimationStateType.Talk, "talk_idle01",
                "PlayEmoteWithReturn", "emote_ed 完了 or emoteHoldDuration 経過",
                "CharacterAnimationController 内で _emoteReturnState にTalkが入っている場合"));
            list.Add(New("Talk", AnimationStateType.Talk, "talk_idle01", AnimationStateType.Thinking, "talk_thinking01",
                "ChatManager / CharacterController", "LLMストリーム遅延で think 再生",
                "加算(Additive)で再生される場合もある（sit中など）"));
            list.Add(New("Talk", AnimationStateType.Thinking, "talk_thinking01", AnimationStateType.Talk, "talk_idle01",
                "StopThinkingAndReturn", "LLMレスポンス到達",
                "think_ed 完了後に元の状態へ復帰"));
            list.Add(New("Talk", AnimationStateType.Talk, "talk_idle01", AnimationStateType.Idle, "",
                "TalkController.ExitTalk", "会話終了 / タイムアウト",
                "talk_idle_ed → Idle"));

            // ─── Interact (sit) ──────────────────────
            list.Add(New("Interact", AnimationStateType.Idle, "", AnimationStateType.Interact, "interact_entry01",
                "InteractionController.StartInteraction", "LLM action=interact_sit/sleep 等",
                "まず NavMesh で家具位置まで移動、その後 entry フェーズ"));
            list.Add(New("Interact", AnimationStateType.Interact, "interact_entry01", AnimationStateType.Interact, "interact_sit01",
                "Timeline signal / LoopRegion", "entry モーション完了",
                "LoopRegionTrack によりループ区間に遷移"));
            list.Add(New("Interact", AnimationStateType.Interact, "interact_sit01", AnimationStateType.Emote, "emote_happy01",
                "ChatManager (additive)", "sit ループ中に emote 受信",
                "加算で上半身のみ再生。EmotePlayableTrack と AdditiveCancelTrack で制御"));
            list.Add(New("Interact", AnimationStateType.Interact, "interact_sit01", AnimationStateType.Interact, "interact_exit01",
                "InteractionController.ExitLoop", "退出要求（次アクションが来た等）",
                "LoopRegion を抜けて ed フェーズへ"));
            list.Add(New("Interact", AnimationStateType.Interact, "interact_exit01", AnimationStateType.Idle, "",
                "OnInteractionComplete", "exit モーション完了",
                "CharacterAnimationController からイベント発火 → Idle 復帰"));

            // ─── Sleep ───────────────────────────────
            list.Add(New("Sleep", AnimationStateType.Idle, "", AnimationStateType.Interact, "interact_sleep01",
                "SleepController.StartSleep", "LLM action=interact_sleep",
                "entry → sleep_loop。Boredom / 夢タイマーが動き始める"));
            list.Add(New("Sleep", AnimationStateType.Interact, "interact_sleep01", AnimationStateType.Emote, "emote_relaxed01",
                "SleepController dream", "夢タイマー発火",
                "加算で表情・身じろぎを再生"));
            list.Add(New("Sleep", AnimationStateType.Interact, "interact_sleep01", AnimationStateType.Idle, "",
                "SleepController.ExitSleep", "起床（タイマー満了 / ユーザー操作）",
                "sleep_exit → Idle"));

            // ─── Boredom ─────────────────────────────
            list.Add(New("Boredom", AnimationStateType.Idle, "common_idle01_lp", AnimationStateType.Idle, "common_idle02_lp",
                "BoredomController", "退屈タイマー満了（idle 巡回）",
                "idle01/02/03 をランダム or 順次で切り替える"));

            return list;
        }

        private static TransitionRuleEntry New(string category,
            AnimationStateType from, string fromId,
            AnimationStateType to, string toId,
            string orchestrator, string trigger, string notes)
        {
            return new TransitionRuleEntry
            {
                category = category,
                fromState = from,
                fromAnimationId = fromId,
                toState = to,
                toAnimationId = toId,
                orchestrator = orchestrator,
                trigger = trigger,
                notes = notes
            };
        }
    }
}
