# Cyan Nook

デスクトップAIテラリウム — VRMキャラクターと会話できるWebアプリケーションです。

## 起動方法
**Build>start-server.bat**で起動し、ブラウザで開きます。
同時起動するPowerShellウインドウを閉じると終了します。

## はじめに

ブラウザでアクセスするだけで使えます。
会話するには **AIプロバイダー** の設定が必要です。以下の2つの方法から選んでください。

---

## AIプロバイダーの設定

### 方法1: LM Studio（ローカルLLM）

PCでLLMを動かす方法です。APIキー不要・無料で使えますが、ある程度のPCスペックが必要です。

#### 手順

1. [LM Studio](https://lmstudio.ai/) をダウンロード・インストール
2. LM Studioを起動し、好きなモデルをダウンロード
   - おすすめ: `Qwen3-VL-4B-Instruct-GGUF` など日本語対応の軽量モデル
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
   - **Endpoint**: 自動入力されます
   - **Model**: `gemini-2.0-flash`（推奨）
   - **API Key**: 取得したAPIキーを入力
4. チャット欄からメッセージを送信して動作確認

> **注意**: Gemini APIには無料枠がありますが、大量に使うと課金が発生する場合があります。テスト利用であれば無料枠内で十分です。

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
6. **Speaker** ドロップダウンからボイスを選択

> VOICEVOXを先に起動してからCyan Nookの設定画面を開くと、Speakerの一覧が自動で読み込まれます。

> **クレジット表記について**: VOICEVOXの音声を使用した動画・配信等を公開する場合、各キャラクターの利用規約に従ったクレジット表記が必要です。詳しくは [VOICEVOX公式サイト](https://voicevox.hiroshiba.jp/) の各キャラクターページをご確認ください。

---

## 基本操作

| 操作 | 説明 |
|------|------|
| チャット入力欄 | メッセージを入力してEnter（またはSendボタン）で送信 |
| マイクボタン（🎤） | チャット欄横のボタンで音声入力のON/OFFを切り替え |
| 設定メニュー（⚙） | 画面上部のアイコンから各種設定パネルを開く |

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
