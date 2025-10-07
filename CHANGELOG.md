# Changelog / 更新履歴

🇬🇧 **English** | 🇯🇵 **日本語**

All notable changes to this project will be documented in this file.  
このプロジェクトの注目すべき変更はすべてこのファイルに記録されます。

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).  
フォーマットは [Keep a Changelog](https://keepachangelog.com/en/1.0.0/) に基づいており、
このプロジェクトは [Semantic Versioning](https://semver.org/spec/v2.0.0.html) に準拠しています。

## [Unreleased] / [未リリース]

### Added / 追加

- **MA Material Helper: Material Setter All Slots Mode**
  **MA Material Helper: Material Setter 全スロットモード**

  - Added All Slots mode to Material Setter for cases where all material slots need to be set regardless of changes  
    変更の有無に関係なく全マテリアルスロットを設定する必要がある場合のために全スロットモードを追加
  - Standard mode only sets material slots that differ from current materials  
    標準モードは現在のマテリアルと異なるスロットのみを設定
  - Context menu: `[Optional] Create Material Setter (All Slots)`  
    右クリックメニュー：`[Optional] Create Material Setter (All Slots)`

- **MA Material Helper: Material Swap vs Material Setter - Usage Guide**
  **MA Material Helper: Material Swap と Material Setter - 使い分けガイド**

  - **Material Setter (Recommended / 推奨)**:
    - Sets materials per material slot, ensuring accurate material placement  
      マテリアルスロット単位で設定するため、正確なマテリアル配置を保証
    - Handles cases where the same source material maps to different target materials within the same mesh  
      同じメッシュ内で同一の元マテリアルが異なるターゲットマテリアルに対応するケースに対応
    - More reliable for complex prefab structures with mixed materials  
      混合マテリアルを含む複雑なPrefab構造でより信頼性が高い
    - **Use Material Setter when in doubt - it works in all scenarios**  
      **迷ったら Material Setter を使用してください - すべてのシナリオで動作します**

  - **Material Swap (Optional / オプション)**:
    - Swaps materials by material reference, simpler configuration  
      マテリアル参照による置換、よりシンプルな設定
    - Suitable when each source material consistently maps to one target material across all meshes  
      各元マテリアルがすべてのメッシュで一貫して1つのターゲットマテリアルに対応する場合に適しています
    - May not work correctly if the same source material needs to map to different targets in different slots  
      同じ元マテリアルが異なるスロットで異なるターゲットに対応する必要がある場合、正しく動作しない可能性があります
    - Use `[Optional] Create Material Swap (Per Object)` for more granular control if needed  
      必要に応じて `[Optional] Create Material Swap (Per Object)` でより細かい制御が可能

### Changed / 変更

- **MA Material Helper: Menu Reorganization**
  **MA Material Helper: メニュー構成の変更**

  - Reordered menu items to prioritize Material Setter as the recommended option  
    Material Setterを推奨オプションとして優先するようにメニュー項目を並び替え
  - Added `[Optional]` prefix to special case options (All Slots mode, Per Object mode)  
    特殊なケース向けオプション（全スロットモード、個別オブジェクトモード）に`[Optional]`接頭辞を追加
  - Material Setter is now recommended for most use cases  
    ほとんどのケースでMaterial Setterが推奨されるようになりました

## [0.3.2-beta.3] - 2025-10-03

### Added / 追加

- **MA Material Helper: Material Setter Support**  
  **MA Material Helper: Material Setter 対応**

  - Added Material Setter automatic generation feature alongside existing Material Swap  
    既存のMaterial Swapに加えてMaterial Setter自動生成機能を追加
  - Allows changing from the same source material to different materials within the same mesh by specifying per-slot  
    スロット単位で指定するため、同じメッシュ内で同一の元マテリアルから異なるマテリアルへの変更が可能
  - More accurately reproduces the material layout of the source prefab compared to Material Swap  
    Material Swapより正確にコピー元Prefabのマテリアル配置を再現
  - Context menu: `Kanameliser Editor Plus > Create Material Setter`  
    右クリックメニュー：`Kanameliser Editor Plus > Create Material Setter`

### Improved / 改善

- **Material Copier & Material Swap Helper: Enhanced Object Matching Algorithm**  
  **Material Copier & Material Swap Helper: オブジェクトマッチングアルゴリズムを強化**

  - Improved 4-stage priority matching system  
    4段階優先度マッチングシステムを改善
  - Added hierarchy depth tracking for better matching accuracy  
    マッチング精度向上のため階層深度追跡を追加
  - Added parent hierarchy filtering to narrow down candidates  
    候補を絞り込むための親階層フィルタリングを追加
  - Added Levenshtein distance-based similarity scoring as final tiebreaker  
    最終的な判定基準としてLevenshtein距離ベースの類似度スコアリングを追加
  - Added rootObjectName tracking for multiple object copy operations  
    複数オブジェクトコピー操作用のrootObjectName追跡を追加
  - Updated Priority 2 from "same directory + cleaned name" to "same depth + exact name"  
    優先度2を「同階層+クリーニング後の名前」から「同じ深さ+完全名前一致」に変更
  - Added Priority 4: Case-insensitive name matching  
    優先度4を追加: 大文字小文字を区別しない名前マッチング
  - Fixed issue where same-name objects in different branches could match incorrectly  
    異なるブランチ内の同名オブジェクトが誤マッチする問題を修正
  - Improved matching accuracy for hierarchies with different depths  
    異なる深さの階層でのマッチング精度を向上

### Changed / 変更

- **MA Material Helper: Refactored codebase**  
  **MA Material Helper: コードベースをリファクタリング**

  - Renamed from "Material Swap Helper" to "MA Material Helper" to reflect expanded functionality  
    拡張された機能を反映するため「Material Swap Helper」から「MA Material Helper」に名称変更
  - Reorganized namespace from `MaterialSwapHelper` to `MAMaterialHelper`  
    名前空間を `MaterialSwapHelper` から `MAMaterialHelper` に再編成
  - Extracted common functionality into shared modules:  
    共通機能を共有モジュールに抽出:
    - `ObjectMatcher` - Object matching logic for both Material Swap and Material Setter  
      `ObjectMatcher` - Material SwapとMaterial Setter両方のオブジェクトマッチングロジック
    - `GenerationResult` - Unified result structure  
      `GenerationResult` - 統合された結果構造
    - `ModularAvatarIntegration` - Enhanced MA component integration with parameter support  
      `ModularAvatarIntegration` - パラメーター対応を含むMAコンポーネント統合の強化
  - Improved code maintainability and reusability  
    コードの保守性と再利用性を向上

## [0.3.1] - 2025-09-07

### Fixed / 修正

- **Material Swap Helper: Fixed Object Matching Issues**  
  **Material Swap Helper: オブジェクトマッチングの問題を修正**
  - Fixed incorrect matching with Armature bones that have similar names to mesh objects  
    メッシュオブジェクトと類似名のArmatureボーンとの誤マッチング問題を修正
  - Fixed matching algorithm  
    マッチングアルゴリズムを修正
  - **Priority 1**: Exact relative path match + Renderer present  
    **優先度1**: 相対パスの完全一致 + Renderer有り
  - **Priority 2**: Same directory + cleaned name match + Renderer present  
    **優先度2**: 同階層でクリーニング後の名前一致 + Renderer有り
  - **Priority 3**: Exact name match + Renderer present  
    **優先度3**: 名前の完全一致 + Renderer有り
  - **Priority 4**: Cleaned name match + Renderer present  
    **優先度4**: クリーニング後の名前一致 + Renderer有り

## [0.3.0] - 2025-07-14

### Added / 追加

- Material Copier feature for copying materials between GameObjects via right-click context menu  
  右クリックメニューからGameObject間でマテリアルをコピーするMaterial Copier機能を追加
- Usage:
  - (Step1) Select source objects in Hierarchy → Right-click → Copy Materials
  - (Step2) Select target objects → Right-click → Paste Materials  
  使用方法：  
  - (Step1) ヒエラルキーでコピー元オブジェクトを選択 → 右クリック → Copy Materials
  - (Step2) ペースト先オブジェクトを選択 → 右クリック → Paste Materials
- Context menu : `Kanameliser Editor Plus > Copy/Paste Materials`  
  コンテキストメニュー：`Kanameliser Editor Plus > Copy/Paste Materials`

- Material Swap Helper feature for automatic color change menu creation using Modular Avatar  
  Modular Avatarを使用した色変更メニュー自動生成機能Material Swap Helperを追加
- Creates color variation menus from color prefabs with automatic object matching  
  カラーバリエーションのPrefabから色変更メニューを作成
- Two creation modes: unified Material Swap components and per-object components  
  統合型と個別オブジェクト型の2つのMaterial Swapコンポーネント作成モード
- Usage:
  - (Step1) Select color variation prefabs → Right-click → Copy Material Setup
  - (Step2) Select target avatar → Right-click → Create Material Swap  
  使用方法：  
  - (Step1) カラーバリエーションPrefabを選択 → 右クリック → Copy Material Setup
  - (Step2) ターゲットAvatarを選択 → 右クリック → Create Material Swap
- Context menu: `Kanameliser Editor Plus > Copy Material Setup / Create Material Swap`  
  コンテキストメニュー：`Kanameliser Editor Plus > Copy Material Setup / Create Material Swap`
- Requires Modular Avatar 1.13.0 or later to be installed  
  利用にはModular Avatar 1.13.0以上のインストールが必要


## [0.3.0-rc.3] - 2025-07-13

### Fixed / 修正

- Corrected Modular Avatar minimum version requirement from 1.8.0 to 1.13.0  
  Modular Avatar最小バージョン要件を1.8.0から1.13.0に修正

## [0.3.0-rc.2] - 2025-07-13

### Fixed / 修正

- Added missing Modular Avatar minimum version requirement (1.8.0+) to assembly definition  
  アセンブリ定義に不足していたModular Avatar最小バージョン要件（1.8.0以上）を追加

## [0.3.0-rc.1] - 2025-07-13

### Added / 追加

- Material Copier feature for copying materials between GameObjects via right-click context menu  
  右クリックメニューからGameObject間でマテリアルをコピーするMaterial Copier機能を追加
- Usage:
  - (Step1) Select source objects in Hierarchy → Right-click → Copy Materials
  - (Step2) Select target objects → Right-click → Paste Materials  
  使用方法：  
  - (Step1) ヒエラルキーでコピー元オブジェクトを選択 → 右クリック → Copy Materials
  - (Step2) ペースト先オブジェクトを選択 → 右クリック → Paste Materials
- Context menu : `Kanameliser Editor Plus > Copy/Paste Materials`  
  コンテキストメニュー：`Kanameliser Editor Plus > Copy/Paste Materials`

- Material Swap Helper feature for automatic color change menu creation using Modular Avatar  
  Modular Avatarを使用した色変更メニュー自動生成機能Material Swap Helperを追加
- Creates color variation menus from color prefabs with automatic object matching  
  カラーバリエーションのPrefabから色変更メニューを作成
- Two creation modes: unified Material Swap components and per-object components  
  統合型と個別オブジェクト型の2つのMaterial Swapコンポーネント作成モード
- Usage:
  - (Step1) Select color variation prefabs → Right-click → Copy Material Setup
  - (Step2) Select target avatar → Right-click → Create Material Swap  
  使用方法：  
  - (Step1) カラーバリエーションPrefabを選択 → 右クリック → Copy Material Setup
  - (Step2) ターゲットAvatarを選択 → 右クリック → Create Material Swap
- Context menu: `Kanameliser Editor Plus > Copy Material Setup / Create Material Swap`  
  コンテキストメニュー：`Kanameliser Editor Plus > Copy Material Setup / Create Material Swap`
- Requires Modular Avatar package to be installed  
  利用にはModular Avatarパッケージのインストールが必要

## [0.2.1] - 2025-07-12

### Fixed / 修正

- Fixed incorrect changelog URL in package.json  
  package.jsonのchangelog URLの修正

## [0.2.0] - 2025-07-12

### Added / 追加

- Added changelog documentation (CHANGELOG.md)  
  変更履歴ドキュメント（CHANGELOG.md）を追加
- Added NDMF preview support to Mesh Info Display  
  Mesh Info DisplayにNDMFプレビュー対応を追加
- Shows optimized mesh information when NDMF preview is active  
  NDMFプレビュー有効時に最適化後のメッシュ情報を表示
- Preview effects of optimization tools like AAO and TexTransTool before building  
  AAOやTexTransToolなどの最適化ツールの効果を事前確認可能に
- Green dot indicator appears when preview is active  
  プレビュー有効時は左上に緑のドットで表示
- Only available when NDMF is installed  
  NDMFがインストールされている場合のみ利用可能

### Changed / 変更

- Improved mesh analysis accuracy for complex hierarchies  
  複雑な階層におけるメッシュ解析精度の向上
- Optimized update cycles for better editor performance  
  エディタパフォーマンス向上のための更新サイクル最適化
- Better error handling and reliability  
  エラーハンドリングと信頼性の向上

### Technical / 技術的変更

- Split monolithic MeshInfoDisplay into specialized classes:  
  モノリシックなMeshInfoDisplayを各クラスに分割:
  - `MeshInfoCalculator` - Core calculation logic / コア計算ロジック
  - `MeshInfoConstants` - UI constants and styling / UI定数とスタイリング
  - `MeshInfoData` - Data structures / データ構造
  - `MeshInfoNDMFIntegration` - NDMF integration / NDMF統合
  - `MeshInfoRenderer` - UI rendering / UIレンダリング
  - `MeshInfoUtility` - Utility functions / ユーティリティ関数
- Added `NDMFPreviewHelper` for NDMF preview integration  
  NDMFプレビュー統合のための`NDMFPreviewHelper`を追加
- Conditional compilation support for optional NDMF dependency  
  NDMF依存関係の条件付きコンパイル
- Assembly definition updates with version define detection  
  バージョン定義検出を含むアセンブリ定義の更新

## [0.1.1] - 2025-05-30

### Added / 追加

- Japanese README documentation (README.ja.md)  
  日本語READMEドキュメント（README.ja.md）

### Changed / 変更

- Updated installation instructions for clarity  
  明確性のためのインストール手順の更新
- Improved README documentation with usage tips  
  使用上のヒントを含むREADMEドキュメントの改善

## [0.1.0] - 2024-05-27

### Added / 追加

- Initial release of Kanameliser Editor Plus  
  Kanameliser Editor Plusの初回リリース
- Mesh Info Display feature with polygon and material counting  
  ポリゴンとマテリアルカウント機能付きMesh Info Display
- Toggle Objects Active functionality with Ctrl+G shortcut  
  Ctrl+Gショートカット付きToggle Objects Active機能
- Component Manager for batch component operations  
  一括コンポーネント操作のためのComponent Manager
- Missing BlendShape Inserter for animation compatibility  
  アニメーション互換性のためのMissing BlendShape Inserter
- VRChat Creator Companion package support  
  VRChat Creator Companionパッケージサポート

### Features / 機能

- Real-time mesh information display in Scene view  
  シーンビューでのリアルタイムメッシュ情報表示
- GameObject active state and EditorOnly tag toggling  
  GameObjectアクティブ状態とEditorOnlyタグの切り替え
- Component listing and management across object hierarchies  
  オブジェクト階層全体でのコンポーネント一覧と管理
- BlendShape key insertion for animation files  
  アニメーションファイルのBlendShapeキー挿入
- Search functionality in Component Manager  
  Component Managerの検索機能
