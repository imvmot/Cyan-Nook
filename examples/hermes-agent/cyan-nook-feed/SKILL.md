---
name: cyan-nook-feed
description: Cyan-Nook の 3D キャラクターを外部アクションフィード経由で動かす。部屋のアバターに行動・表情・発話をさせたいとき、cron でキャラクターの自律行動を決めるとき、会話への反応を部屋のアバターに反映したいときに使う。action JSON を組み立てて PocketBase に curl で PUT する。
---

# Cyan-Nook アクションフィード投稿スキル

Cyan-Nook（Unity 製 3D キャラクター表示アプリ）は、フィードサーバー上の action JSON を数秒間隔で購読し、
受信した JSON に従ってキャラクターが移動・表情変化・発話（TTS 読み上げ）する。
このスキルは「キャラクターの次の行動」を JSON で書き、フィードサーバーへ PUT する手順を定める。
（フィードサーバーは nginx ベースの単純なファイル置き場。構築手順は Cyan-Nook リポジトリの examples/feed-server/ 参照）

## エンドポイント

ベース URL は環境ごとに設定する（以下は例。実際の URL は MEMORY.md や環境設定を参照）:

- 状況の取得: `GET http://<feed-host>:8093/feed/context.json`
- 視界画像の取得: `GET http://<feed-host>:8093/feed/camera.jpg` (image/jpeg)
- 行動の投稿: `PUT http://<feed-host>:8093/feed/action.json`

## 手順

1. **状況を確認する**（推奨）:

   ```bash
   curl -sf http://<feed-host>:8093/feed/context.json
   ```

   - `is_sleeping` / `is_outside` が true の間、投稿した行動は適用されない（アプリ側が保留し、起床/帰宅後に適用される）。古い行動が後から発火するのを避けたいなら、この間は投稿を控える
   - `spatial_context` と `visible_objects` にキャラクターの現在位置・視界情報がある。行動決定の材料にする
   - キャラクター視点の画像が必要なら `/feed/camera.jpg` を取得して見る

2. **action JSON を組み立てる**。下のテンプレートの構造・キー名を厳守し、一時ファイルに書く。
   JSON オブジェクト単体のみ（前後に説明文・Markdown コードフェンス禁止）:

   ```json
   {
     "timestamp": "2026-01-01T12:00:00+09:00",
     "emotion": {
       "happy": 0.6,
       "relaxed": 0.2,
       "angry": 0.0,
       "sad": 0.0,
       "surprised": 0.0
     },
     "reaction": "",
     "action": "move",
     "target": {
       "type": "dynamic",
       "clock": 10,
       "distance": "mid",
       "height": "mid"
     },
     "emote": "happy01",
     "sleep_duration": 0,
     "message": "窓の外、今日は静かだね。"
   }
   ```

3. **PUT する**:

   ```bash
   curl -sf -X PUT -H "Content-Type: application/json" \
     --data-binary @/tmp/cyan_action.json \
     http://<feed-host>:8093/feed/action.json
   ```

## フィールド仕様（許容値以外を書かない）

| フィールド | 値 |
|---|---|
| `timestamp` | 現在時刻 (ISO 8601)。**毎回必ず変える**。アプリは「前回と同一の生 JSON」を無視するため、これが再適用のトリガーになる |
| `emotion.*` | 0.0〜1.0 の数値 5 種 (happy / relaxed / angry / sad / surprised)。表情と仕草に反映される |
| `reaction` | 短い相槌 (省略可、`""` でよい)。message と合わせて読み上げられる |
| `action` | `move` / `interact_sit` / `interact_sleep` / `interact_exit` / `ignore` のいずれか。**`thinking` は予約済みの特別値**（下記注意参照）で、通常の行動としては使わない |
| `target.type` | `talk` (ユーザーの方へ来る) / `dynamic` (clock 等で座標指定) / `mirror` / `screencapture` (部屋の名所) / `interact_sit` (椅子) / `interact_sleep` (ベッド) |
| `target.clock` | 1〜12 (dynamic 時のみ。キャラ基準の方角、12=正面) |
| `target.distance` | `near` / `mid` / `far` (dynamic 時のみ) |
| `target.height` | `high` / `mid` / `low` (dynamic 時のみ。視線の高さ) |
| `emote` | `Neutral` / `happy01` / `relaxed01` / `angry01` / `sad01` / `surprised01` |
| `sleep_duration` | 睡眠時間 (分)。`action` が `interact_sleep` の時のみ意味を持つ。それ以外は 0 |
| `message` | 発話本文。**TTS で読み上げられるので日本語 1〜2 文の短い独り言・語りかけにする**。チャット返信の全文コピーは長すぎるので要約する |

## 行動の使い分け

- 何もさせたくない・待機: `action: "ignore"`（message だけ喋らせることも可能）
- 部屋の中を移動: `action: "move"` + `target.type: "dynamic"` か `"mirror"` / `"screencapture"`
- 椅子に座る: `action: "interact_sit"` + `target.type: "interact_sit"`
- 寝る: `action: "interact_sleep"` + `target.type: "interact_sleep"` + `sleep_duration` (分)。睡眠中は以降の投稿が保留される
- **`interact_exit` は部屋から退出する（外出）**。外出中は帰宅まで行動を受け付けなくなるため、明確な意図がある時だけ使う

## 注意

- JSON は必ずオブジェクト単体。`{` で始まらないレスポンスや壊れた JSON はアプリ側で破棄され、キャラクターは無反応になる（エラーは返らない）
- emotion と emote は矛盾させない（例: happy が最大なら emote は `happy01` か `Neutral`）
- アプリが応答処理中・睡眠中・外出中の投稿は即時適用されず、状態が解けた時に最新の 1 件だけが適用される
- **`action: "thinking"` は「考え中」演出専用の予約値**。推論を始める側（音声リスナー等）が
  `{"action":"thinking","timestamp":"..."}` を PUT すると、次の本応答が届くまでキャラクターが考え中モーションをする
  （本応答が来ない場合は一定時間で自動解除）。**最終的な応答の action に `thinking` を書いてはいけない**
  （本応答が来ないためキャラクターが考え込んだままタイムアウトまで固まる）
