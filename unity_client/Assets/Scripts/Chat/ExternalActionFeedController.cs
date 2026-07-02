using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using CyanNook.Core;

namespace CyanNook.Chat
{
    /// <summary>
    /// 外部アクションフィード（受動制御）コントローラー。
    /// 外部のLLM系統（Discord連携 / openclaw 等）が公開するエンドポイントを定期的にHTTP GETで購読し、
    /// 受信した「完成済みの応答JSON」（既存LLM応答と同一スキーマ）をキャラクターへ適用する。
    ///
    /// 既存の能動的API機能（チャット入力 / Cron / IdleChat / SleepChat）とは排他ではなく並列の追加機能。
    /// デフォルトOFF・上級者向け。LLMへリクエストは送らず、受信済み応答を ChatManager.ApplyExternalResponse で反映する。
    ///
    /// Phase 1（本実装）: 受信→適用のみ。
    /// Phase 2（未実装）: context/camera の公開（publish）。
    /// </summary>
    public class ExternalActionFeedController : MonoBehaviour
    {
        // PlayerPrefsキー
        private const string PrefKey_Enabled = "feed_enabled";
        private const string PrefKey_ActionUrl = "feed_actionUrl";
        private const string PrefKey_SubscribeInterval = "feed_subscribeInterval";

        [Header("References")]
        public ChatManager chatManager;

        [Header("Settings")]
        [Tooltip("外部アクションフィードを有効にする")]
        public bool feedEnabled = false;

        [Tooltip("応答JSONを購読するエンドポイントURL（HTTP GET）")]
        public string actionSubscribeUrl = "";

        [Tooltip("購読間隔（秒）。0以下で無効")]
        public float subscribeInterval = 5f;

        // 購読タイマー
        private float _timer;

        // GET実行中フラグ（多重リクエスト防止）
        private bool _isFetching;

        // 前回適用した生JSON（変更検知用）。同一内容の連続適用を避ける。
        private string _lastAppliedRawJson;

        // キャッシュバスティング用カウンタ（ブラウザキャッシュ回避）
        private int _cacheBustCounter;

        /// <summary>
        /// フィードが実際に稼働可能か（有効かつURL・間隔が妥当）
        /// </summary>
        public bool IsActive =>
            feedEnabled && subscribeInterval > 0f && !string.IsNullOrEmpty(actionSubscribeUrl);

        private void Start()
        {
            LoadSettings();
        }

        private void Update()
        {
            if (!IsActive || _isFetching || chatManager == null) return;

            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                _timer = subscribeInterval;
                StartCoroutine(FetchAndApply());
            }
        }

        // ─────────────────────────────────────
        // 公開メソッド
        // ─────────────────────────────────────

        /// <summary>
        /// フィードのON/OFF切替
        /// </summary>
        public void SetEnabled(bool enabled)
        {
            feedEnabled = enabled;
            _timer = 0f; // ON直後は即座に初回GET
            SaveSettings();
            Debug.Log($"[ExternalActionFeed] Feed: {(enabled ? "ON" : "OFF")}");
        }

        /// <summary>
        /// 購読URLを設定
        /// </summary>
        public void SetActionSubscribeUrl(string url)
        {
            actionSubscribeUrl = url != null ? url.Trim() : "";
            _lastAppliedRawJson = null; // URL変更で変更検知をリセット
            SaveSettings();
        }

        /// <summary>
        /// 購読間隔を設定（秒）。0以下で無効
        /// </summary>
        public void SetSubscribeInterval(float seconds)
        {
            subscribeInterval = Mathf.Max(0f, seconds);
            SaveSettings();
            Debug.Log($"[ExternalActionFeed] Subscribe interval: {(subscribeInterval > 0f ? $"{subscribeInterval}s" : "OFF")}");
        }

        // ─────────────────────────────────────
        // 受信→適用
        // ─────────────────────────────────────

        private IEnumerator FetchAndApply()
        {
            _isFetching = true;
            string rawJson = null;

            // キャッシュバスティング（ブラウザキャッシュ回避）
            _cacheBustCounter++;
            string url = AppendCacheBust(actionSubscribeUrl, _cacheBustCounter);

            using (var request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("Cache-Control", "no-cache");
                request.SetRequestHeader("Pragma", "no-cache");

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    rawJson = request.downloadHandler.text;
                }
                else
                {
                    Debug.LogWarning($"[ExternalActionFeed] Fetch failed: {request.error}");
                }
            }

            // フェッチ完了。次回まで間隔を空け、多重起動フラグを解除（単一箇所でリセット）。
            _timer = subscribeInterval;
            _isFetching = false;

            // フェッチ中にOFF/URL変更/間隔0化された場合は適用しない
            if (!IsActive) yield break;
            if (string.IsNullOrWhiteSpace(rawJson)) yield break;

            // 変更検知: 前回適用分と同一なら何もしない
            // （外部側がtimestamp/uuidを含めればrawが変わり再適用される。
            //   含めない場合は同一内容の連投を自然に抑制する）
            if (rawJson == _lastAppliedRawJson) yield break;

            // JSONらしさの軽いバリデーション（HTMLエラーページ・プロキシエラー等を弾く）。
            // JSONオブジェクトは '{' で始まる。
            string trimmed = rawJson.TrimStart();
            if (trimmed.Length == 0 || trimmed[0] != '{')
            {
                Debug.LogWarning("[ExternalActionFeed] Response is not a JSON object, skipping");
                yield break;
            }

            // パース。LLMResponseData.FromJsonは失敗時に例外を投げずGetFallback（無難な応答）を
            // 返してしまうため、ここではJsonUtilityを直接呼んで不正JSONの失敗を確実に検出する。
            // catch節内ではyieldできないため、成否をフラグで受けてからyield breakする
            LLMResponseData response = null;
            bool parseFailed = false;
            try
            {
                response = JsonUtility.FromJson<LLMResponseData>(rawJson);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[ExternalActionFeed] Failed to parse response JSON: {e.Message}");
                parseFailed = true;
            }

            if (parseFailed || response == null) yield break;

            // 適用（ApplyExternalResponse内でFillDefaults補填）。
            // ビジー（応答待ち/Thinking/睡眠中/外出中）ならfalseが返るので
            // _lastAppliedRawJsonを更新せず、次ポーリングで再試行する。
            // その間に外部側がより新しい応答を出せばrawが変わり最新を適用する（最新へ収束）。
            bool applied = chatManager.ApplyExternalResponse(response);
            if (applied)
            {
                _lastAppliedRawJson = rawJson;
                Debug.Log("[ExternalActionFeed] Applied external response");
            }
            else
            {
                Debug.Log("[ExternalActionFeed] Apply skipped (busy/sleep/outing), will retry next poll");
            }
        }

        /// <summary>
        /// URLにキャッシュバスティング用クエリを付与
        /// </summary>
        private static string AppendCacheBust(string url, int counter)
        {
            if (string.IsNullOrEmpty(url)) return url;
            char separator = url.Contains("?") ? '&' : '?';
            return $"{url}{separator}_cb={counter}";
        }

        // ─────────────────────────────────────
        // 設定の保存/読み込み
        // ─────────────────────────────────────

        private void SaveSettings()
        {
            PlayerPrefs.SetInt(PrefKey_Enabled, feedEnabled ? 1 : 0);
            PlayerPrefs.SetString(PrefKey_ActionUrl, actionSubscribeUrl ?? "");
            PlayerPrefs.SetFloat(PrefKey_SubscribeInterval, subscribeInterval);
            PlayerPrefs.Save();
        }

        private void LoadSettings()
        {
            if (PlayerPrefs.HasKey(PrefKey_Enabled))
            {
                feedEnabled = PlayerPrefs.GetInt(PrefKey_Enabled) == 1;
            }
            if (PlayerPrefs.HasKey(PrefKey_ActionUrl))
            {
                actionSubscribeUrl = PlayerPrefs.GetString(PrefKey_ActionUrl);
            }
            if (PlayerPrefs.HasKey(PrefKey_SubscribeInterval))
            {
                subscribeInterval = PlayerPrefs.GetFloat(PrefKey_SubscribeInterval);
            }
        }
    }
}
