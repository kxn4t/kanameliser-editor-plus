# Changelog / 更新履歴

🇬🇧 **English** | 🇯🇵 **日本語**

All notable changes to this project will be documented in this file.
このプロジェクトの注目すべき変更はすべてこのファイルに記録されます。

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
フォーマットは [Keep a Changelog](https://keepachangelog.com/en/1.0.0/) に基づいており、
このプロジェクトは [Semantic Versioning](https://semver.org/spec/v2.0.0.html) に準拠しています。

## [Unreleased]

## [0.5.0] - 2026-04-23

### Improved

- **MA Material Helper / Material Copier: Object Matching Algorithm** — Exact name matches work the same as before. When no exact match is found, the algorithm now tries score-based similar-name matching as a last resort, so color variants and numbered duplicates can be matched where possible
  - **Color variants** — `Ribbon_blue` ↔ `Ribbon_red`, `Kutsu kuro` ↔ `Kutsu shiro`, `top-blue` ↔ `top-red`
  - **Numbered/versioned duplicates** — `Body_01` ↔ `Body_02`, `Skirt (1)` ↔ `Skirt (2)`, `Hair.001` ↔ `Hair.002`
  - **Same-name disambiguation** — When multiple objects share the same name (e.g., `Mesh` under `Jacket/`, `Skirt/`, `Boots/`), parent hierarchy similarity is used to match them correctly

---

### 改善

- **MA Material Helper / Material Copier: オブジェクトマッチングアルゴリズム** — 名前が完全一致するものは従来通りにマッチング。完全一致が見つからない場合に、最終手段としてスコアリングに基づく類似名マッチングを試みるようになり、色違いや番号違いもなるべく自動的に対応付けされるように改善しました
  - **色違い** — `Ribbon_blue` ↔ `Ribbon_red`、`Kutsu kuro` ↔ `Kutsu shiro`、`top-blue` ↔ `top-red`
  - **番号・バージョン違い** — `Body_01` ↔ `Body_02`、`Skirt (1)` ↔ `Skirt (2)`、`Hair.001` ↔ `Hair.002`
  - **同名オブジェクトの振り分け** — `Jacket/Mesh`・`Skirt/Mesh`・`Boots/Mesh`のように同じ名前のオブジェクトが複数ある場合、親の階層構造の類似度で正しく対応付けるように改善

## [0.4.1] - 2025-10-19

### Added

- **MA Material Helper: Material Swap Limitation Detection**
  - Added automatic detection of Material Swap limitations
  - Detects when the same material in a mesh needs to be swapped to different materials in different slots
  - User can choose to continue with Material Swap or cancel to use Material Setter instead
  - Cancel option uses Undo to cleanly remove all created objects

### Fixed

- **MA Material Helper: Fixed Parameter Name Conflicts in Multiple Color Menus**
  - Fixed issue where creating Material Setter/Swap menus on multiple objects would cause parameter conflicts
    - Now allows independent color changes on multiple objects
  - Sub-menus within the same Color Menu now share the same parameter
    - Now allows mixing Material Setter and Material Swap changes simultaneously
  - When adding to an existing Color Menu, the most commonly used parameter is now reused

---

### 追加

- **MA Material Helper: Material Swap制限の自動検出**
  - Material Swapの制限を自動検出する機能を追加
  - 同じメッシュ内の同じマテリアルを異なるスロットで別のマテリアルに変更しようとする場合を検出
  - ユーザーはMaterial Swapを続行するか、キャンセルしてMaterial Setterを使用するか選択可能
  - キャンセルした場合はUndoを使用してすべての作成オブジェクトを削除します

### 修正

- **MA Material Helper: 複数のColor Menuでのパラメーター名競合を修正**
  - 複数のオブジェクトでMaterial Setter/Swapメニューを作成するとパラメーターが競合する問題を修正
    - 複数のオブジェクトで個別に色変更ができるようになりました
  - 同じColor Menu内のサブメニューは同じパラメーターを共有するようになりました
    - MaterialSetterとMaterialSwapによる変更を混ぜて同時に利用できるようになりました
  - 既存のColor Menuに追加する場合、もっとも多く使用されているパラメーターを再利用するようになりました

## [0.4.0] - 2025-10-12

### Added

- **AO Bounds Setter: Batch Anchor Override and Bounds Configuration**
  - Added AO Bounds Setter window for batch configuration of Anchor Override, Root Bone, and Bounds on meshes
  - Access via `Tools > Kanameliser Editor Plus > AO Bounds Setter`

- **MA Material Helper: Material Setter Support**
  - Added Material Setter automatic generation feature alongside existing Material Swap
  - Allows changing from the same source material to different materials within the same mesh by specifying per-slot
  - More accurately reproduces the material layout of the source prefab compared to Material Swap
  - Context menu: `Kanameliser Editor Plus > Create Material Setter`

- **MA Material Helper: Material Setter All Slots Mode**
  - Added All Slots mode to Material Setter for cases where all material slots need to be set regardless of changes
  - Standard mode only sets material slots that differ from current materials
  - Context menu: `[Optional] Create Material Setter (All Slots)`

- **MA Material Helper: Material Swap vs Material Setter - Usage Guide**
  - **Material Setter (Recommended)**:
    - Sets materials per material slot, ensuring accurate material placement
    - Handles cases where the same source material maps to different target materials within the same mesh
    - More reliable for complex prefab structures with mixed materials
    - **Use Material Setter when in doubt - it works in all scenarios**
  - **Material Swap (Optional)**:
    - Swaps materials by material reference, simpler configuration
    - Suitable when each source material consistently maps to one target material across all meshes
    - May not work correctly if the same source material needs to map to different targets in different slots
    - Use `[Optional] Create Material Swap (Per Object)` for more granular control if needed

### Improved

- **Material Copier & Material Swap Helper: Enhanced Object Matching Algorithm**
  - Improved 4-stage priority matching system
  - Added hierarchy depth tracking for better matching accuracy
  - Added parent hierarchy filtering to narrow down candidates
  - Added Levenshtein distance-based similarity scoring as final tiebreaker
  - Added rootObjectName tracking for multiple object copy operations
  - Updated Priority 2 from "same directory + cleaned name" to "same depth + exact name"
  - Added Priority 4: Case-insensitive name matching
  - Fixed issue where same-name objects in different branches could match incorrectly
  - Improved matching accuracy for hierarchies with different depths

### Changed

- **MA Material Helper: Refactored codebase**
  - Renamed from "Material Swap Helper" to "MA Material Helper" to reflect expanded functionality
  - Reorganized namespace from `MaterialSwapHelper` to `MAMaterialHelper`
  - Extracted common functionality into shared modules:
    - `ObjectMatcher` - Object matching logic for both Material Swap and Material Setter
    - `GenerationResult` - Unified result structure
    - `ModularAvatarIntegration` - Enhanced MA component integration with parameter support
  - Improved code maintainability and reusability

- **MA Material Helper: Menu Reorganization**
  - Reordered menu items to prioritize Material Setter as the recommended option
  - Added `[Optional]` prefix to special case options (All Slots mode, Per Object mode)
  - Material Setter is now recommended for most use cases

---

### 追加

- **AO Bounds Setter: Anchor OverrideとBoundsの一括設定**
  - メッシュのAnchor Override、Root Bone、Boundsを一括設定するAO Bounds Setterウィンドウを追加
  - `Tools > Kanameliser Editor Plus > AO Bounds Setter` からアクセス

- **MA Material Helper: Material Setter対応**
  - 既存のMaterial Swapに加えてMaterial Setter自動生成機能を追加
  - スロット単位で指定するため、同じメッシュ内で同一の元マテリアルから異なるマテリアルへの変更が可能
  - Material Swapより正確にコピー元Prefabのマテリアル配置を再現
  - 右クリックメニュー：`Kanameliser Editor Plus > Create Material Setter`

- **MA Material Helper: Material Setter全スロットモード**
  - 変更の有無に関係なく全マテリアルスロットを設定する必要がある場合のために全スロットモードを追加
  - 標準モードは現在のマテリアルと異なるスロットのみを設定
  - 右クリックメニュー：`[Optional] Create Material Setter (All Slots)`

- **MA Material Helper: Material Swap と Material Setter - 使い分けガイド**
  - **Material Setter（推奨）**:
    - マテリアルスロット単位で設定するため、正確なマテリアル配置を保証
    - 同じメッシュ内で同一の元マテリアルが異なるターゲットマテリアルに対応するケースに対応
    - 混合マテリアルを含む複雑なPrefab構造でより信頼性が高い
    - **迷ったら Material Setter を使用してください - すべてのシナリオで動作します**
  - **Material Swap（オプション）**:
    - マテリアル参照による置換、よりシンプルな設定
    - 各元マテリアルがすべてのメッシュで一貫して1つのターゲットマテリアルに対応する場合に適しています
    - 同じ元マテリアルが異なるスロットで異なるターゲットに対応する必要がある場合、正しく動作しない可能性があります
    - 必要に応じて `[Optional] Create Material Swap (Per Object)` でより細かい制御が可能

### 改善

- **Material Copier & Material Swap Helper: オブジェクトマッチングアルゴリズムを強化**
  - 4段階優先度マッチングシステムを改善
  - マッチング精度向上のため階層深度追跡を追加
  - 候補を絞り込むための親階層フィルタリングを追加
  - 最終的な判定基準としてLevenshtein距離ベースの類似度スコアリングを追加
  - 複数オブジェクトコピー操作用のrootObjectName追跡を追加
  - 優先度2を「同階層+クリーニング後の名前」から「同じ深さ+完全名前一致」に変更
  - 優先度4を追加: 大文字小文字を区別しない名前マッチング
  - 異なるブランチ内の同名オブジェクトが誤マッチする問題を修正
  - 異なる深さの階層でのマッチング精度を向上

### 変更

- **MA Material Helper: コードベースをリファクタリング**
  - 拡張された機能を反映するため「Material Swap Helper」から「MA Material Helper」に名称変更
  - 名前空間を `MaterialSwapHelper` から `MAMaterialHelper` に再編成
  - 共通機能を共有モジュールに抽出:
    - `ObjectMatcher` - Material SwapとMaterial Setter両方のオブジェクトマッチングロジック
    - `GenerationResult` - 統合された結果構造
    - `ModularAvatarIntegration` - パラメーター対応を含むMAコンポーネント統合の強化
  - コードの保守性と再利用性を向上

- **MA Material Helper: メニュー構成の変更**
  - Material Setterを推奨オプションとして優先するようにメニュー項目を並び替え
  - 特殊なケース向けオプション（全スロットモード、個別オブジェクトモード）に `[Optional]` 接頭辞を追加
  - ほとんどのケースでMaterial Setterが推奨されるようになりました

## [0.3.1] - 2025-09-07

### Fixed

- **Material Swap Helper: Fixed Object Matching Issues**
  - Fixed incorrect matching with Armature bones that have similar names to mesh objects
  - Fixed matching algorithm
  - **Priority 1**: Exact relative path match + Renderer present
  - **Priority 2**: Same directory + cleaned name match + Renderer present
  - **Priority 3**: Exact name match + Renderer present
  - **Priority 4**: Cleaned name match + Renderer present

---

### 修正

- **Material Swap Helper: オブジェクトマッチングの問題を修正**
  - メッシュオブジェクトと類似名のArmatureボーンとの誤マッチング問題を修正
  - マッチングアルゴリズムを修正
  - **優先度1**: 相対パスの完全一致 + Renderer有り
  - **優先度2**: 同階層でクリーニング後の名前一致 + Renderer有り
  - **優先度3**: 名前の完全一致 + Renderer有り
  - **優先度4**: クリーニング後の名前一致 + Renderer有り

## [0.3.0] - 2025-07-14

### Added

- **Material Copier** — Copy materials between GameObjects via right-click context menu
  - (Step1) Select source objects in Hierarchy → Right-click → Copy Materials
  - (Step2) Select target objects → Right-click → Paste Materials
  - Context menu: `Kanameliser Editor Plus > Copy/Paste Materials`

- **Material Swap Helper** — Automatic color change menu creation using Modular Avatar
  - Creates color variation menus from color prefabs with automatic object matching
  - Two creation modes: unified Material Swap components and per-object components
  - (Step1) Select color variation prefabs → Right-click → Copy Material Setup
  - (Step2) Select target avatar → Right-click → Create Material Swap
  - Context menu: `Kanameliser Editor Plus > Copy Material Setup / Create Material Swap`
  - Requires Modular Avatar 1.13.0 or later to be installed

---

### 追加

- **Material Copier** — 右クリックメニューからGameObject間でマテリアルをコピー
  - (Step1) ヒエラルキーでコピー元オブジェクトを選択 → 右クリック → Copy Materials
  - (Step2) ペースト先オブジェクトを選択 → 右クリック → Paste Materials
  - コンテキストメニュー：`Kanameliser Editor Plus > Copy/Paste Materials`

- **Material Swap Helper** — Modular Avatarを使用した色変更メニュー自動生成機能
  - カラーバリエーションのPrefabから色変更メニューを作成
  - 統合型と個別オブジェクト型の2つのMaterial Swapコンポーネント作成モード
  - (Step1) カラーバリエーションPrefabを選択 → 右クリック → Copy Material Setup
  - (Step2) ターゲットAvatarを選択 → 右クリック → Create Material Swap
  - コンテキストメニュー：`Kanameliser Editor Plus > Copy Material Setup / Create Material Swap`
  - 利用にはModular Avatar 1.13.0以上のインストールが必要

## [0.3.0-rc.3] - 2025-07-13

### Fixed

- Corrected Modular Avatar minimum version requirement from 1.8.0 to 1.13.0

---

### 修正

- Modular Avatar最小バージョン要件を1.8.0から1.13.0に修正

## [0.3.0-rc.2] - 2025-07-13

### Fixed

- Added missing Modular Avatar minimum version requirement (1.8.0+) to assembly definition

---

### 修正

- アセンブリ定義に不足していたModular Avatar最小バージョン要件（1.8.0以上）を追加

## [0.3.0-rc.1] - 2025-07-13

### Added

- **Material Copier** — Copy materials between GameObjects via right-click context menu
  - (Step1) Select source objects in Hierarchy → Right-click → Copy Materials
  - (Step2) Select target objects → Right-click → Paste Materials
  - Context menu: `Kanameliser Editor Plus > Copy/Paste Materials`

- **Material Swap Helper** — Automatic color change menu creation using Modular Avatar
  - Creates color variation menus from color prefabs with automatic object matching
  - Two creation modes: unified Material Swap components and per-object components
  - (Step1) Select color variation prefabs → Right-click → Copy Material Setup
  - (Step2) Select target avatar → Right-click → Create Material Swap
  - Context menu: `Kanameliser Editor Plus > Copy Material Setup / Create Material Swap`
  - Requires Modular Avatar package to be installed

---

### 追加

- **Material Copier** — 右クリックメニューからGameObject間でマテリアルをコピー
  - (Step1) ヒエラルキーでコピー元オブジェクトを選択 → 右クリック → Copy Materials
  - (Step2) ペースト先オブジェクトを選択 → 右クリック → Paste Materials
  - コンテキストメニュー：`Kanameliser Editor Plus > Copy/Paste Materials`

- **Material Swap Helper** — Modular Avatarを使用した色変更メニュー自動生成機能
  - カラーバリエーションのPrefabから色変更メニューを作成
  - 統合型と個別オブジェクト型の2つのMaterial Swapコンポーネント作成モード
  - (Step1) カラーバリエーションPrefabを選択 → 右クリック → Copy Material Setup
  - (Step2) ターゲットAvatarを選択 → 右クリック → Create Material Swap
  - コンテキストメニュー：`Kanameliser Editor Plus > Copy Material Setup / Create Material Swap`
  - 利用にはModular Avatarパッケージのインストールが必要

## [0.2.1] - 2025-07-12

### Fixed

- Fixed incorrect changelog URL in package.json

---

### 修正

- package.jsonのchangelog URLの修正

## [0.2.0] - 2025-07-12

### Added

- Added changelog documentation (CHANGELOG.md)
- Added NDMF preview support to Mesh Info Display
  - Shows optimized mesh information when NDMF preview is active
  - Preview effects of optimization tools like AAO and TexTransTool before building
  - Green dot indicator appears when preview is active
  - Only available when NDMF is installed

### Changed

- Improved mesh analysis accuracy for complex hierarchies
- Optimized update cycles for better editor performance
- Better error handling and reliability

### Technical

- Split monolithic MeshInfoDisplay into specialized classes:
  - `MeshInfoCalculator` - Core calculation logic
  - `MeshInfoConstants` - UI constants and styling
  - `MeshInfoData` - Data structures
  - `MeshInfoNDMFIntegration` - NDMF integration
  - `MeshInfoRenderer` - UI rendering
  - `MeshInfoUtility` - Utility functions
- Added `NDMFPreviewHelper` for NDMF preview integration
- Conditional compilation support for optional NDMF dependency
- Assembly definition updates with version define detection

---

### 追加

- 変更履歴ドキュメント（CHANGELOG.md）を追加
- Mesh Info DisplayにNDMFプレビュー対応を追加
  - NDMFプレビュー有効時に最適化後のメッシュ情報を表示
  - AAOやTexTransToolなどの最適化ツールの効果を事前確認可能に
  - プレビュー有効時は左上に緑のドットで表示
  - NDMFがインストールされている場合のみ利用可能

### 変更

- 複雑な階層におけるメッシュ解析精度の向上
- エディタパフォーマンス向上のための更新サイクル最適化
- エラーハンドリングと信頼性の向上

### 技術的変更

- モノリシックなMeshInfoDisplayを各クラスに分割:
  - `MeshInfoCalculator` - コア計算ロジック
  - `MeshInfoConstants` - UI定数とスタイリング
  - `MeshInfoData` - データ構造
  - `MeshInfoNDMFIntegration` - NDMF統合
  - `MeshInfoRenderer` - UIレンダリング
  - `MeshInfoUtility` - ユーティリティ関数
- NDMFプレビュー統合のための`NDMFPreviewHelper`を追加
- NDMF依存関係の条件付きコンパイル
- バージョン定義検出を含むアセンブリ定義の更新

## [0.1.1] - 2025-05-30

### Added

- Japanese README documentation (README.ja.md)

### Changed

- Updated installation instructions for clarity
- Improved README documentation with usage tips

---

### 追加

- 日本語READMEドキュメント（README.ja.md）

### 変更

- 明確性のためのインストール手順の更新
- 使用上のヒントを含むREADMEドキュメントの改善

## [0.1.0] - 2024-05-27

Initial release of Kanameliser Editor Plus.

### Added

- **Mesh Info Display** — Real-time mesh information display in Scene view with polygon and material counting
- **Toggle Objects Active** — GameObject active state and EditorOnly tag toggling with Ctrl+G shortcut
- **Component Manager** — Batch component operations with component listing and management across object hierarchies, with search functionality
- **Missing BlendShape Inserter** — BlendShape key insertion for animation files for animation compatibility
- VRChat Creator Companion package support

---

Kanameliser Editor Plusの初回リリース。

### 追加

- **Mesh Info Display** — シーンビューでのリアルタイムメッシュ情報表示（ポリゴン・マテリアルカウント機能付き）
- **Toggle Objects Active** — Ctrl+Gショートカット付きGameObjectアクティブ状態とEditorOnlyタグの切り替え
- **Component Manager** — オブジェクト階層全体でのコンポーネント一覧と管理、検索機能付き一括コンポーネント操作
- **Missing BlendShape Inserter** — アニメーション互換性のためのアニメーションファイルへのBlendShapeキー挿入
- VRChat Creator Companionパッケージサポート
