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

### 🔄 Material Swap Helper

- Automatically create color change menus using Modular Avatar's Material Swap components
- Generate menu items from color variation prefabs with just a few clicks
- Support for multiple color variation prefabs to create menus simultaneously
- Two creation modes: unified and per-object Material Swap components
- Requires [Modular Avatar](https://modular-avatar.nadena.dev/) 1.13.0 or higher to be installed
- Access via right-click context menu in Hierarchy: `Kanameliser Editor Plus > Copy/Create Material Swap`

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

### Material Swap Helper

Perfect for creating avatar color change menus from existing color variations:

1. **Copy**: Select color variation prefabs → Right-click → `Copy Material Setup`  
   Tips: Works with multiple color variation prefabs selected simultaneously
2. **Create**: Select target outfit → Right-click → `Create Material Swap` or `Create Material Swap (Per Object)`
3. **Menu Generation**: Automatically creates "Color Menu" with numbered color variations (Color1, Color2, etc.)

**Creation Modes:**
- **Standard Mode**: Creates unified Material Swap components for better performance
- **Per-Object Mode**: Creates individual Material Swap components for each object (useful for complex setups)

**Matching Specifications:**

The matching between source and target objects follows this priority order:

1. **Priority 1**: Exact relative path match + MeshRenderer/SkinnedMeshRenderer present
   - Perfect match including hierarchy structure (e.g., `Outfit/Jacket` matches `Outfit/Jacket`)
2. **Priority 2**: Same directory + cleaned name match + Renderer present
   - Matches objects in same hierarchy level with similar names (e.g., `Hat.001` matches `Hat`)
3. **Priority 3**: Exact name match + Renderer present
   - Direct name matching across different hierarchy levels
4. **Priority 4**: Cleaned name match + Renderer present
   - Falls back to name matching with suffix removal (`.001`, `_1`, etc.)

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
