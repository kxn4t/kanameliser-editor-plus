# Merge Skinned Mesh (Color Menu Safe)

Creates a Merge Skinned Mesh of AAO: Avatar Optimizer without breaking color change menus driven by Material Setter / Material Swap. The color change setup in the avatar is analyzed, and only the materials whose changes would leak into other meshes after merging are excluded from material slot merging.

**Requirement:** [AAO: Avatar Optimizer](https://vpm.anatawa12.com/avatar-optimizer/) 1.8.0 or higher, [Modular Avatar](https://modular-avatar.nadena.dev/) 1.13.0 or higher

## Background

AAO's Trace and Optimize skips meshes with material replacement animations when merging meshes automatically. Material Setter / Material Swap generate such animations at build time, so meshes targeted by a color change menu are never merged automatically — a manual Merge Skinned Mesh setup is required.

However, when merging manually, slots sharing the same material are combined into a single slot, so a color change that targets only one of the meshes can end up applying to the merged partners as well. Preventing this requires unchecking "Merge" for the affected materials, and finding out which materials are affected means reviewing every Material Setter / Material Swap in the avatar. This feature automates that analysis and setup.

## Usage

1. Select the meshes to merge (SkinnedMeshRenderer / MeshRenderer, multiple selection)
2. Right-click → `Create Merge Skinned Mesh (Color Menu Safe)`

A Merge Skinned Mesh object covering the selected meshes is created under their common parent. Material Setter / Material Swap components in the avatar (including those on inactive menu objects) are analyzed, and materials whose color changes would break are pre-registered with their "Merge" checkbox turned off. Excluded materials are listed in the console log.

## How Exclusion Is Decided

Slots sharing the same material are combined into one slot when merged. Whether that is safe is decided by the following rule:

- Every Material Setter / Material Swap changes **all** slots of the material **in the same way** (or changes none of them) → merging is allowed
- Any component changes **only some** of the slots, or changes them **to different materials** → the material is excluded

Example: Mesh A and Mesh B both use the same material Gray.

| Color change setup | Result |
|---|---|
| One setter changes Gray on both A and B to White | Merged (no exclusion) |
| Gray on A → White, Gray on B → Blue | Gray excluded |
| Only Gray on A is changed (B untouched) | Gray excluded |
| Material Swap with empty Root (whole avatar) changing Gray → White | Merged (no exclusion) |
| Material Swap whose Root covers only Mesh A changing Gray → White | Gray excluded |

Slots of excluded materials remain separate, so the draw call reduction is smaller for them, but the meshes themselves are still merged into one, so the reduction in mesh count and skinning cost is preserved.

## Toggled Meshes

Merged meshes become a single renderer, so meshes that are toggled on/off — bags, accessories, and so on — must be **merged in units that are toggled together**. This also applies to toggles built with MA Object Toggle. Merging them together with always-visible meshes or with meshes on a different toggle stops the toggle from working correctly (AAO shows a warning at build time when this applies).

After merging per toggle unit, the behavior depends on how the toggle is built:

- **The toggle switches a common parent object**: the merged object is created under that parent, so it works as is
- **The toggle switches each mesh individually**: the merged object is not covered by the toggle, so enable "Copy Enablement Animation" on the created Merge Skinned Mesh (available when all merged meshes are toggled by the same animation)

## Notes

- The exclusion list does not update automatically when you change your Material Setter / Material Swap setup afterwards. Re-run the command to recreate it
- Material replacements done directly with custom animation clips are not analyzed; uncheck "Merge" for those materials manually in the Inspector when needed

## Access

Right-click in Hierarchy → `Kanameliser Editor Plus > Create Merge Skinned Mesh (Color Menu Safe)`
