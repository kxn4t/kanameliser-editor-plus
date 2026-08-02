# MA Material Helper

Modular Avatar のマテリアル制御コンポーネントを使った色変更メニューを自動生成します。カラーバリエーションの Prefab から数クリックで色変更メニューを作成でき、複数 Prefab からの同時生成にも対応しています。

**必要条件:** [Modular Avatar](https://modular-avatar.nadena.dev/ja) 1.13.0 以上

## 使い方

1. カラーバリエーション Prefab を選択 → 右クリック → `Copy Color Variants`（複数選択可）
2. ターゲットの衣装を選択 → 右クリック → `Create Color Menu`（コピー後に表示されます）

手順が分からなくなったときは、右クリックメニューの `[How to Create Color Menu]` を開くと、手順の説明とコピー済みオブジェクトの一覧をいつでも確認できます。

番号付きカラーバリエーション（Color1、Color2など）を含む「Color Menu」が自動作成されます。`Create Color Menu` はMaterial Setterをスロット単位で生成します（ほとんどのケースで**推奨**）。

特殊なケース向けに、`Advanced` サブメニュー（コピー後に表示）で別の生成モードを選択できます:

| コマンド | 説明 |
|---|---|
| `Create Material Setter (All Slots)` | すべてのスロットに Setter を作成（カスタマイズ用） |
| `Create Material Swap` | マテリアルの置き換えルールで設定 |
| `Create Material Swap (Per Object)` | オブジェクトごとに個別の Swap を作成 |

## Material Setter と Material Swap の違い

基本的には **Material Setter を推奨**します。

| | Material Setter | Material Swap |
|---|---|---|
| 設定単位 | スロット単位 | マテリアル名単位 |
| 同一メッシュで同じマテリアルを別々の変更先に | 対応 | 非対応 |
| 用途 | ほとんどのケース | シンプルな構成 |

### Material Swap が対応できないケース

```
メッシュA:
  スロット0: マテリアルX → マテリアルY に変更したい
  スロット1: マテリアルX → マテリアルZ に変更したい
```

Material Swap は「マテリアルX」を1つのマテリアルにしか置き換えられないため、両スロットが同じ結果になります。Material Setter ならスロット単位で異なるマテリアルを指定できます。

## Material Slot Remapping

衣装を別アバター向けに変換（自動調整ツールなど）すると、レンダラーのマテリアルスロット順が変わり、インデックスベースの色変更設定がずれることがあります。変換後の衣装にリマッピングコンポーネントを追加し、元の衣装を指定することで補正できます。

### 使い方

1. 変換後の衣装を右クリック → `Add Material Slot Remapping`
2. 元の衣装を `Reference Prefab` に指定 → `Generate Mapping` をクリック
3. 通常どおり `Copy Color Variants` / `Create Color Menu` を実行 — 生成がマッピングに従い、色が正しいスロットに適用されます

マッピングはマテリアル参照から生成されるため、変換直後の衣装のマテリアルを変更する前に実行し、Material Slot Remappingを生成してください。  
生成後はスロットの対応付け（インデックス）のみ保持するため、生成後に衣装のマテリアルを変更してもマッピングは壊れません。
同じマテリアル（または空のスロット）が複数ある場合はスロット順を一意に判定できないため、確認ダイアログと警告が表示されます。推定結果を確認し、必要に応じてInspectorで手動修正してください。

## パフォーマンスに関する注意

Material Setter/Swapはビルド時にマテリアル差し替えアニメーションを生成するため、色変更メニューの対象メッシュはAAO: Avatar OptimizerのTrace and Optimizeによる自動メッシュ統合の対象外になります。メッシュが統合されない分、ドローコールなどの負荷が増えがちです。

余裕があれば、[Merge Skinned Mesh (Color Menu Safe)](./color-menu-safe-merge)で対象メッシュを統合すると、色変更メニューを壊さずに負荷を抑えられます。鞄などトグルでON/OFFするメッシュは統合対象に含めないなどの注意点があるため、詳細はリンク先を参照してください。

## アクセス方法

ヒエラルキー右クリック → `Kanameliser Editor Plus > Copy Color Variants / Create Color Menu / Advanced / [How to Create Color Menu] / Add Material Slot Remapping`

## 詳細マッチングログ

自動オブジェクトマッチングが期待通りの結果にならない場合、Unityコンソールにマッチング判定の詳細情報を出力できます。

切り替え: `Tools > Kanameliser Editor Plus > [Settings] > Verbose Matching Logs`
