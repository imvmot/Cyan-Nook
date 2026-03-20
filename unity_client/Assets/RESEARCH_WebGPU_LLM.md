# WebGPU ブラウザ内LLM推論 - 将来検討資料

> 作成日: 2026-02-19
> 最終更新: 2026-03-15
> ステータス: 実装検討段階（基本機能がほぼ完成、公開準備中）

## 背景

Cyan-Nookは現在、外部LLMサーバー（Ollama / LM Studio / Dify）にHTTP通信する構成。
ユーザーがLLMサーバーをインストール・起動する必要があり、セットアップ負荷が高い。

WebGPUを使ったブラウザ内LLM推論により、外部サーバー不要の完全スタンドアロン構成が技術的に可能になりつつある。

### 目的

公開サイトへの**初回アクセスで、LLMやAPIの知識が無くてもとりあえず会話できる「お試し機能」**として、
2Bクラスのモデルをブラウザ内で動かす。

### 参考プロジェクト

- **Office Sim** (Noumena Labs): https://noumenalabs.itch.io/office-sim
  - Unity + WebGPU でブラウザ内LLM推論を実現した実験的プロジェクト
  - 「高頻度・低レイテンシのインタラクション」を目的としている
  - 全エッジデバイス/プラットフォーム（Unity, Godot, three.js等）向けの推論エンジンを目指している
  - 2026年2月時点でプロトタイプ段階、iOSでは動作しなかった

---

## 推論フレームワーク比較

### WebLLM (MLC-AI) — 最有力候補

- リポジトリ: https://github.com/mlc-ai/web-llm
- ドキュメント: https://webllm.mlc.ai/docs/
- **最新バージョン: v0.2.82**（2025年3月13日リリース）
- WebGPU + WebAssembly のハイブリッド構成
- OpenAI互換API（chat/completions、function calling、embeddings）
- NPM / CDN で導入可能（`@mlc-ai/web-llm`）

#### v0.2.81-v0.2.82の主要変更

- `ChatModule` → `Engine`/`MLCEngine` にリファクタリング
- マルチモデル同時読み込み対応
- **XGrammar統合** — JSON Schema/EBNF/regexによる構造化出力をほぼ確実に保証
  - Cyan-NookのJSON応答スキーマに直接使用可能
  - デコード時のトークン制約で**100%の構造的正確性**
  - デモ: https://huggingface.co/spaces/mlc-ai/WebLLM-Structured-Generation-Playground
- IndexedDBキャッシュ（再訪時のダウンロード不要）
- ServiceWorkerEngine（オフライン対応）

#### WebLLM公式対応モデル（3B以下）

| モデル | サイズ | 量子化 | 備考 |
|--------|--------|--------|------|
| Qwen3-1.7B | 1.7B | q4f16_1, q4f32_1 | 公式対応 |
| Qwen3-0.6B | 0.6B | q4f16_1, q4f32_1 | 超軽量 |
| Qwen2.5-1.5B-Instruct | 1.5B | q4f16_0 | 日本語に強い |
| Qwen2.5-0.5B-Instruct | 0.5B | q4f16_0 | 超軽量 |
| Qwen2.5-3B-Instruct | 3B | q4f16_1 | やや大きい |
| gemma-2-2b-it | 2B | q4f16_1 | Google |
| Llama-3.2-1B-Instruct | 1B | q4f16_1 | Meta |
| Llama-3.2-3B-Instruct | 3B | q4f16_1 | Meta |
| SmolLM2-135M/360M | <1B | q0f16 | HuggingFace |

### wllama (llama.cpp WASM) — フォールバック候補

- リポジトリ: https://github.com/ngxson/wllama
- **最新バージョン: 2.3.7**（2025年11月）
- NPMパッケージ: `@wllama/wllama`
- **GGUF形式を直接読み込み可能**（量子化の選択肢が広い）
- WASM + SIMD による**CPU推論**（WebGPU非使用）
- Firefox の Link Preview 機能に採用実績あり
- llama.cpp のGBNF grammar継承（構造化出力対応の可能性）

#### WebLLM vs wllama 比較

| 項目 | WebLLM | wllama |
|------|--------|--------|
| **GPUアクセラレーション** | WebGPU（GPU） | WASM（CPUのみ） |
| **推論速度（2B, RTX 3060相当）** | ~30-40 tok/s | ~5-15 tok/s |
| **モデル形式** | MLC compiled | GGUF |
| **構造化JSON出力** | XGrammar（強力） | GBNF grammar（要検証） |
| **ブラウザ互換性** | WebGPU対応ブラウザのみ | 全ブラウザ |
| **Qwen3.5-2B対応** | MLC q4版が未提供 | GGUF版あり、即利用可 |

### Transformers.js (Hugging Face)

- WebGPU対応（ONNX Runtime Web backend、v3〜）
- `device: 'webgpu'` で有効化
- ONNX形式のため、モデル変換が必要
- LLM推論に特化したWebLLMと比較すると性能面で劣る可能性
- マルチタスクML向け（画像分類、音声認識など）には強い

---

## LLMモデル候補

### 第一候補: Qwen3.5-2B

- **パラメータ数**: 2B
- **アーキテクチャ**: Gated Delta Networks + Sparse MoE
- **マルチモーダル**: Vision対応（Image-Text-to-Text）
- **コンテキスト長**: 262,144トークン
- **テスト結果（Ollama）**: JSON + 日本語で適切な返答が可能（応答品質は「少々物足りない」が実用範囲内）

#### MLC形式の提供状況（2026年3月時点）

| モデル | 量子化 | 提供者 | 状態 |
|--------|--------|--------|------|
| Qwen3.5-2B-q0f16-MLC | fp16（非量子化） | Mitiskuma | あり（~4GB、大きすぎ） |
| **Qwen3.5-2B-q4f16_1-MLC** | **4bit量子化** | — | **未提供** |
| Qwen3.5-4B-q4f16_1-MLC | 4bit量子化 | imbue | あり |
| Qwen3.5-9B-q4f16_1-MLC | 4bit量子化 | Mitiskuma | あり |

#### GGUF形式の提供状況

コミュニティによる豊富な量子化版が存在:
Q4_0, Q4_1, Q4_K_S, Q4_K_M, Q4_K_L, Q5_K_M, Q6_K, Q8_0, IQ4_XS, IQ4_NL 等

#### 課題

- **WebLLMで使うにはMLC q4f16_1版が必要だが、現時点で存在しない**
- 自分でMLC compileするか、コミュニティの提供を待つ必要あり
- マルチモーダルのためVision Encoderの分だけモデルサイズが大きい
- wllamaを使えばGGUF版で即利用可能だが、CPU推論で遅い

### 代替候補比較

| モデル | サイズ | WebLLM対応 | 日本語 | JSON安定性 | DLサイズ(q4) | 備考 |
|--------|--------|-----------|--------|-----------|-------------|------|
| **Qwen3.5-2B** | 2B | MLC q4未提供 | 優秀 | 要検証 | ~1.2GB | Vision対応、本命だが準備中 |
| **Qwen3-1.7B** | 1.7B | 公式対応 | 良好 | 要検証 | ~1.0GB | **すぐ使える最有力** |
| **Qwen2.5-1.5B-Instruct** | 1.5B | 公式対応 | 優秀 | 良好 | ~0.9GB | 日本語チャットに最適 |
| gemma-2-2b-it | 2B | 公式対応 | 普通 | 良好 | ~1.2GB | Google製、日本語はやや弱い |
| Phi-4-mini-instruct | 3.8B | 公式対応 | 弱い | 良好 | ~2.0GB | 推論は強いが日本語×、大きい |
| SmolLM2 | 0.1-1.7B | 公式対応 | 弱い | 要検証 | ~0.5GB | 超軽量だが英語中心 |

### 推奨戦略

1. **即時利用可能**: Qwen3-1.7B or Qwen2.5-1.5B-Instruct（WebLLM公式対応、日本語OK）
2. **本命**: Qwen3.5-2B-q4f16_1-MLC が提供され次第切り替え
3. **フォールバック**: wllama + GGUF でWebGPU非対応ブラウザもカバー

---

## ブラウザ WebGPU 対応状況（2026年3月時点）

**グローバル対応率: 約83%**

| ブラウザ | 対応状況 |
|---------|---------|
| **Chrome** | v113〜 完全対応 |
| **Edge** | v113〜 完全対応 |
| **Chrome Android** | v145〜 対応 |
| **Samsung Internet** | v24〜 対応 |
| **Safari (macOS)** | v26.0〜 部分対応（macOS Tahoe） |
| **Safari (iOS)** | v26.0〜 対応 |
| **Firefox** | v151時点でデフォルト無効（`dom.webgpu.enabled`フラグ必要） |

**所感**: Chrome + Edge でWeb利用者の~75%をカバー。Firefoxが未対応なのが唯一の大きな穴。
お試し機能として割り切るなら十分なカバレッジ。

---

## Unity WebGPU サポート状況

- Unity 6.1 で experimental として公開アクセス開始
  - 参考: https://discussions.unity.com/t/public-access-to-webgpu-experimental-in-unity-6-1/1572462
  - マニュアル: https://docs.unity3d.com/6000.3/Documentation/Manual/WebGPU.html
- WebGL2 からの自動切替ではなく、手動で有効化が必要
- 2026年3月時点で experimental ステータス

### Unity WebGL + WebGPU LLM の共存

- **Unity描画はWebGL2、LLM推論はWebGPUを使用** — 別々のGPUコンテキストとして共存可能
- Unity自体をWebGPUビルドにする必要はない（experimentalを避けられる）
- WebLLMを**Web Worker**で動かし、メインスレッドのUnityと分離するのが推奨構成

```
Unity C# → [.jslib plugin] → JavaScript (main thread)
                                   ↕ postMessage
                              Web Worker → WebLLM (WebGPU)
```

---

## Cyan-Nookへの統合案

### アーキテクチャ

既存の `ILLMProvider` パターンに新プロバイダーとして追加:

```
ILLMProvider
  ├── OllamaProvider      (既存: 高品質、外部サーバー必要)
  ├── DifyProvider         (既存: クラウドAPI)
  └── WebGPUProvider       (新規: ブラウザ内推論、セットアップ不要)
```

### 実装ステップ（概要）

1. WebLLM の JavaScript ライブラリをビルドに同梱
2. `.jslib` で C# - JavaScript ブリッジを実装
   - モデル読み込み / 推論リクエスト / ストリーミングレスポンス受信
   - Web Worker経由でメインスレッドをブロックしない構成
3. `WebGPUProvider : ILLMProvider` として既存インターフェースに適合
4. 設定パネルの `LLMApiType` に `WebGPU` 選択肢を追加
5. モデルのダウンロード/キャッシュ管理UI
   - IndexedDBによるキャッシュ（WebLLM組み込み機能）
   - 初回ダウンロード進捗の表示

### XGrammarによる構造化JSON出力

WebLLMのXGrammar統合により、Cyan-NookのJSONスキーマをデコード時に強制可能:

```json
{
  "character": "chr001",
  "message": "こんにちは！",
  "animation": "idle01",
  "emotion": {
    "joy": 0.8,
    "fun": 0.5,
    "angry": 0.0,
    "sorrow": 0.0,
    "surprised": 0.0
  }
}
```

プロンプトエンジニアリングに頼らずスキーマ準拠を保証できるため、
2Bクラスの小型モデルでもJSON出力の信頼性が大幅に向上する。

### GPU/メモリ予算

| リソース | 推定使用量 |
|---------|-----------|
| モデルVRAM（q4, 2B） | ~1.5-2.0 GB |
| Unity WebGL描画 | ~0.5-1.0 GB |
| **合計VRAM** | **~2.0-3.0 GB** |
| モデルダウンロード | ~1.0-1.3 GB（q4） |
| システムRAM（モデル関連） | ~2-4 GB |

**4GB VRAM以上のGPUで動作見込み。6-8GB推奨。**

### 推論性能の目安

| GPU | 推定速度（2B, q4） |
|-----|-------------------|
| RTX 3060相当 | ~30-40 tok/s |
| 統合GPU（Intel/AMD） | ~5-15 tok/s |
| モバイル（高性能） | ~3-10 tok/s |

### GPU競合の考慮

- レンダリングとLLM推論がGPUを共有する
- Cyan-Nookの設計思想「Low GPU usage for rendering」が活きる
- 推論中のFPS低下を「Thinking」演出として活用可能（"Inconvenience as a Feature"）
- 描画のさらなる軽量化が必要になる可能性あり

### ユーザー体験

- 「手軽に試す → WebGPU（セットアップ不要、品質は限定的）」
- 「高品質 → Ollama / Dify（外部サーバー必要）」
- 設定パネルで選択可能
- WebGPU非対応ブラウザ向けの案内メッセージ

---

## 着手前に必要な検証

- [ ] WebLLM デモでQwen3-1.7B / Qwen2.5-1.5BにCyan-NookのJSONスキーマを出力させて品質確認
  - XGrammar（構造化出力）の動作確認含む
  - デモ: https://webllm.mlc.ai/ / https://huggingface.co/spaces/mlc-ai/WebLLM-Structured-Generation-Playground
- [ ] Qwen3.5-2B-q4f16_1-MLC の提供状況を定期確認
  - または自前でMLC compileを検討
- [ ] Unity WebGLビルドでの描画 + 推論同時実行時のFPS計測
- [ ] .jslib ブリッジの技術検証（WebLLM API呼び出し → Web Worker → C#コールバック）
- [ ] モデルのダウンロードサイズとIndexedDBキャッシュの挙動確認
- [ ] wllama（GGUF + CPU推論）をフォールバックとして検証するか判断

---

## 関連研究

- **WeInfer** (ACM Web Conference 2025): WebGPU向けLLM推論最適化フレームワーク
  - バッファ再利用・非同期パイプラインにより WebLLM 比で最大3.76倍の性能向上
  - 参考: https://dl.acm.org/doi/10.1145/3696410.3714553

---

## 参考リンク

- [Office Sim (Noumena Labs)](https://noumenalabs.itch.io/office-sim)
- [WebLLM GitHub](https://github.com/mlc-ai/web-llm)
- [WebLLM Documentation](https://webllm.mlc.ai/docs/)
- [WebLLM Paper (arXiv)](https://arxiv.org/html/2412.15803v1)
- [XGrammar GitHub](https://github.com/mlc-ai/xgrammar)
- [XGrammar JSON生成ドキュメント](https://xgrammar.mlc.ai/docs/tutorials/json_generation.html)
- [WebLLM Structured Generation Playground](https://huggingface.co/spaces/mlc-ai/WebLLM-Structured-Generation-Playground)
- [wllama GitHub](https://github.com/ngxson/wllama)
- [Unity WebGPU Manual](https://docs.unity3d.com/6000.3/Documentation/Manual/WebGPU.html)
- [Unity 6.1 WebGPU Public Access](https://discussions.unity.com/t/public-access-to-webgpu-experimental-in-unity-6-1/1572462)
- [WeInfer (ACM 2025)](https://dl.acm.org/doi/10.1145/3696410.3714553)
- [WebGPU Browser Support Status](https://www.webgpu.com/news/webgpu-hits-critical-mass-all-major-browsers-now-ship-it/)
- [Qwen3.5-2B HuggingFace](https://huggingface.co/Qwen/Qwen3.5-2B)
- [QwenLM/Qwen3.5 GitHub](https://github.com/QwenLM/Qwen3.5)
