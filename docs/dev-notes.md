# 開発ノート

コードやコミット履歴からは読み取れない、開発上の知見・運用注意をまとめたもの。
設計仕様は [unity_client/Assets/DESIGN.md](../unity_client/Assets/DESIGN.md)、
プロジェクト規約は [CLAUDE.md](../CLAUDE.md) を参照。

## アーキテクチャ上の原則

### 自動リクエストの可否判定は必ず `ChatManager.IsBusy` を使う

`ChatState == Idle` だけで判定してはいけない。応答処理は LLMClient のコルーチン内
コールバックで走るため、「ChatState は Idle だが LLMClient は処理中」という窓があり、
そこで `SendAutoRequest` すると黙って弾かれてプロンプトが失われる
（過去に WaitingForResponse 固着の原因になった）。

### 自動メッセージが弾かれたら「保留 → Update でリトライ」

夢メッセージ・外出メッセージで採用している方式。ビジー時にスキップすると
メッセージが失われるため、保留フラグを立てて Update で IsBusy が解けるのを待つ。
新しい自動送信系を作る場合も同じパターンに揃えること。

### 設定の復元は「使う側」の責務

PlayerPrefs からの設定復元は、常時アクティブなコントローラーの Awake/Start で行う。
設定パネルは「表示と保存」のみ。パネルの初期アクティブ状態に復元を依存させると、
パネルを開くまで設定が反映されないバグになる。

### 一時停止系は集約ヘルパーを経由する

IdleChat / 退屈度の一時停止は Sleep / Outing 双方の `SetAutonomyPaused` に
集約されている。一時停止対象のシステムを増やすときは**両方のヘルパーに追加**する。

### `[SerializeField]` のリネームは FormerlySerializedAs を添える

シーン・プレハブの割当値が失われる。全シーンを再保存して反映を確認するまで
属性を消さないこと。

## WebGL 固有の注意

### 毎フレームアロケーションは禁止

WebGL にはインクリメンタル GC がなく、GC が一括で走るためスパイク（カクつき）に
直結する。特に以下は既知の罠:

- `agent.path.corners` — アクセスごとに NavMeshPath + Vector3[] を new する。
  直線判定は `steeringTarget` と `pathEndPosition` の比較で代替（アロケなし）
- `ToLower()` / 文字列連結 — 毎フレーム経路では辞書を `StringComparer.OrdinalIgnoreCase`
  にする等で回避
- `GetOutputData` 等のバッファは new せずフィールドで使い回す

### web-llm（ブラウザ内 LLM）のストリーム処理

- `for await` ループを途中で `return` すると**エンジンのロックが解放されず、
  次回の生成が永久に待つ**（デッドロック）。中断したい場合も `continue` で
  自然に最後まで読み捨てること
- `interruptGenerate()` は非同期のフラグ操作でしかない。世代カウンター
  （requestId）で古い通知を破棄する構造を維持すること

### ビルドでの情報ログは既定で抑止されている

`LogVerbosityController` がビルドの `Debug.Log` を遮断している
（文字列ゴミによる GC スパイク対策）。**ビルドでログ調査をするときは、
先にデバッグ設定パネルの Verbose Logs を ON にしてから再現 → エクスポート**。

## unityroom ホスティング

### `settings.json` という名前のファイルは配信されない

unityroom はこのファイル名を 404 でブロックする（`.json` 全般は配信 OK）。
Unity Addressables はこの名前をハードコードしているため、unityroom 版では
Addressables / Localization が初期化できない。対策として unityroom 版は
日本語固定とし、`CyanNook > Localization > Bake Japanese to Active Scene TMPs`
でシーンに焼き付けてからビルドする。詳細は DESIGN.md 参照。

### unityroom 版は「体験版」

Gemini（内蔵既定キー）+ WebLLM のみ。キー/エンドポイント入力・外部アクション
フィード・Cron・WebCam/画面キャプチャ・設定 Import は `UNITYROOM_BUILD` で封鎖。

- **封鎖は二重防御が原則**: UI 非表示（各パネルの `hideOnUnityroomBuild` 配列）
  + コントローラー側の `#if UNITYROOM_BUILD` 実行停止。UI を隠すだけでは
  PlayerPrefs 復元・Import・autoPlay 既定値から動いてしまう
- **新機能を追加するときは unityroom 版で封鎖すべきか必ず検討する**
- 既定キー・モデル名等は `Resources/UnityroomConfig.asset`（gitignore 対象）で管理。
  Gemini TTS のモデルもここで指定する

### リリース手順の罠: define の戻し忘れ

`Switch to Unityroom Build` ⇔ `Switch to GitHub Build` の切替後は
**File > Save Project を実行**しないと ProjectSettings.asset に define が残留する。
リリース後は `git diff unity_client/ProjectSettings/ProjectSettings.asset` が
空であることを確認する。手順全体は `.claude/skills/unityroom-release/` にチェックリスト化済み。

## ローカルサーバー（build/server.ps1）

`/proxy/{URL}` の転送先は `build/proxy-allowlist.txt` のホワイトリスト制
（SSRF 対策）。Dify 等の独自転送先を使う場合はこのファイルに1行追記する
（再起動不要で反映）。

## 将来構想: マルチキャラクター（2〜3体の同居・掛け合い）

「キャラ同士の関係性」を見せ場にする拡張構想。
現在は保留中だが実施予定。着手時の起点として要点を残す。

**設計制約（決定済み）**: 最大3体 / キャラごとに別プロバイダー可 /
同居必須（関係性が育つ場） / ターン制で十分（同時動作不要）。

**方針**: キャラ毎に独立インスタンス（ChatManager × N、LLMClient × N、
CharacterController × N）を prefab 化。キャラ間会話のオーケストレーションと
関係性の状態管理（好感度等）は Dify 側に逃がし、アプリは
「各キャラの応答を受け取って動かす」ことに専念する（Soul/Shell 哲学に整合）。

**現状コードの重大ボトルネック（調査済み・要対応順）**:

1. 会話状態の単一性 — `ChatManager._conversationHistory`、Dify の
   `_conversationId`、PlayerPrefs キー `conversation_history` がいずれも1つ
2. `CharacterController` がイベントの宛先を判別できない（キャラ毎
   インスタンス化で自然解決する見込み）
3. `FurnitureInstance` の占有が単一ユーザー前提（`bool isOccupied` +
   単一 `currentUser`）— List 化 + 競合解決が必要
4. `VrmLoader` が単一 `_currentVrmInstance` — Dictionary 化が必要
5. PlayerPrefs キーが固定文字列で散在 — character_id での namespace 化

物理表現レイヤー（NavMeshAgent・各種コントローラー・DynamicTarget）は
概ね素直に複数化できることを確認済み。問題は会話・状態管理の上位レイヤーに集中。

**その他の留意点**: カメラの追跡対象切替、TTS の排他制御、
`spatial_context` に他キャラ位置を含める拡張。

## AI 支援開発の運用（Claude Code）

- 修正フローは `.claude/skills/fix-item/`、エディタ再生テストは
  `.claude/skills/smoke-test/`、コードレビューは `.claude/agents/unity-csharp-reviewer.md`
  に定型化されている
- Unity MCP でのシーン編集は事故りやすい: GameObject の duplicate で
  scale / localPosition が壊れることがある、ドメインリロードで instance ID が
  変わる、新規 .cs はアセット再インポート（force refresh）まで型が見えない。
  大きなレイアウト調整は人間側で行う分担が安全
- エディタ再生テストで LLM 対話を検証するときは、IdleChat / 退屈度を
  `SetPaused(true)`（ランタイムのみ）で止めないと自律発話が割り込んで
  応答の切り分けができなくなる
