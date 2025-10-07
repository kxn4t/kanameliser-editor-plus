# Kanameliser Editor Plus

A small set of useful Unity & VRChat editor extensions.

## 🚩 Installation

### Via VRChat Creator Companion (Recommended)

1. Visit [https://kxn4t.github.io/vpm-repos/](https://kxn4t.github.io/vpm-repos/)
2. Click the "Add to VCC" button to add the "Kanameliser VPM Packages" repository to your VCC or ALCOM
3. Add "Kanameliser Editor Plus" to your project from the package list in Manage Project

### Manual Installation

If you prefer manual installation:

1. Download the latest release from [GitHub Releases](https://github.com/kxn4t/kanameliser-editor-plus/releases)
2. Import the package into your Unity project

## 📌 Features

### 🔍 Mesh Info Display

- Displays detailed mesh information for selected objects and their children
- Shows polygon count, material count, material slots, and mesh count
- Appears in the top-left corner of the Scene view
- Toggle visibility via `Tools > Kanameliser Editor Plus > Show Mesh Info Display`

#### 🔮 NDMF Preview Support

- **Build-time Statistics**: Shows preview mesh data when NDMF preview is active
- **Optimization Visualization**: Displays both original and optimized mesh counts side-by-side with visual diff indicators
- **Preview Detection**: Automatically detects NDMF proxy meshes and shows clear green dot indicator during preview

### 🔄 Toggle Objects Active

- Quickly toggle between GameObject active state and EditorOnly tag
- Shortcuts: `Ctrl+G`

### 🧩 Component Manager

- List all components on selected objects and their children
- Search for specific objects or component types
- Instantly see which components are attached to which objects
- Select multiple objects simultaneously for batch editing
- Easily remove unwanted components
- Access via `Tools > Kanameliser Editor Plus > Component Manager`

### 🎨 Material Copier

- Copy & paste materials from multiple selected GameObjects to GameObjects with matching names
- Perfect for instant FBX setup and outfit color variations
- Supports both MeshRenderer and SkinnedMeshRenderer components
- Access via right-click context menu in Hierarchy: `Kanameliser Editor Plus > Copy/Paste Materials`

### 🔄 MA Material Helper

Automatic color change menu creation tools using Modular Avatar's material control components.

#### Material Setter (Recommended for most use cases)
- Automatically create color change menus using Modular Avatar's Material Setter components
- Generate menus that directly set materials from color variation prefabs
- Operates differently from Material Swap by directly replacing materials on each material slot of meshes
  - Allows changing from the same source material to different materials within the same mesh by specifying per-slot
  - More accurately reproduces the material layout of the source prefab compared to Material Swap
- Two creation modes:
  - **Standard Mode**: Only creates setters for material slots that differ from the current materials
  - **All Slots Mode**: Creates setters for all material slots regardless of whether they differ from current materials

#### Material Swap
- Automatically create color change menus using Modular Avatar's Material Swap components
- Generate menu items from color variation prefabs with just a few clicks
- Support for multiple color variation prefabs to create menus simultaneously
- Two creation modes: unified and per-object Material Swap components

**Common Specifications:**
- Requires [Modular Avatar](https://modular-avatar.nadena.dev/) 1.13.0 or higher to be installed
- Access via right-click context menu in Hierarchy: `Kanameliser Editor Plus > Copy Material Setup / Create Material Setter / Create Material Swap`

### 😀 Missing BlendShape Inserter

- Automatically detects and fixes missing BlendShape keys across animation files
- Prevents facial expression glitches and broken animations
- Supports batch processing of multiple animation files
- Access via `Tools > Kanameliser Editor Plus > Missing BlendShape Inserter`
- This functionality is also integrated with [Zatools](https://zatools.kb10uy.dev/) for expanded workflow options
- For more detailed information, please refer to [Zatools/missing-blendshape-inserter](https://zatools.kb10uy.dev/editor-extension/missing-blendshape-inserter/)

## 🔧 Usage Tips

### Material Copier

Perfect for managing materials across multiple similar objects:

1. **Copy**: Select source GameObjects (e.g., Avatar_A, Avatar_B) → Right-click → `Copy Materials`
2. **Paste**: Select target GameObjects (e.g., Avatar_C, Avatar_D) → Right-click → `Paste Materials`
3. **Matching**: Materials are applied to objects with matching names (e.g., "Head" → "Head", "Body" → "body")

Common use cases:
- Copying outfit materials between different avatar variants

### MA Material Helper

Perfect for creating avatar color change menus from existing color variations:

1. **Copy**: Select color variation prefabs → Right-click → `Copy Material Setup`
   Tips: Works with multiple color variation prefabs selected simultaneously
2. **Create**: Select target outfit → Right-click → Choose one of the following:
   - `Create Material Setter` - Standard Material Setter mode (recommended for most use cases)
   - `[Optional] Create Material Setter (All Slots)` - Material Setter mode (all slots)
   - `Create Material Swap` - Standard Material Swap mode
   - `[Optional] Create Material Swap (Per Object)` - Per-object Material Swap mode
3. **Menu Generation**: Automatically creates "Color Menu" with numbered color variations (Color1, Color2, etc.)

**Material Setter Modes:**
- **Standard Mode** (recommended): Only sets material slots that differ from current materials
- **All Slots Mode**: Sets all material slots regardless of whether they differ from current materials
  - May affect performance, recommended only when you need to customize manually
- Directly replaces materials by creating Material Setter components for each object

**Material Swap Modes:**
- **Standard Mode**: Creates unified Material Swap components for better performance
- **Per-Object Mode**: Creates individual Material Swap components for each object (useful for complex setups)

**Choosing Between Material Swap and Material Setter:**

When deciding which mode to use, consider the following:

**Use Material Swap when:**
- All material slots within the same mesh use the same material, both before and after the change
- You prioritize performance (fewer components)
- Your material setup is simple

**Use Material Setter when:**
- Different slots in the same mesh use the same material but need to change to different materials
- You want to more accurately reproduce the material layout from the source prefab
- Material Swap produces unintended behavior

**Material Swap Limitation**
```
Mesh A:
  Slot 0: Material X → want to change to Material Y
  Slot 1: Material X → want to change to Material Z
```
In this case, Material Swap can only replace "Material X" with one material, so both slots will end up with the same material.  
Material Setter can specify per-slot, allowing each to change to different materials.

**Matching Specifications:**

The matching between source and target objects follows this priority order:

1. **Priority 1**: Exact relative path match (without root name) + Renderer present
   - Example: Source `Outfit/Jacket` → Target `Outfit/Jacket`
   - Example: Source `Accessories/Earing/Left` → Target `Accessories/Earing/Left`

2. **Priority 2**: Same hierarchy depth + exact name match + Renderer present
   - Example: Source `Outfit_A/Outer/Accessories/Earing` (depth=3) → Target `Outfit_B/Inner/Accessories/Earing` (depth=3)
   - Both objects are named `Earing` and at the same depth, even though parent names differ
   - Applies hierarchy filtering if multiple candidates exist at the same depth

3. **Priority 3**: Exact name match + Renderer present
   - Example: Source `Outfit/Outer/Jacket` (depth=3) → Target `Jacket` (depth=1)
   - Selects by closest depth if multiple `Jacket` objects exist
   - If source is depth 3 and targets are depth 1, 2, 4: prefers depth 2 or 4 (difference=1)
   - Applies hierarchy filtering if multiple candidates exist

4. **Priority 4**: Case-insensitive name match + Renderer present
   - Example: Source `earing` → Target `Earing`
   - Example: Source `JACKET` → Target `Jacket`
   - Selects by closest depth and applies hierarchy filtering

**Advanced Matching:**

When multiple candidates remain after priority matching:
- **Parent hierarchy filtering**: Matches parent names from bottom to top
  - Example: Looking for `Outfit/Outer/Jacket`
    - Candidates: `Outfit/Outer/Jacket`, `Costume/Outer/Jacket`, `Outfit/Inner/Jacket`
    - Filters by immediate parent: prefers candidates under `Outer` → 2 candidates remain
    - Filters by grandparent: prefers candidates under `Outfit` → `Outfit/Outer/Jacket` selected
- **Similarity scoring**: Uses Levenshtein distance for final selection
  - Example: Pasting to `Outfit_C` with candidates from `Outfit_A` and `Outfit_C`
    - Compares root names: `Outfit_C` vs `Outfit_A` and `Outfit_C` vs `Outfit_C`
    - Prefers `Outfit_C` source (distance=0) over `Outfit_A` source (distance=2)

Common use cases:
- Creating color change menus for avatar outfits
- Batch creation of color change menus from existing color variation prefabs

### Missing BlendShape Inserter

A tool that standardizes BlendShape targets across multiple AnimationClips. It's primarily used when:

- You've modified your avatar's facial BlendShapes but kept the original animations
- Your facial expressions break or glitch when switching between animations
- You need to ensure consistent BlendShape manipulation across all animation files

The tool works by updating Animation files to operate on the same BlendShapes across all animations, preventing facial distortions and ensuring smooth transitions between different expression states.

## 📋 Requirements

- Unity 2022.3.22f1 or higher
- Optional: NDMF (Non-Destructive Modular Framework) for enhanced build preview support
- Optional: Modular Avatar 1.13.0 or higher for Material Swap Helper feature

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## 📄 License

MIT License - see the LICENSE file for details.

## 👋 Contact

If you have any questions or feedback, please open an issue on GitHub or contact me on X
