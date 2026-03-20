using UnityEngine.Playables;

namespace CyanNook.Timeline
{
    /// <summary>
    /// LoopRegion PlayableBehaviour
    /// 処理なし。LoopRegionClipのCreatePlayableに必要な構造上の定義。
    /// 実際のループ制御はCharacterAnimationControllerがクリップの位置情報を読み取って行う。
    /// </summary>
    [System.Serializable]
    public class LoopRegionBehaviour : PlayableBehaviour
    {
    }
}
