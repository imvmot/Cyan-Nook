# CLAUDE.md - Cyan-Nook Project Guide

## Project Overview

Unity WebGLベースのVRMキャラクターチャットボットアプリケーション。
詳細な設計仕様は [unity_client/Assets/DESIGN.md](unity_client/Assets/DESIGN.md) を参照。

## 開発者の情報
開発者はベテランの3Dゲームアニメーターです。
アニメーション以外のモデルや背景、エフェクトについてもある程度の知識があり、
10年以上ゲーム開発の経験がありUnityでのアセット実装作業や簡単なパラメータ調整の経験もあります。
しかしながらプログラミング言語については初級～中級以下の知識しかありません。

## Tech Stack

* **Engine:** Unity 6 (or 2022+).
* **Model Format:** VRM 1.0 (UniVRM).
* **Animation:** Humanoid (Blender-exported clips).
* **Architecture:**
    * **Multi-Scene / Prefab-based Environment:** Separation of System, Character, and Background.
    * **Performance:** Optimized for Local LLM execution (Low GPU usage for rendering). Target 60 FPS for idle states.

## Directory Structure

```
Cyan-Nook/
├── unity_client/
│   └── Assets/
│       ├── Scripts/
│       │   ├── Core/           # 基盤（EmotionData, AnimationStateType等）
│       │   ├── Character/      # キャラクター制御（CharacterSetup, InteractionController, TalkController, SleepController等）
│       │   ├── Camera/         # カメラ制御（FOV, LookAt）[namespace: CyanNook.CameraControl]
│       │   ├── Chat/           # LLM通信（マルチプロバイダー、ストリーミング対応）
│       │   ├── Furniture/      # 家具システム
│       │   ├── Timeline/       # カスタムTimelineトラック（ループ、キャンセル、慣性補間等）
│       │   ├── UI/             # UI制御（UIController, 設定パネル群）
│       │   ├── Voice/          # 音声合成（VOICEVOX）・音声入力（Web Speech API）
│       │   ├── Animation/      # アニメーションユーティリティ
│       │   ├── Utilities/      # 汎用ユーティリティ
│       │   ├── DebugTools/     # デバッグ用コンポーネント（DebugKeyController）
│       │   └── CyanNook.asmdef
│       ├── Editor/             # エディタ拡張
│       │   └── CyanNook.Editor.asmdef
│       ├── Animations/
│       │   └── {character}/    # キャラクターごと
│       │       ├── FBX/        # Blenderエクスポート
│       │       ├── Clips/      # 抽出されたAnimationClip
│       │       ├── Timelines/  # Timelineアセット
│       │       └── *_TimelineBindings.asset
│       ├── StreamingAssets/
│       │   └── VRM/            # VRMファイル配置
│       ├── Scenes/
│       └── DESIGN.md           # 設計ドキュメント
└── CLAUDE.md                   # このファイル
```

## Key Classes

### Character System (`Scripts/Character/`)

| Class | Responsibility |
|-------|----------------|
| `VrmLoader` | VRM読み込み、コンポーネントセットアップ |
| `CharacterAnimationController` | Timeline再生、ステート管理 |
| `CharacterExpressionController` | VRM Expression（表情）制御（Facial Timeline駆動/直接制御） |
| `CharacterLookAtController` | VRM LookAt（視線）制御 |
| `CharacterCameraController` | Vision用カメラ（Headボーン追従・画像キャプチャ） |
| `CharacterNavigationController` | 移動・回転制御 |
| `InteractionController` | インタラクション状態管理 |
| `TalkController` | Talkモード状態管理 |
| `SleepController` | 睡眠状態管理・夢タイマー・起床処理 |
| `DynamicTargetController` | 動的ターゲット（clock/distance/height） |
| `RoomTargetManager` | 名前付きターゲット管理（mirror, window等） |
| `LipSyncController` | リップシンク統合（TextOnly/Mora/Simulated/Amplitude） |
| `CharacterController` | LLMレスポンスのaction/target/emote/口パクルーティング |
| `CharacterSetup` | VRM読み込み・全コンポーネント初期化・接続 |

### Data Classes (`Scripts/Core/`)

| Class | Description |
|-------|-------------|
| `EmotionData` | happy, relaxed, angry, sad, surprised (0.0-1.0) |
| `AnimationStateType` | Idle, Walk, Run, Talk, Emote, Interact |
| `TimelineBindingData` | ステート↔Timeline↔Clipマッピング (ScriptableObject) |

### Camera Control (`Scripts/Camera/`, namespace: `CyanNook.CameraControl`)

| Class | Description |
|-------|-------------|
| `DynamicCameraController` | MainCamera動的制御（FOV距離連動、Y軸ルックアット） |

### Chat System (`Scripts/Chat/`)

| Class | Description |
|-------|-------------|
| `ChatManager` | プロンプト生成、会話履歴管理、ストリーミング対応、Vision画像取得 |
| `LLMClient` | LLM通信統合管理、ILLMProvider切替、ストリーミング両方式対応 |
| `ILLMProvider` | LLMプロバイダー共通インターフェース |
| `OllamaProvider` / `LMStudioProvider` / `DifyProvider` / `OpenAIProvider` / `ClaudeProvider` / `GeminiProvider` | 各APIプロバイダー実装 |

### Voice System (`Scripts/Voice/`)

| Class | Description |
|-------|-------------|
| `VoiceInputController` | 音声入力統合管理（WebSpeechRecognition + VAD + UI同期） |
| `VoiceActivityDetector` | 無音検出・自動送信 |
| `WebSpeechRecognition` | Web Speech API C#ラッパー（WebGL専用） |

### UI (`Scripts/UI/`)

| Class | Description |
|-------|-------------|
| `UIController` | チャット入出力・ストリーミング表示・入力モード切替・マイクボタン |
| `SettingsMenuController` | 上部アイコンメニューバー、パネル開閉アニメーション |
| `AvatarSettingsPanel` | アバター設定（VRM選択、カメラ、プロンプト、退屈レート、Save/Reload） |
| `LLMSettingsPanel` | LLM設定（API設定、Vision、IdleChat、Sleep、WebCam） |
| `VoiceSettingsPanel` | 音声設定（VOICEVOX音声合成 + Web Speech API音声入力） |
| `DebugSettingsPanel` | デバッグ設定（デバッグキー、JSONモード、Raw Text表示、設定Import/Export） |

### DebugTools (`Scripts/DebugTools/`)

| Class | Description |
|-------|-------------|
| `DebugKeyController` | デバッグキー一括管理（W/A/D歩行、C/V Talk、F/G/Hインタラクション） |

## Naming Conventions

### Animation Files
```
{character}_{type}_{category}_{action}{variation}_{state}
例: chr001_anim_common_idle01_lp

State: st(start) / lp(loop) / ed(end)
Category: common / talk / emote / interact
```

### VRM Files
```
{character}_{wardrobe}_{type}.vrm
例: chr001_w001_model.vrm
配置: Assets/StreamingAssets/VRM/
```

## Editor Menu Commands

### CyanNook > Animation
- **Extract All Animation Clips** - 全FBXからClip抽出
- **Extract Clips for Selected Character** - 選択キャラクターのClip抽出
- **Create Timelines for Character** - Timeline + TimelineBindingData生成

### CyanNook
- **Setup VRM Test Scene** - テストシーン自動構築

## JSON Schema (LLM Response)

```json
{
  "emotion": {
    "happy": 0.0,
    "relaxed": 0.0,
    "angry": 0.0,
    "sad": 0.0,
    "surprised": 0.0
  },
  "reaction": "短い相槌",
  "action": "move | interact_sit | interact_sleep | interact_exit | ignore",
  "target": {
    "type": "talk | interact_* | dynamic | {room_target_name}",
    "clock": 12,
    "distance": "near | mid | far",
    "height": "high | mid | low"
  },
  "emote": "Neutral | happy01 | relaxed01 | angry01 | sad01 | surprised01",
  "sleep_duration": 30,
  "message": "メッセージ本文"
}
```

## Development Notes

### Animation System
- **Animator Controllerは使わない** - Timeline + PlayableDirectorで制御
- AnimationTrackは実行時にVRMのAnimatorにバインドされる
- TimelineにClipが配置されていないとT-poseになる

### Input System
- プロジェクトは**新Input System専用**
- UIには`InputSystemUIInputModule`を使用（StandaloneInputModuleは不可）

### VRM
- VRM 1.0形式（UniVRM10）
- `Vrm10Instance`からExpression/LookAtにアクセス
- VRM読み込みは非同期（`LoadVrmAsync`）

### TextMeshPro
- デフォルトフォント（LiberationSans SDF）は日本語非対応
- 日本語表示には日本語対応フォントアセットが必要

## Current Implementation Status

### Implemented
- [x] VRM読み込み・表示
- [x] Timeline駆動アニメーション再生（ループ、キャンセル、慣性補間、Facial Timeline）
- [x] VRM Expression（表情）制御
- [x] VRM LookAt（視線）制御
- [x] LLM通信（マルチプロバイダー：Ollama, LM Studio, Dify, OpenAI, Claude, Gemini）
- [x] ストリーミングレスポンス（逐次フィールドパース・表示）
- [x] 家具システム（FurnitureManager）
- [x] ナビゲーション移動（NavMesh + DynamicTarget）
- [x] インタラクション（sit, sleep, exit/entry）
- [x] Talk モード（移動→対面→会話）
- [x] 睡眠システム（夢タイマー・自動起床）
- [x] 退屈システム（BoredomController）
- [x] Vision（キャラクター視点カメラ + WebCam + ScreenCapture）
- [x] 音声合成（VOICEVOX連携）
- [x] 音声入力（Web Speech API + VAD）
- [x] 本番UI（設定パネル群、マイクボタン）
- [x] Cronスケジューラー（定期LLMリクエスト）
- [x] 設定Import/Export
- [x] エディタ拡張（Clip抽出、Timeline生成、シーン構築）
- [x] WebGLビルド対応

## Assembly Definitions

### CyanNook.asmdef
参照: Unity.Timeline, UniVRM10, Unity.TextMeshPro

### CyanNook.Editor.asmdef
参照: CyanNook, Unity.Timeline, Unity.TextMeshPro, Unity.InputSystem


## 1. Project Identity
* **Project Name:** Cyan Nook 
* **Concept:** A "Desktop AI Terrarium." Not just a tool, but a semi-autonomous digital entity living in a corner of the screen.

## 2. The "WHY" (Core Philosophy)
* **Anti-Efficiency:** Unlike typical AI agents designed for speed, this project values "presence" and "co-existence."
* **The "Nook" Concept:** The application acts as a "window" or "porthole" into a small digital room (Vignette) where the AI resides. The user observes the AI's life.
* **Inconvenience as a Feature:** Latency in Local LLM responses is interpreted as "thinking time" or "deep thought." The AI has its own pace.
* **Separation of Soul and Shell:**
    * **Ghost (Soul):** The Local LLM / AI Logic.
    * **Shell (Body):** VRM Avatar (Default: "Shian").
    * The system acts as a container (Nook) where the Ghost possesses the Shell.

## 3. Character & World Settings
* **Default Character:** "Shian" (Cyan).
    * **Visuals:** Cyan (#00FFFF) based color palette. 150cm height scale.
    * **Personality:** Intellectual, sometimes bored, lives in the electronic sea.
* **Atmosphere:**
    * **Visual Style:** Digital, clean, anime-style, cyber-aesthetic but cozy.
    * **Font:** M+ FONTS (Modern, geometric, clean).
    * **Environment:** Minimalist "Vignette" style (e.g., just a floor and a chair in a void).

## 4. Key Mechanics & Behavior
* **Idle System:** The AI should have rich "Boredom" behaviors (looking around, stretching, reading logs) when not interacting with the user.
* **Reaction Latency:** Do not hide the loading time. Visualize the "Thinking" state as part of the character's acting.
* **Window to the World:** The application window is a "Glass Nook." The user is an observer from the outside.