# Kanameliser Editor Plus

A small set of useful Unity & VRChat editor extensions.

## 🚩 Installation

### Via VRChat Creator Companion (Recommended)

1. Click [this link](vcc://vpm/addRepo?url=https%3A%2F%2Fkxn4t.github.io%2Fvpm-repos%2Findex.json) to add the "Kanameliser VPM Packages" repository to your VCC or ALCOM
2. Add "Kanameliser Editor Plus" to your project from the package list in Manage Project

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

### 😀 Missing BlendShape Inserter

- Automatically detects and fixes missing BlendShape keys across animation files
- Prevents facial expression glitches and broken animations
- Supports batch processing of multiple animation files
- Access via `Tools > Kanameliser Editor Plus > Missing BlendShape Inserter`
- This functionality is also integrated with [Zatools](https://zatools.kb10uy.dev/) for expanded workflow options

## 🔧 Usage Tips

### Missing BlendShape Inserter

A tool that standardizes BlendShape targets across multiple AnimationClips. It's primarily used when:

- You've modified your avatar's facial BlendShapes but kept the original animations
- Your facial expressions break or glitch when switching between animations
- You need to ensure consistent BlendShape manipulation across all animation files

The tool works by updating Animation files to operate on the same BlendShapes across all animations, preventing facial distortions and ensuring smooth transitions between different expression states.

## 📋 Requirements

- Unity 2022.3.22f1 or higher

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## 📄 License

MIT License - see the LICENSE file for details.

## 👋 Contact

If you have any questions or feedback, please open an issue on GitHub or contact me on X
