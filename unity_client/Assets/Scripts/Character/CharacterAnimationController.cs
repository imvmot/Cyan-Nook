using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using CyanNook.Core;
using CyanNook.Furniture;
using CyanNook.Timeline;
using System.Collections.Generic;
using System.Linq;
using TMPro;

namespace CyanNook.Character
{
    /// <summary>
    /// キャラクターのアニメーション制御を担当（Timeline主導）
    /// PlayableDirectorを使用してTimelineを再生・管理
    /// </summary>
    public class CharacterAnimationController : MonoBehaviour
    {
        [Header("References")]
        public PlayableDirector director;
        public Animator animator; // Timelineのバインディング対象
        public CharacterTemplateData templateData;

        [Header("Inertial Blend")]
        [Tooltip("慣性補間ヘルパー（VRMインスタンスにアタッチ）")]
        public InertialBlendHelper inertialBlendHelper;

        [Header("Additive Override")]
        [Tooltip("加算ボーンオーバーライドヘルパー（VRMインスタンスにアタッチ）")]
        public AdditiveOverrideHelper additiveOverrideHelper;

        [Header("LookAt")]
        [Tooltip("LookAtControllerの参照（LookAtTrackバインディング用）")]
        public CharacterLookAtController lookAtController;

        [Header("Expression")]
        [Tooltip("ExpressionControllerの参照（VrmExpressionTrackバインディング用）")]
        public CharacterExpressionController expressionController;

        [Header("Navigation")]
        [Tooltip("NavigationControllerの参照（MoveSpeedTrackバインディング用）")]
        public CharacterNavigationController navigationController;

        [Header("Light")]
        [Tooltip("RoomLightControllerの参照（LightControlTrackバインディング用）")]
        public RoomLightController roomLightController;

        [Header("Timeline Assets")]
        [Tooltip("ステートごとのTimelineアセット")]
        public TimelineBindingData timelineBindings;

        [Header("Emote Hold")]
        [Tooltip("テキスト表示完了後、emoteループを維持する時間（秒）。この時間経過後にedモーションを再生して終了")]
        public float emoteHoldDuration = 5f;

        [Header("State")]
        [SerializeField]
        private AnimationStateType _currentState = AnimationStateType.Idle;
        public AnimationStateType CurrentState => _currentState;

        [SerializeField]
        private string _currentAnimationId = "";
        public string CurrentAnimationId => _currentAnimationId;

        // Timeline再生完了イベント
        public event System.Action<AnimationStateType> OnAnimationComplete;

        // 位置保持用（非移動ステートで使用）
        private Transform _characterTransform;
        private Vector3 _preservedPosition;
        private Quaternion _preservedRotation;
        private bool _shouldPreservePosition;
        private bool _positionExplicitlySet;  // SetPreservedPositionで明示的に設定されたか

        // --- InteractionEnd制御 ---
        private bool _hasInteractionEnd;
        private double _interactionEndTime;
        private bool _interactionEndFired;
        private bool _interactionEndDirectorWasPlaying; // director再生状態の追跡（WebGL stopped未発火対策）

        // --- LoopRegion制御 ---
        private bool _hasLoopRegion;
        private double _loopStartTime;
        private double _loopEndTime;
        private double _endStartTime;
        private bool _isInLoop;
        private bool _shouldExitLoop;
        private bool _isEnding;
        private int _endingFrameCount;
        private const int ENDING_GRACE_FRAMES = 3;


        // Timelineのフレームレートが取得できない場合のデフォルト値
        private const double DEFAULT_FRAME_RATE = 60.0;

        // --- Walk終了フェーズ制御 ---
        private bool _isWalkEndPhaseActive;

        // --- CancelRegion制御 ---
        private struct CancelRegion
        {
            public double startTime;
            public double endTime;
            public List<TimelineAsset> allowedTransitions;
        }

        private List<CancelRegion> _cancelRegions = new List<CancelRegion>();
        private bool _shouldCancelAtRegion = false;

        /// <summary>LoopRegionTrackが存在するか</summary>
        public bool HasLoopRegion => _hasLoopRegion;

        /// <summary>現在ループ中か</summary>
        public bool IsInLoop => _isInLoop;

        /// <summary>終了フェーズ（ed区間）再生中か</summary>
        public bool IsInEndPhase => _isEnding;

        /// <summary>現在キャンセル可能か</summary>
        public bool CanCancel => _cancelRegions.Any(
            r => director != null && director.time >= r.startTime && director.time <= r.endTime);

        /// <summary>ActionCancelTrackのクリップが存在するか</summary>
        public bool HasCancelRegions => _cancelRegions.Count > 0;

        /// <summary>LoopStart到達時</summary>
        public event System.Action OnLoopEntered;

        /// <summary>ed再生完了時（またはedなしでEndStart到達時）</summary>
        public event System.Action OnEndPhaseComplete;

        /// <summary>キャンセル実行時（遷移先Timelineを通知）</summary>
        public event System.Action<TimelineAsset> OnCancelExecuted;

        /// <summary>CancelRegion到達時（edフェーズスキップ用）</summary>
        public event System.Action OnCancelRegionReached;

        /// <summary>InteractionEndClip到達時（LoopRegionなしInteractの完了通知）</summary>
        public event System.Action OnInteractionEndReached;

        /// <summary>
        /// 位置保持が有効かどうか（外部からの参照用）
        /// Idle等の非移動ステートではtrue、Walk/Run/Interactではfalse
        /// </summary>
        public bool ShouldPreservePosition => _shouldPreservePosition;

        // LoopRegionジャンプ時のフラグ（巻き戻しデルタを無視するため）
        private bool _loopJumpOccurred;

        // Emoteホールド制御
        private bool _emoteTextDisplayComplete = true;
        private float _emoteHoldTimer;

        // Walk-Turn制御（歩行開始時の旋回アニメーション選択）
        public enum TurnMode { Normal, TurnLeft, TurnRight }
        private TurnMode _turnMode = TurnMode.Normal;

        /// <summary>
        /// 次のPlayState呼び出し時のWalk-Turn方向を設定
        /// BindAnimatorToTimeline内で消費され、自動的にNormalにリセットされる
        /// </summary>
        public void SetTurnMode(TurnMode mode) { _turnMode = mode; }

        /// <summary>
        /// ループジャンプフラグを消費して返す。
        /// Root Motion適用時に呼び出し、ループジャンプ直後の大きな負のdeltaを無視するために使用。
        /// </summary>
        public bool ConsumeLoopJumpFlag()
        {
            if (_loopJumpOccurred)
            {
                _loopJumpOccurred = false;
                return true;
            }
            return false;
        }

        private void Awake()
        {
            if (director == null)
            {
                director = GetComponent<PlayableDirector>();
            }

            if (director != null)
            {
                director.stopped += OnTimelineStopped;
            }
        }

        private void OnDestroy()
        {
            if (director != null)
            {
                director.stopped -= OnTimelineStopped;
            }
        }

        /// <summary>
        /// キャラクターのTransformを設定（位置保持に使用）
        /// </summary>
        public void SetCharacterTransform(Transform characterTransform)
        {
            _characterTransform = characterTransform;
        }

        /// <summary>
        /// 保持位置を明示的に設定
        /// インタラクション終了時など、特定の位置を維持したい場合に使用
        /// このメソッドで設定した場合、次回のSetPositionPreservationでは上書きされない
        /// </summary>
        public void SetPreservedPosition(Vector3 position, Quaternion rotation)
        {
            _preservedPosition = position;
            _preservedRotation = rotation;
            _positionExplicitlySet = true;  // 明示的に設定されたことを記録
            Debug.Log($"[CharacterAnimationController] PreservedPosition explicitly set to: pos={position}, rot={rotation.eulerAngles}");
        }

        /// <summary>
        /// LateUpdateで非移動ステートの位置を保持
        /// TimelineのAnimationTrackが位置を上書きした後に復元
        /// </summary>
        private void LateUpdate()
        {
            if (_shouldPreservePosition && _characterTransform != null)
            {
                _characterTransform.position = _preservedPosition;
                _characterTransform.rotation = _preservedRotation;
            }

            // Timeline Debug表示を更新
            UpdateTimelineDebugText();
        }

        private void Update()
        {
            // Emoteホールドタイマー（テキスト表示完了後にemoteループを自動解除）
            if (_currentState == AnimationStateType.Emote && _hasLoopRegion && _isInLoop
                && !_shouldExitLoop && _emoteTextDisplayComplete)
            {
                _emoteHoldTimer += Time.deltaTime;
                if (_emoteHoldTimer >= emoteHoldDuration)
                {
                    Debug.Log("[CharacterAnimationController] Emote hold expired, requesting end phase");
                    RequestEndPhase();
                }
            }

            // InteractionEndClip到達検知（LoopRegionなしInteractの完了通知）
            if (_hasInteractionEnd && !_interactionEndFired && director != null)
            {
                bool isPlaying = director.state == UnityEngine.Playables.PlayState.Playing;

                if (isPlaying)
                {
                    _interactionEndDirectorWasPlaying = true;
                }

                if (director.time >= _interactionEndTime)
                {
                    _interactionEndFired = true;
                    OnInteractionEndReached?.Invoke();
                }
                // WebGLフォールバック: directorがPlaying→Pausedに遷移したがstoppedイベントが未発火の場合
                // WebGLビルドではWrapMode.NoneのTimeline終了時にdirector.timeが0にリセットされ
                // state=Pausedになるが、stopped イベントが発火しないケースがある
                else if (_interactionEndDirectorWasPlaying && !isPlaying)
                {
                    _interactionEndFired = true;
                    OnInteractionEndReached?.Invoke();
                }
            }

            if (director == null || !_hasLoopRegion) return;

            double currentTime = director.time;

            // CancelRegion到達チェック（ループ中・ed中どちらでも有効）
            if (_shouldCancelAtRegion && CanCancel)
            {
                FireCancelRegionReached();
                return;
            }

            // LoopStart到達の検出
            if (!_isInLoop && !_isEnding && currentTime >= _loopStartTime)
            {
                _isInLoop = true;
                Debug.Log($"[CharacterAnimationController] Loop entered at {currentTime:F3}");
                OnLoopEntered?.Invoke();

                // ループ開始前にRequestEndPhaseが呼ばれていた場合は即座に終了フェーズへ
                if (_shouldExitLoop)
                {
                    JumpToEndPhase();
                    return;
                }
            }

            // LoopEnd到達→LoopStartにジャンプ
            // 予測ベース: director.timeは前フレームの評価時刻のため、
            // Director評価フェーズで currentTime + deltaTime が loopEnd を超えると
            // ed clipとのブレンドが1フレーム発生する。
            // deltaTimeを加味して予測し、超過前にジャンプすることで防止。
            if (_isInLoop && !_shouldExitLoop)
            {
                double predictedTime = currentTime + Time.deltaTime;
                if (predictedTime >= _loopEndTime || currentTime >= _loopEndTime)
                {
                    _loopJumpOccurred = true;
                    director.time = _loopStartTime;
                    // ループバックによる再生位置の不連続を通知し、
                    // _prevCleanPoseを無効化して偽v₀の発生を防ぐ
                    inertialBlendHelper?.InvalidatePrevCleanPose();
                    Debug.Log($"[CharacterAnimationController] Loop back to {_loopStartTime:F3} (predicted={predictedTime:F3})");
                }
            }

            // 終了フェーズの完了検出
            if (_isEnding)
            {
                _endingFrameCount++;
                if (_endingFrameCount <= ENDING_GRACE_FRAMES) return;

                double assetDuration = director.playableAsset?.duration ?? 0;
                var timelineAsset = director.playableAsset as TimelineAsset;
                double frameRate = timelineAsset?.editorSettings.frameRate ?? DEFAULT_FRAME_RATE;
                // 2フレーム分のマージン: Update時のdirector.timeは前フレームのAnimator評価値のため、
                // 1フレームマージンではAnimatorが終端に到達した後のUpdateでしか検出できず、
                // Timeline終端境界でクリップ外のポーズが1F描画される（think_ed→talk_idle等で発生）。
                double frameMargin = 2.0 / frameRate;

                // Timeline終端に到達
                if (currentTime >= assetDuration - frameMargin)
                {
                    CompleteEndPhase();
                    return;
                }

                // 再生が停止した場合（Holdモードでの停止検出）
                bool isEditorPaused = false;
#if UNITY_EDITOR
                isEditorPaused = UnityEditor.EditorApplication.isPaused;
#endif
                bool isPlaying = director.state == UnityEngine.Playables.PlayState.Playing;
                if (!isPlaying && !isEditorPaused && currentTime >= _endStartTime)
                {
                    CompleteEndPhase();
                    return;
                }
            }
        }

        /// <summary>
        /// デバッグ表示：現在のTimeline情報
        /// </summary>
        [Header("Debug Display")]
        [Tooltip("Timeline情報をUnityUIに表示（デバッグ用）")]
        [SerializeField] private bool showTimelineDebug = false;

        [Tooltip("Timeline情報を表示するTextMeshProUGUIコンポーネント")]
        public TMP_Text timelineDebugText;

        /// <summary>
        /// Timeline Debug表示のON/OFFを外部から制御
        /// DebugSettingsPanelから呼ばれる
        /// </summary>
        public void SetTimelineDebugEnabled(bool enabled)
        {
            showTimelineDebug = enabled;
            if (timelineDebugText != null)
            {
                timelineDebugText.gameObject.SetActive(enabled);
            }
        }

        /// <summary>
        /// Timeline Debug表示の現在の状態を取得
        /// </summary>
        public bool IsTimelineDebugEnabled => showTimelineDebug;

        /// <summary>
        /// Timeline情報をUnityUIに更新
        /// </summary>
        private void UpdateTimelineDebugText()
        {
            if (!showTimelineDebug || timelineDebugText == null || director == null) return;

            var timeline = director.playableAsset;
            string timelineName = timeline != null ? timeline.name : "None";
            double currentTime = director.time;
            double duration = timeline != null ? timeline.duration : 0;

            // フレームレートを取得
            double frameRate = DEFAULT_FRAME_RATE;
            if (timeline is UnityEngine.Timeline.TimelineAsset timelineAsset)
            {
                frameRate = timelineAsset.editorSettings.frameRate;
            }

            int currentFrame = (int)(currentTime * frameRate);
            int totalFrames = (int)(duration * frameRate);

            // キャラクターの位置・回転情報
            string posInfo = "N/A";
            string rotInfo = "N/A";
            if (_characterTransform != null)
            {
                posInfo = $"({_characterTransform.position.x:F3}, {_characterTransform.position.y:F3}, {_characterTransform.position.z:F3})";
                rotInfo = $"({_characterTransform.rotation.eulerAngles.x:F1}, {_characterTransform.rotation.eulerAngles.y:F1}, {_characterTransform.rotation.eulerAngles.z:F1})";
            }

            string loopInfo = _hasLoopRegion
                ? $"Loop: {(_isInLoop ? "InLoop" : _isEnding ? "Ending" : "Pre")} exit={_shouldExitLoop}"
                : "Loop: None";

            string cancelInfo = _cancelRegions.Count > 0
                ? $"Cancel: {(CanCancel ? "OK" : "---")} regions={_cancelRegions.Count}"
                : "Cancel: None";

            string debugText = $"Timeline: {timelineName}\n" +
                               $"Frame: {currentFrame} / {totalFrames}\n" +
                               $"Time: {currentTime:F3} / {duration:F3}\n" +
                               $"State: {_currentState} ({_currentAnimationId})\n" +
                               $"PlayState: {director.state}\n" +
                               $"{loopInfo}\n" +
                               $"{cancelInfo}\n" +
                               $"Pos: {posInfo}\n" +
                               $"Rot: {rotInfo}\n" +
                               $"PreservePos: {_shouldPreservePosition}";

            timelineDebugText.text = debugText;
        }

        /// <summary>
        /// 指定ステートのTimelineを再生
        /// </summary>
        public void PlayState(AnimationStateType state, string animationVariant = null, bool resumeAtLoop = false, bool resumeAtEnd = false, bool skipBlend = false)
        {
            // Walk終了フェーズのクリーンアップ（別の状態遷移で中断された場合）
            if (_isWalkEndPhaseActive)
            {
                _isWalkEndPhaseActive = false;
                OnEndPhaseComplete -= OnWalkEndPhaseComplete;
                Debug.Log("[CharacterAnimationController] Walk end phase cancelled due to state change");
            }

            // Emote/Thinking以外への状態遷移時: 加算オーバーライドとThinking状態を暗黙的にキャンセル
            if (state != AnimationStateType.Emote && state != AnimationStateType.Thinking)
            {
                // 加算オーバーライドの停止
                if (additiveOverrideHelper != null && additiveOverrideHelper.IsActive)
                {
                    additiveOverrideHelper.StopOverride();
                }
                // Thinking状態のクリーンアップ（加算有無に関わらず必要）
                if (_isThinkingActive)
                {
                    _isThinkingActive = false;
                    OnEndPhaseComplete -= OnThinkingEndPhaseComplete;
                    Debug.Log("[CharacterAnimationController] Thinking force-stopped due to state change");
                }
            }

            if (director == null)
            {
                Debug.LogWarning("[CharacterAnimationController] PlayableDirector not assigned");
                return;
            }

            if (animator == null)
            {
                Debug.LogWarning("[CharacterAnimationController] Animator not assigned");
                return;
            }

            if (timelineBindings == null)
            {
                Debug.LogWarning("[CharacterAnimationController] TimelineBindings not assigned");
                return;
            }

            var previousState = _currentState;
            _currentState = state;
            _currentAnimationId = animationVariant ?? state.ToString();

            // Root Motion は常時有効（移動制御はアニメーション側のRootボーン有無で行う）
            if (animator != null) animator.applyRootMotion = true;

            // 位置保持設定（非移動ステートでは現在位置を維持）
            SetPositionPreservation(state);

            // Timelineアセットを取得
            // animationVariantがある場合はanimationIdBindingsから優先検索
            TimelineAsset timeline = null;
            if (!string.IsNullOrEmpty(animationVariant))
            {
                timeline = timelineBindings.GetTimelineByAnimationId(animationVariant);
            }
            if (timeline == null)
            {
                timeline = GetTimelineForState(state);
            }
            if (timeline == null)
            {
                Debug.LogWarning($"[CharacterAnimationController] Timeline not found for state: {state}");
                return;
            }

            // Timeline再生
            // Playing状態（Holdモード等）のままplayableAssetを変更すると
            // グラフ再構築が不完全になりバインディングが失われる場合があるため、
            // 明示的にStopしてからアセットを切り替える
            StopDirectorForAssetChange();
            director.playableAsset = timeline;

            // AnimationTrackにAnimatorをバインド
            BindAnimatorToTimeline(timeline);

            // LoopRegionTrack / ActionCancelTrack / InteractionEndTrack / InertialBlendTrackの読み取り
            SetupLoopRegion(timeline);
            SetupInteractionEnd(timeline);
            SetupCancelRegions(timeline);

            // skipBlend時はInertialBlend全体をスキップ（初回読み込み時のT-pose補間を回避）
            if (skipBlend)
            {
                Debug.Log("[CharacterAnimationController] Skipping InertialBlend (skipBlend=true)");
            }
            else
            {
                HasInertialBlendTrack(timeline);

                // Timeline切り替え時、古いIBが残っていればキャンセルする。
                // 新しいIBはdirector.Evaluate()→MixerBehaviour.ProcessFrameで
                // 再生位置にIBクリップがある場合にのみ自動開始される。
                if (inertialBlendHelper != null && inertialBlendHelper.IsActive)
                {
                    inertialBlendHelper.CancelBlend();
                }
            }

            // WrapMode設定
            // LoopRegionTrackがある場合はHold（クリップベースループ制御）
            // Timeline全体ループ（Idle）の場合はLoop
            if (_hasLoopRegion)
            {
                director.extrapolationMode = DirectorWrapMode.Hold;
            }
            else if (IsFullTimelineLoop(state))
            {
                director.extrapolationMode = DirectorWrapMode.Loop;
            }
            else
            {
                director.extrapolationMode = DirectorWrapMode.None;
            }

            // resumeAtEnd: LoopRegionがある場合は終了フェーズ（_ed）開始地点に直接ジャンプ
            // resumeAtLoop: LoopRegionがある場合はループ開始地点から再開（復帰時のst再生を回避）
            if (resumeAtEnd && _hasLoopRegion)
            {
                director.time = _endStartTime;
                _isEnding = true;
                _endingFrameCount = 0;
            }
            else if (resumeAtLoop && _hasLoopRegion)
            {
                director.time = _loopStartTime;
            }
            else
            {
                director.time = 0;
            }
            director.Play();

            // director.Play()後にEvaluate()で即時評価し、1Fポーズフラッシュを防止する。
            // コルーチン等からの呼び出し時、アニメーション評価サイクルが済んでいる場合に
            // 前のTimeline/ポーズが1F見える問題を解消する。
            director.Evaluate();

            string resumeInfo = resumeAtEnd && _hasLoopRegion ? $", ResumedAtEnd: {_endStartTime:F3}"
                              : resumeAtLoop && _hasLoopRegion ? $", ResumedAt: {_loopStartTime:F3}"
                              : "";
            Debug.Log($"[CharacterAnimationController] Playing: {state} ({_currentAnimationId}), Timeline: {timeline.name}, WrapMode: {director.extrapolationMode}, LoopRegion: {_hasLoopRegion}{resumeInfo}");
        }

        /// <summary>
        /// 現在のTimeline時刻でポーズを即時評価する。
        /// PlayState直後に呼ぶことで、補間なしで最初のフレームのポーズを即座に適用できる。
        /// VRM読み込み直後のT-pose回避に使用。
        /// </summary>
        public void EvaluateImmediate()
        {
            if (director == null) return;
            director.Evaluate();
        }

        /// <summary>
        /// TimelineのAnimationTrackにAnimatorをバインド
        /// </summary>
        private void BindAnimatorToTimeline(TimelineAsset timeline)
        {
            if (animator == null)
            {
                Debug.LogError("[CharacterAnimationController] Animator is null! Cannot bind to Timeline.");
                return;
            }

            int trackCount = 0;
            int inertialTrackCount = 0;
            bool hasExpressionTrack = false;
            foreach (var track in timeline.GetOutputTracks())
            {
                if (track is AnimationTrack animTrack)
                {
                    // Walk-Turn トラック切替: トラック名でバインド/アンバインドを決定
                    bool shouldBind = true;
                    if (animTrack.name.Contains("TurnL"))
                        shouldBind = (_turnMode == TurnMode.TurnLeft);
                    else if (animTrack.name.Contains("TurnR"))
                        shouldBind = (_turnMode == TurnMode.TurnRight);
                    else if (animTrack.name.EndsWith("_ST"))
                        shouldBind = (_turnMode == TurnMode.Normal);

                    director.SetGenericBinding(animTrack, shouldBind ? animator : null);
                    Debug.Log($"[CharacterAnimationController] Bound Animator '{animator.name}' to track: {animTrack.name}{(shouldBind ? "" : " (unbound)")}");
                    trackCount++;
                }
                else if (track is InertialBlendTrack inertialTrack)
                {
                    director.SetGenericBinding(inertialTrack, animator);
                    Debug.Log($"[CharacterAnimationController] Bound Animator '{animator.name}' to InertialBlendTrack: {inertialTrack.name}");
                    inertialTrackCount++;
                }
                else if (track is LookAtTrack lookAtTrack)
                {
                    if (lookAtController != null)
                    {
                        director.SetGenericBinding(lookAtTrack, lookAtController);
                        Debug.Log($"[CharacterAnimationController] Bound LookAtController to LookAtTrack: {lookAtTrack.name}");
                    }
                }
                else if (track is VrmExpressionTrack vrmExpressionTrack)
                {
                    if (expressionController != null)
                    {
                        director.SetGenericBinding(vrmExpressionTrack, expressionController);
                        Debug.Log($"[CharacterAnimationController] Bound ExpressionController to VrmExpressionTrack: {vrmExpressionTrack.name}");
                    }
                    hasExpressionTrack = true;
                }
                else if (track is MoveSpeedTrack moveSpeedTrack)
                {
                    if (navigationController != null)
                    {
                        director.SetGenericBinding(moveSpeedTrack, navigationController);
                        Debug.Log($"[CharacterAnimationController] Bound NavigationController to MoveSpeedTrack: {moveSpeedTrack.name}");
                    }
                }
                else if (track is LightControlTrack lightControlTrack)
                {
                    if (roomLightController != null)
                    {
                        director.SetGenericBinding(lightControlTrack, roomLightController);
                        Debug.Log($"[CharacterAnimationController] Bound RoomLightController to LightControlTrack: {lightControlTrack.name}");
                    }
                }
            }

            // Body TimelineにVrmExpressionTrackがある場合、Facial Directorを一時停止
            expressionController?.SetBodyExpressionOverride(hasExpressionTrack);

            // TurnModeをリセット（次回のPlayStateではNormalに戻る）
            _turnMode = TurnMode.Normal;

            if (trackCount == 0)
            {
                Debug.LogWarning($"[CharacterAnimationController] No AnimationTrack found in Timeline: {timeline.name}");
            }
            if (inertialTrackCount > 0)
            {
                Debug.Log($"[CharacterAnimationController] Bound {inertialTrackCount} InertialBlendTrack(s)");
            }
        }

        /// <summary>
        /// 簡易的なアニメーション再生
        /// animationIdからステートを自動推定してPlayStateに委譲する
        /// </summary>
        public void PlayAnimation(string animationId)
        {
            AnimationStateType state = ParseStateFromAnimationId(animationId);
            PlayState(state, animationId);
        }

        /// <summary>
        /// 指定したTimelineを直接再生
        /// </summary>
        public void PlayTimeline(TimelineAsset timeline, string animationId, bool loop = false)
        {
            // Walk終了フェーズのクリーンアップ（Emote等で中断された場合）
            if (_isWalkEndPhaseActive)
            {
                _isWalkEndPhaseActive = false;
                OnEndPhaseComplete -= OnWalkEndPhaseComplete;
                Debug.Log("[CharacterAnimationController] Walk end phase cancelled due to timeline change");
            }

            if (director == null || timeline == null)
            {
                Debug.LogWarning("[CharacterAnimationController] Director or Timeline is null");
                return;
            }

            if (animator == null)
            {
                Debug.LogWarning("[CharacterAnimationController] Animator not assigned");
                return;
            }

            _currentState = ParseStateFromAnimationId(animationId);
            _currentAnimationId = animationId;

            // Root Motion は常時有効（移動制御はアニメーション側のRootボーン有無で行う）
            if (animator != null) animator.applyRootMotion = true;

            // 位置保持設定（非移動ステートでは現在位置を維持）
            SetPositionPreservation(_currentState);

            // Timeline再生
            StopDirectorForAssetChange();
            director.playableAsset = timeline;

            // AnimationTrackにAnimatorをバインド
            BindAnimatorToTimeline(timeline);

            // LoopRegionTrack / ActionCancelTrack / InteractionEndTrack / InertialBlendTrackの読み取り
            SetupLoopRegion(timeline);
            SetupInteractionEnd(timeline);
            SetupCancelRegions(timeline);
            HasInertialBlendTrack(timeline);
            if (inertialBlendHelper != null && inertialBlendHelper.IsActive)
            {
                inertialBlendHelper.CancelBlend();
            }

            // WrapMode設定
            // LoopRegionTrackがある場合はHold（クリップベースループ制御が優先）
            if (_hasLoopRegion)
            {
                director.extrapolationMode = DirectorWrapMode.Hold;
            }
            else
            {
                director.extrapolationMode = loop
                    ? DirectorWrapMode.Loop
                    : DirectorWrapMode.None;
            }

            director.time = 0;
            director.Play();
            director.Evaluate();

            Debug.Log($"[CharacterAnimationController] PlayTimeline: {timeline.name}, AnimId: {animationId}, LoopRegion: {_hasLoopRegion}, WrapMode: {director.extrapolationMode}");
        }

        /// <summary>
        /// Idleに戻る
        /// </summary>
        public void ReturnToIdle()
        {
            PlayState(AnimationStateType.Idle);
        }

        /// <summary>
        /// 歩行開始
        /// </summary>
        public void StartWalk()
        {
            PlayState(AnimationStateType.Walk);
        }

        /// <summary>
        /// 歩行停止（Idleへ）
        /// </summary>
        public void StopWalk()
        {
            ReturnToIdle();
        }

        /// <summary>
        /// Walk/Runを停止。LoopRegionがある場合はed区間を再生してからIdleに遷移。
        /// LoopRegionがない場合は即座にIdleに遷移。
        /// </summary>
        public void StopWalkWithEndPhase()
        {
            if (_hasLoopRegion)
            {
                _isWalkEndPhaseActive = true;
                OnEndPhaseComplete += OnWalkEndPhaseComplete;
                RequestEndPhase();
                Debug.Log("[CharacterAnimationController] StopWalkWithEndPhase: playing end phase");
            }
            else
            {
                ReturnToIdle();
            }
        }

        private void OnWalkEndPhaseComplete()
        {
            OnEndPhaseComplete -= OnWalkEndPhaseComplete;
            _isWalkEndPhaseActive = false;
            Debug.Log("[CharacterAnimationController] Walk end phase complete, returning to Idle");
            ReturnToIdle();
        }

        /// <summary>
        /// 会話モーション再生
        /// </summary>
        public void PlayTalk(string variant = null)
        {
            PlayState(AnimationStateType.Talk, variant);
        }

        /// <summary>
        /// エモート再生
        /// </summary>
        public void PlayEmote(string emoteType)
        {
            PlayState(AnimationStateType.Emote, emoteType);
        }

        // --- Emote再生可能判定 & 自動復帰 ---

        private AnimationStateType _emoteReturnState;
        private string _emoteReturnAnimationId;

        /// <summary>
        /// 現在のTimelineでエモート再生が可能かチェック
        /// EmotePlayableTrackのClipが現在時刻にアクティブであれば再生可能
        /// </summary>
        public bool CanPlayEmote()
        {
            return GetActiveEmoteClip() != null;
        }

        /// <summary>
        /// 現在アクティブなPlayableClipを汎用的に取得
        /// 指定トラック型のクリップのうち、現在時刻にアクティブなものを返す
        /// </summary>
        private TClip GetActivePlayableClip<TTrack, TClip>()
            where TTrack : TrackAsset
            where TClip : PlayableAsset
        {
            if (director == null || director.playableAsset == null) return null;

            var timeline = director.playableAsset as TimelineAsset;
            if (timeline == null) return null;

            double currentTime = director.time;

            foreach (var track in timeline.GetOutputTracks())
            {
                if (track is TTrack)
                {
                    foreach (var clip in track.GetClips())
                    {
                        if (currentTime >= clip.start && currentTime <= clip.end)
                        {
                            return clip.asset as TClip;
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 現在アクティブなEmotePlayableClipを取得（additiveBones情報用）
        /// </summary>
        private EmotePlayableClip GetActiveEmoteClip()
        {
            return GetActivePlayableClip<EmotePlayableTrack, EmotePlayableClip>();
        }

        /// <summary>
        /// AdditiveOverrideを安全に停止する。
        /// 停止前にIBの_lastCleanPoseをAO補正込みポーズで上書きし、
        /// 次回IBでポーズフラッシュが発生するのを防ぐ（DESIGN.md 問題9参照）。
        /// </summary>
        private void StopAdditiveOverrideWithSnapshot()
        {
            if (additiveOverrideHelper != null && additiveOverrideHelper.IsActive)
            {
                inertialBlendHelper?.SnapshotCurrentPoseAsClean();
            }
            additiveOverrideHelper?.StopOverride();
        }

        /// <summary>
        /// エモートを再生し、完了後に現在のステートに自動復帰
        /// additiveBones が設定されている場合は加算ボーンオーバーライドを使用
        /// </summary>
        public void PlayEmoteWithReturn(string emoteType)
        {
            // Emoteホールドタイマーリセット
            _emoteHoldTimer = 0f;

            // 復帰先を保存
            _emoteReturnState = _currentState;
            _emoteReturnAnimationId = _currentAnimationId;

            // 加算ボーン判定
            var emoteClip = GetActiveEmoteClip();
            var additiveBones = emoteClip?.additiveBones;
            if (additiveBones != null && additiveBones.Count > 0 && additiveOverrideHelper != null)
            {
                additiveOverrideHelper.StartOverride(additiveBones);
            }

            // emote再生
            PlayState(AnimationStateType.Emote, emoteType);

            // Timeline完了時に復帰するイベントを登録
            // LoopRegionありのEmoteはCompleteEndPhase → OnEndPhaseCompleteで終了する
            // （WrapMode.HoldのためPlayableDirector.stoppedは発火しない）
            // LoopRegionなしのEmoteはdirector.stoppedで終了する
            if (_hasLoopRegion)
            {
                OnEndPhaseComplete += OnEmoteEndPhaseComplete;
            }
            else
            {
                director.stopped += OnEmoteTimelineStopped;
            }
        }

        private void OnEmoteTimelineStopped(PlayableDirector stoppedDirector)
        {
            if (stoppedDirector != director) return;

            // イベントを解除
            director.stopped -= OnEmoteTimelineStopped;

            StopAdditiveOverrideWithSnapshot();

            // 復帰先ステートに戻る（LoopRegionがある場合はループ開始地点から再開）
            if (_currentState == AnimationStateType.Emote)
            {
                Debug.Log($"[CharacterAnimationController] Emote completed (stopped), returning to: {_emoteReturnState} ({_emoteReturnAnimationId})");
                PlayState(_emoteReturnState, _emoteReturnAnimationId, resumeAtLoop: true);
            }
        }

        /// <summary>
        /// LoopRegionありEmoteの終了フェーズ完了時コールバック
        /// CompleteEndPhase → OnEndPhaseComplete 経由で呼ばれる
        /// </summary>
        private void OnEmoteEndPhaseComplete()
        {
            OnEndPhaseComplete -= OnEmoteEndPhaseComplete;

            StopAdditiveOverrideWithSnapshot();

            // 復帰先ステートに戻る（LoopRegionがある場合はループ開始地点から再開）
            if (_currentState == AnimationStateType.Emote)
            {
                Debug.Log($"[CharacterAnimationController] Emote completed (endPhase), returning to: {_emoteReturnState} ({_emoteReturnAnimationId})");
                PlayState(_emoteReturnState, _emoteReturnAnimationId, resumeAtLoop: true);
            }
        }

        /// <summary>
        /// Emoteを即座に停止して復帰先に戻る（終了アニメーションなし）
        /// InteractionのExitLoop等、別の状態遷移が優先される場合に使用
        /// </summary>
        public void ForceStopEmote()
        {
            if (_currentState != AnimationStateType.Emote) return;

            Debug.Log("[CharacterAnimationController] ForceStopEmote");

            director.stopped -= OnEmoteTimelineStopped;
            OnEndPhaseComplete -= OnEmoteEndPhaseComplete;

            StopAdditiveOverrideWithSnapshot();

            // Emoteの_ed遷移が進行中の場合、resumeAtLoopでループに戻すと
            // 後続のJumpToEndPhaseと合わせて2段階IBでポーズが飛ぶ。
            // resumeAtEndで復帰先の_edに直接ジャンプする。
            if (_isEnding)
            {
                Debug.Log("[CharacterAnimationController] ForceStopEmote during end phase → resumeAtEnd");
                PlayState(_emoteReturnState, _emoteReturnAnimationId, resumeAtEnd: true);
                return;
            }

            // 復帰先ステートに戻る（LoopRegionがある場合はループ開始地点から再開）
            PlayState(_emoteReturnState, _emoteReturnAnimationId, resumeAtLoop: true);
        }

        /// <summary>
        /// Emoteを即座に停止し、復帰先Timelineの終了フェーズ（_ed）に直接ジャンプ。
        /// ExitLoopWithCallback用: ForceStopEmote(resumeAtLoop)→RequestEndPhase(JumpToEndPhase)の
        /// 2段階IB問題を回避するため、_ed開始位置に直接ジャンプして1回のIBで遷移する。
        /// </summary>
        public void ForceStopEmoteToEnd()
        {
            if (_currentState != AnimationStateType.Emote) return;

            Debug.Log("[CharacterAnimationController] ForceStopEmoteToEnd");

            director.stopped -= OnEmoteTimelineStopped;
            OnEndPhaseComplete -= OnEmoteEndPhaseComplete;

            StopAdditiveOverrideWithSnapshot();

            // 復帰先ステートの終了フェーズに直接ジャンプ（ループ再開を経由しない）
            PlayState(_emoteReturnState, _emoteReturnAnimationId, resumeAtEnd: true);
        }

        // --- テキスト表示完了通知 ---

        /// <summary>
        /// チャット応答開始を通知
        /// テキスト表示完了までemoteホールドタイマーを停止する
        /// </summary>
        public void OnResponseStarted()
        {
            _emoteTextDisplayComplete = false;
            _emoteHoldTimer = 0f;
        }

        /// <summary>
        /// テキスト表示完了を通知
        /// この時点からemoteHoldDurationのカウントダウンを開始する
        /// </summary>
        public void NotifyTextDisplayComplete()
        {
            _emoteTextDisplayComplete = true;
            _emoteHoldTimer = 0f;
        }

        // --- Thinking再生可能判定 & 自動復帰 ---

        private AnimationStateType _thinkingReturnState;
        private string _thinkingReturnAnimationId;
        private bool _isThinkingActive;
        public bool IsThinkingActive => _isThinkingActive;

        /// <summary>
        /// 現在のTimelineでThinking再生が可能かチェック
        /// ThinkingPlayableTrackのClipが現在時刻にアクティブであれば再生可能
        /// EmoteのTimelineにもThinkingPlayableClipを配置することでEmote中も判定可能
        /// </summary>
        public bool CanPlayThinking()
        {
            return GetActiveThinkingClip() != null;
        }

        /// <summary>
        /// 現在アクティブなThinkingPlayableClipを取得（additiveBones情報用）
        /// </summary>
        private ThinkingPlayableClip GetActiveThinkingClip()
        {
            return GetActivePlayableClip<ThinkingPlayableTrack, ThinkingPlayableClip>();
        }

        /// <summary>
        /// Thinkingアニメーションを再生（加算ボーンオーバーライド対応）
        /// LoopRegionでループし、StopThinkingAndReturn()で終了フェーズに遷移
        /// </summary>
        public void PlayThinkingWithReturn(string thinkingAnimId)
        {
            if (_isThinkingActive)
            {
                Debug.Log("[CharacterAnimationController] Already thinking, ignoring");
                return;
            }

            // Emote再生中の場合: Emoteの復帰先をThinkingの復帰先として引き継ぐ
            // （EmoteのTimelineにThinkingPlayableClipを配置することでEmote中もCanPlayThinking=true）
            if (_currentState == AnimationStateType.Emote)
            {
                Debug.Log("[CharacterAnimationController] Cancelling emote for thinking");
                director.stopped -= OnEmoteTimelineStopped;
                additiveOverrideHelper?.StopOverride();
                _thinkingReturnState = _emoteReturnState;
                _thinkingReturnAnimationId = _emoteReturnAnimationId;
            }
            else
            {
                _thinkingReturnState = _currentState;
                _thinkingReturnAnimationId = _currentAnimationId;
            }

            // 加算ボーン判定（現在のTimelineからThinkingClipを検索）
            var thinkingClip = GetActiveThinkingClip();
            var additiveBones = thinkingClip?.additiveBones;
            if (additiveBones != null && additiveBones.Count > 0 && additiveOverrideHelper != null)
            {
                additiveOverrideHelper.StartOverride(additiveBones);
            }

            _isThinkingActive = true;

            // Thinking Timeline を再生
            PlayState(AnimationStateType.Thinking, thinkingAnimId);

            // LoopRegion 終了時の復帰イベントを登録
            OnEndPhaseComplete += OnThinkingEndPhaseComplete;

            Debug.Log($"[CharacterAnimationController] Thinking started: {thinkingAnimId}, additive={additiveBones?.Count ?? 0}");
        }

        /// <summary>
        /// Thinkingを停止して復帰先に戻る
        /// LoopRegionがある場合はRequestEndPhaseで終了フェーズ経由
        /// </summary>
        public void StopThinkingAndReturn()
        {
            if (!_isThinkingActive) return;

            Debug.Log("[CharacterAnimationController] StopThinkingAndReturn");

            // 現在の状態がThinkingでない場合は安全に即時復帰（別のTimelineのendを発動させない）
            if (_currentState != AnimationStateType.Thinking)
            {
                Debug.LogWarning("[CharacterAnimationController] StopThinkingAndReturn called but current state is not Thinking, forcing immediate cleanup");
                OnEndPhaseComplete -= OnThinkingEndPhaseComplete;
                additiveOverrideHelper?.StopOverride();
                _isThinkingActive = false;
                return;
            }

            if (_hasLoopRegion)
            {
                // LoopRegion のed区間を再生してから復帰
                RequestEndPhase();
                // OnThinkingEndPhaseComplete が復帰を処理
            }
            else
            {
                // LoopRegionなし → 即時復帰
                OnThinkingEndPhaseComplete();
            }
        }

        /// <summary>
        /// Thinkingを即座に停止して復帰先に戻る（終了アニメーションなし）
        /// ExitTalk等、別の状態遷移が優先される場合に使用
        /// </summary>
        public void ForceStopThinking()
        {
            if (!_isThinkingActive) return;

            Debug.Log("[CharacterAnimationController] ForceStopThinking");

            OnEndPhaseComplete -= OnThinkingEndPhaseComplete;

            StopAdditiveOverrideWithSnapshot();
            _isThinkingActive = false;

            // StopThinkingAndReturn(graceful)がJumpToEndPhaseで_ed遷移を開始済みの場合、
            // resumeAtLoopでループ開始に戻すと_ed遷移がキャンセルされ、
            // 後続のJumpToEndPhaseと合わせて2段階IBでポーズが飛ぶ。
            // resumeAtEndで復帰先の_edに直接ジャンプし、不要なloop再開を回避する。
            //
            // ただしInteract復帰時はresumeAtEndを使わない:
            // interact_edは「立ち上がり」アニメーションであり、InteractionControllerの
            // ExitLoopWithCallbackを経由せずにedが再生されると、OnEndPhaseCompleteの
            // ハンドラが状態ガード(_currentState != Ending)で弾かれ、
            // Idle復帰もpendingAction実行も行われずモーションが停止する。
            if (_isEnding)
            {
                // Interact復帰時はresumeAtEndを使わない:
                // interact_edは「立ち上がり」アニメーションであり、InteractionControllerの
                // ExitLoopWithCallbackを経由せずにedが再生されると、OnEndPhaseCompleteの
                // ハンドラが状態ガード(_currentState != Ending)で弾かれ、
                // Idle復帰もpendingAction実行も行われずモーションが停止する。
                //
                // Talk復帰時もresumeAtEndを使わない:
                // talk_idle等のループ待機アニメーションでresumeAtEndすると、
                // endStart位置がloopEnd以降のためループに入れず即終了してしまう。
                if (_thinkingReturnState == AnimationStateType.Interact
                    || _thinkingReturnState == AnimationStateType.Talk)
                {
                    Debug.Log($"[CharacterAnimationController] ForceStopThinking during end phase ({_thinkingReturnState}) → resumeAtLoop");
                    PlayState(_thinkingReturnState, _thinkingReturnAnimationId, resumeAtLoop: true);
                    return;
                }

                Debug.Log("[CharacterAnimationController] ForceStopThinking during end phase → resumeAtEnd");
                PlayState(_thinkingReturnState, _thinkingReturnAnimationId, resumeAtEnd: true);
                return;
            }

            // 復帰先ステートに戻る（LoopRegionがある場合はループ開始地点から再開）
            PlayState(_thinkingReturnState, _thinkingReturnAnimationId, resumeAtLoop: true);
        }

        /// <summary>
        /// Thinkingを即座に停止し、復帰先Timelineの終了フェーズ（_ed）に直接ジャンプ。
        /// ExitLoopWithCallback用: ForceStopThinking(resumeAtLoop)→RequestEndPhase(JumpToEndPhase)の
        /// 2段階IB問題を回避するため、_ed開始位置に直接ジャンプして1回のIBで遷移する。
        /// </summary>
        public void ForceStopThinkingToEnd()
        {
            if (!_isThinkingActive) return;

            Debug.Log("[CharacterAnimationController] ForceStopThinkingToEnd");

            OnEndPhaseComplete -= OnThinkingEndPhaseComplete;

            StopAdditiveOverrideWithSnapshot();
            _isThinkingActive = false;

            // 復帰先ステートの終了フェーズに直接ジャンプ（ループ再開を経由しない）
            PlayState(_thinkingReturnState, _thinkingReturnAnimationId, resumeAtEnd: true);
        }

        private void OnThinkingEndPhaseComplete()
        {
            OnEndPhaseComplete -= OnThinkingEndPhaseComplete;

            StopAdditiveOverrideWithSnapshot();

            _isThinkingActive = false;

            // 復帰先ステートに戻る（LoopRegionがある場合はループ開始地点から再開）
            if (_currentState == AnimationStateType.Thinking)
            {
                Debug.Log($"[CharacterAnimationController] Thinking completed, returning to: {_thinkingReturnState} ({_thinkingReturnAnimationId})");
                PlayState(_thinkingReturnState, _thinkingReturnAnimationId, resumeAtLoop: true);
            }
        }

        /// <summary>
        /// 現在のTimeline再生を停止
        /// </summary>
        public void Stop()
        {
            director?.Stop();
        }

        /// <summary>
        /// Timeline再生を一時停止
        /// </summary>
        public void Pause()
        {
            director?.Pause();
        }

        /// <summary>
        /// Timeline再生を再開
        /// </summary>
        public void Resume()
        {
            director?.Resume();
        }

        /// <summary>
        /// 現在のアニメーション再生が完了しているか
        /// </summary>
        public bool IsAnimationComplete()
        {
            if (director == null) return true;
            return director.state != UnityEngine.Playables.PlayState.Playing;
        }

        /// <summary>
        /// playableAsset変更前にDirectorを安全に停止する。
        /// Playing状態のままアセットを切り替えるとグラフ再構築が不完全になり、
        /// AnimationTrackのバインディングが失われる場合があるため、
        /// 明示的にStopしてクリーンな状態で新しいアセットに切り替える。
        ///
        /// Stop()がOnTimelineStoppedを発火するため、_isEndingによる
        /// CompleteEndPhaseの誤発火を防ぐためにResetLoopRegionで事前にリセットする。
        /// </summary>
        private void StopDirectorForAssetChange()
        {
            if (director == null) return;
            if (director.state != UnityEngine.Playables.PlayState.Playing) return;

            // Stop()がOnTimelineStoppedを発火し、_isEnding==trueだとCompleteEndPhaseが走るため、
            // 事前にリセットして副作用を防ぐ
            ResetLoopRegion();
            ResetInteractionEnd();

            director.Stop();
        }

        // --- LoopRegion制御メソッド ---

        /// <summary>
        /// TimelineからLoopRegionTrackのクリップ情報を読み取り、ループ領域をセットアップ
        /// </summary>
        private void SetupLoopRegion(TimelineAsset timeline)
        {
            ResetLoopRegion();

            if (timeline == null) return;

            foreach (var track in timeline.GetOutputTracks())
            {
                if (track is LoopRegionTrack)
                {
                    foreach (var clip in track.GetClips())
                    {
                        var loopClip = clip.asset as LoopRegionClip;
                        if (loopClip == null) continue;

                        double frameRate = timeline.editorSettings.frameRate;
                        double frameOffset = loopClip.loopEndOffsetFrames / frameRate;

                        _loopStartTime = clip.start;
                        _loopEndTime = clip.end + frameOffset; // デフォルト: clip.end - 1F
                        _endStartTime = clip.end;
                        _hasLoopRegion = true;

                        Debug.Log($"[CharacterAnimationController] LoopRegion setup: start={_loopStartTime:F3}, loopEnd={_loopEndTime:F3}, endStart={_endStartTime:F3}");
                        return; // 最初のLoopRegionClipのみ使用
                    }
                }
            }
        }

        /// <summary>
        /// TimelineからInteractionEndTrackのクリップ情報を読み取り、完了タイミングをセットアップ
        /// </summary>
        private void SetupInteractionEnd(TimelineAsset timeline)
        {
            ResetInteractionEnd();

            if (timeline == null) return;

            foreach (var track in timeline.GetOutputTracks())
            {
                if (track is InteractionEndTrack)
                {
                    foreach (var clip in track.GetClips())
                    {
                        if (clip.asset is InteractionEndClip)
                        {
                            _interactionEndTime = clip.start;
                            _hasInteractionEnd = true;
                            return; // 最初のInteractionEndClipのみ使用
                        }
                    }
                }
            }
        }

        /// <summary>
        /// InteractionEnd状態をリセット
        /// </summary>
        private void ResetInteractionEnd()
        {
            _hasInteractionEnd = false;
            _interactionEndTime = 0;
            _interactionEndFired = false;
            _interactionEndDirectorWasPlaying = false;
        }

        /// <summary>
        /// LoopRegion状態をリセット
        /// </summary>
        private void ResetLoopRegion()
        {
            _hasLoopRegion = false;
            _loopStartTime = 0;
            _loopEndTime = 0;
            _endStartTime = 0;
            _isInLoop = false;
            _shouldExitLoop = false;
            _shouldCancelAtRegion = false;
            _isEnding = false;
            _endingFrameCount = 0;
            _loopJumpOccurred = false;
        }

        /// <summary>
        /// CancelRegion到達時にキャンセルをリクエスト。
        /// 既にCancelRegion内にいる場合は即座に発火。
        /// </summary>
        public void RequestCancelAtRegion()
        {
            _shouldCancelAtRegion = true;
            Debug.Log($"[CharacterAnimationController] RequestCancelAtRegion (CanCancel={CanCancel})");

            if (CanCancel)
            {
                FireCancelRegionReached();
            }
        }

        private void FireCancelRegionReached()
        {
            _shouldCancelAtRegion = false;
            // ed再生中にStop()が呼ばれた場合のCompleteEndPhase二重発火を防止
            // （StopDirectorForAssetChangeと同じパターン）
            _isEnding = false;
            Debug.Log("[CharacterAnimationController] CancelRegion reached, firing event");
            OnCancelRegionReached?.Invoke();
        }

        /// <summary>
        /// 終了フェーズへの遷移をリクエスト。
        /// ループ中であればLoopEnd到達時にEndStartへジャンプ。
        /// ループ前（st再生中）であればLoopStart到達後に即座にEndStartへジャンプ。
        /// </summary>
        public void RequestEndPhase()
        {
            if (!_hasLoopRegion)
            {
                Debug.LogWarning("[CharacterAnimationController] RequestEndPhase called but no LoopRegion");
                return;
            }

            _shouldExitLoop = true;
            Debug.Log($"[CharacterAnimationController] RequestEndPhase (isInLoop={_isInLoop})");

            // すでにループ中なら即座に終了フェーズへ
            if (_isInLoop)
            {
                JumpToEndPhase();
            }
            // ループ前なら、LoopStart到達時にUpdate内で自動的にJumpToEndPhaseが呼ばれる
        }

        /// <summary>
        /// EndStart位置へジャンプして終了フェーズ開始
        /// </summary>
        private void JumpToEndPhase()
        {
            _isInLoop = false;
            _isEnding = true;
            _endingFrameCount = 0;

            if (director != null)
            {
                director.extrapolationMode = DirectorWrapMode.Hold;
                director.time = _endStartTime;

                // ジャンプ後も再生を継続
                if (director.state != UnityEngine.Playables.PlayState.Playing)
                {
                    director.Play();
                }

                // director.timeの変更は次のアニメーション評価サイクルまで反映されないため、
                // コルーチンなどアニメーション評価後に呼ばれた場合に1Fだけ前のポーズが見える。
                // Evaluate()で即時反映させて1Fポーズフラッシュを防止する。
                director.Evaluate();

                Debug.Log($"[CharacterAnimationController] Jumped to end phase at {_endStartTime:F3}");
            }
        }

        /// <summary>
        /// 終了フェーズ完了時の処理
        /// </summary>
        private void CompleteEndPhase()
        {
            Debug.Log("[CharacterAnimationController] End phase complete");
            ResetLoopRegion();
            OnEndPhaseComplete?.Invoke();
        }

        // --- CancelRegion制御メソッド ---

        /// <summary>
        /// TimelineからActionCancelTrackのクリップ情報を読み取り、キャンセル可能区間をセットアップ
        /// </summary>
        private void SetupCancelRegions(TimelineAsset timeline)
        {
            _cancelRegions.Clear();

            if (timeline == null) return;

            foreach (var track in timeline.GetOutputTracks())
            {
                if (track is ActionCancelTrack)
                {
                    foreach (var clip in track.GetClips())
                    {
                        var cancelClip = clip.asset as ActionCancelClip;
                        if (cancelClip == null) continue;

                        _cancelRegions.Add(new CancelRegion
                        {
                            startTime = clip.start,
                            endTime = clip.end,
                            allowedTransitions = cancelClip.allowedTransitions
                        });

                        Debug.Log($"[CharacterAnimationController] CancelRegion setup: {clip.start:F3}-{clip.end:F3}, transitions: {cancelClip.allowedTransitions.Count}");
                    }
                }
            }
        }

        // --- InertialBlend制御メソッド ---

        /// <summary>
        /// TimelineにInertialBlendTrackが含まれるかを返す。
        /// IBの実際の開始はInertialBlendMixerBehaviour.ProcessFrame（director.Evaluate()経由）が行う。
        /// これにより、再生開始位置にIBクリップがない場合（resumeAtLoop等）に
        /// 不要なIBが開始されることを防ぐ。
        /// </summary>
        private bool HasInertialBlendTrack(TimelineAsset timeline)
        {
            if (inertialBlendHelper == null) return false;
            if (timeline == null) return false;

            foreach (var track in timeline.GetOutputTracks())
            {
                if (track is InertialBlendTrack)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 現在のキャンセル可能区間で許可されている遷移先Timelineリストを取得
        /// </summary>
        public List<TimelineAsset> GetAllowedTransitions()
        {
            if (director == null) return new List<TimelineAsset>();

            double currentTime = director.time;
            foreach (var region in _cancelRegions)
            {
                if (currentTime >= region.startTime && currentTime <= region.endTime)
                {
                    return region.allowedTransitions;
                }
            }

            return new List<TimelineAsset>();
        }

        /// <summary>
        /// キャンセルを実行し、指定した遷移先Timelineに切り替える。
        /// CanCancelがtrueで、targetTimelineが許可リストに含まれる場合のみ実行。
        /// </summary>
        public bool ExecuteCancel(TimelineAsset targetTimeline)
        {
            if (!CanCancel)
            {
                Debug.LogWarning("[CharacterAnimationController] ExecuteCancel failed: not in cancel region");
                return false;
            }

            var allowed = GetAllowedTransitions();
            if (!allowed.Contains(targetTimeline))
            {
                Debug.LogWarning($"[CharacterAnimationController] ExecuteCancel failed: {targetTimeline.name} not in allowed transitions");
                return false;
            }

            Debug.Log($"[CharacterAnimationController] ExecuteCancel: → {targetTimeline.name}");

            // 現在のTimeline停止
            director.Stop();

            // 状態リセット
            ResetLoopRegion();
            _cancelRegions.Clear();

            // イベント発火
            OnCancelExecuted?.Invoke(targetTimeline);

            // 遷移先Timelineを再生
            string animId = targetTimeline.name;
            PlayTimeline(targetTimeline, animId);

            return true;
        }

        private TimelineAsset GetTimelineForState(AnimationStateType state)
        {
            if (timelineBindings == null) return null;
            return timelineBindings.GetTimeline(state);
        }

        /// <summary>
        /// アニメーションIDに対応するTimelineを取得
        /// </summary>
        public TimelineAsset GetTimelineForAnimation(string animationId)
        {
            if (timelineBindings == null) return null;

            // まずアニメーションIDから直接取得を試みる
            var timeline = timelineBindings.GetTimelineByAnimationId(animationId);
            if (timeline != null) return timeline;

            // 見つからない場合はステートから取得
            var state = ParseStateFromAnimationId(animationId);
            return timelineBindings.GetTimeline(state);
        }

        private AnimationStateType ParseStateFromAnimationId(string animationId)
        {
            if (string.IsNullOrEmpty(animationId)) return AnimationStateType.Idle;

            // interactを先に判定（idle等を含む可能性があるため）
            if (animationId.Contains("interact")) return AnimationStateType.Interact;
            if (animationId.Contains("idle")) return AnimationStateType.Idle;
            if (animationId.Contains("walk")) return AnimationStateType.Walk;
            if (animationId.Contains("run")) return AnimationStateType.Run;
            if (animationId.Contains("thinking")) return AnimationStateType.Thinking;
            if (animationId.Contains("talk")) return AnimationStateType.Talk;
            if (animationId.Contains("emote")) return AnimationStateType.Emote;

            return AnimationStateType.Idle;
        }

        private void OnTimelineStopped(PlayableDirector stoppedDirector)
        {
            Debug.Log($"[CharacterAnimationController] OnTimelineStopped: state={_currentState}, animId={_currentAnimationId}, hasInteractionEnd={_hasInteractionEnd}, interactionEndFired={_interactionEndFired}, isEnding={_isEnding}, director.time={stoppedDirector.time:F3}");

            // LoopRegion終了フェーズ中にTimelineが停止した場合
            if (_isEnding)
            {
                CompleteEndPhase();
                return;
            }

            // InteractionEndが未発火のままTimelineが停止した場合のフォールバック
            // WebGLビルドではフレームタイミングの差異により、Update()のtime>=チェックより先に
            // Directorが停止するケースがある（エディタでは再現しにくい）
            if (_hasInteractionEnd && !_interactionEndFired)
            {
                _interactionEndFired = true;
                OnInteractionEndReached?.Invoke();
                return;
            }

            OnAnimationComplete?.Invoke(_currentState);

            // Timeline全体ループ（Idle）の場合
            // DirectorWrapMode.Loopを設定しているので、通常はここに来ないはず
            // 来た場合でも、時間をリセットせずにそのまま再開
            // （時間リセットするとRoot Motionが原点に戻る問題があるため）
            if (IsFullTimelineLoop(_currentState) && stoppedDirector.extrapolationMode == DirectorWrapMode.Loop)
            {
                stoppedDirector.Play();
            }
        }

        /// <summary>
        /// Timeline全体をWrapMode.Loopでループするステートか。
        /// Walk/RunはLoopRegionClipによるSignal-basedループに移行したため含まない。
        /// </summary>
        private bool IsFullTimelineLoop(AnimationStateType state)
        {
            return state == AnimationStateType.Idle;
        }

        /// <summary>
        /// 非移動ステートでは現在位置を保持する設定
        /// TimelineのAnimationTrackが位置を上書きするのを防ぐ
        /// </summary>
        private void SetPositionPreservation(AnimationStateType state)
        {
            // 移動ステート・インタラクションでは位置保持しない（Root Motionを使用）
            bool shouldPreserve = state switch
            {
                AnimationStateType.Walk => false,
                AnimationStateType.Run => false,
                AnimationStateType.Interact => false,  // インタラクションはRoot Motion使用
                _ => true  // Idle, Talk, Emote, Custom は位置保持
            };

            _shouldPreservePosition = shouldPreserve;

            // 保持する場合は現在位置を記録
            // ただし、SetPreservedPositionで明示的に設定済みの場合は上書きしない
            if (shouldPreserve && _characterTransform != null)
            {
                if (_positionExplicitlySet)
                {
                    // 明示的に設定済みなので、その値を維持（上書きしない）
                    Debug.Log($"[CharacterAnimationController] Position preservation using explicit values: pos={_preservedPosition}, rot={_preservedRotation.eulerAngles}");
                    _positionExplicitlySet = false;  // フラグをリセット
                }
                else
                {
                    // 現在位置を記録
                    _preservedPosition = _characterTransform.position;
                    _preservedRotation = _characterTransform.rotation;
                    Debug.Log($"[CharacterAnimationController] Position preserved at: pos={_preservedPosition}, rot={_preservedRotation.eulerAngles}");
                }
            }
            else
            {
                // 位置保持しない場合はフラグをリセット
                _positionExplicitlySet = false;
            }
        }

    }

    /// <summary>
    /// アニメーションのステートタイプ
    /// </summary>
    public enum AnimationStateType
    {
        Idle,
        Walk,
        Run,
        Talk,
        Emote,
        Interact,
        Thinking,
        Custom
    }

}
