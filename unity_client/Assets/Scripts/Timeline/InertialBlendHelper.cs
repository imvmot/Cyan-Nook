using System.Collections.Generic;
using UnityEngine;

namespace CyanNook.Timeline
{
    /// <summary>
    /// 慣性補間（Inertial Blending）の実際の処理を行うヘルパーコンポーネント
    ///
    /// 使用方法:
    /// 1. VRMインスタンスにこのコンポーネントをアタッチ
    /// 2. CharacterAnimationControllerからStartInertialBlend()を呼び出す
    ///
    /// アーキテクチャ（Pre-Update Restoreパターン）:
    /// 1. Update: 前フレームで適用したオフセットをAnimatorが走る前に復元（汚れを拭く）
    /// 2. Animator: クリーンな状態から新しいポーズを計算
    /// 3. LateUpdate: クリーン位置を保存 → オフセットを計算・適用
    /// 4. SkinnedMeshRenderer: オフセット適用済みの位置でメッシュ生成・描画
    ///
    /// ローカル座標（localPosition/localRotation）を使用して累積誤差を防止
    ///
    /// ボーン参照はAwake時に全Humanoidボーンをキャッシュし、
    /// StartInertialBlend時にキャッシュから取得する（GetBoneTransformの再呼び出しを回避）
    /// </summary>
    // UniVRMのSpringBone処理などは11000付近で行われることがあるため、
    // 安全マージンを取って20000を指定します。
    [DefaultExecutionOrder(20000)]
    public class InertialBlendHelper : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("対象のAnimator")]
        public Animator animator;

        [Header("Debug")]
        [SerializeField]
        private bool _isActive;
        [SerializeField]
        private float _elapsedTime;
        [SerializeField]
        private float _blendDuration;

        private const float DEFAULT_BLEND_DURATION = 0.3f;

        // 状態管理
        private enum State
        {
            Idle,                // 待機中
            WaitingFirstFrame,   // 初回フレーム待機（Animator評価後にオフセット計算・ブレンド開始）
            Blending,            // 慣性補間中
        }
        private State _state = State.Idle;

        // ボーンごとの慣性補間データ（ローカル座標、5次多項式方式）
        private struct BoneBlendData
        {
            public Transform transform;
            public Vector3 previousLocalPosition;      // 遷移前ポーズ（_lastCleanPose由来、frame N-1）
            public Quaternion previousLocalRotation;
            public Vector3 prevPrevLocalPosition;      // 2フレーム前ポーズ（_prevCleanPose由来、frame N-2）
            public Quaternion prevPrevLocalRotation;
            public bool hasPrevPrev;                   // prevPrevデータが有効か
            // 位置用（1Dスカラー + 方向ベクトル）
            public float posX0;
            public Vector3 posBaseVec;
            public float posV0, posT1, posA0, posA, posB, posC;
            // 回転用（軸角度表現で1Dスカラー + 回転軸）
            public float rotX0;
            public Vector3 rotAxis;
            public float rotV0, rotT1, rotA0, rotA, rotB, rotC;
            // クリーンポーズ
            public Vector3 cleanLocalPosition;
            public Quaternion cleanLocalRotation;
        }

        // Awakeで全Humanoidボーンの参照をキャッシュ
        private Dictionary<HumanBodyBones, Transform> _boneTransformCache;

        // LateUpdateで保存したクリーンポーズのキャッシュ（Transform → ローカル座標）
        private Dictionary<Transform, (Vector3 localPos, Quaternion localRot)> _lastCleanPose
            = new Dictionary<Transform, (Vector3, Quaternion)>();

        // 1フレーム前のクリーンポーズキャッシュ（速度計算用）
        private Dictionary<Transform, (Vector3 localPos, Quaternion localRot)> _prevCleanPose
            = new Dictionary<Transform, (Vector3, Quaternion)>();

        // UpdateCleanPoseCacheの二重スワップ防止用フレーム番号
        private int _lastCacheUpdateFrame = -1;

        private BoneBlendData[] _bones;
        private int _boneCount;
        private bool _isOffsetApplied; // オフセットが適用済みかどうか
        private bool _prePassApplied;  // PrePassがWaitingFirstFrameを処理済みか

        // 旧IBのビジュアル状態キャッシュ（CaptureVisualStateIfActiveで保存）
        // 新IB開始時に旧IBオフセット消失によるジャンプを防止するため、
        // RestoreCleanIfActive前の実際の表示状態を保持する。
        private Dictionary<Transform, (Vector3 localPos, Quaternion localRot)> _oldVisualState
            = new Dictionary<Transform, (Vector3, Quaternion)>();

        private void Awake()
        {
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            RebuildBoneCache();
        }

        /// <summary>
        /// 全HumanoidボーンのTransformキャッシュを再構築。
        /// VRM10のControlBoneはAwake後に作成される場合があるため、
        /// VRM初期化完了後にも呼び出す必要がある。
        /// </summary>
        public void RebuildBoneCache()
        {
            _boneTransformCache = new Dictionary<HumanBodyBones, Transform>();
            if (animator != null)
            {
                for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
                {
                    var bone = (HumanBodyBones)i;
                    var t = animator.GetBoneTransform(bone);
                    if (t != null)
                    {
                        _boneTransformCache[bone] = t;
                    }
                }
                Debug.Log($"[InertialBlendHelper] Bone cache rebuilt: {_boneTransformCache.Count} bones");
            }
        }

        /// <summary>
        /// 慣性補間を開始（ボーンリスト指定）
        /// Timeline再生前に呼び出す
        /// </summary>
        /// <param name="duration">ブレンド時間（秒）</param>
        /// <param name="targetBones">対象ボーンのリスト</param>
        public void StartInertialBlend(float duration, List<HumanBodyBones> targetBones)
        {
            if (targetBones == null || targetBones.Count == 0)
            {
                StartInertialBlend(duration);
                return;
            }

            if (_boneTransformCache == null || _boneTransformCache.Count == 0)
            {
                Debug.LogWarning("[InertialBlendHelper] Bone transform cache is empty, cannot start inertial blend");
                return;
            }

            // 旧IBのビジュアル状態を保存（RestoreCleanIfActiveで消える前に）
            CaptureVisualStateIfActive();
            // 前回のブレンドが動作中の場合、クリーン値に復元してからオフセット汚染を除去
            RestoreCleanIfActive();

            // キャッシュから対象ボーンのTransformを取得してBoneBlendData配列を構築
            _bones = new BoneBlendData[targetBones.Count];
            _boneCount = 0;
            foreach (var bone in targetBones)
            {
                if (_boneTransformCache.TryGetValue(bone, out var t))
                {
                    _bones[_boneCount].transform = t;

                    // 旧IBのビジュアル状態があればそれを使用（旧IBオフセット消失ジャンプ防止）
                    if (_oldVisualState.TryGetValue(t, out var visualPose))
                    {
                        _bones[_boneCount].previousLocalPosition = visualPose.localPos;
                        _bones[_boneCount].previousLocalRotation = visualPose.localRot;
                    }
                    else if (_lastCleanPose.TryGetValue(t, out var cleanPose))
                    {
                        _bones[_boneCount].previousLocalPosition = cleanPose.localPos;
                        _bones[_boneCount].previousLocalRotation = cleanPose.localRot;
                    }
                    else
                    {
                        _bones[_boneCount].previousLocalPosition = t.localPosition;
                        _bones[_boneCount].previousLocalRotation = t.localRotation;
                    }

                    // 速度計算用: _prevCleanPose（2フレーム前）をUpdate時点で保存
                    // LateUpdateのUpdateCleanPoseCacheでスワップされる前に取得する
                    if (_prevCleanPose.TryGetValue(t, out var prevPrev))
                    {
                        _bones[_boneCount].prevPrevLocalPosition = prevPrev.localPos;
                        _bones[_boneCount].prevPrevLocalRotation = prevPrev.localRot;
                        _bones[_boneCount].hasPrevPrev = true;
                    }
                    else
                    {
                        _bones[_boneCount].hasPrevPrev = false;
                    }
                    _boneCount++;
                }
                else
                {
                    Debug.LogWarning($"[InertialBlendHelper] Bone {bone} not found in cache, skipping");
                }
            }

            if (_boneCount == 0)
            {
                Debug.LogWarning("[InertialBlendHelper] No valid bones found, cannot start inertial blend");
                return;
            }

            BeginBlend(duration);
        }

        /// <summary>
        /// 慣性補間を開始（全Humanoidボーン、目・顎を除く）
        /// InertialBlendTrackが無いTimeline遷移時のフォールバックとして使用
        /// </summary>
        /// <param name="duration">ブレンド時間（秒）</param>
        public void StartInertialBlendAllBones(float duration = DEFAULT_BLEND_DURATION)
        {
            if (_boneTransformCache == null || _boneTransformCache.Count == 0)
            {
                Debug.LogWarning("[InertialBlendHelper] Bone transform cache is empty, cannot start inertial blend");
                return;
            }

            // 旧IBのビジュアル状態を保存（RestoreCleanIfActiveで消える前に）
            CaptureVisualStateIfActive();
            // 前回のブレンドが動作中の場合、クリーン値に復元してからオフセット汚染を除去
            RestoreCleanIfActive();

            // キャッシュ内の全ボーンを対象（目・顎を除く、InertialBlendClipと同じ）
            _bones = new BoneBlendData[_boneTransformCache.Count];
            _boneCount = 0;
            foreach (var kvp in _boneTransformCache)
            {
                var bone = kvp.Key;
                var t = kvp.Value;
                if (t == null) continue;
                if (bone == HumanBodyBones.LeftEye || bone == HumanBodyBones.RightEye || bone == HumanBodyBones.Jaw)
                    continue;

                _bones[_boneCount].transform = t;

                // 旧IBのビジュアル状態があればそれを使用（旧IBオフセット消失ジャンプ防止）
                // なければクリーンポーズキャッシュ（通常の新規IB）
                if (_oldVisualState.TryGetValue(t, out var visualPose))
                {
                    _bones[_boneCount].previousLocalPosition = visualPose.localPos;
                    _bones[_boneCount].previousLocalRotation = visualPose.localRot;
                }
                else if (_lastCleanPose.TryGetValue(t, out var cleanPose))
                {
                    _bones[_boneCount].previousLocalPosition = cleanPose.localPos;
                    _bones[_boneCount].previousLocalRotation = cleanPose.localRot;
                }
                else
                {
                    _bones[_boneCount].previousLocalPosition = t.localPosition;
                    _bones[_boneCount].previousLocalRotation = t.localRotation;
                }

                // 速度計算用: _prevCleanPose（2フレーム前）をUpdate時点で保存
                if (_prevCleanPose.TryGetValue(t, out var prevPrev))
                {
                    _bones[_boneCount].prevPrevLocalPosition = prevPrev.localPos;
                    _bones[_boneCount].prevPrevLocalRotation = prevPrev.localRot;
                    _bones[_boneCount].hasPrevPrev = true;
                }
                else
                {
                    _bones[_boneCount].hasPrevPrev = false;
                }
                _boneCount++;
            }

            if (_boneCount == 0)
            {
                Debug.LogWarning("[InertialBlendHelper] No valid bones found, cannot start inertial blend");
                return;
            }

            BeginBlend(duration);
        }

        /// <summary>
        /// 慣性補間を開始（Hipsのみ、後方互換用）
        /// Timeline再生前に呼び出す
        /// </summary>
        /// <param name="duration">ブレンド時間（秒）</param>
        public void StartInertialBlend(float duration = DEFAULT_BLEND_DURATION)
        {
            if (_boneTransformCache == null || !_boneTransformCache.ContainsKey(HumanBodyBones.Hips))
            {
                Debug.LogWarning("[InertialBlendHelper] Hips bone not found in cache, cannot start inertial blend");
                return;
            }

            // 旧IBのビジュアル状態を保存（RestoreCleanIfActiveで消える前に）
            CaptureVisualStateIfActive();
            // 前回のブレンドが動作中の場合、クリーン値に復元してからオフセット汚染を除去
            RestoreCleanIfActive();

            var hipsTransform = _boneTransformCache[HumanBodyBones.Hips];

            _bones = new BoneBlendData[1];
            _bones[0].transform = hipsTransform;

            // 旧IBのビジュアル状態があればそれを使用（旧IBオフセット消失ジャンプ防止）
            if (_oldVisualState.TryGetValue(hipsTransform, out var visualPose))
            {
                _bones[0].previousLocalPosition = visualPose.localPos;
                _bones[0].previousLocalRotation = visualPose.localRot;
            }
            else if (_lastCleanPose.TryGetValue(hipsTransform, out var cleanPose))
            {
                _bones[0].previousLocalPosition = cleanPose.localPos;
                _bones[0].previousLocalRotation = cleanPose.localRot;
            }
            else
            {
                _bones[0].previousLocalPosition = hipsTransform.localPosition;
                _bones[0].previousLocalRotation = hipsTransform.localRotation;
            }
            _boneCount = 1;

            BeginBlend(duration);
        }

        /// <summary>
        /// 前回のブレンドが動作中の場合、ボーンをクリーン値に復元する。
        /// StartInertialBlendがUpdate(20000)より前に呼ばれた場合に、
        /// 前回ブレンドのオフセットが新ブレンドに影響しないようにする。
        /// </summary>
        private void RestoreCleanIfActive()
        {
            if (_isOffsetApplied && _bones != null)
            {
                for (int i = 0; i < _boneCount; i++)
                {
                    _bones[i].transform.localPosition = _bones[i].cleanLocalPosition;
                    _bones[i].transform.localRotation = _bones[i].cleanLocalRotation;
                }
                _isOffsetApplied = false;
            }
        }

        /// <summary>
        /// 旧IBがアクティブな場合、各ボーンの現在のビジュアル状態
        /// （clean + 旧IBオフセット）を_oldVisualStateに保存する。
        /// RestoreCleanIfActiveの前に呼び出し、新IBの「前ポーズ」として使用することで、
        /// 旧IBオフセット消失による視覚的ジャンプを防止する。
        /// </summary>
        private void CaptureVisualStateIfActive()
        {
            _oldVisualState.Clear();
            if (_isOffsetApplied && _bones != null)
            {
                for (int i = 0; i < _boneCount; i++)
                {
                    _oldVisualState[_bones[i].transform] = (
                        _bones[i].transform.localPosition,
                        _bones[i].transform.localRotation
                    );
                }
            }
        }

        /// <summary>
        /// 現在のボーン状態を_lastCleanPoseとしてスナップショットする。
        /// AdditiveOverrideHelper停止前に呼ぶことで、AO補正込みの実際の表示ポーズを
        /// 次回StartInertialBlendの「前のポーズ」として使用できる。
        ///
        /// 通常_lastCleanPoseはLateUpdate(20000)でAnimator出力（AO適用前）を保存するが、
        /// ForceStopThinking/Emote時にAOを停止してPlayStateすると、IBが_lastCleanPose
        /// （AO補正なし=例えば立ちポーズ）をソースとして使い、AO補正済み表示ポーズ
        /// （座りポーズ）との差が一瞬見えるポーズフラッシュが発生する。
        /// この問題を回避するため、AO停止前にAO補正込みのポーズをキャプチャする。
        ///
        /// 注: 次回LateUpdate/PrePassのUpdateCleanPoseCacheで正しい値に上書きされるため
        /// 一時的な上書きに留まる。
        /// </summary>
        public void SnapshotCurrentPoseAsClean()
        {
            UpdateCleanPoseCache();
        }

        /// <summary>
        /// 全Humanoidボーンのクリーンポーズキャッシュを更新。
        /// LateUpdateの先頭（Animator評価直後・他コンポーネントの適用前）で毎フレーム呼ばれる。
        /// ブレンド非動作時も継続更新することで、次回StartInertialBlend時にStale値を防ぐ。
        /// </summary>
        private void UpdateCleanPoseCache()
        {
            if (_boneTransformCache == null) return;

            // 同一フレームでの二重スワップ防止（PrePass + LateUpdateで複数回呼ばれる場合）
            int currentFrame = Time.frameCount;
            if (_lastCacheUpdateFrame == currentFrame)
            {
                // 同一フレーム: スワップせず、現在値の更新のみ
                foreach (var kvp in _boneTransformCache)
                {
                    var t = kvp.Value;
                    if (t != null)
                        _lastCleanPose[t] = (t.localPosition, t.localRotation);
                }
                return;
            }
            _lastCacheUpdateFrame = currentFrame;

            // 現在値を1フレーム前にシフト（辞書オブジェクトのスワップでGCアロケーション回避）
            (_prevCleanPose, _lastCleanPose) = (_lastCleanPose, _prevCleanPose);

            foreach (var kvp in _boneTransformCache)
            {
                var t = kvp.Value;
                if (t != null)
                {
                    _lastCleanPose[t] = (t.localPosition, t.localRotation);
                }
            }
        }

        /// <summary>
        /// ブレンドを完了し、全ボーンをクリーン位置に戻してアイドル状態に遷移する。
        /// PrePassパスと通常パスの両方から呼ばれる共通完了処理。
        /// </summary>
        private void CompleteBlend()
        {
            for (int i = 0; i < _boneCount; i++)
            {
                _bones[i].transform.localPosition = _bones[i].cleanLocalPosition;
                _bones[i].transform.localRotation = _bones[i].cleanLocalRotation;
            }
            _isOffsetApplied = false;
            _state = State.Idle;
            _isActive = false;
            Debug.Log("[InertialBlendHelper] Blend completed");
        }

        /// <summary>
        /// ブレンド状態の初期化（共通処理）
        /// </summary>
        private void BeginBlend(float duration)
        {
            _blendDuration = duration > 0f ? duration : DEFAULT_BLEND_DURATION;
            _elapsedTime = 0f;
            _isOffsetApplied = false;

            _state = State.WaitingFirstFrame;
            _isActive = true;

            Debug.Log($"[InertialBlendHelper] StartInertialBlend - " +
                $"boneCount={_boneCount}, duration={_blendDuration}, frame={Time.frameCount}");
        }

        /// <summary>
        /// Update: フレームの最初（Animatorが走る前）に、前フレームで適用したオフセットをリセット
        /// </summary>
        private void Update()
        {
            // 前フレームで慣性補間を適用していた場合、
            // Animatorが計算を始める前に「クリーンな位置」に戻しておく
            if (_isOffsetApplied && _bones != null)
            {
                for (int i = 0; i < _boneCount; i++)
                {
                    _bones[i].transform.localPosition = _bones[i].cleanLocalPosition;
                    _bones[i].transform.localRotation = _bones[i].cleanLocalRotation;
                }
                _isOffsetApplied = false;
            }
        }

        /// <summary>
        /// LateUpdate: Animatorが新しいポーズを決定した直後に、慣性補間を適用
        ///
        /// 【重要】クリーンポーズキャッシュは常時更新する。
        /// ブレンド非動作中も更新し続けないと、次回StartInertialBlend時に
        /// 数千フレーム前のStale値を「前のポーズ」として使ってしまう。
        /// </summary>
        private void LateUpdate()
        {
            // PrePassが処理を完了済みの場合、
            // クリーンキャッシュ更新・ポーズ適用済みなのでステート遷移・完了チェックのみ行う
            if (_prePassApplied)
            {
                _prePassApplied = false;
                if (_state == State.WaitingFirstFrame)
                {
                    // PrePassがクリーン保存・前ポーズ適用済み。即座にブレンド初期化・適用
                    InitializeBlend();
                    _state = State.Blending;
                    CalculateAndApplyOffset();
                }
                else if (_state == State.Blending)
                {
                    // PrePassがクリーン保存・_elapsedTime更新・オフセット適用済み。
                    // 完了チェックのみ行う。
                    // 5次多項式はt≥t₁で正確にx(t)=0に到達するため、ポップは発生しない
                    if (_elapsedTime >= _blendDuration)
                    {
                        _state = State.Idle;
                        _isActive = false;
                        Debug.Log("[InertialBlendHelper] Blend completed");
                    }
                }
                return;
            }

            // 常にクリーンポーズキャッシュを更新（ブレンド状態に関係なく）
            // この時点はAnimator評価直後・AdditiveOverride/LookAt適用前のため、
            // 真のAnimator出力値（クリーン値）が取得できる
            UpdateCleanPoseCache();

            if (_state == State.Idle) return;
            if (_bones == null || _boneCount == 0) return;

            // ブレンド対象ボーンのクリーン値をBoneBlendDataにも保存
            for (int i = 0; i < _boneCount; i++)
            {
                _bones[i].cleanLocalPosition = _bones[i].transform.localPosition;
                _bones[i].cleanLocalRotation = _bones[i].transform.localRotation;
            }

            switch (_state)
            {
                case State.WaitingFirstFrame:
                    // Animatorは新ポーズ適用済み（Update→Animation評価→LateUpdateの順序により）
                    // cleanLocal*に新ポーズが格納済みなので、即座にオフセット計算・適用を行う
                    // t=0ではdecay=1.0 → offset=full → 表示=oldPose（ちらつきなし）
                    InitializeBlend();
                    _state = State.Blending;
                    CalculateAndApplyOffset();
                    break;

                case State.Blending:
                    // オフセットを計算・適用
                    _elapsedTime += Time.deltaTime;
                    CalculateAndApplyOffset();

                    // ブレンド完了チェック
                    // 5次多項式はt≥t₁で正確にx(t)=0に到達するため、ポップは発生しない
                    if (_elapsedTime >= _blendDuration)
                    {
                        _state = State.Idle;
                        _isActive = false;
                        Debug.Log("[InertialBlendHelper] Blend completed");
                    }
                    break;
            }
        }

        #region Public Query

        /// <summary>
        /// 慣性補間が動作中かどうか
        /// </summary>
        public bool IsActive => _isActive;

        /// <summary>
        /// 現在アクティブなブレンドの残り時間（非アクティブ時は0）
        /// </summary>
        public float RemainingDuration => _isActive ? Mathf.Max(0f, _blendDuration - _elapsedTime) : 0f;

        /// <summary>
        /// 動作中の慣性補間をキャンセルする。
        /// オフセットが適用済みの場合はクリーン値に復元してから停止する。
        /// </summary>
        public void CancelBlend()
        {
            if (_state == State.Idle) return;

            // オフセットが適用済みならクリーン値に復元
            RestoreCleanIfActive();

            _state = State.Idle;
            _isActive = false;
            Debug.Log("[InertialBlendHelper] Blend cancelled");
        }

        /// <summary>
        /// PrePass用: FastSpringBoneService(11010)前にポーズ補正を適用する。
        /// InertialBlendPrePass(11005)から毎フレーム呼ばれる。
        ///
        /// 実行順序: Vrm10Instance(11000) → PrePass(11005) → FastSpringBoneService(11010)
        ///
        /// Vrm10InstanceのControlRigがAnimator出力をボーンに適用した後、
        /// SpringBone計算前にIB補正済みポーズを適用する。これにより：
        /// - WaitingFirstFrame: SpringBoneが新Timelineの突然のポーズ変化に反応しない
        /// - Blending: SpringBoneがIBオフセット適用済みの滑らかなポーズを見る
        ///   （PrePass無しではSpringBoneが生ポーズを見てしまい、髪・揺れ物がポップする）
        ///
        /// 注: 10999（Vrm10Instance前）ではControlRigが上書きするため無効。
        /// 11005（Vrm10Instance後・FastSpringBone前）で実行する必要がある。
        /// </summary>
        public void ApplyPrePassIfNeeded()
        {
            if (_bones == null || _boneCount == 0) return;

            // Blending中: IBオフセット適用済みポーズをSpringBone前に適用
            if (_state == State.Blending)
            {
                // クリーンポーズキャッシュを更新（ControlRig適用後 = 新Timelineの生ポーズ）
                UpdateCleanPoseCache();

                // ブレンド対象ボーンのクリーン値を保存
                for (int i = 0; i < _boneCount; i++)
                {
                    _bones[i].cleanLocalPosition = _bones[i].transform.localPosition;
                    _bones[i].cleanLocalRotation = _bones[i].transform.localRotation;
                }

                // 経過時間を更新してIBオフセットを計算・適用
                // （IB.LateUpdate(20000)と同じ計算をSpringBone前に実行）
                _elapsedTime += Time.deltaTime;
                CalculateAndApplyOffset();

                _prePassApplied = true;
                return;
            }

            // WaitingFirstFrame: 前ポーズを適用（SpringBoneが新ポーズに突然反応しないように）
            if (_state != State.WaitingFirstFrame) return;

            // クリーンポーズキャッシュを更新（ControlRig適用後の値 = 新Timelineのポーズ）
            UpdateCleanPoseCache();

            // ブレンド対象ボーンのクリーン値を保存
            for (int i = 0; i < _boneCount; i++)
            {
                _bones[i].cleanLocalPosition = _bones[i].transform.localPosition;
                _bones[i].cleanLocalRotation = _bones[i].transform.localRotation;
            }

            // 前ポーズを適用（SpringBoneが正しいポーズで計算できるように）
            for (int i = 0; i < _boneCount; i++)
            {
                _bones[i].transform.localPosition = _bones[i].previousLocalPosition;
                _bones[i].transform.localRotation = _bones[i].previousLocalRotation;
            }
            _isOffsetApplied = true;

            _prePassApplied = true;
        }

        /// <summary>
        /// 指定ボーンのクリーンポーズ（Animator出力値）を取得。
        /// LateUpdate(20000)で保存した値を返すため、他コンポーネントが
        /// IB/AO/LookAtオフセット適用前の真のAnimator出力を取得できる。
        /// CharacterLookAtController等がPre-Update Restoreパターンで使用。
        /// </summary>
        public bool TryGetCleanPose(Transform bone, out Vector3 cleanLocalPos, out Quaternion cleanLocalRot)
        {
            if (bone != null && _lastCleanPose.TryGetValue(bone, out var pose))
            {
                cleanLocalPos = pose.localPos;
                cleanLocalRot = pose.localRot;
                return true;
            }
            cleanLocalPos = bone != null ? bone.localPosition : Vector3.zero;
            cleanLocalRot = bone != null ? bone.localRotation : Quaternion.identity;
            return false;
        }

        #endregion

        #region Core Logic

        /// <summary>
        /// 慣性補間の初期化（ローカル座標）
        /// 全ボーンのオフセットを計算
        /// </summary>
        /// <summary>
        /// 角度を-π～+πに正規化
        /// </summary>
        private static float NormalizeAngle(float angle)
        {
            angle = angle % (Mathf.PI * 2f);
            if (angle > Mathf.PI) angle -= Mathf.PI * 2f;
            if (angle < -Mathf.PI) angle += Mathf.PI * 2f;
            return angle;
        }

        /// <summary>
        /// 5次多項式の係数を計算
        /// x(t) = A·t⁵ + B·t⁴ + C·t³ + (a₀/2)·t² + v₀·t + x₀
        /// t=0でx=x₀, t=t₁でx=0 を満たし、速度項v₀で開始時の勢いを引き継ぐ
        /// </summary>
        private static void CalculatePolynomial(float x0, float v0, float blendTime,
            out float t1, out float a0, out float a, out float b, out float c)
        {
            // [TODO] v0がx0と同符号（ターゲットから離れる方向）の場合のオーバーシュート防止
            // v0=0クランプ: 親ボーン（Spine/Neck）の速度もゼロになり子ボーン（Head）が遅れる
            // maxV0=|x0/blendTime|キャップ: x0≈0の遷移（emote→sit_end等）で位置慣性がゼロになる
            // いずれも副作用が大きく、遷移パターンごとの分析を踏まえた別アプローチが必要

            // オーバーシュート防止: v₀とx₀が逆符号の場合、ブレンド時間を短縮
            float timeMax = (v0 == 0f || x0 / v0 > 0f) ? blendTime : -5f * x0 / v0;
            t1 = Mathf.Min(blendTime, timeMax);

            if (t1 <= 0f)
            {
                a0 = a = b = c = 0f;
                return;
            }

            float t1_2 = t1 * t1;
            float t1_3 = t1_2 * t1;
            float t1_4 = t1_3 * t1;

            a0 = (-8f * v0 * t1 - 20f * x0) / t1_2;
            a = -(a0 * t1_2 + 6f * v0 * t1 + 12f * x0) / (2f * t1_4 * t1);
            b = (3f * a0 * t1_2 + 16f * v0 * t1 + 30f * x0) / (2f * t1_4);
            c = -(3f * a0 * t1_2 + 12f * v0 * t1 + 20f * x0) / (2f * t1_3);
        }

        /// <summary>
        /// 5次多項式を評価（t時点でのオフセット値を返す）
        /// </summary>
        private static float EvaluatePolynomial(float t, float t1,
            float x0, float v0, float a0, float a, float b, float c)
        {
            float tc = Mathf.Min(t, t1);
            float t2 = tc * tc;
            float t3 = t2 * tc;
            float t4 = t3 * tc;
            float t5 = t4 * tc;
            return a * t5 + b * t4 + c * t3 + a0 * t2 * 0.5f + v0 * tc + x0;
        }

        private void InitializeBlend()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) dt = 1f / 60f; // フォールバック

            for (int i = 0; i < _boneCount; i++)
            {
                var bone = _bones[i].transform;

                // --- 位置 ---
                Vector3 vec = _bones[i].previousLocalPosition - _bones[i].cleanLocalPosition;
                float posX0 = vec.magnitude;

                if (posX0 > Mathf.Epsilon)
                {
                    _bones[i].posBaseVec = vec / posX0; // normalized
                    // 2フレーム前のポーズから速度を計算（StartInertialBlend時に保存済み）
                    float xn1 = 0f;
                    if (_bones[i].hasPrevPrev)
                    {
                        xn1 = Vector3.Dot(_bones[i].prevPrevLocalPosition - _bones[i].cleanLocalPosition, _bones[i].posBaseVec);
                    }
                    float posV0 = (posX0 - xn1) / dt;
                    CalculatePolynomial(posX0, posV0, _blendDuration,
                        out _bones[i].posT1, out _bones[i].posA0,
                        out _bones[i].posA, out _bones[i].posB, out _bones[i].posC);
                    _bones[i].posX0 = posX0;
                    _bones[i].posV0 = posV0;
                }
                else
                {
                    _bones[i].posX0 = 0f;
                    _bones[i].posBaseVec = Vector3.zero;
                    _bones[i].posV0 = _bones[i].posT1 = _bones[i].posA0 = 0f;
                    _bones[i].posA = _bones[i].posB = _bones[i].posC = 0f;
                }

                // --- 回転 ---
                Quaternion invRot = Quaternion.Inverse(_bones[i].cleanLocalRotation);
                Quaternion q0 = invRot * _bones[i].previousLocalRotation;
                q0.ToAngleAxis(out float angleDeg, out Vector3 axis);
                float rotX0 = NormalizeAngle(angleDeg * Mathf.Deg2Rad);

                if (Mathf.Abs(rotX0) > Mathf.Epsilon)
                {
                    _bones[i].rotAxis = axis;
                    // 2フレーム前のポーズから角速度を計算（StartInertialBlend時に保存済み）
                    float xn1 = 0f;
                    if (_bones[i].hasPrevPrev)
                    {
                        Quaternion qn1 = invRot * _bones[i].prevPrevLocalRotation;
                        if (Mathf.Abs(qn1.w) > Mathf.Epsilon)
                        {
                            Vector3 qVec = new Vector3(qn1.x, qn1.y, qn1.z);
                            xn1 = 2f * Mathf.Atan(Vector3.Dot(qVec, axis) / qn1.w);
                            xn1 = NormalizeAngle(xn1);
                        }
                    }
                    float deltaAngle = NormalizeAngle(rotX0 - xn1);
                    float rotV0 = deltaAngle / dt;
                    CalculatePolynomial(rotX0, rotV0, _blendDuration,
                        out _bones[i].rotT1, out _bones[i].rotA0,
                        out _bones[i].rotA, out _bones[i].rotB, out _bones[i].rotC);
                    _bones[i].rotX0 = rotX0;
                    _bones[i].rotV0 = rotV0;
                }
                else
                {
                    _bones[i].rotX0 = 0f;
                    _bones[i].rotAxis = Vector3.up;
                    _bones[i].rotV0 = _bones[i].rotT1 = _bones[i].rotA0 = 0f;
                    _bones[i].rotA = _bones[i].rotB = _bones[i].rotC = 0f;
                }
            }

            Debug.Log($"[InertialBlendHelper] InitializeBlend - " +
                $"posX0={_bones[0].posX0:F4}, rotX0={_bones[0].rotX0 * Mathf.Rad2Deg:F2}°, " +
                $"posV0={_bones[0].posV0:F2}, rotV0={_bones[0].rotV0 * Mathf.Rad2Deg:F1}°/s, " +
                $"frame={Time.frameCount}");
        }

        /// <summary>
        /// 5次多項式によるオフセットを計算して適用
        /// </summary>
        private void CalculateAndApplyOffset()
        {
            float t = _elapsedTime;

            for (int i = 0; i < _boneCount; i++)
            {
                // 位置オフセット
                float posVal = EvaluatePolynomial(t, _bones[i].posT1,
                    _bones[i].posX0, _bones[i].posV0, _bones[i].posA0,
                    _bones[i].posA, _bones[i].posB, _bones[i].posC);
                Vector3 posOffset = posVal * _bones[i].posBaseVec;

                // 回転オフセット（軸角度表現）
                float rotVal = EvaluatePolynomial(t, _bones[i].rotT1,
                    _bones[i].rotX0, _bones[i].rotV0, _bones[i].rotA0,
                    _bones[i].rotA, _bones[i].rotB, _bones[i].rotC);
                Quaternion rotOffset = Quaternion.AngleAxis(rotVal * Mathf.Rad2Deg, _bones[i].rotAxis);

                // オフセットを適用（ローカル座標）
                _bones[i].transform.localPosition = _bones[i].cleanLocalPosition + posOffset;
                _bones[i].transform.localRotation = _bones[i].cleanLocalRotation * rotOffset;
            }
            _isOffsetApplied = true;
        }

        #endregion
    }
}
