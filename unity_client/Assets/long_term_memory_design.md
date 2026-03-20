# 長期記憶システム設計メモ (2026-02-26)

## ステータス: 検討中

## 現状
- 会話履歴はインメモリのみ（最大10件、ChatManager._conversationHistory）
- セッション終了で消失、永続化なし
- PlayerPrefs（WebGL→IndexedDB）は設定のみに使用

## 推奨アプローチ: 構造化メモリ + 睡眠統合

### メモリ3層構造

| 層 | 名称 | 内容 | 保持期間 |
|---|------|------|---------|
| Working Memory | 作業記憶 | 現在の_conversationHistory (10件) | セッション中のみ（既存） |
| Episodic Memory | エピソード記憶 | セッション単位の要約（いつ何を話したか） | 最新5-10件、古いものは消える |
| Semantic Memory | 意味記憶 | ユーザーについての恒久的知識 | 蓄積され続ける（上限あり） |

### なぜ構造化JSONか（ナラティブ要約ではなく）
- トークン効率が良い
- 必要な情報だけプロンプトに注入可能
- 更新・マージが容易
- 検索性が高い

### Semantic Memory 構造例
```json
{
  "user_profile": {
    "name": "たろう",
    "interests": ["プログラミング", "猫", "音楽"],
    "personality_notes": "丁寧な話し方、冗談が好き"
  },
  "relationship": {
    "familiarity": 0.7,
    "trust": 0.8,
    "total_conversations": 42,
    "first_met": "2026-01-15",
    "last_talked": "2026-02-26"
  },
  "important_facts": [
    { "fact": "来月引っ越し予定", "added": "2026-02-20", "emotion": "surprised" },
    { "fact": "最近仕事が忙しい", "added": "2026-02-25", "emotion": "sad" }
  ],
  "preferences": {
    "likes": ["甘いもの", "静かな場所"],
    "dislikes": ["早起き"]
  }
}
```

### Episodic Memory 構造例
```json
[
  {
    "date": "2026-02-26 14:30",
    "summary": "仕事の愚痴を聞いた。上司との関係に悩んでいる様子",
    "mood": "sad",
    "topics": ["仕事", "人間関係"],
    "duration_turns": 12
  }
]
```

## 記憶統合トリガー

| トリガー | タイミング | 処理内容 |
|---------|----------|---------|
| セッション終了時 | ブラウザを閉じる前 | Working → Episodic要約（軽量） |
| interact_sleep | キャラが寝る時 | Episodic → Semantic統合（重い） |
| 一定会話量到達 | 例: 20ターン | Working → Episodic要約（自動） |

- interact_sleepでの統合はCyan Nookの「存在感」コンセプトと人間の記憶統合（睡眠中）が一致
- 演出にも使える: 寝ている間にLLMで記憶整理→起きたとき「覚えてるよ」的反応

## 感情による記憶強化
- EmotionDataの値が高い会話は記憶に残りやすくする
- Episodic Memoryに感情の強さを記録
- Semantic統合時に感情の強いエピソードを優先保持
- 人間の記憶と同じ「感情的に強い出来事ほど忘れない」を再現

## 実装方針

### ストレージ
- PlayerPrefs（IndexedDB）でJSONを文字列保存

### 要約・抽出
- LLMClient経由で通常会話とは別に専用リクエスト
- 抽出用プロンプト例:
```
以下の会話ログから、ユーザーについての新しい情報を抽出してJSON形式で返してください。
既知の情報: {既存のsemantic_memory}
会話ログ: {conversation_history}
```

### プロンプト注入
- system_prompt_template.txtに{memory_context}プレースホルダー追加
- Semantic Memory + 直近Episodic Memoryを注入

## 方法比較

| 方法 | メリット | デメリット |
|------|---------|-----------|
| ナラティブ要約 | 実装が単純 | トークン消費大、情報が曖昧化 |
| 構造化メモリ（推奨） | トークン効率良、更新容易 | 抽出プロンプトの設計が必要 |
| 全ログ保存 + 検索 | 情報の損失なし | RAGに近づく、ストレージ膨大 |
| Embedding + 類似度検索 | 関連記憶を正確に取得 | WebGLでのEmbedding計算が困難 |

## 段階的実装の推奨
1. まずSemantic Memory（ユーザープロフィール）だけ実装
2. Episodic Memoryは後から追加
