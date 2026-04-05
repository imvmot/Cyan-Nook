using System;
using System.Collections;
using UnityEngine;
using CyanNook.Core;
using CyanNook.Chat;
using CyanNook.Furniture;

namespace CyanNook.Character
{
    /// <summary>
    /// 睡眠状態の管理コントローラー
    /// - 夢メッセージの定期送信（IdleChat応用）
    /// - 起床タイマー（sleep_duration）
    /// - PlayerPrefs永続化（アプリ再起動時の復元）
    /// - Sleep中の応答抑制（Zzz...表示）
    /// </summary>
    public class SleepController : MonoBehaviour
    {
        // PlayerPrefsキー
        private const string PrefKey_SleepState = "sleep_state";
        private const string PrefKey_WakeTime = "sleep_wake_time";
        private const string PrefKey_FurnitureId = "sleep_furniture_id";
        private const string PrefKey_DefaultDuration = "sleep_defaultDuration";
        private const string PrefKey_MinDuration = "sleep_minDuration";
        private const string PrefKey_MaxDuration = "sleep_maxDuration";
        private const string PrefKey_DreamInterval = "sleep_dreamInterval";
        private const string PrefKey_DreamMessage = "sleep_dreamMessage";
        private const string PrefKey_WakeUpMessage = "sleep_wakeUpMessage";

        [Header("References")]
        public ChatManager chatManager;
        public InteractionController interactionController;
        public FurnitureManager furnitureManager;
        public IdleChatController idleChatController;
        public BoredomController boredomController;
        public VrmLoader vrmLoader;
        [Header("Sleep Timer")]
        [Tooltip("sleep_duration 未指定時のデフォルト値（分）")]
        public int defaultSleepDuration = 30;

        [Tooltip("sleep_duration の最小値（分）")]
        public int minSleepDuration = 5;

        [Tooltip("sleep_duration の最大値（分）")]
        public int maxSleepDuration = 480;

        [Header("Dream Messages")]
        [Tooltip("夢メッセージの送信間隔（分）")]
        public float dreamInterval = 5f;

        [TextArea(2, 4)]
        [Tooltip("夢メッセージのシステムプロンプト")]
        public string dreamPromptMessage = "[system]あなたは睡眠中です。今日の出来事を夢として思い返してください。";

        [Header("Wake-up")]
        [TextArea(2, 4)]
        [Tooltip("起床時にユーザーメッセージに付与するシステムメッセージ")]
        public string wakeUpSystemMessage = "[system]あなたは今起きたところです。";

        [Header("Display")]
        [Tooltip("Sleep中にメッセージの代わりに表示するテキスト")]
        public string sleepDisplayText = "Zzz...";

        [Header("State")]
        [SerializeField]
        private bool _isSleeping = false;

        private float _dreamTimer;
        private DateTime _wakeTime;
        private string _sleepFurnitureId;
        private bool _pendingDreamMessage;

        // Wake-up完了時のコールバック
        private Action _onWakeUpComplete;

        // Wake-up with message: ed再生中のレスポンスキュー
        private bool _isWakingUp;
        private Action<LLMResponseData> _onWakeUpEdComplete;
        private LLMResponseData _queuedWakeUpResponse;

        /// <summary>睡眠中かどうか</summary>
        public bool IsSleeping => _isSleeping;

        /// <summary>起床アニメーション再生中かどうか（ed再生中）</summary>
        public bool IsWakingUp => _isWakingUp;

        /// <summary>起床予定時刻</summary>
        public DateTime WakeTime => _wakeTime;

        /// <summary>起床時のシステムメッセージ</summary>
        public string WakeUpSystemMessage => wakeUpSystemMessage;

        /// <summary>Sleep中の表示テキスト</summary>
        public string SleepDisplayText => sleepDisplayText;

        /// <summary>現在の状態（デバッグ用）</summary>
        public string CurrentStateInfo
        {
            get
            {
                if (!_isSleeping) return "Awake";
                var remaining = _wakeTime - DateTime.Now;
                if (remaining.TotalSeconds <= 0) return "Sleep (waking up...)";
                return $"Sleep (wake in {remaining.TotalMinutes:F0}m, dream in {_dreamTimer / 60f:F1}m)";
            }
        }

        private void Start()
        {
            LoadSettings();
        }

        private void Update()
        {
            if (!_isSleeping) return;

            // 起床タイマー監視
            if (DateTime.Now >= _wakeTime)
            {
                Debug.Log("[SleepController] Wake timer expired");
                ExitSleep(null);
                return;
            }

            // 保留中のDream Promptをリトライ
            if (_pendingDreamMessage)
            {
                if (chatManager != null && chatManager.CurrentState == ChatState.Idle)
                {
                    _pendingDreamMessage = false;
                    Debug.Log("[SleepController] Retrying pending dream message");
                    chatManager.SendAutoRequest(dreamPromptMessage);
                }
                return; // 保留中はタイマーを進めない
            }

            // 夢メッセージタイマー
            _dreamTimer -= Time.deltaTime;
            if (_dreamTimer <= 0f)
            {
                SendDreamMessage();
                _dreamTimer = dreamInterval * 60f; // 分→秒変換
            }
        }

        // ===================================================================
        // 公開API
        // ===================================================================

        /// <summary>
        /// 睡眠状態に遷移
        /// </summary>
        /// <param name="durationMinutes">睡眠時間（分）。0の場合はデフォルト値を使用</param>
        /// <param name="furnitureId">寝ている家具のinstanceId</param>
        public void EnterSleep(int durationMinutes, string furnitureId)
        {
            if (_isSleeping)
            {
                Debug.LogWarning("[SleepController] Already sleeping");
                return;
            }

            // 睡眠時間の決定
            int duration = durationMinutes > 0 ? durationMinutes : defaultSleepDuration;
            duration = Mathf.Clamp(duration, minSleepDuration, maxSleepDuration);

            _isSleeping = true;
            _wakeTime = DateTime.Now.AddMinutes(duration);
            _sleepFurnitureId = furnitureId;

            // 初回Dream Promptを即送信（ループリージョン開始時に呼ばれるため）
            SendDreamMessage();
            _dreamTimer = dreamInterval * 60f; // 2回目以降はInterval経過後（分→秒変換）

            // IdleChat停止
            if (idleChatController != null)
            {
                idleChatController.SetPaused(true);
            }

            // Boredom蓄積停止
            if (boredomController != null)
            {
                boredomController.SetPaused(true);
            }

            // 永続化
            SaveSleepState();

            Debug.Log($"[SleepController] EnterSleep: duration={duration}min, wake at {_wakeTime:HH:mm:ss}, furniture={furnitureId}");
        }

        /// <summary>
        /// 起床処理
        /// interact_sleep のloop脱出 → ed再生 → Idle遷移 → コールバック
        /// </summary>
        /// <param name="onComplete">起床完了後のコールバック（null可）</param>
        public void ExitSleep(Action onComplete)
        {
            if (!_isSleeping)
            {
                onComplete?.Invoke();
                return;
            }

            Debug.Log("[SleepController] ExitSleep started");

            _pendingDreamMessage = false;
            _onWakeUpComplete = onComplete;

            // interact_sleep のloop脱出（CancelRegionスキップでed全体を再生）
            if (interactionController != null && interactionController.IsInteracting())
            {
                interactionController.ExitLoopWithCallback(OnWakeUpAnimationComplete, skipCancelRegion: true);
            }
            else
            {
                // インタラクション中でなければ直接完了
                OnWakeUpAnimationComplete();
            }
        }

        /// <summary>
        /// メッセージ付き起床処理
        /// ed開始と同時にメッセージ送信し、ed再生中にLLM応答をキューする。
        /// ed完了後にonEdComplete(queuedResponse)を呼び出す。
        /// queuedResponseがnullの場合はLLM未応答（Thinking開始が必要）。
        /// </summary>
        /// <param name="onEdComplete">ed完了後のコールバック（キューされたレスポンス付き、null=未応答）</param>
        public void WakeUpWithMessage(Action<LLMResponseData> onEdComplete)
        {
            if (!_isSleeping)
            {
                onEdComplete?.Invoke(null);
                return;
            }

            Debug.Log("[SleepController] WakeUpWithMessage started");

            _isWakingUp = true;
            _onWakeUpEdComplete = onEdComplete;
            _queuedWakeUpResponse = null;

            // interact_sleep のloop脱出（CancelRegionスキップでed全体を再生）
            if (interactionController != null && interactionController.IsInteracting())
            {
                interactionController.ExitLoopWithCallback(OnWakeUpAnimationComplete, skipCancelRegion: true);
            }
            else
            {
                OnWakeUpAnimationComplete();
            }

            // メッセージ送信は呼び出し側で即実行（fall-through）
            // 将来: Timeline Signalで送信タイミングを制御する場合は
            //   onSendMessage コールバックを追加し、Signal到達時に発火する
        }

        /// <summary>
        /// 起床アニメーション再生中にLLM応答をキューする
        /// ed完了後にCharacterControllerで処理される
        /// </summary>
        public void QueueWakeUpResponse(LLMResponseData response)
        {
            if (!_isWakingUp)
            {
                Debug.LogWarning("[SleepController] QueueWakeUpResponse called but not waking up");
                return;
            }
            _queuedWakeUpResponse = response;
            Debug.Log($"[SleepController] Response queued during ed: action={response?.action}");
        }

        /// <summary>
        /// アプリ起動時にsleep状態で開始すべきかチェック
        /// </summary>
        public bool ShouldStartAsSleep()
        {
            if (!PlayerPrefs.HasKey(PrefKey_SleepState)) return false;
            if (PlayerPrefs.GetInt(PrefKey_SleepState) != 1) return false;

            // タイマー超過チェック
            if (PlayerPrefs.HasKey(PrefKey_WakeTime))
            {
                string wakeTimeStr = PlayerPrefs.GetString(PrefKey_WakeTime);
                if (DateTime.TryParse(wakeTimeStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime savedWakeTime))
                {
                    if (DateTime.Now >= savedWakeTime)
                    {
                        // タイマー超過 → sleep解除
                        Debug.Log("[SleepController] Wake time has passed, clearing sleep state");
                        ClearSleepState();
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// アプリ起動時のsleep状態復元
        /// 家具を検索してinteract_sleepのループリージョンから直接開始
        /// </summary>
        /// <param name="animationController">アニメーションコントローラー</param>
        /// <param name="navigationController">ナビゲーションコントローラー</param>
        /// <returns>復元成功かどうか</returns>
        public bool RestoreSleep(CharacterAnimationController animationController,
            CharacterNavigationController navigationController)
        {
            string furnitureId = PlayerPrefs.GetString(PrefKey_FurnitureId, "");
            string wakeTimeStr = PlayerPrefs.GetString(PrefKey_WakeTime, "");

            if (string.IsNullOrEmpty(furnitureId) || furnitureManager == null)
            {
                Debug.LogWarning("[SleepController] Cannot restore sleep: missing furniture data");
                ClearSleepState();
                return false;
            }

            // 家具を検索
            var furniture = furnitureManager.GetFurnitureInstance(furnitureId);
            if (furniture == null)
            {
                Debug.LogWarning($"[SleepController] Cannot restore sleep: furniture '{furnitureId}' not found");
                ClearSleepState();
                return false;
            }

            // 起床時刻を復元
            if (DateTime.TryParse(wakeTimeStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime savedWakeTime))
            {
                _wakeTime = savedWakeTime;
            }
            else
            {
                _wakeTime = DateTime.Now.AddMinutes(defaultSleepDuration);
            }

            _isSleeping = true;
            _sleepFurnitureId = furnitureId;
            _dreamTimer = dreamInterval * 60f; // 分→秒変換（復元時は即送信しない）

            // キャラクターを家具のインタラクションポイントに配置
            var interactionPoints = furniture.GetInteractionPointsForAction("sleep");
            Vector3 targetPos;
            Quaternion targetRot;
            if (interactionPoints != null && interactionPoints.Length > 0)
            {
                targetPos = interactionPoints[0].position;
                targetRot = interactionPoints[0].rotation;
            }
            else
            {
                targetPos = furniture.transform.position;
                targetRot = furniture.transform.rotation;
            }

            if (navigationController != null)
            {
                navigationController.Warp(targetPos, targetRot);
            }

            // InteractionControllerの状態復元（ExitLoopでed再生が正しく動作するため）
            if (interactionController != null)
            {
                interactionController.RestoreInteractionState(furniture, "sleep", targetPos, targetRot);
            }

            // interact_sleepアニメーションをループリージョンから開始
            if (animationController != null)
            {
                string animId = "interact_sleep01";
                animationController.PlayState(AnimationStateType.Interact, animId, resumeAtLoop: true, skipBlend: true);
                Debug.Log("[SleepController] Restored sleep animation from loop region");
            }

            // IdleChat停止
            if (idleChatController != null)
            {
                idleChatController.SetPaused(true);
            }

            // Boredom蓄積停止
            if (boredomController != null)
            {
                boredomController.SetPaused(true);
            }

            Debug.Log($"[SleepController] Sleep restored: furniture={furnitureId}, wake at {_wakeTime:HH:mm:ss}");
            return true;
        }

        // ===================================================================
        // 内部メソッド
        // ===================================================================

        /// <summary>
        /// 起床アニメーション完了時のコールバック
        /// WakeUpWithMessage / ExitSleep 両方に対応
        /// </summary>
        private void OnWakeUpAnimationComplete()
        {
            _isSleeping = false;
            _isWakingUp = false;

            // IdleChat再開
            if (idleChatController != null)
            {
                idleChatController.SetPaused(false);
            }

            // Boredom蓄積再開
            if (boredomController != null)
            {
                boredomController.SetPaused(false);
            }

            // 永続化クリア
            ClearSleepState();

            Debug.Log("[SleepController] Wake-up complete");

            // WakeUpWithMessage: キューされたレスポンス付きコールバック
            if (_onWakeUpEdComplete != null)
            {
                var edCallback = _onWakeUpEdComplete;
                _onWakeUpEdComplete = null;
                var queuedResponse = _queuedWakeUpResponse;
                _queuedWakeUpResponse = null;
                edCallback.Invoke(queuedResponse);
                return;
            }

            // ExitSleep: シンプルなコールバック（タイマー起床等）
            var callback = _onWakeUpComplete;
            _onWakeUpComplete = null;
            callback?.Invoke();
        }

        /// <summary>
        /// 夢メッセージをLLMに送信
        /// </summary>
        private void SendDreamMessage()
        {
            if (chatManager == null) return;

            if (chatManager.CurrentState != ChatState.Idle)
            {
                _pendingDreamMessage = true;
                Debug.Log("[SleepController] Dream message deferred (ChatManager busy)");
                return;
            }

            _pendingDreamMessage = false;
            Debug.Log("[SleepController] Sending dream message");
            chatManager.SendAutoRequest(dreamPromptMessage);
        }

        // ===================================================================
        // 永続化
        // ===================================================================

        private void SaveSleepState()
        {
            PlayerPrefs.SetInt(PrefKey_SleepState, 1);
            PlayerPrefs.SetString(PrefKey_WakeTime, _wakeTime.ToString("O")); // ISO 8601
            PlayerPrefs.SetString(PrefKey_FurnitureId, _sleepFurnitureId ?? "");
            PlayerPrefs.Save();
        }

        private void ClearSleepState()
        {
            _isSleeping = false;
            PlayerPrefs.SetInt(PrefKey_SleepState, 0);
            PlayerPrefs.DeleteKey(PrefKey_WakeTime);
            PlayerPrefs.DeleteKey(PrefKey_FurnitureId);
            PlayerPrefs.Save();
        }

        private void LoadSettings()
        {
            if (PlayerPrefs.HasKey(PrefKey_DefaultDuration))
                defaultSleepDuration = PlayerPrefs.GetInt(PrefKey_DefaultDuration);
            if (PlayerPrefs.HasKey(PrefKey_MinDuration))
                minSleepDuration = PlayerPrefs.GetInt(PrefKey_MinDuration);
            if (PlayerPrefs.HasKey(PrefKey_MaxDuration))
                maxSleepDuration = PlayerPrefs.GetInt(PrefKey_MaxDuration);
            if (PlayerPrefs.HasKey(PrefKey_DreamInterval))
                dreamInterval = PlayerPrefs.GetFloat(PrefKey_DreamInterval);
            if (PlayerPrefs.HasKey(PrefKey_DreamMessage))
                dreamPromptMessage = PlayerPrefs.GetString(PrefKey_DreamMessage);
            if (PlayerPrefs.HasKey(PrefKey_WakeUpMessage))
                wakeUpSystemMessage = PlayerPrefs.GetString(PrefKey_WakeUpMessage);
        }

        /// <summary>
        /// 夢メッセージ間隔を設定（分）
        /// </summary>
        public void SetDreamInterval(float minutes)
        {
            dreamInterval = Mathf.Max(1f, minutes);
            PlayerPrefs.SetFloat(PrefKey_DreamInterval, dreamInterval);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// 夢メッセージプロンプトを設定
        /// </summary>
        public void SetDreamPromptMessage(string message)
        {
            dreamPromptMessage = message;
            PlayerPrefs.SetString(PrefKey_DreamMessage, dreamPromptMessage);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// デフォルト睡眠時間を設定（分）
        /// </summary>
        public void SetDefaultSleepDuration(int minutes)
        {
            defaultSleepDuration = Mathf.Max(1, minutes);
            PlayerPrefs.SetInt(PrefKey_DefaultDuration, defaultSleepDuration);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// 最小睡眠時間を設定（分）
        /// </summary>
        public void SetMinSleepDuration(int minutes)
        {
            minSleepDuration = Mathf.Max(1, minutes);
            PlayerPrefs.SetInt(PrefKey_MinDuration, minSleepDuration);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// 最大睡眠時間を設定（分）
        /// </summary>
        public void SetMaxSleepDuration(int minutes)
        {
            maxSleepDuration = Mathf.Max(1, minutes);
            PlayerPrefs.SetInt(PrefKey_MaxDuration, maxSleepDuration);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// 起床時システムメッセージを設定
        /// </summary>
        public void SetWakeUpSystemMessage(string message)
        {
            wakeUpSystemMessage = message;
            PlayerPrefs.SetString(PrefKey_WakeUpMessage, wakeUpSystemMessage);
            PlayerPrefs.Save();
        }
    }
}
