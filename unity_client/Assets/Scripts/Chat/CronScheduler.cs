using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using CyanNook.Core;
using CyanNook.Character;

namespace CyanNook.Chat
{
    /// <summary>
    /// StreamingAssets/cron/ 内のJSONファイルを読み込み、
    /// cron式に基づいて定期的にLLMへ自動リクエストを送信するスケジューラー
    /// 上級者向け機能（設定UIではON/OFFトグルのみ）
    /// </summary>
    public class CronScheduler : MonoBehaviour
    {
        private const string PrefKey_Enabled = "cronSchedulerEnabled";
        private const string PrefKey_AutoReload = "cronAutoReloadInterval";
        private const string CronFolderName = "cron";

        [Header("References")]
        public ChatManager chatManager;
        public SleepController sleepController;
        public OutingController outingController;

        [Header("Settings")]
        [Tooltip("Cronスケジューラーを有効にする")]
        public bool schedulerEnabled = false;

        [Tooltip("自動リロード間隔（分）。0=無効")]
        public float autoReloadInterval = 0f;

        private List<CronJobData> _jobs = new List<CronJobData>();
        private Queue<string> _pendingPrompts = new Queue<string>();
        private int _lastEvaluatedMinute = -1;
        private bool _isLoaded;
        private int _autoReloadCountdown;

        /// <summary>現在読み込まれているジョブ数</summary>
        public int LoadedJobCount => _jobs.Count;

        /// <summary>キューに溜まっているプロンプト数</summary>
        public int PendingCount => _pendingPrompts.Count;

        private void Start()
        {
            LoadSettings();

            if (chatManager != null)
            {
                chatManager.OnChatResponseReceived += OnChatResponseReceived;
                chatManager.OnError += OnChatError;
            }

            if (schedulerEnabled)
            {
                StartCoroutine(LoadCronJobsAsync());
            }
        }

        private void OnDestroy()
        {
            if (chatManager != null)
            {
                chatManager.OnChatResponseReceived -= OnChatResponseReceived;
                chatManager.OnError -= OnChatError;
            }
        }

        private void Update()
        {
            if (!schedulerEnabled || !_isLoaded || chatManager == null) return;

            // 分が変わったタイミングでのみ評価
            int currentMinute = DateTime.Now.Minute + DateTime.Now.Hour * 60
                + DateTime.Now.Day * 1440 + DateTime.Now.Month * 44640;
            if (currentMinute == _lastEvaluatedMinute) return;
            _lastEvaluatedMinute = currentMinute;

            EvaluateJobs();
            TrySendFromQueue();

            // 自動リロード（分単位カウントダウン）
            if (autoReloadInterval > 0f)
            {
                _autoReloadCountdown--;
                if (_autoReloadCountdown <= 0)
                {
                    _autoReloadCountdown = Mathf.Max(1, Mathf.RoundToInt(autoReloadInterval));
                    Debug.Log("[CronScheduler] Auto-reloading cron jobs...");
                    Reload();
                }
            }
        }

        // ─────────────────────────────────────
        // 公開メソッド
        // ─────────────────────────────────────

        /// <summary>
        /// スケジューラーのON/OFF切替
        /// </summary>
        public void SetEnabled(bool enabled)
        {
            schedulerEnabled = enabled;
            SaveSettings();

            if (enabled && !_isLoaded)
            {
                StartCoroutine(LoadCronJobsAsync());
            }

            Debug.Log($"[CronScheduler] Scheduler: {(enabled ? "ON" : "OFF")}");
        }

        /// <summary>
        /// 自動リロード間隔を設定（分）。0で無効
        /// </summary>
        public void SetAutoReloadInterval(float minutes)
        {
            autoReloadInterval = Mathf.Max(0f, minutes);
            _autoReloadCountdown = Mathf.Max(1, Mathf.RoundToInt(autoReloadInterval));
            SaveSettings();
            Debug.Log($"[CronScheduler] Auto-reload interval: {(autoReloadInterval > 0f ? $"{autoReloadInterval}min" : "OFF")}");
        }

        /// <summary>
        /// ジョブファイルを再読み込み
        /// </summary>
        public void Reload()
        {
            _jobs.Clear();
            _isLoaded = false;
            StartCoroutine(LoadCronJobsAsync());
        }

        // ─────────────────────────────────────
        // ジョブ読み込み
        // ─────────────────────────────────────

        private IEnumerator LoadCronJobsAsync()
        {
            string cronPath = Path.Combine(Application.streamingAssetsPath, CronFolderName);
            _jobs.Clear();

            // WebGLではfile://が使えないためUnityWebRequestを使用
            // まずディレクトリ内のファイル一覧を取得する方法が必要
            // StreamingAssetsではディレクトリ列挙ができないため、
            // file_manifest.jsonから cron/ 以下のファイルを取得する
            string manifestPath = Path.Combine(Application.streamingAssetsPath, "file_manifest.json");
            string manifestJson = null;

            using (var request = UnityWebRequest.Get(manifestPath))
            {
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    manifestJson = request.downloadHandler.text;
                }
                else
                {
                    Debug.LogWarning($"[CronScheduler] file_manifest.json not found: {request.error}");
                    _isLoaded = true;
                    yield break;
                }
            }

            // マニフェストからcron/以下の.jsonファイルを抽出
            var cronFiles = ExtractCronFilesFromManifest(manifestJson);

            if (cronFiles.Count == 0)
            {
                Debug.Log("[CronScheduler] No cron job files found in manifest");
                _isLoaded = true;
                yield break;
            }

            // 各ファイルを読み込み
            foreach (string fileName in cronFiles)
            {
                string filePath = Path.Combine(Application.streamingAssetsPath, CronFolderName, fileName);

                using (var request = UnityWebRequest.Get(filePath))
                {
                    yield return request.SendWebRequest();

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        try
                        {
                            var job = JsonUtility.FromJson<CronJobData>(request.downloadHandler.text);
                            if (job != null && !string.IsNullOrEmpty(job.id) && !string.IsNullOrEmpty(job.schedule))
                            {
                                _jobs.Add(job);
                                Debug.Log($"[CronScheduler] Loaded job: {job.id} ({job.name}) schedule={job.schedule}");
                            }
                            else
                            {
                                Debug.LogWarning($"[CronScheduler] Invalid job file (missing id or schedule): {fileName}");
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"[CronScheduler] Failed to parse {fileName}: {e.Message}");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[CronScheduler] Failed to load {fileName}: {request.error}");
                    }
                }
            }

            _isLoaded = true;
            Debug.Log($"[CronScheduler] Loaded {_jobs.Count} cron jobs");
        }

        /// <summary>
        /// file_manifest.json からcron/以下の.jsonファイル名を抽出
        /// マニフェスト形式: {"files":["cron/xxx.json","VRM/yyy.vrm",...]}
        /// </summary>
        private List<string> ExtractCronFilesFromManifest(string manifestJson)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(manifestJson)) return result;

            try
            {
                var manifest = JsonUtility.FromJson<FileManifest>(manifestJson);
                if (manifest?.files == null) return result;

                string prefix = CronFolderName + "/";
                foreach (string file in manifest.files)
                {
                    if (file.StartsWith(prefix) && file.EndsWith(".json"))
                    {
                        // "cron/xxx.json" → "xxx.json"
                        result.Add(file.Substring(prefix.Length));
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CronScheduler] Failed to parse manifest: {e.Message}");
            }

            return result;
        }

        // ─────────────────────────────────────
        // ジョブ評価
        // ─────────────────────────────────────

        private void EvaluateJobs()
        {
            var now = DateTime.Now;
            bool isSleeping = sleepController != null && sleepController.IsSleeping;
            bool isOutside = outingController != null && outingController.IsOutside;

            foreach (var job in _jobs)
            {
                if (!job.enabled) continue;
                if (!MatchesCron(job.schedule, now)) continue;

                Debug.Log($"[CronScheduler] Job triggered: {job.id} ({job.name})");

                // Sleep/Outing中の処理
                if (isSleeping || isOutside)
                {
                    if (job.cancelSleepOrOuting && chatManager != null)
                    {
                        // Sleep/Outingをキャンセルして実行
                        if (isSleeping)
                        {
                            Debug.Log($"[CronScheduler] Cancelling sleep for job: {job.id}");
                            chatManager.SendCronWakeUpRequest(job.prompt);
                            isSleeping = false; // 後続ジョブは通常キューへ
                        }
                        else
                        {
                            Debug.Log($"[CronScheduler] Cancelling outing for job: {job.id}");
                            chatManager.SendCronEntryRequest(job.prompt);
                            isOutside = false; // 後続ジョブは通常キューへ
                        }
                    }
                    else
                    {
                        // デフォルト: Sleep/Outing中はスキップ
                        Debug.Log($"[CronScheduler] Job skipped (sleep/outing): {job.id}");
                    }
                    continue;
                }

                // 通常: キューに追加
                _pendingPrompts.Enqueue(job.prompt);
            }
        }

        // ─────────────────────────────────────
        // キュー送信
        // ─────────────────────────────────────

        private void TrySendFromQueue()
        {
            if (_pendingPrompts.Count == 0) return;
            if (chatManager == null || chatManager.CurrentState != ChatState.Idle) return;

            string prompt = _pendingPrompts.Dequeue();
            Debug.Log($"[CronScheduler] Sending queued prompt ({_pendingPrompts.Count} remaining)");
            chatManager.SendAutoRequest(prompt);
        }

        // ─────────────────────────────────────
        // イベントハンドラ
        // ─────────────────────────────────────

        private void OnChatResponseReceived(LLMResponseData response)
        {
            // レスポンス完了後、キューに溜まっているものがあれば送信
            TrySendFromQueue();
        }

        private void OnChatError(string error)
        {
            TrySendFromQueue();
        }

        // ─────────────────────────────────────
        // Cron式パーサー
        // ─────────────────────────────────────

        /// <summary>
        /// cron式が現在時刻にマッチするか判定
        /// 形式: "分 時 日 月 曜日"
        /// 対応: * 固定値 カンマ(1,15) 範囲(9-17) ステップ(*/5)
        /// </summary>
        private static bool MatchesCron(string cronExpression, DateTime now)
        {
            if (string.IsNullOrEmpty(cronExpression)) return false;

            string[] parts = cronExpression.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 5) return false;

            // 曜日: 0=日曜, 1=月曜, ..., 6=土曜
            int dayOfWeek = (int)now.DayOfWeek;

            return MatchField(parts[0], now.Minute, 0, 59)
                && MatchField(parts[1], now.Hour, 0, 23)
                && MatchField(parts[2], now.Day, 1, 31)
                && MatchField(parts[3], now.Month, 1, 12)
                && MatchField(parts[4], dayOfWeek, 0, 6);
        }

        /// <summary>
        /// cron式の1フィールドが値にマッチするか判定
        /// </summary>
        private static bool MatchField(string field, int value, int min, int max)
        {
            // カンマ区切り: "1,15,30"
            string[] segments = field.Split(',');
            foreach (string segment in segments)
            {
                if (MatchSegment(segment.Trim(), value, min, max))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// カンマ分割後の1セグメントを評価
        /// </summary>
        private static bool MatchSegment(string segment, int value, int min, int max)
        {
            // ステップ付き: "*/5" or "1-10/2"
            int step = 1;
            int slashIndex = segment.IndexOf('/');
            if (slashIndex >= 0)
            {
                if (!int.TryParse(segment.Substring(slashIndex + 1), out step) || step <= 0)
                    return false;
                segment = segment.Substring(0, slashIndex);
            }

            // ワイルドカード: "*"
            if (segment == "*")
            {
                return (value - min) % step == 0;
            }

            // 範囲: "9-17"
            int dashIndex = segment.IndexOf('-');
            if (dashIndex >= 0)
            {
                if (!int.TryParse(segment.Substring(0, dashIndex), out int rangeStart)) return false;
                if (!int.TryParse(segment.Substring(dashIndex + 1), out int rangeEnd)) return false;

                if (value < rangeStart || value > rangeEnd) return false;
                return (value - rangeStart) % step == 0;
            }

            // 固定値: "30"
            if (int.TryParse(segment, out int exact))
            {
                return value == exact;
            }

            return false;
        }

        // ─────────────────────────────────────
        // 設定の保存/読み込み
        // ─────────────────────────────────────

        private void SaveSettings()
        {
            PlayerPrefs.SetInt(PrefKey_Enabled, schedulerEnabled ? 1 : 0);
            PlayerPrefs.SetFloat(PrefKey_AutoReload, autoReloadInterval);
            PlayerPrefs.Save();
        }

        private void LoadSettings()
        {
            if (PlayerPrefs.HasKey(PrefKey_Enabled))
            {
                schedulerEnabled = PlayerPrefs.GetInt(PrefKey_Enabled) == 1;
            }
            if (PlayerPrefs.HasKey(PrefKey_AutoReload))
            {
                autoReloadInterval = PlayerPrefs.GetFloat(PrefKey_AutoReload);
            }
            _autoReloadCountdown = Mathf.Max(1, Mathf.RoundToInt(autoReloadInterval));
        }

        // ─────────────────────────────────────
        // マニフェスト用データクラス
        // ─────────────────────────────────────

        [Serializable]
        private class FileManifest
        {
            public string[] files;
        }
    }
}
