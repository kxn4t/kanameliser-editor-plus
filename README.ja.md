# Kanameliser Editor Plus

UnityおよびVRChatのための便利なエディター拡張機能セット。

## インストール方法

### VRChat Creator Companion経由（推奨）

1. [https://kxn4t.github.io/vpm-repos/](https://kxn4t.github.io/vpm-repos/) にアクセス
2. 「Add to VCC」ボタンをクリックして、「Kanameliser VPM Packages」リポジトリをVCCまたはALCOMに追加
3. Manage Projectからパッケージ一覧上の「Kanameliser Editor Plus」をプロジェクトに追加

### 手動インストール

1. [GitHub Releases](https://github.com/kxn4t/kanameliser-editor-plus/releases)から最新リリースをダウンロード
2. パッケージをUnityプロジェクトにインポート

## 機能

### Mesh Info Display

選択したオブジェクトとその子オブジェクトのメッシュ情報をシーンビュー左上に表示します。
ポリゴン数・マテリアル数・マテリアルスロット数・メッシュ数を確認できます。

#### NDMFプレビュー対応

NDMFプレビューがアクティブな場合、AAO/TTT/Meshiaなどの最適化結果を確認しながら調整できます。

- 最適化前後のメッシュ数の差分を並列表示
- NDMFプロキシメッシュを自動検出し、緑のドットでプレビュー中であることを表示

表示切替: `Tools > Kanameliser Editor Plus > Show Mesh Info Display`

### Toggle Objects Active

GameObjectのアクティブ状態とEditorOnlyタグをすばやく切り替えます。

ショートカット: `Ctrl+G`

### Component Manager

選択したオブジェクトとその子オブジェクトの全コンポーネントを一覧表示します。

- 特定のオブジェクトやコンポーネントタイプを検索
- どのコンポーネントがどのオブジェクトに付いているかを瞬時に確認
- 複数オブジェクトを同時に選択して一括編集
- 不要なコンポーネントを簡単に一括削除

アクセス: `Tools > Kanameliser Editor Plus > Component Manager`

### Material Copier

複数選択したGameObjectから同名のGameObjectへマテリアルをコピー&ペーストできます。

- FBXセットアップや衣装のカラーバリエーション対応が一瞬で可能
- MeshRendererとSkinnedMeshRendererの両方に対応

#### 使い方

1. コピー元のGameObjectを選択（複数選択可）→ 右クリック → `Copy Materials`
2. コピー先のGameObjectを選択 → 右クリック → `Paste Materials`

#### マッチング仕様

コピー元とコピー先のオブジェクトは、以下の優先順位で自動的に対応付けられます。いずれもRendererを持つオブジェクトのみが対象です。

1. **相対パスの完全一致（ルート名を除く）** — `Outfit/Jacket` → `Outfit/Jacket`
2. **同じ階層深度での名前一致** — 親の名前が異なっていても、同名かつ同じ深度のオブジェクトを対応付け
   - 例: `Outfit_A/Outer/Accessories/Earing`（深度3）→ `Outfit_B/Inner/Accessories/Earing`（深度3）
3. **名前一致（深度不問）** — 複数候補がある場合はもっとも近い深度を優先
   - 例: `Outfit/Outer/Jacket`（深度3）→ `Jacket`（深度1）
4. **大文字小文字を区別しない名前一致** — `earing` → `Earing`

複数の候補が残る場合は、親階層の名前を下から順にマッチングして絞り込みます。それでも決まらない場合は、ルート名のLevenshtein距離で最終選択します。

この仕様はMA Material Helperのマッチングでも共通です。

アクセス: ヒエラルキー右クリック `Kanameliser Editor Plus > Copy/Paste Materials`

### MA Material Helper

Modular Avatarのマテリアル制御コンポーネントを使用した色変更メニューを自動生成します。カラーバリエーションのPrefabから数クリックで色変更メニューを作成でき、複数のPrefabからの同時生成にも対応しています。

使用条件: [Modular Avatar](https://modular-avatar.nadena.dev/ja) 1.13.0以上

#### 使い方

1. カラーバリエーションPrefabを選択 → 右クリック → `Copy Material Setup`（複数選択可）
2. ターゲットの衣装を選択 → 右クリック → 以下のいずれかを選択:
   - `Create Material Setter` — 各スロットに直接マテリアルを設定（推奨）
   - `[Optional] Create Material Setter (All Slots)` — すべてのスロットにSetterを作成（パフォーマンスに影響する可能性があるため、カスタマイズしたい場合のみ推奨）
   - `Create Material Swap` — マテリアルの置き換えルールで設定
   - `[Optional] Create Material Swap (Per Object)` — オブジェクト毎に個別のSwapを作成

番号付きカラーバリエーション（Color1、Color2など）を含む「Color Menu」が自動作成されます。

#### Material SetterとMaterial Swapの違い

基本的にはMaterial Setterを推奨します。

- **Material Setter**: スロット単位で設定するため、同一メッシュ内で同じマテリアルからそれぞれ異なるマテリアルへの変更が可能
- **Material Swap**: マテリアル名ベースで置き換えるため、同一マテリアルは同じ変更先になる。シンプルな構成向け

#### どちらを選ぶべきか

**Material Setterを使う場合（ほとんどのケース）:**

- 同一メッシュ内の異なるスロットで同じマテリアルを使用しているが、それぞれ別のマテリアルに変更したい
- コピー元のマテリアル配置をより正確に再現したい
- Material Swapで意図しない動作が発生する場合

**Material Swapを使う場合:**

- 同一メッシュ内で同じマテリアルスロットが変更先含めすべて同じマテリアルを使用している
- シンプルなマテリアル構成

Material Swapでは以下のようなケースに対応できません:

```
メッシュA:
  スロット0: マテリアルX → マテリアルY に変更したい
  スロット1: マテリアルX → マテリアルZ に変更したい
```

Material Swapは「マテリアルX」を1つのマテリアルにしか置き換えられないため、両スロットが同じ結果になります。Material Setterならスロット単位で異なるマテリアルを指定できます。

アクセス: ヒエラルキー右クリック `Kanameliser Editor Plus > Copy Material Setup / Create Material Setter / Create Material Swap`

### AO Bounds Setter

複数メッシュのAnchor Override、Root Bone、Boundsを一括設定します。衣装製作やアバター制作時に便利です。

1. Root Objectフィールドにヒエラルキーのオブジェクトをドラッグ
2. Anchor Override・Root Bone・Boundsを設定
   - ドロップダウンの検索機能でルートオブジェクト配下のボーン/アンカーをすばやく選択できます
3. チェックボックスで対象メッシュを選択し、「Apply to Selected Meshes」で一括適用

オブジェクト名やAnchor Override、Root Boneのラベルをクリックすると、ヒエラルキー内でそのオブジェクトを選択できます。

アクセス: `Tools > Kanameliser Editor Plus > AO Bounds Setter`

### Missing BlendShape Inserter

アニメーションファイル間で不足しているBlendShapeキーを自動検出・補完します。
すべてのアニメーションが同じBlendShapeを操作するようにファイルを更新することで、異なる表情状態間のスムーズな遷移を確保します。複数ファイルの一括処理にも対応しています。

主な使用場面:

- アバターの顔のBlendShapeを変更したが、表情アニメーションで使用していない場合
- ジェスチャーなどによる表情アニメーション切り替え時に表情が崩れる場合
- すべてのアニメーションファイルで一貫したBlendShape操作を確保したい場合

この機能はkb10uyさんの[Zatools](https://zatools.kb10uy.dev/)に多言語化対応され統合されています。詳しくは[Zatools/missing-blendshape-inserter](https://zatools.kb10uy.dev/editor-extension/missing-blendshape-inserter/)を参照してください。

アクセス: `Tools > Kanameliser Editor Plus > Missing BlendShape Inserter`

## 必要環境

- Unity 2022.3.22f1以上
- オプション: NDMF（ビルドプレビュー機能拡張）
- オプション: Modular Avatar 1.13.0以上（MA Material Helper機能に必要）

## 貢献

お気軽にIssueまたはPull Requestを送信してください。

## ライセンス

MITライセンス — 詳細はLICENSEファイルをご覧ください。

## 連絡先

質問やフィードバックがある場合は、GitHubでIssueを開くか、Xでお問い合わせください。
