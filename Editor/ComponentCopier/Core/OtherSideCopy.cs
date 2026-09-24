using System.Collections.Generic;
using System.Linq;
using Kanameliser.Editor.MAMaterialHelper.Common;
using UnityEditor;
using UnityEngine;

namespace Kanameliser.EditorPlus.ComponentCopier
{
    /// <summary>
    /// "Copy to Other Side" for one object: its pose and the components on it go to its counterpart on the other
    /// side of the avatar, which is created below the other side's parent when there is none. What the counterpart
    /// has is overwritten. It is the mirror copy of the main window at the size of one object, planned by the same
    /// <see cref="CopyPlanBuilder"/>; the only choices are the counterpart, whether to create it, and what to copy.
    /// </summary>
    internal sealed class OtherSideCopy
    {
        private List<ComponentEntry> entries = new();
        private readonly HashSet<ComponentKey> selectedKeys = new();
        private readonly Dictionary<ComponentKey, PlannedComponent> plannedByKey = new();

        // The mappings the user made here (the source's counterpart, a suggestion confirmed for a parent or a reference,
        // "none" to have one created), and the objects an apply created, pinned to their sources (see KeepCreated)
        private readonly Dictionary<Transform, Transform> manualMappings = new();

        // Spans the avatar, so it is kept while nothing it is built from changes, see InvalidateMap
        private TransformMap mirrorMap;

        // The map of the current plan; null while there is a Problem
        private TransformMap map;

        // Checked by the first scan even when it is not copied by default: the user asked for it
        private Component pendingOnly;

        // Set once the pose is the user's choice: asked for from a header, or ticked. Until then it follows the
        // default, which depends on the counterpart, see IsBone.
        private bool poseChosen;

        // Sources pinned to the object an apply created for them, see KeepCreated, oldest first. Shared by every copy,
        // whichever object the window is used on next, and kept in the session state across domain reloads (by
        // instance IDs, which hold until the editor is closed; a source that is deleted keeps its pin for the Undo
        // that brings it back). A pin is for applying again soon after, so only the latest few are kept.
        private const string PinsSessionKey = "Kanameliser.EditorPlus.ComponentCopier.OtherSidePins";
        internal const int MaxPins = 32;
        private static List<Pin> pins;
        private static List<Pin> Pins => pins ??= LoadPins();

        // The sources whose manual mapping stands for a pin, so that a pin that is dropped takes its mapping along
        private readonly HashSet<Transform> fromPins = new();

        /// <summary>Forgets every pin of <see cref="KeepCreated"/>, so that tests start from nothing.</summary>
        internal static void ForgetPins()
        {
            pins = new List<Pin>();
            SavePins();
        }

        private static Pin PinOf(Transform source)
        {
            if (source == null) return null;

            int sourceId = source.GetInstanceID();
            return Pins.Find(pin => pin.SourceId == sourceId);
        }

        /// <summary>Reads the pins from the session state again, as after a domain reload. For tests.</summary>
        internal static void ReloadPins() => pins = null;

        /// <param name="only">
        /// The component the copy was asked for from, if any. It starts out as the only thing checked, like the item of
        /// the main window does: the rest of the object may differ between the sides on purpose. For the Transform
        /// that is the pose alone, bone or not. Without it, or when it has been deleted since, the pose and every
        /// component that is copied by default start out checked.
        /// </param>
        public OtherSideCopy(Transform source, Component only = null)
        {
            Source = source;
            pendingOnly = only;
            if (only != null)
            {
                CopyPose = only is Transform;
                poseChosen = true;
            }

            Rescan();
        }

        public Transform Source { get; }

        /// <summary>
        /// Whether an existing counterpart is given the mirrored pose. A counterpart that is created gets it anyway.
        /// Asked for from a header, only the Transform's has it on. Otherwise it is off for a bone (see
        /// <see cref="IsBone"/>), which is looked at again whenever the counterpart changes, and on for anything else.
        /// </summary>
        public bool CopyPose { get; private set; } = true;

        /// <summary>
        /// True for a bone: a skinning bone (or an ancestor of one), one paired as a humanoid bone, or one whose
        /// counterpart is a skinning bone. The pose of a bone shapes the mesh (or the bone an outfit is merged onto),
        /// and the two sides of a rig need not be mirror images of each other, so it is only mirrored when the user
        /// asks for it.
        /// </summary>
        public bool IsBone { get; private set; }

        /// <summary>Whether a missing counterpart is created, see <see cref="CopySettings.CreateMissingObjects"/>.</summary>
        public bool CreateMissing { get; private set; } = true;

        /// <summary>The components of the source.</summary>
        public IReadOnlyList<ComponentEntry> Entries => entries;

        /// <summary>The side the source is on. None when there is nothing to copy to, see <see cref="Problem"/>.</summary>
        public Side Side { get; private set; }

        /// <summary>Null while there is a <see cref="Problem"/>.</summary>
        public CopyPlan Plan { get; private set; }

        /// <summary>How the source is paired with the other side. Null while there is a <see cref="Problem"/>.</summary>
        public TransformMapping Mapping { get; private set; }

        /// <summary>
        /// The counterpart as the user sees it: the one in use, or the suggestion that waits to be confirmed. Null
        /// when the copy creates it, which a nested prefab around the source does even where the map knows a
        /// counterpart: the prefab is brought over as a whole.
        /// </summary>
        public Transform Counterpart
        {
            get
            {
                if (Mapping == null || Created != null) return null;
                return Mapping.IsUsable ? Mapping.Target : Suggestion;
            }
        }

        /// <summary>True when the copy goes to an existing counterpart: one that was found, or named by the user.</summary>
        public bool CounterpartExists => Mapping != null && Mapping.IsUsable && Created == null;

        /// <summary>
        /// The suggestion the user can confirm: the plan waits for it, or creates the counterpart instead of using it.
        /// Null for one that cannot be the counterpart, see <see cref="UnusableSuggestion"/>.
        /// </summary>
        public Transform Suggestion => IsSuggested(Mapping) && CanBeCounterpart(Mapping.Target) ? Mapping.Target : null;

        /// <summary>
        /// A suggestion that cannot be the counterpart, such as an object of the same name on the middle line. The
        /// copy waits all the same, until the user names the counterpart or has one created.
        /// </summary>
        public Transform UnusableSuggestion =>
            IsSuggested(Mapping) && !CanBeCounterpart(Mapping.Target) ? Mapping.Target : null;

        private static bool IsSuggested(TransformMapping mapping) =>
            mapping != null && mapping.State == MappingState.NeedsReview && mapping.Target != null;

        /// <summary>
        /// False for a skinning bone, which is never created: its counterpart has to be named. See
        /// <see cref="CanCreateAt"/> for the root of a nested prefab.
        /// </summary>
        public bool CanCreate => CanCreateAt(Source);

        /// <summary>
        /// False for a skinning bone (or an ancestor of one), see <see cref="CanCreate"/>, unless it is the root of a
        /// nested prefab: that is instantiated with its bones rather than created. Not when another object in the
        /// prefab is mapped by hand, though: the prefab is in the target then (see <see cref="NestedPrefabs.IsMissing"/>),
        /// and its root would be created like any other bone. The root's own mapping is what creating it replaces.
        /// </summary>
        public bool CanCreateAt(Transform source) =>
            map != null && (!map.SourceSkeleton.IsBone(source) ||
                            (NestedPrefabs.GetPrefabAsset(source, map.SourceRoot) != null &&
                             !map.HasManualMappingWithin(source, excludedSource: source)));

        /// <summary>
        /// True when the user named a counterpart, confirmed a suggestion or asked for one to be created, see
        /// <see cref="ResetMappings"/>. The objects an apply created are no choice of the user's, and do not count.
        /// </summary>
        public bool HasManualMappings =>
            manualMappings.Keys.Any(source =>
                source == null || !fromPins.Contains(source) || (PinOf(source)?.WasNone == true && !IsKept(source)));

        /// <summary>The pose of the counterpart, when the plan has one.</summary>
        public PlannedPose Pose => Plan?.Poses.FirstOrDefault();

        /// <summary>The counterpart the plan creates, or null.</summary>
        public PlannedObject Created => Plan?.ObjectsToCreate.FirstOrDefault(o => o.Source == Source);

        /// <summary>
        /// The other objects the copy creates, one per place in the hierarchy: empty objects that a copied component
        /// refers to, with the parents they need, or a nested prefab that one of them sits in. Neither the counterpart
        /// nor the parents it is created below, which <see cref="CreatedPath"/> shows. Listed so that nothing is
        /// created unseen.
        /// </summary>
        public IEnumerable<PlannedObject> OtherCreated
        {
            get
            {
                if (Plan == null) return Enumerable.Empty<PlannedObject>();

                var counterpartChain = new HashSet<PlannedObject>();
                for (var planned = Created; planned != null; planned = planned.ParentToCreate)
                    counterpartChain.Add(planned);

                // An object that arrives with a prefab is told of by the prefab; one below another new object, by
                // the path of the deepest one
                var parents = new HashSet<PlannedObject>(Plan.ObjectsToCreate.Select(o => o.ParentToCreate).Where(p => p != null));
                return Plan.ObjectsToCreate.Where(o =>
                    o.PrefabRoot == null && !counterpartChain.Contains(o) && (o.IsPrefabRoot || !parents.Contains(o)));
            }
        }

        /// <summary>
        /// The parent that keeps the counterpart from being created, when the plan is blocked by one: a parent (or
        /// the nested prefab around the source) whose counterpart is only a suggestion, or a bone the other side
        /// lacks. Null when the source itself is the reason, or when nothing is blocked. Naming the counterpart of
        /// the source gets around it too.
        /// </summary>
        public Transform BlockingParent
        {
            get
            {
                var reason = BlockReason;
                if (reason != BlockReason.HostNeedsReview && reason != BlockReason.BoneMissing) return null;

                // Where the planner stopped, rather than worked out again: a prefab is planned as a whole
                var blockedAt = Pose != null && Pose.BlockReason != BlockReason.None
                    ? Pose.BlockedAt
                    : Plan.Components.FirstOrDefault(c => c.Entry.Host == Source && c.BlockReason == reason)?.BlockedAt;
                return blockedAt != null && blockedAt != Source ? blockedAt : null;
            }
        }

        /// <summary>
        /// Why the counterpart cannot take the copy, or None. The pose and every component share that one host, so
        /// one reason stands for all of them.
        /// </summary>
        public BlockReason BlockReason
        {
            get
            {
                if (Plan == null) return BlockReason.None;
                if (Pose != null && Pose.BlockReason != BlockReason.None) return Pose.BlockReason;

                var blocked = Plan.Components.FirstOrDefault(c => c.Entry.Host == Source && c.BlockReason != BlockReason.None);
                return blocked?.BlockReason ?? BlockReason.None;
            }
        }

        /// <summary>True when applying the plan would change something.</summary>
        public bool HasWork => Plan != null &&
                               (Plan.Components.Any(c => c.WillWrite) || Plan.ObjectsToCreate.Count > 0 ||
                                Plan.Poses.Any(p => p.WillWrite));

        /// <summary>Why nothing can be planned, as a localization key. Null when the copy can be set up.</summary>
        public string Problem
        {
            get
            {
                if (Source == null) return "componentCopier.otherSide.sourceGone";
                // Changes to an asset cannot be undone, and an asset has no scene around it to mirror in
                if (EditorUtility.IsPersistent(Source)) return "componentCopier.otherSide.sourceIsAsset";
                if (Side == Side.None) return "componentCopier.otherSide.noSide";
                return null;
            }
        }

        #region Choices

        /// <summary>
        /// A checkbox says whether the component ends up on the counterpart. One that arrives with a nested prefab
        /// does, checked or not.
        /// </summary>
        public bool IsChecked(ComponentEntry entry)
        {
            if (selectedKeys.Contains(entry.Key)) return true;
            return plannedByKey.TryGetValue(entry.Key, out var planned) && planned.Implicit && planned.WillWrite;
        }

        public bool TryGetPlanned(ComponentEntry entry, out PlannedComponent planned) =>
            plannedByKey.TryGetValue(entry.Key, out planned);

        /// <summary>
        /// True for a component that comes along with a nested prefab that is added: the prefab arrives as a whole,
        /// and leaving parts of it out is left to the main window.
        /// </summary>
        public bool ArrivesWithPrefab(ComponentEntry entry) =>
            plannedByKey.TryGetValue(entry.Key, out var planned) && planned.ArrivesWithPrefab;

        public void SetChecked(ComponentEntry entry, bool value)
        {
            if (value) selectedKeys.Add(entry.Key);
            else selectedKeys.Remove(entry.Key);
            Recompute();
        }

        public void SetCopyPose(bool value)
        {
            CopyPose = value;
            poseChosen = true;
            Recompute();
        }

        public void SetCreateMissing(bool value)
        {
            CreateMissing = value;
            Recompute();
        }

        /// <summary>
        /// Names the counterpart. Null means there is none, and one is created. Returns false, and changes nothing, for
        /// an object that cannot be the counterpart: one outside of the avatar, whose mirror image this is not, and
        /// one on its middle line (the avatar itself, Hips, ...), which would be moved onto the other side. One on the
        /// source's own side is taken, and blocked with its reason, see <see cref="BlockReason.SameSide"/>.
        /// </summary>
        public bool SetCounterpart(Transform counterpart)
        {
            if (Source == null || map == null) return false;
            if (counterpart != null && !CanBeCounterpart(counterpart)) return false;

            SetMapping(Source, counterpart);
            return true;
        }

        private bool CanBeCounterpart(Transform transform)
        {
            return map != null && transform != null && !EditorUtility.IsPersistent(transform) &&
                   transform != map.SourceRoot && transform.IsChildOf(map.SourceRoot) &&
                   map.Sides.Of(transform) != Side.None;
        }

        /// <summary>
        /// Has a new counterpart created instead of taking the suggestion: for the source, or for a parent that keeps
        /// it from being created (see <see cref="BlockingParent"/>). Asking for that turns creation on.
        /// </summary>
        public void CreateNew(Transform source = null)
        {
            if (Source == null || map == null) return;

            CreateMissing = true;
            SetMapping(source != null ? source : Source, null);
        }

        /// <summary>
        /// The suggestion for an object that <see cref="ConfirmSuggestion"/> takes: one that can be a counterpart.
        /// Null otherwise.
        /// </summary>
        public Transform SuggestionFor(Transform source)
        {
            var mapping = map?.Get(source);
            return IsSuggested(mapping) && CanBeCounterpart(mapping.Target) ? mapping.Target : null;
        }

        /// <summary>
        /// Takes the suggestion for an object: a parent that keeps the counterpart from being created (see
        /// <see cref="BlockingParent"/>), or an object that keeps a reference from being carried over (see
        /// <see cref="PendingSuggestionAt"/>).
        /// </summary>
        public void ConfirmSuggestion(Transform source)
        {
            var suggestion = SuggestionFor(source);
            if (suggestion != null) SetMapping(source, suggestion);
        }

        /// <summary>
        /// The object whose unconfirmed suggestion keeps a reference to <paramref name="value"/> from being carried
        /// over, when taking that suggestion is sure to carry it over. For an empty object the plan would create, that
        /// is where the planner stopped (the object, or a parent it would be created below). For anything else only its
        /// own suggestion is offered, for a component only when the suggested object has one of its kind: what a
        /// suggestion further up would bring is up to the mapping. Null otherwise.
        /// </summary>
        public Transform PendingSuggestionAt(Object value)
        {
            var referenced = ReferenceWalker.GetTransform(value);
            if (Plan == null || referenced == null) return null;

            if (Plan.ReferenceBlocks.TryGetValue(referenced, out var block))
            {
                return block.Reason == BlockReason.HostNeedsReview && SuggestionFor(block.At) != null
                    ? block.At
                    : null;
            }

            var suggestion = SuggestionFor(referenced);
            if (suggestion == null) return null;
            // A Transform is resolved through the map, like a GameObject
            if (value is Component component && !(value is Transform) &&
                ComponentScanner.FindByTypeAndIndex(suggestion, component.GetType(),
                    ComponentScanner.IndexAmongSameType(component)) == null)
            {
                return null;
            }

            return referenced;
        }

        /// <summary>
        /// After an apply of <paramref name="plan"/>, so that applying again copies into what it created instead of
        /// creating it a second time: every object it created is pinned as the counterpart of its source. The
        /// automatic rules need not find it again (the second of two same-name objects is looked for as the second),
        /// and a "there is none" of the user now means that object. Undone, each goes back to what it was before;
        /// redone, it is pinned again. Of the latest applies only, see <see cref="MaxPins"/>.
        /// </summary>
        public void KeepCreated(CopyPlan plan)
        {
            foreach (var planned in plan.ObjectsToCreate)
            {
                // Also skips what was left out of a prefab, and removed again
                if (planned.Created == null) continue;

                bool named = manualMappings.TryGetValue(planned.Source, out var target);
                if (named && !ReferenceEquals(target, null)) continue;
                // The objects that arrived with a prefab are found again below its root, which is pinned. A "none"
                // for one of them is the user's word, which the pin has to take over.
                if (planned.PrefabRoot != null && !named) continue;

                manualMappings[planned.Source] = planned.Created;
                fromPins.Add(planned.Source);
                int sourceId = planned.Source.GetInstanceID();
                Pins.RemoveAll(pin => pin.SourceId == sourceId);
                Pins.Add(new Pin(sourceId, planned.Created.GetInstanceID(), wasNone: named));
            }

            if (Pins.Count > MaxPins) Pins.RemoveRange(0, Pins.Count - MaxPins);
            SavePins();
            InvalidateMap();
        }

        /// <summary>
        /// True when the current counterpart belongs to a pair remembered by <see cref="KeepCreated"/>, in either
        /// direction. False while either object is undone or the mapping no longer uses that pair.
        /// </summary>
        public bool IsKept(Transform source)
        {
            if (source == null) return false;
            var target = manualMappings.TryGetValue(source, out var manual) ? manual : map?.Get(source)?.Target;
            if (target == null) return false;

            int sourceId = source.GetInstanceID();
            int targetId = target.GetInstanceID();
            return Pins.Any(pin => (pin.SourceId == sourceId && pin.CreatedId == targetId) ||
                                   (pin.CreatedId == sourceId && pin.SourceId == targetId));
        }

        /// <summary>An object an apply created, pinned as the counterpart of its source, see <see cref="KeepCreated"/>.</summary>
        private sealed class Pin
        {
            /// <summary>Instance IDs: Undo destroys an object, and Redo brings it back under the same ID.</summary>
            public readonly int SourceId;
            public readonly int CreatedId;

            /// <summary>True when the user had said there is none; else the automatic rules decided.</summary>
            public bool WasNone;

            public Pin(int sourceId, int createdId, bool wasNone)
            {
                SourceId = sourceId;
                CreatedId = createdId;
                WasNone = wasNone;
            }

            /// <summary>The source, or null while it is gone (deleted, and maybe brought back by Undo later).</summary>
            public Transform Source => EditorUtility.InstanceIDToObject(SourceId) as Transform;
        }

        private static List<Pin> LoadPins()
        {
            var loaded = new List<Pin>();
            foreach (string entry in SessionState.GetString(PinsSessionKey, "").Split(';'))
            {
                var parts = entry.Split(',');
                if (parts.Length == 3 && int.TryParse(parts[0], out int sourceId) &&
                    int.TryParse(parts[1], out int createdId))
                {
                    loaded.Add(new Pin(sourceId, createdId, wasNone: parts[2] == "1"));
                }
            }

            return loaded;
        }

        private static void SavePins()
        {
            SessionState.SetString(PinsSessionKey, string.Join(";",
                Pins.Select(pin => $"{pin.SourceId},{pin.CreatedId},{(pin.WasNone ? 1 : 0)}")));
        }

        private void SetMapping(Transform source, Transform target)
        {
            int sourceId = source.GetInstanceID();
            // Choosing from either end replaces the remembered pair. Otherwise creating a new counterpart from
            // the created object's side would leave two pins sharing that object.
            var replaced = Pins.Where(pin => pin.SourceId == sourceId || pin.CreatedId == sourceId).ToList();
            foreach (var pin in replaced)
            {
                Pins.Remove(pin);
                var pinned = pin.Source;
                if (pinned != null && fromPins.Remove(pinned)) manualMappings.Remove(pinned);
            }
            if (replaced.Count > 0) SavePins();

            manualMappings[source] = target;
            fromPins.Remove(source);
            InvalidateMap();
            Recompute();
        }

        /// <summary>
        /// Back to what the automatic rules find, for the counterpart and its parents alike. The objects an apply
        /// created stay the counterparts of their sources: dropped, applying again would create them a second time.
        /// </summary>
        public void ResetMappings()
        {
            // The pins of the counterpart and its parents, as Rescan scopes a "none": the "none" chosen for another
            // object stays that object's
            var inUse = manualMappings.Keys
                .Where(source => source != null && Hierarchy.IsInside(Source, source))
                .Select(PinOf)
                .Where(pin => pin != null)
                .ToList();

            // Also the "none" a pin stands for while its object is undone
            foreach (var source in manualMappings.Keys.Where(source => !IsKept(source)).ToList())
            {
                manualMappings.Remove(source);
                fromPins.Remove(source);
            }
            // Undone, such an object now leaves its source to the automatic rules as well
            foreach (var pin in inUse) pin.WasNone = false;
            SavePins();

            InvalidateMap();
            Recompute();
        }

        #endregion

        #region Scanning and planning

        /// <summary>
        /// Drops the map kept across plans. Called on every scene change, before the rescan that follows it: a
        /// checkbox ticked in the meantime must not plan with a map that still holds deleted objects.
        /// </summary>
        public void InvalidateMap() => mirrorMap = null;

        /// <summary>
        /// Reads the source again and plans anew. The checks are carried over by <see cref="ComponentKey"/>;
        /// components that appear for the first time follow the default rule of the main window.
        /// </summary>
        public void Rescan()
        {
            InvalidateMap();

            var previousKeys = new HashSet<ComponentKey>(entries.Select(e => e.Key));
            entries = Source != null ? ComponentScanner.ScanObject(Source, Source) : new List<ComponentEntry>();
            selectedKeys.IntersectWith(entries.Select(e => e.Key));
            foreach (var entry in entries)
            {
                if (previousKeys.Contains(entry.Key)) continue;

                // Asked for from a component, the first scan checks that one alone: the others on the object may
                // differ between the sides on purpose
                bool check = pendingOnly != null
                    ? entry.Component == pendingOnly
                    : entry.Category != ComponentCategory.ExcludedByDefault;
                if (check) selectedKeys.Add(entry.Key);
            }

            pendingOnly = null;

            // An object that an apply created follows Undo and Redo: gone, its source goes back to what it was before
            // ("none", or the automatic rules), and back, it is the counterpart again. One moved where it cannot be a
            // counterpart (out of the avatar, onto the middle line or the source's side) counts as gone. A "none" only
            // comes back for this copy's own counterpart or a parent of it: for anything else it was a choice made
            // for another object.
            // Put in anew each time: a pin that was dropped since takes its mapping along
            foreach (var source in fromPins) manualMappings.Remove(source);
            fromPins.Clear();

            var mirrorRoot = Source != null ? AvatarRoots.MirrorRoot(Source) : null;
            MirrorSides sides = null;
            foreach (var pin in Pins)
            {
                // The pins of other avatars are left for the copies made there, and a source that is gone keeps its pin
                // for the Undo that brings it back
                var pinned = pin.Source;
                if (pinned == null || mirrorRoot == null || !Hierarchy.IsInside(pinned, mirrorRoot)) continue;

                var created = EditorUtility.InstanceIDToObject(pin.CreatedId) as Transform;
                bool usable = created != null && created != mirrorRoot && created.IsChildOf(mirrorRoot);
                if (usable)
                {
                    sides ??= new MirrorSides(mirrorRoot);
                    var side = sides.Of(created);
                    usable = side != Side.None && side != sides.Of(pinned);
                }

                if (usable)
                {
                    manualMappings[pinned] = created;
                    fromPins.Add(pinned);
                }
                else if (pin.WasNone && Hierarchy.IsInside(Source, pinned))
                {
                    manualMappings[pinned] = null;
                    fromPins.Add(pinned);
                }
            }

            // A counterpart the user named and then deleted leaves the choice to the automatic rules again. A
            // genuinely null one is kept: it means "there is none".
            foreach (var key in manualMappings
                         .Where(p => p.Key == null || (!ReferenceEquals(p.Value, null) && p.Value == null))
                         .Select(p => p.Key)
                         .ToList())
            {
                manualMappings.Remove(key);
            }

            Recompute();
        }

        private void Recompute()
        {
            Plan = null;
            Mapping = null;
            map = null;
            Side = Side.None;
            plannedByKey.Clear();

            if (Source == null || EditorUtility.IsPersistent(Source)) return;

            var built = mirrorMap ??= MirrorMapper.Build(AvatarRoots.MirrorRoot(Source), manualMappings, fromPins);
            // The avatar itself, and the objects on its middle line, have no other side to go to
            Side = built.Sides.Of(Source);
            if (Side == Side.None) return;

            map = built;
            Mapping = map.Get(Source);
            // A bone of an outfit that no mesh of it is skinned to still is one: MA merges it onto the avatar's. Whether
            // it pairs as a humanoid bone is the automatic answer's to say, also under a mapping made by hand.
            var automaticReason = Mapping.State == MappingState.Manual ? Mapping.AutomaticReason : Mapping.Reason;
            IsBone = map.SourceSkeleton.IsBone(Source) || automaticReason == MappingReason.MirroredBone ||
                     (map.SourceSkeleton.IsInArmature(Source) &&
                      HumanoidBoneDictionary.TryFindBone(RenameSuffix.Strip(Source.name), out _)) ||
                     (Mapping.Target != null && map.SourceSkeleton.IsBone(Mapping.Target));
            if (!poseChosen) CopyPose = !IsBone;

            var settings = new CopySettings
            {
                ExistingPolicy = ExistingComponentPolicy.Overwrite,
                CreateMissingObjects = CreateMissing,
                UnresolvedPolicy = UnresolvedReferencePolicy.Clear,
            };
            // A counterpart that is created is placed somewhere, and that is the mirrored pose. Asking for the pose
            // then also creates the counterpart when no component is checked, and says why it cannot be created.
            bool withPose = CopyPose || !Mapping.IsUsable;
            // A component deleted since the last scan is gone until the rescan that follows the change
            Plan = CopyPlanBuilder.Build(
                entries.Where(e => e.Component != null && selectedKeys.Contains(e.Key)), map, settings,
                mirrorRoot: map.SourceRoot,
                keyRoot: Source,
                poses: withPose ? new[] { Source } : null);

            // Only the source's own components: a prefab the copy brings along from elsewhere is keyed from the
            // avatar, and the avatar's own components have the same empty path as the source's
            foreach (var planned in Plan.Components)
            {
                if (planned.Entry.Host == Source) plannedByKey[planned.Entry.Key] = planned;
            }
        }

        /// <summary>
        /// Where the counterpart is created, as a path below the avatar ("Armature/Hips/Hand_R/Collider_R"). Null
        /// when it is not created.
        /// </summary>
        public string CreatedPath() => Created != null ? PathOf(Created) : null;

        /// <summary>Where an existing object is, as a path below the avatar. Null while there is a <see cref="Problem"/>.</summary>
        public string PathOf(Transform transform) =>
            map != null ? ObjectMatcher.GetRelativePathFromRoot(transform, map.SourceRoot) : null;

        /// <summary>Where an object of the plan is created, as a path below the avatar.</summary>
        public string PathOf(PlannedObject created)
        {
            var segments = new List<string>();
            for (var planned = created; planned != null; planned = planned.ParentToCreate)
            {
                segments.Add(planned.Name);
                if (planned.ExistingParent == null) continue;

                string parentPath = ObjectMatcher.GetRelativePathFromRoot(planned.ExistingParent, Plan.Map.SourceRoot);
                if (!string.IsNullOrEmpty(parentPath)) segments.Add(parentPath);
                break;
            }

            segments.Reverse();
            return string.Join("/", segments);
        }

        #endregion
    }
}
