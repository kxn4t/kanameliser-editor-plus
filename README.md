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

Toggle: `Tools > Kanameliser Editor Plus > [Settings] > Show Mesh Info Display`

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
5. **Similar name match** — Names matching after structural normalization (e.g., `Body_01` ≈ `Body_02`) or sharing a common base name (e.g., `Ribbon_blue` ≈ `Ribbon_red`), ranked by a composite score
   - Example: `Ribbon_blue` → `Ribbon_red`, `Hair.001` → `Hair.002`

When multiple candidates remain at the same priority, hierarchy path similarity (comparing parent folders from the leaf upward), ancestor context matching, depth proximity, and Levenshtein distance are used for selection.

This matching specification is also shared by MA Material Helper.

#### Verbose Matching Logs

When automatic matching does not produce the expected result, enable verbose logging to see full match decision details in the Unity console. This toggle also applies to MA Material Helper matching.

Toggle: `Tools > Kanameliser Editor Plus > [Settings] > Verbose Matching Logs`

Access: Right-click in Hierarchy `Kanameliser Editor Plus > Copy/Paste Materials`

### FBX Settings Copier

Copy & paste FBX import settings between FBX assets in the Project window. Useful when importing multiple FBX files that should share the same import setup (e.g. outfits supporting multiple avatars).

- Copies Model tab settings (including Legacy Blend Shape Normals), basic Rig settings, and Materials tab settings including Remapped Materials
- Paste to multiple FBX files at once — files already matching the copied settings are skipped without reimporting

#### Usage

1. Select the source FBX in the Project window → Right-click → `Copy FBX Settings`
2. Select target FBX files (multiple selection supported) → Right-click → `Paste FBX Settings`

#### What Is Copied

- **Model tab**: All settings (Scale Factor, Read/Write, Blend Shapes, Normals/Tangents, Legacy Blend Shape Normals, etc.)
- **Rig tab**: Animation Type, Avatar Definition (including the source avatar reference for Copy From Other Avatar), Skin Weights, Optimize Bones / Optimize Game Objects
- **Materials tab**: Material Creation Mode, Location, Naming/Search settings, and Remapped Materials

Remapped Materials are applied only where the target has a material with the same name; unmatched entries are left untouched.
Animation clip definitions and Humanoid bone mappings (Avatar Configuration) are not copied, as they are specific to each file.

Note: Import settings changes cannot be undone with Ctrl+Z.

Access: Right-click an FBX in the Project window `Kanameliser Editor Plus > Copy FBX Settings / Paste FBX Settings`

### MA Material Helper

Automatically generates color change menus using Modular Avatar's material control components. Create color change menus from color variation prefabs in just a few clicks, with support for simultaneous generation from multiple prefabs.

Requirement: [Modular Avatar](https://modular-avatar.nadena.dev/) 1.13.0 or higher

#### Usage

1. Select color variation prefabs → Right-click → `Copy Color Variants` (multiple selection supported)
2. Select target outfit → Right-click → `Create Color Menu` (this item appears after copying)

If you are unsure of the steps, open `[How to Create Color Menu]` from the right-click menu at any time to review the workflow and see a list of the currently copied objects.

A "Color Menu" with numbered color variations (Color1, Color2, etc.) is automatically created. `Create Color Menu` generates Modular Avatar Material Setter components per slot, which works for most cases.

For special cases, the `Advanced` submenu offers alternative generation modes:

- `Create Material Setter (All Slots)` — Create setters for all slots (may affect performance, recommended only when you need to customize manually)
- `Create Material Swap` — Set materials by replacement rules
- `Create Material Swap (Per Object)` — Create individual swaps per object

#### Difference Between Material Setter and Material Swap

Material Setter (used by `Create Color Menu`) is recommended for most use cases.

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

#### Material Slot Remapping

When an outfit is converted to fit another avatar (e.g. with auto-fitting tools), a renderer's material slot order can change, so an index-based color setup ends up on the wrong slots. Add a remapping component to the converted outfit and point it at the original outfit to fix this:

1. Right-click the converted outfit → `Add Material Slot Remapping`
2. Set the original outfit as `Reference Prefab` → click `Generate Mapping`
3. Run `Copy Color Variants` / `Create Color Menu` as usual — generation follows the mapping so colors land on the correct slots

The mapping is generated from material references, so generate it before changing the converted outfit's materials (i.e. right after conversion).  
After generation, only the slot correspondence (indices) is stored, so changing the outfit's materials afterwards does not break the mapping.
When the same material (or an empty slot) occurs more than once, the slot order cannot be determined uniquely. A confirmation dialog and warnings identify these cases; review the estimated mapping and adjust it manually in the Inspector when needed.

#### Performance Note

Material Setter / Material Swap generate material replacement animations at build time, so meshes targeted by a color change menu are excluded from automatic mesh merging by AAO: Avatar Optimizer's Trace and Optimize and cost extra draw calls. When you can spare the effort, merge the target meshes with [Merge Skinned Mesh (Color Menu Safe)](#merge-skinned-mesh-color-menu-safe) to reduce the load without breaking the color change menu.

Access: Right-click in Hierarchy `Kanameliser Editor Plus > Copy Color Variants / Create Color Menu / Advanced / [How to Create Color Menu] / Add Material Slot Remapping`

### Merge Skinned Mesh (Color Menu Safe)

Creates a Merge Skinned Mesh of AAO: Avatar Optimizer without breaking color change menus driven by Material Setter / Material Swap. AAO's Trace and Optimize never merges meshes targeted by material replacement animations automatically, and a naive manual merge can leak a color change into other meshes sharing the same material — this command analyzes the color change setup and excludes only the unsafe materials from material slot merging.

Requirement: [AAO: Avatar Optimizer](https://vpm.anatawa12.com/avatar-optimizer/) 1.8.0 or higher, [Modular Avatar](https://modular-avatar.nadena.dev/) 1.13.0 or higher

#### Usage

1. Select the meshes to merge (SkinnedMeshRenderer / MeshRenderer, multiple selection)
2. Right-click → `Create Merge Skinned Mesh (Color Menu Safe)`

A Merge Skinned Mesh object covering the selected meshes is created under their common parent, with unsafe materials pre-registered with their "Merge" checkbox turned off. Excluded materials are listed in the console log.

#### How Exclusion Is Decided

A material is excluded when any Material Setter / Material Swap changes only some of the slots sharing it, or changes them to different materials. When every component changes all of its slots in the same way (or none), the material stays merged for maximum draw call reduction.

| Color change setup (A and B share material Gray) | Result |
|---|---|
| One setter changes Gray on both A and B to White | Merged (no exclusion) |
| Gray on A → White, Gray on B → Blue | Gray excluded |
| Only Gray on A is changed (B untouched) | Gray excluded |
| Material Swap with empty Root (whole avatar) | Merged (no exclusion) |
| Material Swap whose Root covers only Mesh A | Gray excluded |

#### Notes

- Re-run the command after changing your Material Setter / Material Swap setup — the exclusion list does not update automatically
- Material replacements done directly with custom animation clips are not analyzed; uncheck "Merge" for those materials manually when needed
- Leave meshes that are toggled on/off (bags, accessories, etc. — e.g. with MA Object Toggle) out of the merge and merge only always-visible meshes; mixing them stops the toggle from working (AAO warns at build time). If you understand your toggle setup, you can merge meshes that are toggled together and build the menu to toggle the merged object itself

Access: Right-click in Hierarchy `Kanameliser Editor Plus > Create Merge Skinned Mesh (Color Menu Safe)`

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

## Requirements

- Unity 2022.3.22f1 or higher
- Optional: NDMF 1.11.0 or higher (Japanese UI with language switching, enhanced build preview support)
- Optional: Modular Avatar 1.13.0 or higher (required for MA Material Helper and Merge Skinned Mesh (Color Menu Safe))
- Optional: AAO: Avatar Optimizer 1.8.0 or higher (required for Merge Skinned Mesh (Color Menu Safe))

## Contributing

Feel free to submit an Issue or Pull Request.

## License

MIT License — see the LICENSE file for details.

## Contact

If you have any questions or feedback, please open an issue on GitHub or contact me on X.
