using UnityEngine;
using System.Collections.Generic;

namespace CyanNook.Core
{
    /// <summary>
    /// キャラクターテンプレートの定義データ
    /// アニメーションセットとキャラクター設定を管理する
    /// </summary>
    [CreateAssetMenu(fileName = "CharacterTemplate_", menuName = "CyanNook/Character Template")]
    public class CharacterTemplateData : ScriptableObject
    {
        [Header("Basic Info")]
        [Tooltip("キャラクターテンプレートID (chr001 など)")]
        public string templateId;

        [Tooltip("キャラクター名")]
        public string characterName;

        [TextArea(3, 6)]
        [Tooltip("キャラクター説明（LLMプロンプト用）")]
        public string characterDescription;

        [Header("Body Type")]
        [Tooltip("体型タイプ")]
        public BodyType bodyType = BodyType.Standard;

        [Header("Animation (Timeline)")]
        [Tooltip("アニメーションプレフィックス (chr001_anim_)")]
        public string animationPrefix;

        [Tooltip("Timelineバインディングデータ（ステートごとのTimeline設定）")]
        public ScriptableObject timelineBindings; // TimelineBindingDataを参照

        [Header("Default VRM")]
        [Tooltip("デフォルトVRMファイル名（StreamingAssets/VRM/以下）")]
        public string defaultVrmFileName;

        [Header("Tool Points")]
        [Tooltip("左手ツールポイントのオフセット位置")]
        public Vector3 toolPointLeftOffset = new Vector3(0.05f, 0, 0);

        [Tooltip("左手ツールポイントのオフセット回転")]
        public Vector3 toolPointLeftRotation = Vector3.zero;

        [Tooltip("右手ツールポイントのオフセット位置")]
        public Vector3 toolPointRightOffset = new Vector3(-0.05f, 0, 0);

        [Tooltip("右手ツールポイントのオフセット回転")]
        public Vector3 toolPointRightRotation = Vector3.zero;

        /// <summary>
        /// アニメーションIDにプレフィックスを付与
        /// </summary>
        public string GetFullAnimationId(string shortId)
        {
            return $"{animationPrefix}{shortId}";
        }
    }

    public enum BodyType
    {
        Standard,      // 標準体型
        MaleTall,      // 男性高身長
        MaleShort,     // 男性低身長
        FemaleTall,    // 女性高身長
        FemaleShort    // 女性低身長
    }
}
