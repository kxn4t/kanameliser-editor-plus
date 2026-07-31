# FBX Settings Copier

ProjectウィンドウでFBXアセット間のインポート設定をコピー＆ペーストできます。同じインポート設定にしたいFBXが複数あるとき（複数のアバター対応時など）に、毎回Inspectorで設定し直す手間を省けます。

## 使い方

1. コピー元のFBXをProjectウィンドウで選択 → 右クリック → `Copy FBX Settings`
2. コピー先のFBXを選択（複数選択可）→ 右クリック → `Paste FBX Settings`

複数のFBXへの一括ペーストに対応しています。コピーした設定と差分のないファイルは再インポートせずスキップされるため、すでに設定済みのファイルが混ざっていても時間を取られません。

## コピーされる設定

| タブ | コピーされる内容 |
|---|---|
| Model | 全設定（Scale Factor、Read/Write、Blend Shapes、Normals/Tangents、Legacy Blend Shape Normalsなど） |
| Rig | Animation Type、Avatar Definition（Copy From Other Avatarの場合は参照アバターも）、Skin Weights、Optimize Bones / Optimize Game Objects |
| Materials | Material Creation Mode、Location、Naming/Search設定、Remapped Materials |

### Remapped Materialsの適用ルール

Remapped Materials（マテリアル名 → マテリアルアセットの対応表）は、ターゲットに同名のマテリアルが存在する場合のみ適用されます。一致しないリマップは無視され、ターゲット側の既存の割り当ても上書きされません。同一アバターの色違い・衣装違いのFBXのようにマテリアル名が共通しているケースで、マテリアル割り当てまで一括で揃えられます。

### コピーされない設定

以下はファイル固有の情報のためコピーされません:

- Animationタブのクリップ定義（フレーム範囲やテイク名はFBXごとに異なるため）
- Humanoidのボーンマッピング（Avatar Configuration）— 同じ素体のアバターを使う場合は、Avatar Definitionを `Copy From Other Avatar` にした状態でコピーしてください

::: warning 注意
インポート設定の変更はCtrl+Zで元に戻せません。心配な場合は、ペースト前にInspector右上のPresetから現在の設定を保存しておくと安全です。
:::

## アクセス方法

ProjectウィンドウのFBX右クリック → `Kanameliser Editor Plus > Copy FBX Settings / Paste FBX Settings`
