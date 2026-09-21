# Component Copier 仕様

衣装やアバターに付いているコンポーネントを、別の衣装やアバターへコピーするツール。設定値だけでなく、コンポーネント内のオブジェクト参照（ボーン、コライダー、Constraint のソースなど）もコピー先の対応するオブジェクトへ置き換える。

- メニュー: `Tools/Kanameliser Editor Plus/Component Copier`
- 名前空間: `Kanameliser.EditorPlus.ComponentCopier`
- UI: UI Toolkit + USS
- このディレクトリ（`design~/`）は `~` 末尾のため、Unity と vpm-packager の両方から無視される

## 目的と優先順位

1. 同じ衣装の更新版や、別アバター対応版へ設定を確実に引き継ぐ（最優先）
2. 一部のコンポーネントだけをコピーする
3. 左右対称のコピー（左半身の設定を右半身へ）
4. 正規表現で対象を絞る（CopyComponentsByRegex 相当）

参照の誤対応は動作に直接響くため、類似名による対応は自動で適用せず、候補として見せて確認させる。

## 中心になる考え方

コピー元 Transform からコピー先 Transform への対応表を 1 つ作り、次の 2 か所で同じ表を使う。

- コンポーネントをどのオブジェクトに付けるか
- コンポーネント内の参照をどこへ向け直すか

ユースケースごとの違いは、対応表の作り方と、何をコピーするかのフィルターだけになる。

| ユースケース | 対応表の作り方 | フィルター |
| --- | --- | --- |
| 衣装の更新版へ引き継ぎ | パス、名前、Humanoid 辞書 | 既定の除外以外すべて |
| 別アバター対応版へ引き継ぎ | 同上、要確認候補と手動補正を併用 | 同上 |
| 一部だけコピー | 同上 | 種類別、個別チェック、検索 |
| 正規表現で指定 | 同上 | パスと型名への正規表現 |
| 左右ミラー（フェーズ 2） | 同一ルート内で L↔R の名前変換 | 片側のコンポーネントだけ自動抽出 |

## 画面

```
[ Language ▾ ]                                                    [↻]
コピー元  [ Costume_v1 (Prefab Asset)                          ⊙ ]
   ↓
コピー先  [ Costume_v2                                         ⊙ ]
──────────────────────────────────────────────
[ 検索 / .*regex ]  (PhysBone) (Constraint) (MA) (すべて)   ☐ その他も表示
▶ ☑ VRCPhysBone            12   新規 9 · 上書き 3
▶ ☑ VRCPhysBoneCollider     4   新規 4               ⚠ 1
▶ ☐ VRCParentConstraint     2
──────────────────────────────────────────────
▼ 対応付け   41/46 対応済み   ? 3   ✖ 2
──────────────────────────────────────────────
▼ 設定   既存 [上書き ▾]  ☑ 足りないオブジェクトを作成  座標 [自動 ▾]
                                  [ 差分チェックのみ ]  [ 適用 ]
```

### コピー元とコピー先

- コピー元とコピー先のフィールドは上下に並べる
- コピー元は、Hierarchy のオブジェクト、Prefab インスタンス、Assets 内の Prefab のどれでも指定できる。指定した時点でスキャンして一覧を表示する
- コピー先はシーン上のオブジェクトに限定する（Prefab インスタンスと Prefab Stage 内のオブジェクトは含む）。アセットへ直接適用すると Undo できないため、`EditorUtility.IsPersistent` に該当するものは拒否し、フィールドを空に戻してインラインで警告を表示する
- コピー元と同じオブジェクト、コピー元の内側にあるオブジェクトはコピー先にできない
- 「差分チェックのみ」は読み取り専用なので、コピー先にもアセットを指定できる

### コンポーネント一覧

- 種類ごとのグループにまとめ、既定では閉じた状態で表示する
- グループのヘッダーには、3 状態チェックボックス、型アイコン、件数、状態の要約（新規、上書きなど）、警告バッジを置く
- ヘッダーは CVG Creator の `CreateCollapsibleGroup` と同じ自前実装にする（Alt+クリックで全開閉）
- 行は、チェック、相対パス、状態チップの構成。パスをクリックするとそのオブジェクトを Ping する
- プリセットは複数選択できるチップ
  - PhysBone: VRCPhysBone と VRCPhysBoneCollider
  - Constraint: VRC Constraint と Unity Constraint
  - MA: `nadena.dev.modular_avatar` 名前空間のコンポーネント
  - すべて: 既定の除外以外すべて
- 「その他も表示」は、既定で対象外の型も一覧に並べるトグル。オンにすると淡色のセクションに並び、選べるようになる
- 既定で対象外の型: Transform、Renderer 系、MeshFilter、Animator、VRCAvatarDescriptor、PipelineManager
- PipelineManager を選んだときは、Blueprint ID まで複製される旨を警告する
- 検索ボックスは部分一致と正規表現（トグル）に対応し、相対パスと型名を対象にする

### リフレッシュ

- `ObjectChangeEvents.changesPublished`、`EditorApplication.hierarchyChanged`、`Undo.undoRedoPerformed` を購読する
- 軽い差分キー（`ComponentKey` の集合）が変わったときだけ再スキャンする
- チェック、開閉、検索の状態は `ComponentKey` で引き継ぐ
- 手動のリフレッシュボタンも置く

### 対応付けセクション

- 表示するのは、選択中のコンポーネントが載っているオブジェクトと、その参照先だけ
- 要対応（要確認、未対応）の行を上に出し、確定済みの行は閉じたサブグループにまとめる
- 各行は、スコア順の候補を並べたドロップダウンと、自由に指定できる ObjectField を持つ
- 親を手動補正したら、その配下を再解決する
- 共通の prefix / suffix を検出したら、まとめて確定できる
- 手動の対応付けは自動の結果と別の層に持ち、リフレッシュでは消さない

### グローバル設定

| 設定 | 選択肢 | 既定 |
| --- | --- | --- |
| 既存の同種コンポーネント | 上書き / 追加 / スキップ / 置き換え（削除してからコピー） | 上書き |
| 足りないオブジェクトを作成 | オン / オフ | オン |
| 座標の扱い | 自動 / ローカル値のまま | 自動 |

- 上書きはコンポーネントを作り直さないので、他からの参照が切れない
- 同じオブジェクトに同じ型が複数あるときは、並び順で対応させる
- 置き換えで削除する範囲は、その型のコピーを受け取るオブジェクトの上だけ
- 置き換えで消えるコンポーネントを、コピー対象でないコンポーネントが参照している場合は、適用前に警告する
- 設定は EditorPrefs に保存する

## 対応付けのアルゴリズム

ルートから下へ、対応済みの親の子どうしで解決を進める。最初に同名の子だけを全階層でたどって確定させ（同名の兄弟は並び順で対応）、残りを上から順に次の判定で解決する。親が未対応のときは、いちばん近い対応済みの祖先の対応先を基準にする。

| 順 | 判定 | 状態 |
| --- | --- | --- |
| 1 | 相対パスが完全一致 | 確定 |
| 2 | 対応済みの親（または祖先）の直下にある同名の子 | 確定 |
| 3 | 両側に Humanoid の Animator があり、同じ HumanBodyBones に当たる | 確定 |
| 4 | 同義語辞書で同じグループになり、両側で一意 | 確定 |
| 5 | 完全同名が両側で 1 つだけある | 確定 |
| 6 | 辞書や同名で複数の候補に当たる | 要確認 |
| 7 | 共通の prefix / suffix を除くと一致（3 件以上で共通ルールとして検出） | 要確認（まとめて確定できる） |
| 8 | UpperChest がなく Chest へ寄せる | 要確認 |
| 9 | 正規化名が一致（大文字小文字、`.001`、`_01` など） | 要確認 |
| 10 | ファジー一致 | 候補のみ（事前選択しない） |

対応表の状態は、確定、要確認、未対応、手動の 4 つ。自動で使うのは確定と手動だけ。手動で対応先を「なし」にすると、対応先がないものとして扱う。確定した対応先は、他のコピー元には割り当てない。

オブジェクトの作成予定は、対応表ではなく `CopyPlan` 側で持つ。付ける先が未対応なら作成し、要確認なら作成せずにブロックする（提案が正しい相手かもしれないため）。

### Humanoid 同義語辞書

Merge Armatures Tool の `core/bone_mappings.py` と `core/armature.py` から移植する。元のリストは MIT（bdunderscore、HhotateA、Azukimochi）なので、ソースに帰属表記を入れる。

- 完全一致を先に引き、次に正規化名（小文字化し、`.`、空白、`_` を除去）で引く
- 正規化で複数のグループに潰れる名前（`Left leg` と `LeftLeg` など）は「一致なし」にする
- 揺れもの用のボーン（Breast、Hips の揺れ用、Twist）は辞書に載せず、名前一致でだけ対応させる。移植元の除外リストのうち、Humanoid のグループと重なっていたのは `Bust` だけだったので、`Bust` を Chest のグループから外している

## 参照の置き換え

- `SerializedObject` の全 ObjectReference プロパティを汎用に走査する。型ごとの処理は書かず、SDK へのハード依存を避ける
- 参照先の場所で扱いを分ける

| 参照先 | 扱い |
| --- | --- |
| コピー元ルート内の GameObject / Transform / Component | 対応表で置き換える |
| 同時にコピーするコンポーネント | 新しく作られる側へ向ける |
| アセット（マテリアルなど） | そのまま残す |
| コピー元ルート外のシーンオブジェクト | そのまま残し、警告を出す |

- 2 パスで処理する。先に全コンポーネントを生成または特定し、その後に参照を置き換える
- 参照の期待値は、コピー先側のキー（オブジェクトまたは作成予定のパス、型、順番）で持つ。計画を作る時点では、これから作るコンポーネントの実体がないため
- 参照先のコンポーネントがコピー対象外で、コピー先にも相当するものがない場合は、追加を提案する
- 未解決の参照は空にする。コピー元を指したままの参照は残さない。対応付けセクションで手動指定すれば解決できる
- 未解決の参照があるコンポーネントをまるごとスキップする選択肢は、MVP には入れない
- 付ける先のオブジェクトがない場合は、対応済みの親の下に同じローカル TRS で作成する
- MA の `AvatarObjectReference` は `referencePath` 文字列を持つので、専用ハンドラーを用意する（フェーズ 2）
- 適用は 1 つの Undo グループにまとめる

## 座標の扱い

- 別階層へのコピーは、ローカル値をそのままコピーするのを既定にする
- MVP では、ボーンの軸の向きやスケールが大きく違うときに警告するだけにとどめる
- フェーズ 2 で、ルート空間で見たボーンの回転差を使って、オフセットの向きだけを補正する。数度程度の差はフィッティング調整とみなして無視する
- スケールは自動補正しない
- ミラーは、ルートの YZ 平面で鏡映してから、コピー先のローカル空間へ戻す
- 軸補正とミラーには、どのプロパティが位置、回転、長さなのかを示す型ごとの表（`SpatialPropertyTable`）が必要。型名とプロパティパスの文字列で持つ

## 左右の名前反転（フェーズ 2）

Blender の `BLI_string_flip_side_name` に合わせる。

1. 末尾の `.001` は切り離し、反転後に付け直す
2. 末尾が「区切り文字（`.` `_` `-` 空白）+ `L`/`R`」なら入れ替える（大文字小文字は維持）
3. 先頭が「`L`/`R` + 区切り文字」なら入れ替える
4. 先頭または末尾の `left`/`right` を入れ替える（`left`/`Left`/`LEFT` の形は維持）
5. 置換は 1 か所だけで、上の順に優先する

拡張として、`Skirt_L_01` や `Skirt_Left_01` のような中間のトークンも扱う。どのルールでも、反転した名前のオブジェクトが一意で存在するときだけ採用する。名前が変わらないものは自分自身に対応させる。

## 差分チェック

`CopyPlan`（コンポーネントごとの期待値）を純関数で作り、Apply と差分チェックの両方で使う。期待値は、コピー元の値に、参照の置換と座標の補正を適用したもの。

- 適用前: プレビューとして使う。同一の行は淡色にして自動でスキップする
- 適用後: 自動で検証して結果を表示する
- 単体: コピーせずに比較だけ行う

分類は次の 6 つ。

- 一致
- 値が違う
- 参照が違う
- 参照が未解決（コピー元の参照に対応先がなく、引き継げない）
- コピー先にない
- コピー先にだけある

「コピー先にだけある」は、コピー対象の型について、コピー元の対応するオブジェクトに同じコンポーネントがないものを指す。単に選択しなかっただけのコンポーネントは含めない。

行を開くとプロパティ単位の差分を表示する。float は許容誤差つきで比較し、`m_Script` などの内部プロパティは無視する。

## 多言語化

- 既存の `Editor/Localization/Localization.cs` と 5 言語の `.po` を使う。キーの接頭辞は `componentCopier.`、ツールチップは `:tooltip`
- 固定ラベルは `ndmf-tr` クラス + キー。ルートで `LocalizeUIElements(root)` を必ず呼ぶ（フォントの適用を兼ねる）
- 動的な文言は `S(key, args)` で作り、`RegisterLanguageChangeCallback` でモデルから描き直す
- Core 層は文字列を返さず、enum と引数で返す。UI 層でローカライズする
- 英語のままにするもの: MenuItem のパス、Undo グループ名、コンソールログ、コンポーネントの型名、プリセット名の `PhysBone` / `Constraint` / `MA`
- ラベルに固定幅を指定しない

## 構成

```
Editor/ComponentCopier/
  ComponentCopierWindow.cs                 lifecycle, layout, language callback
  ComponentCopierWindow.Source.cs          source/target fields, validation, refresh
  ComponentCopierWindow.ComponentList.cs   type groups, presets, search
  ComponentCopierWindow.Mapping.cs         mapping section, manual correction
  ComponentCopierWindow.Report.cs          pre-check and diff report
  ComponentCopierWindow.uss
  Core/
    ComponentKey.cs            (relativePath, typeFullName, indexAmongSameType)
    ComponentScanner.cs        root → entries
    HumanoidBoneDictionary.cs  ported data, normalize, ambiguous-name exclusion
    TransformMapper.cs         top-down resolver chain → TransformMap
    TransformMap.cs            auto results + manual overrides (separate layers)
    ReferenceWalker.cs         SerializedProperty walk, reference classification
    CopyPlan.cs / CopyPlanBuilder.cs
    CopyExecutor.cs            single Undo group, two-pass
    CopyVerifier.cs            plan vs actual → DiffReport
    CopySettings.cs            EditorPrefs-backed
Editor/Tests/ComponentCopier/
```

UI は partial class で分割し、Core は UI に依存しない。

## フェーズ

1. MVP: コピー元とコピー先、一覧（プリセット、検索、正規表現）、リフレッシュ、辞書を含む対応付けと手動補正、グローバル設定、汎用の参照置換、オブジェクトの作成、差分チェック、Undo、多言語化
2. `SpatialPropertyTable`、軸の自動補正、左右ミラー、MA 用ハンドラー、カスタムの名前変換ルール
3. Transform 値（ボーン調整）の引き継ぎ、対応表の保存と再利用
