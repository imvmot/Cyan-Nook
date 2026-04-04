# Cyan-Nook - Project Design Document

## Overview

Unity WebGLベースのチャットボットアプリケーション。
VRM1.0キャラクターが箱庭空間で自律行動し、ユーザーとの会話も可能。

## Target

- プラットフォーム: WebGL (Windows PC向け)
- 配布形態: itch.io + ローカルHTML配布
- ターゲットユーザー: PCゲーマー、VRChatユーザー層

---

## System Architecture

```
┌─────────────────────────────────────────────────────────────┐
│ Local LLM (Ollama / LM Studio)       or  Cloud API         │
│ localhost:11434                       (OpenAI/Claude/Gemini)│
└─────────────────────────┬───────────────────────────────────┘
                          │ HTTP (JSON)
                          ▼
┌─────────────────────────────────────────────────────────────┐
│ Unity WebGL                                                 │
│ ┌─────────────────────────────────────────────────────────┐ │
│ │ LLMClient                - LLM通信統合（ILLMProvider）  │ │
│ │ ILLMProvider             - LLMプロバイダーIF            │ │
│ │ OllamaProvider           - Ollama/LM Studio HTTP通信   │ │
│ │ DifyProvider             - Dify Chat Messages API通信  │ │
│ │ WebLLMProvider           - WebLLM（ブラウザ内LLM via WebGPU）│ │
│ │ WebLLMBridge             - WebLLM jslib C#ブリッジ     │ │
│ │ LLMConfigManager         - API設定保存（PlayerPrefs）   │ │
│ │ LlmStreamHandler         - ストリーミングDownloadHandler│ │
│ │ StreamSeparatorProcessor - ヘッダー/本文分離処理        │ │
│ │ IncrementalJsonFieldParser - JSON逐次フィールドパーサー│ │
│ │ VisibleObjectsProvider   - 視界内オブジェクト検出      │ │
│ │ SceneObjectDescriptor    - オブジェクト説明コンポーネント│ │
│ │ ChatManager              - プロンプト生成、会話履歴管理 │ │
│ │ CharacterController      - キャラクター統合制御         │ │
│ │ DynamicTargetController  - 動的ターゲット(clock/距離)  │ │
│ │ RoomTargetManager        - 名前付きターゲット管理      │ │
│ │ CharacterAnimationController - Timeline/PlayableDirector│ │
│ │ CharacterExpressionController- VRM Expression制御(Facial Timeline/直接制御)│ │
│ │ CharacterLookAtController    - 視線制御(Eye/Head/Chest)  │ │
│ │ CharacterCameraController    - 視点カメラ（Vision）     │ │
│ │ CharacterFaceLightController - 顔ライト（Headボーン追従）│ │
│ │ CharacterNavigationController- 移動制御                 │ │
│ │ InteractionController    - インタラクション状態管理     │ │
│ │ TalkController           - Talkモード状態管理           │ │
│ │ FurnitureManager         - 家具管理                     │ │
│ │ RoomLightController      - ライトON/OFF + Emission + Lightmap連動│ │
│ │ SleepController          - 睡眠状態管理・夢タイマー・起床処理│ │
│ │ OutingController         - 外出状態管理・入退室アニメーション│ │
│ │ FurnitureAnimationController - 家具連動アニメーション再生│ │
│ │ AutonomousBehavior       - 自律行動AI (構想のみ・未着手) │ │
│ │ CharacterSetup           - VRM読み込み・全コンポーネント初期化・RoomTargetワイヤリング・Rendering Layer Mask/Culling Layer設定│ │
│ │ UIController             - チャット入出力・ストリーミング・マイクボタン│ │
│ │ SettingsMenuController   - アイコンメニュー・パネル開閉 │ │
│ │ AvatarSettingsPanel      - アバター設定パネル            │ │
│ │ LLMSettingsPanel         - LLM設定パネル                │ │
│ │ VoiceSettingsPanel       - 音声設定パネル (TTS+STT)      │ │
│ │ WebSpeechSynthesis       - Web Speech TTS C#ラッパー    │ │
│ │ WebSpeechRecognition     - Web Speech STT C#ラッパー    │ │
│ │ VoiceInputController     - 音声入力統合管理             │ │
│ │ VoiceActivityDetector    - 無音検出・自動送信           │ │
│ │ FirstRunController       - 初回起動ポップアップ・WebLLMダウンロード進捗│ │
│ │ DebugSettingsPanel       - デバッグ設定パネル            │ │
│ │ SettingsExporter         - 設定JSON Import/Export       │ │
│ │ MultiLineInputFieldFix  - マルチラインInputField改行修正│ │
│ │ DebugKeyController       - デバッグキー一括管理         │ │
│ │ StatusOverlay            - ステータスオーバーレイ(FPS等) │ │
│ │ FrameRateLimiter         - フレームレート制限           │ │
│ └─────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

---

## JSON Schema (LLM Response)

```json
{
  "emotion": {
    "happy": 0.0-1.0,
    "relaxed": 0.0-1.0,
    "angry": 0.0-1.0,
    "sad": 0.0-1.0,
    "surprised": 0.0-1.0
  },
  "target": {
    "type": "talk | interact_sit | interact_sleep | interact_exit | dynamic | {room_target_name}",
    "clock": 10,
    "distance": "near | mid | far",
    "height": "high | mid | low"
  },
  "reaction": "短い相槌（省略可）",
  "action": "move | interact_sit | interact_sleep | interact_exit | ignore",
  "emote": "Neutral | happy01 | relaxed01 | angry01 | sad01 | surprised01",
  "sleep_duration": 30,
  "message": "メッセージ本文（省略可）"
}
```

### フィールド説明

| Field | Required | Description |
|-------|----------|-------------|
| emotion | No | 表情パラメータ（VRM Expression） |
| reaction | No | 短い相槌・リアクション（即座に表示される） |
| target | No | LookAt / 移動ターゲット |
| target.type | Yes* | talk / interact_* / dynamic / {room_target_name} |
| target.clock | No | 方向（1-12、キャラ基準。dynamic時のみ） |
| target.distance | No | 距離プリセット（near/mid/far。dynamic時のみ） |
| target.height | No | 高さプリセット（high/mid/low。dynamic時のみ） |
| action | No | 行動指示（"ignore"=何もしない） |
| emote | No | 感情モーション（"Neutral"=再生しない） |
| sleep_duration | No | 睡眠時間（分）。action が `interact_sleep` の時のみ有効。未指定時はデフォルト値を使用 |
| message | No | メイン本文（ストリーミング表示される。JSONの最後に配置推奨） |

**推奨フィールド順序**: emotion → reaction → action → target → emote → message。
reaction は即座表示のため前方に、message はストリーミング表示のため最後に配置する。
ただしJSONフィールドの順序は任意で、どの順序でも正しくパースされる。

**`character` フィールドの廃止**: 以前のスキーマに存在した `character` フィールドは設定パネルで管理するため廃止。
内部的にはデフォルト値（`LLMResponseData.DefaultCharacterId`）が使用される。

**target.type の `player` → `talk` 変更**: 旧 `"player"` は `"talk"` にリネーム。
RoomTargets 階層の `talk` ターゲットとして管理される。後方互換のため `"player"` も `TargetType.Talk` として扱われる。

### action × target マトリクス

`action` が移動/行動を決定し、`target.type` が LookAt を決定する。
`action:interact_*` + `target.type:talk/dynamic/{name}` の場合は LookAt のみ target を反映。

| action | target.type | 移動先 | LookAt |
|--------|-------------|--------|--------|
| move | talk | RoomTarget "talk" 位置 → Talk状態 | talk_lookattarget |
| move | interact_sit | sit家具付近（座らない） | 家具lookattarget |
| move | dynamic | DynamicTarget位置 | DynamicTarget |
| move | {name} | RoomTarget位置 | RoomTarget lookattarget |
| interact_sit | talk | sit家具 → 座る（※1） | talk_lookattarget |
| interact_sit | interact_sit | sit家具 → 座る（※1） | 家具lookattarget |
| interact_sit | dynamic | sit家具 → 座る（※1） | DynamicTarget |
| interact_sit | {name} | sit家具 → 座る（※1） | RoomTarget lookattarget |
| ignore | talk | （移動なし） | talk_lookattarget |
| ignore | interact_sit | （移動なし） | 最寄りsit家具lookattarget |
| ignore | dynamic | （移動なし） | DynamicTarget |
| ignore | {name} | （移動なし） | RoomTarget lookattarget |

※1 家具選択ロジック:
- 通常時: 最寄りの空き家具を選択
- 同種インタラクション中（例: sit中にsit）: 現在の家具を除外して別の家具をランダム選択。候補が1つしかない場合は同じ家具で再インタラクト
- この除外ロジックはストリーミング逐次反映パス（`ProcessActionFromField`）と最終レスポンスパス（`ProcessAction`）の両方で動作する

### Talk状態のトリガー

`action:"move"` + `target.type:"talk"` が Talk 状態のトリガー。
RoomTarget "talk" 位置へ移動完了後に Talk 状態に遷移する。

#### Talk状態と他actionの連携

- Talk中に非talkアクション（`move+dynamic`, `move+interact_*`, `move+{name}`, `interact_*`）が来た場合、`ForceExitTalk()` でTalkを即時終了してから新しいアクションを実行
- Talk中に `move+talk` が来た場合:
  - talk位置付近にいれば何もしない（正常状態）
  - talk位置から離れている場合は `ForceExitTalk()` → `EnterTalk()` で再接近

### speak 判定

`speak` フィールドは廃止。`reaction` または `message` の有無で判定：
- いずれかにテキストあり → 会話表示、履歴追加（`FullMessage` = reaction + "\n" + message）
- 両方とも省略 → 会話なし
- emote / emotion はテキスト有無に関係なく常に反映

### emote 再生条件

Timeline 上の `EmotePlayableTrack` の Clip が存在する期間中のみ emote 再生可能。
"Neutral" の場合は何も再生せず現在のモーションを維持。
emote 再生完了後は直前のステート（idle / talk_idle 等）に自動復帰する。

EmotePlayableClip の `additiveBones` が設定されている場合は加算モードで再生される。
指定ボーンのみ Emote アニメーションで上書きし、それ以外のボーンはベースアニメーションを維持する。
（例：interact_sit の lp 区間に上半身ボーンを設定 → 座ったまま上半身のみ Emote 再生）

Thinking 再生中に emote リクエストが来た場合は `ForceStopThinking()` で即座にキャンセルし、エモートアニメーションに遷移する。

### DynamicTarget システム

`target.type: "dynamic"` 時に使用する動的ターゲット。シーンに1つ配置する不可視の GameObject（NavMeshAgent 付き）。

#### パラメータ

| パラメータ | 値 | 説明 |
|-----------|-----|------|
| clock | 1-12 | キャラクター基準の方向（12=正面、3=右、6=背後、9=左） |
| distance | near/mid/far | 距離プリセット（Inspector設定値、デフォルト 1.5/3.0/5.0） |
| height | high/mid/low | 高さプリセット（Inspector設定値、デフォルト 2.0/1.0/0.0） |

#### clock → 角度変換

```
12時 = 0°（正面）、1時 = 30°、2時 = 60°、3時 = 90°（右）
4時 = 120°、5時 = 150°、6時 = 180°（背後）
7時 = 210°、8時 = 240°、9時 = 270°（左）
10時 = 300°、11時 = 330°
```

角度はキャラクターの `forward` 基準で時計回り。

#### LookAtTarget 子オブジェクト

DynamicTarget の子として `DynamicLookAtTarget` を Awake 時に自動生成する。
`CharacterLookAtController.LookAtTransform()` でこの子オブジェクトを追跡することで、
親の NavMesh 移動に自動追従しつつ、height に応じた視線高さを実現する。

```
DynamicTarget (NavMeshAgent)        ← NavMesh上をXZ移動（Y座標はNavMesh依存）
  └─ DynamicLookAtTarget            ← localPosition = (0, height, lookAtForwardOffset)
                                       親移動に自動追従、Yのみ height で制御
```

| パラメータ | デフォルト | 説明 |
|-----------|---------|------|
| `lookAtForwardOffset` | `1.0` | 子オブジェクトのローカルZ方向オフセット |

`SetLookAtHeight(string)` で MoveTo を伴わずに高さのみ更新可能。

### RoomTargets システム

シーン内の名前付きターゲット（mirror, window 等）を管理するシステム。
`RoomTargetManager` コンポーネントを持つ親 GameObject の子要素を起動時にスキャンし、
名前→位置/LookAt先のDictionaryを構築する。

#### シーン階層

```
RoomTargets (RoomTargetManager)
├── talk                       ← Talk時の移動先位置（旧talk_position）
│   └── talk_lookattarget      ← Talk時のLookAt先
├── mirror                     ← 移動先位置（Transform.position）
│   └── mirror_lookattarget    ← LookAt先
└── window
    └── window_lookattarget
```

**`talk` ターゲット**: `TalkController` が `GetTalkPosition()` / `GetTalkLookAtTarget()` で参照する特別なターゲット。
`action:"move"` + `target.type:"talk"` で Talk 状態に遷移する。
`CharacterSetup.OnVrmLoaded()` で `TalkController.roomTargetManager` に自動ワイヤリングされる。

#### ターゲット解決フロー

LLMが `target.type` に子要素の名前（"mirror" 等）を指定すると、
`TargetData.GetTargetType()` が登録済み名前リストと照合して `TargetType.Named` を返す。

```
LLM: target.type = "mirror"
  → GetTargetType(roomTargetNames) → "mirror" が登録済み → TargetType.Named
  → Move: mirror の Transform.position へ NavMesh 移動、lookattarget 方向を向いて停止
  → LookAt: mirror_lookattarget の position を注視
```

未知の type（登録名にもない）は `TargetType.Dynamic` にフォールバックする。
clock/distance/height が未設定なら DynamicTarget も動かず、直前の視線位置を維持する。

#### LLMプロンプトへの通知

`ChatManager.GenerateSystemPrompt()` で `{available_room_targets}` プレースホルダを
`RoomTargetManager.GenerateTargetListForPrompt()` の結果に置換し、LLMに利用可能なターゲット名を通知する。
`ProcessLookAt` は常にこのメソッドを呼び出すため、`action:ignore` 時もheightが正しく反映される。

#### 動作

1. `MoveTo(clock, distance, height, characterTransform)` でターゲット位置を計算し、解決済み位置を `Vector3` で返す
2. `FindValidNavMeshPosition()` で段階的にNavMesh上の有効位置を検索:
   1. 目標位置付近（距離プリセット範囲内）で `NavMesh.SamplePosition`
   2. 拡大検索（`maxSearchRadius`、デフォルト 10m）
   3. キャラクター現在位置付近にフォールバック（到達不能な位置へのテレポート防止）
   4. 最終フォールバック: キャラクター位置そのまま
3. NavMeshAgent.SetDestination で障害物回避移動
4. LookAtTarget 子オブジェクトの localY を height に更新
5. CharacterController は返却値の Y をキャラクター高さに補正してナビゲーション移動に使用
6. 到着時の目標回転はキャラクター→移動先方向の `Quaternion.LookRotation()` で算出（進行方向を向いたまま停止する。移動距離がほぼ0の場合は現在の向きを維持）

#### NavMeshAgent設定

DynamicTargetは不可視マーカーのため、他のNavMeshAgentとの干渉を防ぐ設定:
- `obstacleAvoidanceType = NoObstacleAvoidance`（回避システムから除外）
- `avoidancePriority = 99`（最低優先度）
- `radius = 0.1f`, `height = 0.1f`（極小コリジョン）

#### 用途

- `action:move` + `target:dynamic` → キャラクターがDynamicTarget位置に移動
- `action:ignore` + `target:dynamic` → キャラクターがDynamicTargetを向く（移動なし）
- LLMが「あっちの方を見て」「窓の近くに行って」等の曖昧な指示を出す際に使用

### Streaming Response Protocol

ストリーミングモード使用時、LLMからのレスポンスは単一JSONとして出力される。
**逐次フィールドパース**により、JSONの各フィールドが完了した時点で即座にキャラクターに反映される。
`message` フィールドは特別扱いされ、値の途中でもチャンク単位でストリーミング表示される。

#### フォーマット

```json
{
  "emotion": { ... },
  "reaction": "短い相槌",
  "action": "move",
  "target": { ... },
  "emote": "happy01",
  "message": "本文テキスト..."
}
```

#### 具体例

```json
{
  "emotion": { "happy": 1.0, "relaxed": 0.5, "angry": 0.0, "sad": 0.0, "surprised": 0.0 },
  "reaction": "いいね!",
  "action": "move",
  "target": { "type": "talk", "clock": 10, "distance": "mid", "height": "high" },
  "emote": "happy01",
  "message": "こんにちは！今日もいい天気ですね。"
}
```

#### ルール

- 全情報を単一JSONに含める（`###SEPARATOR###` は不要）
- `reaction`: 短い相槌（10文字以内推奨）。即座に表示される。省略可
- `message`: メイン本文。ストリーミング表示される。JSON内で最後に配置を推奨。省略可
- ヘッダーJSONの前後にマークダウン記法（` ```json ` 等）を付けない

#### 逐次フィールドパース（IncrementalJsonFieldParser）

ストリーミングで届くJSONテキストを文字単位で解析し、トップレベルの各フィールドが完了した時点でイベントを発火する。
これにより、JSON全体の受信完了を待たずに「表情変化 → 相槌表示 → ターゲット注視 → 移動開始 → エモート → 本文ストリーミング」と順にキャラクターが反応する。

##### パース処理

1. `{` の検出でJSON開始（depth=1）
2. depth==1でキー文字列（`"fieldName"`）を検出 → コロン（`:`）の後に値パース開始
3. 値の種類に応じた完了検出:
   - 文字列: 閉じ引用符で完了
   - オブジェクト: depth==1に戻った時点で完了
   - プリミティブ（数値/bool/null）: カンマまたは `}` で完了
4. 値完了時: `OnFieldParsed(fieldName, rawJsonValue)` を発火
5. `StreamingFieldName`（デフォルト: "message"）に一致するフィールドの場合:
   - 文字列値の途中でもチャンク単位で `OnStringValueChunk(fieldName, decodedChunk)` を発火
   - エスケープシーケンス（`\n`, `\"`, `\\` 等）はデコード済みで送出
6. 外側の `}` でdepth==0 → `OnJsonComplete` を発火

##### シングルクォート正規化

LLMがシングルクォートを出力する場合（`'key': 'value'`）にも対応。
バッファにはダブルクォートとして格納し、`JsonUtility.FromJson` が正しくパースできるようにする。
ダブルクォート文字列内のシングルクォート（例: `"it's"`）はそのまま維持される。

##### イベントフロー

```
LLM Streaming Response
    │
    ▼
[Provider] → token
    │
    ▼
[StreamSeparatorProcessor]
    ├─ IncrementalJsonFieldParser.ProcessChunk()
    │   ├─ OnFieldParsed("emotion", "{...}")      ← フィールド完了ごと
    │   ├─ OnFieldParsed("reaction", "\"..\"")     ← 相槌完了
    │   ├─ OnFieldParsed("action", "\"move\"")
    │   ├─ OnFieldParsed("target", "{...}")
    │   ├─ OnFieldParsed("emote", "\"happy01\"")
    │   ├─ OnStringValueChunk("message", chunk)    ← messageストリーミング（複数回）
    │   ├─ OnFieldParsed("message", "\"全文\"")    ← message完了
    │   └─ OnJsonComplete
    │       └─ OnHeaderReceived(header)
    │
    └─ OnTextReceived(chunk)  ← OnStringValueChunk("message") を転送
```

##### 逐次反映の流れ

```
[LLMClient]
    ├─ OnStreamFieldReceived  →  [ChatManager.HandleStreamField]
    │                                ├─ Thinking解除判定
    │                                ├─ emotion → ExpressionController（表情即時反映）
    │                                ├─ reaction → OnStreamingReactionReceived（UI即座表示 + 音声合成）
    │                                └─ target/action/emote → OnStreamFieldApplied
    │                                     └─ [CharacterController.HandleStreamField]
    │                                          ├─ target → LookAt設定、action待ち合わせ
    │                                          ├─ action → target待ち合わせ、揃えば移動実行
    │                                          └─ emote → ProcessEmote: エモート再生（Walking中はキューイング）
    │
    ├─ OnStreamTextReceived   → [ChatManager.HandleStreamText]（messageストリーミング表示 + 音声合成）
    ├─ OnStreamHeaderReceived → [ChatManager.HandleStreamHeader]（状態更新のみ）
    └─ OnResponseReceived     → [ChatManager.HandleLLMResponse]
                                   └─ [CharacterController.HandleChatResponse]
                                        └─ 逐次反映済み → 口パクのみ
                                        └─ ブロッキング → 既存の全フィールド一括処理
```

##### action/target の待ち合わせ

`action` と `target` はそれぞれ独立して届くため、CharacterController内で待ち合わせを行う:
- `target` 到着 → LookAt即時設定 + `_pendingTarget` に保存 → `TryExecutePendingAction()`
- `action` 到着 → `_pendingAction` に保存 → `TryExecutePendingAction()`
- `TryExecutePendingAction()`: 両方揃っている場合のみ移動/インタラクションを実行

##### Thinking解除タイミング

```
[Thinking Loop中]
    │
    ├─ emotion到着  → StopThinking()（graceful: ed再生）
    ├─ reaction到着 → StopThinking()（graceful: ed再生）
    ├─ target到着   → StopThinking()（graceful: ed再生）
    ├─ action到着   → Thinking継続（target/emote待ち）
    └─ emote到着    → ForceStopThinking()（即座キャンセル → エモートへ遷移）

フォールバック: HandleRequestCompleted()時にまだThinking中なら強制解除
```

#### JSON未完了時のフォールバック

LLMがJSON出力を途中で終了した場合、`StreamSeparatorProcessor` はストリーム完了時に
蓄積されたバッファ全体をJSONとしてパースを試みる。

- パース成功 → `OnHeaderReceived` を発火し、正常に処理を継続
- パース失敗 → 不完全JSON修復を試みる（`RepairIncompleteJson`）
  - 修復成功 → 修復後JSONでパース → `OnHeaderReceived` を発火
  - 修復失敗 → `OnParseError(errorMessage, rawText)` を発火（StatusOverlay表示用）

#### 空・不完全レスポンスのデフォルト値補填（FillDefaults）

`LLMResponseData.FromJson()` および `LlmResponseHeader.ToResponseData()` でパース後に
`FillDefaults()` を呼び出し、null/空のフィールドにデフォルト値を補填する。
WebLLM等でJSON構文は正しいが中身が空のレスポンスが返った場合の安全策。

| Field | Default |
|-------|---------|
| character | `"chr001"` |
| action | `"move"` |
| emote | `"Neutral"` |
| target | `{ type: "talk" }` |
| target.type | `"talk"`（targetが存在するが空の場合） |
| emotion | `new EmotionData()`（全値0.0） |

#### エラー表示

LLMエラー・パースエラーはメッセージ欄ではなく `StatusOverlay.ShowError()` に表示される。
一定時間（デフォルト10秒）後に自動消去。StatusOverlay未設定時はメッセージ欄にフォールバック。
パースエラー時に生テキストが存在する場合はメッセージ欄に表示される。

#### 不完全JSON修復（RepairIncompleteJson）

LLMがJSON出力を途中で打ち切った場合（トークン上限到達等）、正常にパースできた部分だけで
処理を続行するためのフォールバック。途中まで正しい形式であれば読み取れた部分だけ反映し、
読み取れなかったフィールドはデフォルト値のままとする。

```
修復ロジック:
├─ JSON開始 '{' を検出
├─ トップレベルのカンマ区切りで最後の完了フィールド境界を走査
│   ├─ 文字列リテラル内のカンマはスキップ（引用符/エスケープ追跡）
│   └─ ネストオブジェクト内のカンマもスキップ（ブレース深度追跡）
├─ 不完全な末尾フィールドを切り捨て
└─ 不足している閉じブレース '}' を補完
```

**例:**
- 入力: `{"emotion":{"happy":0.5},"message":"こんに` → 修復: `{"emotion":{"happy":0.5}}`
- 入力: `{"emotion":{"happy":0.5},"action":"move","message":"` → 修復: `{"emotion":{"happy":0.5},"action":"move"}`
- 最初から `{` がない場合 → 修復不可、パースエラー

#### パースエラー処理

JSONパースエラー時は、コンソールログではなくUI上にエラーメッセージと生レスポンステキストを表示する。
ユーザーがプロンプトを不適切に変更した場合等にJSONエラーの原因が一目で分かるようにする意図。

**エラー経路（`OnParseError`）:**
```
StreamSeparatorProcessor.Complete() [JSONパース失敗]
  │ OnParseError("Stream completed with incomplete JSON", rawText)
  ▼
OllamaNdjsonStreamHandler / DifySseStreamHandler
  │ onParseError callback
  ▼
LLMClient.OnStreamParseError(error, rawText)
  ▼
ChatManager.HandleStreamParseError():
  ├─ _parseErrorHandled = true（後続HandleLLMResponseの二重処理防止）
  ├─ Thinking解除（StopThinking）
  ├─ SetState(Idle)
  ├─ OnParseError → UIController.OnChatParseError
  │     └─ <color>エラーメッセージ</color> + 改行 + 生テキスト
  ├─ voiceSynthesisController.SynthesizeAndPlay(rawText) ← TTS対象（エラーメッセージは除外）
  └─ OnChatResponseReceived(fallback) → CharacterControllerクリーンアップ
```

**通常エラー経路（`OnError`）との分離:**
- `OnParseError`: JSONパースエラー専用。生テキスト付き。UIに色付き表示 + TTS
- `OnError`: ネットワークエラー、設定エラー等。従来の `ChatState.Error` 経路

**二重処理防止:**
`HandleStreamParseError` → `_parseErrorHandled = true` → 後続の `HandleLLMResponse` で早期リターン。
`OnComplete` から `OnResponseReceived` が引き続き発火するため、`HandleLLMResponse` でのフラグチェックが必須。

| 項目 | エラーメッセージ | 生レスポンステキスト |
|------|-----------------|---------------------|
| UI表示 | `errorMessageColor` で色付き表示 | 通常色で表示（JSONをそのまま表示） |
| VOICEVOX TTS | 対象外 | 対象（`SynthesizeAndPlay`） |
| コンソールログ | `Debug.Log`（LLMClient） | なし |

#### 状態フラグ（StreamSeparatorProcessor）

| フラグ | 意味 |
|--------|------|
| `_jsonCompleted` | IncrementalJsonFieldParserがJSON全体のパースを完了 |
| `_headerEmitted` | `OnHeaderReceived` イベントを発火済み（二重発火防止） |

#### データクラス

```csharp
[Serializable]
public class LlmResponseHeader
{
    public string emote;         // "Neutral", "happy01", etc.
    public string action;        // "move", "interact_sit", "interact_sleep", "interact_exit", "ignore"
    public TargetData target;
    public EmotionData emotion;
    // characterフィールドなし（設定パネルで管理）
    // messageフィールドなし

    public LLMResponseData ToResponseData(string message);
}
```

---

## Animation Naming Convention

```
{character}_{type}_{category}_{action}{variation}_{state}

chr001_anim_common_idle01_lp
│      │     │      │    │  │
│      │     │      │    │  └── State: st(start) / lp(loop) / ed(end)
│      │     │      │    └───── Variation number
│      │     │      └────────── Action name
│      │     └───────────────── Category: common / talk / emote / interact
│      └─────────────────────── Type: anim
└────────────────────────────── Character template ID
```

### Tool Animation (Separate file)
```
chr001_anim_interact_bookshelf01_ed_toolL
                                    │
                                    └── Tool bone animation for left hand
```

---

## Animation Categories

### common - 基本行動
| ID | Type | Description |
|----|------|-------------|
| common_idle01_lp | Loop | 待機 |
| common_idlevar01 | OneShot | 待機バリエーション |
| common_walk01_st/lp/ed | Start/Loop/End | 歩き前 |
| common_walk02_lp | Loop | 歩き後 |
| common_walk03_lp | Loop | 歩き左 |
| common_walk04_lp | Loop | 歩き右 |
| common_walkturnL01_st | Start | 左旋回歩き出し（Walk-Turnシステム、TurnL_STトラック） |
| common_walkturnR01_st | Start | 右旋回歩き出し（Walk-Turnシステム、TurnR_STトラック） |
| common_run01_st/lp/ed | Start/Loop/End | 走り |
| common_runturn01_st | Start | 左90度ターン→走り |
| common_runturn02_st | Start | 右90度ターン→走り |

### talk - 会話
| ID | Type | Description |
|----|------|-------------|
| talk_idle01_st/lp/ed | Start/Loop/End | 会話中待機 |
| talk_idle02_lp | Loop | 長時間待機（退屈そう） |
| talk_idlevar01 | OneShot | 会話中バリエーション |
| talk_thinking01_st/lp/ed | Start/Loop/End | 考え中（API待ち） |

### emote - 感情表現
| ID | Type | Description |
|----|------|-------------|
| emote_relaxed01_st/lp/ed | Start/Loop/End | 笑顔、楽しい |
| emote_happy01_st/lp/ed | Start/Loop/End | 喜び |
| emote_angry01_st/lp/ed | Start/Loop/End | 怒り |
| emote_sad01_st/lp/ed | Start/Loop/End | 悲しみ |
| emote_surprised01_st/lp/ed | Start/Loop/End | 驚き |

### interact - 家具インタラクション
| ID | Type | Description |
|----|------|-------------|
| interact_sit01_st/lp/ed | Start/Loop/End | 座る |
| interact_bed01_st/lp/ed | Start/Loop/End | ベッドに寝転ぶ |
| interact_bookshelf01_st/lp/ed | Start/Loop/End | 本棚から本を取る |
| interact_bookchair01_st/lp/ed | Start/Loop/End | 椅子で本を読む |
| interact_exit01_st/lp/ed | Start/Loop/End | ドアから出る（退室） |
| interact_entry01 | OneShot | ドアから入る（入室、Root Motion移動） |

---

## VRM Model Naming Convention

```
{character}_{wardrobe}_{type}.vrm

chr001_w001_model.vrm
│      │    │
│      │    └── Type: model (avatar model file)
│      └─────── Wardrobe variation (w001, w002, ...)
└────────────── Character template ID
```

### Examples
| Filename | Description |
|----------|-------------|
| chr001_w001_model.vrm | chr001 標準衣装 |
| chr001_w002_model.vrm | chr001 夏服 |
| chr001_w003_model.vrm | chr001 冬服 |
| chr002_w001_model.vrm | chr002 標準衣装 |

### File Location
```
Assets/StreamingAssets/VRM/
├── chr001_w001_model.vrm
├── chr001_w002_model.vrm
└── ...
```

---

## Nook System

Nook = 部屋プレハブ。床・壁・家具をひとまとめにした環境単位。

### Nook構造

```
Assets/Prefabs/Nook/
├── Nook01/
│   ├── Nook01_default.prefab          # デフォルト部屋
│   └── Nook01_default/
│       └── NavMesh-Floor_NavMesh.asset # ベイク済みNavMesh
└── Nook02/
    └── Nook02_japanstyle.prefab
```

### Nookプレハブ内部構造

```
Nook01_default (Prefab)
├── Floor_NavMesh          # NavMeshSurface付き床
├── room01_model           # 壁・天球
├── floor01_model          # 床モデル
└── Furniture/
    ├── Chair01 (FurnitureInstance)
    │   ├── chair01_model
    │   ├── Interact_sit01           # 座り位置
    │   └── Interact_lookattarget01  # 視線ターゲット
    ├── Bed01 (FurnitureInstance)
    └── Door01 (FurnitureInstance)
```

### Nook読み込み（Addressables）

```csharp
// 動的読み込み
var handle = Addressables.LoadAssetAsync<GameObject>("Nook01_default");
var nookPrefab = await handle.Task;
var nookInstance = Instantiate(nookPrefab);

// 切り替え時
Destroy(currentNook);
currentNook = Instantiate(newNookPrefab);
```

---

## Furniture System

### Furniture ID Format
```
{room}_{category}_{index}

room01_chair_01
│      │     │
│      │     └── Index (same category in room)
│      └──────── Category ID
└─────────────── Room ID
```

### Furniture Categories

| Category | Actions | Default Action | Description |
|----------|---------|----------------|-------------|
| chair | sit | sit | 座れる椅子全般 |
| bed | sleep, sit | sleep | 寝転べるベッド |
| bookshelf | look, take | look | 本棚 |
| door | exit, entry | 自動判定 | 出入り口（exit=退室インタラクション、entry=入室ポイント） |

### データ構造

#### FurnitureTypeData（ScriptableObject）

家具の「種類」を定義。椅子全般、ベッド全般などの共通設定。

```csharp
[CreateAssetMenu(menuName = "CyanNook/FurnitureTypeData")]
public class FurnitureTypeData : ScriptableObject
{
    public string typeId;                      // "chair", "bed", etc.
    public string[] availableActions;          // ["sit"], ["sleep", "sit"]
    public string defaultAction;               // "sit", "sleep"
    public string interactionPointPrefix;      // "Interact_sit"
    public string lookAtPointPrefix;           // "Interact_lookattarget"
    public float approachRadius = 0.1f;        // 接近判定半径(m)
    public float lookAtMaxDistance = 2.0f;     // LookAt有効距離(m)
    public bool disableColliderDuringInteract = true;
}
```

#### FurnitureInstance（MonoBehaviour）

シーン内の個々の家具インスタンス。プレハブ内の各家具にアタッチ。

```csharp
public class FurnitureInstance : MonoBehaviour
{
    public string instanceId;               // "room01_chair_01"
    public FurnitureTypeData typeData;      // ScriptableObject参照

    // 自動収集
    public Transform[] InteractionPoints { get; private set; }
    public Transform[] LookAtPoints { get; private set; }

    // アクション自動判定
    public string SelectBestAction(Transform character) { ... }

    // 最寄りのInteractionPoint取得
    public Transform GetNearestInteractionPoint(Vector3 position) { ... }
}
```

### InteractionPoint命名規則（Blender Empty）

| 接頭辞 | 用途 | 例 |
|--------|------|-----|
| `Interact_sit` | 座る位置 | `Interact_sit01`, `Interact_sit02` |
| `Interact_sleep` | 寝る位置 | `Interact_sleep01` |
| `Interact_stand` | 立って操作 | `Interact_stand_read01` |
| `Interact_exit` | 退室位置 | `Interact_exit01` |
| `Interact_entry` | 入室位置（NavMesh外可） | `Interact_entry01` |
| `Interact_lookattarget` | 視線ターゲット | `Interact_lookattarget01` |

**注意**: 接頭辞の大文字/小文字は区別しない（case-insensitive）。Blender側での命名が小文字でも正しくマッチする。

---

## Furniture Interaction Flow

### 全体フロー

```
1. インタラクション開始リクエスト
   └─ InteractionController.StartInteraction(request)
   └─ State: None → Approaching

2. NavMesh移動 (CharacterNavigationController)
   └─ NavMeshAgentが位置制御（agent.SetDestination）
   └─ TL_Walk再生（walk01_lp ループ、Root Motionは無視）
   └─ アニメーション速度をagent速度に合わせて調整
   └─ InteractionPointへ接近

3. 接近完了 (OnApproachComplete)
   └─ State: Approaching → StartingInteraction
   └─ 家具を占有（FurnitureInstance.Occupy）
   └─ アニメーションID生成: interact_{action}01（actionはsit/sleep等）
   └─ TimelineBindingData.animationIdBindingsから対応Timelineを取得
   └─ TL_interact_xxx再生開始

4a. [LoopRegionあり] LoopStart到達 (OnLoopEntered)
   └─ State: StartingInteraction → InLoop
   └─ ループ区間でアニメーション繰り返し

4b. [LoopRegionなし] InteractionEndClip到達 (OnInteractionEndReached)
   └─ State: StartingInteraction → None（直接完了）
   └─ interact_exit等のワンショット再生用

5. 終了リクエスト (ExitLoop) ※LoopRegionありの場合のみ
   └─ State: InLoop → Ending
   └─ EndStartTime位置へジャンプ
   └─ interact_ed再生

6. 完了 (OnInteractionComplete)
   └─ State: Ending/StartingInteraction → None
   └─ 家具を解放（FurnitureInstance.Release）
   └─ navigationController.Warp(endPosition, endRotation)
   └─ idle01_lp再生
```

### 移動制御

NavMeshAgent位置制御方式：

```
NavMeshAgentが位置を直接制御し、Root Motionはインタラクション時のローカル調整のみに使用。
アニメーション再生速度をagent速度に合わせて足滑りを防止する。

agent.updatePosition = true;   // NavMeshAgentがCharacterRootの位置を制御
agent.updateRotation = false;  // 回転は手動制御（UpdateMovementRotation()で状況に応じた方向追従）
```

#### Root Motionのフィルタリング（ApplyRootMotion）

```
1. 位置保持モード（Idle/Talk/Emote）→ 無視（位置保持が優先）
2. 移動中（Moving/Approaching/Turning/FinalTurning）→ 無視（agentが制御）
3. Walk/Runアニメーション中 → 無視（安全策: JSON入力等でもドリフト防止）
4. ループジャンプ直後 → 無視（大きな負のdeltaを除外）
5. 上記以外（Interact等）→ ローカル座標で適用（BlendPivot相対の微調整）
```

#### 移動方式

| 移動種別 | 方式 | 説明 |
|---------|------|------|
| MoveTo（目的地指定） | `agent.SetDestination()` | NavMeshAgent経路追従、速度調整あり |
| Interact接近 | `agent.SetDestination()` | MoveTo同様、approachRadius到達で終了 |
| 最終接近フェーズ | `agent.Move()` | `remainingDistance <= finalApproachDistance` で自動ステアリング停止→ターゲットへ直接移動（到着付近の軌跡揺れ防止） |
| 前方歩行（Wキー） | `agent.Move()` | 毎フレーム `forward * walkSpeed * _moveSpeedMultiplier * dt` で直接移動。MoveSpeedTrackの速度カーブが反映される。A/Dキーで CharacterRoot を旋回し進行方向を変更可能 |

#### 移動中の回転制御（UpdateMovementRotation）

MoveTo/Interact接近中のキャラクター回転は `UpdateMovementRotation()` で制御する。
`agent.desiredVelocity` は目的地付近で値が小さくノイジーになり、回転方向が不安定になって
ジグザグ歩行の原因となるため、状況に応じて回転ターゲットの算出方式を切り替える。

| 条件 | 回転ターゲット | 理由 |
|------|---------------|------|
| 最終接近フェーズ（`_inFinalApproach`） | `_targetPosition` への方向 | agent停止中のためdesiredVelocity使用不可 |
| 直線パス（`path.corners <= 2`） | `_targetPosition` への方向 | desiredVelocityが不要、初期方向ズレ防止 |
| 近距離（`remainingDistance <= finalApproachDistance`） | `_targetPosition` への方向 | desiredVelocityノイズによるジグザグ回避 |
| 遠距離＋曲がりパス | `agent.desiredVelocity` | 障害物回避のため経路追従が必要 |

```
パラメータ:
  finalApproachDistance = 1.0f  ← この距離以下でtarget方向への直接回転・直接移動に切替
```

#### 最終接近フェーズ（FinalApproach）

MoveTo/Interact接近中、`remainingDistance <= finalApproachDistance` になると最終接近フェーズに移行する。
NavMeshAgentの自動ステアリング（パス追従）は目的地付近で減速する際にステアリング補正が敏感になり、
キャラクターの移動軌跡が左右に揺れる。特に到着直前で揺れが小刻みになる。
これを防止するため、最終接近フェーズではNavMeshAgentのパス追従を停止し、`agent.Move()` でターゲットへ直接移動する。

```
EnterFinalApproach():
  agent.isStopped = true                ← NavMeshAgentの自動ステアリングを停止

UpdateFinalApproach():
  direction = _targetPosition - transform.position
  agent.Move(direction.normalized * speed * dt)  ← ターゲットへ直線移動（NavMesh境界は尊重）

到着判定:
  distance <= arrivalThreshold → FinalTurningに遷移

回転:
  UpdateMovementRotation() が _inFinalApproach をチェック → ターゲット方向への直接回転

アニメーション速度:
  AdjustAnimatorSpeed(moveSpeed)        ← agent.velocityは0のため、意図した速度を直接指定

リセット箇所:
  StopMoving / StopForwardWalk / HandleNavigationFailure / StartNavigation / Warp
```

#### ナビゲーション安全機構

移動開始時とフレーム毎の更新で以下のチェックを実施:

**移動開始時（StartNavigation）:**
- `agent.ResetPath()` + `agent.velocity = Vector3.zero` で前回の経路状態をクリア
- `agent.isStopped = true` → `SetDestination()` → 方向判定後に移動開始
- 方向ベクトルはY座標を0にしてから正規化（水平面での角度計算の精度確保）
- **Walk-Turn判定:** `|angle| > turnAnimationThreshold` の場合、`StartMovingWithTurn(angle)` で旋回歩き出しアニメーションを使用（後述）
- **近距離インタラクション最適化:** 目標との距離がほぼゼロの場合（`direction.sqrMagnitude <= 0.01f`）、インタラクションリクエストであれば歩行タイムラインを経由せず直接 `OnInteractionReady()` を呼ぶ。例: interact_sit中のベッドからinteract_sleepへ遷移する場合、同じ家具位置にWarpされるため歩行が不要。歩行タイムラインを空で開始すると `StopWalkWithEndPhase()` が空振りしてアニメーションが停止する問題を回避する

**パス有効性チェック:**
- `pathPending` 解決後に `pathStatus` を確認
- `PathInvalid`: 移動中止、Idle復帰
- `PathPartial`: 警告ログ出力、移動は継続

**スタック検出:**
- `agent.velocity.magnitude` が閾値（デフォルト 0.05）未満の状態が `stuckTimeout`（デフォルト 3秒）継続で移動中止
- 全体の `movementTimeout`（デフォルト 30秒）超過でも移動中止
- 中止時は `HandleNavigationFailure()` → Idle復帰 + コールバック呼び出し
- 最終接近フェーズ中は `CheckNavigationHealth()` を呼ばない（`agent.isStopped=true` でvelocity=0のため誤検出防止）

#### アニメーション速度調整（足滑り防止）

```
MoveTo/Interact接近時（通常パス追従）:
  agent.speed = baseSpeed * _moveSpeedMultiplier   ← MoveSpeedTrackから毎フレーム設定
  PlayableGraph速度 = agent.velocity.magnitude / walkAnimationSpeed
  → agentの実速度に合わせてTimeline再生速度を調整

  MoveSpeedClip.adjustAnimatorSpeed = false の場合:
  → PlayableGraph速度調整をスキップ（定速再生）
  → 歩き開始モーション等、アニメーション速度を変えたくない区間に使用

最終接近フェーズ時:
  PlayableGraph速度 = moveSpeed / walkAnimationSpeed
  → agent.velocityは0（agent.isStopped=true）のため、意図した移動速度(overrideSpeed)を直接指定

前方歩行時:
  PlayableGraph速度 = 1.0（固定）
  移動速度 = walkSpeed * _moveSpeedMultiplier
  → MoveSpeedTrackの速度カーブが移動速度に反映される
```

#### Walk-Turn（旋回歩き出しアニメーション）

目標方向との角度差が `turnAnimationThreshold`（デフォルト45°）以上の場合、通常のwalk01_stの代わりに旋回しながら歩き始める専用クリップを再生する。

##### Timeline構造（複数AnimationTrackによるバインド切替方式）

1つのタイムライン `TL_common_walk01` 内に4つのAnimationTrackを配置し、コード側で必要なトラックだけAnimatorをバインドする：

```
TurnL_ST        [walkturnL01_st]              ← 左旋回歩き出し
TurnR_ST        [walkturnR01_st]              ← 右旋回歩き出し
Walk_ST         [walk01_st]                   ← 通常歩き出し
Walk_LPED       [walk01_lp] [walk01_ed]       ← 常時バインド（LP/ED）
LoopRegionTrack [--- LP region ---]
MoveSpeedTrack  [speed curves...]
```

- 3つのSTクリップは同じ開始時間・同じ長さ
- Walk_LPEDは**トラックリストの一番下**に配置（最高優先度、LP/EDが確実にSTを上書き）
- TurnL_ST / TurnR_ST / Walk_ST のクリップは**Post-Extrapolation: None**に設定
- エディタ上でのmuteは**使用しない**（`SetGenericBinding(track, null)` でバインド解除する方式）

##### トラック名によるバインド制御

`BindAnimatorToTimeline()` 内でトラック名を判定し、`TurnMode` に応じてバインド/アンバインドを決定：

| トラック名に含む文字列 | バインド条件 |
|----------------------|-------------|
| `"TurnL"` | `TurnMode.TurnLeft` 時のみ |
| `"TurnR"` | `TurnMode.TurnRight` 時のみ |
| `"_ST"`（TurnL/TurnRを含まない） | `TurnMode.Normal` 時のみ |
| 上記いずれにも該当しない | 常にバインド（Walk_LPED等） |

`TurnMode` は `BindAnimatorToTimeline()` 実行後に自動的に `Normal` にリセットされる。

##### ナビゲーションフロー

```
StartNavigation()
├─ |angle| <= threshold → StartMoving()（通常walk01_st）
├─ |angle| > threshold  → StartMovingWithTurn(angle)
│   ├─ angle < 0 → SetTurnMode(TurnLeft)
│   └─ angle >= 0 → SetTurnMode(TurnRight)
│   └─ PlayAnimation("common_walk01") → BindAnimatorToTimeline で TurnL/R_ST をバインド
└─ distance ≈ 0（インタラクション） → OnInteractionReady()（歩行スキップ）
```

この方式はRun用タイムラインにも同じ構造で適用可能。

#### MoveSpeedTrack（移動速度カーブ制御）

Walk/Run Timelineに配置するカスタムTrack。NavMeshAgentの移動速度をAnimationCurveで制御する。

```
MoveSpeedClip Inspector:
├── speedCurve           : AnimationCurve  ← 正規化時間(0-1) → 速度乗算値
│   例: st区間 (0, 0.1) → (1, 1.0)  ← 徐々に加速
│   例: lp区間 Linear(1.0)           ← 常に全速
└── adjustAnimatorSpeed  : bool            ← アニメ再生速度のAgent速度追従ON/OFF
    false: 歩き開始モーション等を定速再生
    true:  Agent速度に合わせて足滑り防止（デフォルト）
```

バインディング: `CharacterNavigationController`（`CharacterAnimationController.BindAnimatorToTimeline()` で自動バインド）

Clipが無い区間はデフォルト動作（乗算値=1.0、速度調整=ON）。
移動終了時（`StopMoving` / `HandleNavigationFailure`）にリセット。

### 位置・角度補間（BlendPivot方式）

インタラクション開始時、キャラクターの現在位置からInteractionPointまでの位置・回転をスムーズに補間する。

#### Transform階層構造

```
Character (Controller)
└── BlendPivot (Transform)
    └── VRM1 (Vrm10Instance)
        └── Animator
```

- **BlendPivot**: VRMの親として機能し、ワールド座標での補間を担当
- **VRM**: ローカル座標でRoot Motionを適用

#### 補間の流れ

```
1. インタラクション開始（OnApproachComplete）
   ├─ VRMの現在ワールド座標を記録
   ├─ BlendPivotをVRMの現在位置に移動
   └─ VRMのローカル座標をリセット（0,0,0 / identity）

2. Timeline再生中
   ├─ BlendPivot: current → target にワールド座標で補間
   │   └─ PositionBlendTrack / RotationBlendTrack
   └─ VRM: ローカル座標でRoot Motion適用
       └─ localPosition += localDelta, localRotation *= deltaRotation

3. 結果（加算合成）
   VRM.world = BlendPivot.world + VRM.local
             = (current→target補間) + (Root Motion累積)
```

#### 例：chair01インタラクション

```
初期状態:
- VRM現在位置: (0, 0, 0.35)
- VRM現在角度: Y=180°
- InteractionPoint: (0, 0, 0.5), Y=170°

インタラクション開始時:
- BlendPivot: (0, 0, 0.35), Y=180°
- VRM.local: (0, 0, 0), identity

補間 + Root Motion:
- BlendPivot補間: (0, 0, 0.35) → (0, 0, 0.5), Y=180° → Y=170°
- VRM Root Motion: Z+0.2m（アニメーション分）

最終結果:
- BlendPivot: (0, 0, 0.5), Y=170°
- VRM.local: (0, 0, 0.2)（Root Motion分）
- VRM.world: (0, 0, 0.7), Y=170°
```

#### メリット

- **直感的な計算**: 補間とRoot Motionが単純な加算になる
- **同方向なら大きく移動**: 補間0.15m + Root Motion 0.2m = 0.35m
- **逆方向なら相殺**: 補間が逆なら打ち消し合う
- **RootMotionを変更しなくてOK**: アニメーションはそのまま使用可能

#### 注意点

- インタラクション終了時は`navigationController.Warp(endPosition, endRotation)`でCharacterRootをワープ
- Warp()内でVRMのlocalPosition/Rotationをリセット（identity）
- これによりNavMeshAgentの内部位置とCharacterRootの位置が同期される

---

## State Machine

### Interaction States (InteractionController)

```
┌──────────────────────────────────────────────────────────────┐
│                    InteractionController                      │
│                                                               │
│  ┌──────┐  StartInteraction  ┌────────────┐                  │
│  │ None │ ─────────────────→ │ Approaching │                  │
│  └──────┘                    └──────┬─────┘                  │
│      ↑                              │ OnApproachComplete      │
│      │                              ▼                        │
│      │                    ┌───────────────────┐              │
│      │                    │ StartingInteraction│              │
│      │                    └─────────┬─────────┘              │
│      │                              │ LoopStart Signal       │
│      │                              ▼                        │
│      │  OnInteractionComplete ┌─────────┐                    │
│      │←───────────────────────│ InLoop  │←─┐                 │
│      │                        └────┬────┘  │ LoopEnd         │
│      │                             │ ExitLoop (shouldExitLoop)│
│      │                             ▼       │                 │
│      │                        ┌─────────┐  │                 │
│      │←───────────────────────│ Ending  │──┘                 │
│      │  ed完了時              └─────────┘                    │
│      │                                                       │
│      │←─────── TryCancel (キャンセル可能時)                  │
└──────────────────────────────────────────────────────────────┘
```

### Character States
```
┌─────────────────────────────────────────────────────────┐
│                    Normal Mode                          │
│  ┌──────┐    ┌──────┐    ┌──────────┐                  │
│  │ Idle │ ←→ │ Walk │ ←→ │ Interact │                  │
│  └──┬───┘    └──────┘    └────┬─────┘                  │
│     │ emote / thinking trigger │ emote / thinking       │
│     ▼                          ▼ (加算モード)           │
│  ┌──────┐               ┌──────────┐                   │
│  │Emote │               │Emote     │ → return to       │
│  └──────┘               │Thinking  │   interact        │
│  ┌──────────┐           └──────────┘                   │
│  │ Thinking │ → return                                 │
│  └──────────┘                                          │
└──────────────────────┬──────────────────────────────────┘
                       │ user speaks
                       ▼
┌─────────────────────────────────────────────────────────┐
│                    Talk Mode                            │
│  ┌───────────┐    ┌──────────┐                         │
│  │ Talk_Idle │ ←→ │ Thinking │ (API waiting)           │
│  └─────┬─────┘    └──────────┘                         │
│        │ emote trigger                                  │
│        ▼                                                │
│     ┌──────┐                                           │
│     │Emote │ → return to talk_idle                     │
│     └──────┘                                           │
└─────────────────────────────────────────────────────────┘
```

**加算モード**: EmotePlayableClip / ThinkingPlayableClip の `additiveBones` にボーンが指定されている場合、
AdditiveOverrideHelper がベースポーズをスナップショットし、指定ボーン以外を毎フレーム復元する。
Interact 中の座りポーズを維持しながら上半身のみ Emote/Thinking を反映可能。

**Emote → Thinking 優先**: Emote 再生中に Thinking が必要な場合、Emote をキャンセルして Thinking に遷移。
Thinking の復帰先は Emote の復帰先を継承する（二重の復帰先チェーン）。

### Talk States (TalkController)

```
┌──────────────────────────────────────────────────────────────┐
│                       TalkController                          │
│                                                               │
│  ┌──────┐   EnterTalk    ┌─────────────┐                     │
│  │ None │ ──────────────→│ Approaching │                     │
│  └──────┘                └──────┬──────┘                     │
│      ↑                          │ OnApproachComplete          │
│      │                          ▼                            │
│      │                   ┌─────────────┐                     │
│      │                   │   InTalk    │←── Talk中ループ      │
│      │                   └──────┬──────┘                     │
│      │                     │         │ ExitTalk              │
│      │                     │         ▼                       │
│      │                     │  ┌─────────────┐               │
│      │←────────────────────┤  │   Exiting   │→ Idle復帰     │
│      │  OnExitComplete     │  └─────────────┘               │
│      │                     │                                 │
│      │←── ForceExitTalk ───┘ (任意のStateから即時終了)        │
│      │                                                       │
│      │←─────── Cancel (Approaching中のみ)                    │
└──────────────────────────────────────────────────────────────┘
```

**ForceExitTalk**: 終了アニメーションなしで即座にNoneに遷移。
非talkアクション（move+dynamic等）への切替時に使用。

---

## Talk System

Talk システムは、キャラクターが RoomTarget "talk" 位置に移動し、ユーザーとの対話モードに入る機能。
インタラクションシステムと類似した構造で実装。

### TalkController

| 項目 | 内容 |
|------|------|
| 役割 | Talk モードの状態管理、移動・LookAt 制御 |
| 配置 | VRM インスタンスに `VrmLoader` が自動追加 |
| 参照 | `CharacterAnimationController`, `CharacterNavigationController`, `CharacterLookAtController`, `RoomTargetManager` |

### 主要API

| メソッド | 説明 |
|---------|------|
| `EnterTalk()` | Talkモード開始（RoomTarget "talk" 位置へ移動） |
| `ExitTalk()` | Talkモード終了（終了アニメーション再生後にIdle復帰） |
| `ForceExitTalk()` | Talkモードを即座に終了（終了アニメーションなし）。非talkアクションへの遷移時に使用 |
| `CancelTalk()` | Approaching中のキャンセル、InTalk中はExitTalkを呼び出し |
| `IsAwayFromTalkPosition(threshold)` | talk位置から離れているか判定（デフォルト閾値 1.0m） |
| `StartThinking()` / `StopThinking()` | Thinking状態の切替（LLM応答待ち）。Talk以外の状態でもCanPlayThinking()がtrueなら再生可能 |
| `ForceStopThinking()` | Thinking状態を即座に終了（ed再生なし）。emoteが最初に届いた場合等に使用 |

### Talk位置の取得（RoomTargetManager経由）

```csharp
private Transform GetTalkPosition()
{
    if (roomTargetManager != null)
    {
        var talkTarget = roomTargetManager.GetTarget("talk");
        if (talkTarget != null) return talkTarget.transform;
    }
    return talkPositionFallback;
}

private Transform GetTalkLookAtTarget()
{
    if (roomTargetManager != null)
    {
        var talkTarget = roomTargetManager.GetTarget("talk");
        if (talkTarget?.lookAtTarget != null) return talkTarget.lookAtTarget;
    }
    return null;  // フォールバック: LookAtPlayerを使用
}
```

RoomTargets階層の `talk` ターゲットから位置と視線先を取得する。
`roomTargetManager` は `CharacterSetup.OnVrmLoaded()` でワイヤリングされる。
`talkPositionFallback` はRoomTargetに "talk" がない場合のフォールバック位置。

### データ構造

```csharp
public class TalkController : MonoBehaviour
{
    [Header("Talk Position")]
    [Tooltip("RoomTargetManagerから'talk'ターゲットを取得")]
    public RoomTargetManager roomTargetManager;

    [Tooltip("フォールバック: RoomTargetに'talk'がない場合の待機位置")]
    public Transform talkPositionFallback;

    [Header("References")]
    public CharacterAnimationController animationController;
    public CharacterNavigationController navigationController;
    public CharacterLookAtController lookAtController;

    [Header("State")]
    [SerializeField]
    private TalkState _currentState = TalkState.None;
    public TalkState CurrentState => _currentState;
}

public enum TalkState
{
    None,        // 通常状態
    Approaching, // talk位置へ移動中
    InTalk,      // Talk モード中
    Exiting      // Talk モード終了中
}
```

### 使用Timeline

| Timeline | 用途 |
|----------|------|
| TL_Walk | talk位置への移動 |
| TL_talk_idle01 | Talk 中の待機（st/lp/ed パターン） |
| TL_talk_thinking01 | LLM 応答待ち中（st/lp/ed パターン） |

### デバッグ操作（Talk関連）

Talk のデバッグキーは `DebugKeyController` で一括管理される（詳細は「デバッグキー操作」節を参照）。

| キー | 動作 |
|------|------|
| C | Talk モード開始（Approaching → InTalk） |
| V | Talk モード解除（Exiting → Idle） |

### 処理フロー

```
1. EnterTalk() 呼び出し
   ├─ State: None → Approaching
   ├─ NavigationController: GetTalkPosition() へ移動開始
   └─ AnimationController: TL_Walk 再生

2. 移動完了 (OnApproachComplete)
   ├─ State: Approaching → InTalk
   ├─ AnimationController: TL_talk_idle01 再生
   └─ LookAtController: GetTalkLookAtTarget() 注視開始（フォールバック: LookAtPlayer）

3. Talk 中
   ├─ TL_talk_idle01 ループ再生
   ├─ LLM 応答待ち時: TL_talk_thinking01 に切替
   └─ 視線: talk_lookattarget 追従継続

4. ExitTalk() 呼び出し
   ├─ State: InTalk → Exiting
   ├─ LookAtController: LookForward()
   └─ AnimationController: TL_talk_idle01_ed 再生 → Idle

5. 完了 (OnExitComplete)
   └─ State: Exiting → None
```

### LookAt 制御（Timeline駆動）

視線制御は `CharacterLookAtController` + Timeline `LookAtTrack` で制御。
LookAtClip が存在する期間のみ LookAt が有効になる（Clip無し＝LookAt無効）。

#### 制御対象

| 部位 | 制御方式 | デフォルト |
|------|----------|-----------|
| Eye | VRM 1.0 LookAt API (`LookAtInput`) | ON |
| Head | ボーン回転（LateUpdate、ワールド空間角度制限付き） | ON |
| Chest | ボーン回転（LateUpdate、ワールド空間角度制限付き） | OFF |

#### ターゲット指定API

| メソッド | タイミング | 動作 |
|----------|------------|------|
| `LookAtPlayer()` | Talk 開始時 | カメラを注視 |
| `LookForward()` | Talk 終了時、家具lookattarget範囲外時 | 正面を向く |
| `LookAtPosition(Vector3)` | 固定位置注視が必要な場合 | 特定位置を注視（スナップショット、以降追従しない） |
| `LookAtTransform(Transform)` | LLM指示時（全タイプ共通） | 指定Transformを毎フレーム追跡して注視 |

#### 動的再評価（CharacterController.Update）

LLMレスポンス受信時にLookAtコンテキスト（TargetType + パラメータ）を保存し、
`CharacterController.Update()`で毎フレーム再評価する。これにより：
- Interact接近時に`lookAtMaxDistance`を超えたらLookAt自動解除
- RoomTarget/家具のTransform移動にも自動追従

| TargetType | 毎フレーム処理 |
|-----------|--------------|
| Talk / Named | `RoomTarget.lookAtTarget`のTransformを`LookAtTransform`で追跡 |
| Interact | `GetNearestLookAtPoint`で距離判定を再評価、範囲外なら`LookForward` |
| Dynamic | `LookAtTransform`で既に動的追跡中のため追加処理なし |

#### 補間（Blend）

LookAt の有効/無効切り替え時、`blendFrames`（デフォルト60）フレームかけて weight を補間。
Clip 開始時は 0→1、Clip 終了時は 1→0 へ徐々に遷移し、瞬間的な切り替えを防ぐ。
`blendFrames` は LookAtClip の Inspector で設定可能。

#### Microsaccade（疑似マイクロサッケード）

人間の固視中に発生する不随意の微小眼球運動（マイクロサッケード）を疑似的に再現する。
視線の自然な「揺れ」を加え、キャラクターの存在感を高める。

**パラメータ（CharacterLookAtController Inspector）:**

| フィールド | 型 | デフォルト | 説明 |
|-----------|-----|---------|------|
| `microsaccadeInterval` | `float` | `0.5` | 発生周期（秒）。実際のマイクロサッケード頻度は1〜3Hz |
| `microsaccadeAmount` | `float` | `0.01` | オフセット量（0〜1）。視線方向に垂直な平面上の距離 |

**処理フロー:**

1. `Update` でタイマーを加算、周期を超えたら新しいオフセットを生成
2. 視線方向に垂直な平面上でランダムな方向ベクトルを算出し、`microsaccadeAmount` を掛ける
3. `LateUpdate` の `ApplyEyeLookAt` で `_currentLookAtPosition`（通常のLookAt位置）にオフセットを加算
4. 毎回必ず通常値からの加算として処理するため、値が累積して外れることはない

**適用範囲:**
- Eye のみに適用（Head/Chest には影響しない — 頭や体が周期的に動くと不自然なため）
- `microsaccadeAmount <= 0` または `_eyeEnabled == false` の場合は無効

#### Head/Chest ボーン回転の角度計算

ワールド空間でアニメーション回転→ターゲット方向へのオフセットを計算し、
ワールド空間の Pitch(X)/Yaw(Y) で角度制限をクランプした後、ローカル回転に変換して適用する。
Hips に角度がある状態（座りポーズ等）でもワールド基準で角度計算するため、正しい方向を向く。

#### ボーン回転スムージング

角度制限クランプ後のオフセット回転を、前フレームの値から `Quaternion.Slerp` で補間する。
ターゲットが背後にある等、角度制限に張り付いた状態で移動によりクランプ値が瞬時にフリップ
（例: Yaw -45°→+45°）するのを防ぐ。

| フィールド | 型 | デフォルト | 説明 |
|-----------|-----|---------|------|
| `boneSmoothFrames` | `int` | `10` | スムージングフレーム数。1フレームあたり `1/boneSmoothFrames` の割合で新しい値に接近。1=即時反映（スムージング無効） |

Head/Chest 各ボーンで独立したスムージング状態（`_headSmoothedOffset` / `_chestSmoothedOffset`）を保持する。

#### Pre-Update Restore パターン

Head/Chest のボーン回転は LateUpdate で適用し、次フレームの Update でリセットする。
AnimationTrack がボーン回転を毎フレーム上書きするため、この順序が必須。
`[DefaultExecutionOrder(20000)]` で InertialBlendHelper と同じタイミングで実行。

#### ~~将来拡張: LLM からの視線指示~~ → 不採用

~~LLM レスポンスに `lookAt` フィールドを追加して視線制御可能~~ → `target` フィールドベースの動的再評価システム（CharacterController.UpdateLookAt）で実現。専用の `lookAt` JSONフィールドは導入しなかった。

### Expression制御（Timeline駆動）

VRM Expression（表情）をTimeline上で制御するための専用トラック `VrmExpressionTrack`。
`CharacterExpressionController` が管理する感情ベースの表情とは独立に、Timeline上でExpression weight をカーブ制御する。

#### 概要

- Timeline に `VrmExpressionTrack` を追加し、`VrmExpressionClip` を配置する
- 各クリップは **1つの VRM Expression**（Blink, Happy 等）を制御する
- カーブのX軸は正規化時間（0～1）、Y軸はExpression weight（0～1）
- `blendEmotionTag` フィールド: ブレンドTimeline用。このトラックが対応する感情タイプを指定（デフォルト: Neutral = 常にウェイト1.0）
- **Neutral preset**: `ExpressionPreset.neutral` を指定したクリップは全Expressionをニュートラル（=0）にリセットする。他のクリップと組み合わせて「ニュートラル顔 + 口開き + 目上向き」のようなベースを定義可能
- **加算動作**: 同一Timeline上の複数VrmExpressionTrackの値は加算される。フレーム開始時に全Expressionがゼロリセットされ、各トラックのクリップ値が累積適用される

#### カーブソースの二段階構造

| 条件 | 使用カーブ | 用途 |
|------|-----------|------|
| `sourceClip` あり | sourceClip から Bake したカーブ | 共通カーブの一括管理（瞬きパターン等） |
| `sourceClip` なし | `curve` フィールドを直接編集 | Timeline固有の特殊制御 |

**Bake処理:**
- Editor上で `sourceClip`（FBXアニメーション）の BlendShape カーブをドロップダウンで選択
- 「Bake Curve from Source」ボタンで選択カーブを正規化（時間 0～1）して `curve` フィールドに格納
- `bakeScale`（デフォルト 0.01）でカーブ値をスケーリング（Blender出力の0-100 → VRM Expression用の0-1）
- `AnimationUtility.GetEditorCurve()` はEditor専用APIのため、ランタイムではBake済みカーブを使用

**一括Rebake:**
- **CyanNook > Animation > Rebake All VRM Expression Curves** で全TimelineのVrmExpressionClipを一括再Bake
- `sourceClip` + `sourceCurveProperty` が設定済みのクリップのみ対象（手動カーブ編集のクリップはスキップ）

**共有AnimationClipの利点:**
- 瞬きのタイミングを全体的に見直す場合、共通AnimationClipを修正して一括Rebakeで全体に反映
- 特定Timelineのみ特殊なカーブにしたい場合は sourceClip を外して curve を直接編集

#### ExpressionControllerとの優先度

PlayableDirector の評価タイミング（MonoBehaviour.Update の後）で `SetWeight` するため、
`CharacterExpressionController.Update()` の値を自然に上書きし、**Timeline側が優先**される。

本トラックは Body Timeline（CharacterAnimationController管理）と Facial Timeline（CharacterExpressionController管理）の
両方で使用される。Body Timeline に本トラックが存在する場合、Facial Director は一時停止される（Body優先）。
詳細は「Facial Timeline」セクション参照。

VRM 1.0 の `overrideBlink` / `overrideMouth` / `overrideLipSync` はVRMランタイム内部で処理される。
Facial Timelineで瞬きを明示的に制御する場合は、VRMモデル側の感情Expressionの `overrideBlink` を `none` に設定すること。

#### 関連ファイル

| ファイル | 役割 |
|---------|------|
| `Scripts/Timeline/VrmExpressionTrack.cs` | TrackAsset + MixerBehaviour + Behaviour |
| `Scripts/Timeline/VrmExpressionClip.cs` | PlayableAsset（Expression + カーブ保持） |
| `Editor/VrmExpressionClipEditor.cs` | カーブBake UI、BlendShapeカーブ選択ドロップダウン、一括Rebakeメニューコマンド |
| `Scripts/Character/CharacterExpressionController.cs` | `GetVrmInstance()` で VRM インスタンスを公開 |
| `Scripts/Character/CharacterAnimationController.cs` | `BindAnimatorToTimeline()` 内で VrmExpressionTrack をバインド |

### Facial Timeline（感情Timeline駆動の表情制御）

感情ごとの専用Timeline（瞬き、LookAt変更、凝った遷移アニメーション等を含む）を再生する方式。
`FacialTimelineData` が設定されている場合に有効化され、未設定時は従来の直接制御にフォールバックする。

#### アーキテクチャ

```
キャラクター (VRM Instance)
├─ PlayableDirector (Body) ← 既存 / CharacterAnimationController管理
│   ├─ AnimationTrack, LookAtTrack, etc.
│   └─ VrmExpressionTrack (任意、あれば Facial より優先)
│
├─ FacialDirector (子オブジェクト)
│   └─ PlayableDirector (Facial) ← CharacterExpressionController管理
│       ├─ VrmExpressionTrack (表情・瞬きカーブ)
│       ├─ LookAtTrack (任意)
│       └─ LoopRegionTrack (ループ制御)
│
└─ CharacterExpressionController
    ├─ facialDirector の Timeline選択・再生
    ├─ 感情ブレンド（隣接感情ペアのブレンドTimeline選択・ウェイト制御）
    ├─ LoopRegion管理（ループ巻戻し、end遷移）
    ├─ holdタイマー → タイムアウトでend → neutral
    └─ body override 通知でfacial一時停止
```

#### 優先度

**Body VrmExpressionTrack > Facial Director > 直接制御（フォールバック）**

- Body TimelineにVrmExpressionTrackがある場合、`CharacterAnimationController`が`SetBodyExpressionOverride(true)`を呼び出し、Facial Directorを一時停止
- Body Timelineに無い場合はFacial Directorが表情を制御
- `FacialTimelineData`が未設定の場合は従来のSetWeight直接制御にフォールバック

#### 動作フロー

```
1. 起動: StartNeutralFacial() → neutral Timeline再生（瞬きアニメ等）
2. LLM応答: SetEmotion(emotion) → surprised判定 → SelectAndPlayFacialTimeline()
   a. surprised閾値判定:
      - surprised >= surprisedThreshold → StartSurprisedPhase()（後述）
      - surprised < surprisedThreshold → surprised=0扱いで通常処理へ
   b. 上位2感情を抽出 → GetTopTwoEmotions()
   c. ブレンド判定:
      - 隣接ペア && 正規化1位 < 0.9 && ブレンドTimeline登録済 → ブレンドTimeline
      - それ以外 → 1位の単体Timeline
3. 感情切替: PlayFacialTimeline(emotion) → Timeline の start セクション再生
4. LoopRegion: start到達 → ループ開始、LoopEnd到達 → LoopStartに巻戻し
5. テキスト表示完了: NotifyTextDisplayComplete() → ホールドタイマー開始
6. ホールドタイマー: emotionHoldDuration(デフォルト3秒) 経過
7. End遷移: JumpToFacialEndPhase() → end セクション再生
8. 完了: CompleteFacialEndPhase() → neutral Timeline に復帰
※ ホールドタイマーはテキスト表示完了後にのみカウントされる
※ チャット応答開始時に OnResponseStarted() でタイマーが停止される
※ タイムアウト前に別感情が来た場合 → 即時切替（endセクション再生なし）
※ 同じ感情/同ブレンドペアが来た場合 → ホールドタイマーリセット（ブレンドはウェイト更新のみ）
※ 非チャット使用（テスト表情等）ではデフォルト即座にタイマーが動作する

--- Surprised一時リアクション ---
驚き（surprised）は瞬間的な反応のため、他の感情とは異なる特別な制御を行う。
閾値未満の微弱なsurprisedは無視し、閾値以上の場合は短時間再生後に後続感情へ遷移する。

1. surprised >= surprisedThreshold（デフォルト0.3）:
   a. 他4感情（happy/relaxed/angry/sad）を「後続感情」として保存
   b. surprisedタイムラインを再生（StartSurprisedPhase）
   c. surprisedDuration（デフォルト3秒）経過後:
      - 後続感情あり → その感情のTimelineへ遷移（TransitionFromSurprised）
      - 後続感情なし → neutralへ復帰
   d. テキスト表示完了を待たず、即座にタイマーが動作する
   e. surprised中に新しいSetEmotionが来た場合 → surprised中断、新感情が優先
2. surprised < surprisedThreshold → surprised=0として無視、他4感情のみで通常処理
```

#### テキスト表示完了連動タイミング

表情のdecay/holdタイマーは「テキスト表示がすべて終わってから」カウントを開始する。
これにより、長い応答でもテキストが表示されている間は表情が維持される。

```
チャット応答待ち開始（WaitingForResponse）
  └→ CharacterController.HandleChatStateChanged()
      ├→ expressionController.OnResponseStarted()  ← タイマー停止
      └→ animationController.OnResponseStarted()   ← emoteタイマー停止

全テキスト受信完了（HandleChatResponse）
  └→ CharacterController.HandleChatResponse()
      ├→ expressionController.NotifyTextDisplayComplete()  ← タイマー開始
      └→ animationController.NotifyTextDisplayComplete()   ← emoteタイマー開始
```

| パラメータ | コンポーネント | デフォルト | 説明 |
|-----------|---------------|----------|------|
| `emotionHoldDuration` | CharacterExpressionController | 3秒 | テキスト表示完了後、感情Timelineループを維持する時間 |
| `surprisedThreshold` | CharacterExpressionController | 0.3 | surprised閾値。これ未満のsurprisedは無視される |
| `surprisedDuration` | CharacterExpressionController | 3秒 | surprised一時リアクションの持続時間。経過後に後続感情またはneutralへ遷移 |
| `decayDelay` | CharacterExpressionController | 3秒 | テキスト表示完了後、直接制御モードの感情減衰を開始するまでの時間 |
| `emoteHoldDuration` | CharacterAnimationController | 5秒 | テキスト表示完了後、emoteループを維持する時間 |
| `messageDisplayDuration` | UIController | 5秒 | テキスト表示完了後、メッセージが消えるまでの時間 |

#### FacialTimelineData（ScriptableObject）

EmotionType → TimelineAsset のマッピングデータ。
メニュー: **Create > CyanNook > Facial Timeline Data**

```
FacialTimelineData
├── emotionBindings: List<EmotionTimelineBinding>
│   ├── [0] Neutral  → TL_facial_neutral
│   ├── [1] Happy    → TL_facial_happy01
│   ├── [2] Relaxed  → TL_facial_relaxed01
│   ├── [3] Angry    → TL_facial_angry01
│   ├── [4] Sad      → TL_facial_sad01
│   └── [5] Surprised → TL_facial_surprised01
├── blendBindings: List<BlendEmotionTimelineBinding>
│   ├── [0] Happy + Relaxed  → TL_facial_happy-relaxed
│   ├── [1] Relaxed + Sad    → TL_facial_relaxed-sad
│   └── [2] Sad + Angry      → TL_facial_sad-angry
├── GetTimeline(EmotionType) → TimelineAsset (なければnullフォールバック)
└── GetBlendTimeline(EmotionType, EmotionType) → TimelineAsset (順序不問)
```

#### Facial Timelineのバインディング

`PlayFacialTimeline`内で`BindFacialTimeline`を呼び出し、Timeline内のトラックを動的にバインド:

| トラック | バインド先 |
|---------|-----------|
| `VrmExpressionTrack` | `CharacterExpressionController`（this） |
| `LookAtTrack` | `CharacterLookAtController` |
| `LoopRegionTrack` | バインド不要（メタデータとして読み取り） |

#### Timeline作成ワークフロー（デザイナー向け）

1. Facial Timeline アセットを作成（例: `TL_facial_neutral.playable`）
2. `VrmExpressionTrack` を追加、`VrmExpressionClip` でblinkカーブ等を配置
3. 必要に応じて `LookAtTrack` を追加（感情ごとの視線変更）
4. `LoopRegionTrack` を追加（start/loop/end区間を定義）
5. `FacialTimelineData` ScriptableObject で EmotionType → Timeline を紐付け
6. `CharacterExpressionController` のInspectorで `facialTimelineData` を設定

#### 感情ブレンド（円環モデル準拠）

ラッセルの感情円環モデルを参考に、隣接する感情同士のブレンド再生に対応。

**ブレンド可能ペア（隣接関係）:**
- Happy + Relaxed（正の感情、覚醒度違い）
- Relaxed + Sad（低覚醒、感情極性違い）
- Sad + Angry（負の感情、覚醒度違い）

**選択アルゴリズム:**
1. EmotionData から上位2感情を抽出（`GetTopTwoEmotions()`）
2. ブレンド可能ペアか判定（`IsBlendablePair()`）
3. 1位の正規化値（1位÷(1位+2位)）が 0.9 未満 → ブレンドTimeline選択
4. 0.9 以上、または非ブレンドペア → 1位のみの単体Timeline

**ブレンドTimeline構成:**
```
ブレンドFacial Timeline（例: TL_facial_happy-relaxed.playable）
├─ VrmExpressionTrack [blendEmotionTag=Neutral]   ← 共通（Blink等）常にweight 1.0
├─ VrmExpressionTrack [blendEmotionTag=Happy]      ← Happy用Clip、動的weight
├─ VrmExpressionTrack [blendEmotionTag=Relaxed]    ← Relaxed用Clip、動的weight
├─ LookAtTrack（任意）
└─ LoopRegionTrack（ループ制御）
```

- `blendEmotionTag = Neutral` のトラックは常にウェイト1.0（Blink等の共通表情）
- `blendEmotionTag = Happy/Relaxed等` のトラックは比率に応じてウェイトが動的制御される
- `VrmExpressionMixerBehaviour` が `GetTrackBlendWeight()` でウェイトを取得し、カーブ値に乗算
- 単体Timeline: 全トラック `blendEmotionTag = Neutral`（デフォルト）→ 従来通り動作

**同ブレンドペアの再設定:** Timeline再起動なしでウェイト更新+タイマーリセットのみ。

#### VRM overrideBlink に関する注意

VRM 1.0の感情Expression（Happy, Angry等）には `overrideBlink` フラグがあり、
有効時は感情weight分だけBlinkが抑制される（例: Happy=0.8 → Blink実効値×0.2）。
Facial Timelineでは瞬きをデザイナーが明示的に制御するため、VRMモデル側の
感情Expressionの `overrideBlink` を `none` に設定する必要がある。

#### Expression weightリセット・加算

**毎フレームリセット**: `ResetExpressionsForTimelineFrame()` で全プリセットExpressionを0にリセット（フレームあたり1回）。
VrmExpressionTrackが存在するTimelineは全Expressionの制御権を持ち、前フレームの残留値や
他システム（Facial Director、直接制御等）の値をクリアする。

**加算適用**: 各トラックのクリップ値は `AddTimelineExpression()` で累積器に加算され、VRMに即時反映。
同一フレーム内の複数トラックが同じExpressionKeyを制御する場合、値は加算される。

**Neutral preset**: `ExpressionPreset.neutral` のクリップはフレームリセット後にスキップされる。
結果的に全Expression=0（ニュートラル顔）が維持され、同一Timeline上の他クリップの値が加算される。

**Timeline切り替え時**: `ResetVrmExpressionWeights()` による即時リセットも従来通り実行。
前のTimelineの残留値（例: Happy=0.5）が新Timelineの表情に干渉するのを防ぐ。

#### 関連ファイル

| ファイル | 役割 |
|---------|------|
| `Scripts/Character/FacialTimelineData.cs` | EmotionType → TimelineAsset マッピング + ブレンドペアバインディング（ScriptableObject） |
| `Scripts/Character/CharacterExpressionController.cs` | Facial Timeline再生制御、感情ブレンド、LoopRegion管理、holdタイマー（テキスト表示完了連動）、surprised一時リアクション制御、body override |
| `Scripts/Character/VrmLoader.cs` | Facial用PlayableDirector作成（子オブジェクト）、StartNeutralFacial呼び出し |
| `Scripts/Character/CharacterAnimationController.cs` | Body TimelineのVrmExpressionTrack検出 → SetBodyExpressionOverride通知 |

### テキスト口パク（簡易リップシンク）

LLM応答テキスト表示中に、VRM ExpressionPreset の `aa`（あ口）と `ee`（い口）を使い、
モーラ周期で口の開閉を繰り返す簡易リップシンク。音声ソースなしのテキストのみ対応。

将来 VOICEVOX TTS 統合時には `SetTtsActive(true)` でこの機能を停止し、
AudioQuery データに基づく本格リップシンクに切り替える。

#### 動作フロー

```
CharacterController.HandleChatResponse()
  └─ response.HasMessage → LipSyncController.StartSpeaking(message)
       ├─ テキスト長 × moraSpeed で発話推定時間を計算
       ├─ モーラ周期で aa/ee をランダム切替しながら開閉
       └─ 推定時間経過後に自動停止（Lerp でフェードアウト）
```

#### 口パクパターン（1モーラ周期）

```
|←── moraSpeed (default 0.12s) ──→|
|    開く (60%)    |  閉じる (40%)  |
|   Aa or Ee      |      0.0      |
                  ↑モーラ境界で Aa/Ee ランダム切替
```

#### パラメータ

| パラメータ | デフォルト | 説明 |
|-----------|----------|------|
| `moraSpeed` | 0.12s | 1モーラの周期（口パク速度） |
| `maxWeight` | 0.7 | 口の最大開き（ExpressionWeight 0-1） |
| `transitionSpeed` | 15 | 開閉の滑らかさ（Lerp 速度） |

#### 感情Expression・Facial Timelineとの共存

- 口パク: VRM ExpressionPreset `aa` / `ih` / `ou` / `ee` / `oh`（リップシンク系）
- 感情: VRM ExpressionPreset `happy` / `sad` 等（感情系）
- まばたき: VRM ExpressionPreset `blink`（視線系）

**更新タイミングによる共存**:
Facial TimelineのVrmExpressionTrackは`ProcessFrame`で毎フレーム全Expressionを0リセット後に
Timelineの値を加算適用する（感情・まばたき等）。この評価はPlayableDirectorのUpdate後に行われる。
LipSyncControllerは**LateUpdate**で口の形を書き込むことで、Timeline評価後にリップシンク値を
上書きし、感情Timelineとリップシンクが共存する。

VRM 1.0 の `overrideMouth` プロパティにより、感情Expression がリップシンクを
ブロック/ブレンドする場合があるが、これはモデル側の設定で制御される。

#### 関連ファイル

| ファイル | 役割 |
|---------|------|
| `Scripts/Character/LipSyncController.cs` | リップシンク統合制御（TextOnly/Mora/Simulated/Amplitude 4モード、TTS無効化フラグ） |
| `Scripts/Character/CharacterController.cs` | HandleChatResponse で HasMessage 時に StartSpeaking 呼び出し |
| `Scripts/Character/VrmLoader.cs` | SetupCharacterComponents で VRM インスタンスを LipSyncController に渡す |

---

## LLM Communication System

### 概要

LLM API との通信を行うシステム。ILLMProvider インターフェースにより複数の API バックエンド（Ollama / LM Studio / Dify / OpenAI / Claude / Gemini / WebLLM）に対応。
WebGL 公開時にユーザーがブラウザから API エンドポイントを設定可能。WebLLMはブラウザ内でLLMを実行するためサーバー不要。

### クラス構成

| Class | Responsibility |
|-------|----------------|
| `LLMClient` | LLM 通信統合管理、ILLMProvider 切替、ブロッキング/ストリーミング両方式対応、`AbortRequest()` でリクエスト中断、フィールド逐次イベント転送 |
| `ILLMProvider` | LLM プロバイダー共通インターフェース（SendRequest / SendStreamingRequest） |
| `OllamaProvider` | Ollama 用 HTTP 通信（NDJSON ストリーミング対応） |
| `LMStudioProvider` | LM Studio 用 HTTP 通信（OpenAI互換 Chat Completions API、SSE ストリーミング対応） |
| `DifyProvider` | Dify Chat Messages API 通信（SSE ストリーミング対応、conversation_id 管理） |
| `OpenAIProvider` | OpenAI API 通信（Chat Completions API + Bearer認証、SSE ストリーミング対応） |
| `ClaudeProvider` | Anthropic Claude API 通信（Messages API + x-api-key認証、SSE ストリーミング対応） |
| `GeminiProvider` | Google Gemini API 通信（Generative Language API + x-goog-api-key認証、SSE ストリーミング対応） |
| `WebLLMProvider` | WebLLM（ブラウザ内LLM via WebGPU）ILLMProvider実装。WebLLMBridge経由でweb-llm APIを呼び出し。StreamSeparatorProcessorでJSONパース |
| `WebLLMBridge` | WebLLM jslib C#ブリッジ（MonoBehaviour Singleton）。DllImport宣言 + SendMessageコールバック受信。C#イベントで外部に公開 |
| `FirstRunController` | 初回起動ポップアップUI + WebLLMモデルダウンロード進捗表示 |
| `LlmStreamHandler` | DownloadHandlerScript 継承。生テキストストリーム用（UTF-8 マルチバイト安全） |
| `StreamSeparatorProcessor` | JSONストリームの逐次パース処理。IncrementalJsonFieldParserによる逐次パース統合、messageフィールドのストリーミング転送。プロバイダー間で共有 |
| `IncrementalJsonFieldParser` | ストリーミングJSONの逐次フィールドパーサー。トップレベルフィールド完了ごとにイベント発火。StreamingFieldName指定フィールドはチャンク単位でOnStringValueChunk発火。シングルクォート正規化対応 |
| `LlmResponseHeader` | ストリーミングヘッダーのデータクラス（JSON全体からパース。reaction含む） |
| `ChatManager` | プロンプト生成、会話履歴管理、状態管理、TalkController連携、ストリーミング対応（逐次フィールド反映含む）、Vision画像取得、RoomTarget一覧のプロンプト埋め込み |
| `LLMConfigManager` | API 設定の保存・読み込み（PlayerPrefs） |
| `CharacterCameraController` | Headボーンコンストレイント・カメラ、オンデマンドキャプチャ、base64画像出力（ExecutionOrder 20001、LookAt後に実行） |
| `CharacterFaceLightController` | Headボーン追従の顔用ライト制御。positionOffset/followHeadRotation設定可能（ExecutionOrder 20001） |
| `DynamicCameraController` | MainCamera動的制御（FOV距離連動、Y軸ルックアット）。namespace: CyanNook.CameraControl |
| `UIController` | チャット入出力・ストリーミング逐次表示・入力モード切替（JSON/Chat）・マイクボタン（VoiceInputController連動）・TTSクレジット表示 |
| `SettingsMenuController` | 上部アイコンメニューバー、ホバーツールチップ、パネル展開/折り畳みアニメーション |
| `AvatarSettingsPanel` | アバター設定（VRMモデル選択、カメラ高さ、カメラルックアット、プロンプト（キャラ設定/レスポンスフォーマット）、Save/Reload） |
| `LLMSettingsPanel` | LLM設定（API設定、Vision、IdleChat、Sleep、WebCam、Save/TestConnection） |
| `VoiceSettingsPanel` | 音声設定（VOICEVOX音声合成 + Web Speech API音声入力） |
| `WebSpeechRecognition` | Web Speech API C#ラッパー（WebGL専用、音声認識） |
| `VoiceInputController` | 音声入力統合管理（WebSpeechRecognition + VAD + UIController接続）、`OnEnabledChanged`イベントでUI同期 |
| `VoiceActivityDetector` | 無音検出・自動送信（部分結果蓄積、N秒無音でトリガー） |
| `DebugSettingsPanel` | デバッグ設定（デバッグキーON/OFF、JSONモード、LLM Raw Text表示、設定Import/Export、ライセンス表示） |
| `SettingsExporter` | 全設定のJSON形式エクスポート・インポート（WebGL: ブラウザダウンロード/ファイル選択、Editor: クリップボード） |
| `MultiLineInputFieldFix` | TMP_InputFieldマルチライン改行修正コンポーネント（New Input System + TMP競合ワークアラウンド） |
| `CronScheduler` | cronスケジューラー。StreamingAssets/cron/からジョブ定義を読み込み、cron式に基づく定期的LLM自動リクエスト。キューイング方式 |
| `CronJobData` | cronジョブ定義のデータクラス（JSONデシリアライズ用） |
| `DebugKeyController` | デバッグキー一括管理（W/A/D歩行・旋回、C/V Talk、F/G/Hインタラクション）。UIトグルでON/OFF可能 |
| `StatusOverlay` | ステータスオーバーレイ（FPS・キャラクターステート・再生中タイムライン・JSヒープメモリを常時表示）。DebugSettingsPanel のトグルで表示切替。LLMエラー発生時は赤字でエラーメッセージを一定時間表示（`ShowError()`） |
| `FrameRateLimiter` | フレームレート制限（Inspector設定可能、デフォルト60FPS）。`Application.targetFrameRate` + `vSyncCount=0` |

### 会話履歴永続化

ChatManager の `_conversationHistory`（最大 `maxHistoryLength` 件、デフォルト10）を PlayerPrefs に保存・復元する。
アプリを閉じても直近の会話履歴が保持され、次回起動時にLLMが前回の会話を参照できる。

#### 保存タイミング

**メッセージ追加のたびに保存**する（WebGLではOnApplicationQuitが信頼できないため）。

```
[セッション開始]  Start() → LoadHistory() → PlayerPrefsから復元
[ユーザー送信]    Add(user) → TrimHistory() → SaveHistory()
[LLM応答]        Add(assistant) → TrimHistory() → SaveHistory()
[セッション終了]  特別な処理なし（直前のSaveHistoryで保存済み）
```

#### シリアライズ

`JsonUtility` は `DateTime` を扱えないため、シリアライズ用ラッパークラスで変換する。

| クラス | 用途 |
|--------|------|
| `SerializableConversationEntry` | `role`, `content`, `timestamp`（ISO 8601文字列）|
| `SerializableConversationHistory` | `entries`（SerializableConversationEntry配列）|

**PlayerPrefs キー:** `conversation_history`（JSON文字列）

#### ClearHistory() の動作

`ClearHistory()` 呼び出し時は以下をすべてクリアする：
- `_conversationHistory`（インメモリ）
- `PlayerPrefs` の `conversation_history` キー
- Dify の `conversation_id`（`llmClient.ClearConversation()`）

### デバッグキー操作（DebugKeyController）

`DebugKeyController` がデバッグ用キー入力を一括管理する。
`DebugSettingsPanel` のトグルUIで全キーを一括ON/OFF可能。
デフォルトでは無効状態（OFF）で起動し、必要に応じてONに切り替えて使用する。
入力フィールドにフォーカスがある場合はキー入力を自動的に無視する。

#### キーバインド一覧

| キー | 動作 | 条件 |
|------|------|------|
| W（押下） | 前方歩行開始（`StartForwardWalk`） | 歩行中でないこと |
| W（離し） | 前方歩行停止 → walk_ed → Idle | 歩行中であること |
| A（押下中） | 左旋回（CharacterRootを回転） | W歩行中のみ |
| D（押下中） | 右旋回（CharacterRootを回転） | W歩行中のみ |
| C | Talkモード開始（`EnterTalk`） | Talk中でないこと |
| V | Talkモード終了（`ExitTalk`/`CancelTalk`） | Talk中であること |
| F | 家具インタラクション開始 | 最寄りの家具あり、インタラクション中でないこと |
| G | インタラクション終了（ループ脱出） | インタラクション中であること |
| H | インタラクションキャンセル | インタラクション中であること |

#### A/D旋回の仕組み

W歩行中にA/Dキーで `CharacterRoot`（NavigationControllerのtransform）を回転させる。
`CharacterNavigationController.UpdateMoving()` が `transform.forward * walkSpeed` で移動方向を決定するため、
CharacterRootを回転させることで視覚的な向きと移動方向の両方が連動して変わる。
VRMインスタンスはCharacterRootの子オブジェクトとして親の回転に追従する。

- 旋回速度: `turnSpeed`（デフォルト 180°/秒）
- A/D単体押下（W非押下時）: 無反応

#### G/Hキーのエッジ検出

Timeline再生中に `wasPressedThisFrame` が動作しない場合があるため、
G/Hキーは `isPressed` の変化を手動で追跡する二重エッジ検出を実装している。

#### 参照設定

`DebugKeyController` の各参照フィールドは `CharacterSetup.OnVrmLoaded()` で自動設定される:
- `animationController`
- `navigationController`
- `talkController`
- `interactionController`
- `furnitureManager`

### API 設定の保存（PlayerPrefs 方式）

WebGL では PlayerPrefs が内部的に IndexedDB を使用するため、ブラウザを閉じても設定が保持される。

#### LLMConfig データ構造

```csharp
[Serializable]
public class LLMConfig
{
    public string apiEndpoint = "http://localhost:11434/api/generate";
    public string modelName = "gemma2";
    public float temperature = 0.7f;
    public float topP = 0.9f;          // Top P（Nucleus Sampling）
    public int topK = 40;              // Top K
    public int numPredict = 512;       // 最大応答トークン数（-1=無制限）
    public int numCtx = 4096;          // コンテキスト長（VRAM消費に直結）
    public float repeatPenalty = 1.1f; // 繰り返しペナルティ（1.0=無効）
    public bool think = false;         // Thinkingモード（推論モデル用）
    public float timeout = 60f;
    public LLMApiType apiType = LLMApiType.Ollama;
    public string apiKey = "";  // Dify/OpenAI用 Bearer トークン
}

public enum LLMApiType
{
    Ollama,     // ローカル Ollama（デフォルト）
    LMStudio,   // LM Studio（OpenAI互換 Chat Completions API）
    Dify,       // Dify Chat Messages API
    OpenAI,     // OpenAI API（Chat Completions API + Bearer認証）
    Claude,     // Anthropic Claude API（Messages API + x-api-key認証）
    Gemini,     // Google Gemini API（Generative Language API + x-goog-api-key認証）
    WebLLM      // ブラウザ内LLM（WebGPU、サーバー不要）
}
```

#### 保存・読み込み

```csharp
public class LLMConfigManager
{
    private const string CONFIG_KEY = "llm_config";

    public static void Save(LLMConfig config)
    {
        string json = JsonUtility.ToJson(config);
        PlayerPrefs.SetString(CONFIG_KEY, json);
        PlayerPrefs.Save();
    }

    public static LLMConfig Load()
    {
        string json = PlayerPrefs.GetString(CONFIG_KEY, "");
        if (string.IsNullOrEmpty(json))
        {
            return LLMConfig.GetDefault();
        }
        return JsonUtility.FromJson<LLMConfig>(json);
    }
}
```

### ILLMProvider アーキテクチャ

LLMClient は ILLMProvider インターフェースを介して各 API バックエンドと通信する。
APIタイプに応じてファクトリメソッドでプロバイダーを自動生成。
ブロッキング方式（SendRequest）とストリーミング方式（SendStreamingRequest）の2つの通信方式を持つ。

```csharp
public interface ILLMProvider
{
    IEnumerator SendRequest(LLMConfig config, string systemPrompt, string userMessage,
        Action<string> onSuccess, Action<string> onError,
        string imageBase64 = null, Action<string> onRequestBody = null);
    IEnumerator SendStreamingRequest(LLMConfig config, string systemPrompt, string userMessage,
        Action<LlmResponseHeader> onHeader, Action<string> onTextChunk,
        Action onComplete, Action<string> onError,
        string imageBase64 = null, Action<string> onRequestBody = null,
        Action<string, string> onField = null);
    bool SupportsStreaming { get; }
    IEnumerator TestConnection(LLMConfig config, Action<bool, string> callback);
    void ClearConversation();
}
```

`onField` コールバックは `IncrementalJsonFieldParser` からの逐次フィールドイベントを転送する。
`LLMClient` は `OnStreamFieldReceived` イベントとして外部に公開する。

`onRequestBody` コールバックは、プロバイダーが構築したJSONリクエストボディをそのまま通知する（デバッグ表示用）。
LLMClient は `OnRequestBodySent` イベントとして外部に公開する。

| Provider | API | ストリーミング | Vision（画像送信） | 特徴 |
|----------|-----|---------------|-------------------|------|
| `OllamaProvider` | Ollama `/api/generate` | NDJSON (`stream: true`) | `images` フィールド（base64直接） | systemPrompt対応、会話状態なし、生成パラメータ（`options`）+ `think`対応 |
| `LMStudioProvider` | OpenAI互換 `/v1/chat/completions` | SSE (`stream: true`) | OpenAI Vision形式（`image_url`） | messages配列形式、temperature/top_p/max_tokens/frequency_penalty対応 |
| `DifyProvider` | Dify `/chat-messages` | SSE (`response_mode: "streaming"`) | `/files/upload` → `files` フィールド | Bearer認証、conversation_id管理、systemPromptをquery先頭に付与 |
| `OpenAIProvider` | OpenAI `/v1/chat/completions` | SSE (`stream: true`) | OpenAI Vision形式（`image_url`） | Bearer認証必須、LMStudioと同じAPI形式、401エラー検出 |
| `ClaudeProvider` | Anthropic `/v1/messages` | SSE (`stream: true`) | Claude Vision形式（`base64` source） | x-api-key認証、system別フィールド、max_tokens必須、top_k対応 |
| `GeminiProvider` | Gemini `models/{model}:generateContent` | SSE (`alt=sse`) | `inline_data` 形式（base64直接） | x-goog-api-key認証、URL内モデル名、contents+parts配列、generationConfig |
| `WebLLMProvider` | web-llm (in-browser WebGPU) | OpenAI互換チャンク（jslib経由コールバック） | 非対応 | サーバー不要、@mlc-ai/web-llm CDN、Qwen3-1.7B、XGrammar対応、`interruptGenerate()`による中断 |

**OllamaProvider リクエストJSON構造:**

`BuildRequestJson()` で手動構築（JsonUtilityではトップレベル`think`や条件付き`images`が扱えないため）。

```json
{
  "model": "qwen3",
  "prompt": "...",
  "system": "...",
  "stream": true,
  "think": false,
  "options": {
    "temperature": 0.7,
    "top_p": 0.9,
    "top_k": 40,
    "num_predict": 512,
    "num_ctx": 4096,
    "repeat_penalty": 1.1
  },
  "images": ["base64..."]  // Vision有効時のみ
}
```

- `think`: Ollama APIのトップレベルパラメータ。Qwen3等の推論モデルでThinkingモードを制御
  - Qwen3: `think: true/false` で切替可能。プロンプト内の `/think` `/nothink` も有効
  - Qwen3.5: プロンプトベースの制御は不可。API `think: false` で無効化

**LMStudioProvider リクエストJSON構造:**

`BuildRequestJson()` で手動構築（OpenAI互換 Chat Completions API形式）。

```json
{
  "model": "qwen3-8b",
  "stream": true,
  "messages": [
    {"role": "system", "content": "..."},
    {"role": "user", "content": "..."}
  ],
  "temperature": 0.7,
  "top_p": 0.9,
  "max_tokens": 512,
  "frequency_penalty": 0.1
}
```

- OllamaとはAPI形式が異なる（`prompt`/`system` → `messages`配列、`options`オブジェクト → トップレベルパラメータ）
- `num_predict` → `max_tokens` にマッピング（-1=無制限の場合は省略）
- `repeat_penalty` → `frequency_penalty` に近似マッピング（`repeat_penalty - 1.0`）
- `think`, `top_k`, `num_ctx` はOpenAI互換APIでは非対応のため送信しない（UIには表示されるが無視）
- Vision画像: OpenAI形式の`image_url`（`data:image/jpeg;base64,...`）で埋め込み
- ストリーミング: SSE形式（`data: {"choices":[{"delta":{"content":"..."}}]}`）
- 接続テスト: `/v1/models` エンドポイントへGET

**OpenAIProvider:**

LMStudioProviderと同じOpenAI Chat Completions API形式。主な違いは認証ヘッダーの追加のみ。

- 全リクエストに `Authorization: Bearer {apiKey}` ヘッダーを付与
- リクエストJSON構造はLMStudioProviderと同一（messages配列、temperature、top_p、max_tokens、frequency_penalty）
- SSEストリーミングハンドラは `LMStudioSseStreamHandler` を共有
- 接続テスト: `/v1/models` エンドポイントへGET（Bearer認証付き）
- 401レスポンス時に「Authentication failed (invalid API Key)」を表示
- エラーレスポンスのbodyも表示（OpenAI APIはエラー詳細をJSONで返すため）

**ClaudeProvider リクエストJSON構造:**

`BuildRequestJson()` で手動構築（Anthropic Messages API形式）。

```json
{
  "model": "claude-sonnet-4-6",
  "max_tokens": 512,
  "stream": true,
  "system": "...",
  "messages": [
    {"role": "user", "content": "..."}
  ],
  "temperature": 0.7,
  "top_k": 40
}
```

- OpenAI形式との主な違い:
  - 認証: `x-api-key` ヘッダー（Bearer形式ではない）+ `anthropic-version: 2023-06-01` 必須
  - `system` はトップレベルフィールド（messages配列外）
  - `max_tokens` 必須（numPredict <= 0 の場合はデフォルト4096を使用）
  - `temperature` は 0.0-1.0 にクランプ（OpenAIの 0.0-2.0 より狭い）
  - `top_p` は送信しない（Claudeでは `temperature` と排他的、同時指定で400エラー）
  - `top_k` 対応（OpenAIでは非対応だがClaudeでは対応）
  - `frequency_penalty` / `num_ctx` / `think` は非対応のため送信しない
- Vision画像: Claude形式 `{"type":"image","source":{"type":"base64","media_type":"image/jpeg","data":"..."}}`
- SSEストリーミング: `event: content_block_delta` + `data: {"delta":{"type":"text_delta","text":"..."}}` 形式（専用 `ClaudeSseStreamHandler`）
- 接続テスト: モデル一覧APIがないため最小リクエスト（`max_tokens:1`）で確認
- 401エラー時に「Authentication failed」、404エラー時に「Model not found」を表示

**GeminiProvider リクエストJSON構造:**

`BuildRequestJson()` で手動構築（Gemini Generative Language API形式）。

```json
{
  "contents": [
    {
      "role": "user",
      "parts": [
        {"text": "..."},
        {"inline_data": {"mime_type": "image/jpeg", "data": "base64..."}}
      ]
    }
  ],
  "systemInstruction": {"parts": {"text": "..."}},
  "generationConfig": {
    "temperature": 0.7,
    "topP": 0.9,
    "topK": 40,
    "maxOutputTokens": 512
  }
}
```

- OpenAI/Claude形式との主な違い:
  - エンドポイントURLにモデル名を含む（`models/{model}:generateContent`）。ベースURL（`v1beta`）を設定に保存し、プロバイダー内で動的構築
  - ストリーミングは別エンドポイント（`:streamGenerateContent?alt=sse`）。リクエストボディに`stream`パラメータは不要
  - 認証: `x-goog-api-key` ヘッダー（Bearer形式でもx-api-key形式でもない）
  - メッセージは `contents` 配列 + `parts` 配列形式（`role: "user"` / `"model"`、`"assistant"` ではない）
  - システムプロンプトは `systemInstruction` トップレベルフィールド
  - パラメータは `generationConfig` オブジェクト内（`maxOutputTokens`, `topP`, `topK`）
  - `repeatPenalty` / `numCtx` / `think` は非対応のため送信しない
- Vision画像: `inline_data` 形式 `{"inline_data":{"mime_type":"image/jpeg","data":"base64..."}}`
- SSEストリーミング: `data:` 行のみ（`event:` 行なし、`[DONE]` なし）。`candidates[0].content.parts[0].text` からテキスト抽出。専用 `GeminiSseStreamHandler`
- 接続テスト: `GET /models/{model}` でモデル存在確認（x-goog-api-key認証付き）
- 400/403エラー時に「Authentication failed」、404エラー時に「Model not found」を表示

**デフォルトエンドポイント（`LLMConfig.GetDefaultEndpoint()`）:**

APIタイプ切替時にLLMSettingsPanelが自動でエンドポイントを切替。
ユーザーが手動編集済みの場合は上書きしない（既知のデフォルト値の場合のみ自動切替）。

| APIタイプ | デフォルトエンドポイント |
|-----------|------------------------|
| Ollama | `http://localhost:11434/api/generate` |
| LM Studio | `http://localhost:1234/v1/chat/completions` |
| Dify | `http://localhost/v1` |
| OpenAI | `https://api.openai.com/v1/chat/completions` |
| Claude | `https://api.anthropic.com/v1/messages` |
| Gemini | `https://generativelanguage.googleapis.com/v1beta` |
  - キャラクターチャットボットではデフォルトOFF推奨（推論トレースは遅延増加のみ）

#### ストリーミングアーキテクチャ

```
[LLM API] ─ raw bytes ─→ DownloadHandlerScript
                             │
                   ┌─────────┼──────────┐
                   │         │          │
         LlmStreamHandler  Ollama     Dify
         (生テキスト)    NdjsonHandler SseHandler
                   │         │          │
                   └────┬────┘          │
                        │               │
                StreamSeparatorProcessor ← JSON逐次パース処理
                        │
              IncrementalJsonFieldParser ← 逐次フィールドパース + messageストリーミング
                        │
            ┌───────────┼───────────────────┬──────────────┐
            │           │                   │              │
  OnFieldParsed   OnHeaderReceived    OnTextReceived  OnParseError
  (フィールド単位)  (JSON全体完了)    (messageストリーミング) (JSONパース失敗)
            │           │                   │              │
         LLMClient ──→ ChatManager ──→ CharacterController / UI
```

- `LlmStreamHandler`: 生テキストストリーム用の `DownloadHandlerScript`。直接 HTTP ストリームを受信する用途
- `OllamaNdjsonStreamHandler`: Ollama の NDJSON（各行が JSON）をパースし `response` フィールドを抽出
- `DifySseStreamHandler`: Dify の SSE イベントをパースし `answer` フィールドを抽出
- `StreamSeparatorProcessor`: 上記すべてのハンドラが共有するJSON逐次パース処理。messageフィールドのストリーミング転送
- `IncrementalJsonFieldParser`: StreamSeparatorProcessor内部で使用。JSONフィールド単位のインクリメンタルパーサー。StreamingFieldName指定フィールドのチャンクストリーミング対応

#### UTF-8 マルチバイト安全性

`System.Text.Decoder` を使用。`flush=false` で不完全なマルチバイトシーケンス（日本語3バイト文字の途中など）を次回チャンクに自動的に持ち越す。完了時に `flush=true` で残りをフラッシュ。

#### Dify プロバイダー仕様

| 項目 | 内容 |
|------|------|
| エンドポイント | `{baseURL}/chat-messages` (POST) |
| 認証 | Bearer トークン（LLMConfig.apiKey） |
| 会話管理 | Dify側で管理（conversation_id で紐付け）。クライアントから会話履歴は送信しない |
| システムプロンプト | Difyアプリ側で設定。Cyan-Nookからは送信しない（query にも含めない） |
| 動的変数 | `inputs` フィールドで送信。Difyアプリのプロンプトテンプレート内で `{{variable_name}}` として参照 |
| query | ユーザーの現在のメッセージのみ（会話履歴やシステムプロンプトを含めない） |
| 接続テスト | `{baseURL}/parameters` (GET) |
| ブロッキング | `response_mode: "blocking"` → `answer` フィールドからテキスト取得 |
| ストリーミング | `response_mode: "streaming"` → SSE で `answer` チャンクを逐次受信 |
| ファイルアップロード | `{baseURL}/files/upload` (POST, multipart/form-data) → `upload_file_id` 取得 |
| Vision画像送信 | `files: [{"type":"image","transfer_method":"local_file","upload_file_id":"..."}]` |

##### Dify inputs 変数一覧

ChatManager の `BuildDynamicInputs()` が以下の変数を `inputs` フィールドとして送信する。
Difyアプリ側の Chatflow Start ノードで入力変数を定義し、LLMノードのプロンプト内で参照する。

| 変数名 | Dify型 | 推奨最大長 | 内容 | 備考 |
|--------|--------|-----------|------|------|
| `character_name` | テキスト入力 | 48 | キャラクター名 | CharacterTemplateData |
| `character_description` | 段落 | 2000 | キャラクター設定テキスト | CharacterTemplateData |
| `character_id` | テキスト入力 | 48 | キャラクターID（例: chr001） | CharacterTemplateData |
| `current_datetime` | テキスト入力 | 48 | 現在日時 | `yyyy-MM-dd HH:mm (ddd)` 形式、約25文字 |
| `current_room` | テキスト入力 | 48 | 現在の部屋ID | デフォルト: "room01" |
| `current_pose` | テキスト入力 | 48 | 現在のaction | "idle", "move" 等 |
| `current_emotion` | テキスト入力 | 128 | 現在の感情 | "neutral", "happy" 等 |
| `bored` | テキスト入力 | 8 | 退屈ポイント | 0-100 の整数文字列 |
| `spatial_context` | 段落 | 4000 | 空間認識JSON | SpatialContextProvider出力。家具・ターゲット数に比例して増加 |
| `available_furniture` | 段落 | 2000 | 利用可能家具リスト | FurnitureManager出力。1家具あたり約60文字 |
| `available_room_targets` | 段落 | 1000 | ルームターゲットリスト | RoomTargetManager出力。1ターゲットあたり約20文字 |
| `visible_objects` | 段落 | 1000 | 視界内オブジェクト説明リスト | VisibleObjectsProvider出力。Vision有効かつSleep/Outing中でない場合のみ。1オブジェクトあたり約30〜50文字 |

> **注意:** Dify の `inputs` に未定義の変数を送信すると 400 Bad Request エラーになる。
> 短い変数は「テキスト入力」、長い変数（JSON・リスト等）は「段落 (Paragraph)」を使用すること。
> 家具やターゲットが多い環境では `spatial_context`・`available_furniture` の最大長を増やす必要がある場合がある。

##### Dify と Ollama/LM Studio のプロンプト送信比較

```
[Ollama/LM Studio]
  system: GenerateSystemPrompt()（プレースホルダ置換済み）
  prompt: GenerateFullPrompt()（会話履歴 + 現在のメッセージ、プレースホルダ置換済み）

[Dify]
  query:  ユーザーの現在のメッセージのみ
  inputs: BuildDynamicInputs()（動的変数の辞書）
  ※ システムプロンプト・会話履歴はDify側で管理
```

##### Dify ユーザー向けセットアップ

1. Dify Chatflow アプリの **Start ノード** で入力変数（上記一覧）を定義
2. **LLM ノード** のシステムプロンプトで変数を参照（例: `{{#start.bored#}}`）
3. 会話履歴は Dify が `conversation_id` で自動管理するため、特別な設定は不要

### MainCamera Dynamic Control（DynamicCameraController）

namespace: `CyanNook.CameraControl`

MainCameraの動的制御を担当するコンポーネント。キャラクターとの距離に応じたFOV（視野角）の自動調整と、Y軸のみのルックアット（水平方向のみ回転）機能を提供します。

`targetCharacter` は `CharacterSetup.OnVrmLoaded()` でVRM Instanceのtransformに自動設定される（`[NonSerialized]`、Inspectorには表示されない）。VRM Instanceを追跡するため、インタラクション中もBlendPivot + Root Motionによる実際のキャラクター位置にカメラが追従する。

#### 機能1: FOV距離連動制御（enableFovControl）

キャラクターとカメラの距離（XZ平面のみ、Y軸無視）に応じて、FOVを動的に変更します。

| フィールド | 型 | デフォルト | 説明 |
|-----------|-----|---------|------|
| `enableFovControl` | `bool` | `true` | FOV制御を有効化 |
| `minDistance` | `float` | `1.5` | この距離でFOV最小値（望遠） |
| `maxDistance` | `float` | `5.0` | この距離でFOV最大値（広角） |
| `minFov` | `float` | `30` | 最小FOV（近距離時・望遠）|
| `maxFov` | `float` | `60` | 最大FOV（遠距離時・広角） |
| `fovSmoothSpeed` | `float` | `5.0` | FOV変化のスムージング速度 |

**動作:**
```
距離 < minDistance → FOV = minFov (30°)
距離 > maxDistance → FOV = maxFov (60°)
minDistance ≤ 距離 ≤ maxDistance → 線形補間
```

**XZ平面距離計算:**
```csharp
float distance = Vector2.Distance(
    new Vector2(transform.position.x, transform.position.z),
    new Vector2(targetCharacter.position.x, targetCharacter.position.z)
);
```
Y軸（高さ）を無視することで、カメラの上下移動がFOVに影響しません。

#### 機能2: Y軸ルックアット（enableLookAt）

カメラをキャラクターに向けて水平方向のみ回転させます。X軸（ピッチ）とZ軸（ロール）は維持されます。

| フィールド | 型 | デフォルト | 説明 |
|-----------|-----|---------|------|
| `enableLookAt` | `bool` | `false` | Y軸ルックアットを有効化 |
| `lookAtRotationSpeed` | `float` | `2.0` | ルックアット回転速度 |
| `lookAtDelay` | `float` | `0.2` | ルックアット遅延時間（秒） |

**動作:**
1. キャラクター位置を遅延追従（`lookAtDelay`秒で追いつく）
2. XZ平面上の方向ベクトルを計算（`direction.y = 0`）
3. Y軸のみの回転を計算（X/Z軸は現在の値を維持）
4. スムージング回転（`Quaternion.Slerp`）

**Y軸のみ回転の実装:**
```csharp
Vector3 direction = _delayedTargetPosition - transform.position;
direction.y = 0;  // 水平方向のみ

Quaternion targetRotation = Quaternion.LookRotation(direction);
Vector3 targetEuler = targetRotation.eulerAngles;
targetEuler.x = currentEuler.x;  // X軸（ピッチ）維持
targetEuler.z = currentEuler.z;  // Z軸（ロール）維持

transform.rotation = Quaternion.Slerp(transform.rotation,
    Quaternion.Euler(targetEuler), Time.deltaTime * lookAtRotationSpeed);
```

#### 機能3: カメラ高さ設定

カメラのY軸位置を設定・取得します。PlayerPrefs経由で保存・復元されます。

| メソッド | 説明 |
|---------|------|
| `SetCameraHeight(float height)` | カメラ高さを設定（`transform.position.y`） |
| `GetCameraHeight()` | 現在のカメラ高さを取得 |

#### Public API

| メソッド | 説明 |
|---------|------|
| `SetLookAtEnabled(bool enabled)` | ルックアットON/OFF（AvatarSettingsPanelから呼ばれる） |
| `IsLookAtEnabled` (property) | ルックアット状態取得 |
| `SetCameraHeight(float height)` | カメラ高さ設定（AvatarSettingsPanelから呼ばれる） |
| `GetCameraHeight()` | カメラ高さ取得（AvatarSettingsPanelから呼ばれる） |
| `SetMinFov(float fov)` | 最小FOV設定（1.0～179.0にClamp）（AvatarSettingsPanelから呼ばれる） |
| `GetMinFov()` | 最小FOV取得（AvatarSettingsPanelから呼ばれる） |
| `SetMaxFov(float fov)` | 最大FOV設定（1.0～179.0にClamp）（AvatarSettingsPanelから呼ばれる） |
| `GetMaxFov()` | 最大FOV取得（AvatarSettingsPanelから呼ばれる） |
| `SaveSettings()` | カメラ設定をPlayerPrefsに保存（高さ、ルックアット、FOV） |

#### 実行タイミング

- **LateUpdate()**: FOV制御、ルックアット制御
- **Start()**: PlayerPrefsから設定を自動復元（`LoadSettings()`）

#### PlayerPrefs保存項目

| キー | 型 | 説明 |
|------|------|------|
| `camera_height` | float | カメラY軸高さ |
| `camera_lookAtEnabled` | int | ルックアットON/OFF（1/0） |
| `camera_minFov` | float | 最小FOV（近距離時・望遠） |
| `camera_maxFov` | float | 最大FOV（遠距離時・広角） |

#### AvatarSettingsPanelとの連携

- **Camera Height Input**: `SetCameraHeight()` / `GetCameraHeight()` で値を設定・取得
- **Camera Look At Toggle**: `SetLookAtEnabled()` でリアルタイムON/OFF
- **Save Button**: `SaveSettings()` でカメラ設定をまとめて保存
- **起動時復元**: DynamicCameraController.Start() で自動復元（AvatarSettingsPanelは関与しない）

### Vision System（CharacterCamera）

キャラクターの Head ボーンに追従するカメラを設置し、キャラクターの「視点」映像を LLM API リクエストに画像として含める。
マルチモーダル対応 LLM がキャラクターの視点を理解して応答を生成できるようになる。

#### CharacterCameraController

namespace: `CyanNook.Character`

| フィールド | 型 | デフォルト | 説明 |
|-----------|-----|---------|------|
| `characterCamera` | `Camera` | - | キャラクター視点カメラ（Inspector参照） |
| `positionOffset` | `Vector3` | `(0, 0.06, 0.1)` | Headボーンからのローカル位置オフセット（目の位置調整用） |
| `rotationOffset` | `Vector3` | `(0, 0, 0)` | 回転オフセット（度） |
| `captureWidth` | `int` | `512` | キャプチャ解像度（幅） |
| `captureHeight` | `int` | `512` | キャプチャ解像度（高さ） |
| `jpegQuality` | `int` | `75` | JPEG品質 (0-100) |
| `alwaysRender` | `bool` | `false` | 常時レンダリング（デバッグ用） |

| メソッド | 説明 |
|---------|------|
| `SetVrmInstance(Vrm10Instance)` | Animator から Head ボーンを取得、RenderTexture を作成して camera.targetTexture に設定。`LoadSettings()` で PlayerPrefs から `llm_cameraPreview` を読み込み `alwaysRender` を復元 |
| `CaptureImageAsBase64()` | `camera.Render()` → ReadPixels → EncodeToJPG → base64 文字列を返す |
| `GetRenderTexture()` | デバッグ UI 用に RenderTexture を公開 |
| `SetAlwaysRender(bool)` | 常時レンダリングの有効/無効を切り替え。PlayerPrefs に `llm_cameraPreview` として保存 |

**PlayerPrefs キー:**
- `llm_cameraPreview` (int): カメラプレビュー表示ON/OFF（1=ON, 0=OFF）

#### ロールフリー・コンストレイント

`[DefaultExecutionOrder(20001)]` により `CharacterLookAtController`(20000) の後に実行。
LookAt が Head ボーンに回転を適用した後にカメラ位置を更新するため、視線方向とカメラが連動する。

LateUpdate で Head ボーンの位置・回転をカメラに適用する。`Vector3.up` を使用してロール軸を常にゼロに保ち、画像が常に水平になるようにする。

```
position = headBone.position + headBone.rotation * positionOffset
forward  = headBone.rotation * Quaternion.Euler(rotationOffset) * Vector3.forward
rotation = Quaternion.LookRotation(forward, Vector3.up)  ← ロール軸を常に水平化
```

#### オンデマンドレンダリング

負荷軽減のため、通常は `camera.enabled = false` とし、画像が必要な時のみ `camera.Render()` を呼び出す。

| モード | camera.enabled | レンダリング方式 |
|--------|---------------|----------------|
| 通常（`alwaysRender=false`） | `false` | `CaptureImageAsBase64()` 内で `camera.Render()` を手動実行 |
| デバッグ（`alwaysRender=true`） | `true` | 毎フレーム自動レンダリング（UI プレビュー表示用） |

#### Sleep/Outing中のVision抑制

Sleep中・Outing中は `ChatManager.IsVisionSuppressed` プロパティにより画像キャプチャをスキップする。
`useVision` 設定自体は変更しないため、状態解除後は自動的に画像送信が再開される。

| 状態 | 判定条件 | 抑制理由 |
|------|----------|----------|
| Sleep | `sleepController.IsSleeping` | 睡眠中に部屋の中が見えるのは演出として不自然 |
| Outing | `outingController.IsOutside` | 外出中はUnity背景が見えているだけで無意味 |

適用箇所: `SendChatMessage()` および `SendAutoRequest()` 内の Vision キャプチャ条件。

#### Visible Objects（視界内オブジェクト説明）

キャラクターカメラの Frustum（視錐台）内にあるオブジェクトの説明テキストをLLMプロンプトに注入する。
Vision画像だけでは認識しにくいCGオブジェクトを、テキストで補足してLLMの理解を助ける。

##### SceneObjectDescriptor

namespace: `CyanNook.Core`

シーン内のオブジェクトに説明文を付与するコンポーネント。`[RequireComponent(typeof(Renderer))]`。

| フィールド | 型 | 説明 |
|-----------|-----|------|
| `objectName` | `string` | オブジェクトの表示名（例: 木製の椅子、花瓶） |
| `description` | `string` | 詳細説明（例: 白い花が生けてある小さな花瓶）。空欄可 |

| メソッド | 説明 |
|---------|------|
| `ObjectRenderer` | Frustum判定用のRendererプロパティ（キャッシュ付き） |
| `GetPromptText()` | プロンプト用テキスト生成。descriptionが空なら`objectName`のみ、あれば`objectName（description）`形式 |

##### VisibleObjectsProvider

namespace: `CyanNook.Chat`

| フィールド | 型 | 説明 |
|-----------|-----|------|
| `cameraController` | `CharacterCameraController` | キャラクターカメラコントローラー参照 |

| メソッド | 説明 |
|---------|------|
| `GenerateVisibleObjectsText()` | Frustum内のSceneObjectDescriptorを収集し、改行区切りのリストテキストを返す |

##### 処理フロー

```
LLMリクエスト時（SendChatMessage / SendAutoRequest）
  ↓
ChatManager.ReplaceDynamicPlaceholders() / BuildDynamicInputs()
  ↓ useVision=true && !IsVisionSuppressed
VisibleObjectsProvider.GenerateVisibleObjectsText()
  ↓
GeometryUtility.CalculateFrustumPlanes(characterCamera)
  ↓
FindObjectsByType<SceneObjectDescriptor>()
  ↓ 各オブジェクトについて
Renderer.enabled チェック → GeometryUtility.TestPlanesAABB(frustumPlanes, renderer.bounds)
  ↓ Frustum内のオブジェクトのみ
GetPromptText() でテキスト収集
  ↓
{visible_objects} プレースホルダに注入
```

**出力例:**
```
- ユーザーのWebカメラ映像（ユーザーの顔が映っている）
- 木製の椅子
- 白い花瓶（白い花が生けてある小さな花瓶）
```

##### 抑制条件

`{visible_objects}` は Vision 画像送信と同じ条件で抑制される:
- `useVision = false` の場合: 空文字
- Sleep中 / Outing中（`IsVisionSuppressed`）: 空文字
- Renderer.enabled = false のオブジェクト: リストから除外（WebCam/ScreenCapture OFF時に自動除外）

##### シーンセットアップ

1. VisibleObjectsProvider コンポーネントをシーンに配置し、`cameraController` をアサイン
2. ChatManager の `visibleObjectsProvider` にアサイン
3. 認識させたいオブジェクト（WebCamのQuad、花瓶、椅子など）に SceneObjectDescriptor を付けて `objectName` と任意で `description` を設定
4. プロンプト（characterPrompt または responseFormatPrompt）に `{visible_objects}` プレースホルダを追加

#### プロバイダー別 Vision 画像送信方式

**Ollama / LM Studio:**
- リクエスト JSON に `images` フィールドとして base64 文字列を直接含める
- `JsonUtility` では条件付きフィールドが困難なため、画像あり時は `BuildRequestJsonWithImage()` で手動 JSON 構築

```json
{
  "model": "llava",
  "prompt": "...",
  "system": "...",
  "stream": true,
  "options": { "temperature": 0.7 },
  "images": ["base64_encoded_string"]
}
```

**Dify:**
- 2ステップ方式: まず `/files/upload` で画像をアップロードし、取得した `upload_file_id` をチャットリクエストの `files` フィールドで参照
- 画像アップロード失敗時は警告ログを出力し、画像なしでリクエストを続行（グレースフルデグラデーション）

```
① POST /files/upload (multipart/form-data: file + user)
   → Response: { "id": "file_id", "name": "..." }

② POST /chat-messages
   → Body: { ..., "files": [{"type":"image","transfer_method":"local_file","upload_file_id":"file_id"}] }
```

### WebCam Display（ユーザー映像表示）

ユーザーPCのWebカメラ映像を3Dシーン内のQuadに表示し、キャラクターカメラ経由でLLMがユーザーの姿を認識できるようにする。

#### WebCamDisplayController

namespace: `CyanNook.Core`

Quadオブジェクト（MeshRenderer）にアタッチして使用する。

| フィールド | 型 | デフォルト | 説明 |
|-----------|-----|---------|------|
| `deviceName` | `string` | `""` | 使用するWebカメラデバイス名（空欄=デフォルト） |
| `requestedWidth` | `int` | `640` | 要求解像度（幅） |
| `requestedHeight` | `int` | `480` | 要求解像度（高さ） |
| `requestedFPS` | `int` | `30` | 要求フレームレート |
| `mirrorHorizontal` | `bool` | `true` | 映像を水平反転（フロントカメラ用） |
| `autoPlay` | `bool` | `true` | 起動時に自動再生 |

| メソッド | 説明 |
|---------|------|
| `StartWebCam()` | Webカメラを起動してQuadに表示開始 |
| `StopWebCam()` | Webカメラを停止、Rendererを無効化 |
| `IsPlaying` (property) | 再生中かどうか |

**Unity Lifecycle:**
- **Start()**: `LoadSettings()` で PlayerPrefs から `llm_webCam` を読み込み `autoPlay` を復元。`autoPlay=true` の場合は自動的に `StartWebCam()` を呼び出し

**PlayerPrefs キー:**
- `llm_webCam` (int): WebCam表示ON/OFF（1=ON, 0=OFF）

#### 映像の流れ

```
ユーザーPC の Webカメラ
    ↓ WebCamTexture
Quad (3Dシーン内) に Unlit マテリアルで表示
    ↓ シーンの一部として存在
CharacterCameraController が Head ボーンからシーンをレンダリング
    ↓ RenderTexture → base64 JPEG
ChatManager → LLMClient → Provider
    ↓
LLM がキャラクター視点画像（Webカメラ映像含む）を認識
```

#### シーンセットアップ（デザイナー作業）

1. Quad を作成し、メインカメラ付近（キャラクターの正面方向）に配置
2. WebCamDisplayController を Quad に追加
3. UIController の `webCamDisplayController` に Quad をドラッグ＆ドロップ
4. DebugUI の WebCam トグルで ON/OFF 可能

#### 水平反転（ミラー）

フロントカメラは左右反転した映像を返すため、`material.mainTextureScale.x = -1` でUVを反転し自然な見た目にする。`mirrorHorizontal` で切替可能。

### Screen Capture Display（デスクトップ画面表示）

ユーザーのデスクトップ画面（またはウィンドウ）を3Dシーン内のQuadに表示する。WebCam Displayと同様に、キャラクターカメラ経由でLLMがユーザーの画面を認識できるようになる。

ブラウザの Screen Capture API（`getDisplayMedia()`）を使用するため、WebGLビルド専用。

#### ScreenCaptureDisplayController

namespace: `CyanNook.Core`

Quadオブジェクト（MeshRenderer）にアタッチして使用する。

| フィールド | 型 | デフォルト | 説明 |
|-----------|-----|---------|------|
| `maxWidth` | `int` | `640` | キャプチャ解像度の最大幅 |
| `maxHeight` | `int` | `480` | キャプチャ解像度の最大高さ |
| `autoPlay` | `bool` | `false` | 起動時に自動再生（前回の設定を復元） |

| メソッド | 説明 |
|---------|------|
| `StartCapture()` | ブラウザの画面共有ダイアログを表示してキャプチャ開始 |
| `StopCapture()` | キャプチャを停止、Rendererを無効化 |
| `IsPlaying` (property) | キャプチャ中かどうか |

**Unity Lifecycle:**
- **Start()**: `LoadSettings()` で PlayerPrefs から `llm_screenCapture` を読み込み `autoPlay` を復元。`autoPlay=true` の場合は自動的に `StartCapture()` を呼び出し
- **Update()**: キャプチャ開始待機中はjslib側の状態をポーリングして検知（SendMessageフォールバック）。キャプチャ中はjslibからフレームデータを取得し `Texture2D` に書き込み

**PlayerPrefs キー:**
- `llm_screenCapture` (int): Screen Capture表示ON/OFF（1=ON, 0=OFF）

#### jslib プラグイン（ScreenCapture.jslib）

| 関数 | 説明 |
|------|------|
| `ScreenCapture_Start(maxW, maxH, objName, methodName)` | `getDisplayMedia()` で画面共有開始。結果をSendMessageで通知（※非同期コールバックのため届かない場合あり、C#側でポーリングフォールバックあり） |
| `ScreenCapture_Stop()` | ストリーム停止、リソース解放 |
| `ScreenCapture_IsCapturing()` | キャプチャ中なら1 |
| `ScreenCapture_IsFrameReady()` | 新フレームが利用可能なら1 |
| `ScreenCapture_GetWidth/Height()` | 実際のキャプチャ解像度 |
| `ScreenCapture_UpdateBuffer()` | フレームデータをjslib内部バッファ（`_malloc`確保）に書き込み。1=成功, 0=フレームなし |
| `ScreenCapture_GetBufferPtr()` | 内部バッファのポインタを返す（Unity側で`LoadRawTextureData(IntPtr)`に使用） |
| `ScreenCapture_GetBufferSize()` | 内部バッファのサイズ（バイト）を返す |

**処理フロー:**
```
getDisplayMedia() → <video> 要素 → requestAnimationFrame ループ
    ↓ canvas.drawImage()
<canvas> でフレーム描画 → getImageData()
    ↓ HEAPU8.set()
jslib 内部バッファ（_malloc 確保、RGBA）に書き込み
    ↓ ScreenCapture_GetBufferPtr() でポインタ取得
Texture2D.LoadRawTextureData(IntPtr, size) → Texture2D.Apply()
    ↓
Quad に Unlit マテリアルで表示
```

#### 制約・注意事項

- **WebGL専用**: `getDisplayMedia()` はブラウザAPIのためWebGLビルドでのみ動作
- **ユーザー許可が毎回必要**: ブラウザの画面共有ダイアログでユーザーが選択する（プログラムから直接指定不可）
- **選択可能なソース**: 画面全体 / ウィンドウ / ブラウザタブ（ブラウザ依存）
- **ブラウザ側からの停止**: ユーザーがブラウザUIで共有を停止した場合、`OnScreenCaptureStopped` コールバックで検知
- **SendMessageフォールバック**: jslib内の二重非同期コールバック（Promise `.then()` → `loadedmetadata` イベント）内からの `SendMessage` がUnity側に届かない場合があるため、C#側の `Update()` で `ScreenCapture_IsCapturing()` をポーリングして検知する仕組みを併用
- **モバイル非対応**: PCブラウザ向け機能（スマホのWebGLでは`getDisplayMedia()`が利用不可）

#### シーンセットアップ（デザイナー作業）

1. Quad を作成し、部屋内に配置（WebCam用Quadとは別のオブジェクト）
2. ScreenCaptureDisplayController を Quad に追加
3. LLMSettingsPanel の `screenCaptureDisplayController` に Quad をドラッグ＆ドロップ
4. LLM設定パネルの Screen Capture トグルで ON/OFF 可能

### 自律リクエスト（IdleChat）

ユーザーの入力がない時間が一定を超えたら、LLMに自動でリクエストを送り、キャラクターが自発的に話しかけてくる機能。

#### IdleChatController

namespace: `CyanNook.Chat`

| フィールド | 型 | デフォルト | 説明 |
|-----------|-----|---------|------|
| `chatManager` | `ChatManager` | - | ChatManager参照 |
| `autoRequestEnabled` | `bool` | `false` | 自律リクエストの有効/無効 |
| `cooldownDuration` | `float` | `10` | 応答完了後、次のリクエストまでの待機秒数 |
| `idlePromptMessage` | `string` | (下記参照) | LLMに送るシステムメッセージ |

| メソッド | 説明 |
|---------|------|
| `SetEnabled(bool)` | 自律リクエストのON/OFF切替。PlayerPrefs に保存 |
| `OnUserMessageSent()` | ユーザー入力時にタイマーリセット |
| `SetCooldownDuration(float)` | クールダウン時間を設定。PlayerPrefs に保存 |
| `SetIdlePromptMessage(string)` | 自律リクエストメッセージを設定。PlayerPrefs に保存 |

#### 状態フロー

```
[ユーザー入力 or LLM応答完了]
    ↓
Cooldown (N秒) ── 次のリクエストまでの待機
    ↓
SendAutoRequest ── ChatManager.SendAutoRequest()
    ↓
WaitingForResponse ── LLM処理中（タイマー停止）
    ↓
├─ action="ignore" → Cooldown に戻る（履歴に残さない、メッセージ有りなら表示）
├─ テキスト有り → メッセージ表示 → Cooldown
└─ ユーザー入力割り込み → 自律リクエスト中断 → ユーザー入力を処理
```

#### ユーザー入力優先（自律リクエスト中断）

自律リクエスト（WaitingForResponse）中にユーザーが入力した場合、自律リクエストを中断してユーザー入力を優先する。

```
WaitingForResponse（自律リクエスト中）
    ↓ ユーザー入力
ChatManager.SendChatMessage():
    ├─ _isAutoRequest == true を確認
    ├─ LLMClient.AbortRequest() でコルーチン停止
    ├─ _isAutoRequest = false
    ├─ ChatState → Idle
    └─ ユーザーメッセージを通常フローで送信
```

- `LLMClient.AbortRequest()` は実行中のコルーチンを `StopCoroutine()` で停止し、`_isProcessing` をリセットする
- ローカルLLM側はリクエスト処理を継続する場合があるが、レスポンス受信側がいなくなるだけなので実用上問題ない
- `IdleChatController.OnUserMessageSent()` は常にタイマーをリセットする（WaitingForResponse中でも中断後の再開に備える）

### Cronスケジューラー

`StreamingAssets/cron/` フォルダ内のJSONファイルで定義されたスケジュールに基づき、定期的にLLMへ自動リクエストを送信する上級者向け機能。
設定UIではON/OFFトグルのみ表示。ジョブ定義の追加・編集はJSONファイルを直接操作する。

#### CronJobData

namespace: `CyanNook.Core`

| フィールド | 型 | デフォルト | 説明 |
|-----------|-----|---------|------|
| `id` | `string` | - | ジョブの一意識別子 |
| `name` | `string` | - | 表示用名称（ログ出力用） |
| `enabled` | `bool` | `true` | ジョブ単位の有効/無効 |
| `schedule` | `string` | - | cron式（5フィールド: 分 時 日 月 曜日） |
| `prompt` | `string` | - | LLMに送信するプロンプト |
| `cancelSleepOrOuting` | `bool` | `false` | Sleep/Outingをキャンセルして実行する（false=スキップ） |

#### CronScheduler

namespace: `CyanNook.Chat`

| フィールド | 型 | デフォルト | 説明 |
|-----------|-----|---------|------|
| `chatManager` | `ChatManager` | - | ChatManager参照 |
| `sleepController` | `SleepController` | - | Sleep状態判定用 |
| `outingController` | `OutingController` | - | Outing状態判定用 |
| `schedulerEnabled` | `bool` | `false` | スケジューラーの有効/無効 |
| `autoReloadInterval` | `float` | `0` | 自動リロード間隔（分）。0=無効 |

| メソッド | 説明 |
|---------|------|
| `SetEnabled(bool)` | スケジューラーのON/OFF切替。PlayerPrefs に保存 |
| `SetAutoReloadInterval(float)` | 自動リロード間隔を設定（分）。0で無効。PlayerPrefs に保存 |
| `Reload()` | ジョブファイルを再読み込み |

| PlayerPrefs キー | 型 | 説明 |
|-----------------|-----|------|
| `cronSchedulerEnabled` | int | スケジューラーON/OFF |
| `cronAutoReloadInterval` | float | 自動リロード間隔（分）。0=無効 |

#### cron式記法

```
┌───── 分 (0-59)
│ ┌───── 時 (0-23)
│ │ ┌───── 日 (1-31)
│ │ │ ┌───── 月 (1-12)
│ │ │ │ ┌───── 曜日 (0-6, 0=日曜)
│ │ │ │ │
* * * * *
```

| 記法 | 例 | 説明 |
|------|-----|------|
| `*` | `* * * * *` | 毎分 |
| 固定値 | `0 9 * * *` | 毎日9:00 |
| カンマ | `0,30 * * * *` | 毎時0分と30分 |
| 範囲 | `0 9-17 * * *` | 9:00〜17:00の毎時0分 |
| ステップ | `*/5 * * * *` | 5分ごと |
| 範囲+ステップ | `0 9-17/2 * * *` | 9:00〜17:00の2時間ごと |

#### ジョブファイル形式

配置先: `StreamingAssets/cron/`（1ファイル1ジョブ、ファイル名は任意）

```json
{
    "id": "morning_greeting",
    "name": "朝の挨拶",
    "enabled": true,
    "schedule": "0 9 * * *",
    "prompt": "[SYSTEM]: 現在の時刻は朝9時です。ユーザーに朝の挨拶をしてください。",
    "cancelSleepOrOuting": true
}
```

ファイルは `file_manifest.json` に登録が必要（WebGL環境ではディレクトリ列挙不可のため）。

#### 動作フロー

```
[毎分チェック]
    ↓
cron式マッチング（全ジョブを評価）
    ↓
├─ enabled=false → スキップ
├─ Sleep/Outing中:
│   ├─ cancelSleepOrOuting=true → Sleep/Outingをキャンセルして実行
│   │   ├─ Sleep中 → ChatManager.SendCronWakeUpRequest(prompt)
│   │   │   ├─ wakeUpSystemMessage + cronPrompt を合体
│   │   │   ├─ interact_sleep_ed再生と並行してLLMリクエスト
│   │   │   └─ ed完了後にレスポンス処理（ユーザー起床と同じフロー）
│   │   └─ Outing中 → ChatManager.SendCronEntryRequest(prompt)
│   │       ├─ entryPromptMessage + cronPrompt を合体
│   │       ├─ Entry再生と並行してLLMリクエスト（通常のentryPrompt送信は抑制）
│   │       └─ Entry完了後にレスポンス処理
│   └─ cancelSleepOrOuting=false → スキップ
└─ 通常 → プロンプトをキューに追加
    ↓
[キュー送信]
├─ ChatManager.Idle → 即座にSendAutoRequest
└─ ChatManager.Busy → レスポンス完了後に自動送信
```

**キューイング方式:**
- ChatManagerがビジー時（ユーザー入力待ち、他の自動リクエスト処理中など）はキューに保留
- `OnChatResponseReceived` / `OnError` イベントでキューからデキューして送信
- キュー内の複数ジョブは先着順（FIFO）で処理
- プロンプトは `ChatManager.SendAutoRequest()` 経由で送信（IdleChatと同じパイプライン）

**Sleep/Outingキャンセル方式:**
- `cancelSleepOrOuting=true` のジョブがSleep/Outing中に発火すると、状態をキャンセルして実行
- Sleep: ユーザー起床と同じフロー（ed再生 + LLM並行リクエスト + レスポンスキューイング）
- Outing: Entry再生 + LLM並行リクエスト + Entry完了後にレスポンス処理
- 合体プロンプト: `wakeUpSystemMessage`/`entryPromptMessage` + `\n` + cronPrompt
- 同一分に複数のcancelジョブがある場合、最初のもののみがキャンセルを実行し、後続は通常キューへ
- リクエストは履歴に追加しない（レスポンスのみ履歴に追加される）

#### action フィールド（LLMResponseData / LlmResponseHeader）

`action` はキャラクターの「次の行動」を示す。移動やインタラクトを含む。

| action | 動作 |
|--------|------|
| `"move"` | target に向かって移動（target.type:talk でTalk状態） |
| `"interact_sit"` | 座れる家具に移動して座る（同種インタラクション中は別の家具をランダム選択） |
| `"interact_sleep"` | 寝られる家具に移動して寝る（同種インタラクション中は別の家具をランダム選択） |
| `"interact_exit"` | 出口に移動して退出 |
| `"ignore"` / `null` | 現在のステートを維持（移動・インタラクトなし） |

会話の有無は `reaction` または `message` フィールドの有無で判定（`speak` は廃止）。
action が `ignore` でも emote / emotion は反映される。

#### 自律リクエスト時の動作

- **Thinking状態**: 表示しない（`OnThinkingStarted` を発火しない）
- **履歴**: idle promptは履歴に追加しない。テキスト有り時はアシスタント応答のみ追加
- **ignore + テキスト無し時**: 表情やアニメーションの変更なし

#### デフォルトのidle promptメッセージ

```
[SYSTEM]: ユーザーは黙っています。
話しかけても、一人で行動しても、何もしなくても構いません。
あなたの今の気分に合った行動を選んでください。
```

**ポイント**: 「話すか話さないか」の二択ではなく、行動（move/interact_sit等）も選択肢として提示する。
これにより idle 中でもキャラクターが散歩したり座ったりする自律行動が発生する。

#### 設定の保存

**PlayerPrefs キー:**
- `idleChatEnabled` (int): 自律リクエストON/OFF（1=ON, 0=OFF）
- `idleChatCooldown` (float): クールダウン秒数
- `idleChat_message` (string): 自律リクエストメッセージ

**Unity Lifecycle:**
- **Start()**: `LoadSettings()` で PlayerPrefs から全設定を読み込み
- 各 Set メソッド (`SetEnabled`, `SetIdleTimeout`, `SetCooldownDuration`, `SetIdlePromptMessage`) は変更時に自動的に PlayerPrefs に保存

### 退屈ポイント（BoredomController）

キャラクターの「退屈度」を数値で管理し、LLMの行動選択に影響を与えるシステム。
LLMは命を持たない＝寿命がない＝時間が惜しいという概念がないため、
時間経過による不満をシミュレートすることで、ユーザーに従順すぎない性格を実現する。

namespace: `CyanNook.Character`

#### フィールド

| フィールド | 型 | デフォルト | 説明 |
|-----------|-----|---------|------|
| `increaseRate` | `float` | `1` | 退屈ポイント自然増加レート（ポイント/分）。0で自然増加OFF |
| `happyFactor` | `float` | `-3.0` | happy感情の退屈度係数（負=退屈が減る） |
| `relaxedFactor` | `float` | `-1.5` | relaxed感情の退屈度係数（負=退屈が減る） |
| `angryFactor` | `float` | `2.0` | angry感情の退屈度係数（正=退屈が増える） |
| `sadFactor` | `float` | `3.0` | sad感情の退屈度係数（正=退屈が増える） |
| `surprisedFactor` | `float` | `1.0` | surprised感情の増幅係数（常に絶対値として使用） |
| `_bored` | `float` | `0` | 現在の退屈ポイント（0-100）。Inspector表示 |

#### プロパティ・メソッド

| プロパティ/メソッド | 説明 |
|-------------------|------|
| `Bored` (float) | 現在の退屈ポイント（0-100） |
| `BoredInt` (int) | プロンプト用の整数値 |
| `ApplyEmotionDelta(EmotionData)` | LLMレスポンスの感情データに基づいて退屈度を変動 |
| `SetPaused(bool)` | 蓄積の一時停止/再開（Sleep/Outing中に使用） |
| `Reset()` | 0にリセット（Outing帰還時等に使用） |

#### 値の増減ルール

退屈度は2つの独立したメカニズムで変動する:

**1. 自然増加（Update毎フレーム）:**
```
_bored += increaseRate / 60 * deltaTime
上限: 100
```
会話がない時間が続くと退屈が蓄積する。increaseRate=0で自然増加を無効化可能。

**2. 感情ベース変動（LLMレスポンス受信時）:**
```
baseDelta = (happy × happyFactor) + (sad × sadFactor) + (relaxed × relaxedFactor) + (angry × angryFactor)
surpriseMultiplier = (surprised + 1.0) × |surprisedFactor|
delta = baseDelta × surpriseMultiplier

_bored = Clamp(_bored + delta, 0, 100)
```

- ポジティブな感情（happy, relaxed）は負の係数で退屈を減少させる
- ネガティブな感情（angry, sad）は正の係数で退屈を増加させる
- surprised は方向を変えずに増減幅を増幅する（(surprised+1.0)で最低1.0倍保証）
- 係数はユーザーがUI設定パネルで自由に変更可能（ツンデレ等の性格カスタマイズ）

#### 呼び出し元

| 呼び出し元 | メソッド | タイミング |
|-----------|---------|-----------|
| `ChatManager.HandleLLMResponse()` | `ApplyEmotionDelta()` | LLM応答の感情データ受信時 |
| `SleepController` | `SetPaused(true/false)` | Sleep開始/終了時 |
| `OutingController` | `SetPaused(true/false)` | Outing開始/終了時 |

#### プロンプトへの連携

`ChatManager.ReplaceDynamicPlaceholders()` で `{bored}` プレースホルダを置換。
プロンプトで退屈度に応じた感情選択の指示を記述することで、
高退屈時にsadやangryを選びやすくなるなどの性格変化を実現する。

#### IdleChatとの連携

BoredomControllerとIdleChatControllerは独立して動作するが、相補的な関係にある:

```
[ユーザー無入力]
    ↓
BoredomController: bored値が時間経過で自然増加
IdleChatController: idleTimeout後にSendAutoRequest()
    ↓
LLMリクエスト時: プロンプトに {bored} = 高い値
    ↓
LLM応答: 退屈度を反映した感情・行動選択
    ↓
感情データ → ApplyEmotionDelta() → bored値変動
（ポジティブ感情: 減少 / ネガティブ感情: 増加）
```

#### 感情係数のカスタマイズ例

| 性格パターン | happyFactor | relaxedFactor | angryFactor | sadFactor | surprisedFactor |
|-------------|------------|--------------|------------|----------|----------------|
| デフォルト | -3.0 | -1.5 | 2.0 | 3.0 | 1.0 |
| ツンデレ | -3.0 | -1.5 | -2.0 | 3.0 | 1.0 |
| 感情的 | -5.0 | -2.0 | 4.0 | 5.0 | 2.0 |
| 穏やか | -1.0 | -1.0 | 0.5 | 1.0 | 0.5 |

### Sleep System（睡眠システム）

LLMが `action:interact_sleep` を返した時に開始される睡眠状態の管理システム。
睡眠中はキャラクターが「夢を見ている」という演出を行い、ユーザー発話または起床タイマーで覚醒する。

#### 設計思想

- 睡眠中の夢の内容はLLMのバックエンド側に委ねる（コンテキスト節約）
- 夢メッセージは会話履歴に含めない（デフォルト6メッセージのコンテキストを消費しない）
- 起床時刻の決定もLLMに委ねる（`sleep_duration`フィールド）
- 睡眠状態はローカルに永続化し、アプリ再起動後もsleep状態を復元する

#### 状態遷移

```
[Normal] ──LLMが interact_sleep 返却──→ [Sleep]
  ↑                                        │
  ├─ ユーザー発話 ─────────────────────────┤
  ├─ 起床タイマー満了 ─────────────────────┤
  └─ アプリ再起動（タイマー超過）──────────┘

[Sleep中の動作]
  ├─ interact_sleep タイムラインのループリージョンで継続
  ├─ 定期的にdreamメッセージをLLMに送信（IdleChat応用）
  ├─ LLM応答は内容に関わらず「Zzz...」と表示
  ├─ TTS無効（音声合成を行わない）
  ├─ ストリーミング中もヘッダー・テキスト・フィールドを抑制（ChatManager）
  ├─ LLM応答の action/target/emote は無視（sleep状態維持）
  ├─ BoredomController の蓄積を停止
  ├─ Vision画像キャプチャ抑制（IsVisionSuppressed）
  ├─ LightControlTrackクリップ（lightsOn=false）でライト消灯（リアルタイムLight + Emission + Lightmap OFF）
  └─ アプリ終了 → 再起動時もsleepループから再開（startOffで起動時から消灯）

[起床時]
  └─ LightControlTrackクリップ（lightsOn=true）でライト点灯（全て復元）
```

#### SleepController

新規クラス。睡眠状態の管理、夢メッセージ送信、起床処理を担当。

##### Inspector フィールド

| フィールド | 型 | デフォルト | 説明 |
|-----------|-----|----------|------|
| `chatManager` | ChatManager | - | ChatManager参照 |
| `interactionController` | InteractionController | - | InteractionController参照 |
| `furnitureManager` | FurnitureManager | - | FurnitureManager参照（起動時復帰用） |
| `defaultSleepDuration` | int | 30 | `sleep_duration` 未指定時のデフォルト値（分） |
| `minSleepDuration` | int | 5 | `sleep_duration` の最小値（分） |
| `maxSleepDuration` | int | 480 | `sleep_duration` の最大値（分） |
| `dreamInterval` | float | 300 | 夢メッセージの送信間隔（秒） |
| `dreamPromptMessage` | string | ※下記 | 夢メッセージのシステムプロンプト |
| `wakeUpSystemMessage` | string | ※下記 | 起床時にユーザーメッセージに付与するメッセージ |

**dreamPromptMessage デフォルト**:
`[system]あなたは睡眠中です。今日の出来事を夢として思い返してください。`

**wakeUpSystemMessage デフォルト**:
`[system]あなたは今起きたところです。`

##### 公開API

| メソッド | 説明 |
|---------|------|
| `EnterSleep(int durationMinutes, string furnitureId)` | 睡眠状態に遷移。タイマー設定、PlayerPrefs保存、IdleChat停止、Boredom停止（ライト制御はLightControlTrackに移行） |
| `ExitSleep(Action onWakeUpComplete)` | 起床処理。interact_sleep のloop脱出 → ed再生（CancelRegionスキップ） → Idle遷移 → コールバック |
| `WakeUpWithMessage(Action<LLMResponseData> onEdComplete)` | メッセージ付き起床。ed開始と同時にLLM送信を許可し、ed再生中にレスポンスをキューする。ed完了後に `onEdComplete(queuedResponse)` を呼び出す（null=LLM未応答） |
| `QueueWakeUpResponse(LLMResponseData response)` | 起床ed再生中にLLMレスポンスをキューする（ChatManagerから呼び出し） |
| `bool IsSleeping` | 睡眠中かどうか |
| `bool IsWakingUp` | 起床アニメーション再生中かどうか（ed再生中） |
| `bool ShouldStartAsSleep()` | アプリ起動時にsleep状態で開始すべきか（PlayerPrefs確認） |
| `void RestoreSleep(animCtrl, navCtrl)` | アプリ起動時のsleep状態復元（家具検索 → Warp(位置,回転) → InteractionController状態復元 → interact_sleep ループ開始）。ライトはRoomLightControllerの`startOff`で起動時から消灯済み |
| `SetDefaultSleepDuration(int minutes)` | デフォルト睡眠時間を設定（最小1分）+ PlayerPrefs保存 |
| `SetMinSleepDuration(int minutes)` | 最小睡眠時間を設定（最小1分）+ PlayerPrefs保存 |
| `SetMaxSleepDuration(int minutes)` | 最大睡眠時間を設定（最小1分）+ PlayerPrefs保存 |
| `SetDreamInterval(float seconds)` | 夢メッセージ間隔を設定（最小60秒）+ PlayerPrefs保存 |
| `SetDreamPromptMessage(string message)` | 夢メッセージプロンプトを設定 + PlayerPrefs保存 |
| `SetWakeUpSystemMessage(string message)` | 起床時システムメッセージを設定 + PlayerPrefs保存 |

##### 永続化データ（PlayerPrefs）

| Key | 型 | 説明 |
|-----|----|------|
| `sleep_state` | int (0/1) | 睡眠中フラグ |
| `sleep_wake_time` | string (ISO 8601) | 起床予定時刻 |
| `sleep_furniture_id` | string | 寝ている家具の instanceId |
| `sleep_defaultDuration` | int | デフォルト睡眠時間（分） |
| `sleep_minDuration` | int | 最小睡眠時間（分） |
| `sleep_maxDuration` | int | 最大睡眠時間（分） |
| `sleep_dreamInterval` | float | 夢メッセージ間隔（秒） |
| `sleep_dreamMessage` | string | 夢メッセージプロンプト |
| `sleep_wakeUpMessage` | string | 起床時システムメッセージ |

#### sleep_duration（起床タイマー）

LLMが `action:interact_sleep` と共に返す睡眠時間（分単位）。

##### LLMからの取得

```json
{
  "action": "interact_sleep",
  "sleep_duration": 30,
  "message": "少し眠いな…30分だけ寝よう"
}
```

##### 値の処理

1. LLMが値を返した場合: `Clamp(value, minSleepDuration, maxSleepDuration)`
2. LLMが値を返さなかった場合: `defaultSleepDuration` を使用
3. 起床予定時刻 = `DateTime.Now + TimeSpan.FromMinutes(duration)`
4. ISO 8601形式でPlayerPrefsに保存

##### プロンプトへの指示（推奨）

LLMに離散値で選ばせると安定する:
```
sleep_duration は 15, 30, 60, 120, 240 のいずれかを選択してください。
```

#### Sleep中のLLM応答処理

```
LLM応答受信（sleep中、_isWakeUpRequest == false の場合）
├─ ストリーミング中:
│   ├─ HandleStreamHeader → 無視（return）
│   ├─ HandleStreamText → 無視（return）
│   └─ HandleStreamField → 無視（return）
├─ 応答完了時:
│   ├─ action, target, emote → すべて無視
│   ├─ emotion → 無視（sleep表情を維持）
│   ├─ message → 「Zzz...」に置換して表示
│   ├─ TTS → スキップ
│   └─ 会話履歴 → 追加しない

※ _isWakeUpRequest == true の場合はsleep中でもストリーミング・応答を通常処理する
```

#### 夢メッセージ（Dream Chat）

IdleChatController と同様の仕組みで、sleep中に定期的にLLMへメッセージを送信する。

| 項目 | IdleChat | Dream Chat |
|------|----------|------------|
| トリガー | アイドルタイムアウト | dreamInterval（固定間隔） |
| 送信メッセージ | `idlePromptMessage` | `dreamPromptMessage` |
| 応答表示 | 通常表示 | 「Zzz...」 |
| 応答のaction実行 | する | 無視 |
| 会話履歴保存 | する | しない |
| TTS | 有効 | 無効 |

Sleep中はIdleChatControllerの通常タイマーを停止し、SleepController内の夢タイマーが稼働する。

##### Dream Prompt 送信のリトライ機構

`EnterSleep()` は初回Dream Promptを即送信するが、ChatManagerが別のリクエストを処理中（例: ユーザーの音声入力とinteract_sleepが競合した場合）だと `SendDreamMessage()` が失敗する。この場合、`_pendingDreamMessage` フラグを立て、Update()で毎フレームChatManagerのIdle状態を監視し、Idleになり次第リトライする。

```
EnterSleep()
├─ SendDreamMessage()
│   ├─ ChatState == Idle → 送信成功
│   └─ ChatState != Idle → _pendingDreamMessage = true（保留）
│
Update() (毎フレーム)
├─ _pendingDreamMessage == true?
│   ├─ ChatState == Idle → リトライ送信 → _pendingDreamMessage = false
│   └─ ChatState != Idle → 待機（dreamTimerも進めない）
├─ dreamTimer カウントダウン
│   └─ 0到達 → SendDreamMessage() → タイマーリセット
```

ExitSleep時には `_pendingDreamMessage` をリセットする。

#### Wake-up フロー

##### トリガー1: ユーザー発話（並行処理フロー）

```
ユーザー発話（ChatManager.SendChatMessage）
├─① 夢メッセージリクエスト中なら中断（AbortRequest）
├─② wakeUpSystemMessage をメッセージに付与
│   例: "[system]あなたは今起きたところです。\nおはよう！"
├─③ SleepController.WakeUpWithMessage(onEdComplete) 呼び出し
│   ├─ InteractionController.ExitLoopWithCallback(skipCancelRegion: true)
│   │   └─ interact_sleep の ed phase 再生（CancelRegionスキップでed全体を再生）
│   └─ ed完了時 → onEdComplete(queuedResponse) 発火
├─④ LLM送信（③と並行して即座に実行、fall-through）
│   ├─ _isWakeUpRequest = true（Thinking表示・ストリーミング処理の制御フラグ）
│   ├─ HandleRequestStarted: Thinking開始をスキップ
│   ├─ HandleStreamHeader/Text/Field: sleep中でもwake-up時は通常通り処理
│   └─ HandleLLMResponse: ed再生中ならQueueWakeUpResponse、完了済みなら即発火
└─⑤ ed完了時の処理（onEdComplete コールバック）
    ├─ queuedResponse != null → OnChatResponseReceived（Thinkingスキップ）
    └─ queuedResponse == null → talkController.StartThinking()（LLM未応答）

※ 将来拡張: ed開始時ではなくTimeline Signal到達時にLLM送信する場合は、
  WakeUpWithMessage に onSendMessage コールバックを追加し、Signal発火時に呼び出す
```

##### トリガー2: 起床タイマー満了

```
タイマー満了（Update監視）
├─① SleepController.ExitSleep(null) 呼び出し
│   ├─ InteractionController.ExitLoopWithCallback(skipCancelRegion: true)
│   │   └─ interact_sleep の ed phase 全体を再生
│   └─ ed完了 → sleep状態OFF（PlayerPrefs保存）
├─② IdleChatController再開
├─③ BoredomController再開
└─④ 通常のアイドル動作へ
```

##### トリガー3: アプリ再起動（タイマー超過済み）

```
アプリ起動時
├─ SleepController.ShouldStartAsSleep() チェック
│   ├─ sleep_state == false → 通常起動
│   └─ sleep_state == true
│       ├─ 現在時刻 < sleep_wake_time → RestoreSleep()（sleepループ開始）
│       └─ 現在時刻 >= sleep_wake_time → sleep_state OFF → 通常起動（interact_entry01再生）
```

##### RestoreSleep の詳細

```
RestoreSleep(animationController, navigationController):
├─ 家具検索（PlayerPrefs sleep_furniture_id）
├─ InteractionPointの位置・回転を取得
├─ navigationController.Warp(position, rotation)  ← 位置+回転の両方を復元
├─ interactionController.RestoreInteractionState(furniture, "sleep", pos, rot)
│   ├─ state = InLoop（ExitLoopWithCallbackが正しく動作するため）
│   ├─ BlendPivotをtarget位置に移動、VRMローカル座標をリセット
│   └─ furniture.Occupy()
├─ animationController.PlayState(Interact, "interact_sleep01", resumeAtLoop, skipBlend)
├─ IdleChat停止、Boredom停止
└─ ライトはRoomLightControllerのstartOffで起動時から消灯済み
```

**updateWhenOffscreen常時有効化:**
VRM読み込み時に `updateWhenOffscreen=true` を常時設定（VrmLoader）。
座り・寝ポーズなどでメッシュが大きく変形した際、バウンディングボックスが
実際のメッシュ位置と乖離し、ライト判定やフラスタムカリングに問題が生じるのを防ぐ。
VRM1体でのCPUコストは無視できるレベルのため、ポーズごとのON/OFFは行わない。

#### アプリ起動時の Sleep 復元（RestoreSleep）

```
RestoreSleep()
├─ PlayerPrefs から sleep_furniture_id 取得
├─ FurnitureManager から該当家具を検索
│   ├─ 見つかった → その家具で interact_sleep 開始（ループリージョンから直接開始）
│   └─ 見つからない → sleep_state OFF → 通常起動にフォールバック
├─ BoredomController 停止
├─ IdleChatController 停止
└─ 夢タイマー開始
```

**注意**: 復元時は移動（Approaching）をスキップし、直接ベッド位置に配置してループリージョンから再生する。

#### Sleep中に停止するシステム

| システム | Sleep中の動作 |
|---------|--------------|
| IdleChatController | 停止（夢タイマーに切替） |
| BoredomController | 蓄積停止（_bored値は維持） |
| LookAtController | 前方固定（lookForward）またはsleepポーズ準拠 |
| DynamicCameraController | 通常動作（カメラはsleep中も動く） |

#### CharacterController との連携

```
HandleChatResponse(response)
├─ SleepController.IsSleeping == true の場合
│   ├─ emotion → 無視
│   ├─ action/target/emote → 無視
│   ├─ message → 「Zzz...」に置換
│   └─ return（通常処理をスキップ）
└─ IsSleeping == false → 通常処理

HandleChatStateChanged(ChatState.WaitingForResponse)
├─ SleepController.IsWakingUp == true の場合
│   └─ Thinking状態遷移をスキップ（edアニメーション再生中のため）
└─ IsWakingUp == false → 通常のThinking遷移
```

#### CharacterSetup との連携

```
OnVrmLoaded()
├─ 既存のセットアップ処理
├─ SleepController の参照設定（vrmLoader参照含む）
├─ OutingController の参照設定
├─ SleepController.ShouldStartAsSleep() チェック
│   ├─ true → SleepController.RestoreSleep()（Entry/Idle開始をスキップ）
│   │   └─ ShowModelAfterAnimation（3フレーム後にRenderer有効化）
│   └─ false → OutingController.PlayEntry(→Idle, skipBlend: true)
│       └─ ShowModelAfterFrames（PlayEntry内、3フレーム後にRenderer有効化）
└─ OutingControllerがない場合: PlayState(Idle, skipBlend: true) + ShowModelAfterAnimation
```

#### sleep_duration のストリーミング対応

IncrementalJsonFieldParser で `sleep_duration` フィールドを検出した場合:
- CharacterController.HandleStreamField() で SleepController に渡す
- ただし sleep 状態への遷移は interact_sleep アニメーションのループ突入時に行う

#### 関連ファイル

| ファイル | 役割 |
|---------|------|
| `Scripts/Character/SleepController.cs` | 睡眠状態管理、夢タイマー、起床処理、WakeUpWithMessage並行処理 |
| `Scripts/Character/CharacterController.cs` | Sleep中の応答抑制、sleep_duration受け渡し、起床中のThinkingスキップ |
| `Scripts/Character/InteractionController.cs` | ExitLoopWithCallback(skipCancelRegion)でed全体再生を制御 |
| `Scripts/Character/CharacterSetup.cs` | 起動時のsleep復元チェック |
| `Scripts/Chat/ChatManager.cs` | Wake-up並行LLM送信、ストリーミング応答抑制、レスポンスキュー |
| `Scripts/Chat/LlmStreamHandler.cs` | 不完全JSON修復（RepairIncompleteJson） |
| `Scripts/Chat/IdleChatController.cs` | Sleep中の停止/再開 |
| `Scripts/Character/BoredomController.cs` | Sleep中の蓄積停止 |

### Outing System（外出システム）

LLMが `action:interact_exit` を返した時に開始される外出状態の管理システム。
キャラクターがドアから退室し、外出中は不在演出を行い、ゲーム再起動またはLLMの帰還判断で入室する。

#### 設計思想

- 外出状態は**非永続化**（アプリ再起動で必ず解除され、入室アニメーションから開始）
- 外出中もLLMとの通信は継続（定期メッセージで外での活動をシミュレート）
- 入室はLLMが `interact_entry01` アニメーションを選定した場合、またはユーザーが「Come Home」ボタンを押した場合
- ドア家具と連動したアニメーション再生（FurnitureAnimationController）

#### 状態遷移

```
[Normal] ──LLMが interact_exit 返却──→ [Exit Animation]
  ↑                                       │
  │                                       ▼
  │                                   [Outside]
  │                                       │
  ├─ アプリ再起動 ────────────────────────┤
  ├─ LLMが interact_entry01 選定 ──────────┤
  └─ ユーザーが「Come Home」ボタン押下 ────┘
                    ↓
              [Entry Animation] → [Normal]

[Outside中の動作]
  ├─ キャラクター非表示（VrmLoader.SetMeshVisibility(false)）
  ├─ 定期的にoutingメッセージをLLMに送信
  ├─ IdleChatController 停止
  ├─ BoredomController 蓄積停止
  ├─ UI「お出かけ中…」表示（LLM応答でも上書きしない）+ 「Come Home」ボタン表示
  ├─ Vision画像キャプチャ抑制（IsVisionSuppressed）
  └─ TTS・ストリーミング抑制（ChatManager側で制御）
```

#### OutingController

外出状態の管理、入退室アニメーション、定期メッセージ送信を担当。

##### Inspector フィールド

| フィールド | 型 | デフォルト | 説明 |
|-----------|-----|----------|------|
| `outingMessageInterval` | float | 5 | 外出中定期メッセージ間隔（分） |
| `outingPromptMessage` | string | ※下記 | 外出中定期メッセージのプロンプト |
| `entryPromptMessage` | string | ※下記 | 入室時のシステムメッセージ |
| `outingDisplayText` | string | "お出かけ中…" | 外出中のUI表示テキスト |
| `entryAnimationId` | string | "interact_entry01" | 入室アニメーションID |
| `doorFurnitureId` | string | "room01_door_01" | ドア家具のinstanceId |

##### 公開API

| メソッド | 説明 |
|---------|------|
| `EnterOuting()` | 外出状態に遷移。キャラクター非表示、IdleChat/Boredom停止、UI表示 |
| `ExitOuting()` | 外出状態を解除。IdleChat/Boredom再開、UI表示解除 |
| `PlayEntry(Action onComplete, bool skipBlend, bool suppressEntryPrompt)` | 入室アニメーション再生。ドアentry point配置 → NavMeshAgent無効化 → Root Motion移動 → 完了後Agent再有効化。`suppressEntryPrompt=true` で通常のEntry Prompt送信を抑制（Cron帰宅時用） |
| `bool IsOutside` | 外出中かどうか |
| `bool IsPlayingEntry` | 入室アニメーション再生中か |

#### PlayEntry フロー（入室アニメーション）

```
PlayEntry(onComplete, skipBlend, suppressEntryPrompt):
├─ 外出中なら ExitOuting()
├─ ドア家具を検索（doorFurnitureId）
├─ NavMeshAgent無効化（NavMesh外のentryポイント対応）
├─ ドアのInteract_entryポイントにキャラクターを配置
├─ AnimationStateType.Interact として entry アニメーション再生
│   └─ Root Motion有効: アニメーションでキャラクターが部屋に入る
├─ OnInteractionEndReached を購読（PlayState後に購読する）
│   └─ PlayState内部のStopDirectorForAssetChangeがイベントを発火するため
├─ ドア家具アニメーション同期再生（FurnitureAnimationController）
├─ ShowModelAfterFrames コルーチン（3フレーム後にモデル表示）
├─ SendEntryPromptWhenReady コルーチン（suppressEntryPrompt=false時のみ）
└─ OnEntryAnimationComplete（InteractionEndClip到達時）:
    ├─ イベント解除
    ├─ 家具アニメーション停止
    ├─ Root Motionの最終ワールド位置を取得
    ├─ NavMesh.SamplePositionで有効位置を検索（1m → 5mフォールバック）
    ├─ 親transformを最終位置に移動
    ├─ NavMeshAgent再有効化 + Warp(最終位置)
    ├─ characterTransformローカル座標リセット
    └─ onComplete コールバック
```

**Entry再生中のレスポンスガード:**
Entry再生中（`IsPlayingEntry == true`）は、CharacterControllerの以下3箇所でLLMレスポンスをブロックする：
- `HandleChatResponse` — action/target/emote処理をスキップ
- `HandleStreamField` — ストリーミングフィールド処理をスキップ
- `HandleChatStateChanged` — Thinking状態遷移をスキップ

これによりEntry Promptの応答が到着してもentryタイムラインが上書きされることを防止する。

**NavMeshAgent無効化の理由:**
Entry pointはドアの外側（NavMesh外）に配置される場合がある。
NavMeshAgentが有効だとNavMesh外の位置に配置できないため、
entry再生中はAgentを無効化しRoot Motionで移動させる。

#### 退室→外出フロー

```
LLM応答: action="interact_exit"
├─ CharacterController: 通常のインタラクションフロー
│   ├─ ドア家具に接近（Approaching）
│   ├─ interact_exit01_st 再生
│   ├─ InLoop（ドア前ポーズ維持）
│   └─ ExitLoop → interact_exit01_ed 再生
├─ InteractionController.OnInteractionComplete
│   └─ CharacterController.OnInteractionExitComplete()
│       ├─ VrmLoader.SetMeshVisibility(false)
│       └─ OutingController.EnterOuting()
```

#### 起動時の入室フロー

```
CharacterSetup.OnVrmLoaded():
├─ Sleep復元チェック（優先）
│   ├─ true → RestoreSleep（Entry再生をスキップ）
│   └─ false → OutingController.PlayEntry(onComplete, skipBlend: true)
│       └─ onComplete: PlayState(Idle)
```

#### Outing中のLLM応答処理

Sleep中と同様、Outing中はChatManager側でストリーミング・TTS・フィールド処理を抑制する。
UI側の表示抑制（UIController.IsOutingActive）に加え、ChatManager内で以下のガードを実施:

```
LLM応答受信（Outing中、_isCronEntryRequest == false の場合）
├─ ストリーミング中:
│   ├─ HandleStreamText → 無視（return）
│   ├─ HandleStreamField → 無視（return）
│   └─ HandleRequestCompleted → OnStreamingComplete スキップ
├─ 応答完了時:
│   ├─ TTS → スキップ（ブロッキング応答時の SynthesizeAndPlay も抑制）
│   └─ UI表示 → 「お出かけ中…」維持（UIController側で抑制）

※ _isCronEntryRequest == true の場合はOuting中でもストリーミング・TTS・応答を通常処理する
```

| 項目 | Outing定期メッセージ | Cron帰宅リクエスト |
|------|---------------------|-------------------|
| TTS | 無効 | 有効 |
| ストリーミングテキスト表示 | 抑制 | 有効 |
| ストリーミングフィールド処理 | 抑制 | 有効 |
| UI表示 | 「お出かけ中…」維持 | 通常表示 |

#### 永続化データ（PlayerPrefs）

| Key | 型 | 説明 |
|-----|----|------|
| `outing_messageInterval` | float | 定期メッセージ間隔（分） |
| `outing_promptMessage` | string | 外出中定期メッセージプロンプト |
| `outing_entryPromptMessage` | string | 入室時システムメッセージ |

#### 関連ファイル

| ファイル | 役割 |
|---------|------|
| `Scripts/Character/OutingController.cs` | 外出状態管理、入退室アニメーション、定期メッセージ |
| `Scripts/Character/CharacterController.cs` | interact_exit完了後のEnterOuting呼び出し、外出中LLM応答でのentry判定 |
| `Scripts/Character/CharacterSetup.cs` | 起動時のPlayEntry呼び出し、OutingController参照設定 |
| `Scripts/Furniture/FurnitureAnimationController.cs` | ドア家具連動アニメーション |

### FurnitureAnimationController（家具連動アニメーション）

家具がキャラクターアニメーションと同期してTimeline再生するためのコンポーネント。
ドアの開閉アニメーションなど、家具自体が動く演出に使用する。

#### 仕組み

- 家具のFBXから抽出したAnimationClipをTimeline（PlayableDirector）で再生
- キャラクターアニメーションの開始/停止に合わせてPlay/Stopを呼び出す
- **LateUpdateでルートボーン位置を復元**: AnimationTrackがルートボーン（Animator直下）を
  移動させてしまう問題を、LateUpdateで毎フレーム元の位置に戻すことで解決。
  子ボーン（Door等）の回転アニメーションはそのまま反映される。

#### 主要メソッド

| メソッド | 説明 |
|---------|------|
| `bool HasTimeline(string animId)` | 指定IDに対応するTimelineが登録されているか |
| `void Play(string animId)` | 対応するTimelineを再生（AnimatorバインドとWrapMode設定を含む） |
| `void StopWithCharacter()` | キャラクターアニメーション完了時に家具アニメーションも停止 |

### VRM初回読み込み時のT-pose回避

VRM読み込み直後はボーンがT-poseの状態にあるため、通常のInertialBlendで補間すると
T-poseからアニメーションポーズへのブレンドが目に見えてしまう。
これを防ぐため、以下の仕組みで初回読み込み時のT-poseを非表示にする。

#### フロー

```
VrmLoader.LoadBytesAsync(showMeshes: false)
├─ UniVRM が全 Renderer を disabled で生成（T-pose が描画されない）
├─ OnVrmLoaded()
│   ├─ PlayState(Idle, skipBlend: true) または RestoreSleep(skipBlend: true)
│   │   └─ skipBlend=true: InertialBlendTrack / フォールバックIB 両方をスキップ
│   │       → T-poseからの補間が発生しない
│   └─ StartCoroutine(ShowModelAfterAnimation)
│       ├─ yield return null × 3（PlayableDirector評価→Animator反映→確定待ち）
│       └─ VrmLoader.SetMeshVisibility(true) → 全 Renderer を有効化
└─ 将来: SetMeshVisibility の代わりにエフェクト演出で表示開始
```

#### skipBlend パラメータ

`PlayState(skipBlend: true)` は InertialBlend 全体をスキップする:
- **InertialBlendTrack**: `SetupInertialBlendTrack()` の呼び出し自体をスキップ
- **フォールバックIB**: `StartInertialBlendAllBones()` の呼び出しをスキップ

VRM初回読み込み時にのみ使用。通常の状態遷移では使用しない。

#### VrmLoader メッシュ表示制御

```csharp
public void SetMeshVisibility(bool visible);          // 全Rendererのenabled切り替え
public void SetUpdateWhenOffscreen(bool enabled);     // 全SkinnedMeshRendererのupdateWhenOffscreen切り替え
public bool showMeshesOnLoad = false;                  // UniVRMのshowMeshesパラメータに渡す
public bool disableCastShadow = true;                  // VRM全RendererのCast Shadowを無効化（セルフシャドウ除去）
```

#### 関連ファイル

| ファイル | 役割 |
|---------|------|
| `Scripts/Character/VrmLoader.cs` | `showMeshesOnLoad=false`、`SetMeshVisibility()` |
| `Scripts/Character/CharacterSetup.cs` | `skipBlend: true` 指定、`ShowModelAfterAnimation` コルーチン |
| `Scripts/Character/CharacterAnimationController.cs` | `PlayState(skipBlend)` でIB全スキップ、`EvaluateImmediate()` |
| `Scripts/Character/SleepController.cs` | `RestoreSleep` 内で `skipBlend: true` 使用 |

### システムプロンプト設計

ChatManager のプロンプトは2つのフィールドに分割されている:

| フィールド | 用途 | UI編集 |
|-----------|------|--------|
| `characterPrompt` | キャラクター設定（人格・性格・世界観） | 自由編集 |
| `responseFormatPrompt` | JSON出力形式の指示 | ロックトグルで保護可能 |

`GenerateSystemPrompt()` で `characterPrompt + "\n\n" + responseFormatPrompt` を結合した後、
プレースホルダを実行時の値に置換してからLLMに送信する。

**分割の目的:** 詳しくないユーザーがJSON記述部分を誤って編集し、パースエラーの原因となることを防ぐ。
AvatarSettingsPanelのロックトグル（デフォルトON）で `responseFormatPrompt` の入力欄を `interactable = false` にできる。

**プロバイダーによる動作の違い:**
- **Ollama/LM Studio**: 結合後のプロンプト + `ReplaceDynamicPlaceholders()` でプレースホルダを置換し、systemPrompt として送信。会話履歴を含むfullPrompt内のプレースホルダも同様に置換。
- **Dify**: システムプロンプトはDifyアプリ側で設定する。ChatManagerからは送信しない。動的プレースホルダの値は `BuildDynamicInputs()` で辞書形式に生成し、Dify APIの `inputs` フィールドで送信する（「Dify プロバイダー仕様」参照）。

#### プレースホルダ一覧

| プレースホルダ | 置換元 | 説明 |
|---------------|--------|------|
| `{character_name}` | `CharacterTemplateData.characterName` | キャラクター名 |
| `{character_description}` | `CharacterTemplateData.characterDescription` | キャラクター設定 |
| `{character_id}` | `CharacterTemplateData.templateId` | キャラクターID（例: chr001） |
| `{current_datetime}` | `DateTime.Now` | 現在の日時（例: `2026-02-12 15:30 (Thu)`） |
| `{current_room}` | `FurnitureManager.currentRoomId` | 現在の部屋ID |
| `{current_pose}` | ChatManager内部状態 | 現在のaction（例: ignore, move） |
| `{current_emotion}` | ChatManager内部状態 | 現在の支配的感情（例: happy, neutral） |
| `{available_furniture}` | `FurnitureManager.GenerateFurnitureListForPrompt()` | 部屋内の利用可能家具リスト |
| `{available_room_targets}` | `RoomTargetManager.GenerateTargetListForPrompt()` | 部屋内の名前付きターゲットリスト |
| `{spatial_context}` | `SpatialContextProvider.GenerateSpatialContextJson()` | 空間認識JSON（方向・距離付き） |
| `{bored}` | `BoredomController.BoredInt` | 退屈ポイント（0-100の整数） |
| `{visible_objects}` | `VisibleObjectsProvider.GenerateVisibleObjectsText()` | キャラクターカメラの視界内にあるオブジェクトの説明リスト（Vision有効時のみ） |

#### SpatialContextProvider（空間認識）

namespace: `CyanNook.Chat`

キャラクターの現在位置・向きを基準にした一人称視点の空間情報をJSON形式で生成する。
DynamicTargetControllerと同じ時計方向（1-12）の表記を使用し、LLMの入出力で方向表現を統一する。

**参照フィールド:**
- `characterTransform`: CharacterRoot（NavMeshAgent所在）
- `furnitureManager`: FurnitureManager
- `roomTargetManager`: RoomTargetManager

**`GenerateSpatialContextJson()` 出力例:**
```json
{
  "room_bounds": { "12": 3.2, "3": 1.5, "6": 4.0, "9": 2.1 },
  "furniture": [
    { "id": "room01_chair_01", "name": "椅子", "clock": 2, "distance": 1.2, "actions": ["sit"], "occupied": false }
  ],
  "room_targets": [
    { "name": "window", "clock": 11, "distance": 2.0 }
  ]
}
```

- **room_bounds**: NavMesh.Raycastで4方向（前12/右3/後6/左9）の端までの距離を計測
- **furniture**: 全家具の時計方向・距離・アクション・使用状態
- **room_targets**: 全ルームターゲットの時計方向・距離
- **時計方向**: `Vector3.SignedAngle(forward, toTarget, Vector3.up)` → 0-360° → clock(1-12)

#### プロンプト構成の指針

システムプロンプトには以下のセクションを含める:

1. **キャラクター設定**: 名前、性格、世界観
2. **応答フォーマット**: 単一JSON（reaction + message 含む）の形式説明
3. **フィールド説明**: emote / action / target / emotion / reaction / message の選択肢と意味
4. **空間認識**: 周囲の家具・ターゲット・部屋境界の方向・距離情報（JSON）
5. **行動の判断基準**: 自発的な会話・行動の動機付け（後述）
6. **ルール**: JSON記法の注意点、言語指定
7. **出力例**: 複数パターンの具体例（few-shot）

#### 行動の判断基準セクション

LLMはデフォルトで最も安全な選択（`action: "ignore"` のみ）に偏る傾向がある。
キャラクターに自律行動をさせるには、プロンプトで「行動してもよい」という許可と動機付けが必要。

**含めるべき内容:**

- 行動の選択肢と発動条件の例示
  - 退屈 → 歩き回る（move + dynamic）
  - 気になるもの → そちらを見る（ignore + dynamic）
  - 疲れた → 座る（interact_sit）
  - ユーザーと話したい → 前に移動（move + talk）
- 「何もしないで立っているだけは不自然」という動機付け
- 話さなくても体を動かすことを促す指示

#### 話さない場合の出力ルール

`reaction` と `message` の両方を省略する（空文字または含めない）。
コード側は `HasMessage`（reaction/message いずれか有り）で会話判定する。

#### 出力例のバリエーション

プロンプトに含める出力例は、LLMの出力傾向を大きく左右する（few-shot効果）。
以下のパターンを網羅することで、多様な行動が発生しやすくなる:

| パターン | action | target.type | メッセージ | 説明 |
|---------|--------|-------------|-----------|------|
| 話しかける | ignore | talk | あり | 基本的な会話 |
| 座りに行く | interact_sit | interact_sit | あり/なし | 家具インタラクション |
| 散歩する | move | dynamic | なし | 無言の移動 |
| 鏡を見に行く | move | mirror | あり/なし | RoomTargetへ移動 |
| 横を見る | ignore | dynamic | なし | 視線だけ変化 |
| 窓を見る | ignore | window | なし | RoomTargetを注視 |
| 話さない | ignore | talk | なし | 完全な沈黙 |

### Chat UI

リリース向けに刷新されたUI。`SettingsMenuController` による上部アイコンメニュー + 展開式設定パネル構成。

#### UIController（チャット入出力）

`InputMode` で JSON/Chat モードを切り替え可能。設定UI部分は各パネルに分離し、チャットI/Oに特化。

```csharp
public enum InputMode
{
    Json,  // JSONデバッグモード
    Chat   // LLMチャットモード
}
```

| UI 要素 | JSON モード | Chat モード |
|---------|-------------|-------------|
| `chatInputField` | JSON 入力（複数行、コードフォント） | メッセージ入力（複数行、UIフォント） |
| `messageText` | キャラクター発言 | LLM 応答表示 / "..."（Thinking中） |
| `modeLabel` | "JSON Mode" | "Chat Mode" |
| `llmRawResponseText` | リクエスト/レスポンス表示 | リクエスト/レスポンス表示 |
| `micButton` | マイクON/OFFボタン（アイコン切替） | マイクON/OFFボタン（アイコン切替） |

**入力フィールド高さ調整:**
- `jsonModeInputHeight`: JSONモード時の高さ（ピクセル、デフォルト500）
- `chatModeInputHeight`: Chatモード時の高さ（ピクセル、デフォルト100）
- モード切替時に `RectTransform.sizeDelta.y` を変更（アンカーはエディター設定維持）

**フォント設定:**
- `chatFont`: Chat用フォント（Mplus2-SemiBold SDF）
- `codeFont`: JSON用フォント（Mplus1Code-Regular SDF）
- `chatFontSize`: Chat用フォントサイズ（デフォルト18）
- `codeFontSize`: JSON用フォントサイズ（デフォルト14）

**送信方式:**
- **Enter**: 送信（`onValueChanged` で改行数増加を検出）
  - `MultiLineNewline`モードではEnterが`\n`挿入のみで`onEndEdit`が発火しないため、`onValueChanged`で前回テキストとの改行数差分を比較して送信トリガーとする
  - カーソル位置に依存しない検出方式（テキスト長+1 かつ 改行数増加 = Enter押下）
  - `_isSubmitting`フラグで二重送信を防止
- **Shift+Enter**: 改行挿入（キャレット位置に改行追加）
- **Chatモード**: Enter送信時、挿入された改行を除去して送信（送信後テキストクリア）
- **JSONモード**: Enter送信時、`_previousText`（Enter押下前のテキスト）をそのまま送信（JSON構造を維持、改行による変化なし）
- IME変換確定のEnterは送信しない（空テキスト判定でガード）
- **Sendボタンは廃止**（Enter送信のみ）
- ※ 設定パネルのInputFieldでは`MultiLineInputFieldFix`コンポーネントでEnter=改行を実現（送信機能なし）

**入力フィールドのフォーカス管理:**
- **送信後**: テキストクリア後に`ActivateInputField()`で即座に再フォーカス（クリック不要で連続入力可能）
- **起動時**: `Start()`で`ActivateInputField()`を呼び、起動直後からキーボード入力可能
- **設定パネル閉じ後**: `SettingsMenuController.OnPanelClosed`イベントを購読し、パネル閉じアニメーション完了時に自動フォーカス
- ユーザーがパネル外（3D画面等）をクリックするまでフォーカスは維持される

**JSON モードの処理フロー:**
- `CharacterController.ProcessResponse()` を経由して action/target/emote を処理
- CharacterController 未設定時は emote と emotion のみ直接適用

#### 設定パネルシステム（SettingsMenuController）

上部アイコンメニューバー + 展開式設定パネル構成。Coroutine駆動のアニメーション（EaseOut補間）。

**アイコンメニュー（5つ）:**

| アイコン | パネル | 説明 |
|---------|--------|------|
| 顔 | `AvatarSettingsPanel` | VRMモデル選択、カメラ高さ、カメラルックアット、プロンプト（キャラ設定/レスポンスフォーマット）、Save/Reload |
| 吹き出し | `LLMSettingsPanel` | API設定、Vision、IdleChat、Sleep、WebCam、Save/TestConnection |
| スピーカー | `VoiceSettingsPanel` | TTS ON/OFF、エンジン選択（VOICEVOX/Web Speech API）、VOICEVOX設定、Web Speech設定、STT設定 |
| 歯車 | (空) | その他設定（将来用） |
| [...] | `DebugSettingsPanel` | デバッグキー、JSONモード、LLM Raw Text、Timeline Debug、設定Import/Export、ライセンス表示 |

**アニメーション仕様:**
- **ホバー**: EventTrigger（PointerEnter/Exit）でツールチップをフェード＋スライド表示
  - `hoverFadeDuration`: 0.15秒
  - `tooltipSlideDistance`: 10ピクセル下方向
- **クリック**: パネルエリアをフェード展開/折り畳み
  - `panelExpandDuration`: 0.2秒
  - 排他制御（同時に1パネルのみ）
  - **パネル外クリックで閉じる**: Backdrop（全画面透明ボタン）をパネルと同時に表示
    - Backdropクリック → `ClosePanel()`
    - 別アイコンクリック → パネル切り替え
    - 同じアイコン再クリック → パネル閉じる（従来通り）
- **補間**: EaseOut（`t = 1 - (1-t)²`）
- **Time**: `Time.unscaledDeltaTime`（ポーズ非依存）

**データ構造:**
```csharp
[System.Serializable]
public class MenuEntry
{
    public Button iconButton;           // アイコンボタン
    public CanvasGroup tooltipGroup;    // ツールチップ（フェード用）
    public RectTransform tooltipRect;   // ツールチップ（スライド用）
    public GameObject panel;            // 対応パネル
}

// パネルエリア
public CanvasGroup panelAreaGroup;      // パネル表示エリア（フェード用）
public RectTransform panelAreaRect;     // パネル表示エリア（展開アニメ用）

// Backdrop（パネル外クリック検知）
public CanvasGroup backdropGroup;       // 全画面背景（フェード用）
public Button backdropButton;           // クリックでClosePanel()を呼ぶ
```

**Backdrop実装:**

Backdrop方式はUnity UIで一般的に使われるパターンで、全画面透明ボタンをパネルの下に配置してパネル外クリックを検知する。

**Hierarchy構造:**
```
UICanvas
├── UIContainer (トグルで一括非表示にする対象)
│   ├── IconBar (アイコンメニュー)
│   ├── Backdrop (全画面透明ボタン、初期非表示)
│   │   └── Image (Raycast Target=true, alpha=0) + Button
│   └── PanelArea (パネル表示エリア)
│       └── 各種設定パネル
├── UIHideToggleButton (常に表示、UIContainerと同階層)
└── StatusOverlay (対象外、独自制御)
```

**動作:**
1. `OpenPanel()` 時: Backdropを `SetActive(true)` + `CanvasGroup.alpha` を0→1にフェード
2. Backdropは**PanelAreaより下の階層**（Hierarchy上で上）に配置
3. PanelArea内のUI要素がクリックを受け取り、Backdropに届かない
4. パネル外（Backdrop部分）クリック → `backdropButton.onClick` → `ClosePanel()`
5. `ClosePanel()` 時: Backdropを `alpha` 1→0にフェード + `SetActive(false)`
6. 閉じアニメーション完了 → `OnPanelClosed` イベント発火 → UIController がチャット入力フィールドに自動フォーカス

**メリット:**
- シンプルで確実（イベント駆動）
- パフォーマンスが良い（Update()不要）
- フェードアニメーションが容易

#### AvatarSettingsPanel

VRMモデル、カメラ、プロンプトの設定。

**機能:**
- **Model Dropdown**: StreamingAssets/VRM/ からVRMファイル一覧を取得してドロップダウン表示
  - `Directory.GetFiles(vrmFolder, "*.vrm")` で動的スキャン
- **Animation Set**: アニメーションテンプレート選択（現在はChr001固定、placeholder）
- **Camera Height(m)**: `DynamicCameraController.SetCameraHeight()` でカメラY軸高さ調整
  - UIには `GetCameraHeight()` で現在値を表示
- **Camera Look At**: `DynamicCameraController.SetLookAtEnabled()` でY軸ルックアットON/OFF
  - ON時、カメラは水平方向のみキャラクターを追従（X/Z軸回転は維持）
- **Min FOV**: `DynamicCameraController.SetMinFov()` で最小FOV設定（近距離時・望遠）
  - 有効範囲: 1.0 ～ 179.0、UIには `GetMinFov()` で現在値を表示
- **Max FOV**: `DynamicCameraController.SetMaxFov()` で最大FOV設定（遠距離時・広角）
  - 有効範囲: 1.0 ～ 179.0、UIには `GetMaxFov()` で現在値を表示
- **Bored Rate**: `BoredomController.increaseRate` の設定（ポイント/分、0-10、0で自然増加OFF）
  - `onEndEdit` で即時反映、Clamp(0, 10)適用
- **Emotion Factors**: 感情ベース退屈度係数（happy/relaxed/angry/sad/surprised）
  - 各感情が退屈度に与える影響の強さと方向を設定（負=退屈減少、正=退屈増加）
  - surprisedは増減幅の増幅係数（絶対値として使用）
  - `onEndEdit` で即時反映
- **Character Prompt**: ChatManager.characterPrompt の編集（MultiLine InputField）
  - キャラクター設定（人格・性格・世界観）を記述
- **Response Format**: ChatManager.responseFormatPrompt の編集（MultiLine InputField）
  - JSON出力形式の指示を記述
  - ロックトグル（デフォルトON）で `interactable = false` にして誤編集を防止
- **Save**: VRMファイル名/プロンプト/退屈レート/感情係数/ロック状態をPlayerPrefsに保存、カメラ設定は `DynamicCameraController.SaveSettings()` に委譲
  - AvatarSettingsPanelが保存: `avatar_vrmFileName` (string), `avatar_characterPrompt` (string), `avatar_responseFormat` (string), `avatar_responseFormatLocked` (int), `avatar_boredRate` (float), `avatar_boredFactorHappy` (float), `avatar_boredFactorRelaxed` (float), `avatar_boredFactorAngry` (float), `avatar_boredFactorSad` (float), `avatar_boredFactorSurprised` (float)
  - DynamicCameraControllerが保存: `camera_height` (float), `camera_lookAtEnabled` (int), `camera_minFov` (float), `camera_maxFov` (float)
- **Reload**: `CharacterSetup.ReloadVrm(selectedFileName)` でモデル再読み込み
  - 内部で `VrmLoader.DisposeCurrentVrm()` + `LoadVrmAsync()` を呼び出し
  - OnVrmLoadedイベント経由で自動再セットアップ

**Unity Lifecycle:**
- **Awake()**: PlayerPrefsから設定復元（VRM/CharacterPrompt/ResponseFormat）
  - カメラ設定は DynamicCameraController.Start() で自動復元（AvatarSettingsPanelは関与しない）
- **OnEnable()**: パネル表示時に最新の状態を反映（VRMファイル一覧更新、現在値をUIに反映）
- **Start()**: イベントハンドラ登録

**責務の分離:**
- AvatarSettingsPanel: UIとコントローラーの仲介（値の表示・入力受付）
- DynamicCameraController: カメラ状態の管理・保存・復元

#### LLMSettingsPanel

LLM API設定、生成パラメータ、Vision、IdleChat、Sleep、WebCamの設定。

**機能:**
- **API Config**: AI Service / Endpoint / API Key / Model（UIControllerから移植）
  - `apiTypeDropdown`: Ollama / LM Studio / Dify / OpenAI / Claude / Gemini
  - API Keyフィールドは Dify/OpenAI/Claude/Gemini 選択時のみ表示
  - **エンドポイント自動切替**: APIタイプ変更時、エンドポイントが別タイプのデフォルト値なら新タイプのデフォルトに自動更新（手動編集済みの場合は維持）
  - **Dify注釈テキスト**: Dify選択時のみ表示（`difyAnnotationText`）。inputs変数の設定方法等をユーザーに案内
- **Generation Parameters**: 生成パラメータ設定（`generationParamsSection`）
  - **Dify選択時は非表示**（Difyはサーバー側で管理するため）
  - `temperatureInputField`: Temperature（0.0-2.0、デフォルト0.7）
  - `topPInputField`: Top P（0.0-1.0、デフォルト0.9）
  - `topKInputField`: Top K（0-100、デフォルト40）
  - `numPredictInputField`: 最大応答トークン数（-1=無制限、デフォルト512）
  - `numCtxInputField`: コンテキスト長（VRAM消費に直結、デフォルト4096）
  - `repeatPenaltyInputField`: 繰り返しペナルティ（1.0=無効、デフォルト1.1）
  - `thinkToggle`: Thinkingモード（推論モデル用、デフォルトOFF）
  - 全パラメータは`LLMConfig`に含まれ、`llm_config` PlayerPrefsキーでJSON一括保存
- **Max History**: `ChatManager.maxHistoryLength`（会話履歴保持数）
- **Use Vision**: `ChatManager.useVision` + カメラプレビュー表示
  - `cameraPreviewToggle`: ON時に`CharacterCameraController.GetRenderTexture()`を`RawImage`に設定
  - プレビュー初期化は`RetryCameraPreview()`コルーチンでVRM読み込み完了を待機（最大10回、0.5秒間隔）
- **Idle Chat**: 自律リクエスト機能
  - `idleChatToggle`: ON/OFF
  - `cooldownInputField`: クールダウン秒数
  - `idleChatMessageInputField`: 自律リクエストメッセージ（`IdleChatController.idlePromptMessage`）
- **Cron Scheduler**: 定期リクエスト機能（上級者向け）
  - `cronSchedulerToggle`: ON/OFF（ジョブ定義はStreamingAssets/cron/のJSONファイルで管理）
  - `cronReloadButton`: ジョブファイル手動リロード
  - `cronAutoReloadInputField`: 自動リロード間隔（分）。0=無効
- **Sleep**: 睡眠システム設定（IdleChatと同様のレイアウト）
  - `defaultSleepDurationInputField`: デフォルト睡眠時間（分）
  - `minSleepDurationInputField`: 最小睡眠時間（分）
  - `maxSleepDurationInputField`: 最大睡眠時間（分）
  - `dreamIntervalInputField`: 夢メッセージ間隔（秒）
  - `dreamPromptInputField`: 夢メッセージプロンプト
  - `wakeUpMessageInputField`: 起床時システムメッセージ
  - 各フィールドは `onEndEdit` で即時反映（`SleepController` のsetterメソッド経由でPlayerPrefs保存）
- **Web Cam**: `WebCamDisplayController` ON/OFF
- **Screen Capture**: `ScreenCaptureDisplayController` ON/OFF（PCブラウザ専用、画面共有ダイアログが表示される）
- **Save**: 全ての設定をPlayerPrefsに保存
  - `LLMClient.SaveAndApplyConfig()` でAPI設定保存
  - 追加で保存: `llm_useVision`, `llm_maxHistory`, `llm_cameraPreview`, `llm_webCam`, `llm_screenCapture`, `idleChat_message`
- **Test Connection**: `LLMClient.TestConnection()` で接続テスト

**Unity Lifecycle:**
- **Awake()**: `LoadSavedSettings()` で保存済み設定を復元
  - UseVision, MaxHistory, IdleChatMessage を ChatManager/IdleChatController に反映
  - WebCam / ScreenCapture / CameraPreview は各コントローラーで自己復元（下記参照）
  - Sleep設定は `SleepController.LoadSettings()` で自己復元
- **OnEnable()**: パネル表示時に現在の設定をUIに反映（`LoadSleepToUI()` 含む）+ カメラプレビュー初期化
- **Start()**: イベントハンドラ登録

**PlayerPrefs キー:**
| キー | 型 | 説明 |
|------|-----|------|
| `llm_useVision` | int | Vision機能ON/OFF（1=ON, 0=OFF） |
| `llm_maxHistory` | int | 会話履歴最大保持数 |
| `llm_cameraPreview` | int | カメラプレビュー表示ON/OFF（1=ON, 0=OFF） |
| `llm_webCam` | int | WebCam表示ON/OFF（1=ON, 0=OFF） |
| `llm_screenCapture` | int | Screen Capture表示ON/OFF（1=ON, 0=OFF） |
| `idleChat_message` | string | 自律リクエストメッセージ |
| `conversation_history` | string | 会話履歴JSON（ChatManager管理。SaveHistory/LoadHistoryで保存・復元） |

**設定の自己復元（各コントローラー）:**
- **WebCamDisplayController**: `Start()` で `llm_webCam` を読み込み、`autoPlay` に反映
- **ScreenCaptureDisplayController**: `Start()` で `llm_screenCapture` を読み込み、`autoPlay` に反映
- **CharacterCameraController**: `SetVrmInstance()` で `llm_cameraPreview` を読み込み、`alwaysRender` に反映
- **IdleChatController**: `LoadSettings()` で `idleChat_message` を読み込み、`idlePromptMessage` に反映
- **SleepController**: `LoadSettings()` で `sleep_defaultDuration`, `sleep_minDuration`, `sleep_maxDuration`, `sleep_dreamInterval`, `sleep_dreamMessage`, `sleep_wakeUpMessage` を読み込み、各フィールドに反映

この設計により、LLMSettingsPanel が非アクティブでも各コントローラーが起動時に設定を自動復元する。

**既知の制限（Camera Preview UI）:**
- LLMSettingsPanel は起動時に `SetActive(false)` のため、`OnEnable()` が呼ばれずカメラプレビューUIが初期化されない
- パネルを初めて開くまでプレビューRawImageは表示されない
- ただし、`CharacterCameraController.alwaysRender` は正常に復元されるため、実際のVision機能（LLMへの画像送信）は起動直後から動作する
- プレビューUIは確認用の補助機能のため、実用上の問題はない

#### VoiceSettingsPanel

音声設定パネル。TTS ON/OFF、エンジン選択（Web Speech API / VOICEVOX）、音声入力を統合管理。

**セクション0: TTS ON/OFF**
- TTS有効/無効トグル（デフォルトOFF）
- OFF時はエンジン選択・設定セクションが非表示/操作不可
- 設定変更は即時PlayerPrefsに保存

**セクション1: 音声合成エンジン設定**
- TTSエンジン選択ドロップダウン（Web Speech API / VOICEVOX）
- VOICEVOX: API URL入力、スピーカー選択、音声パラメータ（話速、音高、抑揚）
- Web Speech API: ボイス選択、Rate、Pitch
- テスト用テキスト入力、再生ボタン、接続テスト、設定保存

**セクション2: 音声入力（Speech to Text）**
- マイクON/OFFトグル（チャット欄横のマイクボタンと連動）
- エコー防止ON/OFFトグル（デフォルトON、ヘッドセット使用時はOFFでよい）
- 認識言語選択（日本語、英語、中国語、韓国語）
- 自動送信までの無音秒数（デフォルト2.0秒）
- ステータス表示（"待機中" / "🎤 録音中..."）

**動作:**
1. マイクON → `VoiceInputController.SetEnabled(true)`
2. ブラウザがマイク権限要求（初回のみ）
3. 音声認識開始 → リアルタイム文字起こし → Chat入力フィールド
4. N秒無音検出 → 自動送信 → 入力フィールドクリア
5. 設定はPlayerPrefsに保存（`voice_micEnabled`, `voice_inputLanguage`, `voice_silenceThreshold`）
6. 起動時に保存済みマイク設定を自動適用（`Start()`で`ApplySavedMicrophoneSetting()`）

**TTSクレジット通知:**
VoiceSettingsPanelは以下のタイミングで `VoiceSynthesisController.UpdateTTSCredit()` を呼び出し、
UIController上のTTSクレジット表示を更新する:
- TTS ON/OFF変更時
- TTSエンジン変更時
- VOICEVOXスピーカーリスト読み込み完了時
- Save時
- 起動時（`Start()`末尾）

**WebGL専用機能:**
- Unity エディタでは初期化失敗（正常動作）
- WebGL ビルドでのみ動作

#### DebugSettingsPanel

デバッグ機能の統合制御。

**機能:**
- **Debug Key**: `DebugKeyController.SetEnabled()` でデバッグキー一括ON/OFF
  - キーアサイン表示（W/A/D歩行、C/V Talk、F/G/H Interact）
- **Json Mode**: `UIController.SetInputMode()` で入力フィールドサイズ/フォント切替
  - JSONモード: 縦に広い（500px）、コードフォント、小さい文字
  - Chatモード: 小さい（100px）、UIフォント、通常文字
- **LLM Raw Text**: 画面左半分のデバッグテキストパネル表示/非表示
  - `rawTextPanel.SetActive()` で制御
- **Timeline Debug**: Timeline情報パネル表示/非表示
  - `CharacterAnimationController.SetTimelineDebugEnabled()` でON/OFF
  - `timelineDebugPanel.SetActive()` で制御
  - 表示内容: Timeline名、Frame、Time、State、PlayState、Loop/Cancel情報、Pos/Rot、PreservePos
- **Status Overlay**: ステータスオーバーレイ表示/非表示
  - `StatusOverlay.SetVisible()` で制御
  - 表示内容: FPS（色分け）、キャラクターステート、アニメーションステート・Timeline名、JSヒープメモリ
  - FPS色分け: 緑(55+) / 黄(30+) / 赤(30未満)
  - メモリ: WebGL=JSヒープ（Chrome限定、`performance.memory`）、Editor=Unityマネージドメモリ
- **Settings Import/Export**: 全設定のJSON形式エクスポート・インポート
  - `exportSettingsButton`: 全PlayerPrefs設定をJSONファイルとしてダウンロード
  - `importSettingsButton`: JSONファイルを選択してPlayerPrefsにインポート
  - `importExportStatusText`: 操作結果のステータス表示
  - `SettingsExporter` コンポーネント経由で実行
- **License**: ライセンス情報の表示
  - `licenseButton`: トグル表示（クリックで開閉）
  - `licensePanel`: スクロール可能なライセンス表示パネル
  - `licenseText`: TMP_Text表示先
  - `licenseCloseButton`: パネル閉じるボタン
  - テキストは `Resources/LicenseText.txt` (TextAsset) から読み込み

**OnEnable()**: パネル表示時に現在の状態を反映

#### SettingsExporter（設定Import/Export）

全PlayerPrefs設定をJSON形式でエクスポート・インポートするユーティリティ。
`DebugSettingsPanel` のボタンから操作する。

**エクスポート:**
- `AllSettings` 配列に定義された全キー（40項目）をPlayerPrefsから読み取り
- 整形済みJSON（インデント付き）を生成
- WebGL: `FileIO.jslib` の `FileIO_Download()` でブラウザダウンロード
- Editor: `GUIUtility.systemCopyBuffer` でクリップボードにコピー
- ファイル名: `cyan_nook_settings_yyyyMMdd_HHmmss.json`

**インポート:**
- WebGL: `FileIO.jslib` の `FileIO_OpenFileDialog()` でブラウザファイル選択ダイアログ → FileReaderで読み込み → `SendMessage` でコールバック
- Editor: クリップボードから読み込み
- ホワイトリスト方式（`AllSettings` に定義されたキーのみインポート対象）
- インポート後はページリロードで全設定が反映される

**対象設定カテゴリ:**

| カテゴリ | キー数 | 主なキー |
|---------|--------|---------|
| Avatar | 5 | `avatar_vrmFileName`, `avatar_characterPrompt`, `avatar_responseFormat`, `avatar_boredRate` 等 |
| Camera | 4 | `camera_height`, `camera_lookAtEnabled`, `camera_minFov`, `camera_maxFov` |
| LLM | 6 | `llm_config`, `llm_useVision`, `llm_maxHistory`, `llm_cameraPreview`, `llm_webCam`, `llm_screenCapture` |
| IdleChat | 3 | `idleChatEnabled`, `idleChatCooldown`, `idleChat_message` |
| Cron Scheduler | 2 | `cronSchedulerEnabled`, `cronAutoReloadInterval` |
| Sleep | 6 | `sleep_defaultDuration`, `sleep_minDuration`, `sleep_maxDuration`, `sleep_dreamInterval`, `sleep_dreamMessage`, `sleep_wakeUpMessage` |
| Voice TTS | 2 | `voice_ttsEnabled`, `voice_ttsEngine` |
| Voice WebSpeech | 3 | `voice_webSpeechVoiceURI`, `voice_webSpeechRate`, `voice_webSpeechPitch` |
| Voice VOICEVOX | 5 | `voice_apiUrl`, `voice_speakerId`, `voice_speedScale`, `voice_pitchScale`, `voice_intonationScale` |
| Voice Input | 3 | `voice_micEnabled`, `voice_inputLanguage`, `voice_silenceThreshold` |

※ ランタイム状態（`sleep_state`, `sleep_wake_time`, `sleep_furniture_id`）と会話履歴（`conversation_history`）はエクスポート対象外

**FileIO.jslib** (`Plugins/WebGL/`):

| 関数 | 説明 |
|------|------|
| `FileIO_Download(filename, content)` | Blob + `<a>` click でJSONファイルダウンロード |
| `FileIO_OpenFileDialog(callbackObj, callbackMethod, accept)` | `<input type="file">` でファイル選択 → FileReader → SendMessage コールバック |

**関連ファイル:**

| ファイル | 役割 |
|---------|------|
| `Scripts/Core/SettingsExporter.cs` | エクスポート・インポートロジック、全設定キー定義 |
| `Plugins/WebGL/FileIO.jslib` | WebGL用ブラウザファイルI/O |
| `Scripts/UI/DebugSettingsPanel.cs` | UI統合（Export/Importボタン、ステータス表示） |

#### Timeline Debug表示（CharacterAnimationController）

従来のOnGUI()表示をUnityUIに変更。

**フィールド:**
- `showTimelineDebug`: デバッグ表示ON/OFF（SerializeField、デフォルトfalse）
- `timelineDebugText`: Timeline情報を表示するTMP_Text

**API:**
```csharp
public void SetTimelineDebugEnabled(bool enabled)
public bool IsTimelineDebugEnabled { get; }
private void UpdateTimelineDebugText()  // LateUpdate()で毎フレーム更新
```

**表示内容:**
```
Timeline: chr001_anim_talk_talk01_lp
Frame: 120 / 240
Time: 2.000 / 4.000
State: Talk (talk01)
PlayState: Playing
Loop: InLoop exit=False
Cancel: --- regions=0
Pos: (0.000, 0.000, 0.000)
Rot: (0.0, 180.0, 0.0)
PreservePos: True
```

#### UIController 初期設定

**Inspector フィールド:**
- `initialMode`: 起動時のモード（デフォルト: Chat）
- `messageDisplayDuration`: テキスト表示完了後、メッセージが消えるまでの時間（秒、0で無限表示）
- `jsonModeInputHeight`: JSONモード時の入力フィールド高さ（ピクセル、デフォルト500）
- `chatModeInputHeight`: Chatモード時の入力フィールド高さ（ピクセル、デフォルト100）
- `chatFont` / `codeFont`: Chat/JSON用フォント
- `chatFontSize` / `codeFontSize`: Chat/JSON用フォントサイズ
- `errorMessageColor`: エラーメッセージの表示色（デフォルト: 薄い赤 `(1.0, 0.4, 0.4, 1.0)`）。TMPリッチテキスト `<color>` タグで適用

**UI非表示トグル:**
- `uiContainer`: UI全体を格納するコンテナGameObject（トグルで非表示にする対象）
- `uiHideToggleButton`: UI非表示トグルボタン（Button）— Canvas直下、UIContainerと同階層に配置（常に表示）
- `uiShowIcon` / `uiHideIcon`: 表示/非表示アイコン（Image）— マイクボタンと同じ排他的表示切替パターン
- `uiContainer.SetActive(false)` で一括非表示。非表示中もロジック系（ChatManager, CharacterController等）はCanvas外で通常動作
- StatusOverlayは対象外（独自のSetVisible制御）

**マイクボタン（チャット入力欄横）:**
- `micButton`: マイクON/OFFボタン（Button）— チャット入力欄の右に配置
- `micOnIcon` / `micOffIcon`: マイクON/OFFアイコン（Image）— 同じ位置に重ね、状態に応じて表示切替
- `voiceInputController`: VoiceInputController参照
- ボタン押下で `VoiceInputController.SetEnabled(!IsEnabled)` をトグル
- `VoiceInputController.OnEnabledChanged` イベントを購読し、外部からの状態変更（設定パネルのトグル等）もアイコンに反映
- 設定パネル内のマイクトグルと常に同期（どちらから操作しても連動）

**呼び戻しボタン（Come Home）:**
- `recallButton`: 外出中のみ表示されるボタン（Button）
- `ShowOutingDisplay()` で表示、`ClearOutingDisplay()` で非表示
- クリック時: `LLMResponseData.GetFallback()` に `action="interact_entry"` を設定し、`CharacterController.ProcessResponse()` に渡してentryフローを実行
- ボタン押下後は即時非表示（entryアニメーション開始）

**TTSクレジット表示（メッセージ表示エリア付近）:**
- `ttsCreditText`: TTSクレジット表示テキスト（TMP_Text）
- `voiceSynthesisController`: VoiceSynthesisController参照（イベント購読用）
- `VoiceSynthesisController.OnTTSCreditChanged` イベントを購読し、TTS設定変更時に自動更新
- 表示例: `"VOICEVOX:ずんだもん(ノーマル)"` / `"Web Speech API"` / `"OFF"`
- VOICEVOXクレジット表記は規約準拠（例: [ずんだもん利用規約](https://zunko.jp/con_ongen_kiyaku.html)）

#### ストリーミング表示（UIController）

ストリーミングモード時、`UIController` は以下のイベントを購読して逐次表示を行う:
- `OnStreamingReactionReceived`: reaction テキストを `messageText` に即座に表示
- `OnStreamingTextReceived`: message テキストチャンクを `messageText` に逐次追加表示（reaction があれば改行付きで連結）
- `OnStreamingHeaderReceived`: ヘッダー JSON を `llmRawResponseText` に表示
- `OnParseError`: JSONパースエラー時、`errorMessageColor` で色付きエラーメッセージ + 生テキストを `messageText` に表示

#### メッセージ表示の状態別抑制（UIController）

Sleep中・Outing中は `UIController.IsOutingActive` により `messageText` への表示更新を抑制する。
デバッグ用の `llmRawResponseText`（Raw Text表示）は抑制対象外で、状態に関わらず更新される。

| 抑制対象メソッド | 通常時 | Outing中 |
|------------------|--------|----------|
| `OnThinkingStarted` | "..."を表示 | スキップ（「お出かけ中…」維持） |
| `OnStreamingReaction` | reactionを表示 | スキップ（状態変数は蓄積） |
| `OnStreamingText` | テキスト逐次表示 | スキップ（_streamingMessageBuilder蓄積は継続） |
| `OnChatMessageReceived` | 最終メッセージ表示 | スキップ |

Sleep中のメッセージ表示抑制は `CharacterController` 側で処理される（「Zzz...」置換表示）。

**TTS抑制（ChatManager側）:**
上記UIController側のメッセージ表示抑制に加え、ChatManager側でSleep/Outing中のTTS（音声合成）を抑制する。
`HandleStreamText`、`HandleStreamField`、`HandleRequestCompleted`、`HandleLLMResponse` の各ハンドラで、
Sleep中（`_isWakeUpRequest`を除く）・Outing中（`_isCronEntryRequest`を除く）はTTS関連の処理をスキップする。

#### リクエストボディ表示（UIController）

`UIController` は `LLMClient.OnRequestBodySent` イベントを購読し、
LLMへの送信内容を `llmRawResponseText` にレスポンスと合わせて表示する。

```
--- REQUEST ---
{"model":"gemma3","system":"あなたは...","prompt":"ユーザー: こんにちは\n","stream":true,...}

--- RESPONSE ---
{"emotion":{"happy":0.8,...},"reaction":"いいね!","action":"ignore",...,"message":"こんにちは！元気ですか？"}
```

- リクエスト送信時: `--- RESPONSE ---` に `(waiting...)` と表示
- レスポンス到着後: 実際の内容に置換
- 画像データを含む場合: リクエストボディは2000文字で切り詰め
- `llmRawResponseText` は DebugSettingsPanel の「LLM Raw Text」トグルで表示/非表示切替

### データフロー

#### ブロッキング方式（useStreaming = false）

```
[User Input]
    │
    ▼
┌────────────────────┐
│ UIController  │
│ (inputField)       │
│ Enter=送信          │
│ Shift+Enter=改行   │
└────────┬───────────┘
         │ SendMessage()
         ▼
┌──────────────────────┐
│   ChatManager        │
│ (プロンプト生成)      │
│                      │  useVision=true?
│ CharacterCamera ─────┼──→ CaptureImageAsBase64() → imageBase64
│ Controller           │
└────────┬─────────────┘
         │                    ┌──────────────────┐
         │───────────────────→│    LLMClient     │
         │                    │ SendRequest(      │
         │                    │   ..., imageBase64)│
         │                    └────────┬─────────┘
         │                             │ CreateProvider()
         │                             ▼
         │                    ┌──────────────────┐
         │                    │  ILLMProvider     │
         │                    │  .SendRequest()   │
         │                    └────────┬─────────┘
         │                             │ HTTP POST（全文受信 + 画像）
         │                             ▼
         │                    ┌──────────────────┐
         │                    │  LLM Server      │
         │                    │ (Ollama/Dify等)  │
         │                    └────────┬─────────┘
         │                             │ JSON Response
         │◄────────────────────────────┘
         │ OnResponseReceived(LLMResponseData)
         ▼
┌──────────────────┐
│  TalkController  │
└────────┬─────────┘
         │
    ┌────┴────┬──────────┬──────────┐
    ▼         ▼          ▼          ▼
Animation  Expression  LookAt    Message
Controller Controller Controller  Display
```

#### ストリーミング方式（useStreaming = true）

```
[User Input]
    │
    ▼
┌────────────────────┐
│ UIController  │
└────────┬───────────┘
         │ SendMessage()
         ▼
┌──────────────────────┐
│   ChatManager        │  useVision=true?
│ useStreaming=true     │──→ CaptureImageAsBase64() → imageBase64
└────────┬─────────────┘
         │                    ┌───────────────────────────────┐
         │───────────────────→│    LLMClient                  │
         │                    │ SendStreamingRequest(          │
         │                    │   ..., imageBase64)            │
         │                    └────────┬──────────────────────┘
         │                             │
         │                             ▼
         │                    ┌─────────────────────────┐
         │                    │  ILLMProvider            │
         │                    │  .SendStreamingRequest() │
         │                    └────────┬────────────────┘
         │                             │ HTTP POST (stream + 画像)
         │                          ▼
         │                 ┌─────────────────────────┐
         │                 │ DownloadHandlerScript    │
         │                 │ (NDJSON / SSE パース)     │
         │                 └────────┬────────────────┘
         │                          │
         │                          ▼
         │                 ┌─────────────────────────┐
         │                 │ StreamSeparatorProcessor │
         │                 │ + IncrementalJsonField   │
         │                 │   Parser                 │
         │                 └──┬──────┬───────────────┘
         │                    │      │
         │ ①OnStreamField     │      │ (フィールド単位で複数回)
         │◄───────────────────┘      │
         │  → 表情・LookAt・移動を     │
         │    フィールド単位で逐次反映   │
         │  → reaction → UI即座表示    │
         │  → Thinking解除判定         │
         │                            │
         │ ②OnStreamText (複数回)     │ (messageフィールドストリーミング)
         │◄──── テキストチャンク逐次到着 │
         │                            │
         │ ③OnStreamHeader            │ (JSON全体完了時)
         │◄───────────────────────────┘
         │  → 状態更新のみ（反映済み）
         │
         │ ④OnStreamCompleted → OnResponseReceived
         ▼
   最終LLMResponseData構築
   会話履歴に追加（逐次反映済みの場合は口パクのみ）
```

### Chat 状態

```csharp
public enum ChatState
{
    Idle,               // 待機中
    WaitingForResponse, // LLM 応答待ち（Thinking アニメーション再生）
    Error               // エラー状態
}
```

### 処理シーケンス（ブロッキング方式）

```
1. ユーザー入力 → Enter キー
   ├─ UIController: inputField.text を ChatManager.SendChatMessage() へ
   ├─ inputField をクリア + 再フォーカス（連続入力対応）
   └─ IdleChatController.OnUserMessageSent() → タイマーリセット
   ※ Talk状態への遷移はLLM応答の action:"move"+target:"talk" で制御

2. ChatManager → LLMClient: リクエスト送信
   ├─ LLMClient.OnRequestStarted イベント発火
   ├─ ChatManager.HandleRequestStarted():
   │   ├─ ChatState: Idle → WaitingForResponse
   │   ├─ TalkController.StartThinking() → TL_talk_thinking01 再生
   │   └─ OnThinkingStarted イベント発火
   └─ UIController: messageText に "..." 表示

3. LLM 応答受信
   ├─ LLMClient.OnResponseReceived イベント発火
   │   └─ ChatManager.HandleLLMResponse():
   │       ├─ CharacterController.HandleChatResponse():
   │       │   ├─ emotion → ExpressionController: 表情適用
   │       │   ├─ action → ProcessAction: 移動/インタラクト/無視
   │       │   ├─ target → ProcessLookAt: 視線コンテキスト設定（以降Update()で動的再評価）
   │       │   ├─ emote → ProcessEmote: 感情モーション再生（Walking中はキューイング）
   │       │   └─ HasMessage → LipSyncController: テキスト口パク開始
   │       ├─ ChatState: WaitingForResponse → Idle
   │       ├─ HasMessage=true の場合のみ会話履歴に追加
   │       └─ OnMessageReceived イベント発火
   ├─ LLMClient.OnRequestCompleted イベント発火
   │   └─ ChatManager.HandleRequestCompleted():
   │       ├─ TalkController.StopThinking()
   │       └─ OnThinkingEnded イベント発火
   └─ UIController: messageText に応答メッセージ表示
```

### 処理シーケンス（ストリーミング方式）

```
1. ユーザー入力（ブロッキングと同じ）

2. ChatManager → LLMClient: ストリーミングリクエスト送信
   ├─ ChatManager.useStreaming=true → llmClient.SendStreamingRequest()
   ├─ LLMClient.OnRequestStarted イベント発火
   │   ├─ ChatState: Idle → WaitingForResponse
   │   ├─ TalkController.StartThinking() → TL_talk_thinking01 再生
   │   └─ UIController: messageText に "..." 表示
   └─ Provider: stream=true / response_mode="streaming" でHTTPリクエスト送信

3. フィールド逐次受信（各フィールド完了時に発火）
   ├─ IncrementalJsonFieldParser がフィールド単位でパース
   ├─ LLMClient.OnStreamFieldReceived イベント発火
   └─ ChatManager.HandleStreamField():
       ├─ Thinking解除判定（HandleThinkingExitOnField）:
       │   ├─ emotion/target/reaction → StopThinking()（graceful: ed再生）
       │   ├─ emote → ForceStopThinking()（即座キャンセル）
       │   └─ action → Thinking継続（target/emote待ち）
       ├─ emotion → ExpressionController: 表情を即座に適用
       ├─ reaction → OnStreamingReactionReceived: UI即座表示 + 音声合成
       └─ target/action/emote → OnStreamFieldApplied イベント発火
           └─ CharacterController.HandleStreamField():
               ├─ target → LookAt設定 + _pendingTarget保存
               ├─ action → _pendingAction保存
               ├─ target/action両方揃い → TryExecutePendingAction() → 移動実行
               └─ emote → ProcessEmote: エモート再生（Walking中はキューイング）

4. messageフィールドストリーミング（message値のパース中、複数回発火）
   ├─ IncrementalJsonFieldParser.OnStringValueChunk → StreamSeparatorProcessor.OnTextReceived
   ├─ LLMClient.OnStreamTextReceived イベント発火
   └─ UIController:
       ├─ 最初のチャンク: reaction + "\n" をプリペンド
       ├─ messageText にテキストを逐次追加表示
       └─ llmRawResponseText にヘッダー+テキストを逐次更新

5. ヘッダー全体受信（JSON閉じ括弧検出時）
   ├─ StreamSeparatorProcessor がパース済みフィールドからLlmResponseHeaderを構築
   ├─ LLMClient.OnStreamHeaderReceived イベント発火
   └─ ChatManager.HandleStreamHeader():
       └─ 状態更新のみ（表情・Thinking解除は逐次反映で処理済み）

6. ストリーミング完了（正常時）
   ├─ LLMClient: LlmResponseHeader + 蓄積テキスト → LLMResponseData を構築
   ├─ LLMClient.OnStreamCompleted + OnResponseReceived イベント発火（既存フローと互換）
   │   └─ ChatManager.HandleLLMResponse():
   │       ├─ CharacterController.HandleChatResponse():
   │       │   ├─ _hasIncrementalFields=true → 口パクのみ（逐次反映済み）
   │       │   └─ _hasIncrementalFields=false → 従来の全フィールド一括処理
   │       ├─ 会話履歴追加、ChatState → Idle
   │       └─ OnMessageReceived イベント発火
   └─ LLMClient.OnRequestCompleted イベント発火
       └─ ChatManager.HandleRequestCompleted():
           ├─ Thinkingフォールバック解除（まだThinking中なら強制解除）
           └─ OnThinkingEnded イベント発火

6b. ストリーミング完了（JSONパースエラー時）
   ├─ StreamSeparatorProcessor.Complete(): フォールバックパース失敗
   │   └─ OnParseError("Stream completed with incomplete JSON", rawText)
   │       └─ LLMClient.OnStreamParseError → ChatManager.HandleStreamParseError():
   │           ├─ _parseErrorHandled = true
   │           ├─ Thinking解除、ChatState → Idle
   │           ├─ OnParseError → UIController: 色付きエラー + 生テキスト表示
   │           ├─ VoiceSynthesis: rawText を TTS（エラーメッセージは除外）
   │           └─ OnChatResponseReceived(fallback) → CharacterController
   ├─ Provider.onComplete → LLMClient.OnResponseReceived イベント発火
   │   └─ ChatManager.HandleLLMResponse(): _parseErrorHandled=true → 早期リターン
   └─ LLMClient.OnRequestCompleted → HandleRequestCompleted()（通常通り）
```

### WebLLM（ブラウザ内LLM via WebGPU）

@mlc-ai/web-llm ライブラリを使用し、ブラウザ内でLLMを実行する。サーバー不要。WebGPU対応ブラウザが必要。

#### ターゲットモデル

`Qwen3-1.7B-q4f16_1-MLC`（約1.1GB）。初回起動時にCDNからダウンロードされ、ブラウザのCache Storage APIにキャッシュされる。

#### アーキテクチャ（jslib ブリッジパターン）

WebSpeechAPI.jslib / ScreenCapture.jslib と同じパターンで実装。

```
C# (WebLLMProvider)
  → WebLLMBridge (DllImport)
    → WebLLM.jslib (JavaScript)
      → 動的ES Moduleインポート（CDN: esm.run/@mlc-ai/web-llm）
        → window.webllm → WebGPU

JS → C#: SendMessage(gameObjectName, methodName, stringParam) コールバック
```

WebLLMライブラリはjslib内から動的に `<script type="module">` を生成してCDNから読み込む。
HTMLテンプレートに依存しないため、Unityroomなど独自HTMLを使うホスティング環境でも動作する。
読み込みタイムアウト（30秒）付きで、失敗時はC#側にエラーを通知する。

#### コンポーネント

| ファイル | 役割 |
|---------|------|
| `WebGLTemplates/CyanNook/index.html` | カスタムWebGLテンプレート（WebLLMのCDN読み込みはjslibに移行済み） |
| `Plugins/WebGL/WebLLM.jslib` | JavaScript側ブリッジ。web-llm CDNからの動的ES Moduleインポート、API呼び出し、XGrammarによるJSON制約生成。HTMLテンプレート非依存 |
| `Scripts/Chat/WebLLMBridge.cs` | MonoBehaviour Singleton。`[DllImport("__Internal")]`宣言（`#if UNITY_WEBGL && !UNITY_EDITOR`）+ SendMessageコールバック受信。C#イベントで外部に公開 |
| `Scripts/Chat/WebLLMProvider.cs` | ILLMProvider実装。WebLLMBridge経由でリクエスト送信、StreamSeparatorProcessorでJSON逐次パース |

#### 制約・特記事項

- システムプロンプトに `/no_think` を付与（Qwen3のThinkingモード無効化）
- ストリーミング/非ストリーミング両方で `<think>` タグをフィルタ除去
- 生成中に新リクエストが来た場合、`interruptGenerate()` で前回の生成を中断
- WebLLMライブラリはjslib内で動的にCDNから読み込み（HTMLテンプレート非依存、Unityroom等のサードパーティホスティングでも動作）
- `LLMConfigManager.IsValid()` はWebLLM選択時にエンドポイント未設定でもtrueを返す
- `LLMSettingsPanel` はWebLLM選択時にendpoint/apiKey/modelName/generationParamsを非表示にする

### 初回起動フロー（First-Run Flow）

LLM未設定状態を検出し、初回起動ガイドを表示するシステム。

#### 検出条件

`LLMConfigManager.HasSavedConfig()` が `PlayerPrefs` に `llm_config` キーが存在するか確認。存在しない場合を初回起動と判定。

#### フロー

```
[起動時]
RoomLightController.Awake()
└── startOff == true → 即消灯（ライト intensity=0 + Emission OFF）
    ※最初のフレームから暗い状態を保証

CharacterSetup.OnVrmLoaded()
├── HasSavedConfig() == true（設定済み）
│   └── PlayEntryOrIdle() → キャラクターEntry（ライト制御はLightControlTrackで実行）
└── HasSavedConfig() == false（初回起動）
    ├── キャラクターEntry抑制
    └── FirstRunController: ポップアップ表示
        ├── [Yes: ブラウザAIを使う]
        │   ├── WebLLMモデルダウンロード開始（進捗バー表示）
        │   ├── ダウンロード完了 → LLMConfig保存
        │   ├── FirstRunController.Complete()
        │   └── PlayEntryOrIdle() → キャラクターEntry再生
        └── [No: 手動設定]
            ├── ポップアップ閉じる（消灯維持）
            ├── ユーザーがLLM設定パネルで設定
            └── Save実行時:
                ├── LLMSettingsPanel.OnLLMConfigured イベント発火
                │   └── CharacterSetup.OnLLMConfiguredFromSettings()
                │       ├── FirstRunController.Complete()
                │       └── PlayEntryOrIdle() → キャラクターEntry再生
                └── WebLLM選択時: ShowDownloadOnlyモードでダウンロードUI表示
```

#### コンポーネント

| クラス | 役割 |
|--------|------|
| `RoomLightController` | ライトON/OFF + マテリアルEmission + ベイク済みLightmap連動。`startOff`で起動時消灯。LightControlTrack経由で制御 |
| `FirstRunController` | ポップアップUI表示、Yes/Noボタン処理、WebLLMダウンロード進捗表示 |
| `CharacterSetup` | `firstRunController`/`llmSettingsPanel`参照保持、初回判定によるEntry抑制、`PlayEntryOrIdle()`でEntry開始 |
| `LLMSettingsPanel` | `OnLLMConfigured`イベント、WebLLM選択時のUI制御、`firstRunController`参照によるダウンロードフロー連携 |

---

## Animation System (Timeline-driven)

アニメーションはTimeline主導で制御。Animator Controllerは使用せず、PlayableDirectorでTimelineを直接再生。

### Architecture

```
CharacterAnimationController
    │
    ├─ ステート管理（カスタム）
    │   └─ Idle / Walk / Run / Talk / Emote / Interact / Thinking / Custom
    │
    ├─ PlayableDirector制御
    │   └─ Timeline再生・停止・ループ
    │
    └─ バインディング管理
        └─ TimelineBindingData (ScriptableObject)
```

#### PlayState() の Timeline 解決順序

`PlayState(state, animationVariant)` 呼び出し時、Timeline は以下の優先順で検索される：

1. **animationIdBindings**（animationVariant 指定時のみ） — ID→Timeline 直接マッピング
2. **stateBindings**（フォールバック） — AnimationStateType→Timeline マッピング（最初の一致を返す）

例: `PlayState(Talk, "talk_thinking01")`
→ animationIdBindings で `talk_thinking01` を検索 → `TL_talk_thinking01` を取得

例: `PlayState(Talk)` (variant なし)
→ stateBindings で Talk を検索 → `TL_talk_idle01` を取得

### Animation State Types

| State | Description | Loop方式 |
|-------|-------------|------|
| Idle | 待機 | WrapMode.Loop（Timeline全体） |
| Walk | 歩行 | LoopRegionClip（st/lp/edパターン） |
| Run | 走行 | LoopRegionClip（st/lp/edパターン） |
| Talk | 会話 | LoopRegionClip（st/lp/edパターン） |
| Emote | 感情表現 | LoopRegionClip（テキスト表示完了後 emoteHoldDuration 経過で自動ed遷移） |
| Interact | 家具インタラクション | LoopRegionClip（st/lp/edパターン） |
| Thinking | 考え中（API待ち） | LoopRegionClip（st/lp/edパターン） |

### Animation Phase

| Phase | Suffix | Description |
|-------|--------|-------------|
| Start | _st | 動作開始 |
| Loop | _lp | ループ部分 |
| End | _ed | 動作終了 |

Face (VRM Expression) は Facial Timeline で制御（FacialTimelineData 設定時）。未設定時は直接制御にフォールバック。

### Key Classes

| Class | Responsibility |
|-------|----------------|
| `CharacterAnimationController` | Timeline再生制御、ステート管理、ループ制御、キャンセル判定、慣性補間起動、Walk終了フェーズ制御（StopWalkWithEndPhase） |
| `TimelineBindingData` | ステート↔Timeline↔Clipのマッピング (ScriptableObject)。animationIdBindings による ID→Timeline 直接解決にも対応 |
| `VrmLoader` | VRM読み込み、Animator/PlayableDirectorセットアップ、メッシュ表示制御（SetMeshVisibility） |
| `CharacterExpressionController` | VRM Expression (表情) 制御。Facial Timeline駆動（FacialTimelineData設定時）/ 直接制御（フォールバック）。感情ブレンド（円環モデル準拠）。Body VrmExpressionTrack優先制御。`GetVrmInstance()` でVRMインスタンスを公開。`ResetExpressionsForTimelineFrame()` / `AddTimelineExpression()` でTimeline Expression加算制御 |
| `FacialTimelineData` | EmotionType → Facial TimelineAsset マッピング + ブレンドペアバインディング（ScriptableObject） |
| `CharacterLookAtController` | Timeline駆動の視線制御（Eye/Head/Chest、Pre-Update Restoreパターン、ExecutionOrder 20100）。LookAtTransform()によるTransform追跡対応。疑似Microsaccade（Eye限定の微小揺れ）対応。ボーン回転スムージング（角度制限フリップ防止） |
| `CharacterNavigationController` | NavMeshAgent位置制御、Root Motionフィルタリング、速度調整、パス有効性チェック、スタック検出 |
| `InteractionController` | インタラクション状態管理（ループ/キャンセルはCharacterAnimationControllerに委譲） |
| `TalkController` | Talkモード状態管理、talk_position移動、LookAt制御連携、ForceExitTalk（即時終了）、再接近判定 |
| `LLMClient` | LLM通信統合管理（ILLMProviderによるOllama/Dify切替） |
| `ChatManager` | プロンプト生成、会話履歴管理、Chat状態管理 |
| `SpatialContextProvider` | 空間認識JSON生成（方向・距離付き家具/ターゲット/部屋境界情報） |
| `VisibleObjectsProvider` | キャラクターカメラFrustum内のSceneObjectDescriptorを収集し、LLMプロンプト用テキスト生成 |
| `SceneObjectDescriptor` | シーン内オブジェクトに説明文を付与するコンポーネント（objectName + description、Renderer必須） |
| `LLMConfigManager` | API設定の保存・読み込み（PlayerPrefs） |
| `InertialBlendHelper` | 慣性補間（マルチボーン対応、Pre-Update Restoreパターン、ExecutionOrder 20000） |
| `LoopRegionTrack/Clip` | ループ領域定義（クリップの位置・長さからLoopStart/LoopEnd/EndStartを自動導出） |
| `ActionCancelTrack/Clip` | キャンセル可能領域定義 + 遷移先Timeline管理 |
| `LookAtTrack/Clip` | Timeline駆動の視線制御（Eye/Head/Chest、角度制限、補間フレーム設定） |
| `RootMotionForwarder` | Root Motion転送（VRMのAnimator→NavigationController） |
| `WebCamDisplayController` | ユーザーWebカメラ映像をシーン内Quadに表示 |
| `ScreenCaptureDisplayController` | ユーザーのデスクトップ/ウィンドウ画面をシーン内Quadに表示（WebGL専用、Screen Capture API使用） |
| `IdleChatController` | 自律リクエスト制御（タイマー管理、自動LLMリクエスト送信） |
| `BoredomController` | 退屈ポイント管理（時間経過で自然増加、LLMレスポンスの感情データで増減、プロンプト連携） |
| `SleepController` | 睡眠状態管理。夢メッセージ定期送信、起床タイマー（sleep_duration）、PlayerPrefs永続化、アプリ起動時復元。Sleep中はIdleChat停止・Boredom停止・応答抑制（Zzz...表示） |
| `LipSyncController` | リップシンク統合制御。TextOnly（テキスト口パク）/ Mora（VOICEVOX母音同期）/ Simulated（Web Speech API推定）/ Amplitude（AudioSource振幅）の4モード。TTS有効時はTextOnlyモード自動抑制 |
| `CharacterController` | LLMレスポンスのaction/target/emoteルーティング、サブコントローラ統合管理、Talk状態と他actionの連携管理、RoomTarget移動/LookAt、口パク開始。`ProcessResponse()`で外部（DebugUI等）からの直接呼び出しに対応 |
| `DynamicTargetController` | clock/distance/heightに基づく動的ターゲット位置計算（NavMeshAgent、段階的NavMesh検索、回避システム除外）。LookAtTarget子オブジェクトでheight管理 |
| `RoomTargetManager` | シーン内の名前付きターゲット管理（mirror, window等）。子GOスキャン→Dictionary構築、lookattarget解決、プロンプト用リスト生成 |
| `EmotePlayableTrack/Clip` | emote再生可能期間を定義するメタデータTimeline Track |
| `ThinkingPlayableTrack/Clip` | Thinking再生可能期間を定義するメタデータTimeline Track |
| `VrmExpressionTrack/Clip` | Timeline駆動のVRM Expression制御（カーブ評価、sourceClipからのBake、ExpressionController上書き優先）。`blendEmotionTag`でトラックの感情タイプを指定し、ブレンドTimeline時はMixerがExpressionControllerからウェイトを取得して動的制御。Neutralプリセットで全Expression=0リセット、同一Timeline上の複数トラックは加算動作 |
| `AdditiveOverrideHelper` | 加算ボーンオーバーライド（Bone Snapshot + LateUpdate Restore、ExecutionOrder 20050）。StopOverride前にIBのSnapshotCurrentPoseAsCleanが必須（問題9参照） |
| `MoveSpeedTrack/Clip` | NavMeshAgent移動速度のカーブ制御。speedCurve（速度乗算値）+ adjustAnimatorSpeed（アニメ速度追従ON/OFF） |
| `LightControlTrack/Clip/Behaviour` | RoomLightControllerのON/OFFをTimelineクリップで制御。lightsOn（bool）パラメータのみ。クリップ終了後も状態維持（復元しない）。sleep/exit/entryアニメーションにクリップ配置してタイミング調整可能。**ファイル分割必須**（LightControlTrack.cs / LightControlClip.cs / LightControlBehaviour.cs）— PlayableAssetを独立ファイルにしないとWebGLビルドでスクリプト参照が解決されない |

### st/lp/ed パターン Timeline構成

st（開始）/ lp（ループ）/ ed（終了）のアニメーションクリップを1つのTimelineで管理（**60fps**）。
Walk/Run/Interact等で共通のパターン。

#### Walk Timeline例

```
TL_Walk
│
├─ AnimationTrack
│   ├─ [0F-30F] walk01_st              ← 歩き開始
│   ├─ [30F-90F] walk01_lp             ← ループ区間
│   └─ [90F-120F] walk01_ed            ← 歩き終了
│
├─ LoopRegionTrack
│   └─ [30F-90F] LoopRegionClip        ← walk01_lpと同じ位置・長さ
│
├─ ActionCancelTrack
│   └─ [30F-89F] ActionCancelClip      ← lpリージョン中にキャンセル可能
│       └─ allowedTransitions: [TL_interact_sit01, ...]
│
├─ InertialBlendTrack
│   └─ [0F-24F] InertialBlendClip      ← 慣性補間
│
└─ MoveSpeedTrack
    ├─ [0F-30F] MoveSpeedClip           ← st区間: speedCurve(0→0.1, 1→1.0), adjustAnimatorSpeed=false
    └─ [30F-90F] MoveSpeedClip          ← lp区間: speedCurve(1.0), adjustAnimatorSpeed=true
```

※edクリップがない場合、LoopRegionClip.endに到達した時点で終了。

#### Interact Timeline例

```
TL_interact_sit01
│
├─ AnimationTrack
│   ├─ [0F-140F] interact_sit01_st   ← 座り開始
│   ├─ [140F-260F] interact_sit01_lp ← ループ区間
│   └─ [260F-400F] interact_sit01_ed ← 立ち上がり
│
├─ LoopRegionTrack
│   └─ [140F-260F] LoopRegionClip      ← lpと同じ位置・長さ
│
├─ ActionCancelTrack
│   └─ [360F-400F] ActionCancelClip    ← ed終盤にキャンセル可能（立ち上がりスキップ）
│       └─ allowedTransitions: [TL_Idle, TL_Walk]
│
├─ PositionBlendTrack
│   └─ [0F-60F] PositionBlendClip
│       └─ 現在位置→InteractionPoint位置を補間
│
├─ RotationBlendTrack
│   └─ [0F-60F] RotationBlendClip
│       └─ 現在角度→InteractionPoint角度を補間
│
└─ InertialBlendTrack
    └─ [0F-24F] InertialBlendClip      ← 慣性補間
```

※ActionCancelClipの配置はアニメーション設計により異なる。
interact_sit01ではed（立ち上がり）の終盤に配置し、次のアクションが予約されている場合に
立ち上がり完了を待たずキャンセル→次Timeline遷移（InertialBlendで姿勢補間）する。

※フレーム数はアニメーションにより変動。上記は参考値。

### ループリージョン制御

CharacterAnimationControllerがLoopRegionTrackのクリップ情報を読み取り、ループを制御。
Walk/Run/Interact等、st/lp/edパターンを使うすべてのステートで共通。

#### ループ領域の読み取り

```csharp
// CharacterAnimationController - Timelineセットアップ時
private void SetupLoopRegion(TimelineAsset timeline)
{
    foreach (var track in timeline.GetOutputTracks())
    {
        if (track is LoopRegionTrack)
        {
            foreach (var clip in track.GetClips())
            {
                var loopClip = clip.asset as LoopRegionClip;
                double frameRate = timeline.editorSettings.frameRate;
                double frameOffset = loopClip.loopEndOffsetFrames / frameRate;

                _loopStartTime = clip.start;
                _loopEndTime = clip.end + frameOffset;  // デフォルト: -1F
                _endStartTime = clip.end;
                _hasLoopRegion = true;
            }
        }
    }
}
```

#### ループ制御

```csharp
// CharacterAnimationController.Update()
// 予測ベース: director.timeは前フレームの評価時刻のため、
// Director評価フェーズで currentTime + deltaTime が loopEnd を超えると
// ed clipとのブレンドが1フレーム発生する。
// deltaTimeを加味して予測し、超過前にジャンプすることで防止。
if (_isInLoop && !_shouldExitLoop && director != null)
{
    double predictedTime = currentTime + Time.deltaTime;
    if (predictedTime >= _loopEndTime || currentTime >= _loopEndTime)
    {
        _loopJumpOccurred = true;
        director.time = _loopStartTime;  // ループ開始位置に戻す
    }
}

// 終了リクエスト（InteractionController / NavigationController から呼ばれる）
public void RequestEndPhase()
{
    _shouldExitLoop = true;
    if (_isInLoop)
    {
        director.time = _endStartTime;  // ed開始位置へジャンプ
    }
}
```

#### 呼び出し元

| 呼び出し元 | タイミング | 後続処理 |
|---|---|---|
| InteractionController | ExitLoop要求時 | OnEndPhaseComplete → 家具解放 → Idle |
| CharacterAnimationController.StopWalkWithEndPhase() | NavMesh目的地到着時（FinalTurning完了） | OnEndPhaseComplete → OnWalkEndPhaseComplete → ReturnToIdle() |
| ThinkingのStopThinkingAndReturn() | Thinking停止時 | OnEndPhaseComplete → OnThinkingEndPhaseComplete → 復帰先PlayState |
| EmoteのLoopRegionあり再生 | Emoteホールド→ed完了時 | OnEndPhaseComplete → OnEmoteEndPhaseComplete → 復帰先PlayState |

#### Walk終了フェーズ（StopWalkWithEndPhase）

NavMesh目的地到着時にwalk_edを再生してからIdleに遷移する仕組み。

```
NavigationController.UpdateFinalTurning() → 回転完了
  → animationController.StopWalkWithEndPhase()
    ├─ LoopRegionあり → _isWalkEndPhaseActive=true, OnEndPhaseComplete購読, RequestEndPhase()
    │   → walk_ed再生（InertialBlendTrackで補間）
    │   → CompleteEndPhase() → OnWalkEndPhaseComplete → ReturnToIdle()
    └─ LoopRegionなし → ReturnToIdle()（即時Idle遷移）
  → callback?.Invoke()（到着コールバック）
```

**例外（walk_edスキップ）:**
- **Talk到着**: 到着コールバック → PlayState(Talk) → walk_edをPlayState内クリーンアップで中断
- **emote予約あり**: PlayEmoteAfterWalkコルーチンがIsInEndPhase検出 → ReturnToIdle()で中断 → emote再生
- **StopMoving/CancelMovement**: 強制停止 → ReturnToIdle()直接呼び出し（walk_edなし）

**中断時クリーンアップ**: PlayState()/PlayTimeline()の先頭で_isWalkEndPhaseActiveを確認し、OnEndPhaseComplete購読を解除。

---

## Custom Timeline Tracks

インタラクション用のカスタムTimelineトラック。BlendPivot方式で位置・回転を補間。

### PositionBlendTrack

BlendPivotのワールド位置を current → target に補間。

```csharp
[TrackBindingType(typeof(Transform))]  // BlendPivotをバインド
public class PositionBlendTrack : TrackAsset
{
    [System.NonSerialized] public Vector3 targetPosition;
    [System.NonSerialized] public bool hasTarget;
}

public class PositionBlendBehaviour : PlayableBehaviour
{
    public AnimationCurve blendCurve;
    public Vector3 targetPosition;
    public bool hasTarget;

    private Vector3 _startPosition;
    private bool _initialized;
    private Transform _blendPivot;

    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        var blendPivot = playerData as Transform;
        if (blendPivot == null || !hasTarget) return;

        // 初回のみ開始位置を記録
        if (!_initialized)
        {
            _blendPivot = blendPivot;
            _startPosition = blendPivot.position;
            _initialized = true;
        }

        // 正規化時間からブレンド値を取得
        float normalizedTime = (float)(playable.GetTime() / playable.GetDuration());
        float blend = blendCurve?.Evaluate(normalizedTime) ?? normalizedTime;

        // ワールド位置を補間
        blendPivot.position = Vector3.Lerp(_startPosition, targetPosition, blend);
    }
}
```

### RotationBlendTrack

BlendPivotのワールド回転を current → target に補間。
Y軸回転の方向を指定可能（最短/時計回り/反時計回り）。

#### 回転方向オプション

```csharp
public enum RotationDirection
{
    Shortest,         // 最短距離で補間（デフォルト）
    Clockwise,        // 時計回り（右回転）で補間
    CounterClockwise  // 反時計回り（左回転）で補間
}
```

| 設定 | 説明 | 例: 210° → 180° |
|------|------|-----------------|
| Shortest | 最短距離で補間 | -30°（反時計回り） |
| Clockwise | 時計回りに強制 | +330°（時計回り） |
| CounterClockwise | 反時計回りに強制 | -30°（反時計回り） |

#### RotationBlendClip Inspector設定

- **Blend Curve**: イージングカーブ（デフォルト: EaseInOut）
- **Rotation Direction**: 回転方向の選択

#### 実装

```csharp
[TrackBindingType(typeof(Transform))]  // BlendPivotをバインド
public class RotationBlendTrack : TrackAsset
{
    [System.NonSerialized] public Quaternion targetRotation;
    [System.NonSerialized] public bool hasTarget;
}

public class RotationBlendBehaviour : PlayableBehaviour
{
    public AnimationCurve blendCurve;
    public Quaternion targetRotation;
    public bool hasTarget;
    public RotationDirection rotationDirection;

    private Vector3 _startEuler;
    private Vector3 _targetEuler;
    private float _yDelta;  // Y軸の回転差分（方向考慮済み）
    private bool _initialized;
    private Transform _blendPivot;

    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        var blendPivot = playerData as Transform;
        if (blendPivot == null || !hasTarget) return;

        // 初回のみ開始回転を記録
        if (!_initialized)
        {
            _blendPivot = blendPivot;
            _startEuler = blendPivot.rotation.eulerAngles;
            _targetEuler = targetRotation.eulerAngles;
            _yDelta = CalculateYDelta(_startEuler.y, _targetEuler.y, rotationDirection);
            _initialized = true;
        }

        // 正規化時間からブレンド値を取得
        float normalizedTime = (float)(playable.GetTime() / playable.GetDuration());
        float blend = blendCurve?.Evaluate(normalizedTime) ?? normalizedTime;

        // Y軸は回転方向を考慮した補間
        float newY = _startEuler.y + _yDelta * blend;
        // X/Z軸は線形補間
        float newX = Mathf.LerpAngle(_startEuler.x, _targetEuler.x, blend);
        float newZ = Mathf.LerpAngle(_startEuler.z, _targetEuler.z, blend);

        blendPivot.rotation = Quaternion.Euler(newX, newY, newZ);
    }

    private float CalculateYDelta(float startY, float targetY, RotationDirection direction)
    {
        float delta = targetY - startY;
        // -180〜180の範囲に正規化
        while (delta > 180f) delta -= 360f;
        while (delta < -180f) delta += 360f;

        switch (direction)
        {
            case RotationDirection.Clockwise:
                if (delta <= 0f) delta += 360f;  // 正の値に強制
                break;
            case RotationDirection.CounterClockwise:
                if (delta >= 0f) delta -= 360f;  // 負の値に強制
                break;
        }
        return delta;
    }
}
```

### InertialBlendTrack / InertialBlendHelper

アニメーション切り替え時の慣性補間（Inertial Blending）を行うシステム。
従来のCrossFade（線形補間）とは異なり、遷移先のアニメーションに即座に切り替えた上で、
遷移元との「差分（Offset）」を時間経過で0に収束させる手法。

#### 理論

```
x(t) = Target(t) + Offset(t)
```

- `Offset(0)` = 遷移元ポーズ - 遷移先ポーズ（初期差分）
- `Offset(clipDuration)` = 0（クリップ終了時に完全収束）

#### 仕様

| 項目 | 内容 |
|------|------|
| 実行コンポーネント | InertialBlendHelper（MonoBehaviour） |
| 適用対象 | Humanoidボーン（InertialBlendClipのInspectorから選択） |
| 座標系 | ローカル座標（localPosition / localRotation） |
| 減衰方式 | Critical Damping（臨界減衰, ζ=1.0） |
| 収束タイミング | ブレンド時間終了時に99%収束 |
| ExecutionOrder | 20000（UniVRM SpringBone処理より後） |
| ボーン参照方式 | Awake時キャッシュ（後述のTransformキャッシュパターン） |

#### アーキテクチャ（Pre-Update Restoreパターン）

ボーンのTransformを安全に上書きするために、以下の実行順序で処理する。

```
1. Update [ExecutionOrder 20000]
   └─ 前フレームで適用したオフセットを「クリーン位置」に復元
      （Animatorが汚染されたTransformから計算するのを防止）

2. Animator評価
   └─ クリーンな状態から新しいポーズを計算

3. Vrm10Instance [ExecutionOrder 11000]
   └─ ControlRigがAnimator出力をボーンに上書き（localRotation直接代入）

4. InertialBlendPrePass [ExecutionOrder 11005]
   └─ IB動作中: クリーンポーズ保存→IB補正済みポーズ適用
      ├── WaitingFirst/SecondFrame: 前ポーズを適用
      └── Blending: IBオフセットを計算・適用（IB.LateUpdateと同じ減衰計算）
      （SpringBoneが新Timelineの未補正ポーズで計算するのを全期間で防止）

5. FastSpringBoneService [ExecutionOrder 11010]
   └─ SpringBone計算（PrePassで補正済みのポーズを入力として使用）

6. LateUpdate [ExecutionOrder 20000] - InertialBlendHelper
   └─ PrePass処理済みの場合: ステート遷移・完了チェックのみ
   └─ PrePass無しの場合（フォールバック）:
      └─ UpdateCleanPoseCache(): 全ボーンのクリーンポーズを_lastCleanPoseに保存
      └─ ブレンド対象ボーンのクリーン値をBoneBlendDataに保存
      └─ 状態に応じた処理（WaitingFirstFrame/WaitingSecondFrame/Blending）
   └─ SkinnedMeshRendererがオフセット適用済み位置でメッシュ描画

7. LateUpdate [ExecutionOrder 20050] - AdditiveOverrideHelper
   └─ 非加算ボーンのスナップショット復元

8. LateUpdate [ExecutionOrder 20100] - CharacterLookAtController
   └─ TryGetCleanPose()で頭・胸のクリーン回転を取得
   └─ LookAtオフセットを適用
```

**重要**: `DefaultExecutionOrder(20000)` が必須。UniVRM 1.0のSpringBone処理（11000付近）
よりも後にLateUpdateを実行しないと、UniVRMがTransformを上書きして慣性補間が無効になる。

#### 対象ボーン選択

InertialBlendClipのInspectorで`[HumanBoneSelect]`属性付きAdvancedDropdownから
任意のHumanoidボーンを選択可能。カテゴリ分類されたドロップダウンで直感的に操作できる。

```
AdvancedDropdownカテゴリ:
├── Torso（体幹）: Hips, Spine, Chest, UpperChest
├── Head（頭部）: Neck, Head, LeftEye, RightEye, Jaw
├── Left Arm（左腕）: LeftShoulder, LeftUpperArm, LeftLowerArm, LeftHand
├── Right Arm（右腕）: 同上
├── Left Leg（左脚）: LeftUpperLeg, LeftLowerLeg, LeftFoot, LeftToes
├── Right Leg（右脚）: 同上
├── Left Fingers（左指）: Proximal/Intermediate/Distal × 5指
└── Right Fingers（右指）: 同上
```

デフォルト値は全Humanoidボーン（LeftEye, RightEye, Jaw除外）。

#### テンプレートボタン

`BoneTemplateUtility`（共有ユーティリティ）により、InertialBlendClip/EmotePlayableClip/ThinkingPlayableClipの
Inspectorにテンプレートボタンを表示し、ボーンリストを一括設定可能。

| テンプレート | 内容 |
|---|---|
| 全て | 全Humanoidボーン（LeftEye, RightEye, Jaw除外） |
| 指除外 | 全て - 指ボーン（Thumb/Index/Middle/Ring/Little） |
| 上半身 | Spine, Chest, UpperChest, Neck, Head, Shoulder, UpperArm, LowerArm, Hand + 全指ボーン |
| 下半身 | Hips, UpperLeg, LowerLeg, Foot, Toes |

**注意**: LeftEye, RightEye, Jawは全テンプレート共通で除外。これらのボーンは慣性補間の対象外とする。

#### データフロー

```
[Timeline Editor]
InertialBlendClip.targetBones (Inspector上でAdvancedDropdown / テンプレートボタンから設定)
  ↓
[Runtime: CharacterAnimationController.SetupInertialBlendTrack()]
PlayState() / PlayTimeline() から呼び出し
track.GetClips() → clip.asset as InertialBlendClip → targetBones読み取り
  ↓
InertialBlendHelper.StartInertialBlend(duration, targetBones)
  ↓
_boneTransformCache[bone] でAwake時キャッシュから参照を取得
  ↓
BoneBlendData[] に格納して毎フレーム処理
```

**対応アニメーション**: Idle, Walk, Run, Talk, Emote, Interact すべてのTimelineで使用可能。
InertialBlendTrackを含むTimelineであれば、CharacterAnimationController経由の再生時に自動的にInertialBlendHelperが起動される。

#### クリーンポーズキャッシュ（_lastCleanPose）

毎フレームのLateUpdate先頭（IB/AdditiveOverride/LookAt適用前）で全Humanoidボーンの
ローカル座標をキャッシュする仕組み。ブレンド非動作時も継続更新する。

**目的:**
- `StartInertialBlend`時に「前のポーズ」として使用。ボーンの現在値はLookAt/IBオフセットで
  汚染されている可能性があるため、キャッシュしたクリーン値を使うことで二重適用を防止。
- `TryGetCleanPose()`公開APIで外部コンポーネント（CharacterLookAtController等）が
  IBオフセット適用前のAnimator出力値を取得可能。

```
LateUpdate先頭 → UpdateCleanPoseCache() → 全ボーンのlocalPos/Rotを_lastCleanPoseに保存
                                            ↓
                                  ブレンド処理（オフセット適用）
                                            ↓
                                  描画（オフセット適用済み）
```

**注意: AdditiveOverrideHelper(AO)との関係**

`_lastCleanPose`はIB.LateUpdate(20000)で保存されるため、AO.LateUpdate(20050)の補正を**含まない**。
AO動作中にStopOverride→PlayStateすると、IBが`_lastCleanPose`（AO補正なし=例えば立ちポーズ）を
起点にブレンドを開始し、実際の表示ポーズ（座りポーズ等）との差が一瞬見えるポーズフラッシュが発生する。

この問題を防ぐため、`SnapshotCurrentPoseAsClean()`メソッドが用意されている。
AO停止前にこのメソッドを呼ぶことで、AO補正込みの実際の表示ポーズを`_lastCleanPose`に上書きし、
次回IBが正しいポーズからブレンドを開始する。次回LateUpdateの`UpdateCleanPoseCache`で正しい値に
再上書きされるため、一時的な上書きに留まる。

`SnapshotCurrentPoseAsClean()`の内部実装は`UpdateCleanPoseCache()`への委譲。
用途が異なる（外部からのAO補正保存 vs LateUpdate先頭でのAnimator出力保存）ため
メソッド名は分離しているが、実処理は共通。

この一連の処理は`StopAdditiveOverrideWithSnapshot()`ヘルパーメソッドに集約されている:

```csharp
// CharacterAnimationController内のprivateヘルパー
private void StopAdditiveOverrideWithSnapshot()
{
    if (additiveOverrideHelper != null && additiveOverrideHelper.IsActive)
    {
        inertialBlendHelper?.SnapshotCurrentPoseAsClean();
    }
    additiveOverrideHelper?.StopOverride();
}
```

**適用箇所（CharacterAnimationController内、全7箇所から`StopAdditiveOverrideWithSnapshot()`を呼び出し）:**
- `OnEmoteTimelineStopped()` - Emote自然終了（stoppedイベント）
- `OnEmoteEndPhaseComplete()` - Emote終了フェーズ完了
- `ForceStopEmote()` - Emote強制停止
- `ForceStopEmoteToEnd()` - Emote強制停止→ed再生
- `ForceStopThinking()` - Thinking強制停止
- `ForceStopThinkingToEnd()` - Thinking強制停止→ed再生
- `OnThinkingEndPhaseComplete()` - Thinking終了フェーズ完了

#### フォールバック慣性補間（StartInertialBlendAllBones）

InertialBlendTrackを持たないTimelineの遷移時に、`director.Stop()`による1Fポーズジャンプを
防止するため、全Humanoidボーン（目・顎除く）を対象とした短時間（0.15秒）のフォールバック
慣性補間を自動的に開始する。

```csharp
// CharacterAnimationController.PlayState / PlayTimeline 内
bool hasInertialBlendTrack = SetupInertialBlendTrack(timeline);
if (!hasInertialBlendTrack && inertialBlendHelper != null)
{
    inertialBlendHelper.StartInertialBlendAllBones(FALLBACK_INERTIAL_BLEND_DURATION); // 0.15秒
}
```

#### JumpToEndPhase時のIB開始（lp→ed遷移スムージング）

lp→ed遷移（`JumpToEndPhase`）時に、IBを開始してポーズポップを防止する。

**背景:** lpクリップのループ途中ポーズからedクリップの開始ポーズへ`director.time`をジャンプさせるため、
ポーズが一致しない場合に1Fのポーズポップ（フラッシュ）が発生する。
IBを使ってlpポーズからedポーズへスムーズに遷移させることで解消する。

既にIBがアクティブな場合（同一フレームでPlayStateが先行実行された場合など）も、
`StartInertialBlendAllBones`が内部で`CaptureVisualStateIfActive → RestoreCleanIfActive`を
行うため、旧IBのビジュアル状態を正しく引き継いで新IBを開始する。

```csharp
// CharacterAnimationController.JumpToEndPhase 内
if (inertialBlendHelper != null)
{
    inertialBlendHelper.StartInertialBlendAllBones(FALLBACK_INERTIAL_BLEND_DURATION); // 0.15秒
}
```

#### データ構造

```csharp
// ボーンごとの慣性補間データ
private struct BoneBlendData
{
    public Transform transform;           // Awakeでキャッシュした参照
    public Vector3 previousLocalPosition; // 遷移元ポーズ
    public Quaternion previousLocalRotation;
    public Vector3 initialPositionOffset; // 初期差分
    public Quaternion initialRotationOffset;
    public Vector3 cleanLocalPosition;    // Animatorが設定したクリーン値
    public Quaternion cleanLocalRotation;
}

// Awakeで全Humanoidボーンの参照をキャッシュ
private Dictionary<HumanBodyBones, Transform> _boneTransformCache;

// LateUpdateで保存したクリーンポーズのキャッシュ（Transform → ローカル座標）
// StartInertialBlend時に「前のポーズ」として使用する。
// ボーンの現在値はLookAtやInertialBlendのオフセットで汚染されている可能性があるため、
// Animator出力直後に保存したクリーン値を使うことでオフセットの二重適用を防止する。
private Dictionary<Transform, (Vector3 localPos, Quaternion localRot)> _lastCleanPose;

// StartInertialBlendで対象ボーンのみ配列として構築
private BoneBlendData[] _bones;
private int _boneCount;
```

#### Critical Damping計算式

臨界減衰（ζ = 1.0）の簡略化応答（速度項なし）：

```
x(t) = x₀ · (1 + ω·t) · e^(-ω·t)
```

ブレンド時間終了時に99%収束させるためのω：

```csharp
float omega = 4.605f / blendDuration;  // ln(100) ≈ 4.605
```

#### 処理フロー

```
StartInertialBlend(duration, targetBones) 呼び出し（Timeline再生前）
    ├── _boneTransformCacheから対象ボーンのTransformを取得
    ├── BoneBlendData[] を構築
    ├── 全対象ボーンの localPosition/localRotation をキャッシュ
    ├── omega計算（ln(100) / blendDuration）
    └── State → WaitingFirstFrame

[Frame N] LateUpdate - WaitingFirstFrame
    ├── 全対象ボーンのクリーン位置を保存
    ├── 【ちらつき防止】全対象ボーンに前のポーズを強制適用
    └── State → WaitingSecondFrame

[Frame N+1] Update
    └── 全対象ボーンのクリーン位置に復元（Animator計算前）
[Frame N+1] LateUpdate - WaitingSecondFrame
    ├── 全対象ボーンのクリーン位置を保存
    ├── InitializeBlend: 各ボーンの Offset = prevPose - cleanPose
    ├── CalculateAndApplyOffset（decay=1.0、完全に前ポーズを維持）
    └── State → Blending

[Frame N+2~] 毎フレーム
    Update:
        └── 全対象ボーンのクリーン位置に復元
    LateUpdate:
        ├── 全対象ボーンの新しいクリーン位置を保存
        ├── decay = (1 + ω·t) · e^(-ω·t) を計算（1回のみ）
        ├── 各ボーン: posOffset = initialOffset * decay
        ├── 各ボーン: rotOffset = Slerp(identity, initialRotOffset, decay)
        └── 各ボーン: localPos = cleanPos + posOffset, localRot = cleanRot * rotOffset

[ブレンド完了] elapsedTime >= blendDuration
    ├── 全対象ボーンをクリーン位置に戻す（オフセット除去）
    └── State → Idle
```

#### InertialBlendTrack（Timeline側）

InertialBlendTrackはTimeline上にクリップとして配置される。
実際のボーン操作はInertialBlendHelperが行い、トラックは開始トリガーとして機能。
InertialBlendClipが対象ボーンリストとブレンド時間を定義する。

```
Timeline
├── AnimationTrack
│   └── AnimationClip
└── InertialBlendTrack
    └── InertialBlendClip
        ├── targetBones: List<HumanBodyBones>（Inspector選択可能）
        └── クリップの長さ = ブレンド時間
```

#### 使用例

1. TimelineにAnimationTrackを追加、AnimationClipを配置
2. InertialBlendTrackを追加
3. InertialBlendClipをAnimationClipの開始位置に配置
4. InertialBlendClipの長さでブレンド時間を調整（例: 0.4秒）
5. InertialBlendClipのInspectorでテンプレートボタンまたはAdvancedDropdownから対象ボーンを設定
6. トラックにAnimatorをバインド
7. VRMインスタンスにInertialBlendHelperコンポーネントが必要（VrmLoaderが自動追加）

#### 制限事項

- RootMotionには適用しない（RootMotionForwarderとは独立）
- UniVRM 1.0環境では`DefaultExecutionOrder(20000)`が必須
  - 10000以下ではUniVRMのSpringBone処理に上書きされる
- Transform参照はAwake時のキャッシュを使用する必要がある（後述のTransformキャッシュパターン参照）
- `JumpToEndPhase`（lp→ed遷移）時は新たにIBが開始される（lpの途中ポーズ→edの開始ポーズをスムージング）

#### ローカル座標を使用する理由

ワールド座標で実装した場合、前フレームのオフセット適用が次フレームのAnimator入力に蓄積し、
フレームごとにオフセットが増大する問題が発生する。ローカル座標を使用し、
Updateでクリーン位置に復元することで蓄積を防止する。

#### 公開API

| メソッド/プロパティ | 用途 |
|---|---|
| `StartInertialBlend(duration, targetBones)` | InertialBlendTrackから呼び出し（指定ボーン） |
| `StartInertialBlendAllBones(duration)` | フォールバック用（全ボーン、目・顎除く） |
| `CancelBlend()` | 動作中IBをキャンセル |
| `ApplyPrePassIfNeeded()` | PrePass用：SpringBone計算前にWaitingFirstFrame/SecondFrameのポーズ補正を適用 |
| `IsActive` | 慣性補間が動作中かどうか |
| `TryGetCleanPose(bone, out pos, out rot)` | 指定ボーンのAnimator出力値（クリーン値）を取得 |

#### 関連ファイル

| ファイル | 役割 |
|---------|------|
| `Scripts/Timeline/InertialBlendHelper.cs` | 慣性補間の実処理（マルチボーン対応） |
| `Scripts/Timeline/InertialBlendClip.cs` | Timelineクリップ（targetBonesフィールド） |
| `Scripts/Timeline/InertialBlendTrack.cs` | Timelineトラック定義 |
| `Scripts/Core/HumanBoneSelectAttribute.cs` | PropertyAttribute マーカー |
| `Editor/HumanBoneSelectDrawer.cs` | AdvancedDropdownボーン選択UI |
| `Editor/InertialBlendClipEditor.cs` | テンプレートボタン付きカスタムInspector |
| `Scripts/Timeline/InertialBlendPrePass.cs` | SpringBone前ポーズ補正（後述） |

#### InertialBlendPrePass（SpringBone前ポーズ補正）

IB動作中のポーズ補正をFastSpringBoneService(11010)の前に適用するプリパスコンポーネント。
WaitingFirstFrame/SecondFrameだけでなく、**Blending状態でも動作**し、IB全期間にわたって
SpringBoneがIB補正済みの滑らかなポーズで計算できるようにする。

**解決する問題:**
IB(20000)がボーンを補正する処理はSpringBone(11010)より後に実行される。
PrePass無しの場合、SpringBoneは新Timelineの未補正（生）ポーズで計算するため：
- WaitingFirst/SecondFrame: 突然のポーズ変化で髪・揺れ物がポップ
- Blending: IBがボディを滑らかに補正しても、SpringBoneは生ポーズで計算。
  IB補正前後でポーズ差がある場合、髪が「頭が急に動いた」かのように反応する

**実行順序の制約:**
```
Vrm10Instance (11000): ControlRigがAnimator出力をボーンに上書き（localRotation直接代入）
  ↓ ← ここより前では補正が無効化される（ControlRigが上書きするため）
InertialBlendPrePass (11005): ApplyPrePassIfNeeded()でIB補正済みポーズに変換
  ↓
FastSpringBoneService (11010): 補正済みポーズでSpringBone計算
  ↓
InertialBlendHelper (20000): ステート遷移・完了チェック（PrePass処理済みの場合）
```

**重要:** ExecutionOrderは**11005**でなければならない。
- 10999以前: Vrm10InstanceのControlRig(11000)がlocalRotationを直接上書きするため補正が無効
- 11010以降: SpringBoneが未補正ポーズで計算済みのため意味がない

**処理内容（ApplyPrePassIfNeeded）:**

| IBステート | PrePassの処理 |
|---|---|
| WaitingFirstFrame/SecondFrame | クリーン保存→前ポーズ適用（SpringBoneが突然の変化を見ない） |
| Blending | クリーン保存→_elapsedTime更新→IBオフセット計算・適用（SpringBoneが滑らかなポーズを見る） |
| Idle | 何もしない |

IB.LateUpdate(20000)は`_prePassApplied`フラグを見て、PrePass処理済みの場合はステート遷移と
完了チェックのみ行い、オフセット計算をスキップする。

**セットアップ:** VrmLoaderがInertialBlendHelper追加後に自動で追加・参照設定。

### LoopRegionTrack / LoopRegionClip

Timeline上のアニメーションクリップに対してループ領域を定義するカスタムトラック。
従来のLoopStart / LoopEnd / EndStart Signal マーカー3つを、**クリップ1つ**で置き換える。

#### 仕様

| 項目 | 内容 |
|------|------|
| Track | LoopRegionTrack（バインド不要） |
| Clip | LoopRegionClip |
| 用途 | lpアニメーションクリップの位置・長さに合わせて配置 |

#### LoopRegionClipの自動導出

クリップの配置位置から以下の値を自動導出する：

| 値 | 導出元 | 説明 |
|---|---|---|
| LoopStart | `clip.start` | ループ開始時間 |
| LoopEnd | `clip.end + loopEndOffsetFrames / frameRate` | ループ終了時間（デフォルト: -1F） |
| EndStart | `clip.end` | 終了アニメーション開始時間 |

`loopEndOffsetFrames`のデフォルトは **-1**。LoopEndをlpクリップ終了の1F手前とする。
これはクリップ境界でRoot Motion累積がリセットされる問題への対策。

#### Inspector

```
┌─────────────────────────────────┐
│ LoopRegionClip                  │
│                                 │
│ Loop End Offset: [-1] frames    │
└─────────────────────────────────┘
```

#### edクリップが無い場合

EndStart到達時点でOnEndPhaseCompleteが発火し、即座に次のステート（Idle等）に遷移。

#### Signalとの比較

| 項目 | Signal方式（旧） | LoopRegionClip方式（新） |
|---|---|---|
| 配置 | マーカー3つ | クリップ1つ |
| 位置調整 | 各マーカーを個別調整 | クリップの移動/リサイズで一括調整 |
| 視認性 | 小さいアイコン | 帯表示、lpの範囲が一目瞭然 |
| -1Fオフセット | 手動設定 | 自動適用（loopEndOffsetFrames） |

#### 関連ファイル

| ファイル | 役割 |
|---------|------|
| `Scripts/Timeline/LoopRegionTrack.cs` | TrackAsset定義 |
| `Scripts/Timeline/LoopRegionClip.cs` | PlayableAsset（loopEndOffsetFramesフィールド） |
| `Scripts/Timeline/LoopRegionBehaviour.cs` | PlayableBehaviour（構造上必要、処理なし） |

### InteractionEndTrack / InteractionEndClip

LoopRegionを持たないワンショットInteractタイムラインの完了ポイントを定義するカスタムトラック。
クリップの配置位置（`clip.start`）がInteractionEnd発火タイミングとなる。

#### 仕様

| 項目 | 内容 |
|------|------|
| Track | InteractionEndTrack（バインド不要、トラックカラー: オレンジ） |
| Clip | InteractionEndClip |
| 用途 | タイムライン終了付近に配置し、完了タイミングを定義 |

#### 完了検知の仕組み

CharacterAnimationController.Update()でdirector.timeを監視し、
`director.time >= InteractionEndClipの開始位置` になった時点で`OnInteractionEndReached`イベントを発火する。
`director.stopped`イベント（WrapMode.Noneで不安定）に依存しない確実な完了検知方式。

**WebGLフォールバック**: WebGLビルドではWrapMode.NoneのTimeline終了時に`PlayableDirector.stopped`イベントが
発火せず、`director.time`が0にリセットされ`state=Paused`になるケースがある（エディタでは再現しない）。
この場合、`director.time`がInteractionEnd時刻に到達しないまま再生が終了するため、
Update()内で`Playing→非Playing`への状態遷移を検知してInteractionEndを発火するフォールバックを実装している
（`_interactionEndDirectorWasPlaying`フラグによる追跡）。

#### 運用ルール

**LoopRegionを持たないInteractタイムラインには必ずInteractionEndClipを配置する。**

| タイムラインタイプ | 完了検知方式 |
|-------------------|-------------|
| LoopRegionあり（sit, sleep等） | LoopRegion + OnEndPhaseComplete |
| LoopRegionなし（exit, entry等） | InteractionEndClip + OnInteractionEndReached |

#### 購読先

| コントローラー | 用途 |
|---------------|------|
| InteractionController | interact_exit等のワンショットInteract完了検知 |
| OutingController | interact_entry01のEntry完了検知（PlayState後に動的購読） |

#### 関連ファイル

| ファイル | 役割 |
|---------|------|
| `Scripts/Timeline/InteractionEndTrack.cs` | TrackAsset定義 |
| `Scripts/Timeline/InteractionEndClip.cs` | PlayableAsset（ClipCaps.None） |
| `Scripts/Timeline/InteractionEndBehaviour.cs` | PlayableBehaviour（構造上必要、処理なし） |

### ActionCancelTrack / ActionCancelClip

Timeline上にキャンセル可能な区間を定義するカスタムトラック。
クリップの持続期間中はキャンセルして別のTimelineアニメーションに即時遷移が可能。

#### 仕様

| 項目 | 内容 |
|------|------|
| Track | ActionCancelTrack（バインド不要） |
| Clip | ActionCancelClip |
| 用途 | キャンセル可能区間の定義 + 遷移先Timeline管理 |

#### ActionCancelClipプロパティ

| プロパティ | 型 | 説明 |
|---|---|---|
| allowedTransitions | `List<TimelineAsset>` | キャンセル時に遷移可能なTimelineリスト |

#### Inspector

```
┌─────────────────────────────────┐
│ ActionCancelClip                │
│                                 │
│ Allowed Transitions:            │
│   [0] TL_Idle          [x]     │
│   [1] TL_Walk          [x]     │
│   [+] Add                      │
└─────────────────────────────────┘
```

#### キャンセル状態の検出

Timelineセットアップ時にActionCancelTrackのクリップ情報をキャッシュし、時間ベースで判定：

```csharp
// CharacterAnimationController
private struct CancelRegion
{
    public double startTime;
    public double endTime;
    public List<TimelineAsset> allowedTransitions;
}

private List<CancelRegion> _cancelRegions;

// セットアップ時: clip.start / clip.end を記録
// 毎フレーム: director.time がいずれかのCancelRegion内かチェック
public bool CanCancel => _cancelRegions.Any(
    r => director.time >= r.startTime && director.time <= r.endTime);

public List<TimelineAsset> GetAllowedTransitions() => ...;
```

#### キャンセル実行フロー（InteractionController連携）

インタラクションのed再生中にCancelRegionに到達すると、ed完了を待たずに次のアクションへ遷移する。
InteractionControllerの`ExitLoopWithCallback()`がキャンセルリクエストの起点。

```
1. ExitLoopWithCallback(callback)
   ├→ _pendingAction = callback, state = Ending
   ├→ HasCancelRegions == true の場合:
   │   └→ animationController.RequestCancelAtRegion()
   │       ├→ CanCancel == true（既にCancelRegion内）→ 即座にFireCancelRegionReached()
   │       └→ CanCancel == false → _shouldCancelAtRegion = true（フラグセット）
   └→ animationController.RequestEndPhase()（ed再生開始）

2. Update()（ed再生中、毎フレーム）
   └→ _shouldCancelAtRegion && CanCancel
       └→ FireCancelRegionReached()

3. CharacterAnimationController:
   ├→ _shouldCancelAtRegion = false
   ├→ _isEnding = false（Stop時のCompleteEndPhase二重発火防止）
   └→ OnCancelRegionReached イベント発火

4. InteractionController (OnAnimCancelRegionReached):
   ├→ animationController.Stop()（Timeline停止）
   ├→ 家具解放、状態リセット、BlendPivotリセット
   └→ pendingAction直接実行（Idle中間遷移なし）
       └→ ボーンがインタラクト中のポーズを保持 → 次TimelineのIBが正しくブレンド
```

**重要: Idle中間遷移の回避**

通常のed完了時（`OnInteractionComplete`）はReturnToIdle + 2フレーム遅延でボーンをリセットしてからpendingActionを実行する（座り戻し防止）。
キャンセル時はed途中で中断するため、Idle経由するとインタラクトポーズが失われ、次TimelineのInertialBlendClipが正しい差分を計算できない。
そのためキャンセル時はIdle経由せず直接pendingActionを実行し、IBにインタラクトポーズ→次ポーズのブレンドを任せる。

#### 関連ファイル

| ファイル | 役割 |
|---------|------|
| `Scripts/Timeline/ActionCancelTrack.cs` | TrackAsset定義 |
| `Scripts/Timeline/ActionCancelClip.cs` | PlayableAsset（allowedTransitionsフィールド） |
| `Scripts/Timeline/ActionCancelBehaviour.cs` | PlayableBehaviour（構造上必要、処理なし） |
| `Scripts/Character/CharacterAnimationController.cs` | CancelRegion検出・RequestCancelAtRegion・OnCancelRegionReachedイベント |
| `Scripts/Character/InteractionController.cs` | キャンセルハンドリング・直接アクション実行 |

### バインディング設定（InteractionController）

```csharp
// インタラクション開始時にBlendPivotをバインド
private void SetupBlendTracks(TimelineAsset timeline, InteractionRequest request)
{
    var director = animationController.director;
    Vector3 targetPos = request.targetPoint.position;
    Quaternion targetRot = request.targetPoint.rotation;

    foreach (var track in timeline.GetOutputTracks())
    {
        if (track is PositionBlendTrack posTrack)
        {
            posTrack.targetPosition = targetPos;
            posTrack.hasTarget = true;
            director.SetGenericBinding(posTrack, blendPivot);
        }
        else if (track is RotationBlendTrack rotTrack)
        {
            rotTrack.targetRotation = targetRot;
            rotTrack.hasTarget = true;
            director.SetGenericBinding(rotTrack, blendPivot);
        }
    }
}
```

### ColliderControlTrack

インタラクション中の当たり判定制御。

```csharp
public class ColliderControlBehaviour : PlayableBehaviour
{
    public Collider targetCollider;  // 家具のコライダー
    public bool disableCollider = true;

    public override void OnBehaviourPlay(...)
    {
        if (targetCollider != null)
            targetCollider.enabled = !disableCollider;
    }

    public override void OnBehaviourPause(...)
    {
        if (targetCollider != null)
            targetCollider.enabled = true;
    }
}
```

### LookAtTrack

Timeline駆動の視線制御。Clipが存在する期間のみ Eye / Head / Chest の LookAt を有効化。

#### LookAtClip パラメータ

| フィールド | 型 | デフォルト | 説明 |
|-----------|-----|---------|------|
| `enableEye` | `bool` | `true` | VRM LookAt（目）の追従 |
| `enableHead` | `bool` | `true` | 頭ボーンの追従 |
| `headAngleLimitX` | `float` | `45` | 頭の上下制限角度 |
| `headAngleLimitY` | `float` | `45` | 頭の左右制限角度 |
| `enableChest` | `bool` | `false` | 胸ボーンの追従 |
| `chestAngleLimitX` | `float` | `10` | 胸の上下制限角度 |
| `chestAngleLimitY` | `float` | `10` | 胸の左右制限角度 |
| `blendFrames` | `int` | `60` | 有効/無効切り替え時の補間フレーム数 |

#### バインディング

```csharp
[TrackBindingType(typeof(CharacterLookAtController))]
public class LookAtTrack : TrackAsset
```

ランタイムバインディングは `CharacterAnimationController.BindAnimatorToTimeline()` で自動実行：

```csharp
else if (track is LookAtTrack lookAtTrack)
{
    director.SetGenericBinding(lookAtTrack, lookAtController);
}
else if (track is LightControlTrack lightControlTrack)
{
    director.SetGenericBinding(lightControlTrack, roomLightController);
}
```

#### データフロー

```
LookAtMixerBehaviour.ProcessFrame()
  ↓ SetTimelineLookAtState(settings)
CharacterLookAtController
  ├─ Update(): Pre-Update Restore + ターゲット位置ブレンド
  └─ LateUpdate(): _effectiveWeight 補間 → Eye/Head/Chest 適用
```

#### Clip非存在時の動作

- 同一Timeline内でClip終了 → Mixerが `active=false` を送信 → `_targetWeight=0` → blendFramesかけてフェードアウト
- LookAtTrackが無いTimelineに切り替え → フレームカウントで未更新検出 → `_targetWeight=0` → フェードアウト

#### 関連ファイル

| ファイル | 役割 |
|---------|------|
| `Scripts/Timeline/LookAtClip.cs` | PlayableAsset（Inspectorパラメータ） |
| `Scripts/Timeline/LookAtTrack.cs` | TrackAsset + LookAtBehaviour + LookAtMixerBehaviour |

### EmotePlayableTrack

emote（感情モーション）再生可能な期間を定義するメタデータTrack。
Clip自体はアニメーション処理を行わず、CharacterAnimationControllerがClip存在期間を参照して
emote再生可否を判定する。

#### 用途

- Timeline上にClipを配置すると、その期間中は外部からのemoteリクエストに応答可能
- Clip非存在期間のemoteリクエストは無視される
- `additiveBones` にボーンを指定すると、指定ボーンのみEmoteで上書きし、それ以外はベースポーズを維持

#### Inspector

```
┌─────────────────────────────────────────┐
│ EmotePlayableClip                       │
│                                         │
│ [Template]                              │
│ [全て][指除外][上半身][下半身][クリア]    │
│                                         │
│ Additive Bones:                         │
│   [0] Spine             [x]            │
│   [1] Chest             [x]            │
│   [+] Add                               │
└─────────────────────────────────────────┘
```

#### additiveBones の動作

| additiveBones | 動作 |
|---|---|
| 空（デフォルト） | 全身をEmoteアニメーションで置き換え（従来動作） |
| 非空（例: 上半身） | AdditiveOverrideHelperが起動。指定ボーンのみEmoteで上書き、それ以外はスナップショット維持 |

配置例:
- `common_idle01` の lp 区間に配置（additiveBones 空） → Idle中に全身emote再生
- `interact_sit01` の lp 区間に配置（additiveBones=上半身） → 座り中に上半身のみemote再生

#### emote再生フロー

```
1. LLMレスポンスの emote フィールドを受信（"happy01", "relaxed01" 等）
2. Thinking再生中（ed含む）の場合 → コルーチンでThinking完了まで待機
3. Walking/Running中の場合 → PlayEmoteAfterWalkコルーチンで移動完了後に再生
   （詳細は「Emote Queuing After Walk」セクション参照）
4. それ以外: emote即時再生 + _pendingWalkEmote に保存（後続walkでの再再生用）
5. CharacterAnimationController.CanPlayEmote()
   └→ 現在のTimelineにEmotePlayableTrackが存在し、
      director.time がいずれかのClip範囲内にあるか確認
6. CanPlayEmote() == true
   └→ PlayEmoteWithReturn(emoteType)
       ├→ EmotePlayableClipのadditiveBones取得
       ├→ additiveBones非空 → AdditiveOverrideHelper.StartOverride()
       ├→ 現在のstate/animationIdを保存
       ├→ PlayState(Emote, emoteType)
       └→ 復帰イベント登録（LoopRegion有無で分岐）
           ├→ LoopRegionあり → OnEndPhaseComplete に OnEmoteEndPhaseComplete を登録
           └→ LoopRegionなし → director.stopped に OnEmoteTimelineStopped を登録
7. emoteループ中（LoopRegionがある場合）
   ├→ テキスト表示完了: NotifyTextDisplayComplete() → emoteホールドタイマー開始
   ├→ emoteHoldDuration(デフォルト5秒) 経過 → RequestEndPhase()
   └→ ed（終了）セクションを再生
8a. LoopRegionあり: ed再生完了 → CompleteEndPhase() → OnEndPhaseComplete
   └→ OnEmoteEndPhaseComplete()
       ├→ StopAdditiveOverrideWithSnapshot()（AO補正込みポーズ保存→AO停止）
       └→ 保存したstate/animationIdで PlayState(resumeAtLoop: true) → 元のモーションに復帰
8b. LoopRegionなし: Timeline再生完了 → director.stopped
   └→ OnEmoteTimelineStopped()
       ├→ StopAdditiveOverrideWithSnapshot()（AO補正込みポーズ保存→AO停止）
       └→ 保存したstate/animationIdで PlayState(resumeAtLoop: true) → 元のモーションに復帰
※ LoopRegionなしのemoteは一発再生で自然に完了する（ホールドタイマー不要）
※ 次の応答で新しいemoteが届けば、ホールドタイマーに関わらず即座に切り替わる
```

#### Emote中にThinkingが必要な場合

Emote再生中にThinkingリクエストが来た場合、EmoteをキャンセルしてThinkingに遷移する。
Thinkingの復帰先はEmoteの復帰先を継承する（復帰先チェーン）。

```
Emote再生中 → PlayThinkingWithReturn()
  ├→ director.stopped -= OnEmoteTimelineStopped（Emoteコールバック解除）
  ├→ AdditiveOverrideHelper.StopOverride()
  ├→ _thinkingReturnState = _emoteReturnState（復帰先継承）
  └→ PlayState(Thinking, thinkingAnimId)
```

#### 関連ファイル

| ファイル | 役割 |
|---------|------|
| `Scripts/Timeline/EmotePlayableClip.cs` | PlayableAsset（additiveBones フィールド） |
| `Scripts/Timeline/EmotePlayableTrack.cs` | TrackAsset + EmotePlayableBehaviour（メタデータ専用） |
| `Scripts/Timeline/AdditiveOverrideHelper.cs` | 加算ボーンオーバーライド実行 |
| `Scripts/Character/CharacterAnimationController.cs` | CanPlayEmote() / PlayEmoteWithReturn() / ForceStopEmote() / ForceStopEmoteToEnd() |
| `Editor/EmotePlayableClipEditor.cs` | テンプレートボタンUI |

---

### ThinkingPlayableTrack

Thinking（考え中モーション）再生可能な期間を定義するメタデータTrack。
EmotePlayableTrackと同構造。CharacterAnimationControllerがClip存在期間を参照してThinking再生可否を判定する。

#### 用途

- Timeline上にClipを配置すると、その期間中はThinkingアニメーションを再生可能
- Clip非存在期間のThinkingリクエストは無視される（静かにスキップ）
- `additiveBones` にボーンを指定すると、指定ボーンのみThinkingで上書き

#### Thinking ライフサイクル（Emoteとの違い）

| | Emote | Thinking |
|---|---|---|
| 再生方式 | LoopRegionあり: st→lp→ed（ホールド→自動ed遷移） / なし: ワンショット | LoopRegion（st→lp→ed） → RequestEndPhase で手動停止 |
| 開始トリガー | LLMレスポンスの emote フィールド | LLMリクエスト送信時（ChatManager経由） |
| 終了トリガー | LoopRegionあり: ホールドタイマー→RequestEndPhase→CompleteEndPhase / なし: director.stopped | LLMレスポンス到着 → StopThinkingAndReturn → RequestEndPhase |
| 復帰 | LoopRegionあり: OnEmoteEndPhaseComplete（OnEndPhaseComplete経由） / なし: OnEmoteTimelineStopped（director.stopped経由） | OnThinkingEndPhaseComplete（OnEndPhaseComplete経由） |
| CanPlay判定 | EmotePlayableClip有無 | ThinkingPlayableClip有無 |

#### Thinking再生フロー

```
1. LLMリクエスト送信 → ChatManager → TalkController.StartThinking()
2. CharacterAnimationController.CanPlayThinking()
   └→ 現在のTimelineにThinkingPlayableTrackが存在し、
      director.time がいずれかのClip範囲内にあるか確認
3. CanPlayThinking() == true
   └→ PlayThinkingWithReturn(thinkingAnimId)
       ├→ Emote再生中ならキャンセル（復帰先チェーン継承）
       ├→ ThinkingPlayableClipのadditiveBones取得
       ├→ additiveBones非空 → AdditiveOverrideHelper.StartOverride()
       ├→ 現在のstate/animationIdを保存
       ├→ PlayState(Thinking, thinkingAnimId)
       └→ OnEndPhaseComplete に復帰コールバックを登録
4. LLMレスポンス到着 → StopThinkingAndReturn()
   └→ RequestEndPhase() → ed再生
5. ed再生完了 → OnThinkingEndPhaseComplete()
   ├→ StopAdditiveOverrideWithSnapshot()（AO補正込みポーズ保存→AO停止）
   └→ PlayState(returnState, returnAnimId, resumeAtLoop: true)
```

##### ForceStopThinking の Interact復帰時の特殊処理

`StopThinkingAndReturn()`（graceful）→ `ForceStopThinking()` が連続で呼ばれた場合
（例: emotionフィールド到着→StopThinking、その後emoteフィールド到着→ForceStopThinking）、
`_isEnding`がtrue（Thinking edフェーズ進行中）のままForceStopThinkingが実行される。

通常の復帰ロジック: `_isEnding == true` → `PlayState(returnState, resumeAtEnd: true)` で
復帰先の_edに直接ジャンプ（2段階IB回避）。

**Interact復帰時の問題:** interact_edは「立ち上がり」アニメーションであり、
`InteractionController.ExitLoopWithCallback()`を経由せず再生されると、
`OnEndPhaseComplete`のハンドラが状態ガード（`_currentState != Ending`）で弾かれ、
Idle復帰もpendingAction実行も行われずモーションが停止する。

**対策:** `ForceStopThinking`で`_isEnding == true`かつ復帰先がInteract状態の場合、
`resumeAtEnd`ではなく`resumeAtLoop`を使用してインタラクトループに復帰する。
interact_edの再生はInteractionControllerの`ExitLoopWithCallback()`で制御する。

```
ForceStopThinking() && _isEnding == true:
  ├─ returnState == Interact → resumeAtLoop（ループに復帰、edはInteractionControllerが制御）
  ├─ returnState == Talk     → resumeAtLoop（talk_idle等のループ待機に復帰）
  └─ returnState == other    → resumeAtEnd（復帰先の_edに直接ジャンプ、2段階IB回避）
```

> **Note:** `ForceStopEmote()`にも同様の`_isEnding`→`resumeAtEnd`パターンがある。
> 現時点ではEmote中にInteract復帰が発生するシナリオは報告されていないが、
> 同じ問題が発生した場合は同様の対策が必要。

#### 関連ファイル

| ファイル | 役割 |
|---------|------|
| `Scripts/Timeline/ThinkingPlayableClip.cs` | PlayableAsset（additiveBones フィールド） |
| `Scripts/Timeline/ThinkingPlayableTrack.cs` | TrackAsset + ThinkingPlayableBehaviour（メタデータ専用） |
| `Scripts/Timeline/AdditiveOverrideHelper.cs` | 加算ボーンオーバーライド実行 |
| `Scripts/Character/CharacterAnimationController.cs` | CanPlayThinking() / PlayThinkingWithReturn() / StopThinkingAndReturn() / ForceStopThinking() / ForceStopThinkingToEnd() |
| `Editor/ThinkingPlayableClipEditor.cs` | テンプレートボタンUI |

---

### VrmExpressionTrack

Timeline上でVRM Expression（表情）のweightをカーブ制御するためのカスタムトラック。
1クリップにつき1つのExpressionを制御する。

#### バインディング

`CharacterExpressionController` にバインドし、`GetVrmInstance()` 経由で VRM Expression API にアクセスする。
`CharacterAnimationController.BindAnimatorToTimeline()` 内で自動バインド。

#### クリップ構成

```
VrmExpressionClip
├── expressionPreset    : ExpressionPreset (blink, happy, angry, etc.)
├── customExpressionName: string (preset が custom の場合)
├── sourceClip          : AnimationClip (任意、カーブソース)
├── sourceCurveProperty : string (sourceClip 内の BlendShape カーブパス)
├── bakeScale           : float (デフォルト 0.01、Blender 0-100 → VRM 0-1 変換用)
└── curve               : AnimationCurve (X: 正規化時間 0-1, Y: weight 0-1)
```

#### カーブ Bake フロー

```
1. Inspector で sourceClip（FBXアニメーション）を設定
2. BlendShape カーブ一覧がドロップダウンで表示される
3. bakeScale を確認（デフォルト 0.01 = Blender の 0-100 → VRM の 0-1）
4. 「Bake Curve from Source」ボタン押下
5. AnimationUtility.GetEditorCurve() でカーブを抽出
6. 時間軸を 0-1 に正規化、値に bakeScale を適用して curve フィールドに格納
   └─ タンジェント値も duration と bakeScale に応じてスケーリング
```

#### 一括 Rebake

メニュー: **CyanNook > Animation > Rebake All VRM Expression Curves**

```
1. プロジェクト内の全 TimelineAsset を検索
2. 各 Timeline の VrmExpressionTrack 内のクリップを走査
3. sourceClip + sourceCurveProperty が設定済みのクリップのみ Bake 実行
4. sourceClip 未設定のクリップはスキップ（手動カーブ編集用）
5. 完了後にダイアログで結果表示（検出数/成功数/スキップ数/エラー数）
```

#### ランタイム処理（MixerBehaviour.ProcessFrame）

```
1. playerData から CharacterExpressionController を取得
2. ResetExpressionsForTimelineFrame() で全Expressionをゼロリセット（フレームガード: 1回/フレーム）
3. 各入力クリップについて:
   a. クリップの weight を取得（Timeline ブレンド対応）
   b. ExpressionPreset.neutral → スキップ（リセット済み = ニュートラル状態維持）
   c. 正規化時間 = localTime / duration
   d. curveValue = curve.Evaluate(normalizedTime)
   e. AddTimelineExpression(key, curveValue * weight * trackBlendWeight) → 加算適用
```

#### sourceClip 変更時の警告

sourceClip が更新されて選択中の BlendShape カーブが消失した場合、
Inspector に警告メッセージを表示し、そのExpressionの制御はスキップされる。
ユーザーは別のカーブを選択して再Bakeすることで対応する。

#### 関連ファイル

| ファイル | 役割 |
|---------|------|
| `Scripts/Timeline/VrmExpressionTrack.cs` | TrackAsset + VrmExpressionBehaviour + VrmExpressionMixerBehaviour |
| `Scripts/Timeline/VrmExpressionClip.cs` | PlayableAsset（Expression + カーブデータ保持） |
| `Editor/VrmExpressionClipEditor.cs` | カーブBake UI、BlendShapeドロップダウン、警告表示、一括Rebakeメニューコマンド |
| `Scripts/Character/CharacterExpressionController.cs` | バインディング対象（GetVrmInstance() を公開） |
| `Scripts/Character/CharacterAnimationController.cs` | BindAnimatorToTimeline() 内で VrmExpressionTrack を自動バインド |

---

### AdditiveOverrideHelper

Emote/Thinking の加算ボーンオーバーライドを実行するコンポーネント。
VRMインスタンスにアタッチされ、指定ボーン以外のポーズをスナップショットから毎フレーム復元する。

#### 仕組み（Bone Snapshot + LateUpdate Restore）

```
1. StartOverride(additiveBones) 呼び出し
   ├→ 全ボーンの localPosition/localRotation をスナップショット
   └→ additiveBones 以外のボーンを復元対象として記録

2. 毎フレーム LateUpdate [ExecutionOrder 20050]
   └→ 復元対象ボーンの localPosition/localRotation をスナップショット値に書き戻し

3. StopOverride() 呼び出し
   └→ 復元処理を停止
```

→ 結果: additiveBones（上半身等）のみ Emote/Thinking ポーズ、それ以外は元のポーズを維持

**重要: StopOverride()呼び出し前のスナップショット**

StopOverride→PlayState実行時、InertialBlendHelperの`_lastCleanPose`にはAO補正が含まれない
（IB: 20000, AO: 20050のExecutionOrder差による）。そのままPlayStateすると、IBがAO補正なしの
ポーズ（例: 立ちポーズ）からブレンドを開始し、ポーズフラッシュが発生する。
CharacterAnimationControllerの`StopAdditiveOverrideWithSnapshot()`ヘルパーを使用すること（詳細は問題9を参照）。

**制約**: 非加算ボーンはスナップショット時点で静止する。座りループ等の微細な動きは失われるが、
実用上問題ない（座り下半身はほぼ静的）。

#### 関連ファイル

| ファイル | 役割 |
|---------|------|
| `Scripts/Timeline/AdditiveOverrideHelper.cs` | MonoBehaviour（ExecutionOrder 20050） |
| `Scripts/Character/VrmLoader.cs` | VRMインスタンスへの自動アタッチ |

---

### MoveSpeedTrack

NavMeshAgentの移動速度をTimeline上のAnimationCurveで制御するカスタムトラック。
Walk/Run Timelineのst区間に配置して、歩き始めの徐々に加速する表現などに使用する。

#### バインディング

`CharacterNavigationController` にバインドし、`SetMoveSpeedMultiplier()` で速度乗算値とアニメーション速度調整フラグを設定する。
`CharacterAnimationController.BindAnimatorToTimeline()` 内で自動バインド。

#### クリップ構成

```
MoveSpeedClip
├── speedCurve           : AnimationCurve (X: 正規化時間 0-1, Y: 速度乗算値)
│   デフォルト: Linear(0, 0.1, 1, 1.0) ← 開始0.1倍速 → 終了1.0倍速
└── adjustAnimatorSpeed  : bool (デフォルト: true)
    true:  AdjustAnimatorSpeed()でAgent速度にアニメーション追従（足滑り防止）
    false: アニメーション定速再生（歩き開始モーション等）
```

#### ランタイム処理（MoveSpeedBehaviour.ProcessFrame）

```
1. playerData から CharacterNavigationController を取得
2. playable.GetTime() / GetDuration() で正規化時間を算出
3. speedCurve.Evaluate(normalizedTime) で乗算値を取得
4. navController.SetMoveSpeedMultiplier(multiplier, adjustAnimatorSpeed) を呼び出し
```

NavigationController側:
```
UpdateMoving() / UpdateApproachingInteraction():
  agent.speed = baseSpeed * _moveSpeedMultiplier
  if (_adjustAnimatorSpeedEnabled)
    AdjustAnimatorSpeed();    // Agent速度にアニメーション追従
  else
    ResetAnimatorSpeed();     // グラフ速度を1.0にリセット（定速再生を保証）
```

#### リセットタイミング

- クリップ終了時: `OnBehaviourPause` で乗算値=1.0, 速度調整=true にリセット
- 移動終了時: `StopMoving()` / `HandleNavigationFailure()` でリセット

#### 関連ファイル

| ファイル | 役割 |
|---------|------|
| `Scripts/Timeline/MoveSpeedTrack.cs` | TrackAsset + MoveSpeedBehaviour + MoveSpeedMixerBehaviour |
| `Scripts/Timeline/MoveSpeedClip.cs` | PlayableAsset（speedCurve + adjustAnimatorSpeed） |
| `Scripts/Character/CharacterNavigationController.cs` | バインディング対象（SetMoveSpeedMultiplier を公開） |
| `Scripts/Character/CharacterAnimationController.cs` | BindAnimatorToTimeline() 内で MoveSpeedTrack を自動バインド |

---

### LightControlTrack（Timeline駆動ライト制御）

RoomLightControllerのON/OFFをTimelineクリップで制御する。sleep/exit/entryアニメーションにクリップを配置してライトの点灯・消灯タイミングをアニメーションと同期。

#### 設計ポイント

- クリップ終了後も状態維持（復元しない）。ONに戻すには別のONクリップで明示的に行う
- **ファイル分割必須**: PlayableAssetクラスは独立した.csファイルに配置しないと、WebGLビルドでスクリプト参照が解決されずクリップが無効化される

#### ファイル構成

| ファイル | 役割 |
|---------|------|
| `Scripts/Timeline/LightControlTrack.cs` | TrackAsset（RoomLightControllerバインディング） |
| `Scripts/Timeline/LightControlClip.cs` | PlayableAsset（lightsOnパラメータ） |
| `Scripts/Timeline/LightControlBehaviour.cs` | LightControlBehaviour + LightControlMixerBehaviour |
| `Scripts/Character/CharacterAnimationController.cs` | BindAnimatorToTimeline() 内で LightControlTrack を自動バインド |
| `Scripts/Furniture/RoomLightController.cs` | バインディング対象（SetLights(bool) を公開） |

---

### Emote Queuing After Walk

移動中またはこれから移動開始するタイミングでEmoteが要求された場合、移動完了後にEmoteを再生するキューイング機能。
JSONフィールドの到着順序に依存しない設計。

#### 動作フロー

```
ProcessEmote(emote):
  ├─ Thinking中 → PlayEmoteAfterThinking コルーチン
  ├─ Walking/Running中 → PlayEmoteAfterWalk コルーチン（直接起動）
  └─ それ以外 → PlayEmoteIfPossible（即時再生）+ _pendingWalkEmote に保存

ProcessMoveAction → SetState(Walking) → DeferPendingWalkEmote():
  └─ _pendingWalkEmote が存在 → PlayEmoteAfterWalk コルーチン起動 + クリア

HandleChatResponse（レスポンス境界）:
  └─ _pendingWalkEmote = null（古いemoteが次のwalkで誤再生されないように）
```

#### PlayEmoteAfterWalk コルーチン

```
1. navigationController.CurrentState が Idle になるまで待機
   ※ CharacterStateではなくNavigationStateで判定
   （Talkターゲット時にCharacterStateがWalkingのまま変わらない問題を回避）
2. CanPlayEmote() が true になるまでポーリング（最大3秒）
   ※ Idle/TalkIdleタイムラインのEmotePlayableClipが再生位置に到達するまで待つ
   2a. 新しいナビゲーションが開始された場合はキャンセル
   2b. Walk終了フェーズ（walk_ed）再生中の場合 → ReturnToIdle()でスキップしてIdle遷移
       ※ emoteが予約されている場合はwalk_edを待たずに即座に遷移する
3. タイムアウト時はログ出力してスキップ
```

#### 対応パターン

| パターン | 動作 |
|---------|------|
| ブロッキング: action→emote順 | ProcessAction→Walking → ProcessEmote→Walking検出→コルーチン直接起動 |
| ストリーミング: emoteが先に到着 | ProcessEmote→即時再生+保存 → walk開始→DeferPendingWalkEmote→コルーチン起動 |
| ストリーミング: walkが先に開始 | walk開始→保存なし → ProcessEmote→Walking検出→コルーチン直接起動 |
| ストリーミング: emoteが移動中に到着 | ProcessEmote→Walking検出→コルーチン直接起動 |
| 移動なしのemote | 従来通り即時再生（_pendingWalkEmoteは次のレスポンス境界でクリア） |

---

## Timeline制御システム（クリップベース）

ループ領域とキャンセル可能領域をすべてクリップで定義。
従来のSignal Marker（LoopStartSignal / LoopEndSignal / EndStartSignal / CanCancelSignal / InteractionSignalReceiver）は削除済み。

### 制御の責務

| 機能 | 担当 | データソース |
|------|------|---|
| ループ制御 | CharacterAnimationController | LoopRegionTrack |
| キャンセル判定 | CharacterAnimationController | ActionCancelTrack |
| ワンショット完了検知 | CharacterAnimationController | InteractionEndTrack |
| インタラクション状態管理 | InteractionController | CharacterAnimationControllerのイベント |
| ナビゲーション状態管理 | NavigationController | CharacterAnimationControllerのイベント |
| 移動速度カーブ制御 | MoveSpeedTrack (ProcessFrame) | MoveSpeedClip の speedCurve → NavigationController.SetMoveSpeedMultiplier |

### CharacterAnimationController のTimeline制御API

```csharp
// ループ・キャンセル状態
public bool HasLoopRegion { get; }     // LoopRegionTrackが存在するか
public bool IsInLoop { get; }          // 現在ループ中か
public bool CanCancel { get; }         // 現在キャンセル可能か
public bool HasCancelRegions { get; }  // ActionCancelTrackのクリップが存在するか

// 終了フェーズリクエスト
public void RequestEndPhase();
// → LoopEnd到達時にEndStartへジャンプし、ed再生後にOnEndPhaseCompleteを発火

// CancelRegion到達でのキャンセル（ed再生スキップ用）
public void RequestCancelAtRegion();
// → 既にCancelRegion内なら即座にOnCancelRegionReachedを発火
// → CancelRegion外なら_shouldCancelAtRegionフラグをセットし、到達時にUpdate()から発火

// Emote制御
public bool CanPlayEmote();                          // EmotePlayableClip存在判定
public void PlayEmoteWithReturn(string emoteType);   // Emote再生（復帰先保存、加算対応）
public void ForceStopEmote();                        // Emote強制停止→復帰
public void ForceStopEmoteToEnd();                   // Emote強制停止→ed再生→復帰

// テキスト表示完了連動
public void OnResponseStarted();                     // 応答開始（emoteホールドタイマー停止）
public void NotifyTextDisplayComplete();             // テキスト表示完了（emoteホールドタイマー開始）

// Thinking制御
public bool CanPlayThinking();                       // ThinkingPlayableClip存在判定
public void PlayThinkingWithReturn(string thinkingAnimId); // Thinking再生（復帰先保存、加算対応、Emoteキャンセル）
public void StopThinkingAndReturn();                 // Thinking停止（ed再生→復帰）
public void ForceStopThinking();                     // Thinking強制停止→復帰（Interact/Talk復帰時はresumeAtLoop）
public void ForceStopThinkingToEnd();                // Thinking強制停止→ed再生→復帰
public bool IsThinkingActive { get; }                // Thinking再生中か

// イベント
public event Action OnLoopEntered;          // LoopStart到達時
public event Action OnEndPhaseComplete;     // ed再生完了時
public event Action OnInteractionEndReached;// InteractionEndClip到達時（LoopRegionなしInteractの完了通知）
public event Action OnCancelRegionReached;  // CancelRegion到達時（edスキップ用）
public event Action<TimelineAsset> OnCancelExecuted;  // キャンセル実行時（ExecuteCancel用、未使用）

// 遷移先リスト取得
public List<TimelineAsset> GetAllowedTransitions();

// 状態遷移
public void PlayState(AnimationStateType state, string animationVariant = null, bool resumeAtLoop = false, bool resumeAtEnd = false, bool skipBlend = false);
// → resumeAtLoop: trueの場合、LoopRegion開始位置から再開（st再生を回避）
// → resumeAtEnd: trueの場合、LoopRegionのed開始位置から再開（_isEnding中のForceStop復帰用。ただしInteract復帰時は使用不可→resumeAtLoop）
// → skipBlend: trueの場合、InertialBlend全体をスキップ（InertialBlendTrack・フォールバック両方）。VRM初回読み込み時のT-pose補間回避に使用

// 現在のTimeline時刻でポーズを即時評価（Evaluate()ラッパー）
public void EvaluateImmediate();
```

### InteractionController の利用

```csharp
// ExitLoop → RequestEndPhase に委譲
public void ExitLoop()
{
    ExitLoopWithCallback(null);
}

// ExitLoopWithCallback → ed再生後にコールバック実行
// Thinking/Emote再生中はForceStopしてからed再生
// CancelRegionがある場合はRequestCancelAtRegionも設定（skipCancelRegion=falseの場合）
public void ExitLoopWithCallback(Action onComplete, bool skipCancelRegion = false)
{
    _pendingAction = onComplete;
    // ForceStopThinking/ForceStopEmote → RequestEndPhase
    // skipCancelRegion == false && HasCancelRegions == true の場合: RequestCancelAtRegion() を併用
    // skipCancelRegion == true の場合: CancelRegionを無視しed全体を再生（Sleep起床用）
    // Interact状態に復帰していない場合は即座にOnInteractionComplete
}

// OnEndPhaseComplete → インタラクション終了処理（通常ed完了パス）
animationController.OnEndPhaseComplete += OnInteractionComplete;

// OnInteractionEndReached → LoopRegionなしInteractの完了検知（exit等）
animationController.OnInteractionEndReached += OnAnimInteractionEndReached;

// OnCancelRegionReached → edスキップで直接次アクション実行
animationController.OnCancelRegionReached += OnAnimCancelRegionReached;
// OnAnimCancelRegionReached:
//   Stop() → 家具解放 → BlendPivotリセット → pendingAction直接実行
//   ※Idle中間遷移なし（IBがインタラクトポーズからブレンドするため）
```

### 状態遷移パターン

| 現在状態 | 操作 | 次状態 | 遷移方法 |
|----------|------|--------|----------|
| InLoop | RequestEndPhase | Ending→None | ed再生 → OnEndPhaseComplete |
| InLoop(CancelRegionあり) | ExitLoopWithCallback | Ending→None | ed再生 → CancelRegion到達 → OnCancelRegionReached → 直接callback実行（Idle経由なし） |
| InLoop(CancelRegionあり) | ExitLoopWithCallback(skipCancelRegion: true) | Ending→None | ed全体を再生（CancelRegion無視） → OnEndPhaseComplete → callback実行。Sleep起床用 |
| InLoop | ExecuteCancel | None | 即時遷移先再生（未使用） |
| Walk lp中 | StopWalkWithEndPhase（到着） | → walk_ed → Idle | ed再生 → OnWalkEndPhaseComplete → ReturnToIdle |
| Walk lp中 | StopWalkWithEndPhase + Talk/emote | → 即時遷移（walk_edスキップ） | PlayState/ReturnToIdleがwalk_edを中断 |
| Walk lp中 | StopMoving/CancelMovement | → Idle（walk_edなし） | 強制停止、ReturnToIdle()直接呼び出し |
| Walk lp中 | ExecuteCancel | → 指定Timeline | 即時遷移 |
| Interact + Emote/Thinking | ExitLoopWithCallback | → ForceStop → ed → callback | Thinking/Emote停止→Interact復帰→ed再生→コールバック |
| Emote lp中 | emoteHoldDuration経過 | → emote_ed → 復帰 | テキスト表示完了後に自動RequestEndPhase |
| Emote再生中 | PlayThinkingWithReturn | → Thinking | Emoteキャンセル→Thinking再生（復帰先継承） |
| Thinking再生中 | StopThinkingAndReturn | → thinking_ed → 復帰 | ed再生 → OnThinkingEndPhaseComplete |

---

## Folder Structure

```
Assets/
├── Scripts/
│   ├── Core/           - 基盤クラス、インターフェース、属性定義
│   │   ├── SettingsExporter.cs          ← 全設定JSON形式Import/Export
│   │   └── FrameRateLimiter.cs          ← フレームレート制限（Inspector設定可能）
│   ├── Character/      - キャラクター制御
│   │   ├── CharacterAnimationController.cs
│   │   ├── CharacterCameraController.cs  ← Vision カメラ（Headボーン追従・画像キャプチャ）
│   │   ├── CharacterFaceLightController.cs ← 顔ライト（Headボーン追従）
│   │   ├── CharacterExpressionController.cs  ← Facial Timeline駆動 / 直接制御フォールバック
│   │   ├── FacialTimelineData.cs             ← EmotionType→TimelineAssetマッピング + ブレンドペアバインディング(ScriptableObject)
│   │   ├── CharacterLookAtController.cs
│   │   ├── CharacterNavigationController.cs
│   │   ├── InteractionController.cs
│   │   ├── TalkController.cs           ← Talk モード状態管理
│   │   ├── LipSyncController.cs           ← リップシンク統合（TextOnly/Mora/Simulated/Amplitude 4モード）
│   │   ├── CharacterController.cs      ← LLMレスポンスのaction/target/emote/口パクルーティング
│   │   ├── DynamicTargetController.cs  ← 動的ターゲット（clock/distance/height）
│   │   ├── RoomTargetManager.cs       ← 名前付きターゲット管理（mirror, window等）
│   │   ├── SleepController.cs        ← 睡眠状態管理・夢タイマー・起床処理・PlayerPrefs永続化
│   │   ├── CharacterSetup.cs         ← VRM読み込み・全コンポーネント初期化・接続・Rendering Layer Mask/Culling Layer設定
│   │   └── VrmLoader.cs
│   ├── Camera/         - カメラ制御 (namespace: CyanNook.CameraControl)
│   │   └── DynamicCameraController.cs  ← MainCamera動的制御（FOV距離連動、Y軸ルックアット）
│   ├── Chat/           - LLM通信、JSONパース、ストリーミング
│   │   ├── LLMClient.cs                ← LLM通信統合（ブロッキング/ストリーミング両対応）
│   │   ├── ILLMProvider.cs             ← プロバイダーIF（SendRequest/SendStreamingRequest）
│   │   ├── OllamaProvider.cs           ← Ollama（NDJSONストリーミング対応）
│   │   ├── LMStudioProvider.cs        ← LM Studio（OpenAI互換API、SSEストリーミング対応）
│   │   ├── DifyProvider.cs             ← Dify Chat Messages API（SSEストリーミング対応）
│   │   ├── OpenAIProvider.cs           ← OpenAI API（Bearer認証 + Chat Completions API）
│   │   ├── ClaudeProvider.cs           ← Anthropic Claude API（x-api-key認証 + Messages API）
│   │   ├── GeminiProvider.cs          ← Google Gemini API（x-goog-api-key認証 + Generative Language API）
│   │   ├── WebLLMProvider.cs           ← WebLLM（ブラウザ内LLM via WebGPU）
│   │   ├── WebLLMBridge.cs             ← WebLLM jslib C#ブリッジ（Singleton MonoBehaviour）
│   │   ├── LlmStreamHandler.cs         ← DownloadHandlerScript + StreamSeparatorProcessor
│   │   ├── IncrementalJsonFieldParser.cs ← ストリーミングJSON逐次フィールドパーサー
│   │   ├── LlmResponseHeader.cs        ← ストリーミングヘッダーデータクラス
│   │   ├── ChatManager.cs              ← プロンプト生成、会話履歴、ストリーミング対応
│   │   ├── SpatialContextProvider.cs   ← 空間認識JSON生成（NavMesh/家具/ターゲット）
│   │   └── LLMConfigManager.cs         ← API設定保存（PlayerPrefs）
│   ├── Furniture/      - 家具システム + 部屋環境制御
│   │   └── RoomLightController.cs    ← ライトON/OFF + Emission + Lightmap連動（初回起動・Sleep等で共通利用）
│   ├── Timeline/       - カスタムTimelineトラック、ループ制御、キャンセル制御、慣性補間、emote/thinking再生判定、加算オーバーライド、移動速度カーブ制御
│   ├── DebugTools/     - デバッグ用コンポーネント
│   │   └── DebugKeyController.cs       ← デバッグキー一括管理
│   ├── UI/             - UI関連
│   │   ├── UIController.cs             ← チャット入出力・ストリーミング表示
│   │   ├── SettingsMenuController.cs   ← アイコンメニューバー・パネル開閉
│   │   ├── AvatarSettingsPanel.cs      ← アバター設定パネル
│   │   ├── LLMSettingsPanel.cs         ← LLM設定パネル
│   │   ├── VoiceSettingsPanel.cs       ← 音声設定パネル（TTS/STT設定）
│   │   ├── DebugSettingsPanel.cs       ← デバッグ設定パネル（設定Import/Export含む）
│   │   ├── FirstRunController.cs      ← 初回起動ポップアップ・WebLLMダウンロード進捗
│   │   ├── MultiLineInputFieldFix.cs  ← マルチラインInputField改行修正
│   │   └── StatusOverlay.cs           ← ステータスオーバーレイ（FPS・ステート・Timeline・メモリ）
│   ├── Utilities/      - ユーティリティ
│   └── CyanNook.asmdef - アセンブリ定義 (Unity.Timeline参照)
├── Resources/
│   └── LicenseText.txt               ← ライセンス表示用テキスト（TextAsset）
├── ScriptableObjects/
│   ├── Furniture/      - 家具カテゴリ定義
│   └── Characters/     - キャラクターテンプレート
├── Prefabs/
│   ├── Characters/
│   ├── Furniture/
│   └── UI/
├── Animations/
│   └── chr001/                         - キャラクターごと
│       ├── FBX/                        - BlenderからのFBX
│       ├── Clips/                      - 抽出されたAnimationClip
│       ├── Timelines/                  - Timelineアセット
│       └── chr001_TimelineBindings.asset
├── StreamingAssets/
│   ├── VRM/            - VRMファイル配置
│   └── Config/         - 設定ファイル
├── Plugins/
│   └── WebGL/
│       ├── WebLLM.jslib               ← WebLLM jslibブリッジ（CDN動的読み込み、web-llm API呼び出し、XGrammar）
│       └── StatusOverlay.jslib        ← JSヒープメモリ取得（performance.memory、Chrome限定）
├── WebGLTemplates/
│   └── CyanNook/
│       └── index.html                 ← カスタムWebGLテンプレート
├── Scenes/
├── Editor/             - エディタ拡張
│   ├── AnimationClipExtractor.cs   - FBXからClip抽出
│   ├── TimelineCreator.cs          - Timeline自動生成
│   ├── VrmTestSceneSetup.cs        - テストシーン構築
│   ├── HumanBoneSelectDrawer.cs    - HumanoidボーンAdvancedDropdown選択UI
│   ├── BoneTemplateUtility.cs      - ボーンテンプレートボタン共有ユーティリティ
│   ├── InertialBlendClipEditor.cs - InertialBlendClipカスタムEditor
│   ├── EmotePlayableClipEditor.cs - EmotePlayableClipカスタムEditor
│   ├── ThinkingPlayableClipEditor.cs - ThinkingPlayableClipカスタムEditor
│   └── CyanNook.Editor.asmdef
└── DESIGN.md           - このドキュメント
```

---

## Release Phases

### Phase 1 (Initial Release)
- 1部屋のみ
- 1体型テンプレート
- 基本アニメーションセット
- ローカルLLM連携
- VRM差し替え機能

### Phase 2
- 体型バリエーション (4パターン)
- 複数部屋対応（シーン遷移）
- ~~interact + emote ブレンド~~ → Phase 1で実装済み（加算ボーンオーバーライド）

### Phase 3
- Dify連携拡張（自律思考、Workflow API）
- RAG記憶システム
- ~~TTS対応~~ → Phase 1で実装済み（VOICEVOX / Web Speech API デュアルエンジン、リップシンク4モード）
- Blenderリターゲットテンプレート配布

---

## Editor Tools

### Animation Tools (CyanNook > Animation)

| Menu | Description |
|------|-------------|
| Extract All Animation Clips | 全キャラクターのFBXからClipを抽出 |
| Extract Clips for Selected Character | 選択キャラクターのClipを抽出 |
| Create Timelines for Character | Timeline + TimelineBindingDataを生成 |
| Rebake All VRM Expression Curves | 全TimelineのVrmExpressionClipカーブを一括再Bake |

#### Clip抽出のGUID維持

FBXからAnimationClipを抽出する際、既存の.animファイルがある場合は `EditorUtility.CopySerialized()` で中身だけを上書きし、アセットのGUIDを維持する。これによりTimeline上のAnimationPlayableAssetのクリップ参照が壊れない。既存ファイルがない場合（初回抽出）は `AssetDatabase.CreateAsset()` で新規作成する。

### Scene Tools (CyanNook)

| Menu | Description |
|------|-------------|
| Setup VRM Test Scene | VRMテストシーンを自動構築 |
| Add VRM Test Components to Selected | 選択オブジェクトにテストコンポーネント追加 |

### Build Tools (CyanNook)

| Menu | Description |
|------|-------------|
| Generate StreamingAssets Manifest | StreamingAssetsのファイル一覧マニフェストを生成（WebGLビルド前に自動実行） |
| Add VRM Shaders to Always Included | VRMランタイム読み込みに必要なシェーダーをAlways Included Shadersに追加 |

---

## Dependencies

- Unity 6 LTS (6000.3.0f1)
- UniVRM 1.0
- Unity Timeline (Unity標準パッケージ)
- (Optional) UniTask - 非同期処理効率化

---

## Technical Notes

### Timeline Frame Rate

Timelineは**60fps**で動作。フレーム数から秒数への変換：
- `秒数 = フレーム数 / 60`
- 例: 120F = 2.0秒、260F = 4.33秒

コード内でフレームベースの計算を行う際は`TimelineAsset.editorSettings.frameRate`から取得。

### Root Motion設定

#### 設計方針

Root Motionは**常時有効**（`applyRootMotion = true`）。
ただし `ApplyRootMotion()` 内で状態に応じてフィルタリングし、Interact時のみ実際に適用する。

```csharp
// CharacterAnimationController
animator.applyRootMotion = true;  // 常時ON（OnAnimatorMove発火に必要）

// CharacterNavigationController.ApplyRootMotion
// フィルタリング後、Interact等でのみ適用:
// BlendPivot方式：ローカル座標でRoot Motionを適用
Transform parent = characterTransform.parent;  // BlendPivot
if (parent != null)
{
    Vector3 localDelta = Quaternion.Inverse(parent.rotation) * deltaPosition;
    characterTransform.localPosition += localDelta;
}
characterTransform.localRotation *= deltaRotation;
```

| アニメーション種別 | Rootボーンアニメ | ApplyRootMotionでの扱い |
|---|---|---|
| Idle | なし | 位置保持モードで無視（deltaPosition = 0なので実質無影響） |
| Walk/Run | あり | 無視（NavMeshAgentが位置制御、安全策でアニメ状態チェック） |
| Interact (sit01_st等) | あり | ローカル座標で適用（BlendPivot相対の微調整） |

#### ループジャンプ対策

Interact等のループ再生時、`director.time`がLoopStartにジャンプすると、
Animatorが1ループ分の大きな負のdeltaPositionを生成する。

```
対策: CharacterAnimationController._loopJumpOccurred フラグ
- ループジャンプ直前にフラグをセット
- ApplyRootMotion()でConsumeLoopJumpFlag()を呼び、フラグが立っていたらdeltaを無視
```

#### BlendPivot方式でのRoot Motion適用

インタラクション中はVRMがBlendPivotの子になっているため、Root Motionをローカル座標で適用する必要がある。

```
deltaPosition（Animatorからの出力）: ワールド座標系
↓
localDelta = Quaternion.Inverse(parent.rotation) * deltaPosition
↓
VRM.localPosition += localDelta: 親（BlendPivot）のローカル座標系
```

これにより、BlendPivotの回転に関わらず、Root Motionが正しい方向に適用される。

#### RootMotionForwarder

VRMインスタンスの`OnAnimatorMove`をNavigationControllerに転送するコンポーネント。
AnimatorがあるGameObjectに配置が必要。

```csharp
// VRMインスタンスに配置
[RequireComponent(typeof(Animator))]
public class RootMotionForwarder : MonoBehaviour
{
    public CharacterNavigationController navigationController;

    private void OnAnimatorMove()
    {
        Vector3 deltaPos = _animator.deltaPosition;
        Quaternion deltaRot = _animator.deltaRotation;
        navigationController.ApplyRootMotion(deltaPos, deltaRot);
    }
}
```

### AnimationTrack設定（インタラクション用）

複数クリップを連続再生する際の重要な設定。

#### 必須設定

| 設定項目 | 値 | 説明 |
|---------|-----|------|
| Track Offsets | **Apply Scene Offsets** | Root位置をワールド座標として扱う |
| Remove Start Offset | **OFF** | クリップ間でRoot位置を連続的に扱う |
| Root Motion Node | Root bone (hips等) | Rootボーン指定 |

#### 問題と解決策

**問題1: クリップ遷移時の位置ジャンプ**

`Remove Start Offset = ON` の場合、各クリップ開始時にRoot位置がリセットされ、
前クリップの終了位置と次クリップの開始位置でジャンプが発生。

```
sit01_st終了時: Z = 0.3m
sit01_lp開始時: Z = 0.0m (リセット)
→ 0.3mのジャンプ発生
```

**解決:** `Remove Start Offset = OFF`

**問題2: ループごとの位置ズレ**

LoopEnd位置をクリップ終了フレームと同じにすると、
クリップ境界でRoot Motion累積がリセットされる。

**解決:** LoopRegionClipの`loopEndOffsetFrames`（デフォルト: -1）で自動対策。

```
LoopRegionClip: [140F-260F]
→ LoopEnd = 260F + (-1F/60fps) = 259F 相当
→ EndStart = 260F
```

#### LoopRegionTrack 必須要件

**重要:** st/lp/edパターンを使うTimelineには、必ずLoopRegionTrackとLoopRegionClipを配置すること。

| トラック | 必須 | 説明 |
|--------|------|------|
| LoopRegionTrack + LoopRegionClip | Yes | lpクリップと同じ位置・長さ |
| ActionCancelTrack + ActionCancelClip | No | キャンセル可能にする場合のみ |

**LoopRegionTrack未設定時の動作:**
- ループ制御が無効化される
- WrapMode.Loopが設定されていなければ1回再生して終了

#### 参考資料

- [Qiita: Timelineのループ設定](https://qiita.com/MSA-i/items/40ba633466a9146f8eea)

### インタラクション終了→Idle遷移の問題と解決策

インタラクション（sit01等）終了後にIdleへ遷移する際に発生した問題と解決策。

#### 問題1: 位置リセット

**症状:** sit01_ed終了時、キャラクターがInteractionPointの位置に戻ってしまう

**原因:** `OnInteractionComplete`で`_interactionTargetPosition`（Timeline開始時の位置）を使用していた

**解決:** 現在のTransform位置を使用
```csharp
// InteractionController.OnInteractionComplete
Vector3 endPosition = _characterTransform.position;  // 現在位置を使用
Quaternion endRotation = _characterTransform.rotation;
```

#### 問題2: エディタ一時停止で早期遷移

**症状:** エディタのPauseボタンでTimelineを一時停止すると、即座にIdle遷移してしまう

**原因:** `PlayableDirector.state`がPausedになり、「再生停止」と誤検出

**解決:** エディタ一時停止を除外
```csharp
#if UNITY_EDITOR
bool isEditorPaused = UnityEditor.EditorApplication.isPaused;
#endif
if (!isPlaying && !isEditorPaused && currentTime >= endStartTime)
```

#### 問題3: 完了検出が6フレーム早い

**症状:** sit01_edが400Fまで再生されず、394F付近で遷移

**原因:** 完了判定のマージンが0.1秒（約6F）だった

**解決:** 1フレーム分のマージンに変更
```csharp
double frameMargin = 1.0 / frameRate;  // 0.1秒 → 1F
if (currentTime >= expectedEndTime - frameMargin)
```

#### 問題4: Timeline切り替え時の大きなRoot Motion delta

**症状:** Idle遷移時に位置・回転が大きくジャンプ

**原因:** TL_Idle開始時に`OnAnimatorMove`で巨大なdelta（位置-0.95m、回転192°等）が発生

**解決:** 位置保持モード中はRoot Motion適用をスキップ
```csharp
// CharacterNavigationController.ApplyRootMotion
if (animationController != null && animationController.ShouldPreservePosition)
{
    // Root Motionを適用しない
    return;
}
```

#### 問題5: 明示的に設定した保持位置が上書きされる

**症状:** `SetPreservedPosition`で設定した回転が`PlayState`内で上書きされる

**原因:** `SetPositionPreservation`が常に現在位置を記録していた

**解決:** 明示的設定フラグで上書きを防止
```csharp
// CharacterAnimationController
private bool _positionExplicitlySet;

public void SetPreservedPosition(Vector3 position, Quaternion rotation)
{
    _preservedPosition = position;
    _preservedRotation = rotation;
    _positionExplicitlySet = true;  // フラグ設定
}

private void SetPositionPreservation(AnimationStateType state)
{
    if (_positionExplicitlySet)
    {
        // 上書きしない
        _positionExplicitlySet = false;
    }
    else
    {
        // 現在位置を記録
    }
}
```

#### 問題6: Idle遷移時の微小な回転オフセット

**症状:** sit01_ed終了後、キャラクターの向きが微妙にずれる

**原因:** idle01_lpアニメーションクリップ自体に微量の回転値が含まれていた

**解決:** アニメーションクリップから不要な回転キーフレームを削除

**教訓:** Root Motion有効時は、Idleアニメーションにも意図しない回転が含まれていないか確認が必要

#### 問題7: ForceStopThinking→ed遷移時のthinkingポーズフラッシュ

**症状:** 座っている最中にLLMからアクション指示 → 立ち上がりアニメーション（ed）の前に一瞬thinkingの立ちポーズが挟まる

**原因:** `ExitLoopWithCallback`が以下を同一フレームで連続実行する:
1. `ForceStopThinking()` → `PlayState(Interact, resumeAtLoop: true)` → **IBが開始**（前のポーズ = thinking立ちポーズ）
2. `RequestEndPhase()` → `JumpToEndPhase()` → directorのtimeがed開始位置にジャンプ

IBのWaitingFirstFrameが次のLateUpdateで「前のポーズ」（thinking立ちポーズ）を1F表示してしまう。

**解決:** `JumpToEndPhase`内で動作中のIBをキャンセル
```csharp
// CharacterAnimationController.JumpToEndPhase
if (inertialBlendHelper != null && inertialBlendHelper.IsActive)
{
    inertialBlendHelper.CancelBlend();
}
```

lp→ed遷移は同一Timeline内のクリップ繋ぎなのでIBが不要。IBがIdle状態の場合はno-op。

#### 問題8: ed→Walk遷移時の座り戻し

**症状:** sit01_ed完了後にWalkに遷移する際、一瞬座り戻しのような動きが見える

**原因:** interact_edアニメーションはRoot Motionで縦移動を行うため、ボーンのローカルY座標（Hips等）が
標準の立ちポーズと大きく異なる。pendingAction（Walk）に直接遷移するとInertialBlendが大きなオフセットを
ブレンドし、座り戻しが発生する。

**解決:** `OnInteractionComplete`でpendingAction実行前にIdle経由で標準ポーズに戻す
```csharp
// InteractionController.OnInteractionComplete
if (pendingAction != null)
{
    animationController?.SetPreservedPosition(endPosition, endRotation);
    animationController?.ReturnToIdle();
    // 数フレーム遅延してpendingAction実行（Animatorが標準ポーズを評価するのを待つ）
    _pendingActionCoroutine = StartCoroutine(ExecutePendingActionDelayed(pendingAction));
}
```

PENDING_ACTION_DELAY_FRAMES = 2。Idle遷移後にAnimatorがボーンを標準ポーズにリセットし、
_lastCleanPoseが更新されてからWalk遷移のIBが走るため、オフセットが最小化される。

#### 問題9: AdditiveOverride停止時のポーズフラッシュ

**症状:** Interact中（座りモーション等）にEmote/Thinkingが加算再生され、その終了時（自然終了・強制停止両方）に一瞬立ちポーズが挟まる

**原因:** `_lastCleanPose`がAdditiveOverrideHelper(AO)の補正を含まないことに起因する。

```
実行順序:
1. IB.LateUpdate(20000) → UpdateCleanPoseCache() → _lastCleanPoseに保存（AO適用前）
2. AO.LateUpdate(20050) → 非加算ボーンをスナップショットで復元（例: 座りポーズの下半身）
3. 描画 → AO補正込みの正しいポーズが表示される

StopOverride時:
1. StopOverride() → AO停止
2. PlayState() → 新IBが開始、_lastCleanPose（AO補正なし=立ちポーズ）を「前のポーズ」として使用
3. WaitingFirstFrame → 立ちポーズを1F表示 → ポーズフラッシュ
```

**影響範囲:** Emote/Thinkingの全終了パス（自然終了3箇所 + 強制停止4箇所 = 計7箇所）

**解決:** AO停止前に`SnapshotCurrentPoseAsClean()`を呼び、AO補正込みの表示ポーズを
`_lastCleanPose`に上書きする。これにより次回IBがAO補正済みポーズからブレンドを開始する。
この処理は`StopAdditiveOverrideWithSnapshot()`ヘルパーに集約し、全7箇所から呼び出す。

```csharp
// CharacterAnimationController内のprivateヘルパー（全7箇所から呼び出し）
private void StopAdditiveOverrideWithSnapshot()
{
    if (additiveOverrideHelper != null && additiveOverrideHelper.IsActive)
    {
        inertialBlendHelper?.SnapshotCurrentPoseAsClean();
    }
    additiveOverrideHelper?.StopOverride();
}
```

**教訓:** ExecutionOrderの異なるコンポーネント間でポーズデータを共有する場合、
実行順序によるデータの鮮度差に注意が必要。`_lastCleanPose`はIB(20000)で保存されるため
AO(20050)の補正を含まず、AO停止→IB開始の遷移で不整合が生じる。

#### 問題10: Interact中のThinking→ForceStopThinkingでモーション停止

**症状:** interact_sit01ループ中（座り状態）にメッセージ送信 → LLMレスポンス（action=ignore）到着後、
interact_sit01_edが再生されてキャラクターが立ち上がるが、その後Idleアニメーションが開始されず
立ちポーズのまま停止する。Expressionは動作継続。

**原因:** ストリーミングフィールド到着順序により`StopThinkingAndReturn()`→`ForceStopThinking()`が
連続呼び出しされた場合の競合:

```
1. emotionフィールド到着 → StopThinkingAndReturn()
   → RequestEndPhase() → JumpToEndPhase() → _isEnding = true
   → Thinking edフェーズ開始

2. emoteフィールド到着 → ForceStopThinking()
   → _isEnding == true を検出
   → PlayState(Interact, "interact_sit01", resumeAtEnd: true)
   → interact_sit01_edが再生される（意図しない立ち上がり）

3. interact_sit01_ed完了 → OnEndPhaseComplete発火
   → InteractionController.OnAnimEndPhaseComplete()
   → _currentState == Active（Endingではない） → ガードで弾かれる
   → Idle復帰もpendingAction実行も行われない → モーション停止
```

**解決:** `ForceStopThinking()`で`_isEnding == true`かつ復帰先がInteract状態の場合、
`resumeAtEnd`ではなく`resumeAtLoop`を使用。interact_edの再生は`InteractionController.ExitLoopWithCallback()`に委ねる。

```csharp
// ForceStopThinking内
if (_isEnding)
{
    if (_thinkingReturnState == AnimationStateType.Interact
        || _thinkingReturnState == AnimationStateType.Talk)
    {
        PlayState(_thinkingReturnState, _thinkingReturnAnimationId, resumeAtLoop: true);
        return;
    }
    PlayState(_thinkingReturnState, _thinkingReturnAnimationId, resumeAtEnd: true);
    return;
}
```

**教訓:** `resumeAtEnd`は復帰先Timelineのedを暗黙再生するため、ed完了を処理する
コントローラー（InteractionController等）を経由せずにedが走ると、完了ハンドラの状態ガードで
弾かれてデッドロック状態になる。`ForceStopEmote()`にも同じパターンがあり、将来同じ問題が
発生する可能性がある。

#### 問題11: Talk中のThinking→ForceStopThinkingでtalk_idleループ停止

**症状:** Talkポジションでの会話中、ForceStopThinking()後にtalk_idle01が再生されるが、
ループアニメーションが再生されずポーズのまま停止する。

**原因:** 問題10と同じ`_isEnding == true`パターン。Talk復帰時に`resumeAtEnd: true`で
talk_idle01が再生されると、endStart位置（2.750）がloopEnd（2.733）より後ろのため、
ループ範囲を完全にスキップして即座にEnd phase completeになる。

```
LoopRegion: start=0.750, loopEnd=2.733, endStart=2.750
resumeAtEnd → director.time = 2.750（endStart）
→ loopEnd(2.733)を超えているのでループに入れない
→ 即座に End phase complete → モーション停止
```

**解決:** Interact同様、Talk復帰時も`resumeAtLoop`を使用。
talk_idle等のループ待機アニメーションはループ開始位置から再開すべき。

### Transformキャッシュパターン（参照と状態の分離）

**重要なノウハウ**: Animator配下のボーンTransformを操作するコンポーネントでは、
「Transform参照の取得」と「状態値のリセット」を分離する設計が必須。

#### 問題

`animator.GetBoneTransform(HumanBodyBones.Xxx)` を **Awake以外のタイミング**
（Update、コールバック、Timeline再生直前など）で呼び出し、その参照を使ってTransformを
書き換えると、**コード上は値が正しくても視覚的に反映されない**現象が発生する。

この現象は以下の条件で確認された：

- `DefaultExecutionOrder(20000)` のLateUpdateでTransformに書き込み
- ログ上は正しい値（position, rotation）が設定されている
- しかしSkinnedMeshRendererの描画に反映されない
- Awakeで取得した同じTransform参照を使うと正常に動作する

#### 原因（推定）

PlayableDirector（Timeline）の再生開始やAnimatorの内部バインド切り替えと
同じフレームで`GetBoneTransform`を呼ぶと、Animatorの内部状態との不整合が
生じる可能性がある。正確なUnity内部の挙動は不明だが、経験的にAwake時の
参照取得であれば安定して動作することが確認されている。

#### 解決パターン

```csharp
// ■ Awake: 参照のキャッシュ（一度だけ、変更しない）
private Dictionary<HumanBodyBones, Transform> _boneTransformCache;

private void Awake()
{
    _boneTransformCache = new Dictionary<HumanBodyBones, Transform>();
    for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
    {
        var bone = (HumanBodyBones)i;
        var t = animator.GetBoneTransform(bone);
        if (t != null) _boneTransformCache[bone] = t;
    }
}

// ■ 任意のタイミング: 状態値のみリセット（参照は触らない）
public void StartBlend(List<HumanBodyBones> targetBones)
{
    foreach (var bone in targetBones)
    {
        // キャッシュから参照を取得（GetBoneTransformを再呼び出ししない）
        if (_boneTransformCache.TryGetValue(bone, out var t))
        {
            // 参照はキャッシュのまま、数値データだけ初期化
            _bones[i].transform = t;
            _bones[i].previousLocalPosition = t.localPosition;
            ...
        }
    }
}
```

#### 設計原則

| 区分 | 取得タイミング | 変更頻度 |
|------|-------------|---------|
| Transform参照 | Awakeのみ | 不変（キャッシュして使い回す） |
| 計算用の数値 | 処理開始時 | 毎回リセット |

#### コスト

- 全55 Humanoidボーンのキャッシュ: 約0.1ms未満（Awakeで1回のみ）
- メモリ: 約1-2 KB（Dictionary 55エントリ分）
- VRMモデル読み込み（数百ms～数秒）と比較して完全に無視できるレベル

#### 適用箇所

- `InertialBlendHelper`: 慣性補間の対象ボーン操作
- `AdditiveOverrideHelper`: 加算ボーンオーバーライドの対象ボーン操作
- 今後ボーンTransformを直接操作するコンポーネントを追加する場合も同パターンを推奨

### BlendPivot方式の実装経緯

インタラクション時の位置・回転補間について、複数のアプローチを検討し最終的にBlendPivot方式を採用。

#### 初期アプローチ（問題あり）

VRMを直接InteractionPoint位置に設定し、BlendPivotでオフセットを管理する方式。

```
問題点:
- VRM.localRotationをターゲット角度に設定 → Root Motionが追加回転を加える
- 結果: 想定より多く回転する（一回転多いように見える）
- 位置計算も複雑になり、ワールド座標とローカル座標の変換でズレが発生
```

#### 採用方式（BlendPivot方式）

BlendPivotをVRMの現在位置に移動し、VRMをローカル原点にリセット。
BlendPivotがワールド座標で補間、VRMはローカル座標でRoot Motion適用。

```
メリット:
- 計算がシンプル: VRM.world = BlendPivot.world + VRM.local
- Root Motionの修正不要: アニメーションをそのまま使用
- 直感的な結果: 補間とRoot Motionが素直に加算される
```

#### 実装時の問題と解決

**問題1: deltaPositionの座標系**

`Animator.deltaPosition`はワールド座標系だが、`localPosition`に直接加算していた。

```
現象: X方向に約0.1mのズレ
原因: BlendPivotが回転している場合、ワールド座標のdeltaをそのまま加算すると方向がずれる
解決: deltaPositionを親（BlendPivot）のローカル座標系に変換
      localDelta = Quaternion.Inverse(parent.rotation) * deltaPosition
```

**問題2: PlayTimelineでのShouldPreservePosition**

`PlayTimeline`で`SetPositionPreservation`が呼ばれておらず、前回のIdle再生時の`_shouldPreservePosition = true`が残っていた。

```
現象: sit01_edのRoot Motionが反映されない
原因: ShouldPreservePosition = true のため、ApplyRootMotionがスキップされる
解決: PlayTimelineにSetPositionPreservation(_currentState)を追加
      Interactステートでは shouldPreserve = false になる
```

#### 関連ファイル

| ファイル | 変更内容 |
|---------|---------|
| InteractionController.cs | OnApproachCompleteでBlendPivot/VRM設定、SetupBlendTracksでバインド |
| PositionBlendTrack.cs | BlendPivotのワールド位置を補間 |
| RotationBlendTrack.cs | BlendPivotのワールド回転を補間 |
| CharacterNavigationController.cs | ApplyRootMotionでローカル座標変換 |
| CharacterAnimationController.cs | PlayTimelineにSetPositionPreservation追加 |
| VrmLoader.cs | BlendPivotをVRMの親として設定 |

---

### デバッグ表示

`CharacterAnimationController`に画面左上デバッグ表示機能を実装。

```
Timeline: TL_Walk
Frame: 45 / 120
Time: 0.750 / 2.000
State: Walk (Walk)
PlayState: Playing
Loop: InLoop exit=False
Cancel: OK regions=1
Pos: (0.940, 0.000, 0.820)
Rot: (0.0, 169.7, 0.0)
PreservePos: False
```

`showTimelineDebug`フラグで表示/非表示を切り替え可能。

---

## Known Issues / Technical Debt

将来のリファクタリング対象として認識している課題。現時点で機能には影響しない。

### 優先度高

#### ~~TL_interact_sit01のSignal Marker移行~~ (解決済み)

LoopRegionClip + ActionCancelClipへの移行完了。ActionCancelClipはed終盤に配置し、CancelRegion到達時のキャンセル遷移も実装済み。

#### InteractionController - 終了処理の重複

`CancelInteraction()`と`OnInteractionComplete()`が以下の同じ処理を含んでいる：
- 状態リセット
- Idle再生

BlendPivotリセット・位置復元は`ResetBlendPivotAndPosition()`に共通化済み。
残りの状態リセット・Idle再生も共通メソッドに抽出可能。

### 優先度中

#### ~~デフォルトアニメーションIDのハードコード~~ (解決済み)

`"common_idle01"` のハードコードは `ReturnToIdle()` メソッド呼び出しに置換済み。

#### ~~アニメーションID生成のハードコード~~ (解決済み)

`InteractionController.GetInteractionAnimationId()`で`$"interact_{typeId}01"`（家具TypeId）から
IDを生成していたが、`$"interact_{action}01"`（アクション名）に変更済み。
これにより1つの家具が複数アクションに対応できる（例: bedがsit/sleepの両方に対応）。
旧実装ではTypeIdベース（`interact_bed01`）でTimelineBindingDataに一致せず、
フォールバックでstateBindingsの最初のInteract Timeline（sit）が常に返されていた。

#### Timeline遷移時のSpringBoneポップ（髪・揺れ物の大きな動き）

チャット開始時などのTimeline切り替え時に、まれに髪や揺れ物が1〜数フレーム大きく動くことがある。

**対処済みのケース:**
- IB開始時のWaitingFirstFrame/SecondFrame → PrePassで前ポーズをSpringBone前に適用
- ~~IB Blending中のポーズ変化~~ → PrePassをBlending状態にも拡張し、IB全期間にわたって
  SpringBoneがIB補正済みポーズで計算するように修正済み
- lp→ed遷移のポーズポップ → JumpToEndPhaseでIBを開始してスムージング

**残存する可能性のあるケース:**

1. **PlayState内のdirector.Stop()→Play()間の1Fギャップ**: `StopDirectorForAssetChange()`で
   directorを停止し、新Timelineを設定後にPlay()する。Stop()時にAnimatorが出すポーズと
   新Timeline開始ポーズの間で1F差が出る可能性がある（IBのWaitingFirstFrameで対処するが、
   Stop()の瞬間のAnimator出力が予測不能）。
   → 検討: director.Stop()前にボーンポーズをスナップショットし、IBの前ポーズとして使用

2. **複数IB重複開始**: 同一フレームでPlayState→JumpToEndPhaseが実行され、
   PlayStateのIBがWaitingFirstFrameの状態でJumpToEndPhaseが新IBを開始するケース。
   CaptureVisualStateIfActiveで引き継ぐが、WaitingFirstFrame時のvisualStateは
   前ポーズ（previousLocal*）であり、「ControlRig適用後のクリーンポーズ」ではない。
   → 検討: WaitingFirstFrame中のCaptureVisualStateIfActiveで何を返すべきか再検討

### 優先度低

#### ~~VrmLoader - セットアップメソッドの重複~~ (解決済み)

`SetupNavigationComponents()` は `SetupCharacterComponents()` への委譲に変更済み（後方互換性のためメソッド自体は残存）。

#### FurnitureManager - レガシー互換性コード

`_legacyRegistry`とFurniturePoint関連のメソッド（GetFurnitureInCurrentRoom等）が残存している。

**推奨**: FurnitureInstance移行完了後に削除

#### TimelineBindingData - 未使用フィールド

`clipVariants`リストが定義されているが、使用されている形跡が少ない。

**推奨**: 用途を明確化するか、不要なら削除

#### LLMSettingsPanel - Camera Preview UI の起動時表示

**現象:**
- Camera Preview（RawImage）が起動時に表示されない（LLM設定パネルを一度開くと表示される）
- 実際の Vision 機能（LLMへの画像送信）は正常動作

**原因:**
- `SettingsMenuController` により LLMSettingsPanel は起動時に `SetActive(false)`
- 非アクティブな GameObject では `OnEnable()` が呼ばれないため、`InitializeCameraPreview()` および `RetryCameraPreview()` コルーチンが実行されない
- RawImage UI 要素も非アクティブ時は使用不可

**回避策の検討:**
1. パネルを常時アクティブにして CanvasGroup.alpha で可視性制御（UI アーキテクチャの大幅変更が必要）
2. プレビューを別の常時アクティブな場所に移動（設定パネル内のプレビューという UI 設計が崩れる）

**判定:**
- Vision 機能（画像送信）は正常動作するため実用上の問題なし
- プレビューは確認用の補助機能
- 解決には UI システム全体の設計変更が必要なため、現状維持とする

---

## Voice Synthesis System (TTS)

2つのTTSエンジンをサポート。`TTSEngineType` enum でエンジンを切り替え。

| エンジン | 特徴 | リップシンク |
|---------|------|------------|
| **Web Speech API** (デフォルト) | 設定不要、ブラウザ標準、WebGL専用 | Simulated（時間推定） |
| **VOICEVOX** | 高品質、要ローカルサーバー | Mora（母音正確同期） |

### System Architecture

```
ChatManager (Streaming Response)
    ↓ (文単位イベント)
VoiceSynthesisController (TTSEngineType分岐)
    ├─ [VOICEVOX]
    │   ↓ (API呼び出し)
    │   VoicevoxClient ────→ VOICEVOX API (localhost:50021)
    │   ↓ (WAVデータ + モーラタイムライン)
    │   AudioSource (順次再生キュー)
    │   ↓ (再生中イベント)
    │   LipSyncController (Moraモード: 母音正確同期)
    │
    └─ [Web Speech API]
        ↓ (Enqueue)
        WebSpeechSynthesis.cs ──jslib──→ Browser SpeechSynthesis API
        ↓ (OnSpeechStarted / OnSpeechEnded コールバック)
        LipSyncController (Simulatedモード: 時間推定口パク)

LipSyncController → Vrm10Instance.Expression (aa, ih, ou, ee, oh)
```

### Components

#### VoicevoxClient (`Scripts/Voice/`)

**役割**: VOICEVOX REST APIとの通信

**主要機能**:
- スピーカー一覧取得 (`GET /speakers`)
- 音声合成 (`POST /audio_query` → `POST /synthesis`)
- 接続テスト (`GET /version`)
- PlayerPrefs保存/読込（API URL、スピーカーID、話速、音高、抑揚）

**PlayerPrefs キー**:
- `voice_apiUrl` (string)
- `voice_speakerId` (int)
- `voice_speedScale` (float, 0.5-2.0)
- `voice_pitchScale` (float, -0.15-0.15)
- `voice_intonationScale` (float, 0.0-2.0)

**Public API**:
```csharp
Task<List<VoicevoxSpeaker>> GetSpeakers();
Task<(AudioClip clip, List<MoraEntry> moraTimeline)> SynthesizeAsync(string text);
Task<bool> TestConnection();
void LoadSettings();
void SaveSettings();
```

**重要な実装上の注意点**:

1. **audio_query の JSON 処理**:
   - `audio_query` のレスポンスは複雑なネスト構造（accent_phrases、moras等）を持つ
   - Unity の `JsonUtility` でデシリアライズ→再シリアライズすると構造が壊れる
   - **解決策**: 正規表現による文字列置換で `speedScale`、`pitchScale`、`intonationScale` のみを変更
   ```csharp
   // 正規表現で "speedScale":1.0 → "speedScale":1.5 のように置換
   queryJson = Regex.Replace(queryJson, @"""speedScale"":\s*[\d.]+",
       $"\"speedScale\":{speedScale}");
   ```

2. **API URL の末尾スラッシュ問題**:
   - ユーザー入力の URL に末尾スラッシュがある場合、`/speakers` が `//speakers` になる
   - **解決策**: `TrimEnd('/')` で末尾スラッシュを削除
   ```csharp
   apiUrl = apiUrl.TrimEnd('/');
   ```

3. **スピーカーID 0 の扱い**:
   - VOICEVOX API は一部のスタイル（例：四国めたん - あまあま）で `id=0` を返す
   - `id=0` は有効なスピーカーID（ただしバージョンによっては動作しない可能性）
   - ドロップダウンでの選択時に問題ないことを確認済み

4. **モーラタイムライン抽出（リップシンク用）**:
   - `SynthesizeAsync()`がAudioClipとモーラタイムラインのタプルを返す
   - `audio_query`のJSON（`accent_phrases` → `moras` → `vowel`/`vowel_length`/`consonant_length`）を走査
   - 各モーラの累積開始時間（`startTime`）を計算して`MoraEntry`リストを構築
   - `pause_mora`はポーズ（`pau`）としてタイムラインに含まれる
   - JSONパースには`AudioQueryData`系の内部データクラスを使用

#### WavUtility (`Scripts/Voice/`)

**役割**: WAVデータ（byte配列）をUnity AudioClipに変換

**主要機能**:
- WAVヘッダー解析（チャンネル数、サンプルレート、ビット深度）
- PCM16/PCM8 → float配列変換
- AudioClip生成

**Public API**:
```csharp
static AudioClip ToAudioClip(byte[] wavData, string clipName = "VoicevoxClip");
static void SaveAudioClipToWav(AudioClip clip, string filePath); // デバッグ用
```

#### WebSpeechSynthesis (`Scripts/Voice/`) + WebSpeechSynthesis.jslib (`Plugins/WebGL/`)

**役割**: ブラウザ標準Web Speech Synthesis API（TTS）のC#ラッパー。WebGL専用。

**jslib公開関数**:
```javascript
WebSpeechSynth_Initialize(callbackObjectName) // 初期化、voiceschangedリスン
WebSpeechSynth_IsSupported()                  // ブラウザ対応チェック
WebSpeechSynth_GetVoices()                    // 音声リスト取得（JSON、日本語のみ）
WebSpeechSynth_Speak(text, voiceURI, rate, pitch)   // 即時発話（テスト用）
WebSpeechSynth_Enqueue(text, voiceURI, rate, pitch)  // キューに追加→順次再生
WebSpeechSynth_Cancel()                       // 発話中止+キュークリア
WebSpeechSynth_IsSpeaking()                   // 発話中確認
```

**コールバック（SendMessage）**:
- `OnSpeechStarted` - 発話開始 → リップシンク開始
- `OnSpeechEnded` - 発話終了 → リップシンク停止
- `OnSpeechError` - エラー
- `OnVoicesLoaded` - 音声リスト読み込み完了（JSON）
- `OnQueueEmpty` - キュー空（全発話完了）

**C# Public API**:
```csharp
bool Initialize();        // JS側初期化
void Speak(string text);  // 即時発話（テスト用、キュークリア）
void Enqueue(string text); // キュー追加（ストリーミング用）
void Cancel();            // 発話中止+キュークリア
bool IsSpeaking { get; }  // 発話中か
List<WebSpeechVoice> AvailableVoices { get; } // 日本語音声リスト
void LoadSettings();      // PlayerPrefsから読込
void SaveSettings();      // PlayerPrefsへ保存
```

**PlayerPrefs キー**:
- `voice_webSpeechVoiceURI` (string) - 選択音声URI
- `voice_webSpeechRate` (float, 0.5-3.0) - 話速
- `voice_webSpeechPitch` (float, 0.0-2.0) - ピッチ

**jslib実装上の注意点**:

1. **`this`コンテキスト問題**:
   - Unity WebGLのjslibは`mergeInto(LibraryManager.library, {...})`で関数を登録する
   - emscriptenがライブラリ関数を個別に抽出するため、`this.関数名()`で他のライブラリ関数を呼べない
   - `this.プロパティ名`でのデータアクセスは正常に動作する
   - **解決策**: キュー処理関数はInitialize時にクロージャとして作成し、プロパティに格納
   ```javascript
   // ❌ this.processQueue() は呼べない（関数はthis経由で参照不可）
   // ✅ this._webSpeechSynthProcessQueue() は呼べる（プロパティに格納した関数）
   var self = this;
   this._webSpeechSynthProcessQueue = function() { /* ... */ };
   ```

2. **音声リスト非同期読み込み**:
   - ブラウザによって`speechSynthesis.getVoices()`は非同期で読み込まれる
   - `voiceschanged`イベントで通知、既に読み込み済みの場合は即座に通知
   - C#側で`OnVoicesLoadedEvent`を購読前に音声が読み込まれるケースがある
   - 対策: VoiceSettingsPanelの`Start()`で`AvailableVoices`を直接参照して補完

#### TTSEngineType (`Scripts/Core/`)

**役割**: TTSエンジン種別enum

```csharp
public enum TTSEngineType
{
    WebSpeechAPI = 0,  // デフォルト（設定不要）
    VOICEVOX = 1       // 高品質（要サーバー）
}
```

#### VoiceSynthesisController (`Scripts/Voice/`)

**役割**: TTS中央制御。TTSEngineTypeに応じたエンジン分岐、再生キュー管理、リップシンク連携。

**主要機能**:
- TTS ON/OFF（デフォルトOFF、PlayerPrefs永続化）
- TTSエンジン切替（WebSpeechAPI / VOICEVOX）
- ストリーミング応答時の文区切り検出（。！？…\n）
- VOICEVOX時: AudioClip + モーラタイムライン再生キュー
- WebSpeechAPI時: JS側キューにEnqueue、イベントでリップシンク制御
- エンジン設定のPlayerPrefs保存
- **TTS-STTエコー防止**: TTS再生中はSTT（音声認識）を自動抑制し、全再生完了＋クールダウン後に再開（ON/OFF切替可能、ヘッドセット使用時はOFF推奨）

**Public API**:
```csharp
void SynthesizeAndPlay(string text);          // Blocking Response用
void OnStreamingTextReceived(string chunk);   // ChatManagerから呼ばれる
void OnStreamingComplete();                   // ストリーミング終了時
void Stop();                                  // 再生停止・キュークリア
void SetEnabled(bool enable);
void SetTTSEngine(TTSEngineType type);        // エンジン切替
TTSEngineType CurrentEngine { get; }          // 現在のエンジン

// Inspector参照
public VoiceInputController voiceInputController; // STTエコー防止用
public float sttResumeCooldown = 1.0f;            // TTS終了後のSTT再開待機秒数
public bool echoPreventionEnabled = true;         // エコー防止ON/OFF（デフォルトON）

// エコー防止
void SetEchoPreventionEnabled(bool enable);       // ON/OFF切替（OFF時はSTT抑制即解除）
bool IsEchoPreventionEnabled { get; }             // 現在の状態

// TTSクレジット表示
string TTSCreditText { get; }                     // 現在のクレジット文字列
event Action<string> OnTTSCreditChanged;          // クレジット変更イベント
void UpdateTTSCredit(string speakerName, string styleName); // クレジット更新
```

**TTSクレジット文字列:**
| TTS状態 | 表示テキスト |
|---------|-------------|
| OFF | `"OFF"` |
| Web Speech API | `"Web Speech API"` |
| VOICEVOX（スピーカー情報あり） | `"VOICEVOX:ずんだもん(ノーマル)"` |
| VOICEVOX（スピーカー情報なし） | `"VOICEVOX"` |

**PlayerPrefs キー**:
- `voice_ttsEnabled` (int) - TTS ON/OFF（1=ON, 0=OFF、デフォルトOFF）
- `voice_ttsEngine` (int) - 0=WebSpeechAPI, 1=VOICEVOX
- `voice_echoPrevention` (int) - エコー防止ON/OFF（1=ON, 0=OFF、デフォルトON）

**動作フロー（ストリーミング時）**:
1. ChatManager → `OnStreamingTextReceived(chunk)`
2. バッファに蓄積、文区切り検出
3. **VOICEVOX時**:
   - 完成文 → `VoicevoxClient.SynthesizeAsync()`（非同期）
   - `(AudioClip, List<MoraEntry>)` 取得 → VOICEVOXキューに追加
   - AudioSource再生 + `LipSyncController.StartMoraLipSync(moraTimeline)`
4. **WebSpeechAPI時**:
   - 完成文 → `WebSpeechSynthesis.Enqueue(text)`
   - JS側で順次発話、`OnSpeechStarted`コールバックで`LipSyncController.StartSimulatedLipSync()`
   - テキスト長からおおよその発話時間を推定（日本語約6文字/秒÷rate）

#### LipSyncController (`Scripts/Character/`)

**役割**: VRM 1.0 Expressionベースのリップシンク統合コントローラ（4モード対応）

旧`CharacterLipSyncController`（テキスト口パク専用）を統合。
TTS有効時はTextOnlyモードが自動抑制される。

**更新タイミング**: `LateUpdate`で実行。PlayableDirector評価（Facial TimelineによるExpression全リセット＋加算）
の後に口の形（aa/ih/ou/ee/oh）を書き込むことで、感情Timelineとリップシンクを共存させる。

**リップシンクモード**:

| モード | 用途 | データソース | 精度 |
|--------|------|-------------|------|
| **Mora** | VOICEVOX | AudioQuery のモーラ時間+母音データ | 高（母音一致） |
| **Simulated** | Web Speech API | テキスト長から推定 | 低（母音サイクル） |
| **TextOnly** | テキスト表示のみ（音声なし） | テキスト長から推定 | 低（母音サイクル） |
| **Amplitude** | フォールバック | AudioSource振幅 | 中（ランダム母音） |

**データ構造**:
```csharp
public struct MoraEntry
{
    public float startTime;  // 累積開始時間（秒）
    public float duration;   // consonant_length + vowel_length
    public string vowel;     // a, i, u, e, o, N, cl, pau
}
```

**母音マッピング（VOICEVOX → VRM Expression）**:
- `a` → `aa`, `i` → `ih`, `u` → `ou`, `e` → `ee`, `o` → `oh`
- `N`/`cl`/`pau` → 閉口（weight=0）

**設定パラメータ**:
- `intensity` (0.0-1.0): リップシンク強度
- `speed`: 口の動きの速度（Lerp係数）
- `amplitudeThreshold`: 振幅しきい値（Amplitudeモード用）
- `vowelSwitchProbability`: 母音切り替え確率（Amplitudeモード用）
- `simulatedMoraSpeed` (0.12秒): Simulated/TextOnlyモードの1モーラ周期

**Public API**:
```csharp
// VRM設定（VRM読み込み時）
void SetVrmInstance(Vrm10Instance instance);

// TextOnlyモード（テキスト表示のみ用、TTS有効時は自動抑制）
void StartSpeaking(string text);
void StopSpeaking();
bool IsSpeaking { get; }
void SetTtsActive(bool active);

// TTS用モード
void StartLipSync(AudioClip clip);                    // Amplitudeモード
void StartMoraLipSync(List<MoraEntry> moraTimeline);  // Moraモード（VOICEVOX）
void StartSimulatedLipSync(float estimatedDuration);  // Simulatedモード（WebSpeech）
void StopLipSync();
```

**各モードの動作**:
- **Mora**: `moraTimeline`のcurrentTimeに応じた母音を正確に適用。子音時間中はスムーズ遷移。
- **Simulated**: 母音サイクルを周期（~0.12秒）で切り替え。60%開口・40%閉口パターン。
- **TextOnly**: Simulatedと同等のロジック。テキスト長から推定時間を算出し自動停止。TTS有効時は抑制。
- **Amplitude**: AudioSource振幅解析＋ランダム母音切り替え（従来方式）。

#### VoiceSettingsPanel (`Scripts/UI/`)

**役割**: TTSエンジン選択 + 各エンジン設定 + 音声入力設定の統合UI

**UIレイアウト**:
```
VoiceSettingsPanel
├─ [☐ TTS]                               ← ttsEnabledToggle（デフォルトOFF）
├─ [TTSエンジン: [Web Speech API ▼]]     ← ttsEngineDropdown（TTS OFF時は操作不可）
├─ WebSpeechSettingsSection (GameObject)  ← TTS ON + WebSpeech選択時のみ表示
│  ├─ Voice: [Google 日本語 ▼]            ← 日本語音声のみ
│  ├─ Rate:  [---●---------] 1.00        ← 0.5-3.0
│  ├─ Pitch: [------●------] 1.00        ← 0.0-2.0
│  └─ [▶ Test Play]
├─ VoicevoxSettingsSection (GameObject)   ← TTS ON + VOICEVOX選択時のみ表示
│  ├─ API URL: [http://localhost:50021]
│  ├─ Speaker: [ずんだもん (ノーマル) ▼]
│  ├─ Speed/Pitch/Intonation sliders
│  └─ [Test Connection] [▶ Test Play]
├─ Voice Input (STT) セクション（常時表示）
│  ├─ [☐ Microphone]                     ← microphoneToggle
│  ├─ [☑ Echo Prevention]                ← echoPreventionToggle（デフォルトON）
│  ├─ Language: [日本語 (ja-JP) ▼]
│  ├─ Silence Threshold: [2.0]
│  └─ Status: "Standby" / "Recording..."
└─ [Save]
```

**条件表示ロジック**:
- `UpdateTTSSettingsVisibility()`: TTS ON/OFFとエンジン選択に応じてセクション表示を制御
- TTS OFF時: エンジンドロップダウン操作不可、両エンジン設定セクション非表示
- TTS ON時: `UpdateTTSEngineUI()`でエンジンに応じたセクション表示
  - WebSpeechAPI選択時: `webSpeechSettingsSection`表示、`voicevoxSettingsSection`非表示
  - VOICEVOX選択時: 逆

**保存ロジック（OnSaveClicked）**:
- VOICEVOX API URLは常に保存（エンジン切替後に使えるように）
- VOICEVOXスピーカー検証はVOICEVOX選択時のみ（WebSpeechAPI選択中はスキップ）
- Web Speech API設定は常に保存
- 音声入力（STT）設定は常に保存

**動作**:
- `OnEnable()`: 設定をUIに反映、スピーカー一覧取得
- `OnDisable()`: テスト再生の停止・clipクリア
- `Start()`: Web Speech音声リスト取得（`AvailableVoices`直接参照で非同期タイミング補完）
- 接続テスト: `/version`でVOICEVOX起動確認 → スピーカー一覧更新
- テスト再生（VOICEVOX）: VoicevoxClient.SynthesizeAsync → AudioSource.Play
- テスト再生（WebSpeech）: WebSpeechSynthesis.Speak（即時発話）
- 保存: 各コンポーネントの`SaveSettings()`呼び出し

### ChatManager Integration

**変更箇所**:

```csharp
[Header("Voice Synthesis")]
public CyanNook.Voice.VoiceSynthesisController voiceSynthesisController;
```

**統合ポイント**:

1. **ストリーミング応答時** (`HandleStreamText()`):
   ```csharp
   // Sleep/Outing中は抑制（wake-up/cron entry時は通す）
   voiceSynthesisController?.OnStreamingTextReceived(textChunk);
   ```

2. **ストリーミング完了時** (`HandleRequestCompleted()`):
   ```csharp
   // Sleep/Outing中は抑制
   voiceSynthesisController?.OnStreamingComplete();
   ```

3. **ブロッキング応答時のみ** (`HandleLLMResponse()`):
   ```csharp
   // _isStreamingRequestフラグでストリーミング時の二重合成を防止
   // Outing中は抑制（_isCronEntryRequest時は通す）
   if (!_isStreamingRequest)
       voiceSynthesisController?.SynthesizeAndPlay(response.message);
   ```

**Sleep/Outing中のTTS抑制:**
上記3箇所すべてで、Sleep中（`_isWakeUpRequest`を除く）およびOuting中（`_isCronEntryRequest`を除く）はTTS処理をスキップする。
`HandleStreamField` のreaction TTS転送も同様に抑制される。

### Data Flow

#### ストリーミング応答 + 音声合成（VOICEVOX）

```
User Input
    ↓
ChatManager.SendChatMessage()
    ↓
LLMClient (Streaming Response)
    ↓ (chunk受信)
ChatManager.HandleStreamText(chunk)
    ├→ UIController (テキスト表示)
    └→ VoiceSynthesisController.OnStreamingTextReceived(chunk)
        ├→ バッファに蓄積
        └→ 文区切り検出時
            ↓
        VoicevoxClient.SynthesizeAsync(sentence)
            ↓
        WAVデータ + モーラタイムライン取得
            ↓
        VOICEVOXキューに追加 (AudioClip + List<MoraEntry>)
            ↓
        AudioSource.Play() (順次再生)
            ↓
        LipSyncController.StartMoraLipSync(moraTimeline)
            ↓
        モーラ母音に基づく正確な口パク
            ↓
        Vrm10Instance.Expression (aa, ih, ou, ee, oh)
```

#### ストリーミング応答 + 音声合成（Web Speech API）

```
User Input
    ↓
ChatManager.SendChatMessage()
    ↓
LLMClient (Streaming Response)
    ↓ (chunk受信)
ChatManager.HandleStreamText(chunk)
    ├→ UIController (テキスト表示)
    └→ VoiceSynthesisController.OnStreamingTextReceived(chunk)
        ├→ バッファに蓄積
        └→ 文区切り検出時
            ↓
        WebSpeechSynthesis.Enqueue(sentence)
            ↓
        jslib → Browser SpeechSynthesis.speak() (キュー管理はJS側)
            ↓
        OnSpeechStarted (SendMessage callback)
            ↓
        LipSyncController.StartSimulatedLipSync(estimatedDuration)
            ↓
        時間推定ベースの母音サイクル口パク
            ↓
        Vrm10Instance.Expression (aa, ih, ou, ee, oh)
            ↓
        OnSpeechEnded → LipSyncController.StopLipSync()
```

#### 音声テスト（VoiceSettingsPanel）

```
User (VoiceSettingsPanel)
    ↓ (TestPlayButton click)
OnTestPlayClicked()
    ├─ [VOICEVOX]:
    │   VoicevoxClient.SynthesizeAsync(testText)
    │   → WAVデータ取得 → AudioClip → testAudioSource.Play()
    └─ [Web Speech API]:
        WebSpeechSynthesis.Speak(testText) → ブラウザ直接発話
```

### VOICEVOX API Specification

#### エンドポイント

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/version` | GET | バージョン取得（接続テスト用） |
| `/speakers` | GET | スピーカー一覧取得 |
| `/audio_query` | POST | 音声合成用クエリ作成 |
| `/synthesis` | POST | 音声合成（WAV取得） |

#### パラメータ範囲

| Parameter | Range | Default | Description |
|-----------|-------|---------|-------------|
| `speedScale` | 0.5-2.0 | 1.0 | 話速 |
| `pitchScale` | -0.15-0.15 | 0.0 | 音高 |
| `intonationScale` | 0.0-2.0 | 1.0 | 抑揚 |

### WebGL対応

**Web Speech API（対応済み）**:
- ブラウザ標準APIのためCORS制限なし。WebGLでそのまま動作。
- デフォルトTTSエンジンとして採用。設定不要で即使用可能。

**VOICEVOX WebGL対応（未実装）**:
- UnityWebRequestはブラウザ環境でCORS制限を受ける
- `localhost:50021`へのアクセスは同一オリジンでないとブロックされる
- VOICEVOX APIはCORS非対応（デスクトップアプリ向け設計）

**解決策（プロキシサーバー経由）**:
```
Unity WebGL → Node.jsプロキシ → VOICEVOX API (localhost:50021)
(ブラウザ)     (同一オリジン)      (ローカル)
```

**現状**: VOICEVOXはスタンドアロンビルドのみ対応。WebGLではWeb Speech APIをデフォルトで使用。

### 実装ステータス

**VOICEVOX TTS** (2026-02-16):
- ✅ VOICEVOX API 通信（スピーカー一覧、音声合成、接続テスト）
- ✅ 音声パラメータ設定（話速、音高、抑揚）
- ✅ WAV → AudioClip 変換
- ✅ ストリーミング対応（文単位での順次再生）
- ✅ PlayerPrefs による設定保存
- ✅ ChatManager 統合

**Web Speech API TTS + TTSエンジン切替 + リップシンク3モード** (2026-02-20):
- ✅ Web Speech Synthesis API 統合（jslib + C#ラッパー）
- ✅ TTSエンジン切替システム（WebSpeechAPI / VOICEVOX）
- ✅ VoiceSettingsPanel: エンジン選択ドロップダウン、条件表示UI
- ✅ Web Speech API: 日本語音声選択、Rate/Pitch設定、テスト再生
- ✅ LipSyncController 4モード対応（Mora / Simulated / TextOnly / Amplitude）
  - 旧CharacterLipSyncControllerを統合、TextOnlyモードとして吸収
- ✅ VOICEVOX モーラタイムライン抽出（AudioQuery → MoraEntry）
- ✅ Web Speech API シミュレーションリップシンク（テキスト長推定）
- ✅ ストリーミング時のキュー再生（JS側キュー管理）
- ✅ 全設定のPlayerPrefs保存・復元

**動作確認環境**:
- Unity 6 (WebGL Build) - Web Speech API
- Unity 6 (Windows Standalone) - VOICEVOX
- VOICEVOX 0.25.1 (localhost:50021)

### 制限事項と今後の拡張

#### 制限事項
- Web Speech APIはWebGL専用（Editorでは動作しない）
- Web Speech APIの音声リストはブラウザ/OS依存（利用可能な日本語音声が異なる）
- Web Speech APIリップシンクはテキスト長推定による簡易シミュレーション
- VOICEVOX WebGLはCORSプロキシ未実装のためスタンドアロンのみ
- Unity JsonUtility の制限により、audio_query の再シリアライズは不可
  - パラメータ変更は正規表現による文字列置換で対応

#### 既知の問題
- 一部のスピーカースタイル（id=0）は VOICEVOX のバージョンによっては動作しない可能性
  - 回避策: id=2 以上のスピーカーを選択

#### 今後の拡張案
- **感情表現**: EmotionData → VOICEVOXスタイル自動選択
- **VOICEVOX WebGL対応**: JavaScript Bridge + Node.jsプロキシサーバー
- **音声キャッシュ**: 頻出フレーズのWAVファイルキャッシュ
- **より高度なJSON処理**: Newtonsoft.Json 等のサードパーティライブラリ導入

### トラブルシューティング

#### HTTP 500 Internal Server Error (synthesis endpoint)

**症状**: `/synthesis` エンドポイントで 500 エラーが発生

**原因**:
1. **JsonUtility 再シリアライズ問題**: `audio_query` のレスポンスを `JsonUtility.FromJson()` → `JsonUtility.ToJson()` で処理すると、複雑なネスト構造（`accent_phrases`、`moras` 等）が正しくシリアライズされず、VOICEVOX API がパースできない
2. **スピーカーID 0**: 一部の VOICEVOX バージョンで `speaker=0` が受け付けられない

**解決策**:
```csharp
// ❌ 間違った実装（JsonUtility再シリアライズ）
var query = JsonUtility.FromJson<AudioQuery>(json);
query.speedScale = newSpeed;
string modified = JsonUtility.ToJson(query); // accent_phrases が壊れる

// ✅ 正しい実装（正規表現による文字列置換）
json = Regex.Replace(json, @"""speedScale"":\s*[\d.]+",
    $"\"speedScale\":{newSpeed}");
```

#### HTTP 404 Not Found (/speakers endpoint)

**症状**: `/speakers` エンドポイントで 404 エラー、URL が `//speakers` になる

**原因**: ユーザー入力の API URL に末尾スラッシュがある（例: `http://localhost:50021/`）

**解決策**:
```csharp
apiUrl = apiUrl.TrimEnd('/'); // 末尾スラッシュを削除
```

#### 音声が再生されない

**症状**: AudioClip は正常に作成されるが、音が出ない

**チェックリスト**:
1. **Audio Listener**: MainCamera に `Audio Listener` コンポーネントがあるか
   - Mute されていないか確認
2. **AudioSource 設定**:
   - Volume: 1.0
   - Mute: チェック無し
   - Spatial Blend: 0 (2D)
3. **Unity エディタ**: Edit > Project Settings > Audio
   - Master Volume が 0 でないか確認

#### スピーカーリストが空

**症状**: Test Connection 後もスピーカーが表示されない

**原因**:
1. VOICEVOX が起動していない
2. API URL が間違っている
3. ファイアウォールがブロックしている

**解決策**:
1. VOICEVOX アプリケーションを起動
2. ブラウザで `http://localhost:50021/speakers` にアクセスして JSON が返ることを確認
3. Windows Defender ファイアウォールの例外設定を確認

---

## Voice Input System (Web Speech API)

WebGL専用の音声認識システム。ブラウザ標準のWeb Speech APIを使用し、マイク入力をリアルタイムで文字起こし。

### アーキテクチャ

```
Microphone (Browser)
  ↓
Web Speech API (JavaScript)
  ↓ SendMessage()
WebSpeechAPI.jslib
  ↓ Callback
WebSpeechRecognition.cs (C#)
  ↓ UnityEvent
VoiceInputController.cs
  ├→ UIController.chatInputField (リアルタイム表示)
  └→ VoiceActivityDetector.cs (蓄積)
       ↓ N秒無音検出
       ↓ OnSilenceDetected
VoiceInputController
  ↓ GetAndClearText()
UIController.SendMessageFromVoice()
  ↓
ChatManager.SendChatMessage()
```

### コンポーネント

#### WebSpeechAPI.jslib (`Plugins/WebGL/`)

**役割**: JavaScript側のWeb Speech APIラッパー

**公開関数**:
```javascript
WebSpeech_Initialize(callbackObjectName, language, continuous, interimResults)
WebSpeech_Start()
WebSpeech_Stop()
WebSpeech_Abort()
WebSpeech_SetLanguage(language)
WebSpeech_IsSupported()
```

**動作**:
1. `SpeechRecognition` オブジェクト作成
2. イベントハンドラ設定（`onresult`, `onerror`, `onend`）
3. 認識結果を `SendMessage()` でUnity側に送信

**コールバック**:
- `OnRecognitionStarted(string)` - 認識開始
- `OnPartialResult(string)` - 部分結果（話している途中）
- `OnFinalResult(string)` - 確定結果
- `OnRecognitionError(string)` - エラー
- `OnRecognitionEnded(string)` - 認識終了

#### WebSpeechRecognition.cs (`Scripts/Voice/`)

**役割**: Web Speech APIのC#ラッパー

**プロパティ**:
```csharp
[Header("Settings")]
public string language = "ja-JP";           // 認識言語
public bool continuous = true;              // 継続的認識
public bool interimResults = true;          // 部分結果取得

[Header("Events")]
public UnityEvent OnRecognitionStartedEvent;
public UnityEvent<string> OnPartialResultEvent;
public UnityEvent<string> OnFinalResultEvent;
public UnityEvent<string> OnRecognitionErrorEvent;
public UnityEvent OnRecognitionEndedEvent;
```

**メソッド**:
```csharp
bool Initialize()           // 初期化（エディタではfalse）
bool StartRecognition()     // 認識開始（_shouldAutoRestart=true）
bool StopRecognition()      // 認識停止（abort()使用、自動再起動抑制）
bool SetLanguage(string)    // 言語変更
bool IsRecognizing { get; } // 認識中かどうか
```

**WebGL専用**:
- `#if UNITY_WEBGL && !UNITY_EDITOR` でコンパイル条件分岐
- エディタでは常に `false` を返す（警告ログなし）

**停止方式**:
- `StopRecognition()` は `abort()` を使用（`stop()` ではない）。`stop()` は処理中の音声セグメントを最後まで処理して結果を返してしまうため、TTS音声のエコーが認識結果として返される問題がある
- `_shouldAutoRestart` フラグで `onend` イベントでの自動再起動を制御。`StopRecognition()` 時は `false` に設定し、`StartRecognition()` 時に `true` に復元

#### VoiceActivityDetector.cs (`Scripts/Voice/`)

**役割**: 無音検出・自動送信

**プロパティ**:
```csharp
public float silenceThreshold = 2f;  // 無音判定秒数
public UnityEvent OnSilenceDetected; // 無音検出イベント
```

**動作**:
1. `OnPartialResult()` / `OnFinalResult()` で文字起こし結果を蓄積
2. 結果更新時に最終更新時刻を記録
3. `Update()` で経過時間をチェック
4. N秒経過 → `OnSilenceDetected` イベント発火

**メソッド**:
```csharp
void OnPartialResult(string text)  // 部分結果受信（上書き）
void OnFinalResult(string text)    // 確定結果受信（追加）
string GetAndClearText()           // 蓄積テキスト取得＆クリア
void Reset()                       // 手動リセット
```

#### VoiceInputController.cs (`Scripts/Voice/`)

**役割**: 音声入力全体の統合管理

**プロパティ**:
```csharp
[Header("References")]
public WebSpeechRecognition speechRecognition;
public VoiceActivityDetector activityDetector;
public UIController uiController;

[Header("Settings")]
public string language = "ja-JP";
```

**イベント**:
```csharp
event Action<bool> OnEnabledChanged;    // マイクON/OFF状態変更通知（UI同期用）
```

**メソッド**:
```csharp
void SetEnabled(bool enabled)           // マイクON/OFF（→ OnEnabledChanged発火）
void SetLanguage(string newLanguage)    // 言語変更
void SetSilenceThreshold(float)         // 無音閾値変更
void SuppressForTTS()                   // TTS再生中のSTT一時停止（状態クリアなし）
void ResumeFromTTS()                    // TTS再生完了後のSTT再開（VADリセット付き）
bool IsEnabled { get; }                 // 有効状態取得
```

**動作**:
1. `Start()` で初期化（エディタでは失敗、WebGLで成功）
2. イベント接続: `OnPartialResultEvent` → `OnPartialTranscription()`
3. 部分結果 → UIController.chatInputField に表示
4. 確定結果 → VoiceActivityDetector に蓄積
5. 無音検出 → `SendMessageFromVoice()` で自動送信

#### VoiceSettingsPanel.cs (`Scripts/UI/`)

**役割**: 音声入力設定UI

**UIフィールド**:
```csharp
[Header("UI - Voice Input (Speech to Text)")]
public Toggle microphoneToggle;                    // マイクON/OFF
public Toggle echoPreventionToggle;                // エコー防止ON/OFF
public TMP_Dropdown voiceInputLanguageDropdown;    // 言語選択
public TMP_InputField silenceThresholdInputField;  // 無音閾値
public TMP_Text voiceInputStatusText;              // ステータス表示
```

**PlayerPrefs キー**:
- `voice_micEnabled` (int): マイク有効状態
- `voice_inputLanguage` (string): 認識言語
- `voice_silenceThreshold` (float): 無音閾値

**動作**:
- `OnEnable()` で設定読み込み、UIに反映
- トグル/ドロップダウン変更時に `VoiceInputController` / `VoiceSynthesisController` を制御
- `Start()` 末尾で `ApplySavedMicrophoneSetting()` — 保存済みマイク設定の起動時適用
- `Update()` でステータス表示をリアルタイム更新
- `VoiceInputController.OnEnabledChanged` を購読 — 外部（UIControllerのマイクボタン等）からの状態変更時に `microphoneToggle.SetIsOnWithoutNotify()` でUI同期（無限ループ防止）+ PlayerPrefs保存

**起動時マイク設定の適用**:
`OnEnable()` → `LoadVoiceInputSettings()` はリスナー登録（`Start()` → `InitializeVoiceInput()`）より先に実行されるため、トグルUI更新だけで `VoiceInputController.SetEnabled()` が呼ばれない。`Start()` 末尾の `ApplySavedMicrophoneSetting()` で明示的に適用する。

### データフロー

#### 音声認識の開始

```
User: マイクトグルON（設定パネル or チャット欄横ボタン）
  ↓
VoiceSettingsPanel.OnMicrophoneToggleChanged(true)  ← 設定パネルから
UIController.OnMicButtonClicked()                   ← マイクボタンから
  ↓
VoiceInputController.SetEnabled(true)
  ├→ WebSpeechRecognition.StartRecognition()
  └→ OnEnabledChanged(true)  ← UI同期イベント
       ├→ UIController.UpdateMicButtonVisual(true)          ← アイコン切替
       └→ VoiceSettingsPanel.OnVoiceInputEnabledChanged()   ← トグル同期（SetIsOnWithoutNotify）+ PlayerPrefs保存
  ↓
WebSpeech_Start() [jslib]
  ↓
SpeechRecognition.start() [Browser API]
  ↓
Browser: マイク権限要求ダイアログ（初回のみ）
```

#### リアルタイム文字起こし

```
User speaks
  ↓
Browser: Web Speech API
  ↓ onresult event
WebSpeechAPI.jslib
  ↓ SendMessage("OnPartialResult", transcript)
WebSpeechRecognition.OnPartialResult(transcript)
  ↓ OnPartialResultEvent.Invoke(transcript)
VoiceInputController.OnPartialTranscription(transcript)
  ├→ UIController.chatInputField.text = transcript (表示)
  └→ VoiceActivityDetector.OnPartialResult(transcript) (蓄積)
```

#### 確定結果の送信

```
User stops speaking (N秒無音)
  ↓
VoiceActivityDetector.Update()
  ↓ Time.time - lastUpdateTime >= silenceThreshold
  ↓ OnSilenceDetected.Invoke()
VoiceInputController.OnSilenceDetected()
  ↓ activityDetector.GetAndClearText()
  ↓ uiController.SendMessageFromVoice(finalText)
UIController.SendMessageFromVoice(text)
  ↓ chatInputField.text = text
  ↓ OnSend()
ChatManager.SendChatMessage()
```

#### TTS-STTエコー防止

TTS音声をマイクが拾い、キャラクターの発話内容がそのままLLMへ再送信される問題を防止する。

**ON/OFF切替**:
- `VoiceSynthesisController.echoPreventionEnabled`（デフォルトON、PlayerPrefs永続化）
- VoiceSettingsPanelのエコー防止トグルで制御
- ヘッドセット使用時やエコーキャンセル対応スピーカーマイク使用時はOFFにできる
- OFF切替時: 抑制中のSTTを即座に再開、以降のTTS再生でもSTTを停止しない

**抑制タイミング**（`echoPreventionEnabled == true` 時のみ動作）:
- TTS再生開始時（VOICEVOX `PlayNextVoicevox()` / Web Speech API `OnWebSpeechStarted()`）
- `VoiceSynthesisController` → `VoiceInputController.SuppressForTTS()`

**再開条件（`TryResumeSTT()` で一元判定）**:
以下の**全条件**を満たした時、`sttResumeCooldown`（デフォルト1.0秒）後にSTTを再開:
1. `_isPlaying == false` — TTS再生中でない
2. `_isStreaming == false` — LLMストリーミング完了
3. `_pendingSynthesisCount == 0` — VOICEVOX合成リクエストなし
4. `_voicevoxQueue.Count == 0` — 再生キュー空

**クールダウン**:
- TTS再生完了後、即座にSTTを再開するとスピーカーの残響をマイクが拾うため、`sttResumeCooldown`秒の待機後に再開
- クールダウン中にTTSが再開された場合はコルーチンをキャンセル

**多層防御**:
1. `WebSpeechRecognition.StopRecognition()` — `abort()` で即座に音声認識を中断（`stop()` は処理中の音声を返してしまう）
2. `_shouldAutoRestart = false` — `onend` での自動再起動を抑制
3. `VoiceInputController._isSuppressedByTTS` ガード — `OnPartialTranscription` / `OnFinalTranscription` / `OnSilenceDetected` で結果を破棄
4. `ResumeFromTTS()` 時に `activityDetector.Reset()` — 蓄積テキストをクリア

```
TTS再生開始
  ↓
VoiceSynthesisController.PlayNextVoicevox() / OnWebSpeechStarted()
  ↓ CancelSTTResumeCooldown()
  ↓ voiceInputController.SuppressForTTS()
VoiceInputController._isSuppressedByTTS = true
  ↓ speechRecognition.StopRecognition()
WebSpeechRecognition: abort() + _shouldAutoRestart = false
  ↓
[TTS再生中: STT結果は全て破棄]
  ↓
TTS再生完了（WaitForPlaybackEnd / OnWebSpeechQueueEmpty）
  ↓ TryResumeSTT() — 全条件チェック
  ↓ ResumeSTTAfterCooldown() — sttResumeCooldown秒待機
  ↓ 再チェック（クールダウン中にTTS再開されていないか）
VoiceInputController.ResumeFromTTS()
  ↓ activityDetector.Reset()
  ↓ speechRecognition.StartRecognition() — _shouldAutoRestart = true
```

### Web Speech API の特徴

#### ✅ メリット

- **ブラウザ標準API**: 追加ライブラリ不要
- **WebGL完全対応**: ネイティブプラグイン不要
- **高精度**: Google Speech APIベース
- **無料**: 回数制限なし
- **多言語対応**: 50+言語（日本語、英語、中国語、韓国語対応）

#### ⚠️ 制限事項

1. **オンライン必須**
   - インターネット接続が必要
   - オフラインでは動作しない

2. **ブラウザ対応**
   - Chrome, Edge: 完全サポート ✅
   - Firefox: 一部制限あり ⚠️
   - Safari: iOS 14.5+でサポート ✅

3. **マイク権限**
   - 初回使用時にブラウザがマイク許可を要求
   - HTTPSまたはlocalhostでのみ動作

4. **音量制御**
   - ブラウザがマイク音量を管理
   - Unity側からの音量調整は不可

5. **タイムアウト**
   - 無音が続くと自動停止（`no-speech`エラー）
   - `continuous: true` で自動再起動

### 実装状況

- ✅ Web Speech API統合（WebGL専用）
- ✅ リアルタイム文字起こし
- ✅ 無音検出・自動送信
- ✅ 多言語対応（4言語）
- ✅ 設定の永続化（PlayerPrefs）
- ✅ VoiceSettingsPanelに統合
- ✅ TTS-STTエコー防止（TTS再生中のSTT自動抑制）
- ⬜ 音量可視化（ブラウザ制限により不可）
- ⬜ オフライン対応（Web Speech API制限により不可）

### トラブルシューティング

#### エディタで初期化失敗ログが出る

**症状**: `[VoiceInputController] Voice input not available in Editor (WebGL build only)`

**原因**: Web Speech APIはWebGL専用機能のため、エディタでは動作しない

**対応**: **正常な動作です**。WebGLビルドでテストしてください。

#### マイク権限が拒否される

**症状**: `[VoiceInputController] Microphone permission denied`

**原因**: ブラウザのマイク権限が拒否されている

**解決策**:
1. ブラウザのアドレスバーのアイコンをクリック
2. マイク権限を「許可」に変更
3. ページをリロード

#### 文字起こしが表示されない

**チェックリスト**:
1. **WebGLビルドか確認**: エディタでは動作しない
2. **ブラウザ対応**: Chrome/Edgeを使用
3. **HTTPS/localhost**: セキュアコンテキストが必要
4. **マイク接続**: マイクが正しく接続されているか
5. **ブラウザコンソール**: F12でエラーログを確認

#### 無音検出が早すぎる/遅すぎる

**症状**: 話し終わる前に送信される / 長時間待たないと送信されない

**調整**:
1. VoiceSettingsPanel > 無音閾値を調整
   - 早すぎる場合: 2.0 → 3.0秒に増やす
   - 遅すぎる場合: 2.0 → 1.0秒に減らす
2. 設定保存で永続化

---

## WebGL Build Support

WebGLビルド固有の制約に対応するための仕組み。

### StreamingAssets マニフェストシステム

**問題**: WebGLではブラウザ上で動作するため、`Directory.GetFiles()` 等のファイルシステムAPIが使えない。VRMファイル一覧の動的取得が不可能。

**解決策**: ビルド時にマニフェストJSON（ファイル一覧）を自動生成し、WebGLではUnityWebRequestで読み込む。

#### コンポーネント

| ファイル | 役割 |
|---------|------|
| `Editor/StreamingAssetsManifestGenerator.cs` | マニフェスト生成（Editor専用） |
| `StreamingAssets/file_manifest.json` | 自動生成されるファイル一覧 |

#### マニフェスト形式

```json
{
    "files": [
        "Config/llm_response_schema.json",
        "Config/system_prompt_template.txt",
        "VRM/chr001_w001_model.vrm",
        "cron/example_morning_greeting.json",
        "cron/example_hourly_comment.json"
    ]
}
```

StreamingAssetsからの相対パス、フォワードスラッシュ統一。`.meta`ファイルとマニフェスト自身は除外。
`CronScheduler` は `cron/` プレフィックスのエントリをフィルタしてジョブファイルを読み込む。

#### 生成タイミング

- **手動**: メニュー `CyanNook > Generate StreamingAssets Manifest`
- **自動**: `IPreprocessBuildWithReport` でビルド前に自動実行（全プラットフォーム共通）

#### AvatarSettingsPanel のプラットフォーム分岐

```
RefreshVrmFileList()
├── WebGL (#if UNITY_WEBGL && !UNITY_EDITOR)
│   └── UnityWebRequest で file_manifest.json を非同期読み込み
│       → "VRM/*.vrm" エントリをフィルタしてDropdownに反映
└── Editor/Standalone (#else)
    └── Directory.GetFiles() で直接ファイル一覧取得（従来動作）
```

### 外部ホスティング時のlocalhost接続制限

WebGLアプリを外部サーバー（Unityroom等）でホスティングした場合、ブラウザのPrivate Network Access制限により
`localhost` / `127.0.0.1` へのAPIリクエストがブロックされる。Ollama・LM Studio等のローカルLLMは使用不可。

#### 利用可能なLLMプロバイダー（外部ホスティング時）

| プロバイダー | 動作 | 備考 |
|-------------|------|------|
| WebLLM | OK | ブラウザ内で完結（WebGPU） |
| OpenAI / Claude / Gemini | OK | クラウドAPIエンドポイント |
| Ollama / LM Studio | NG | localhostへの接続がブロックされる |
| Dify（クラウド） | OK | 公開エンドポイントなら可 |
| Dify（ローカル） | NG | localhostへの接続がブロックされる |

#### エラーハンドリング

`LLMClient.IsRemoteOriginWithLocalEndpoint()` で事前検出し、日英バイリンガルのエラーメッセージを表示する。
TestConnection・SendRequest・SendStreamingRequestの全経路で適用。

```
判定条件:
  WebGLビルド && Application.absoluteURLがlocalhost以外
    && config.apiEndpointがlocalhost/127.0.0.1
    && apiType != WebLLM
  → localhostブロックメッセージを表示
```

### VRM ランタイム読み込み（WebGL対応）

#### シェーダーの明示的包含

**問題**: UniVRMは `Shader.Find()` でランタイムにシェーダーを検索するが、WebGLビルドではシーン内のマテリアルが直接参照していないシェーダーがストリップ（除外）される。

**解決策**: `Editor/VrmShaderIncluder.cs` が以下のシェーダーを `Always Included Shaders`（Project Settings > Graphics）に追加する。

| シェーダー名 | 用途 |
|-------------|------|
| `VRM10/Universal Render Pipeline/MToon10` | URP環境のVRM MToonマテリアル |
| `VRM10/MToon10` | Built-in RP / VRM 0.xマイグレーション |
| `UniGLTF/UniUnlit` | Unlitマテリアル |

**初回セットアップ**: メニュー `CyanNook > Add VRM Shaders to Always Included` を1回実行。設定はプロジェクトに保存される。

#### VRM ライティング制御（Rendering Layer Mask + Culling Layer）

VRMモデルはStreamingAssetsからランタイムロードされるため、Editorで事前にレイヤー設定ができない。
`CharacterSetup` のフィールド（Inspector設定可能）で、ロード後に一括設定する。

##### Rendering Layer Mask（エディタ専用）

URP Rendering Layersによるライト-オブジェクト制御。
**WebGLビルドでは機能しない**（OpenGL ES非対応。URP 16.0公式ドキュメントに明記）。
エディタ（Direct3D）では正常に動作するため、開発時のプレビュー用途に使用可能。

```
CharacterSetup.OnVrmLoaded()
└── vrmRenderingLayerMask != 1（デフォルト以外）の場合
    └── vrmInstance.GetComponentsInChildren<Renderer>()
        └── 全Rendererの renderingLayerMask を設定
```

| 設定項目 | 説明 |
|---------|------|
| `vrmRenderingLayerMask` | ビットマスク値。Layer N = `1 << N`。複数レイヤーはOR結合（例: Layer 0 + Layer 2 = `1 + 4 = 5`） |

##### Culling Layer（WebGL対応）

`Light.cullingMask`（GameObjectのLayer基準）によるライト-オブジェクト制御。
**WebGLビルドでも機能する**。ただしURPレンダリングパスが**Forward**であることが必要（Forward+/Deferredでは `Light.cullingMask` が無視される）。

```
CharacterSetup.OnVrmLoaded()
└── vrmCullingLayer >= 0 の場合
    └── SetLayerRecursive(vrmInstance.gameObject, vrmCullingLayer)
        └── 全子GameObjectの layer を再帰的に設定
```

| 設定項目 | 説明 |
|---------|------|
| `vrmCullingLayer` | GameObjectのLayer番号（0-31）。-1で変更しない（デフォルト）。Tags and Layersで事前にLayerを作成しておく |

**用途**: キャラクター顔専用SpotLight/PointLightなど、VRMモデルだけに影響するライトを設定する際に使用。
ライトのCulling Maskで対象Layerを指定し、ライトの範囲（Range/Spot Angle）で顔への照射を制限する。

##### CharacterFaceLightController（顔ライトHeadボーン追従）

キャラクターの顔を照らすライトをHeadボーンに追従させるコントローラー（`Scripts/Character/`）。
`CharacterCameraController`と同様に`[DefaultExecutionOrder(20001)]`でLookAt後に実行される。

**注意**: CharacterCameraControllerの子オブジェクトにライトを配置すると、
オンデマンドレンダリング（`camera.enabled = false` → `camera.Render()`）のタイミングでライトが点滅する。
顔ライトは必ずこのコントローラーで独立して管理すること。

```
CharacterFaceLightController
├── faceLight              ← 顔用Light参照
├── positionOffset         ← Headボーンからのローカル位置オフセット (default: 0, 0.06, 0.3)
├── rotationOffset         ← 回転オフセット（度） (default: 0, 0, 0)
├── followHeadRotation     ← Headの回転に追従するか (default: true)
│
└── SetVrmInstance()       ← VRM読み込み時にHeadボーンを取得
    └── LateUpdate()       ← 毎フレームHeadボーン位置・回転に追従
```

| フィールド | 型 | デフォルト | 説明 |
|-----------|-----|---------|------|
| `faceLight` | `Light` | - | 顔用ライト（Inspector参照） |
| `positionOffset` | `Vector3` | `(0, 0.06, 0.3)` | Headボーンからのローカル位置オフセット |
| `rotationOffset` | `Vector3` | `(0, 0, 0)` | 回転オフセット（度）。SpotLight使用時にY=180で反転、X/Yで照射角度調整 |
| `followHeadRotation` | `bool` | `true` | trueならHeadの回転に追従、falseならワールド固定方向 |

##### RoomLightController（部屋ライト一括制御）

ライトのON/OFF + マテリアルEmission + ベイク済みLightmap連動を一括制御するコンポーネント（`Scripts/Furniture/`）。
初回起動演出・Sleep状態など、様々な場所から共通利用する。

```
RoomLightController
├── targetLights[]          ← 制御対象Light（空ならシーン内全Light自動取得）
├── emissionTargets[]       ← ON/OFF時にEmission切替するマテリアル
├── startOff                ← trueなら起動時から消灯（Awakeで即適用）
├── disableLightmapOnOff    ← ライトOFF時にベイク済みライトマップも無効化
│
├── SetLightsOn()           ← ライト点灯 + Emission ON + Lightmap復元
├── SetLightsOff()          ← ライト消灯 + Emission OFF + Lightmap無効化
└── SetLights(bool on)      ← 統合切替
```

| 設定項目 | 説明 |
|---------|------|
| `targetLights` | 制御対象Light配列。空ならFindObjectsByType<Light>で全Light自動取得 |
| `emissionTargets` | EmissionTarget配列。Renderer + materialIndex + HDR ON/OFFカラー |
| `startOff` | ONにすると起動時から消灯状態（最初のフレームから暗い状態を保証） |
| `disableLightmapOnOff` | ONにするとライトOFF時にLightmapSettingsを空にしてベイク済み照明を無効化。ON時にAwakeでキャッシュしたLightmapを復元（デフォルトON） |

EmissionTargetの各フィールド:

| フィールド | 説明 |
|-----------|------|
| `targetRenderer` | 対象Renderer |
| `materialIndex` | 対象マテリアルのインデックス（複数マテリアルの場合） |
| `onColor` | ライトON時のEmissionカラー（HDR対応） |
| `offColor` | ライトOFF時のEmissionカラー（HDR対応、デフォルト黒） |

##### レンダリングパス設定

| 項目 | 設定 |
|------|------|
| URP Rendering Path | **Forward**（必須） |
| 理由1 | `Light.cullingMask` がForwardでのみ機能する |
| 理由2 | MToon10シェーダーがDeferredで正しく描画されない |
| Forward+との差異 | ライト数上限（Forward: ~8/object）。Cyan-Nookの1部屋構成では問題なし |

#### AwaitCaller のプラットフォーム分岐

**問題**: UniVRMのデフォルト `RuntimeOnlyAwaitCaller` は `Task.Run()` でバックグラウンドスレッドを使用するが、WebGLはシングルスレッド環境のためスレッド生成に失敗する。

**解決策**: `VrmLoader.cs` でプラットフォームに応じた `IAwaitCaller` を明示指定。

```
Vrm10.LoadBytesAsync(awaitCaller: ...)
├── WebGL (#if UNITY_WEBGL && !UNITY_EDITOR)
│   └── RuntimeOnlyNoThreadAwaitCaller — メインスレッドで同期実行
└── Editor/Standalone (#else)
    └── RuntimeOnlyAwaitCaller — バックグラウンドスレッド使用
```

### Input System 型ストリッピング防止（link.xml）

**問題**: WebGLのIL2CPPビルドで、Input Systemの型がストリップ（除外）され、起動時に`TypeInitializationException`（`UnityEngine.InputSystem.InputSystem`）や`RuntimeError: function signature mismatch`が発生する。

**原因**: IL2CPPのマネージドコードストリッピングが、リフレクション経由でのみ使用される型を未使用と判断して除外する。Input Systemは内部でリフレクションを多用するため影響を受けやすい。

**解決策**: `Assets/link.xml` でInput Systemアセンブリの保持を宣言。

```xml
<linker>
  <!-- Input System: WebGL IL2CPP ビルドで型ストリッピングによる初期化エラーを防止 -->
  <assembly fullname="Unity.InputSystem" preserve="all"/>
  <assembly fullname="Unity.InputSystem.ForUI" preserve="all"/>
</linker>
```

### WebGLビルド手順チェックリスト

1. ✅ `CyanNook > Add VRM Shaders to Always Included` を実行済み（初回のみ）
2. ✅ `Assets/link.xml` が存在すること（Input System型ストリッピング防止）
3. ✅ StreamingAssets/VRM/ にVRMファイルを配置
4. ✅ Player Settings > WebGL Template で `CyanNook` を選択（WebLLM CDN読み込みはjslib内で行うためテンプレート選択は必須ではないが、UIレイアウトのため推奨）
5. ビルド実行（マニフェストは自動生成される）
6. HTTPS またはlocalhost でホスティング（Web Speech API / WebGPU等のセキュアコンテキストが必要）

### WebGL 日本語入力（IME）対応

**問題**: Unity WebGLではIMEが公式未サポートのため、日本語入力ができない。

**現在の解決策**: [WebGLInput (kou-yeung)](https://github.com/kou-yeung/WebGLInput) パッケージを使用。

- TMP_InputFieldに `WebGLInput` コンポーネントをアタッチすることでIME入力を有効化
- フルスクリーン対応済み（v1.3.3以降、`WebGLSupport.WebGLWindow.SwitchFullscreen()` を使用）
- モバイルサポートは実験的（入力パネルの表示不具合等の報告あり）

**対象InputField:**

| 分類 | InputField | 日本語入力 |
|------|-----------|-----------|
| チャット | UIController.chatInputField | 必須 |
| キャラクタープロンプト | AvatarSettingsPanel.characterPromptInputField | 必要 |
| レスポンスフォーマット | AvatarSettingsPanel.responseFormatInputField | 必要 |
| IdleChat | LLMSettingsPanel.idleChatMessageInputField | 必要 |
| Sleep夢プロンプト | LLMSettingsPanel.dreamPromptInputField | 必要 |
| Sleep起床メッセージ | LLMSettingsPanel.wakeUpMessageInputField | 必要 |
| テスト音声 | VoiceSettingsPanel.testTextInputField | 必要 |
| URL/APIキー/数値 | 各設定パネル | 不要（英数字のみ） |

### マルチラインInputField改行対応（MultiLineInputFieldFix）

**問題**: New Input Systemの`*/{Submit}`バインディングがEnterキーをグローバルにSubmitアクションとして消費し、TMP_InputFieldの`MultiLineNewline`設定が正常に機能しない。

**根本原因の詳細:**

1. Enterキー押下 → Input SystemがSubmitアクションを発火
2. `InputSystemUIInputModule`がフォーカス中のTMP_InputFieldにSubmitイベントを送信
3. TMP_InputFieldは`MultiLineNewline`設定でも`onEndEdit`を発火しフィールドを非アクティブ化
4. `MultiLineNewline`の場合、TMPが内部的に改行を挿入するが、`onEndEdit`の`text`パラメータは挿入前のテキスト。一方`caretPosition`はTMP内部の挿入後の位置を返すため、`text.Insert(caretPosition, "\n")`で`ArgumentOutOfRangeException`が発生しテキストが消失する

**解決策**: `MultiLineInputFieldFix`コンポーネント（`Scripts/UI/MultiLineInputFieldFix.cs`）

- `lineType`を`MultiLineSubmit`に上書き（TMPの内部改行処理を無効化し、改行挿入を完全にコンポーネントが制御）
- `Update()`でフォーカス中のキャレット位置を毎フレーム記録（`onEndEdit`発火時には非アクティブで`caretPosition`が0になるため）
- `onEndEdit`でEnterキーを検出し、記録済みキャレット位置に改行を手動挿入してフォーカスを維持
- 再アクティブ化後に`selectionAnchorPosition`/`selectionFocusPosition`を設定して全選択状態を解除

**使い方**: マルチライン対応したいTMP_InputFieldと同じGameObjectにAddComponentする。`[RequireComponent(typeof(TMP_InputField))]`付き。

**対象InputField:**

| パネル | InputField | 用途 |
|--------|-----------|------|
| AvatarSettingsPanel | `characterPromptInputField` | キャラクター設定プロンプト |
| AvatarSettingsPanel | `responseFormatInputField` | レスポンスフォーマットプロンプト |
| LLMSettingsPanel | `idleChatMessageInputField` | アイドルチャットメッセージ |
| LLMSettingsPanel | `dreamPromptInputField` | 夢メッセージプロンプト |
| LLMSettingsPanel | `wakeUpMessageInputField` | 起床時システムメッセージ |
| VoiceSettingsPanel | `testTextInputField` | テスト音声文章 |

> **注**: UIControllerの`chatInputField`はEnter=送信、Shift+Enter=改行という独自ハンドラを持つため、このコンポーネントは不要。WebGLでは`onValueChanged`でEnter（`\n`挿入）を検出し、Desktop/Editorでは`onEndEdit`をフォールバックとして使用する二重検出方式。

#### 将来対応: カスタムHTML Overlay方式（モバイル本格対応時）

WebGLInputのモバイル対応が不十分な場合、ブラウザネイティブの`<input>`/`<textarea>`要素をUnityキャンバス上にオーバーレイする方式に移行する。

**アーキテクチャ:**
```
TMP_InputField フォーカス検知（C#）
    → jslib経由でHTML input要素を生成・表示（JavaScript）
        → ブラウザネイティブIMEで入力
            → 確定テキストをSendMessage()でUnityに返却
                → TMP_InputField.textに反映
```

**必要な実装:**

| コンポーネント | 説明 |
|--------------|------|
| `WebGLIMEInput.jslib` | HTML input要素の生成・位置同期・テキスト返却 |
| `WebGLIMEInputBridge.cs` | C#側のjslib呼び出し・TMP_InputField連携 |
| カスタムWebGLテンプレート | overlay用のCSS・コンテナ定義 |

**メリット:**
- ブラウザネイティブIMEを使用するため、全プラットフォーム（PC/モバイル）で安定動作
- フルスクリーンの問題なし

**実装時の注意点:**
- Unity UIの座標とHTML要素のCSS位置を同期させる必要がある（Canvas Scaler考慮）
- 既存の `WebSpeechAPI.jslib` と同じPlugins/WebGL/に配置
- `#if UNITY_WEBGL && !UNITY_EDITOR` で分岐し、エディター上では通常のTMP_InputFieldを使用
