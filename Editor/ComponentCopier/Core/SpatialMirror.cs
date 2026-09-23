using System;
using UnityEngine;

namespace Kanameliser.EditorPlus.ComponentCopier
{
    /// <summary>
    /// A local coordinate frame: that of an existing transform, or the pose an object will have once it is
    /// created. Spatial values are carried from one frame to another through world space.
    /// </summary>
    internal readonly struct Frame
    {
        private readonly Matrix4x4 localToWorld;
        private readonly Matrix4x4 worldToLocal;
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;

        private Frame(Matrix4x4 localToWorld, Matrix4x4 worldToLocal, Vector3 position, Quaternion rotation)
        {
            this.localToWorld = localToWorld;
            this.worldToLocal = worldToLocal;
            Position = position;
            Rotation = rotation;
        }

        public static Frame Of(Transform transform) =>
            new Frame(transform.localToWorldMatrix, transform.worldToLocalMatrix, transform.position, transform.rotation);

        public static Frame Of(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            var matrix = Matrix4x4.TRS(position, rotation, scale);
            return new Frame(matrix, matrix.inverse, position, rotation);
        }

        /// <summary>The same frame at unit scale, for values their frame moves and turns but does not scale.</summary>
        public Frame WithoutScale() => Of(Position, Rotation, Vector3.one);

        /// <summary>The average size of one local unit in world space.</summary>
        public float Scale => MirrorContext.AverageScale(localToWorld.lossyScale);

        public Vector3 TransformPoint(Vector3 point) => localToWorld.MultiplyPoint3x4(point);
        public Vector3 InverseTransformPoint(Vector3 point) => worldToLocal.MultiplyPoint3x4(point);

        // Displacements scale like points but ignore the origin, like Transform.TransformVector
        public Vector3 TransformVector(Vector3 vector) => localToWorld.MultiplyVector(vector);
        public Vector3 InverseTransformVector(Vector3 vector) => worldToLocal.MultiplyVector(vector);

        // Directions ignore scale, like Transform.TransformDirection
        public Vector3 TransformDirection(Vector3 direction) => Rotation * direction;
        public Vector3 InverseTransformDirection(Vector3 direction) => Quaternion.Inverse(Rotation) * direction;
    }

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
            target.position = ReflectPoint(source.position);
            target.rotation = ReflectRotation(source.rotation);

            float scale = target.parent != null ? AverageScale(target.parent.lossyScale) : 1f;
            target.localPosition = Tidy(target.localPosition, source.localPosition, PointTolerance(source.position, scale));
            target.localRotation = Tidy(target.localRotation, source.localRotation);
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

        public Quaternion MirrorRotation(Quaternion local, Frame from, Frame to) =>
            Tidy(Quaternion.Inverse(to.Rotation) * ReflectRotation(from.Rotation * local), local);

        public Vector3 MirrorEuler(Vector3 euler, Frame from, Frame to)
        {
            var mirrored = NormalizeEuler(MirrorRotation(Quaternion.Euler(euler), from, to).eulerAngles);
            return new Vector3(TidyAngle(mirrored.x, euler.x), TidyAngle(mirrored.y, euler.y), TidyAngle(mirrored.z, euler.z));
        }

        internal static float AverageScale(Vector3 scale) => (Mathf.Abs(scale.x) + Mathf.Abs(scale.y) + Mathf.Abs(scale.z)) / 3f;

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
