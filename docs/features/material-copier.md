# Material Copier

複数選択した GameObject から同名の GameObject へマテリアルをコピー＆ペーストできます。FBX セットアップや衣装のカラーバリエーション対応が一瞬で完了します。MeshRenderer と SkinnedMeshRenderer の両方に対応しています。

## 使い方

1. コピー元の GameObject を選択（複数選択可）→ 右クリック → `Copy Materials`
2. コピー先の GameObject を選択 → 右クリック → `Paste Materials`

マテリアルは名前が一致するオブジェクトに自動で適用されます（例: `Head` → `Head`、`Body` → `body`）。

## マッチング仕様

コピー元とコピー先のオブジェクトは以下の優先順位で自動対応付けされます。いずれも Renderer を持つオブジェクトのみが対象です。

| 優先度 | マッチング方法 | 例 |
|---|---|---|
| 1 | 相対パスの完全一致（ルート名を除く） | `Outfit/Jacket` → `Outfit/Jacket` |
| 2 | 同じ階層深度での名前一致 | `Outfit_A/Outer/Accessories/Earing`（深度3）→ `Outfit_B/Inner/Accessories/Earing`（深度3）|
| 3 | 名前一致（深度不問、近い深度を優先） | `Outfit/Outer/Jacket`（深度3）→ `Jacket`（深度1）|
| 4 | 大文字小文字を区別しない名前一致 | `earing` → `Earing` |
| 5 | 類似名マッチ（サフィックス除去・共通ベース名） | `Ribbon_blue` → `Ribbon_red`、`Hair.001` → `Hair.002` |

同じ優先度に複数の候補が残る場合は、階層パスの類似度・祖先コンテキストの一致度・深度の近さ・レーベンシュタイン距離の順で選択します。

このマッチング仕様は MA Material Helper でも共通です。

## アクセス方法

ヒエラルキー右クリック → `Kanameliser Editor Plus > Copy Materials / Paste Materials`
