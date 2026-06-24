# MA Material Helper

Modular Avatar のマテリアル制御コンポーネントを使った色変更メニューを自動生成します。カラーバリエーションの Prefab から数クリックで色変更メニューを作成でき、複数 Prefab からの同時生成にも対応しています。

**必要条件:** [Modular Avatar](https://modular-avatar.nadena.dev/ja) 1.13.0 以上

## 使い方

1. カラーバリエーション Prefab を選択 → 右クリック → `Copy Material Setup`（複数選択可）
2. ターゲットの衣装を選択 → 右クリック → 以下のいずれかを選択:

| コマンド | 説明 |
|---|---|
| `Create Material Setter` | 各スロットに直接マテリアルを設定（**推奨**） |
| `[Optional] Create Material Setter (All Slots)` | すべてのスロットに Setter を作成（カスタマイズ用） |
| `Create Material Swap` | マテリアルの置き換えルールで設定 |
| `[Optional] Create Material Swap (Per Object)` | オブジェクトごとに個別の Swap を作成 |

番号付きカラーバリエーション（Color1、Color2 など）を含む「Color Menu」が自動作成されます。

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

## アクセス方法

ヒエラルキー右クリック → `Kanameliser Editor Plus > Copy Material Setup / Create Material Setter / Create Material Swap`
