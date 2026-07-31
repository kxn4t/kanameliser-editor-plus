# FBX Settings Copier

Project ウィンドウで FBX アセット間のインポート設定をコピー＆ペーストできます。同じインポート設定にしたい FBX が複数あるとき（衣装のカラーバリエーションやアバターのアップデートなど）に、毎回 Inspector で設定し直す手間を省けます。

## 使い方

1. コピー元の FBX を Project ウィンドウで選択 → 右クリック → `Copy FBX Settings`
2. コピー先の FBX を選択（複数選択可）→ 右クリック → `Paste FBX Settings`

複数の FBX への一括ペーストに対応しています。コピーした設定と差分のないファイルは再インポートせずスキップされるため、すでに設定済みのファイルが混ざっていても時間を取られません。

## コピーされる設定

| タブ | コピーされる内容 |
|---|---|
| Model | 全設定（Scale Factor、Read/Write、Blend Shapes、Normals/Tangents、Legacy Blend Shape Normals など） |
| Rig | Animation Type、Avatar Definition（Copy From Other Avatar の場合は参照アバターも）、Skin Weights、Optimize Bones / Optimize Game Objects |
| Materials | Material Creation Mode、Location、Naming/Search 設定、Remapped Materials |

### Remapped Materials の適用ルール

Remapped Materials（マテリアル名 → マテリアルアセットの対応表）は、ターゲットに同名のマテリアルが存在する場合のみ適用されます。一致しないリマップは無視され、ターゲット側の既存の割り当ても上書きされません。同一アバターの色違い・衣装違いの FBX のようにマテリアル名が共通しているケースで、マテリアル割り当てまで一括で揃えられます。

### コピーされない設定

以下はファイル固有の情報のためコピーされません:

- Animation タブのクリップ定義（フレーム範囲やテイク名は FBX ごとに異なるため）
- Humanoid のボーンマッピング（Avatar Configuration）— 同じ素体のアバターを使う場合は、Avatar Definition を `Copy From Other Avatar` にした状態でコピーしてください

::: warning 注意
インポート設定の変更は Ctrl+Z で元に戻せません。心配な場合は、ペースト前に Inspector 右上の Preset から現在の設定を保存しておくと安全です。
:::

## アクセス方法

Project ウィンドウの FBX 右クリック → `Kanameliser Editor Plus > Copy FBX Settings / Paste FBX Settings`
