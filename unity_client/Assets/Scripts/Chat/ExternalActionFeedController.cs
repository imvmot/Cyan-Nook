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
        private const string PrefKey_VoiceEnabled = "feed_voiceEnabled";
        private const string PrefKey_VoiceUrl = "feed_voiceUrl";
        private const string PrefKey_ThinkingVoiceEnabled = "feed_thinkingVoiceEnabled";
        private const string PrefKey_ThinkingVoicePitch = "feed_thinkingVoicePitch";

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

        [Header("Voice Settings")]
        [Tooltip("フィードの合成済み音声（voice.wav）を取得して応答と同時に再生する。" +
                 "有効時、音声付きの応答では自前TTS合成より wav を優先する")]
        public bool voiceEnabled = false;

        [Tooltip("voice.wav の購読URL（HTTP GET）。空の場合はアクション購読URLと同じ場所の voice.wav を使う")]
        public string voiceSubscribeUrl = "";

        [Tooltip("thinking の作業状況メッセージを読み上げる。音声ソースは応答と同じ優先順" +
                 "（voice.wav があればそれ、無ければ本体TTS（有効時））")]
        public bool thinkingVoiceEnabled = false;

        [Tooltip("状況読み上げの再生ピッチ倍率（1.0=通常、1.5〜2.0で早回しロボ声風）。速度と音程が一緒に変わる")]
        public float thinkingVoicePitch = 1.5f;

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

        // 直前に読み上げた thinking 状況文。同じ文の再受信（timestamp 違いの
        // タイムアウト延長 PUT 等）で毎回読み上げ直し・合成 API を叩き直すのを防ぐ。
        // thinking 終了（OnThinkingEnded）でクリア
        private string _lastSpokenThinkingStatus;

        // ── 分割音声（voice_parts / voice_1..N.wav）の受信状態 ──
        // 外部側（herald）は長い返答を区切りごとに合成し、区切りを置くたびに
        // 同じ timestamp の action.json を置き直す。同じ timestamp は「同じ返答の続き」なので
        // 適用し直さず、まだ取っていない区切りだけ取得して再生待ちの列に積む

        // 適用済みの分割配信の返答の timestamp（null = 分割配信の返答を適用していない）
        private string _partsTimestamp;

        // 取得を試みた区切りの最大番号（失敗した区切りは飛ばす）
        private int _partsFetched;

        // 再生側（VoiceSynthesisController）の分割再生の serial（0 = 音声の列なし）
        private int _partsSerial;

        // 区切りが揃うまでの間・外部Thinking中の見回り間隔（秒）。
        // 最初の一言の遅れと、区切りの間の途切れを縮めるため
        private const float FastPollInterval = 1f;

        /// <summary>
        /// 購読（受信→適用）が稼働可能か（有効かつURL・間隔が妥当）
        /// </summary>
        public bool IsActive =>
            feedEnabled && subscribeInterval > 0f && !string.IsNullOrEmpty(actionSubscribeUrl);

        /// <summary>
        /// 今の見回り間隔。区切りが揃うまでの分割再生中と外部Thinking中（本応答待ち）は
        /// FastPollInterval に縮める（設定値の方が短ければ設定値）
        /// </summary>
        private float CurrentSubscribeInterval
        {
            get
            {
                var synth = chatManager != null ? chatManager.voiceSynthesisController : null;
                bool waitingParts = synth != null && synth.IsExternalPartsActive(_partsSerial);
                bool waitingResponse = chatManager != null && chatManager.IsExternalThinkingActive;
                return (waitingParts || waitingResponse)
                    ? Mathf.Min(subscribeInterval, FastPollInterval)
                    : subscribeInterval;
            }
        }

        /// <summary>
        /// 公開（publish）が稼働可能か（有効かついずれかのURL・間隔が妥当）
        /// context/cameraのどちらか一方だけの運用も許容する
        /// </summary>
        public bool IsPublishActive =>
            feedEnabled && publishInterval > 0f &&
            (!string.IsNullOrEmpty(contextPublishUrl) || !string.IsNullOrEmpty(cameraPublishUrl));

        private void Start()
        {
#if UNITYROOM_BUILD
            // unityroom版（体験版）では外部アクションフィードを完全停止。
            // 誘導された悪意URLの登録でcontext/カメラ画像が外部送信されるのを防ぐ。
            // UIを隠すだけではPlayerPrefs復元やImportで有効化され得るため機能側で塞ぐ
            feedEnabled = false;
            enabled = false;
            return;
#else
            LoadSettings();

            // LLM応答適用直後（キャラの状態が変わった直後）に即時publishする
            if (chatManager != null)
            {
                chatManager.OnChatResponseReceived += OnChatResponseReceivedForPublish;
                chatManager.OnThinkingEnded += OnThinkingEndedForStatusReset;
            }
#endif
        }

        private void OnDestroy()
        {
            if (chatManager != null)
            {
                chatManager.OnChatResponseReceived -= OnChatResponseReceivedForPublish;
                chatManager.OnThinkingEnded -= OnThinkingEndedForStatusReset;
            }
        }

        /// <summary>
        /// thinking 終了で状況読み上げの重複抑止をリセット（次のターンで同じ文が来たら読み上げる）
        /// </summary>
        private void OnThinkingEndedForStatusReset()
        {
            _lastSpokenThinkingStatus = null;
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
                // 速い見回りに切り替わった時に、長い残り待ちを引きずらない
                float interval = CurrentSubscribeInterval;
                if (_timer > interval) _timer = interval;

                _timer -= Time.deltaTime;
                if (_timer <= 0f)
                {
                    _timer = interval;
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
            if (!enabled) CancelParts(); // 続きを取りに行かなくなるため、分割再生は打ち切る
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
            _partsTimestamp = null;
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
        /// フィード音声（voice.wav）再生のON/OFF切替
        /// </summary>
        public void SetVoiceEnabled(bool enable)
        {
            voiceEnabled = enable;
            if (!enable) CancelParts(); // 続きを取りに行かなくなるため、分割再生は打ち切る
            SaveSettings();
            Debug.Log($"[ExternalActionFeed] Voice playback: {(enable ? "ON" : "OFF")}");
        }

        /// <summary>
        /// 進行中の分割再生を打ち切る（OFF切替時。done が来ないまま喋り途中で待ち続けないように）
        /// </summary>
        private void CancelParts()
        {
            if (_partsSerial == 0) return;
            chatManager?.voiceSynthesisController?.CancelExternalParts(_partsSerial);
            _partsSerial = 0;
        }

        /// <summary>
        /// voice.wav の購読URLを設定（空でアクションURLから自動導出）
        /// </summary>
        public void SetVoiceSubscribeUrl(string url)
        {
            voiceSubscribeUrl = url != null ? url.Trim() : "";
            SaveSettings();
        }

        /// <summary>
        /// thinking状況読み上げのON/OFF切替
        /// </summary>
        public void SetThinkingVoiceEnabled(bool enable)
        {
            thinkingVoiceEnabled = enable;
            SaveSettings();
            Debug.Log($"[ExternalActionFeed] Thinking voice: {(enable ? "ON" : "OFF")}");
        }

        /// <summary>
        /// 状況読み上げのピッチ倍率を設定（0.5〜3.0にクランプ）
        /// </summary>
        public void SetThinkingVoicePitch(float pitch)
        {
            thinkingVoicePitch = Mathf.Clamp(pitch, 0.5f, 3f);
            SaveSettings();
            Debug.Log($"[ExternalActionFeed] Thinking voice pitch: {thinkingVoicePitch:F2}");
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
            // wav取得（yield）を挟んでも多重起動しないよう、フラグ解除はfinallyの単一箇所で行う
            // （途中停止＝イテレータDispose時もfinallyは実行される）
            try
            {
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

                // フェッチ完了。次回まで間隔を空ける
                _timer = CurrentSubscribeInterval;

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

                bool isThinking = ChatManager.IsThinkingAction(response);
                FeedMarker marker = ExtractMarker(rawJson);

                // 分割配信（voice_parts）の返答か。thinking は従来どおり voice.wav 1本の取り決め
                bool isParts = !isThinking && marker.voice_parts > 0 && !string.IsNullOrEmpty(marker.timestamp);

                // 分割配信の続き（適用済みの返答と同じ timestamp）: 適用し直さず、
                // まだ取っていない区切りだけ取得して再生待ちの列に積む。
                // ボイス再生OFFでも再適用はしない（置き直しのたびに動作・自前TTSが繰り返されるため）
                if (isParts && marker.timestamp == _partsTimestamp)
                {
                    _lastAppliedRawJson = rawJson;

                    // 続きの経路は ApplyExternalResponse のビジー判定を通らないため、
                    // 内部リクエストが始まっていたら残りの区切りは打ち切る（新しい内部応答に前の声が重ならないように）
                    if (chatManager.IsBusy)
                    {
                        if (_partsSerial != 0)
                        {
                            Debug.Log($"[ExternalActionFeed] Voice parts cancelled: internal request in progress (serial={_partsSerial})");
                        }
                        chatManager.voiceSynthesisController?.CancelExternalParts(_partsSerial);
                        yield break;
                    }

                    if (voiceEnabled)
                    {
                        yield return FetchRemainingParts(marker);
                    }
                    yield break;
                }

                // フィード音声の取得（適用前に済ませることで応答表示と音声再生を同時に始める）。
                // - 分割配信: 先頭の区切り voice_1.wav
                // - 従来形式: jsonのvoice_timestamp（外部側がwav PUT成功時のみ付ける印）がある場合のみ voice.wav
                // 取得失敗時はnullのまま渡し、従来の自前TTSにフォールバックする
                // （分割配信で先頭が取れなかった場合は返答全体を自前TTSで読み、後続の区切りは使わない）
                AudioClip voiceClip = null;
                if (voiceEnabled)
                {
                    if (isParts)
                    {
                        yield return FetchVoiceClip(ResolvePartUrl(1), marker.timestamp, 1, clip => voiceClip = clip);
                    }
                    else if (!string.IsNullOrEmpty(marker.voice_timestamp))
                    {
                        yield return FetchVoiceClip(ResolveVoiceUrl(), marker.voice_timestamp, 0, clip => voiceClip = clip);
                    }
                }

                // wav取得中にOFF/URL変更された場合は適用しない
                if (!IsActive)
                {
                    if (voiceClip != null) Destroy(voiceClip);
                    yield break;
                }

                // 適用（ApplyExternalResponse内でFillDefaults補填）。
                // ビジー（応答待ち/Thinking/外出中。睡眠中は起床して適用される）ならfalseが返るので
                // _lastAppliedRawJsonを更新せず、次ポーリングで再試行する（wavも次回取得し直す）。
                // その間に外部側がより新しい応答を出せばrawが変わり最新を適用する（最新へ収束）。
                // thinkingの音声（状況読み上げ）は本クラスが扱うため、クリップはChatManagerに渡さない
                var synth = chatManager.voiceSynthesisController;
                int serialBefore = synth != null ? synth.CurrentExternalClipSerial : 0;

                bool applied = chatManager.ApplyExternalResponse(response, isThinking ? null : voiceClip);
                if (applied)
                {
                    _lastAppliedRawJson = rawJson;
                    Debug.Log("[ExternalActionFeed] Applied external response");

                    // 前の返答の分割再生を打ち切る。新しい返答が音を鳴らした場合は再生側で既に
                    // 破棄済み（no-op）。鳴らさない返答（message無し・TTS OFF・thinking 等）でも
                    // 前の返答の続きが流れ続けたり、done 待ちで喋り途中のまま残ったりしないようにする
                    if (_partsSerial != 0)
                    {
                        synth?.CancelExternalParts(_partsSerial);
                        _partsSerial = 0;
                    }

                    if (isThinking)
                    {
                        PlayThinkingVoice(response, voiceClip);
                    }
                    else if (isParts)
                    {
                        yield return StartParts(marker, voiceClip != null, synth, serialBefore);
                    }
                    else
                    {
                        // 新しい通常の返答で分割配信の続きの判定は終わり
                        _partsTimestamp = null;
                        _partsSerial = 0;
                    }
                }
                else
                {
                    if (voiceClip != null) Destroy(voiceClip);
                    Debug.Log("[ExternalActionFeed] Apply skipped (busy/outing/entry), will retry next poll");
                }
            }
            finally
            {
                _isFetching = false;
            }
        }

        /// <summary>
        /// thinking の作業状況メッセージの読み上げ。
        /// 音声ソースは応答音声と同じ優先順: voice.wav（取得済みクリップ）→ 本体TTS（有効時）→ なし。
        /// クリップの所有権は再生側へ渡す（使わない場合はここで破棄）
        /// </summary>
        private void PlayThinkingVoice(LLMResponseData response, AudioClip voiceClip)
        {
            var synth = chatManager.voiceSynthesisController;
            if (!thinkingVoiceEnabled || synth == null)
            {
                if (voiceClip != null) Destroy(voiceClip);
                return;
            }

            if (voiceClip != null)
            {
                _lastSpokenThinkingStatus = response.message?.Trim();
                synth.PlayThinkingClip(voiceClip, thinkingVoicePitch);
            }
            else if (!string.IsNullOrWhiteSpace(response.message))
            {
                string status = response.message.Trim();

                // 同じ状況文の再受信は読み上げ直さない（タイムアウト延長のための再PUT等）
                if (status == _lastSpokenThinkingStatus) return;

                _lastSpokenThinkingStatus = status;
                synth.SpeakThinkingStatus(status, thinkingVoicePitch);
            }
        }

        // 音声関連のフィールドだけを取り出す部分パース用（他フィールドは無視される）
        [System.Serializable]
        private struct FeedMarker
        {
            public string timestamp;
            public string voice_timestamp;   // 従来形式: voice.wav が付いている印
            public int voice_parts;          // 分割配信: いま置いてある区切りの数（無ければ0）
            public bool voice_done;          // 分割配信: 最後の区切りまで置き終えたか
        }

        /// <summary>
        /// 生JSONから音声関連のフィールドを抽出。パース失敗時は全て既定値
        /// </summary>
        private static FeedMarker ExtractMarker(string rawJson)
        {
            try
            {
                return JsonUtility.FromJson<FeedMarker>(rawJson);
            }
            catch (System.Exception)
            {
                return default;
            }
        }

        /// <summary>
        /// 分割配信の返答を適用した直後の処理: 先頭の区切りの再生に後続の列を紐づけ、
        /// 届いている残りの区切りを取得する
        /// </summary>
        private IEnumerator StartParts(FeedMarker marker, bool firstPartFetched,
            CyanNook.Voice.VoiceSynthesisController synth, int serialBefore)
        {
            _partsTimestamp = marker.timestamp;
            _partsSerial = 0;

            // 先頭が取れなかった（ボイス再生OFF・取得失敗）場合は自前TTSで全文を読んでいるため、
            // 後続の区切りは使わない（以後の置き直しは同じ timestamp として読み飛ばされる）
            if (!firstPartFetched || synth == null)
            {
                _partsFetched = marker.voice_parts;
                Debug.Log($"[ExternalActionFeed] Voice parts: first part unavailable, using own TTS for full message (ts={marker.timestamp})");
                yield break;
            }

            _partsFetched = 1;

            // 適用で先頭の区切りの再生が始まっていれば（外出中の抑制等で再生されなかった場合は除く）
            // そのクリップに後続の列を紐づける
            int serial = synth.CurrentExternalClipSerial;
            if (serial == 0 || serial == serialBefore || !synth.BeginExternalParts(serial))
            {
                _partsFetched = marker.voice_parts;
                Debug.Log($"[ExternalActionFeed] Voice parts: first part was not played, ignoring later parts (ts={marker.timestamp})");
                yield break;
            }

            _partsSerial = serial;
            Debug.Log($"[ExternalActionFeed] Voice parts started: part 1/{marker.voice_parts}{(marker.voice_done ? " (done)" : "")} (ts={marker.timestamp}, serial={serial})");
            yield return FetchRemainingParts(marker);
        }

        /// <summary>
        /// まだ取っていない区切り（_partsFetched+1 〜 voice_parts）を順に取得して列に積む。
        /// 取得に失敗した区切りは飛ばす（外部側は wav を置いてから json を置くため、
        /// 失敗は一時的な遅れではなく本当のエラーとみなし、待って後ろを詰まらせない）
        /// </summary>
        private IEnumerator FetchRemainingParts(FeedMarker marker)
        {
            var synth = chatManager.voiceSynthesisController;
            if (synth == null || !synth.IsExternalPartsActive(_partsSerial)) yield break;

            while (_partsFetched < marker.voice_parts)
            {
                int k = _partsFetched + 1;
                AudioClip clip = null;
                yield return FetchVoiceClip(ResolvePartUrl(k), marker.timestamp, k, c => clip = c);
                _partsFetched = k;

                // 取得中に停止・差し替え・OFFになっていたら以降は不要
                if (!IsActive || !synth.IsExternalPartsActive(_partsSerial))
                {
                    if (clip != null) Destroy(clip);
                    yield break;
                }

                if (clip != null)
                {
                    Debug.Log($"[ExternalActionFeed] Voice part {k}/{marker.voice_parts} fetched ({clip.length:F1}s, serial={_partsSerial})");
                    synth.EnqueueExternalPart(_partsSerial, clip);
                }
            }

            if (marker.voice_done)
            {
                Debug.Log($"[ExternalActionFeed] Voice parts done: {marker.voice_parts} parts received (serial={_partsSerial})");
                synth.SetExternalPartsDone(_partsSerial);
            }
        }

        /// <summary>
        /// voice.wav の実効URLを返す。設定が空ならアクション購読URLと同じ場所の voice.wav を使う
        /// （外部側（herald）が action.json の隣に voice.wav を置く規約に対応）
        /// </summary>
        private string ResolveVoiceUrl()
        {
            if (!string.IsNullOrEmpty(voiceSubscribeUrl)) return voiceSubscribeUrl;
            if (string.IsNullOrEmpty(actionSubscribeUrl)) return null;

            int slash = actionSubscribeUrl.LastIndexOf('/');
            if (slash < 0) return null;
            return actionSubscribeUrl.Substring(0, slash + 1) + "voice.wav";
        }

        /// <summary>
        /// 分割配信の k 番目の区切りの URL。voice.wav と同じ場所の voice_k.wav
        /// </summary>
        private string ResolvePartUrl(int k)
        {
            string voiceUrl = ResolveVoiceUrl();
            if (string.IsNullOrEmpty(voiceUrl)) return null;

            // クエリ付きURL（?以降）を除いたパスの最後の / までをディレクトリとする
            int query = voiceUrl.IndexOf('?');
            string path = query >= 0 ? voiceUrl.Substring(0, query) : voiceUrl;
            int slash = path.LastIndexOf('/');
            if (slash < 0) return null;
            return path.Substring(0, slash + 1) + $"voice_{k}.wav";
        }

        /// <summary>
        /// wav を取得してAudioClip化する。失敗時はonLoadedを呼ばず警告のみ
        /// （呼び出し側が自前TTSにフォールバック / 区切りを飛ばす）。
        /// part は分割配信の区切り番号（キャッシュバスターに含める。従来の voice.wav は 0）
        /// </summary>
        private IEnumerator FetchVoiceClip(string baseUrl, string timestamp, int part, System.Action<AudioClip> onLoaded)
        {
            if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(timestamp)) yield break;

            // 失敗時の扱い（ログ用）: voice.wav と分割の先頭は自前TTSへフォールバック、2番目以降は飛ばす
            string onFail = part >= 2 ? $"skip part {part}" : (part == 1 ? "part 1, fallback to TTS" : "fallback to TTS");

            // 外部側の規約に合わせ、jsonのタイムスタンプ（と区切り番号）をキャッシュバスターに使う
            char separator = baseUrl.Contains("?") ? '&' : '?';
            string url = $"{baseUrl}{separator}t={UnityWebRequest.EscapeURL(timestamp)}";
            if (part > 0) url += $"&p={part}";

            // まず生のバイト列で取得し、リニアPCM（8/16bit）なら自前で AudioClip を組み立てる。
            // 組み立て時に音量の変化（AudioEnvelope）が記録され、WebGL でも口パクが動く
            // （ブラウザに解読させたクリップは WebGL で再生中の音量を読めず、口が開かない）
            byte[] wavBytes = null;
            using (var request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("Cache-Control", "no-cache");
                request.SetRequestHeader("Pragma", "no-cache");

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[ExternalActionFeed] Voice fetch failed ({onFail}): {request.error}");
                    yield break;
                }

                wavBytes = request.downloadHandler.data;
            }

            if (CyanNook.Voice.WavUtility.IsSupportedPcm(wavBytes))
            {
                var pcmClip = CyanNook.Voice.WavUtility.ToAudioClip(wavBytes, part > 0 ? $"FeedVoicePart{part}" : "FeedVoice");
                if (pcmClip != null)
                {
                    onLoaded(pcmClip);
                    yield break;
                }
                // 組み立てに失敗した（ヘッダ不整合等）場合もブラウザ側の解読を試す
                Debug.LogWarning("[ExternalActionFeed] Voice PCM conversion failed, decoding via browser (lip sync unavailable on WebGL)");
            }
            else
            {
                // リニアPCM以外の wav はブラウザ側の解読に任せる（音は鳴るが、WebGL では口パクしない）。
                // 対応形式を増やすのは要望があってから
                Debug.LogWarning("[ExternalActionFeed] Voice is not 8/16bit linear PCM wav, decoding via browser (lip sync unavailable on WebGL)");
            }
            wavBytes = null;

            AudioClip clip = null;
            using (var request = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.WAV))
            {
                request.SetRequestHeader("Cache-Control", "no-cache");
                request.SetRequestHeader("Pragma", "no-cache");

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[ExternalActionFeed] Voice fetch failed ({onFail}): {request.error}");
                    yield break;
                }

                try
                {
                    clip = DownloadHandlerAudioClip.GetContent(request);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[ExternalActionFeed] Voice decode failed ({onFail}): {e.Message}");
                }
            }

            if (clip == null) yield break;

            // WebGLではブラウザ側デコードが非同期で、loadStateがLoadingにならず
            // Unloadedのまま進む（完了後もLoadedにならない場合がある）ため、
            // 「Loadedになる or クリップ長が確定する（デコード完了の実質的な印）」を
            // タイムアウト付きで待つ。Failedは即失敗
            float deadline = Time.realtimeSinceStartup + 10f;
            while (clip.loadState != AudioDataLoadState.Loaded &&
                   clip.loadState != AudioDataLoadState.Failed &&
                   clip.length <= 0f)
            {
                if (Time.realtimeSinceStartup > deadline) break;
                yield return null;
            }

            if (clip.loadState == AudioDataLoadState.Failed ||
                (clip.loadState != AudioDataLoadState.Loaded && clip.length <= 0f))
            {
                Debug.LogWarning($"[ExternalActionFeed] Voice clip failed to load ({onFail}, state={clip.loadState}, length={clip.length})");
                Destroy(clip);
                yield break;
            }

            onLoaded(clip);
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
            PlayerPrefs.SetInt(PrefKey_VoiceEnabled, voiceEnabled ? 1 : 0);
            PlayerPrefs.SetString(PrefKey_VoiceUrl, voiceSubscribeUrl ?? "");
            PlayerPrefs.SetInt(PrefKey_ThinkingVoiceEnabled, thinkingVoiceEnabled ? 1 : 0);
            PlayerPrefs.SetFloat(PrefKey_ThinkingVoicePitch, thinkingVoicePitch);
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
            if (PlayerPrefs.HasKey(PrefKey_VoiceEnabled))
            {
                voiceEnabled = PlayerPrefs.GetInt(PrefKey_VoiceEnabled) == 1;
            }
            if (PlayerPrefs.HasKey(PrefKey_VoiceUrl))
            {
                voiceSubscribeUrl = PlayerPrefs.GetString(PrefKey_VoiceUrl);
            }
            if (PlayerPrefs.HasKey(PrefKey_ThinkingVoiceEnabled))
            {
                thinkingVoiceEnabled = PlayerPrefs.GetInt(PrefKey_ThinkingVoiceEnabled) == 1;
            }
            if (PlayerPrefs.HasKey(PrefKey_ThinkingVoicePitch))
            {
                thinkingVoicePitch = Mathf.Clamp(PlayerPrefs.GetFloat(PrefKey_ThinkingVoicePitch), 0.5f, 3f);
            }
        }
    }
}
