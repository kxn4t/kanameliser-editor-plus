# Kanameliser Editor Plus

UnityおよびVRChatのための便利なエディタ拡張機能セット。

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

### 🔄 Material Swap Helper

- Modular AvatarのMaterial Swapコンポーネントを使用した色変更メニューの自動作成
- カラーバリエーションのPrefabから数クリックでメニューアイテムを生成
- 複数のカラーバリエーションPrefabから同時にメニュー生成が可能
- 2つの作成モード：統合型と個別オブジェクト型のMaterial Swapコンポーネント
- [Modular Avatar](https://modular-avatar.nadena.dev/ja) 1.13.0以上のインストールが必要
- ヒエラルキー上右クリックメニューからアクセス：`Kanameliser Editor Plus > Copy/Create Material Swap`

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

### Material Swap Helper

既存のカラーバリエーションからアバターの色変更メニューを作成するのに最適：

1. **コピー**: カラーバリエーションPrefabを選択 → 右クリック → `Copy Material Setup`  
  Tips: カラーバリエーションPrefabを複数選択しても動作します
2. **作成**: ターゲットとなる衣装を選択 → 右クリック → `Create Material Swap` または `Create Material Swap (Per Object)`
3. **メニュー生成**: 番号付きカラーバリエーション（Color1、Color2など）を含む「Color Menu」を自動作成

**作成モード:**
- **標準モード**: パフォーマンスが良い統合型Material Swapコンポーネントを作成
- **個別オブジェクトモード**: 各オブジェクト毎に個別のMaterial Swapコンポーネントを作成（複雑なセットアップに有用）

よくある使用例：
- アバター衣装の色変更メニューの作成
- 既存のカラーバリエーションPrefabからの色変更メニューの一括作成

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