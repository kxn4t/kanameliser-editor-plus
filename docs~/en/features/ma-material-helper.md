# MA Material Helper

Automatically generates color change menus using Modular Avatar's material control components. Create color change menus from color variation prefabs in just a few clicks, with support for simultaneous generation from multiple prefabs.

**Requirement:** [Modular Avatar](https://modular-avatar.nadena.dev/) 1.13.0 or higher

## Usage

1. Select color variation prefabs → Right-click → `Copy Material Setup` (multiple selection supported)
2. Select target outfit → Right-click → Choose one of the following:

| Command | Description |
|---|---|
| `Create Material Setter` | Set materials per slot (**recommended**) |
| `[Optional] Create Material Setter (All Slots)` | Create setters for all slots (for manual customization) |
| `Create Material Swap` | Set materials by replacement rules |
| `[Optional] Create Material Swap (Per Object)` | Create individual swaps per object |

A "Color Menu" with numbered color variations (Color1, Color2, etc.) is automatically created.

## Material Setter vs Material Swap

**Material Setter is recommended for most use cases.**

| | Material Setter | Material Swap |
|---|---|---|
| Assignment unit | Per slot | Per material name |
| Same material → different targets in one mesh | Supported | Not supported |
| Use case | Most cases | Simple configurations |

### Cases Material Swap Cannot Handle

```
Mesh A:
  Slot 0: Material X → want to change to Material Y
  Slot 1: Material X → want to change to Material Z
```

Material Swap can only replace "Material X" with one material, so both slots end up with the same result. Material Setter can specify per slot, allowing each to change to a different material.

## Material Slot Remapping

When an outfit is converted to fit another avatar (e.g. with auto-fitting tools), a renderer's material slot order can change, so an index-based color setup ends up on the wrong slots. Add a remapping component to the converted outfit and point it at the original outfit to fix this.

### Usage

1. Right-click the converted outfit → `Add Material Slot Remapping`
2. Set the original outfit as `Reference Prefab` → click `Generate Mapping`
3. Run `Copy Material Setup` / `Create Material Setter` / `Create Material Swap` as usual — generation follows the mapping so colors land on the correct slots

The mapping is generated from material references, so generate it before changing the converted outfit's materials (i.e. right after conversion).  
After generation, only the slot correspondence (indices) is stored, so changing the outfit's materials afterwards does not break the mapping.

## Access

Right-click in Hierarchy → `Kanameliser Editor Plus > Add Material Slot Remapping / Copy Material Setup / Create Material Setter / Create Material Swap`

## Verbose Matching Logs

When automatic object matching does not produce the expected result, enable verbose logging to see full match decision details in the Unity console.

Toggle: `Tools > Kanameliser Editor Plus > [Settings] > Verbose Matching Logs`
