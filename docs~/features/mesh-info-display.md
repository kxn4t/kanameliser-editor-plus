# Mesh Info Display

選択したオブジェクトとその子オブジェクトのメッシュ情報をシーンビュー左上に表示します。

![シーンビュー左上の Mesh Info 表示](/images/mesh-info-display/overview.png)

確認できる情報：

- ポリゴン数
- マテリアル数
- マテリアルスロット数
- メッシュ数
- パーティクルシステム数と消費マテリアルスロット数（選択に含まれる場合のみ）
- Trail/Line レンダラーの消費マテリアルスロット数（選択に含まれる場合のみ）

## パーティクル情報の表示

パーティクルシステムや Trail/Line レンダラーは、メッシュとは別枠で VRChat のパフォーマンスランクのマテリアルスロットを消費します（パーティクルシステム 1 個で 1 スロット、Trails 有効時は 2 スロット、Trail/Line レンダラーは各 1 スロット）。

なお、Render Mode が Mesh のパーティクルのポリゴン数は、VRChat ではアバターの Polygons ではなく別のスタット（Mesh Particle Active Polys）として数えられるため、上のポリゴン数・メッシュ数には含めていません。

選択中のオブジェクトにこれらが含まれる場合、メッシュ情報の下に区切り線付きで表示されます：

- `Particle Systems` — パーティクルシステムの数
- `Particle Slots` — パーティクルシステムが消費するマテリアルスロット数
- `Trail/Line Slots` — Trail/Line レンダラーが消費するマテリアルスロット数

パフォーマンスランクが参照する実際のマテリアルスロット数は、`Material Slots` とこれらの追加スロットの合計になります。

## NDMFプレビュー対応

NDMFプレビューがアクティブな場合、AAO・TTT・Meshia などの最適化結果を確認しながら調整できます。

- 最適化前後のメッシュ数を差分付きで並列表示
- NDMFプロキシメッシュを自動検出し、プレビュー中であることを緑のドットで表示

![NDMF プレビュー中の差分表示](/images/mesh-info-display/ndmf-preview.png)

## アクセス方法

表示のオンオフが可能です。

- 表示全体：`Tools > Kanameliser Editor Plus > [Settings] > Show Mesh Info Display`
- パーティクル情報のセクション：`Tools > Kanameliser Editor Plus > [Settings] > Show Particle Info Display`
