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
    /// Phase 1: 受信→適用（subscribe）。
    /// Phase 2: context/camera の公開（publish）。現在の空間情報・状態・カメラ画像を
    ///          外部URLへ定期PUTし、外部LLM側が「Cyanの今」を読める状態にする。
    /// </summary>
    public class ExternalActionFeedController : MonoBehaviour
    {
        // PlayerPrefsキー
        private const string PrefKey_Enabled = "feed_enabled";
        private const string PrefKey_ActionUrl = "feed_actionUrl";
        private const string PrefKey_SubscribeInterval = "feed_subscribeInterval";
        private const string PrefKey_ContextUrl = "feed_contextUrl";
        private const string PrefKey_CameraUrl = "feed_cameraUrl";
        private const string PrefKey_PublishInterval = "feed_publishInterval";

        [Header("References")]
        public ChatManager chatManager;

        [Header("Settings")]
        [Tooltip("外部アクションフィードを有効にする")]
        public bool feedEnabled = false;

        [Tooltip("応答JSONを購読するエンドポイントURL（HTTP GET）")]
        public string actionSubscribeUrl = "";

        [Tooltip("購読間隔（秒）。0以下で無効")]
        public float subscribeInterval = 5f;

        [Header("Publish Settings")]
        [Tooltip("context JSONを公開するエンドポイントURL（HTTP PUT）。空で無効")]
        public string contextPublishUrl = "";

        [Tooltip("カメラ画像を公開するエンドポイントURL（HTTP PUT, image/jpeg）。空で無効")]
        public string cameraPublishUrl = "";

        [Tooltip("公開間隔（秒）。0以下で無効")]
        public float publishInterval = 30f;

        // 購読タイマー
        private float _timer;

        // GET実行中フラグ（多重リクエスト防止）
        private bool _isFetching;

        // 前回適用した生JSON（変更検知用）。同一内容の連続適用を避ける。
        private string _lastAppliedRawJson;

        // キャッシュバスティング用カウンタ（ブラウザキャッシュ回避）
        private int _cacheBustCounter;

        // 公開タイマー
        private float _publishTimer;

        // PUT実行中フラグ（多重リクエスト防止）
        private bool _isPublishing;

        /// <summary>
        /// 購読（受信→適用）が稼働可能か（有効かつURL・間隔が妥当）
        /// </summary>
        public bool IsActive =>
            feedEnabled && subscribeInterval > 0f && !string.IsNullOrEmpty(actionSubscribeUrl);

        /// <summary>
        /// 公開（publish）が稼働可能か（有効かついずれかのURL・間隔が妥当）
        /// context/cameraのどちらか一方だけの運用も許容する
        /// </summary>
        public bool IsPublishActive =>
            feedEnabled && publishInterval > 0f &&
            (!string.IsNullOrEmpty(contextPublishUrl) || !string.IsNullOrEmpty(cameraPublishUrl));

        private void Start()
        {
            LoadSettings();

            // LLM応答適用直後（キャラの状態が変わった直後）に即時publishする
            if (chatManager != null)
            {
                chatManager.OnChatResponseReceived += OnChatResponseReceivedForPublish;
            }
        }

        private void OnDestroy()
        {
            if (chatManager != null)
            {
                chatManager.OnChatResponseReceived -= OnChatResponseReceivedForPublish;
            }
        }

        private void OnDisable()
        {
            // GameObject非アクティブ化でコルーチンが途中停止した場合に
            // フラグが立ったまま残ると購読/公開が永久停止するためリセットする
            _isFetching = false;
            _isPublishing = false;
        }

        private void Update()
        {
            if (chatManager == null) return;

            // 購読（受信→適用）
            if (IsActive && !_isFetching)
            {
                _timer -= Time.deltaTime;
                if (_timer <= 0f)
                {
                    _timer = subscribeInterval;
                    StartCoroutine(FetchAndApply());
                }
            }

            // 公開（heartbeat）
            if (IsPublishActive && !_isPublishing)
            {
                _publishTimer -= Time.deltaTime;
                if (_publishTimer <= 0f)
                {
                    _publishTimer = publishInterval;
                    StartCoroutine(PublishContext());
                }
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
            _timer = 0f;        // ON直後は即座に初回GET
            _publishTimer = 0f; // ON直後は即座に初回publish
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

        /// <summary>
        /// context公開URLを設定
        /// </summary>
        public void SetContextPublishUrl(string url)
        {
            contextPublishUrl = url != null ? url.Trim() : "";
            SaveSettings();
        }

        /// <summary>
        /// カメラ画像公開URLを設定
        /// </summary>
        public void SetCameraPublishUrl(string url)
        {
            cameraPublishUrl = url != null ? url.Trim() : "";
            SaveSettings();
        }

        /// <summary>
        /// 公開間隔を設定（秒）。0以下で無効
        /// </summary>
        public void SetPublishInterval(float seconds)
        {
            publishInterval = Mathf.Max(0f, seconds);
            // タイマーを新しい値で再セット（0→正値に戻した時の即時発火を防ぐ）
            _publishTimer = publishInterval;
            SaveSettings();
            Debug.Log($"[ExternalActionFeed] Publish interval: {(publishInterval > 0f ? $"{publishInterval}s" : "OFF")}");
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
        // 公開（publish）
        // ─────────────────────────────────────

        /// <summary>
        /// LLM応答適用直後の即時publish（heartbeatを待たずに最新状態を外部へ伝える）
        /// </summary>
        private void OnChatResponseReceivedForPublish(LLMResponseData response)
        {
            if (!IsPublishActive) return;

            // 応答直後はアクション（移動・インタラクト等）の実行開始タイミングなので、
            // 数フレーム待ってからキャプチャすると変化後の状態が乗りやすい。
            // 厳密さより簡潔さを優先し、次のheartbeatを前倒しする形で実現する。
            // 実行中の多重起動はUpdate側の!_isPublishingガードが防ぐため、ここではタイマー操作のみ。
            // Minにすることで既に1秒未満の場合に逆に遅延させない
            _publishTimer = Mathf.Min(_publishTimer, 1f);
        }

        /// <summary>
        /// context JSONとカメラ画像を外部URLへPUTする
        /// </summary>
        private IEnumerator PublishContext()
        {
            _isPublishing = true;

            // カメラ画像を今回実際に公開するか（context JSONのcamera_image_fresh判定にも使う）。
            // 睡眠中・外出中は既存Visionと同様に抑制
            bool cameraSuppressed =
                (chatManager.sleepController != null && chatManager.sleepController.IsSleeping) ||
                (chatManager.outingController != null && chatManager.outingController.IsOutside);
            bool willPublishCamera = !string.IsNullOrEmpty(cameraPublishUrl) && !cameraSuppressed &&
                chatManager.cameraController != null;

            // カメラキャプチャはyield前（chatManager破棄リスクの無い区間）に済ませる
            byte[] jpegBytes = null;
            if (willPublishCamera)
            {
                string base64 = chatManager.cameraController.CaptureImageAsBase64();
                if (!string.IsNullOrEmpty(base64))
                {
                    try
                    {
                        jpegBytes = System.Convert.FromBase64String(base64);
                    }
                    catch (System.FormatException e)
                    {
                        Debug.LogWarning($"[ExternalActionFeed] Camera base64 decode failed: {e.Message}");
                    }
                }
            }
            bool cameraFresh = jpegBytes != null;

            // context JSON公開（URL設定時のみ）
            if (!string.IsNullOrEmpty(contextPublishUrl))
            {
                string contextJson = BuildContextJson(cameraFresh);
                byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes(contextJson);

                using (var request = UnityWebRequest.Put(contextPublishUrl, jsonBytes))
                {
                    request.SetRequestHeader("Content-Type", "application/json");
                    yield return request.SendWebRequest();

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        Debug.LogWarning($"[ExternalActionFeed] Context publish failed: {request.error}");
                    }
                }
            }

            // カメラ画像公開
            if (cameraFresh)
            {
                using (var request = UnityWebRequest.Put(cameraPublishUrl, jpegBytes))
                {
                    request.SetRequestHeader("Content-Type", "image/jpeg");
                    yield return request.SendWebRequest();

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        Debug.LogWarning($"[ExternalActionFeed] Camera publish failed: {request.error}");
                    }
                }
            }

            _isPublishing = false;
        }

        /// <summary>
        /// 現在のキャラクター状態・空間情報をcontext JSONとして組み立てる。
        /// spatial_contextはSpatialContextProviderの生JSONをそのまま埋め込む
        /// </summary>
        /// <param name="cameraFresh">今回のpublishでカメラ画像も更新されるか（外部側の鮮度判定用）</param>
        private string BuildContextJson(bool cameraFresh)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("{\n");

            // タイムスタンプ（外部側の鮮度判定用）
            sb.Append($"  \"timestamp\": \"{System.DateTime.Now:yyyy-MM-ddTHH:mm:sszzz}\",\n");

            // 状態系
            sb.Append($"  \"chat_state\": \"{chatManager.CurrentState}\",\n");
            bool isSleeping = chatManager.sleepController != null && chatManager.sleepController.IsSleeping;
            bool isOutside = chatManager.outingController != null && chatManager.outingController.IsOutside;
            sb.Append($"  \"is_sleeping\": {(isSleeping ? "true" : "false")},\n");
            sb.Append($"  \"is_outside\": {(isOutside ? "true" : "false")},\n");

            // 空間認識（SpatialContextProviderの生JSONを埋め込み）
            string spatialJson = chatManager.spatialContextProvider != null
                ? chatManager.spatialContextProvider.GenerateSpatialContextJson()
                : "{}";
            sb.Append("  \"spatial_context\": ");
            sb.Append(spatialJson);
            sb.Append(",\n");

            // 視界内オブジェクト（テキスト）
            string visibleText = chatManager.visibleObjectsProvider != null
                ? chatManager.visibleObjectsProvider.GenerateVisibleObjectsText()
                : "";
            sb.Append($"  \"visible_objects\": \"{EscapeJson(visibleText)}\",\n");

            // カメラ画像の参照先と鮮度（freshがfalseの時、URLの先の画像は古い可能性がある）
            sb.Append($"  \"camera_image_url\": \"{EscapeJson(cameraPublishUrl ?? "")}\",\n");
            sb.Append($"  \"camera_image_fresh\": {(cameraFresh ? "true" : "false")}\n");

            sb.Append("}");
            return sb.ToString();
        }

        /// <summary>
        /// JSON文字列エスケープ（最低限）
        /// </summary>
        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }

        // ─────────────────────────────────────
        // 設定の保存/読み込み
        // ─────────────────────────────────────

        private void SaveSettings()
        {
            PlayerPrefs.SetInt(PrefKey_Enabled, feedEnabled ? 1 : 0);
            PlayerPrefs.SetString(PrefKey_ActionUrl, actionSubscribeUrl ?? "");
            PlayerPrefs.SetFloat(PrefKey_SubscribeInterval, subscribeInterval);
            PlayerPrefs.SetString(PrefKey_ContextUrl, contextPublishUrl ?? "");
            PlayerPrefs.SetString(PrefKey_CameraUrl, cameraPublishUrl ?? "");
            PlayerPrefs.SetFloat(PrefKey_PublishInterval, publishInterval);
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
            if (PlayerPrefs.HasKey(PrefKey_ContextUrl))
            {
                contextPublishUrl = PlayerPrefs.GetString(PrefKey_ContextUrl);
            }
            if (PlayerPrefs.HasKey(PrefKey_CameraUrl))
            {
                cameraPublishUrl = PlayerPrefs.GetString(PrefKey_CameraUrl);
            }
            if (PlayerPrefs.HasKey(PrefKey_PublishInterval))
            {
                publishInterval = PlayerPrefs.GetFloat(PrefKey_PublishInterval);
            }
        }
    }
}
