# Material Copier

Copy & paste materials from multiple selected GameObjects to GameObjects with matching names. Supports instant FBX setup and outfit color variation. Works with both MeshRenderer and SkinnedMeshRenderer.

## Usage

1. Select source GameObjects (multiple selection supported) → Right-click → `Copy Materials`
2. Select target GameObjects → Right-click → `Paste Materials`

Materials are applied to objects with matching names (e.g., `Head` → `Head`, `Body` → `body`).

## Matching Specifications

Source and target objects are automatically matched in the following priority order. Only objects with a Renderer are considered.

| Priority | Method | Example |
|---|---|---|
| 1 | Exact relative path match (excluding root name) | `Outfit/Jacket` → `Outfit/Jacket` |
| 2 | Name match at the same hierarchy depth | `Outfit_A/Outer/Accessories/Earing` (depth 3) → `Outfit_B/Inner/Accessories/Earing` (depth 3) |
| 3 | Name match (any depth, closest depth preferred) | `Outfit/Outer/Jacket` (depth 3) → `Jacket` (depth 1) |
| 4 | Case-insensitive name match | `earing` → `Earing` |
| 5 | Similar name match (suffix normalization, common base name) | `Ribbon_blue` → `Ribbon_red`, `Hair.001` → `Hair.002` |

When multiple candidates remain at the same priority, hierarchy path similarity, ancestor context matching, depth proximity, and Levenshtein distance are used for selection.

This matching specification is also shared by MA Material Helper.

## Access

Right-click in Hierarchy → `Kanameliser Editor Plus > Copy Materials / Paste Materials`
