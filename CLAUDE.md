# CLAUDE.md

このファイルはClaude Code (claude.ai/code) がこのリポジトリで作業する際のガイダンスを提供します。

## 言語ルール

- **コーディング**: 英語で行う（コード、コメント、変数名、XMLドキュメント等すべて英語）
- **作業・会話**: ユーザーとのやり取りや作業説明は日本語で行う
- **コミットメッセージ**: 英語でConventional Commits形式に従う（例: `feat:`, `fix:`, `docs:`）

## プロジェクト概要

**Kanameliser Editor Plus** — Unity・VRChat向けエディター拡張セット（`net.kanameliser.editor-plus`）。

## リリースパッケージ

GitHub Actionsワークフロー（`.github/workflows/release.yml`）を手動実行すると`vpm-packager`がzip/unitypackageを生成し、draftリリースを作成する。draftを手動でpublishするとリリース完了（beta版はプレリリースとしてpublishする）。

vpm-packagerは`.github/`と`~`末尾ディレクトリのみ自動除外するため、それ以外の開発用ファイル（CLAUDE.md等）はワークフロー内でビルド前に削除している。開発用ファイルを追加した場合は削除リストも更新すること。

## ドキュメントサイト運用

`docs~/`のVitePressサイトをGitHub Pages（https://kxn4t.github.io/kanameliser-editor-plus/）へデプロイする。ワークフローは`.github/workflows/docs.yml`。

### チャンネル構成

| URL | 内容 | ビルドソース |
| --- | --- | --- |
| ルート `/` | 安定版ドキュメント | `docs-release`ブランチ |
| `/beta/` | beta版ドキュメント | 最新のプレリリースタグ（安定版より新しい場合のみ） |

- mainへのマージではサイトは更新されない（未リリース内容は公開されない）
- デプロイトリガー: リリースpublish / `docs-release`へのpush / 手動実行（workflow_dispatch）

### 通常リリース時（全自動）

リリースをpublishする以外の操作は不要。

- 安定版（`x.y.z`）をpublish → `docs-release`ブランチが自動でタグ位置にforce-updateされ、ルートが更新される。同時にbetaは安定版より新しくなくなるため`/beta/`は消える
- beta（`x.y.z-beta.N`）をpublish → `/beta/`のみ更新され、ルートは変わらない

### ドキュメントのみの修正（リリース不要）

```bash
git fetch origin && git checkout docs-release
git cherry-pick <docs修正コミット>   # または直接編集してコミット
git push origin docs-release
```

pushするとルートサイトが自動で再ビルドされる。

**必ずmainにも同じ修正を入れること**（基本はmainにPRで入れてから`docs-release`へcherry-pick）。次の安定版リリースで`docs-release`はタグ位置に上書きされるため、mainにない修正はそこで消える。

`/beta/`側のホットフィックスは非対応。修正は次のbetaリリースに含めて出す。

### バージョン表示の仕組み

- docs.ymlがビルド時に環境変数`DOCS_VERSION` / `DOCS_CHANNEL` / `DOCS_STABLE_VERSION` / `DOCS_BETA_VERSION`を注入する
- `docs~/.vitepress/config.mts`がナビ右上のバージョンドロップダウン（stable⇔beta相互リンク）を生成し、betaビルドには`noindex`メタタグを付与する
- betaビルドではページ上部に警告バナーを表示する（`docs~/.vitepress/theme/BetaBanner.vue`）
- ローカルビルド（環境変数なし）ではバージョン表示・バナーとも表示されない

### 移行期間の注意（1.0.0正式リリースまで）

安定版タグ（0.5.0以前）には`docs~`が存在しないため、`docs-release`ブランチが自動作成される1.0.0のpublishまでは、最新betaタグの内容がルートに配置される（`/beta/`は無し）。
