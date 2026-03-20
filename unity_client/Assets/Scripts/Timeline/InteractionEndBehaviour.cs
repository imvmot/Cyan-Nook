using UnityEngine.Playables;

namespace CyanNook.Timeline
{
    /// <summary>
    /// InteractionEnd PlayableBehaviour
    /// 処理なし。InteractionEndClipのCreatePlayableに必要な構造上の定義。
    /// 実際の完了検知はCharacterAnimationControllerがクリップの位置情報を読み取って行う。
    /// </summary>
    [System.Serializable]
    public class InteractionEndBehaviour : PlayableBehaviour
    {
    }
}
