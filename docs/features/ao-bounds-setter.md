# AO Bounds Setter

複数メッシュの Anchor Override・Root Bone・Bounds を一括設定します。衣装制作やアバターセットアップ時に便利です。

## 使い方

1. Root Object フィールドにヒエラルキーのオブジェクトをドラッグ
2. 以下の設定を行う:
   - **Anchor Override** — アンカーポイントとして使用するオブジェクトを設定
   - **Root Bone**（SkinnedMeshRenderer のみ）— スキンメッシュのルートボーンを設定
   - **Bounds**（SkinnedMeshRenderer のみ）— バウンドを設定
   - ドロップダウンの検索機能でルートオブジェクト配下のボーン・アンカーをすばやく選択可能
3. チェックボックスで対象メッシュを選択し、**Apply to Selected Meshes** をクリックして一括適用

オブジェクト名・Anchor Override・Root Bone のラベルをクリックすると、ヒエラルキー内でそのオブジェクトを選択できます。

## アクセス方法

`Tools > Kanameliser Editor Plus > AO Bounds Setter`
