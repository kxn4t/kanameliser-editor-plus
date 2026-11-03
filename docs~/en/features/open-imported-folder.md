# Open Imported Folder

Opens the folder a .unitypackage import added in the Project window as soon as the import finishes, so a new outfit or tool does not have to be hunted down under Assets.

## Which folder opens

The common parent folder of the imported files. Files that landed in folders which existed before the import, such as a shop's shared folder (`Assets/Shop/Common`, say), are ignored, so the outfit's own folder is what opens.

| Imported | Folder that opens |
| --- | --- |
| An outfit | `Assets/Shop/Outfit` |
| An outfit with a shared folder, first purchase from the shop | `Assets/Shop` |
| A second outfit of the same shop, shared folder already installed | `Assets/Shop/OutfitB` |
| A set with items from several shops | `Assets`, with `ShopA` and `ShopB` selected |
| An update of an installed outfit | `Assets/Shop/Outfit` |

In the two-column Project window this is the same state as a double-click on the folder: its contents are listed. In the one-column layout the folder is selected and highlighted in the tree.

::: tip
VPM packages added through VCC / ALCOM (under `Packages`) are not affected.
:::

## Access

Enabled by default. Toggle: `Tools > Kanameliser Editor Plus > [Settings] > Open Imported Folder`
