# Kanameliser Editor Plus

A set of useful editor extensions for Unity and VRChat.

## Installation

### Via VRChat Creator Companion (Recommended)

1. Visit [https://kxn4t.github.io/vpm-repos/](https://kxn4t.github.io/vpm-repos/)
2. Click the "Add to VCC" button to add the "Kanameliser VPM Packages" repository to your VCC or ALCOM
3. Add "Kanameliser Editor Plus" to your project from the package list in Manage Project

### Manual Installation

1. Download the latest release from [GitHub Releases](https://github.com/kxn4t/kanameliser-editor-plus/releases)
2. Import the package into your Unity project

## Features

### Mesh Info Display

Displays mesh information for selected objects and their children in the top-left corner of the Scene view.
You can check polygon count, material count, material slot count, and mesh count.

#### NDMF Preview Support

When NDMF preview is active, you can check optimization results from AAO/TTT/Meshia and adjust accordingly.

- Displays original and optimized mesh counts side-by-side with diff indicators
- Automatically detects NDMF proxy meshes and shows a green dot to indicate preview state

Toggle: `Tools > Kanameliser Editor Plus > Show Mesh Info Display`

### Toggle Objects Active

Quickly toggle between GameObject active state and EditorOnly tag.

Shortcut: `Ctrl+G`

### Component Manager

Lists all components on selected objects and their children.

- Search for specific objects or component types
- Instantly see which components are attached to which objects
- Select multiple objects simultaneously for batch editing
- Easily remove unwanted components in bulk

Access: `Tools > Kanameliser Editor Plus > Component Manager`

### Material Copier

Copy & paste materials from multiple selected GameObjects to GameObjects with matching names.

- Instant FBX setup and outfit color variation support
- Supports both MeshRenderer and SkinnedMeshRenderer

#### Usage

1. Select source GameObjects (multiple selection supported) → Right-click → `Copy Materials`
2. Select target GameObjects → Right-click → `Paste Materials`

Materials are applied to objects with matching names (e.g., `Head` → `Head`, `Body` → `body`).

#### Matching Specifications

Source and target objects are automatically matched in the following priority order. Only objects with a Renderer are considered.

1. **Exact relative path match (excluding root name)** — `Outfit/Jacket` → `Outfit/Jacket`
2. **Name match at the same hierarchy depth** — Matches objects with the same name and depth, even if parent names differ
   - Example: `Outfit_A/Outer/Accessories/Earing` (depth 3) → `Outfit_B/Inner/Accessories/Earing` (depth 3)
3. **Name match (any depth)** — When multiple candidates exist, the closest depth is preferred
   - Example: `Outfit/Outer/Jacket` (depth 3) → `Jacket` (depth 1)
4. **Case-insensitive name match** — `earing` → `Earing`

When multiple candidates remain, parent names are matched from bottom to top to narrow down the results. If still ambiguous, the Levenshtein distance of root names is used for final selection.

This matching specification is also shared by MA Material Helper.

Access: Right-click in Hierarchy `Kanameliser Editor Plus > Copy/Paste Materials`

### MA Material Helper

Automatically generates color change menus using Modular Avatar's material control components. Create color change menus from color variation prefabs in just a few clicks, with support for simultaneous generation from multiple prefabs.

Requirement: [Modular Avatar](https://modular-avatar.nadena.dev/) 1.13.0 or higher

#### Usage

1. Select color variation prefabs → Right-click → `Copy Material Setup` (multiple selection supported)
2. Select target outfit → Right-click → Choose one of the following:
   - `Create Material Setter` — Directly set materials per slot (recommended)
   - `[Optional] Create Material Setter (All Slots)` — Create setters for all slots (may affect performance, recommended only when you need to customize manually)
   - `Create Material Swap` — Set materials by replacement rules
   - `[Optional] Create Material Swap (Per Object)` — Create individual swaps per object

A "Color Menu" with numbered color variations (Color1, Color2, etc.) is automatically created.

#### Difference Between Material Setter and Material Swap

Material Setter is recommended for most use cases.

- **Material Setter**: Sets per slot, allowing different materials to be assigned from the same source material within the same mesh
- **Material Swap**: Replaces by material name, so the same source material always maps to the same target. Best for simple configurations

#### Which Should You Choose?

**Use Material Setter (most cases):**

- Different slots in the same mesh use the same material but need to change to different materials
- You want to more accurately reproduce the material layout from the source prefab
- Material Swap produces unintended behavior

**Use Material Swap:**

- All slots using the same material within a mesh should change to the same target material
- Simple material configurations

Material Swap cannot handle cases like:

```
Mesh A:
  Slot 0: Material X → want to change to Material Y
  Slot 1: Material X → want to change to Material Z
```

Material Swap can only replace "Material X" with one material, so both slots end up with the same result. Material Setter can specify per slot, allowing each to change to a different material.

Access: Right-click in Hierarchy `Kanameliser Editor Plus > Copy Material Setup / Create Material Setter / Create Material Swap`

### AO Bounds Setter

Batch configure Anchor Override, Root Bone, and Bounds for multiple meshes. Useful for outfit creation and avatar setup.

1. Drag an object from the Hierarchy to the Root Object field
2. Configure settings:
   - **Anchor Override**: Set the object to use as the anchor point
   - **Root Bone** (SkinnedMeshRenderer only): Set the root bone for skinned meshes
   - **Bounds** (SkinnedMeshRenderer only): Configure bounds
   - Use the dropdown search to quickly find bones/anchors under the root object
3. Select target meshes with checkboxes and click "Apply to Selected Meshes" to batch apply

Click on object name, Anchor Override, or Root Bone labels to quickly select that object in the Hierarchy.

Access: `Tools > Kanameliser Editor Plus > AO Bounds Setter`

### Missing BlendShape Inserter

Automatically detects and fills in missing BlendShape keys across animation files.
Updates all animations to operate on the same BlendShapes, ensuring smooth transitions between different expression states. Supports batch processing of multiple files.

Common use cases:

- You've modified your avatar's facial BlendShapes but haven't updated the expression animations
- Facial expressions break or glitch when switching via gestures
- You need to ensure consistent BlendShape manipulation across all animation files

This feature has been localized and integrated into [Zatools](https://zatools.kb10uy.dev/) by kb10uy. For details, see [Zatools/missing-blendshape-inserter](https://zatools.kb10uy.dev/editor-extension/missing-blendshape-inserter/).

Access: `Tools > Kanameliser Editor Plus > Missing BlendShape Inserter`

## Requirements

- Unity 2022.3.22f1 or higher
- Optional: NDMF (enhanced build preview support)
- Optional: Modular Avatar 1.13.0 or higher (required for MA Material Helper)

## Contributing

Feel free to submit an Issue or Pull Request.

## License

MIT License — see the LICENSE file for details.

## Contact

If you have any questions or feedback, please open an issue on GitHub or contact me on X.
