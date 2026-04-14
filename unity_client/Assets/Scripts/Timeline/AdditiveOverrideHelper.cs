using System.Collections.Generic;
using UnityEngine;

namespace CyanNook.Timeline
{
    /// <summary>
    /// 加算ボーンオーバーライド用ヘルパー。
    /// 指定ボーン（additiveBones）以外のボーンを、スナップショット時点のポーズに毎フレーム復元する。
    ///
    /// 使用方法:
    /// 1. VRMインスタンスにこのコンポーネントをアタッチ
    /// 2. StartOverride(additiveBones) でスナップショット取得・開始
    /// 3. PlayState()でTimelineを切り替え → 全ボーンにEmote/Thinkingポーズが適用される
    /// 4. LateUpdateで additiveBones以外のボーンをスナップショットに復元
    /// 5. StopOverride() で終了
    ///
    /// アーキテクチャ:
    /// 1. StartOverride(): additiveBones以外の全ボーンのローカル座標をスナップショット
    /// 2. Animator: 新Timelineのポーズを全ボーンに適用
    /// 3. LateUpdate: additiveBones以外のボーンをスナップショットに復元
    ///
    /// ボーン参照はAwake時に全Humanoidボーンをキャッシュし、
    /// StartOverride時にキャッシュから取得する（GetBoneTransformの再呼び出しを回避）
    /// </summary>
    // InertialBlendHelper(20000)の後に実行し、非加算ボーンをスナップショットに復元する。
    // InertialBlendHelperがオフセット適用した後にAdditiveOverrideが復元することで、
    // 非加算ボーンは正しくスナップショット値に戻り、加算ボーンのみInertialBlendが効く。
    [DefaultExecutionOrder(20050)]
    public class AdditiveOverrideHelper : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("対象のAnimator")]
        public Animator animator;

        [Tooltip("AO停止時にクリーンポーズキャッシュを無効化するためのIB参照")]
        public InertialBlendHelper inertialBlendHelper;

        [Header("Debug")]
        [SerializeField]
        private bool _isActive;
        public bool IsActive => _isActive;

        // 現在の加算ボーンリスト（StartOverride時に保持、StopOverride時にクリア）
        private List<HumanBodyBones> _currentAdditiveBones;
        /// <summary>
        /// 現在の加算ボーンリスト（AdditiveCancelClipから参照される）
        /// </summary>
        public IReadOnlyList<HumanBodyBones> CurrentAdditiveBones => _currentAdditiveBones;

        // Awakeで全Humanoidボーンの参照をキャッシュ
        private Dictionary<HumanBodyBones, Transform> _boneTransformCache;

        // 復元対象ボーン（additiveBones以外）のスナップショット
        private struct BoneSnapshot
        {
            public Transform transform;
            public Vector3 localPosition;
            public Quaternion localRotation;
        }

        private BoneSnapshot[] _restoreBones;
        private int _restoreCount;

        private void Awake()
        {
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            // 全HumanoidボーンのTransformをキャッシュ
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
            }
        }

        /// <summary>
        /// 加算ボーンオーバーライドを開始。
        /// additiveBones以外の全ボーンの現在ローカル座標をスナップショットとして保存する。
        /// PlayState()でTimelineを切り替える前に呼び出すこと。
        /// </summary>
        /// <param name="additiveBones">Emote/Thinkingで上書きするボーンリスト</param>
        public void StartOverride(List<HumanBodyBones> additiveBones)
        {
            if (_boneTransformCache == null || _boneTransformCache.Count == 0)
            {
                Debug.LogWarning("[AdditiveOverrideHelper] Bone transform cache is empty");
                return;
            }

            if (additiveBones == null || additiveBones.Count == 0)
            {
                Debug.LogWarning("[AdditiveOverrideHelper] additiveBones is empty, skipping override");
                return;
            }

            var additiveBoneSet = new HashSet<HumanBodyBones>(additiveBones);

            // additiveBones以外のボーンをスナップショット対象として構築
            var restoreList = new List<BoneSnapshot>();
            foreach (var kvp in _boneTransformCache)
            {
                if (!additiveBoneSet.Contains(kvp.Key))
                {
                    restoreList.Add(new BoneSnapshot
                    {
                        transform = kvp.Value,
                        localPosition = kvp.Value.localPosition,
                        localRotation = kvp.Value.localRotation
                    });
                }
            }

            _restoreBones = restoreList.ToArray();
            _restoreCount = restoreList.Count;
            _currentAdditiveBones = new List<HumanBodyBones>(additiveBones);
            _isActive = true;

            Debug.Log($"[AdditiveOverrideHelper] StartOverride: additiveBones={additiveBones.Count}, restoreBones={_restoreCount}");
        }

        /// <summary>
        /// 加算ボーンオーバーライドを停止。
        /// 以降のLateUpdateでの復元処理を行わない。
        /// </summary>
        /// <param name="invalidateCleanPose">
        /// trueの場合、IBのクリーンポーズキャッシュを無効化してv₀=0フォールバックを強制する。
        /// AdditiveCancelClip経由で停止する場合は false を指定し、
        /// 現在の加算込みポーズをIBの「補間元」として流用する。
        /// </param>
        public void StopOverride(bool invalidateCleanPose = true)
        {
            if (!_isActive) return;

            _isActive = false;
            _restoreBones = null;
            _restoreCount = 0;
            _currentAdditiveBones = null;

            if (invalidateCleanPose)
            {
                // AO動作中、IBのクリーンポーズキャッシュにはAO復元前のポーズ
                // （Thinking/Emoteの全身ポーズ）が記録されている。
                // AO停止後の次回IB開始時にこの不連続が偽v₀として検出されるため、
                // _prevCleanPoseを無効化してv₀=0フォールバックを強制する。
                inertialBlendHelper?.InvalidatePrevCleanPose();
            }

            Debug.Log($"[AdditiveOverrideHelper] StopOverride (invalidateCleanPose={invalidateCleanPose})");
        }

        /// <summary>
        /// LateUpdate: Animatorが新Timelineのポーズを全ボーンに書き込んだ後、
        /// additiveBones以外のボーンをスナップショットに復元する。
        /// </summary>
        private void LateUpdate()
        {
            if (!_isActive || _restoreBones == null) return;
            ApplyRestore();
        }

        /// <summary>
        /// 復元処理を即時実行する（LateUpdateを待たずに手動で呼ぶ用）。
        /// AdditiveCancel経由でSnapshotCurrentPoseAsCleanする前に呼ぶと、
        /// 「実際に画面に見えていたポーズ（下半身スナップショット＋上半身加算）」を
        /// IBの補間元として正しくキャプチャできる。
        /// </summary>
        public void ApplyRestoreNow()
        {
            if (!_isActive || _restoreBones == null) return;
            ApplyRestore();
        }

        private void ApplyRestore()
        {
            for (int i = 0; i < _restoreCount; i++)
            {
                _restoreBones[i].transform.localPosition = _restoreBones[i].localPosition;
                _restoreBones[i].transform.localRotation = _restoreBones[i].localRotation;
            }
        }
    }
}
