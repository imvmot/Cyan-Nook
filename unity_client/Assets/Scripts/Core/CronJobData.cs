using System;
using UnityEngine;

namespace CyanNook.Core
{
    /// <summary>
    /// StreamingAssets/cron/ 内のJSONファイルから読み込むCronジョブ定義
    /// </summary>
    [Serializable]
    public class CronJobData
    {
        [Tooltip("ジョブの一意識別子")]
        public string id;

        [Tooltip("表示用名称（ログ出力用）")]
        public string name;

        [Tooltip("ジョブ単位の有効/無効")]
        public bool enabled = true;

        [Tooltip("cron式（5フィールド: 分 時 日 月 曜日）")]
        public string schedule;

        [Tooltip("LLMに送信するプロンプト")]
        public string prompt;

        [Tooltip("Sleep/Outingをキャンセルして実行する（false=スキップ）")]
        public bool cancelSleepOrOuting = false;
    }
}
