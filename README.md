# Cyan Nook

AIチャットボット＋マスコットアプリ — VRMキャラクターと会話できるWebアプリケーションです。

## 起動方法
**Build>start-server.bat**で起動し、ブラウザで開きます。
同時起動するPowerShellウインドウを閉じると終了します。

## ブラウザAIの使用について

初回起動時にブラウザAI(WebLLM)の使用を選択できます。
ただし、軽量モデル(Qwen3-1.7B)のため挙動が不安定で会話も限定的です。
あくまで動作確認として使用してください。

---

## AIプロバイダーの設定

会話するには **AIプロバイダー** の設定が必要です。以下の2つの方法から選んでください。


### 方法1: LM Studio（ローカルLLM）

PCでLLMを動かす方法です。APIキー不要・無料で使えますが、ある程度のPCスペックが必要です。

#### 手順

1. [LM Studio](https://lmstudio.ai/) をダウンロード・インストール
2. LM Studioを起動し、好きなモデルをダウンロード
   - おすすめ: `Qwen3-VL-4B-Instruct-GGUF` など日本語対応の軽量モデル(Qwen3.5系はThink無効化できず挙動が不安定なため非推奨です)
3. LM Studioの **Local Server** タブでサーバーを起動（Startボタン）
   - デフォルトで `http://localhost:1234` で起動します
   - **Server Settings** で **Enable CORS** を有効にしてください（ブラウザからの接続に必要です）
4. Cyan Nookの **上部設定アイコン> LLM** を開く
5. 以下を設定:
   - **API Type**: `LM Studio`
   - **Endpoint**: `http://localhost:1234/v1/chat/completions`（自動入力されます）
   - **Model**: LM Studioで読み込んだモデル名を入力
6. チャット欄からメッセージを送信して動作確認

#### ネットワーク越しに接続する場合

LM Studioが別のPCで動いている場合は、EndpointのIPアドレスを変更してください。
例: `http://192.168.1.100:1234/v1/chat/completions`

> LM Studio側で **Serve on Local Network** を有効にする必要があります。

---

### 方法2: Gemini（Google AI）

Google APIキーを使う方法です。PCスペックに依存せず使えます。

#### 手順

1. [Google AI Studio](https://aistudio.google.com/apikey) にアクセスしてAPIキーを取得
   - Googleアカウントでログインし、「APIキーを作成」
2. Cyan Nookの **上部設定アイコン> LLM** を開く
3. 以下を設定:
   - **API Type**: `Gemini`
   - **Endpoint**: https://generativelanguage.googleapis.com/v1beta
   - **Model**: `gemini-2.0-flash`（推奨）
   - **API Key**: 取得したAPIキーを入力
4. チャット欄からメッセージを送信して動作確認

> **注意**: Gemini APIには無料枠がありますが、大量に使うと課金が発生する場合があります。テスト利用であれば無料枠内で十分です。

---

### 方法3: Dify（上級者向け）

[Dify](https://dify.ai/) を使ってプロンプトやワークフローを自由にカスタマイズする方法です。
セルフホストまたはDify Cloudで動作します。会話履歴・プロンプトはDify側で管理されます。

#### 手順

1. Difyでチャットボットアプリを作成
2. アプリの「変数」に以下の入力変数を追加（Cyan Nookが自動送信します）:

| 変数名 | Dify型 | 推奨最大長 | 内容 |
|--------|--------|-----------|------|
| `current_datetime` | テキスト入力 | 256 | 現在日時（例: `2026-03-20 15:30 (Thu)`） |
| `current_pose` | テキスト入力 | 256 | キャラクターの現在のアクション（`ignore`, `move`等） |
| `current_emotion` | テキスト入力 | 256 | 現在の感情（`neutral`, `happy`等） |
| `available_furniture` | 段落 | 2048 | 利用可能な家具リスト |
| `available_room_targets` | 段落 | 2048 | 利用可能な部屋ターゲットリスト |
| `spatial_context` | 段落 | 2048 | 空間コンテキスト（JSON） |
| `bored` | テキスト入力 | 256 | 退屈度（0〜100の整数） |
| `visible_objects` | 段落 | 2048 | キャラクターの視界内オブジェクト（Vision有効時） |

3. Difyアプリの「公開」からAPIキーを取得
4. Cyan Nookの **上部設定アイコン > LLM** を開く
5. 以下を設定:
   - **API Type**: `Dify`
   - **Endpoint**: `http://<Difyサーバー>/v1`（例: `http://localhost/v1`）
   - **API Key**: DifyアプリのAPIキー
6. チャット欄からメッセージを送信して動作確認

> **注意**: Difyモードではシステムプロンプトと会話履歴はDify側で管理されます。Cyan Nookの設定パネルで入力したプロンプトは使用されません。
> Vision（画像送信）を使用する場合は、Difyアプリの「機能」でビジョンを有効にしてください。

---

## プロンプトのカスタマイズ

**上部設定アイコン > Avatar** パネルからプロンプトを編集できます。

- **Character Prompt**: キャラクターの性格や口調を自由に設定できます。ここを書き換えることで、キャラクターの振る舞いを変えられます。
- **Response Format Prompt**: アバター制御に必要なJSON構文の定義です。編集を誤るとキャラクターが正常に動作しなくなるため、通常はロックされています。ロック解除して編集する場合は十分注意してください。

> **注意**: Difyモードではこれらのプロンプトは使用されません。Dify側のアプリ設定で管理してください。

---

## 音声読み上げの設定（VOICEVOX）

キャラクターの返答を音声で読み上げさせたい場合に設定します。
設定しなくてもテキストチャットは使えます。

#### 手順

1. [VOICEVOX](https://voicevox.hiroshiba.jp/) をダウンロード・インストール
2. VOICEVOXを起動する（起動するだけでOK、内部でAPIサーバーが立ち上がります）
3. Cyan Nookの **上部設定アイコン > Audio** を開く
4. **TTS Engine** で `VOICEVOX` を選択
5. **VOICEVOX URL** がデフォルト（`http://localhost:50021`）になっていることを確認
6. **Test Connection** ボタンを押して接続を確認
7. 接続成功後、**Speaker** ドロップダウンからボイスを選択

> VOICEVOXを先に起動した状態で **Test Connection** を押すと、Speakerの一覧が読み込まれます。

> **クレジット表記について**: VOICEVOXの音声を使用した動画・配信等を公開する場合、各キャラクターの利用規約に従ったクレジット表記が必要です。詳しくは [VOICEVOX公式サイト](https://voicevox.hiroshiba.jp/) の各キャラクターページをご確認ください。

---

## 基本操作

| 操作 | 説明 |
|------|------|
| チャット入力欄 | メッセージを入力してEnter（またはSendボタン）で送信 |
| マイクボタン（🎤） | チャット欄横のボタンで音声入力のON/OFFを切り替え |
| 設定メニュー| 画面上部のアイコンから各種設定パネルを開く |

---

## カスタムアバターの読み込み

`StreamingAssets/VRM/` フォルダに VRM 1.0 形式のアバターファイル（`.vrm`）を配置すると、**上部設定アイコン > Avatar** パネルの **VRM Model** プルダウンから選択・読み込みできます。

#### 必要なExpression

アバターの表情・視線・口パクを正常に動作させるには、VRMに以下のExpressionが設定されている必要があります。

| 種類 | Expression名 | 用途 |
|------|-------------|------|
| 感情 | `happy`, `angry`, `sad`, `relaxed`, `surprised` | LLMレスポンスに応じた表情変化 |
| 瞬き | `blink`, `blinkLeft`, `blinkRight` | 自動まばたき |
| リップシンク | `aa`, `ih`, `ou`, `ee`, `oh` | 音声合成時の口パク |
| ルックアット | `lookUp`, `lookDown`, `lookLeft`, `lookRight` | 視線制御 |

> Expressionが不足していても読み込み自体は可能ですが、対応する機能が動作しません。

---

## トラブルシューティング

| 症状 | 対処 |
|------|------|
| メッセージを送っても返答がない | AIプロバイダーの設定を確認。LM Studioならサーバーが起動しているか確認 |
| VOICEVOXの声が出ない | VOICEVOXアプリが起動しているか確認。URLが `http://localhost:50021` か確認 |
| キャラクターがT-poseのまま | ページを再読み込みしてください |
| 設定が消えた | ブラウザのキャッシュ/データを消去すると設定もリセットされます |

---

## 動作環境

- **ブラウザ**: Chrome推奨（WebGL対応ブラウザ）
- **LM Studio使用時**: VRAM 4GB以上推奨（2Bモデルの場合）
- **VOICEVOX使用時**: VOICEVOXアプリが同じPCまたはLAN内で起動していること
