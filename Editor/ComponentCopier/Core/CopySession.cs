using System;
using System.Collections.Generic;
using System.Linq;
using Kanameliser.Editor.MAMaterialHelper.Common;
using UnityEditor;
using UnityEngine;

namespace Kanameliser.EditorPlus.ComponentCopier
{
    /// <summary>
    /// A copy being set up: the roots, the components and objects the user checked, the mappings they
    /// corrected, and the plan that follows from all of it. The window renders it and passes the user's input
    /// on; what an input means is decided here, apart from the UI, so that it can be tested.
    /// Every input goes through a method that brings the plan up to date and drops whatever the input makes
    /// stale, so no caller has to remember to.
    /// </summary>
    internal sealed class CopySession
    {
        /// <summary>The roots and the mode. The window keeps them across domain reloads; the rest is rebuilt.</summary>
        [Serializable]
        internal sealed class Inputs
        {
            public GameObject SourceRoot;
            public GameObject TargetRoot;

            /// <summary>Mirror copy: one side of the source goes to the other side of the same hierarchy.</summary>
            public bool MirrorMode;

            /// <summary>The side a mirror copy copies from.</summary>
            public Side MirrorSide = Side.Left;
        }

        private readonly Inputs inputs;

        private List<ComponentEntry> entries = new();
        private readonly HashSet<ComponentKey> selectedKeys = new();
        // Components the user unchecked while they were about to arrive with a nested prefab, see Check
        private readonly HashSet<ComponentKey> leftOutKeys = new();
        // Nested prefabs and empty objects the user chose to add although no selected component needs them.
        // Kept by path so that they survive a rescan.
        private readonly HashSet<string> selectedObjectPaths = new();
        private readonly List<Transform> missingRoots = new();
        // The missing roots that the plan creates, whether asked for or needed by a selected component
        private readonly HashSet<Transform> plannedRoots = new();
        private readonly Dictionary<Transform, Transform> manualMappings = new();
        // Objects that would be created, for which the user wants to pick an existing object instead
        private readonly HashSet<Transform> pickExistingFor = new();
        private readonly Dictionary<ComponentKey, PlannedComponent> plannedByKey = new();

        // The maps that span whole avatars are kept while nothing they are built from changes: ticking a
        // checkbox only rebuilds the plan. See InvalidateMaps.
        private TransformMap mirrorMap;
        private TransformMap externalMap;
        private HashSet<Transform> externalMapScope;

        // A component the next rescan narrows the selection down to, see SetSource
        private Component pendingOnlyComponent;

        public CopySession(Inputs inputs, CopySettings settings)
        {
            this.inputs = inputs ?? throw new ArgumentNullException(nameof(inputs));
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <summary>Read here; changed through <see cref="ChangeSettings"/>, which plans with them.</summary>
        public CopySettings Settings { get; }

        public GameObject SourceRoot => inputs.SourceRoot;
        public GameObject TargetRoot => inputs.TargetRoot;
        public bool MirrorMode => inputs.MirrorMode;
        public Side MirrorSide => inputs.MirrorSide;

        /// <summary>False until the first <see cref="Rescan"/>.</summary>
        public bool HasScanned { get; private set; }

        /// <summary>The components on offer: those of the source, only the chosen side in a mirror copy.</summary>
        public IReadOnlyList<ComponentEntry> Entries => entries;

        /// <summary>
        /// Nested prefabs and empty objects of the source that the target lacks. They are added when a selected
        /// component needs them, and on request (a hat that only has meshes, an anchor object, ...).
        /// </summary>
        public IReadOnlyList<Transform> MissingRoots => missingRoots;

        /// <summary>Null while there is no source, or no target that can take the copy.</summary>
        public TransformMap Map { get; private set; }

        /// <summary>Null whenever <see cref="Map"/> is.</summary>
        public CopyPlan Plan { get; private set; }

        /// <summary>The user's corrections of the map. A null target means "no counterpart".</summary>
        public IReadOnlyDictionary<Transform, Transform> ManualMappings => manualMappings;

        #region Roots and mode

        /// <summary>
        /// Starts over with another source: the choices of the component list and the mappings belong to the
        /// old one. With <paramref name="only"/>, just that component is checked after the scan, so a single
        /// component can be brought over from its context menu without searching the list for it.
        /// </summary>
        public void SetSource(GameObject source, Component only = null)
        {
            pendingOnlyComponent = only;
            // A component on the middle line has no other side to go to, so it asks for an ordinary copy
            if (MirrorMode && only != null &&
                new MirrorSides(AvatarRoots.MirrorRoot(source.transform)).Of(only.transform) == Side.None)
            {
                inputs.MirrorMode = false;
            }

            inputs.SourceRoot = source;
            StartOverWithSource();
        }

        /// <summary>
        /// Copies in the other direction. The component list and the mappings belong to the old source, so they
        /// start over just like after picking a new source.
        /// </summary>
        public void Swap()
        {
            (inputs.SourceRoot, inputs.TargetRoot) = (inputs.TargetRoot, inputs.SourceRoot);
            pendingOnlyComponent = null;
            StartOverWithSource();
        }

        private void StartOverWithSource()
        {
            if (MirrorMode && SourceRoot != null) AdoptSideOf(SourceRoot.transform);
            ForgetMappings();
            Rescan(resetSelection: true);
        }

        /// <summary>
        /// Picking a target ("Use as Target" from the menu, ...) asks for an ordinary copy. The mappings belong to
        /// the old target and start over.
        /// </summary>
        public void SetTarget(GameObject target)
        {
            bool leavesMirror = target != null && MirrorMode;
            if (leavesMirror) inputs.MirrorMode = false;

            inputs.TargetRoot = target;
            ForgetMappings();
            // The list of a mirror copy holds one side only
            if (leavesMirror) Rescan(resetSelection: false);
            else Recompute();
        }

        /// <summary>
        /// The source stays, so the checks of the components that are still listed stay too. The mirror map
        /// pairs other objects than the map to a target, so the mappings start over.
        /// </summary>
        public void SetMirrorMode(bool value)
        {
            inputs.MirrorMode = value;
            if (MirrorMode && SourceRoot != null) AdoptSideOf(SourceRoot.transform);
            ForgetMappings();
            Rescan(resetSelection: false);
        }

        public void SetMirrorSide(Side side)
        {
            inputs.MirrorSide = side;
            Rescan(resetSelection: false);
        }

        /// <summary>
        /// A source on one side (a hand, a component picked from its context menu, ...) only has something to
        /// copy in one direction, so that direction is chosen. The avatar itself leaves the choice alone.
        /// </summary>
        /// <param name="sides">Those of the mirror map when it is at hand; read from the avatar otherwise.</param>
        private void AdoptSideOf(Transform transform, MirrorSides sides = null)
        {
            var side = (sides ?? new MirrorSides(AvatarRoots.MirrorRoot(SourceRoot.transform))).Of(transform);
            if (side != Side.None) inputs.MirrorSide = side;
        }

        /// <summary>
        /// What is wrong with the target, as a localization key, or null. <paramref name="isError"/> is set for a
        /// target that cannot take a copy at all; nothing is planned then. The other warnings only keep Apply
        /// disabled, see <see cref="IsTargetAsset"/>.
        /// </summary>
        public string TargetWarning(out bool isError)
        {
            isError = false;

            // The source is its own target, and an asset can take a copy no more than as a target
            if (MirrorMode) return IsTargetAsset ? "componentCopier.warning.mirrorSourceIsAsset" : null;

            if (TargetRoot == null) return null;

            if (SourceRoot != null)
            {
                if (TargetRoot == SourceRoot)
                {
                    isError = true;
                    return "componentCopier.warning.sameObject";
                }

                if (TargetRoot.transform.IsChildOf(SourceRoot.transform) ||
                    SourceRoot.transform.IsChildOf(TargetRoot.transform))
                {
                    isError = true;
                    return "componentCopier.warning.nested";
                }
            }

            return IsTargetAsset ? "componentCopier.warning.targetIsAsset" : null;
        }

        /// <summary>
        /// Assets are accepted as a target for diff checks only: applying to an asset (e.g. a Prefab Variant)
        /// cannot be undone, so Apply stays disabled for them.
        /// </summary>
        public bool IsTargetAsset => EffectiveTarget != null && EditorUtility.IsPersistent(EffectiveTarget);

        /// <summary>The object that receives the copy: the source itself in a mirror copy.</summary>
        private GameObject EffectiveTarget => MirrorMode ? SourceRoot : TargetRoot;

        private bool CanPlan
        {
            get
            {
                if (SourceRoot == null) return false;
                if (MirrorMode) return true;

                TargetWarning(out bool isError);
                return TargetRoot != null && !isError;
            }
        }

        #endregion

        #region Component selection

        public bool IsSelected(ComponentKey key) => selectedKeys.Contains(key);

        public bool TryGetPlanned(ComponentKey key, out PlannedComponent planned) =>
            plannedByKey.TryGetValue(key, out planned);

        /// <summary>
        /// A checkbox says whether the component ends up in the target. Inside a nested prefab that gets added
        /// that is true without being selected: the prefab arrives as a whole.
        /// </summary>
        public bool IsChecked(ComponentEntry entry)
        {
            if (selectedKeys.Contains(entry.Key)) return true;
            return plannedByKey.TryGetValue(entry.Key, out var planned) && planned.Implicit && planned.WillWrite;
        }

        public void SetChecked(ComponentEntry entry, bool value) => SetChecked(new[] { entry }, value);

        public void SetChecked(IEnumerable<ComponentEntry> toChange, bool value)
        {
            foreach (var entry in toChange)
                Check(entry, value);

            Recompute();
        }

        /// <summary>
        /// Checks the components that <paramref name="filter"/> matches, or unchecks them when all of them are
        /// checked already. A preset chip is "on" exactly when all of its components are checked, so it always
        /// reflects the real selection, even after single checkboxes were changed.
        /// </summary>
        public void TogglePreset(Func<ComponentEntry, bool> filter)
        {
            var matching = entries.Where(filter).ToList();
            if (matching.Count == 0) return;

            SetChecked(matching, !matching.All(IsChecked));
        }

        /// <summary>Selects the components that unresolved references point at, see the pre-check.</summary>
        public void SelectDependencies(IEnumerable<ComponentKey> keys)
        {
            foreach (var key in keys)
            {
                selectedKeys.Add(key);
                leftOutKeys.Remove(key);
            }

            Recompute();
        }

        /// <summary>
        /// Unchecking a component that arrives with a nested prefab cannot simply deselect it, it would still
        /// come along. It is remembered as left out instead, and removed from the new instance.
        /// </summary>
        private void Check(ComponentEntry entry, bool value)
        {
            plannedByKey.TryGetValue(entry.Key, out var planned);

            if (!value)
            {
                selectedKeys.Remove(entry.Key);
                if (planned != null && planned.ArrivesWithPrefab) leftOutKeys.Add(entry.Key);
                return;
            }

            bool wasLeftOut = leftOutKeys.Remove(entry.Key);
            // Back to coming along with the prefab. Selecting it would keep copying a component that is not
            // copied by default (a renderer of a hat, ...) once the prefab exists in the target.
            if (wasLeftOut && planned != null && planned.LeftOut &&
                entry.Category == ComponentCategory.ExcludedByDefault)
            {
                return;
            }

            selectedKeys.Add(entry.Key);
        }

        #endregion

        #region Missing objects

        /// <summary>True when the plan creates the object, asked for or not.</summary>
        public bool IsObjectPlanned(Transform missing) => plannedRoots.Contains(missing);

        /// <summary>
        /// On its way because a selected component needs it. It counts as checked, and unchecking it would have
        /// no effect.
        /// </summary>
        public bool IsObjectAddedByComponents(Transform missing) =>
            IsObjectPlanned(missing) && !IsObjectSelected(missing);

        public bool IsObjectChecked(Transform missing) => IsObjectSelected(missing) || IsObjectPlanned(missing);

        /// <summary>
        /// Asks for the objects or takes the request back. Objects that selected components bring along are left
        /// to those: asking for them here would keep them after the components are deselected.
        /// </summary>
        public void SetObjectsChecked(IEnumerable<Transform> missing, bool value)
        {
            foreach (var transform in missing)
            {
                if (IsObjectAddedByComponents(transform)) continue;

                if (value) selectedObjectPaths.Add(ObjectPath(transform));
                else selectedObjectPaths.Remove(ObjectPath(transform));
            }

            Recompute();
        }

        /// <summary>True when the user asked for the object, whether a selected component needs it or not.</summary>
        private bool IsObjectSelected(Transform missing) => selectedObjectPaths.Contains(ObjectPath(missing));

        private string ObjectPath(Transform source) =>
            ObjectMatcher.GetRelativePathFromRoot(source, SourceRoot.transform);

        #endregion

        #region Mappings

        public bool IsPickingExisting(Transform source) => pickExistingFor.Contains(source);

        /// <summary>
        /// Lists an object that would be created among the ones that need a counterpart, for the user to name an
        /// existing one, or takes that back. Nothing else changes: the object is created until a mapping says
        /// otherwise.
        /// </summary>
        public void SetPickingExisting(Transform source, bool value)
        {
            if (value) pickExistingFor.Add(source);
            else pickExistingFor.Remove(source);
        }

        /// <param name="target">Null for "no counterpart".</param>
        public void SetManualMapping(Transform source, Transform target) =>
            SetManualMappings(new[] { (source, target) });

        public void SetManualMappings(IEnumerable<(Transform source, Transform target)> mappings)
        {
            foreach (var (source, target) in mappings)
                manualMappings[source] = target;

            InvalidateMaps();
            Recompute();
        }

        /// <summary>
        /// Back to what the automatic rules say, which for an object to create also means being created again.
        /// </summary>
        public void ResetMapping(Transform source)
        {
            manualMappings.Remove(source);
            pickExistingFor.Remove(source);
            InvalidateMaps();
            Recompute();
        }

        /// <summary>
        /// The corrections belong to one pair of roots, and so does the wish to pick an existing object instead
        /// of creating one: the new target may well have it. Another pair starts over.
        /// </summary>
        private void ForgetMappings()
        {
            manualMappings.Clear();
            pickExistingFor.Clear();
            InvalidateMaps();
        }

        #endregion

        #region Scanning and planning

        /// <summary>
        /// Drops the maps kept across plans, because the scene or something they are built from changed.
        /// The window calls it right away on a scene change, not with the rescan that follows: a checkbox ticked
        /// in the meantime must not plan with a map that still holds deleted objects.
        /// </summary>
        public void InvalidateMaps()
        {
            mirrorMap = null;
            externalMap = null;
            externalMapScope = null;
        }

        /// <summary>
        /// Scans the source again. The selection is carried over by <see cref="ComponentKey"/>; components that
        /// appear for the first time follow the default selection rule.
        /// </summary>
        public void Rescan(bool resetSelection)
        {
            HasScanned = true;
            InvalidateMaps();

            var previousKeys = new HashSet<ComponentKey>(entries.Select(e => e.Key));
            entries = SourceRoot != null ? ComponentScanner.Scan(SourceRoot.transform) : new List<ComponentEntry>();
            if (MirrorMode && SourceRoot != null)
            {
                // Built here rather than on the Recompute below, which reuses it
                var sides = GetMirrorMap().Sides;

                // A component picked from its context menu decides the direction
                if (pendingOnlyComponent != null) AdoptSideOf(pendingOnlyComponent.transform, sides);

                // Only the chosen side is on offer; objects on the middle line have no other side to go to
                entries = entries.Where(e => sides.Of(e.Host) == MirrorSide).ToList();
            }

            if (resetSelection)
            {
                selectedKeys.Clear();
                leftOutKeys.Clear();
                selectedObjectPaths.Clear();
                previousKeys.Clear();
            }

            var currentKeys = new HashSet<ComponentKey>(entries.Select(e => e.Key));
            selectedKeys.IntersectWith(currentKeys);
            leftOutKeys.IntersectWith(currentKeys);
            foreach (var entry in entries)
            {
                if (!previousKeys.Contains(entry.Key) && entry.Category != ComponentCategory.ExcludedByDefault)
                    selectedKeys.Add(entry.Key);
            }

            if (pendingOnlyComponent != null)
            {
                var only = entries.FirstOrDefault(e => e.Component == pendingOnlyComponent);
                pendingOnlyComponent = null;
                // Nothing else is checked even when it is not listed: the user asked for that one component
                selectedKeys.Clear();
                if (only != null) selectedKeys.Add(only.Key);
            }

            // Drop manual mappings whose objects were deleted. A genuinely null value is kept: it means
            // "no counterpart", whereas a destroyed target only compares equal to null.
            var dead = manualMappings
                .Where(p => p.Key == null || (!ReferenceEquals(p.Value, null) && p.Value == null))
                .Select(p => p.Key)
                .ToList();
            foreach (var key in dead)
                manualMappings.Remove(key);
            if (dead.Count > 0) InvalidateMaps();
            pickExistingFor.RemoveWhere(t => t == null);

            Recompute();
        }

        /// <summary>
        /// Changes the settings and plans with them. Keeping them for the next session (see
        /// <see cref="CopySettings.Save"/>) is up to the caller, so that tests leave the user's settings alone.
        /// </summary>
        public void ChangeSettings(Action<CopySettings> change)
        {
            change(Settings);
            Recompute();
        }

        /// <summary>Plans the copy anew from the current choices; every input ends with it.</summary>
        private void Recompute()
        {
            Map = null;
            Plan = null;
            plannedByKey.Clear();
            missingRoots.Clear();
            plannedRoots.Clear();

            if (!CanPlan) return;

            Map = MirrorMode
                ? GetMirrorMap()
                : TransformMapper.Build(SourceRoot.transform, TargetRoot.transform, manualMappings);
            // The mirror map spans the avatar; only the chosen side of the source is on offer
            var scope = MirrorMode ? SourceRoot.transform : null;
            missingRoots.AddRange(NestedPrefabs.FindMissingRoots(Map, scope));
            missingRoots.AddRange(MissingObjects.FindRoots(Map, scope));
            if (MirrorMode) missingRoots.RemoveAll(t => Map.Sides.Of(t) != MirrorSide);
            Plan = BuildPlan(Settings);

            // A mirror copy can bring a prefab from elsewhere in the avatar. Its components are keyed from
            // the avatar, and such a key could name a component of the source.
            foreach (var planned in Plan.Components)
            {
                if (Hierarchy.IsInside(planned.Entry.Host, SourceRoot.transform))
                    plannedByKey[planned.Entry.Key] = planned;
            }

            plannedRoots.UnionWith(Plan.ObjectsToCreate.Where(o => o.PrefabRoot == null).Select(o => o.Source));
            plannedRoots.IntersectWith(missingRoots);
        }

        /// <summary>
        /// The plan for "Diff check only". It compares against the existing components whatever the policy says:
        /// with Add or Replace the plan has no existing counterparts to compare with. Null without a map.
        /// </summary>
        public CopyPlan BuildDiffCheckPlan()
        {
            if (Map == null) return null;

            var diffSettings = Settings.Clone();
            diffSettings.ExistingPolicy = ExistingComponentPolicy.Overwrite;
            return BuildPlan(diffSettings);
        }

        private CopyPlan BuildPlan(CopySettings planSettings)
        {
            return CopyPlanBuilder.Build(
                entries.Where(e => selectedKeys.Contains(e.Key)), Map, planSettings,
                objectsToAdd: missingRoots.Where(IsObjectSelected),
                // A mirror copy stays inside one avatar, so there are no surroundings to redirect to
                externalMapProvider: MirrorMode ? null : GetExternalMap,
                leftOut: leftOutKeys,
                mirrorRoot: MirrorMode ? Map.SourceRoot : null,
                keyRoot: SourceRoot.transform);
        }

        /// <summary>
        /// The mirror map spans the whole avatar, so like the map of the surroundings it is kept until
        /// <see cref="InvalidateMaps"/>.
        /// </summary>
        private TransformMap GetMirrorMap()
        {
            return mirrorMap ??= MirrorMapper.Build(AvatarRoots.MirrorRoot(SourceRoot.transform), manualMappings);
        }

        /// <summary>
        /// The map of the surroundings spans whole avatars, so it is kept until <see cref="InvalidateMaps"/>.
        /// A new plan usually asks for objects that are resolved already; any other object has it rebuilt.
        /// </summary>
        private TransformMap GetExternalMap(IReadOnlyCollection<Transform> referenced)
        {
            if (externalMapScope == null || !externalMapScope.IsSupersetOf(referenced))
            {
                externalMapScope = new HashSet<Transform>(referenced);
                externalMap = ExternalContext.BuildMap(
                    SourceRoot.transform, TargetRoot.transform, referenced, manualMappings);
            }

            return externalMap;
        }

        #endregion
    }
}
