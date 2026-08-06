# 外部アクションフィード用 受け渡しサーバー (nginx)

Cyan-Nook の外部アクションフィード機能で使う、**JSON 2つと画像 1枚を受け渡すだけの小さなサーバー**です。
nginx の標準機能 (`dav_methods PUT`) だけで動くため、カスタムコードはありません。

```
外部エージェント (Hermes 等) ── PUT action.json ──▶ ┌─────────────┐
外部エージェント ◀── GET context.json / camera.jpg ── │ nginx (このサーバー) │
Cyan-Nook ◀──────── GET action.json ─────────────── │  /feed-data/ に保存  │
Cyan-Nook ── PUT context.json / camera.jpg ────────▶ └─────────────┘
```

| ファイル | 書き手 | 読み手 | 内容 |
|---|---|---|---|
| `action.json` | 外部エージェント | Cyan-Nook | キャラクターへの行動指示 (LLM レスポンスと同一スキーマ) |
| `context.json` | Cyan-Nook | 外部エージェント | キャラの状態・位置・視界情報 |
| `camera.jpg` | Cyan-Nook | 外部エージェント | キャラクター視点の画像 |

## 前提

- Docker + Docker Compose が動く常時稼働マシン (外部エージェントと同居させると管理が楽)
- **閉じたネットワーク (LAN / Tailscale) 内での利用限定**。認証が無いため、インターネットに公開しないでください

## セットアップ

```bash
mkdir cyan-feed && cd cyan-feed
# このフォルダの nginx-feed.conf と docker-compose.yml を置く

mkdir feed-data
# nginx コンテナ (worker は uid 101) が書き込めるようにする
sudo chown 101:101 feed-data

docker compose up -d
```

## 動作確認

```bash
# PUT (201 Created / 204 No Content が返れば成功)
echo '{"test": 1}' > /tmp/t.json
curl -si -X PUT -H "Content-Type: application/json" \
  --data-binary @/tmp/t.json http://localhost:8093/feed/action.json | head -1

# GET (今書いた内容が返る)
curl -s http://localhost:8093/feed/action.json

# ファイルとしても確認できる (デバッグはこれが一番早い)
cat feed-data/action.json
```

別マシンからは `localhost` をサーバーの LAN / Tailscale アドレスに読み替えてください。

## Cyan-Nook 側の設定

LLM 設定パネルの「外部アクションフィード」セクションに以下を入力 (`<feed-host>` はサーバーのアドレス):

| 設定欄 | 値 |
|---|---|
| action subscribe URL | `http://<feed-host>:8093/feed/action.json` |
| context publish URL | `http://<feed-host>:8093/feed/context.json` |
| camera publish URL | `http://<feed-host>:8093/feed/camera.jpg` |

## トラブルシューティング

| 症状 | 原因と対処 |
|---|---|
| PUT が 403 / 500 | `feed-data` の書き込み権限。`sudo chown 101:101 feed-data` を再確認 |
| PUT が 404 | パスが3ファイル以外 (ファイル名の綴り・拡張子を確認)。許可リスト方式のため未知の名前は拒否される |
| 書いたのに反映されない | `cat feed-data/action.json` で実物を確認。中身が変わっていれば受け渡しは成功しており、問題は書き手側の JSON 内容か読み手側の設定 |
| WebGL 版で PUT だけ失敗 | CORS プリフライト。`nginx-feed.conf` の `Access-Control-Allow-*` ヘッダを削っていないか確認 |

## セキュリティ上の注意

- このサーバーに書き込める人は誰でもキャラクターを操作でき、カメラ画像 (キャラ視点の 3D 画面) を閲覧できます
- 許可リスト方式 (3ファイル固定) のため任意ファイルの設置はできませんが、認証はありません。**必ず閉域ネットワーク内で運用してください**
- 露出を最小にしたい場合は、`docker-compose.yml` の ports を `"100.x.x.x:8093:80"` のように Tailscale アドレスへのバインドに変更すると、LAN 側からは見えなくなります
