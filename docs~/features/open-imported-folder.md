# Open Imported Folder

unitypackage のインポートが終わると、インポートされたフォルダーを Project ウィンドウで自動的に開きます。衣装やツールを入れたあとに Assets 配下を探し回る必要がなくなります。

## 開くフォルダーの決め方

インポートされたファイルの共通の親フォルダーを開きます。ショップ共通の素材フォルダー（`Assets/Shop/Common` など）のように、インポート前からあるフォルダーへ入ったファイルは無視するので、衣装本体のフォルダーが開きます。

| インポート内容 | 開くフォルダー |
| --- | --- |
| 衣装のみ | `Assets/Shop/Outfit` |
| 共通フォルダー付きの衣装（ショップではじめての購入） | `Assets/Shop` |
| 同じショップの 2 着目（共通フォルダーは導入済み） | `Assets/Shop/OutfitB` |
| 複数ショップの商品が入ったセット | `Assets` を開き、`ShopA` と `ShopB` を選択 |
| 導入済み衣装のアップデート | `Assets/Shop/Outfit` |

2 カラム表示の Project ウィンドウでは、フォルダーをダブルクリックしたのと同じ状態（フォルダーの中身を一覧表示）になります。1 カラム表示ではツリー上でそのフォルダーを選択してハイライトします。

::: tip
VCC / ALCOM から追加した VPM パッケージ（`Packages` 配下）は対象外です。
:::

## アクセス方法

標準で有効です。オンオフの切り替え: `Tools > Kanameliser Editor Plus > [Settings] > Open Imported Folder`
