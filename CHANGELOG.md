# Changelog / 更新履歴

🇬🇧 **English** | 🇯🇵 **日本語**

All notable changes to this project will be documented in this file.  
このプロジェクトの注目すべき変更はすべてこのファイルに記録されます。

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).  
フォーマットは [Keep a Changelog](https://keepachangelog.com/en/1.0.0/) に基づいており、
このプロジェクトは [Semantic Versioning](https://semver.org/spec/v2.0.0.html) に準拠しています。

## [Unreleased] / [未リリース]

## [0.2.0] - 2025-07-12

### Added / 追加

- Added changelog documentation (CHANGELOG.md)  
  変更履歴ドキュメント（CHANGELOG.md）を追加
- Added NDMF preview support to Mesh Info Display  
  Mesh Info Display に NDMF プレビュー対応を追加
- Shows optimized mesh information when NDMF preview is active  
  NDMF プレビュー有効時に最適化後のメッシュ情報を表示
- Preview effects of optimization tools like AAO and TexTransTool before building  
  AAO や TexTransTool などの最適化ツールの効果を事前確認可能に
- Green dot indicator appears when preview is active  
  プレビュー有効時は左上に緑のドットで表示
- Only available when NDMF is installed  
  NDMF がインストールされている場合のみ利用可能

### Changed / 変更

- Improved mesh analysis accuracy for complex hierarchies  
  複雑な階層におけるメッシュ解析精度の向上
- Optimized update cycles for better editor performance  
  エディタパフォーマンス向上のための更新サイクル最適化
- Better error handling and reliability  
  エラーハンドリングと信頼性の向上

### Technical / 技術的変更

- Split monolithic MeshInfoDisplay into specialized classes:  
  モノリシックな MeshInfoDisplay を各クラスに分割:
  - `MeshInfoCalculator` - Core calculation logic / コア計算ロジック
  - `MeshInfoConstants` - UI constants and styling / UI 定数とスタイリング
  - `MeshInfoData` - Data structures / データ構造
  - `MeshInfoNDMFIntegration` - NDMF integration / NDMF 統合
  - `MeshInfoRenderer` - UI rendering / UI レンダリング
  - `MeshInfoUtility` - Utility functions / ユーティリティ関数
- Added `NDMFPreviewHelper` for NDMF preview integration  
  NDMF プレビュー統合のための`NDMFPreviewHelper`を追加
- Conditional compilation support for optional NDMF dependency  
  NDMF 依存関係の条件付きコンパイル
- Assembly definition updates with version define detection  
  バージョン定義検出を含むアセンブリ定義の更新

## [0.1.1] - 2025-05-30

### Added / 追加

- Japanese README documentation (README.ja.md)  
  日本語 README ドキュメント（README.ja.md）

### Changed / 変更

- Updated installation instructions for clarity  
  明確性のためのインストール手順の更新
- Improved README documentation with usage tips  
  使用上のヒントを含む README ドキュメントの改善

## [0.1.0] - 2024-05-27

### Added / 追加

- Initial release of Kanameliser Editor Plus  
  Kanameliser Editor Plus の初回リリース
- Mesh Info Display feature with polygon and material counting  
  ポリゴンとマテリアルカウント機能付き Mesh Info Display
- Toggle Objects Active functionality with Ctrl+G shortcut  
  Ctrl+G ショートカット付き Toggle Objects Active 機能
- Component Manager for batch component operations  
  一括コンポーネント操作のための Component Manager
- Missing BlendShape Inserter for animation compatibility  
  アニメーション互換性のための Missing BlendShape Inserter
- VRChat Creator Companion package support  
  VRChat Creator Companion パッケージサポート

### Features / 機能

- Real-time mesh information display in Scene view  
  シーンビューでのリアルタイムメッシュ情報表示
- GameObject active state and EditorOnly tag toggling  
  GameObject アクティブ状態と EditorOnly タグの切り替え
- Component listing and management across object hierarchies  
  オブジェクト階層全体でのコンポーネント一覧と管理
- BlendShape key insertion for animation files  
  アニメーションファイルの BlendShape キー挿入
- Search functionality in Component Manager  
  Component Manager の検索機能
