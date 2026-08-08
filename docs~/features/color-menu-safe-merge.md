# Merge Skinned Mesh (Color Menu Safe)

Material Setter/Swapによる色変更メニューを壊さずに、AAO: Avatar OptimizerのMerge Skinned Meshを作成します。アバター内の色変更の内容を解析し、統合すると色変更が他のメッシュへ波及してしまうマテリアルだけを自動でスロット統合から除外します。

**必要条件:** [AAO: Avatar Optimizer](https://vpm.anatawa12.com/avatar-optimizer/ja/) 1.8.0以上、[Modular Avatar](https://modular-avatar.nadena.dev/ja) 1.13.0以上

## 背景

AAOのTrace and Optimizeによる自動メッシュ統合は、現状、マテリアル差し替えアニメーションが付いたメッシュを対象外にします。Material Setter/Swapはビルド時にこのアニメーションを生成するため、色変更メニューの対象メッシュは自動では統合されず、統合するには手動でMerge Skinned Meshを構成する必要があります。

ただし手動で統合する場合、同じマテリアルを使うスロット同士が1つのスロットにまとめられるため、片方のメッシュだけを対象にした色変更が統合相手のメッシュにも適用されてしまうことがあります。これを防ぐには対象マテリアルの「統合する」チェックを外す必要がありますが、どのマテリアルが該当するかはすべてのMaterial Setter/Swapを確認しないと分かりません。この判定と設定を自動化するのが本機能です。

## 使い方

1. 統合したいメッシュ（SkinnedMeshRenderer・MeshRenderer）を複数選択
2. 右クリック → `Create Merge Skinned Mesh (Color Menu Safe)`

選択したメッシュを統合するMerge Skinned Meshオブジェクトが共通の親の下に作成されます。アバター内のMaterial Setter/Swap（非アクティブなメニューオブジェクト上のものも含む）を解析し、統合すると色変更が壊れるマテリアルはあらかじめ「統合する」チェックが外された状態になります。除外されたマテリアルはコンソールログで確認できます。

MA Object Toggleと組み合わせる場合向けに、2つのバリエーションがあります:

| コマンド | 説明 |
|---|---|
| `Create Merge Skinned Mesh (Exclude Object Toggle)` | 選択したメッシュのうち、アバター内のMA Object Toggleの対象（配下を含む）を自動で除外して統合します |
| `Create Merge Skinned Mesh (From Object Toggle)` | MA Object Toggleが付いたオブジェクトを右クリックして実行します。トグルに設定されたオブジェクト配下のメッシュを、トグルのON/OFF設定ごとに統合します。トグルが統合オブジェクトに届かない場合は、統合オブジェクトを自動でトグルの対象に追加します |

## 除外の判定ルール

同じマテリアルを使うスロット同士は、統合時に1つのスロットにまとめられます。まとめても色変更の挙動が変わらないかどうかを次のルールで判定します:

- すべてのMaterial Setter/Swapが、そのマテリアルのスロット全部を**同じように変更する**（あるいはまったく変更しない）→ 統合を許可
- いずれかのコンポーネントが**一部のスロットだけを変更する、もしくは別々の変更先に変える** → そのマテリアルを除外

例: メッシュAとメッシュBが同じマテリアルGrayを使っている場合

| 色変更の構成 | 判定 |
|---|---|
| 1つのSetterがA・B両方のGrayをWhiteに変更 | 統合OK（除外なし） |
| AのGrayをWhiteに、BのGrayをBlueに変更 | Grayを除外 |
| AのGrayだけをWhiteに変更（Bは変更なし） | Grayを除外 |
| Root未設定（アバター全体）のSwapでGray→Whiteに変更 | 統合OK（除外なし） |
| RootにメッシュAのみを含むSwapでGray→Whiteに変更 | Grayを除外 |

除外されたマテリアルのスロットは統合されずに残るためドローコール削減効果はその分下がりますが、メッシュ自体は1つに統合されるためメッシュ数やスキニングコストの削減効果は維持されます。

## メッシュをON/OFFする場合

鞄やアクセサリーなど、トグル（MA Object Toggleなど）でON/OFFするメッシュは、**基本的に統合対象へ含めず**、常時表示のメッシュだけを統合してください。統合したメッシュは1つのレンダラーになるため、常時表示のメッシュと混ぜて統合するとトグルが正しく動作しなくなります（該当する場合はビルド時にAAOが警告を表示します）。`Create Merge Skinned Mesh (Exclude Object Toggle)` を使うと、この除外を自動で行えます。

トグルの仕組みを理解している場合は、一緒にON/OFFされるメッシュだけを統合し、**統合オブジェクト自体をON/OFFするようにメニューを組む**と、トグルを保ったまま統合できます:

- トグルが共通の親オブジェクトをON/OFFしている場合は、統合オブジェクトがその親の下に作成されるため、そのまま動作します
- MA Object Toggleでメッシュを個別にON/OFFしている場合は、作成された統合オブジェクトをトグルの対象に追加してください（メッシュのエントリと同じON/OFF設定にします）。ビルド時に統合元の表示切り替えに関する警告が表示されますが、統合メッシュ自体がトグルされるため正しく動作します

MA Object Toggleを使っている場合は、`Create Merge Skinned Mesh (From Object Toggle)` でこの構成を自動化できます。ON/OFF設定ごとに統合し、トグルが統合オブジェクトに届かない場合は自動でトグルの対象に追加します。別のObject Toggleの対象になっているメッシュは、表示単位が異なるため自動で統合から除外されます。

なお、Merge Skinned Meshの「有効無効状態に関するアニメーションをコピーする」オプションは、MA Object Toggleでメッシュを個別に指定している場合はメッシュごとに別々のアニメーション扱いになるためエラーになり、使用できません。

## 注意事項

- 作成後にMaterial Setter/Swapの構成を変更した場合、除外リストは自動では追従しません。メニューを再実行して作り直してください
- アニメーションクリップで直接マテリアルを差し替える自作ギミックは解析対象外です。必要に応じてInspectorで対象マテリアルの「統合する」チェックを手動で外してください

## アクセス方法

ヒエラルキー右クリック → `Kanameliser Editor Plus > Create Merge Skinned Mesh (Color Menu Safe) / (From Object Toggle) / (Exclude Object Toggle)`

`(From Object Toggle)` はMA Object Toggleが付いたオブジェクトを右クリックした場合のみ表示されます。
