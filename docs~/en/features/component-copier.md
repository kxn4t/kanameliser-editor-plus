# Component Copier

Copies the components of an outfit or avatar (PhysBone, Contact, Constraint, Modular Avatar, ...) to another outfit or avatar. It does more than copy values: **the object references inside the components (bones, colliders, constraint sources, ...) are redirected to the corresponding objects of the target automatically.**

Typical uses:

- Carry PhysBone and Modular Avatar settings over to an updated version of the same outfit, or to the version made for another avatar
- Bring just a few components from one outfit to another

## Usage

1. Open `Tools > Kanameliser Editor Plus > Component Copier` from the menu
2. Set **Source** to the object that holds the settings (a Hierarchy object, a Prefab instance, or a Prefab asset in the Project view all work; the components below it are listed)
3. Check the components to copy
4. Set **Target** to the scene object that receives the settings (the objects of the source and the target are matched automatically; the `⇅` button swaps the source and the target)
5. Look through the **Mapping** section and, where a row is marked to review (`?`) or unmapped (`✖`), pick the counterpart by hand as needed
6. Read the **pre-check** and, if everything looks right, press **Apply** (Undo reverts it)
7. Check the diff shown after applying to confirm that everything arrived as expected

::: tip Note: what can be a target
Only **scene objects** (including Prefab instances and objects in a Prefab Stage) can be a target. Applying to a Prefab asset directly would spread to its Prefab Variants and be hard to undo.
**Diff check only** changes nothing, so a Prefab asset can be used there.
:::

## Component list

Once a source is set, the components below it are listed grouped by type or by object.

- **Presets**: PhysBone, Contact, Constraint, MA (Modular Avatar), other tools (AAO, TTT, ..., whichever the source has), and All check a whole group at once
- **Search**: partial match on object paths and type names. Turn on the `.*` button for regular expressions (e.g. `Skirt|Hair`)
- **Show others**: also offers the basic types that are hidden by default (Transform, Renderer, MeshFilter, Animator, VRCAvatarDescriptor, PipelineManager)
- **Objects missing in the target**: at the end of the list, prefabs and empty objects (anchors, ...) that the target lacks. The ones a copied component needs are created automatically; the rest can be checked to add them too

Clicking a row or a heading in the list selects the object and highlights it in the Hierarchy.

::: warning Recreate MA Mesh Settings after copying
MA Mesh Settings can be copied with its references, but the Anchor Override and Root Bone differ from avatar to avatar. After copying, delete it and run `Setup Outfit` again on the target avatar so that it is set up for that avatar.
:::

## Mapping

Objects of the source and the target are matched automatically by **path, name, and a dictionary of humanoid bone names** (`Hips` and `Hip`, `UpperArm_L` and `Left arm`, ...). Suffixes added to avoid name clashes, such as `Armature.1` or `.001`, and a common prefix or suffix on one side are recognized and compensated for as well.

- **Ambiguous matches**: matches by similar names are never applied automatically; they are offered as suggestions
- **Bones and other objects**: bones (the transforms used by SkinnedMeshRenderers) and other objects are told apart and matched separately
- **Missing bones**: bones missing in the target are not created. A component on such a bone is **blocked**, so map it by hand in the Mapping section
- **Manual choice**: each row offers a dropdown of candidates and an ObjectField for any object. "None" treats the object as having no counterpart
- **Kept choices**: mappings picked by hand survive a rescan (`↻`) and scene changes

### References to the outside

When a component of the outfit refers to a bone or collider of the avatar, and the target sits below another avatar, **the reference is redirected to the corresponding object of the target avatar automatically.** Without a counterpart, the original reference is kept.

The toggle on the "References to the outside" group switches all of them at once, and each row offers "Keep as is".

::: tip Put the outfits inside their avatars
When copying the components of an outfit, place both the source outfit and the target outfit inside their avatars first.
References of components that tend to point at avatar objects, such as `MA Mesh Settings`, `MA Mesh Cutter`, or `MA Shape Changer`, are then redirected along with everything else.
:::

## Settings

| Setting | Options | Default |
| --- | --- | --- |
| **Existing components** | Overwrite / Add (duplicate) / Skip / Replace (delete, then copy) | **Overwrite** |
| **Create missing objects** | On / Off | **On** |
| **Unresolved references** | Copy with None / Skip the component | **Copy with None** |

- **Overwrite**: updates the values without recreating the component, so references held by other components stay intact
- **Unresolved references**: what happens when a reference has no counterpart in the target. **Skip the component** avoids half-configured components, such as a constraint without its source or a PhysBone without its colliders, and holds them back until the mapping is complete

## Nested prefabs

When a prefab inside the source (a prefab bundling PhysBone settings, a hat directly below `Head`, ...) does not exist in the target, **the same prefab is instantiated in the target** instead of rebuilding its objects one by one.

Every component inside it is copied, selected or not, and the values changed on the source instance carry over. Components you uncheck are removed automatically once the prefab is instantiated.

## Pre-check and diff check

### Pre-check

Before applying, the following is listed:

- How many components are added, overwritten, replaced, or skipped, and how many objects are created
- Blocked components and the reason
- Unresolved references that will be cleared to None (or, depending on the setting, skipped) and the cause
- References to the outside that are kept as they are
- A warning when replacing (deleting) a component would break a reference from a component that is not part of the copy

### Diff check

**Diff check only** compares the expected result of the copy with the current state without changing the scene. The same report is shown after applying, so the result can be checked by status:

- Statuses: identical / value differs / reference differs / unresolved reference / missing in the target / only in the target

## Notes

- **Transform values**: positions and rotations are copied as local values. Between avatars whose bones point in very different directions, some adjustment by hand may be needed after copying
- **Modular Avatar references**: the targets of Merge Armature, Object Toggle, Shape Changer, and the like are rewritten as a whole, the avatar-relative path along with the object reference
- **PipelineManager**: including PipelineManager copies the Blueprint ID too. Leave it unchecked unless that is what you want

## Access

The tool opens from any of these:

- Main menu: `Tools > Kanameliser Editor Plus > Component Copier`
- Right-click in the Hierarchy → `Kanameliser Editor Plus > Component Copier > Use as Source / Use as Target`
- Right-click a Prefab in the Project view → `Kanameliser Editor Plus > Component Copier > Use as Source`
- Inspector (right-click a component header) → `Kanameliser Editor Plus > Copy with Component Copier` (opens with just that component checked)
