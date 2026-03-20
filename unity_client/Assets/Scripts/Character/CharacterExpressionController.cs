using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UniVRM10;
using CyanNook.Core;
using CyanNook.Timeline;

namespace CyanNook.Character
{
    /// <summary>
    /// キャラクターの表情制御を担当
    ///
    /// 動作モード:
    /// - Facial Timeline モード: facialTimelineData が設定されている場合
    ///   感情ごとの専用Timelineを再生し、LoopRegionで保持→タイムアウト後にneutral復帰
    /// - 直接制御モード（フォールバック）: facialTimelineData が未設定の場合
    ///   従来通りVRM Expressionに直接値を書き込む
    ///
    /// 優先度: Body Timeline VrmExpressionTrack > Facial Director > 直接制御
    /// </summary>
    public class CharacterExpressionController : MonoBehaviour
    {
        [Header("Settings（直接制御フォールバック用）")]
        [Tooltip("表情変化のブレンド速度")]
        public float blendSpeed = 5f;

        [Tooltip("感情の減衰速度（感情が自然に戻る速度）")]
        public float decaySpeed = 0.5f;

        [Tooltip("テキスト表示完了後、感情が自動減衰を開始するまでの時間（秒）")]
        public float decayDelay = 3f;

        [Header("Facial Timeline")]
        [Tooltip("感情→Facial Timelineの紐付けデータ（未設定時は直接制御モード）")]
        public FacialTimelineData facialTimelineData;

        [Header("Surprised")]
        [Tooltip("驚き表情の閾値（これ未満は無視）")]
        public float surprisedThreshold = 0.3f;

        [Tooltip("驚き表情の持続時間（秒）。経過後にneutralまたは他感情へ遷移")]
        public float surprisedDuration = 3f;

        [Tooltip("テキスト表示完了後、感情Timelineのループを保持する時間（秒）。この時間経過後にend→neutral復帰")]
        public float emotionHoldDuration = 3f;

        [Header("Current State")]
        [SerializeField]
        private EmotionData _targetEmotion = new EmotionData();

        [SerializeField]
        private EmotionData _currentEmotion = new EmotionData();

        [SerializeField]
        private float _timeSinceLastUpdate = 0f;

        private Vrm10Instance _vrmInstance;

        // --- Facial Timeline制御 ---
        [HideInInspector] public PlayableDirector facialDirector;
        [HideInInspector] public CharacterLookAtController lookAtController;

        [SerializeField]
        private EmotionType _currentFacialEmotion = EmotionType.Neutral;

        private float _facialHoldTimer;

        // Facial LoopRegion制御
        private bool _facialHasLoopRegion;
        private double _facialLoopStartTime;
        private double _facialLoopEndTime;
        private double _facialEndStartTime;
        private bool _facialIsInLoop;
        private bool _facialShouldExitLoop;
        private bool _facialIsEnding;
        private int _facialEndingFrameCount;
        private const int FACIAL_ENDING_GRACE_FRAMES = 3;

        // テキスト表示完了フラグ
        // チャット応答時: 応答開始でfalse → テキスト表示完了でtrue
        // 非チャット使用時: デフォルトtrue（タイマーが即座に動作）
        private bool _textDisplayComplete = true;

        // Body Timeline override
        private bool _isBodyExpressionOverriding;

        // Timeline Expression 加算制御
        private int _lastTimelineExpressionResetFrame = -1;
        private Dictionary<ExpressionKey, float> _timelineExpressionAccum = new Dictionary<ExpressionKey, float>();

        /// <summary>全プリセットExpressionKey（フレームリセット用）</summary>
        private static readonly ExpressionKey[] ALL_PRESET_EXPRESSION_KEYS = new[]
        {
            ExpressionKey.Happy, ExpressionKey.Angry, ExpressionKey.Sad,
            ExpressionKey.Relaxed, ExpressionKey.Surprised, ExpressionKey.Neutral,
            ExpressionKey.Aa, ExpressionKey.Ih, ExpressionKey.Ou,
            ExpressionKey.Ee, ExpressionKey.Oh,
            ExpressionKey.Blink, ExpressionKey.BlinkLeft, ExpressionKey.BlinkRight,
            ExpressionKey.LookUp, ExpressionKey.LookDown,
            ExpressionKey.LookLeft, ExpressionKey.LookRight,
        };

        // Surprised一時リアクション制御
        private bool _surprisedPhaseActive;
        private float _surprisedTimer;
        private EmotionData _postSurprisedEmotion; // null = neutral復帰

        // 感情ブレンド制御
        private bool _isBlendMode;
        private EmotionType _currentBlendSecondary = EmotionType.Neutral;
        private Dictionary<EmotionType, float> _trackBlendWeights = new Dictionary<EmotionType, float>();

        /// <summary>Facial Timelineモードが有効か</summary>
        private bool UseFacialTimeline => facialTimelineData != null && facialDirector != null;

        /// <summary>現在のFacial感情タイプ</summary>
        public EmotionType CurrentFacialEmotion => _currentFacialEmotion;

        // =====================================================================
        // Unity Lifecycle
        // =====================================================================

        private void Update()
        {
            // テキスト表示完了後のみタイマーを進める
            if (_textDisplayComplete)
            {
                _timeSinceLastUpdate += Time.deltaTime;
            }

            if (UseFacialTimeline)
            {
                UpdateFacialTimeline();

                // Facial DirectorまたはBody Overrideがアクティブなら直接制御しない
                if (IsFacialTimelineActive() || _isBodyExpressionOverriding) return;
            }

            // フォールバック: 従来の直接制御
            if (_textDisplayComplete && _timeSinceLastUpdate > decayDelay)
            {
                DecayEmotion();
            }

            BlendEmotion();
            ApplyToVRM();
        }

        // =====================================================================
        // Public API
        // =====================================================================

        /// <summary>
        /// 感情を設定
        /// Facial Timelineモード: 感情値から単体またはブレンドTimelineを選択
        /// 直接制御モード: ターゲット値を設定
        /// </summary>
        public void SetEmotion(EmotionData emotion)
        {
            _targetEmotion = emotion ?? new EmotionData();
            _timeSinceLastUpdate = 0f;

            if (UseFacialTimeline)
            {
                if (_targetEmotion.surprised >= surprisedThreshold)
                {
                    StartSurprisedPhase(_targetEmotion);
                }
                else
                {
                    _surprisedPhaseActive = false;
                    _targetEmotion.surprised = 0f;
                    SelectAndPlayFacialTimeline();
                }
            }
            else
            {
                // 直接制御: 閾値フィルタのみ
                if (_targetEmotion.surprised < surprisedThreshold)
                {
                    _targetEmotion.surprised = 0f;
                }
            }
        }

        /// <summary>
        /// 感情を即座に設定（ブレンドなし、直接制御モード用）
        /// Facial Timelineモードでも感情Timelineの切り替えは行う
        /// </summary>
        public void SetEmotionImmediate(EmotionData emotion)
        {
            _targetEmotion = emotion ?? new EmotionData();
            _currentEmotion = new EmotionData
            {
                happy = _targetEmotion.happy,
                relaxed = _targetEmotion.relaxed,
                angry = _targetEmotion.angry,
                sad = _targetEmotion.sad,
                surprised = _targetEmotion.surprised
            };
            _timeSinceLastUpdate = 0f;

            if (UseFacialTimeline)
            {
                if (_targetEmotion.surprised >= surprisedThreshold)
                {
                    StartSurprisedPhase(_targetEmotion);
                }
                else
                {
                    _surprisedPhaseActive = false;
                    _targetEmotion.surprised = 0f;
                    SelectAndPlayFacialTimeline();
                }
            }
            else
            {
                // 直接制御: 閾値フィルタのみ
                if (_targetEmotion.surprised < surprisedThreshold)
                {
                    _targetEmotion.surprised = 0f;
                    _currentEmotion.surprised = 0f;
                }
                ApplyToVRM();
            }
        }

        /// <summary>
        /// 感情をニュートラルにリセット
        /// </summary>
        public void ResetEmotion()
        {
            _targetEmotion = new EmotionData();

            if (UseFacialTimeline && _currentFacialEmotion != EmotionType.Neutral)
            {
                PlayFacialTimeline(EmotionType.Neutral);
            }
        }

        /// <summary>
        /// Body TimelineのVrmExpressionTrack優先制御を設定
        /// Body TimelineにVrmExpressionTrackが存在する場合、Facial Directorを一時停止
        /// </summary>
        public void SetBodyExpressionOverride(bool isOverriding)
        {
            if (_isBodyExpressionOverriding == isOverriding) return;

            _isBodyExpressionOverriding = isOverriding;
            if (!UseFacialTimeline) return;

            if (isOverriding)
            {
                facialDirector.Pause();
                Debug.Log("[CharacterExpressionController] Facial paused: body expression override active");
            }
            else
            {
                if (facialDirector.state == PlayState.Paused)
                {
                    facialDirector.Resume();
                }
                Debug.Log("[CharacterExpressionController] Facial resumed: body expression override ended");
            }
        }

        /// <summary>
        /// VRM Instanceを設定（VRM読み込み時に呼び出し）
        /// </summary>
        public void SetVrmInstance(Vrm10Instance vrmInstance)
        {
            _vrmInstance = vrmInstance;
            Debug.Log("[CharacterExpressionController] VRM Expression initialized");
        }

        /// <summary>
        /// VRM Instanceを取得（VrmExpressionTrackからのアクセス用）
        /// </summary>
        public Vrm10Instance GetVrmInstance()
        {
            return _vrmInstance;
        }

        /// <summary>
        /// 現在の感情を取得
        /// </summary>
        public EmotionData GetCurrentEmotion()
        {
            return _currentEmotion;
        }

        /// <summary>
        /// 現在の支配的感情を取得
        /// </summary>
        public EmotionType GetDominantEmotion()
        {
            return _currentEmotion.GetDominantEmotion();
        }

        /// <summary>
        /// Neutral Facial Timelineの初期再生を開始
        /// VrmLoaderから呼び出し
        /// </summary>
        public void StartNeutralFacial()
        {
            if (!UseFacialTimeline) return;

            PlayFacialTimeline(EmotionType.Neutral);
            Debug.Log("[CharacterExpressionController] Neutral facial timeline started");
        }

        /// <summary>
        /// チャット応答開始を通知
        /// テキスト表示完了までdecay/holdタイマーを停止する
        /// </summary>
        public void OnResponseStarted()
        {
            _textDisplayComplete = false;
        }

        /// <summary>
        /// テキスト表示完了を通知
        /// この時点からdecayDelay / emotionHoldDuration のカウントダウンを開始する
        /// </summary>
        public void NotifyTextDisplayComplete()
        {
            _textDisplayComplete = true;
            _timeSinceLastUpdate = 0f;
            _facialHoldTimer = 0f;
        }

        /// <summary>
        /// トラックのブレンドウェイトを取得（VrmExpressionMixerBehaviourから呼び出し）
        /// Neutral = 1.0（共通トラック）、非ブレンド時 = 1.0、ブレンド時 = 比率値
        /// </summary>
        public float GetTrackBlendWeight(EmotionType emotionTag)
        {
            if (emotionTag == EmotionType.Neutral) return 1f;
            if (!_isBlendMode) return 1f;
            if (_trackBlendWeights.TryGetValue(emotionTag, out float w)) return w;
            return 0f;
        }

        // =====================================================================
        // Timeline Expression リセット・加算
        // =====================================================================

        /// <summary>
        /// VrmExpressionTrackのフレーム評価前に全Expressionをゼロリセット。
        /// フレームあたり1回のみ実行（複数トラックからの呼び出しを統合）。
        /// VrmExpressionTrackが存在するTimelineは全Expressionの制御権を持つため、
        /// 前フレームの残留値や他システム（Facial Director等）の値をクリアする。
        /// </summary>
        public void ResetExpressionsForTimelineFrame()
        {
            if (_lastTimelineExpressionResetFrame == Time.frameCount) return;
            _lastTimelineExpressionResetFrame = Time.frameCount;

            _timelineExpressionAccum.Clear();

            if (_vrmInstance == null || _vrmInstance.Runtime == null) return;
            var expression = _vrmInstance.Runtime.Expression;
            if (expression == null) return;

            foreach (var key in ALL_PRESET_EXPRESSION_KEYS)
            {
                expression.SetWeight(key, 0f);
            }
        }

        /// <summary>
        /// Timeline ExpressionをExpressionKeyに加算適用。
        /// 同一フレーム内の複数トラックからの値を累積し、VRMに即時反映する。
        /// ResetExpressionsForTimelineFrame()でゼロリセット後に呼び出すこと。
        /// </summary>
        public void AddTimelineExpression(ExpressionKey key, float value)
        {
            if (_timelineExpressionAccum.TryGetValue(key, out float current))
                _timelineExpressionAccum[key] = current + value;
            else
                _timelineExpressionAccum[key] = value;

            _vrmInstance?.Runtime?.Expression?.SetWeight(key, _timelineExpressionAccum[key]);
        }

        // =====================================================================
        // Facial Timeline制御
        // =====================================================================

        private bool IsFacialTimelineActive()
        {
            return facialDirector != null &&
                   facialDirector.playableAsset != null &&
                   (facialDirector.state == PlayState.Playing || facialDirector.state == PlayState.Paused);
        }

        /// <summary>
        /// Surprised一時リアクションフェーズを開始
        /// surprisedタイムラインを再生し、surprisedDuration後に後続感情へ遷移
        /// </summary>
        private void StartSurprisedPhase(EmotionData emotion)
        {
            // 後続感情を保存（surprised以外の4感情）
            var postEmotion = new EmotionData
            {
                happy = emotion.happy,
                relaxed = emotion.relaxed,
                angry = emotion.angry,
                sad = emotion.sad,
                surprised = 0f
            };
            _postSurprisedEmotion = postEmotion.GetTotalIntensity() > 0f ? postEmotion : null;

            // surprisedタイムラインを再生
            _surprisedPhaseActive = true;
            _surprisedTimer = 0f;

            // surprised単体として再生（ブレンドなし）
            _targetEmotion = new EmotionData { surprised = emotion.surprised };
            PlayFacialTimeline(EmotionType.Surprised);

            Debug.Log($"[CharacterExpressionController] Surprised phase started (duration={surprisedDuration}s, hasPostEmotion={_postSurprisedEmotion != null})");
        }

        /// <summary>
        /// Surprised一時リアクションフェーズからの遷移
        /// 後続感情があればその表情を再生、なければneutralへ
        /// </summary>
        private void TransitionFromSurprised()
        {
            _surprisedPhaseActive = false;
            var postEmotion = _postSurprisedEmotion;
            _postSurprisedEmotion = null;

            if (postEmotion != null && postEmotion.GetTotalIntensity() > 0f)
            {
                Debug.Log("[CharacterExpressionController] Surprised -> post-emotion transition");
                _targetEmotion = postEmotion;
                SelectAndPlayFacialTimeline();
            }
            else
            {
                Debug.Log("[CharacterExpressionController] Surprised -> neutral transition");
                PlayFacialTimeline(EmotionType.Neutral);
            }
        }

        /// <summary>
        /// 感情値からTimeline選択ロジックを実行
        /// ブレンド可能ペアの判定、正規化比率の計算を行い、
        /// 単体またはブレンドTimelineを選択して再生する
        /// </summary>
        private void SelectAndPlayFacialTimeline()
        {
            var (primary, secondary, primaryRatio) = _targetEmotion.GetTopTwoEmotions();

            bool shouldBlend = secondary != EmotionType.Neutral
                && EmotionData.IsBlendablePair(primary, secondary)
                && primaryRatio < 0.9f
                && facialTimelineData.GetBlendTimeline(primary, secondary) != null;

            if (shouldBlend)
            {
                if (_isBlendMode
                    && _currentFacialEmotion == primary
                    && _currentBlendSecondary == secondary)
                {
                    // 同ブレンドペア → ウェイト更新+タイマーリセットのみ（Timeline再起動なし）
                    _trackBlendWeights[primary] = primaryRatio;
                    _trackBlendWeights[secondary] = 1f - primaryRatio;
                    _facialHoldTimer = 0f;
                }
                else
                {
                    // 異なるペアまたは単体からの切り替え → ブレンドTimeline再生
                    PlayFacialTimeline(primary, secondary, primaryRatio);
                }
            }
            else
            {
                if (primary != _currentFacialEmotion || _isBlendMode)
                {
                    // 異なる感情、またはブレンドから単体への切り替え
                    PlayFacialTimeline(primary);
                }
                else
                {
                    // 同じ感情 → ホールドタイマーリセット
                    _facialHoldTimer = 0f;
                }
            }
        }

        /// <summary>
        /// 感情Timelineを再生（単体またはブレンド）
        /// </summary>
        private void PlayFacialTimeline(
            EmotionType emotion,
            EmotionType blendSecondary = EmotionType.Neutral,
            float primaryRatio = 1f)
        {
            bool isBlend = blendSecondary != EmotionType.Neutral;

            // Timeline取得
            TimelineAsset timeline;
            if (isBlend)
            {
                timeline = facialTimelineData.GetBlendTimeline(emotion, blendSecondary);
                if (timeline == null)
                {
                    // ブレンドTimeline未登録 → 単体にフォールバック
                    isBlend = false;
                    timeline = facialTimelineData.GetTimeline(emotion);
                }
            }
            else
            {
                timeline = facialTimelineData.GetTimeline(emotion);
            }

            // 該当Timelineがない場合はneutralにフォールバック
            if (timeline == null && emotion != EmotionType.Neutral)
            {
                timeline = facialTimelineData.GetTimeline(EmotionType.Neutral);
                emotion = EmotionType.Neutral;
                isBlend = false;
            }

            if (timeline == null)
            {
                Debug.LogWarning($"[CharacterExpressionController] Facial timeline not found for: {emotion}");
                return;
            }

            // 状態更新
            _currentFacialEmotion = emotion;
            _currentBlendSecondary = isBlend ? blendSecondary : EmotionType.Neutral;
            _isBlendMode = isBlend;
            _facialHoldTimer = 0f;

            // ブレンドウェイト設定
            _trackBlendWeights.Clear();
            if (isBlend)
            {
                _trackBlendWeights[emotion] = primaryRatio;
                _trackBlendWeights[blendSecondary] = 1f - primaryRatio;
            }

            // 再生中のTimelineを確実に停止してからplayableAssetを差し替え
            facialDirector.Stop();

            // 前のTimelineが設定したExpression weightをクリア
            ResetVrmExpressionWeights();

            facialDirector.playableAsset = timeline;
            BindFacialTimeline(timeline);
            SetupFacialLoopRegion(timeline);

            // WrapMode設定
            if (_facialHasLoopRegion)
            {
                facialDirector.extrapolationMode = DirectorWrapMode.Hold;
            }
            else if (emotion == EmotionType.Neutral && !isBlend)
            {
                facialDirector.extrapolationMode = DirectorWrapMode.Loop;
            }
            else
            {
                facialDirector.extrapolationMode = DirectorWrapMode.Hold;
            }

            facialDirector.time = 0;
            facialDirector.Play();

            if (isBlend)
            {
                Debug.Log($"[CharacterExpressionController] Playing facial blend: {emotion}+{blendSecondary} ({primaryRatio:F2}:{1f - primaryRatio:F2}), Timeline: {timeline.name}, LoopRegion: {_facialHasLoopRegion}");
            }
            else
            {
                Debug.Log($"[CharacterExpressionController] Playing facial: {emotion}, Timeline: {timeline.name}, LoopRegion: {_facialHasLoopRegion}");
            }
        }

        /// <summary>
        /// Facial Timelineのトラックをバインド
        /// </summary>
        private void BindFacialTimeline(TimelineAsset timeline)
        {
            foreach (var track in timeline.GetOutputTracks())
            {
                if (track is VrmExpressionTrack vrmTrack)
                {
                    facialDirector.SetGenericBinding(vrmTrack, this);
                }
                else if (track is LookAtTrack lookAtTrack)
                {
                    if (lookAtController != null)
                    {
                        facialDirector.SetGenericBinding(lookAtTrack, lookAtController);
                    }
                }
            }
        }

        /// <summary>
        /// Facial Timeline毎フレーム更新
        /// </summary>
        private void UpdateFacialTimeline()
        {
            if (!IsFacialTimelineActive()) return;
            if (_isBodyExpressionOverriding) return;

            double currentTime = facialDirector.time;

            // --- LoopRegion制御 ---
            if (_facialHasLoopRegion)
            {
                // LoopStart到達の検出
                if (!_facialIsInLoop && !_facialIsEnding && currentTime >= _facialLoopStartTime)
                {
                    _facialIsInLoop = true;
                    Debug.Log($"[CharacterExpressionController] Facial loop entered at {currentTime:F3}");

                    // ループ開始前にEndPhaseがリクエストされていた場合
                    if (_facialShouldExitLoop)
                    {
                        JumpToFacialEndPhase();
                        return;
                    }
                }

                // LoopEnd到達→LoopStartにジャンプ
                if (_facialIsInLoop && !_facialShouldExitLoop)
                {
                    if (currentTime >= _facialLoopEndTime)
                    {
                        facialDirector.time = _facialLoopStartTime;
                    }
                }

                // 終了フェーズの完了検出
                if (_facialIsEnding)
                {
                    _facialEndingFrameCount++;
                    if (_facialEndingFrameCount <= FACIAL_ENDING_GRACE_FRAMES) return;

                    double assetDuration = facialDirector.playableAsset?.duration ?? 0;
                    var timelineAsset = facialDirector.playableAsset as TimelineAsset;
                    double frameRate = timelineAsset?.editorSettings.frameRate ?? 60.0;
                    double frameMargin = 1.0 / frameRate;

                    // Timeline終端に到達
                    if (currentTime >= assetDuration - frameMargin)
                    {
                        CompleteFacialEndPhase();
                        return;
                    }

                    // 再生が停止した場合
                    if (facialDirector.state != PlayState.Playing && currentTime >= _facialEndStartTime)
                    {
                        CompleteFacialEndPhase();
                        return;
                    }
                }

                // ホールドタイマー（Neutral以外のみ）
                if (_facialIsInLoop && !_facialShouldExitLoop && _currentFacialEmotion != EmotionType.Neutral)
                {
                    if (_surprisedPhaseActive)
                    {
                        // Surprised: 専用タイマー（テキスト表示完了を待たない）
                        _surprisedTimer += Time.deltaTime;
                        if (_surprisedTimer >= surprisedDuration)
                        {
                            RequestFacialEndPhase();
                        }
                    }
                    else if (_textDisplayComplete)
                    {
                        // 通常感情: テキスト表示完了後にカウント
                        _facialHoldTimer += Time.deltaTime;
                        if (_facialHoldTimer >= emotionHoldDuration)
                        {
                            RequestFacialEndPhase();
                        }
                    }
                }
            }
            else
            {
                // LoopRegionなし: 一発再生の完了検出（Neutral以外）
                if (_currentFacialEmotion != EmotionType.Neutral)
                {
                    double assetDuration = facialDirector.playableAsset?.duration ?? 0;
                    var timelineAsset = facialDirector.playableAsset as TimelineAsset;
                    double frameRate = timelineAsset?.editorSettings.frameRate ?? 60.0;
                    double frameMargin = 1.0 / frameRate;

                    if (assetDuration > 0 && currentTime >= assetDuration - frameMargin)
                    {
                        if (_surprisedPhaseActive)
                        {
                            TransitionFromSurprised();
                        }
                        else
                        {
                            Debug.Log("[CharacterExpressionController] One-shot facial complete, returning to neutral");
                            PlayFacialTimeline(EmotionType.Neutral);
                        }
                    }
                }
            }
        }

        // =====================================================================
        // Facial LoopRegion制御
        // =====================================================================

        /// <summary>
        /// Facial TimelineからLoopRegionTrackの情報を読み取り
        /// </summary>
        private void SetupFacialLoopRegion(TimelineAsset timeline)
        {
            ResetFacialLoopRegion();

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

                        _facialLoopStartTime = clip.start;
                        _facialLoopEndTime = clip.end + frameOffset;
                        _facialEndStartTime = clip.end;
                        _facialHasLoopRegion = true;

                        Debug.Log($"[CharacterExpressionController] Facial LoopRegion: start={_facialLoopStartTime:F3}, loopEnd={_facialLoopEndTime:F3}, endStart={_facialEndStartTime:F3}");
                        return; // 最初のLoopRegionClipのみ使用
                    }
                }
            }
        }

        private void ResetFacialLoopRegion()
        {
            _facialHasLoopRegion = false;
            _facialLoopStartTime = 0;
            _facialLoopEndTime = 0;
            _facialEndStartTime = 0;
            _facialIsInLoop = false;
            _facialShouldExitLoop = false;
            _facialIsEnding = false;
            _facialEndingFrameCount = 0;
        }

        /// <summary>
        /// Facialのend phaseへの遷移をリクエスト
        /// </summary>
        private void RequestFacialEndPhase()
        {
            if (!_facialHasLoopRegion) return;

            _facialShouldExitLoop = true;
            Debug.Log($"[CharacterExpressionController] Facial RequestEndPhase (isInLoop={_facialIsInLoop})");

            if (_facialIsInLoop)
            {
                JumpToFacialEndPhase();
            }
        }

        private void JumpToFacialEndPhase()
        {
            _facialIsInLoop = false;
            _facialIsEnding = true;
            _facialEndingFrameCount = 0;

            if (facialDirector != null)
            {
                facialDirector.extrapolationMode = DirectorWrapMode.Hold;
                facialDirector.time = _facialEndStartTime;
                Debug.Log($"[CharacterExpressionController] Facial jumped to end phase at {_facialEndStartTime:F3}");

                if (facialDirector.state != PlayState.Playing)
                {
                    facialDirector.Play();
                }
            }
        }

        private void CompleteFacialEndPhase()
        {
            Debug.Log("[CharacterExpressionController] Facial end phase complete");
            ResetFacialLoopRegion();

            if (_surprisedPhaseActive)
            {
                TransitionFromSurprised();
            }
            else
            {
                Debug.Log("[CharacterExpressionController] Returning to neutral");
                PlayFacialTimeline(EmotionType.Neutral);
            }
        }

        // =====================================================================
        // 直接制御（フォールバック）
        // =====================================================================

        private void BlendEmotion()
        {
            float t = Time.deltaTime * blendSpeed;

            _currentEmotion.happy = Mathf.Lerp(_currentEmotion.happy, _targetEmotion.happy, t);
            _currentEmotion.relaxed = Mathf.Lerp(_currentEmotion.relaxed, _targetEmotion.relaxed, t);
            _currentEmotion.angry = Mathf.Lerp(_currentEmotion.angry, _targetEmotion.angry, t);
            _currentEmotion.sad = Mathf.Lerp(_currentEmotion.sad, _targetEmotion.sad, t);
            _currentEmotion.surprised = Mathf.Lerp(_currentEmotion.surprised, _targetEmotion.surprised, t);
        }

        private void DecayEmotion()
        {
            float t = Time.deltaTime * decaySpeed;

            _targetEmotion.happy = Mathf.Lerp(_targetEmotion.happy, 0f, t);
            _targetEmotion.relaxed = Mathf.Lerp(_targetEmotion.relaxed, 0f, t);
            _targetEmotion.angry = Mathf.Lerp(_targetEmotion.angry, 0f, t);
            _targetEmotion.sad = Mathf.Lerp(_targetEmotion.sad, 0f, t);
            _targetEmotion.surprised = Mathf.Lerp(_targetEmotion.surprised, 0f, t);
        }

        private void ApplyToVRM()
        {
            if (_vrmInstance == null || _vrmInstance.Runtime == null) return;

            var expression = _vrmInstance.Runtime.Expression;
            if (expression == null) return;

            expression.SetWeight(ExpressionKey.Happy, _currentEmotion.happy);
            expression.SetWeight(ExpressionKey.Relaxed, _currentEmotion.relaxed);
            expression.SetWeight(ExpressionKey.Angry, _currentEmotion.angry);
            expression.SetWeight(ExpressionKey.Sad, _currentEmotion.sad);
            expression.SetWeight(ExpressionKey.Surprised, _currentEmotion.surprised);
        }

        /// <summary>
        /// 全VRM Expression weightを0にリセット
        /// Facial Timeline切り替え時に、前のTimelineの残留値をクリアする
        /// </summary>
        private void ResetVrmExpressionWeights()
        {
            if (_vrmInstance == null || _vrmInstance.Runtime == null) return;

            var expression = _vrmInstance.Runtime.Expression;
            if (expression == null) return;

            expression.SetWeight(ExpressionKey.Happy, 0f);
            expression.SetWeight(ExpressionKey.Relaxed, 0f);
            expression.SetWeight(ExpressionKey.Angry, 0f);
            expression.SetWeight(ExpressionKey.Sad, 0f);
            expression.SetWeight(ExpressionKey.Surprised, 0f);
            expression.SetWeight(ExpressionKey.Neutral, 0f);
            expression.SetWeight(ExpressionKey.Blink, 0f);
            expression.SetWeight(ExpressionKey.BlinkLeft, 0f);
            expression.SetWeight(ExpressionKey.BlinkRight, 0f);
        }
    }
}
