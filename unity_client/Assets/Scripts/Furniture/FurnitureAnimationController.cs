using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace CyanNook.Furniture
{
    /// <summary>
    /// 家具のアニメーション再生を制御
    /// FurnitureInstanceと同じGameObjectにアタッチ
    /// キャラクターのインタラクションアニメーションと同期して再生する
    /// </summary>
    [RequireComponent(typeof(FurnitureInstance))]
    public class FurnitureAnimationController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("家具のPlayableDirector")]
        public PlayableDirector director;

        [Tooltip("家具のAnimator（FBXモデル等にアタッチ）")]
        public Animator animator;

        [Header("Timeline Bindings")]
        [Tooltip("家具用TimelineBindingData")]
        public FurnitureTimelineBindingData timelineBindings;

        [Header("State")]
        [SerializeField]
        private bool _isPlaying;
        public bool IsPlaying => _isPlaying;

        [SerializeField]
        private string _currentAnimationId;
        public string CurrentAnimationId => _currentAnimationId;

        /// <summary>
        /// 再生完了イベント
        /// </summary>
        public event System.Action<string> OnPlaybackComplete;

        // ルートボーン位置復元用
        private Vector3 _savedAnimatorLocalPos;
        private Quaternion _savedAnimatorLocalRot;

        private void Awake()
        {
            if (director == null)
            {
                director = GetComponent<PlayableDirector>();
            }
        }

        private void OnEnable()
        {
            if (director != null)
            {
                director.stopped += OnDirectorStopped;
            }
        }

        private void OnDisable()
        {
            if (director != null)
            {
                director.stopped -= OnDirectorStopped;
            }
        }

        /// <summary>
        /// 指定アニメーションIDに対応する家具Timelineを再生
        /// キャラクター側のPlayStateと同じフレームで呼ぶことでフレーム同期する
        /// </summary>
        /// <param name="animationId">キャラクター側のアニメーションID（interact_exit01 など）</param>
        /// <returns>再生を開始できたらtrue</returns>
        public bool Play(string animationId)
        {
            if (timelineBindings == null)
            {
                Debug.LogWarning($"[FurnitureAnimationController] TimelineBindings not assigned on {gameObject.name}");
                return false;
            }

            var timeline = timelineBindings.GetTimeline(animationId);
            if (timeline == null)
            {
                Debug.Log($"[FurnitureAnimationController] No timeline for animationId: {animationId} on {gameObject.name}");
                return false;
            }

            // 既に再生中なら停止
            if (_isPlaying)
            {
                Stop();
            }

            // Timeline設定
            director.playableAsset = timeline;

            // AnimationTrackにAnimatorをバインド（ApplySceneOffsetsで位置ズレ防止）
            BindAnimatorToTimeline(timeline);

            // 家具アニメーションは最終フレームで停止（Hold）
            // キャラクターより短い場合は最終ポーズを維持する
            director.extrapolationMode = DirectorWrapMode.Hold;
            director.time = 0;
            director.Play();

            _isPlaying = true;
            _currentAnimationId = animationId;

            Debug.Log($"[FurnitureAnimationController] Playing: {animationId}, Timeline: {timeline.name} on {gameObject.name}");
            return true;
        }

        /// <summary>
        /// 再生を停止
        /// </summary>
        public void Stop()
        {
            if (director != null && _isPlaying)
            {
                director.Stop();
                // OnDirectorStoppedでクリーンアップされる
            }
        }

        /// <summary>
        /// キャラクター側のタイムライン終了に連動して停止する
        /// 家具のタイムラインがキャラクターより長い場合に使用
        /// </summary>
        public void StopWithCharacter()
        {
            if (_isPlaying)
            {
                Debug.Log($"[FurnitureAnimationController] Stopped with character: {_currentAnimationId} on {gameObject.name}");
                Stop();
            }
        }

        /// <summary>
        /// 指定アニメーションIDに対応するタイムラインが存在するか
        /// </summary>
        public bool HasTimeline(string animationId)
        {
            return timelineBindings != null && timelineBindings.HasBinding(animationId);
        }

        /// <summary>
        /// 再生中、Animatorのルート位置をTimelineが書き換えるのを毎フレーム上書きで元に戻す
        /// 子ボーン（ドアの回転等）のアニメーションは影響を受けない
        /// </summary>
        private void LateUpdate()
        {
            if (!_isPlaying || animator == null) return;

            animator.transform.localPosition = _savedAnimatorLocalPos;
            animator.transform.localRotation = _savedAnimatorLocalRot;
        }

        /// <summary>
        /// AnimationTrackにAnimatorをバインド
        /// </summary>
        private void BindAnimatorToTimeline(TimelineAsset timeline)
        {
            if (animator == null || director == null) return;

            // 再生前のAnimatorローカル座標を保存（LateUpdateで復元する）
            _savedAnimatorLocalPos = animator.transform.localPosition;
            _savedAnimatorLocalRot = animator.transform.localRotation;

            foreach (var track in timeline.GetOutputTracks())
            {
                if (track is AnimationTrack animTrack)
                {
                    director.SetGenericBinding(track, animator);
                }
            }
        }

        private void OnDirectorStopped(PlayableDirector stoppedDirector)
        {
            if (stoppedDirector != director) return;

            string completedId = _currentAnimationId;
            _isPlaying = false;
            _currentAnimationId = null;

            OnPlaybackComplete?.Invoke(completedId);
        }
    }
}
