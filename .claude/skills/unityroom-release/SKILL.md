---
name: unityroom-release
description: unityroom 版（体験版）のリリース手順をチェックリストで実行する。unityroom へのビルド・アップロード準備のとき使う。
---

# unityroom 版リリース手順

unityroom 版は Gemini 内蔵キー+WebLLM のみの体験版（UNITYROOM_BUILD で機能封鎖）。
以下を順に実行し、ユーザー操作が必要なステップは依頼して完了を待つ。

## 1. 事前確認

- `CyanNook > Build > Create or Open Unityroom Config` で UnityroomConfig.asset を確認:
  - geminiApiKey が設定されている（HasDefaultApiKey）
  - geminiModelName / geminiEndpoint / geminiTtsModel が現行の想定値
- git status がクリーンに近いこと（リリースに含めたくない変更が混ざらないように）

## 2. Unityroom ビルドへ切替

- `CyanNook > Build > Switch to Unityroom Build`（UNITYROOM_BUILD ON）
- 切替後、コンパイルエラーがないことを read_console で確認

## 3. 日本語ベイク

- main シーンを開く
- `CyanNook > Localization > Bake Japanese to Active Scene TMPs` を実行
- 背景: unityroom は `settings.json` という名前のファイル配信を 404 でブロックするため
  Addressables/Localization が初期化できず、日本語をシーンに焼き付ける必要がある
- ベイク済み TMP は GitHub 版でも runtime の LocalizeStringEvent が上書きするため、
  main.unity の差分はコミットして問題ない

## 4. WebGL ビルド

- ユーザーに WebGL ビルドを依頼（出力先: `unityroom/build/`、ビルド名: unityroom）
- ビルド完了後、リポジトリルートの `prepare_unityroom.bat` を実行
  （.unityweb → .gz へのリネーム。unityroom の配信仕様に合わせるため）

## 5. アップロード（ユーザー作業）

- unityroom にアップロードできるのは **Build フォルダ4ファイル + StreamingAssets のみ**
  （index.html は差し替え不可）

## 6. 動作確認チェックリスト

アップロード後、unityroom 上で以下を確認するようユーザーに依頼:

- [ ] チャットが内蔵キーの Gemini で動く
- [ ] LLM 設定: API Type が Gemini/WebLLM の2択、キー/エンドポイント/モデル名の入力欄がない
- [ ] External Action Feed / Cron / WebCam / 画面キャプチャのセクションが表示されない
- [ ] デバッグ設定: Import ボタンがない（Export はある）
- [ ] 音声設定: VOICEVOX がなく、Gemini TTS が内蔵キーで鳴る
- [ ] UI が日本語表示（`settings.json` の 404 と `[LocaleSelector] No available locales` ログは想定内）

## 7. 後片付け（重要・忘れやすい）

- `CyanNook > Build > Switch to GitHub Build`（UNITYROOM_BUILD OFF）
- **File > Save Project を実行**（忘れると ProjectSettings.asset のメモリ上の変更が
  ディスクに反映されず、次の define 切替往復で UNITYROOM_BUILD が残留する）
- `git diff unity_client/ProjectSettings/ProjectSettings.asset` が空であることを確認
- コンパイルが GitHub 版で通ることを確認
