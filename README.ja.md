# Kanameliser Editor Plus

UnityおよびVRChatのための便利なエディター拡張機能セット。

## 🚩 インストール方法

### VRChat Creator Companion経由（推奨）

1. [https://kxn4t.github.io/vpm-repos/](https://kxn4t.github.io/vpm-repos/) にアクセス
2. 「Add to VCC」ボタンをクリックして、「Kanameliser VPM Packages」リポジトリをVCCまたはALCOMに追加
3. Manage Projectからパッケージ一覧上の「Kanameliser Editor Plus」をプロジェクトに追加

### 手動インストール

手動インストールする場合：

1. [GitHub Releases](https://github.com/kxn4t/kanameliser-editor-plus/releases) から最新リリースをダウンロード
2. パッケージをUnityプロジェクトにインポート

## 📌 機能

### 🔍 Mesh Info Display

- 選択したオブジェクトとその子オブジェクトの詳細なメッシュ情報を表示
- ポリゴン数、マテリアル数、マテリアルスロット、メッシュ数を表示
- シーンビューの左上に表示
- `Tools > Kanameliser Editor Plus > Show Mesh Info Display` で表示/非表示を切り替え

#### 🔮 NDMFプレビュー対応

- **ビルド時統計**: NDMFプレビューがアクティブな時、プレビュー表示状態のメッシュデータを表示
- **最適化の可視化**: 元のメッシュ数と最適化後のメッシュ数を視覚的な差分と併せて並列表示
- **プレビュー中検出**: NDMFプロキシメッシュを自動検出し、緑のドットでプレビュー中かわかりやすく

### 🔄 Toggle Objects Active

- GameObjectのアクティブ状態とEditorOnlyタグをすばやく切り替え
- ショートカット：`Ctrl+G`

### 🧩 Component Manager

- 選択したオブジェクトとその子オブジェクトのすべてのコンポーネントをリスト表示
- 特定のオブジェクトやコンポーネントタイプを検索
- どのコンポーネントがどのオブジェクトに付いているかを瞬時に確認
- 複数のオブジェクトを同時に選択して一括編集
- 不要なコンポーネントを簡単に一括削除
- `Tools > Kanameliser Editor Plus > Component Manager` からアクセス

### 🎨 Material Copier

- 複数選択したGameObjectから同名のGameObjectへマテリアルをコピー&ペースト
- FBXセットアップ時や、衣装対応時のカラーバリエーション対応が一瞬で可能に
- MeshRendererとSkinnedMeshRendererコンポーネントの両方に対応
- ヒエラルキー上右クリックメニューからアクセス：`Kanameliser Editor Plus > Copy/Paste Materials`

### 🔄 MA Material Helper

Modular Avatarのマテリアル制御コンポーネントを使用した色変更メニューの自動作成ツール。

#### Material Setter（ほとんどのケースで推奨）
- Modular AvatarのMaterial Setterコンポーネントを使用した色変更メニューの自動作成
- カラーバリエーションのPrefabから直接マテリアルを設定するメニューを生成
- Material Swapと異なり、メッシュの各マテリアルスロットに対して直接置き換える方式で動作
  - スロット単位で指定するため、同じメッシュ内で同一の元マテリアルから異なるマテリアルへの変更が可能
  - Material Swapより正確にコピー元Prefabのマテリアル配置を再現
- 2つの作成モード：
  - **標準モード**: 現在のマテリアルと異なるマテリアルスロットのみにSetterを作成
  - **全スロットモード**: 現在のマテリアルとの差異に関係なくすべてのマテリアルスロットにSetterを作成

#### Material Swap
- Modular AvatarのMaterial Swapコンポーネントを使用した色変更メニューの自動作成
- カラーバリエーションのPrefabから数クリックでメニューアイテムを生成
- 複数のカラーバリエーションPrefabから同時にメニュー生成が可能
- 2つの作成モード：統合型と個別オブジェクト型のMaterial Swapコンポーネント

**共通仕様:**
- [Modular Avatar](https://modular-avatar.nadena.dev/ja) 1.13.0以上のインストールが必要
- ヒエラルキー上右クリックメニューからアクセス：`Kanameliser Editor Plus > Copy Material Setup / Create Material Setter / Create Material Swap`

### 🎯 AO Bounds Setter

- 複数のメッシュのAnchor Override、Root Bone、Boundsを一括設定
- `Tools > Kanameliser Editor Plus > AO Bounds Setter` からアクセス

### 😀 Missing BlendShape Inserter

- アニメーションファイル間で不足しているBlendShapeキーを自動検出して修正
- シェイプキーによる表情の破綻や破損したアニメーションを防止
- 複数のアニメーションファイルの一括処理をサポート
- `Tools > Kanameliser Editor Plus > Missing BlendShape Inserter` からアクセス
- この機能は拡張ワークフローオプションのため [Zatools](https://zatools.kb10uy.dev/) とも統合されています
- 詳しい説明は [Zatools/missing-blendshape-inserter](https://zatools.kb10uy.dev/editor-extension/missing-blendshape-inserter/) を参照してください

## 🔧 使用上のヒント

### Material Copier

複数の類似オブジェクト間でのマテリアル管理に最適：

1. **コピー**: コピー元GameObjectを選択（例：Avatar_A、Avatar_B）→ 右クリック → `Copy Materials`
2. **ペースト**: コピー先GameObjectを選択（例：Avatar_C、Avatar_D）→ 右クリック → `Paste Materials`
3. **マッチング**: 同名のオブジェクトにマテリアルが適用されます（例："Head" → "Head"、"Body" → "body"）

よくある使用例：
- 異なるアバターバリアント間での衣装マテリアルのコピー

### MA Material Helper

既存のカラーバリエーションからアバターの色変更メニューを作成するのに最適：

1. **コピー**: カラーバリエーションPrefabを選択 → 右クリック → `Copy Material Setup`
  Tips: カラーバリエーションPrefabを複数選択しても動作します
2. **作成**: ターゲットとなる衣装を選択 → 右クリック → 以下のいずれかを選択
   - `Create Material Setter` - Material Setterモード（ほとんどのケースで推奨）
   - `[Optional] Create Material Setter (All Slots)` - Material Setterモード（全スロット）
   - `Create Material Swap` - 標準のMaterial Swapモード
   - `[Optional] Create Material Swap (Per Object)` - 個別オブジェクトのMaterial Swapモード
3. **メニュー生成**: 番号付きカラーバリエーション（Color1、Color2など）を含む「Color Menu」を自動作成

**Material Setter モード:**
- **標準モード**（推奨）: 現在のマテリアルと異なるマテリアルスロットのみを設定
- **全スロットモード**: 現在のマテリアルとの差異に関係なくすべてのマテリアルスロットを設定
  - パフォーマンスに影響を及ぼす可能性があるため、自分でカスタマイズしたい場合のみ利用を推奨
- マテリアルを直接置き換える方式で、各オブジェクトにMaterial Setterコンポーネントを作成

**Material Swap モード:**
- **標準モード**: Material Swapコンポーネントを作成
- **個別オブジェクトモード**: 各オブジェクト毎に個別のMaterial Swapコンポーネントを作成（複雑なセットアップに有用）

**Material SwapとMaterial Setterの使い分け:**

どちらのモードを使うべきか迷ったら、以下を参考にしてください。  
基本的にはMaterial Setterを推奨します。

**Material Swapを使う場合:**
- 同一メッシュ内で同じマテリアルスロットが変更先含めすべて同じマテリアルを使用している
- シンプルなマテリアル構成

**Material Setterを使う場合:**
- 同一メッシュ内の異なるスロットで同じマテリアルを使用しているが、それぞれ別のマテリアルに変更したい
- より正確にコピー元のマテリアル配置を再現したい
- Material Swapで意図しない動作が発生する場合

**Material Swapの制限**
```
メッシュA:
  スロット0: マテリアルX → マテリアルY に変更したい
  スロット1: マテリアルX → マテリアルZ に変更したい
```
この場合、Material Swapでは「マテリアルX」を1つのマテリアルにしか置き換えられないため、両方のスロットが同じマテリアルになってしまいます。  
Material Setterではスロット単位で指定できるため、それぞれ異なるマテリアルに変更できます。

**マッチング仕様:**

コピー元とコピー先のオブジェクトのマッチングは以下の優先順位で行われます：

1. **優先度1**: 相対パスの完全一致（ルート名除外）+ Renderer有り
   - 例: コピー元 `Outfit/Jacket` → ターゲット `Outfit/Jacket`
   - 例: コピー元 `Accessories/Earing/Left` → ターゲット `Accessories/Earing/Left`

2. **優先度2**: 同じ階層深度 + 完全名前一致 + Renderer有り
   - 例: コピー元 `Outfit_A/Outer/Accessories/Earing`（深度=3）→ ターゲット `Outfit_B/Inner/Accessories/Earing`（深度=3）
   - 親の名前が異なっていても、どちらも `Earing` という名前で同じ深度
   - 同じ深度に複数候補がある場合は階層フィルタリングを適用

3. **優先度3**: 完全名前一致 + Renderer有り
   - 例: コピー元 `Outfit/Outer/Jacket`（深度=3）→ ターゲット `Jacket`（深度=1）
   - 複数の `Jacket` が存在する場合、もっとも近い深度を選択
   - コピー元が深度3で、ターゲット候補が深度1, 2, 4の場合: 深度2または4を優先（差=1）
   - 複数候補がある場合は階層フィルタリングを適用

4. **優先度4**: 大文字小文字を区別しない名前一致 + Renderer有り
   - 例: コピー元 `earing` → ターゲット `Earing`
   - 例: コピー元 `JACKET` → ターゲット `Jacket`
   - もっとも近い深度を選択し、階層フィルタリングを適用

**追加のマッチング:**

優先度マッチング後も複数候補が残る場合：

- **親階層フィルタリング**: 下から上へ親の名前をマッチング
  - 例: `Outfit/Outer/Jacket` を探している場合
    - 候補: `Outfit/Outer/Jacket`、`Costume/Outer/Jacket`、`Outfit/Inner/Jacket`
    - 直接の親でフィルター: `Outer` 配下を優先 → 2候補が残る
    - そのさらに親でフィルター: `Outfit` 配下を優先 → `Outfit/Outer/Jacket` が選択される
- **類似度スコアリング**: Levenshtein距離を使用した最終選択
  - 例: `Outfit_C` にペーストする場合、`Outfit_A` と `Outfit_C` 由来の候補がある
    - ルート名を比較: `Outfit_C` vs `Outfit_A` と `Outfit_C` vs `Outfit_C`
    - `Outfit_C` 由来のコピー元（距離=0）を `Outfit_A` 由来（距離=2）より優先

よくある使用例：
- アバター衣装の色変更メニューの作成
- 既存のカラーバリエーションPrefabからの色変更メニューの一括作成

### AO Bounds Setter

衣装製作やアバター制作時の一括設定に最適：

1. **ウィンドウを開く**: `Tools > Kanameliser Editor Plus > AO Bounds Setter`
2. **ルートオブジェクトを選択**: ヒエラルキー上のオブジェクトをRoot Objectフィールドにドラッグ
3. **設定**:
   - **Anchor Override**: アンカーポイントとして使用するオブジェクトを設定
     - ドロップダウンでルートオブジェクト配下のオブジェクトを検索・選択可能
   - **Root Bone**（SkinnedMeshRendererのみ）: SkinnedMeshのルートボーンを設定
     - ドロップダウンでルートオブジェクト配下のオブジェクトを検索・選択可能
   - **Bounds**（SkinnedMeshRendererのみ）: Boundsを設定
4. **メッシュを選択**: チェックボックスを使用して設定を適用するメッシュを選択
5. **適用**:「Apply to Selected Meshes」をクリックして設定を一括適用

**ヒント:**
- オブジェクト名、Anchor Override、Root Boneのラベルをクリックすると、そのオブジェクトをヒエラルキー内で素早く選択できます
- ドロップダウンの検索機能を使用して、ボーン/アンカーを素早く見つけられます
- 設定はチェックされたメッシュにのみ適用されます

### Missing BlendShape Inserter

複数のAnimationClip間で変更したBlendShapeの対象を標準化するツールです。主に以下の場合に使用します：

- アバターの顔のBlendShapeを変更したけど、表情アニメーションで使用していない場合
- ジェスチャーなどによる表情アニメーション切り替え時に表情が崩れたり破綻する場合
- すべてのアニメーションファイルで一貫したBlendShape操作を確保する必要がある場合

このツールは、すべてのアニメーション間で同じBlendShapeを操作するようにアニメーションファイルを更新することで、表情の歪みを防止し、異なる表情状態間のスムーズな遷移を確保します。

## 📋 必要環境

- Unity 2022.3.22f1以上
- オプション: NDMF（Non-Destructive Modular Framework）でビルドプレビュー機能拡張
- オプション: Modular Avatar 1.13.0以上（Material Swap Helper機能に必要）

## 🤝 貢献

お気軽にIssueまたはPull Requestを送信してください。

## 📄 ライセンス

MITライセンス - 詳細はLICENSEファイルをご覧ください。

## 👋 連絡先

質問やフィードバックがある場合は、GitHubでissueを開くか、Xでお問い合わせください。