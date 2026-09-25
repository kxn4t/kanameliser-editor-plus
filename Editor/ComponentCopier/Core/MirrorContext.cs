using System;
using UnityEngine;

namespace Kanameliser.EditorPlus.ComponentCopier
{
    /// <summary>
    /// Mirrors across the YZ plane of a root transform, i.e. the plane that splits a symmetric avatar into
    /// its left and right half. Local values are mirrored through world space: the frames of the left and
    /// right bone need not be mirror images of each other (bone rolls differ between rigs), so the result is
    /// wherever the mirror image of the world position ends up in the frame of the counterpart.
    /// The way through world space leaves float noise ("2.2e-08" where the source has 0). A result that close
    /// to the source value or its negation is taken to be exactly that: on a symmetric rig it is the exact
    /// answer, and a difference that small is far below anything placed by hand.
    /// </summary>
    internal sealed class MirrorContext
    {
        // Relative to the distance from the origin: float noise is some 1e-7 of it, a few operations deep
        private const float Precision = 1e-5f;
        private const float DirectionTolerance = 1e-5f;
        private const float AngleTolerance = 1e-3f;

        public Transform Root { get; }

        public MirrorContext(Transform root)
        {
            Root = root != null ? root : throw new ArgumentNullException(nameof(root));
        }

        public Vector3 ReflectPoint(Vector3 world)
        {
            var local = Root.InverseTransformPoint(world);
            local.x = -local.x;
            return Root.TransformPoint(local);
        }

        public Vector3 ReflectDirection(Vector3 world)
        {
            var local = Root.InverseTransformDirection(world);
            local.x = -local.x;
            return Root.TransformDirection(local);
        }

        /// <summary>
        /// The proper rotation that a mirrored object has: the axis is reflected and the angle negated,
        /// which in the plane's own space is (x, -y, -z, w).
        /// </summary>
        public Quaternion ReflectRotation(Quaternion world)
        {
            var local = Quaternion.Inverse(Root.rotation) * world;
            local = new Quaternion(local.x, -local.y, -local.z, local.w);
            return Root.rotation * local;
        }

        /// <summary>The pose of the mirror image of a transform: where its counterpart is created.</summary>
        public Frame MirroredFrame(Transform source) =>
            Frame.Of(ReflectPoint(source.position), ReflectRotation(source.rotation), source.lossyScale);

        /// <summary>
        /// Puts <paramref name="target"/> at the mirror image of <paramref name="source"/>. Below a parent that
        /// mirrors the source's parent, that is the source's own local pose.
        /// </summary>
        public void Place(Transform target, Transform source)
        {
            var parent = target.parent != null ? Frame.Of(target.parent) : Frame.World;
            MirrorPose(source, parent, out var position, out var rotation, out var scale);
            target.localPosition = position;
            target.localRotation = rotation;
            target.localScale = scale;
        }

        /// <summary>The local pose that mirrors the source's world position and rotation and keeps its size.</summary>
        public void MirrorPose(Transform source, Frame parent, out Vector3 position, out Quaternion rotation,
            out Vector3 scale)
        {
            position = Tidy(parent.InverseTransformPoint(ReflectPoint(source.position)), source.localPosition,
                PointTolerance(source.position, parent.Scale));
            rotation = Tidy(parent.InverseTransformRotation(ReflectRotation(source.rotation)), source.localRotation);
            // Measure the child's axes after rotation, not the parent's xyz axes. Each column scales independently,
            // so this also preserves lossyScale under a non-uniform parent (shear itself is not a Transform scale).
            var unitScale = parent.Child(Vector3.zero, rotation, Vector3.one).LossyScale;
            var worldScale = source.lossyScale;
            scale = new Vector3(SizeAxis(worldScale.x, unitScale.x, source.localScale.x),
                SizeAxis(worldScale.y, unitScale.y, source.localScale.y),
                SizeAxis(worldScale.z, unitScale.z, source.localScale.z));
        }

        public Vector3 MirrorPoint(Vector3 local, Frame from, Frame to)
        {
            var world = from.TransformPoint(local);
            var mirrored = to.InverseTransformPoint(ReflectPoint(world));
            return Tidy(mirrored, local, PointTolerance(world, to.Scale));
        }

        public Vector3 MirrorDirection(Vector3 local, Frame from, Frame to) =>
            Tidy(to.InverseTransformDirection(ReflectDirection(from.TransformDirection(local))), local, DirectionTolerance);

        public Vector3 MirrorVector(Vector3 local, Frame from, Frame to)
        {
            var mirrored = to.InverseTransformVector(ReflectDirection(from.TransformVector(local)));
            return Tidy(mirrored, local, PointTolerance(from.Position, to.Scale));
        }

        public Vector3 MirrorWorldDirection(Vector3 world) => Tidy(ReflectDirection(world), world, DirectionTolerance);

        /// <summary>
        /// A rotation that is turned with its frame, such as the shape of a collider: the runtime puts it after the
        /// world rotation of the frame.
        /// </summary>
        public Quaternion MirrorRotation(Quaternion local, Frame from, Frame to) =>
            Tidy(Quaternion.Inverse(to.Rotation) * ReflectRotation(from.Rotation * local), local);

        /// <summary>
        /// The local rotation of a Transform below the frames, such as the rest pose of a constraint. Below a parent
        /// with a negative scale Unity mirrors it as well, like the pose, see <see cref="Frame.Child"/>.
        /// </summary>
        public Quaternion MirrorLocalRotation(Quaternion local, Frame fromParent, Frame toParent) =>
            Tidy(toParent.InverseTransformRotation(ReflectRotation(fromParent.TransformRotation(local))), local);

        public Vector3 MirrorEuler(Vector3 euler, Frame from, Frame to) =>
            TidyEuler(MirrorRotation(Quaternion.Euler(euler), from, to), euler);

        /// <summary>See <see cref="MirrorLocalRotation"/>.</summary>
        public Vector3 MirrorLocalEuler(Vector3 euler, Frame fromParent, Frame toParent) =>
            TidyEuler(MirrorLocalRotation(Quaternion.Euler(euler), fromParent, toParent), euler);

        private static Vector3 TidyEuler(Quaternion rotation, Vector3 euler)
        {
            var mirrored = NormalizeEuler(rotation.eulerAngles);
            return new Vector3(TidyAngle(mirrored.x, euler.x), TidyAngle(mirrored.y, euler.y), TidyAngle(mirrored.z, euler.z));
        }

        /// <summary>
        /// The box that encloses the mirror image of <paramref name="local"/>: the eight corners are mirrored like
        /// points, and the result is the axis-aligned box around them. On frames that are mirror images of each
        /// other that is the mirrored center with the same size. Where the axes of the counterpart differ more than
        /// that (another bone roll, swapped axes, another scale), the size follows them. Enclosing the mirror image
        /// of the original bounds keeps this transform from cutting them off; whether they cover the mesh in the
        /// first place is not its business.
        /// </summary>
        public Bounds MirrorBounds(Bounds local, Frame from, Frame to)
        {
            var low = local.min;
            var high = local.max;
            var min = Vector3.positiveInfinity;
            var max = Vector3.negativeInfinity;
            float tolerance = 0f;
            for (int corner = 0; corner < 8; corner++)
            {
                var point = new Vector3((corner & 1) == 0 ? low.x : high.x, (corner & 2) == 0 ? low.y : high.y,
                    (corner & 4) == 0 ? low.z : high.z);
                var mirrored = MirrorPoint(point, from, to);
                min = Vector3.Min(min, mirrored);
                max = Vector3.Max(max, mirrored);
                // The size carries the noise of the corners, the farthest one most
                tolerance = Mathf.Max(tolerance, PointTolerance(from.TransformPoint(point), to.Scale));
            }

            // The mirrored corners lie symmetric around the mirrored center, so the box around them is centered there
            return new Bounds(MirrorPoint(local.center, from, to), Tidy(max - min, local.size, tolerance));
        }

        internal static float AverageScale(Vector3 scale) => (Mathf.Abs(scale.x) + Mathf.Abs(scale.y) + Mathf.Abs(scale.z)) / 3f;

        private static float SizeAxis(float world, float unit, float original) =>
            Mathf.Approximately(unit, 0f)
                ? original
                : Tidy(world / unit, original, Precision * Mathf.Max(1f, Mathf.Abs(original)));

        /// <summary>How far noise can carry a point near <paramref name="world"/>, in units of that scale.</summary>
        private float PointTolerance(Vector3 world, float scale) =>
            Precision * Mathf.Max(1f, world.magnitude, Root.position.magnitude) / Mathf.Max(scale, 1e-6f);

        private static float Tidy(float mirrored, float source, float tolerance)
        {
            if (Mathf.Abs(mirrored - source) <= tolerance) return source;
            if (Mathf.Abs(mirrored + source) <= tolerance) return -source;
            return mirrored;
        }

        private static Vector3 Tidy(Vector3 mirrored, Vector3 source, float tolerance) =>
            new Vector3(Tidy(mirrored.x, source.x, tolerance), Tidy(mirrored.y, source.y, tolerance),
                Tidy(mirrored.z, source.z, tolerance));

        /// <summary>Per component, so that each keeps or flips its sign; q and -q are the same rotation.</summary>
        private static Quaternion Tidy(Quaternion mirrored, Quaternion source) =>
            new Quaternion(Tidy(mirrored.x, source.x, DirectionTolerance), Tidy(mirrored.y, source.y, DirectionTolerance),
                Tidy(mirrored.z, source.z, DirectionTolerance), Tidy(mirrored.w, source.w, DirectionTolerance));

        private static float TidyAngle(float mirrored, float source)
        {
            if (Mathf.Abs(Mathf.DeltaAngle(mirrored, source)) <= AngleTolerance) return Wrap(source);
            if (Mathf.Abs(Mathf.DeltaAngle(mirrored, -source)) <= AngleTolerance) return Wrap(-source);
            return mirrored;
        }

        /// <summary>Brings angles into (-180, 180], the range the Inspector usually shows.</summary>
        internal static Vector3 NormalizeEuler(Vector3 euler) => new Vector3(Wrap(euler.x), Wrap(euler.y), Wrap(euler.z));

        private static float Wrap(float angle)
        {
            angle = Mathf.Repeat(angle, 360f);
            if (angle > 180f) angle -= 360f;
            // Values like 359.99997 wrap to a tiny negative number; the Inspector would show -0.00003
            return Mathf.Abs(angle) < 1e-4f ? 0f : angle;
        }
    }
}
