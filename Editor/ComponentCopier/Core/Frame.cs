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
        private readonly Vector3 scaleSigns;
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;

        private Frame(Matrix4x4 localToWorld, Matrix4x4 worldToLocal, Vector3 position, Quaternion rotation,
            Vector3 scaleSigns)
        {
            this.localToWorld = localToWorld;
            this.worldToLocal = worldToLocal;
            Position = position;
            Rotation = rotation;
            this.scaleSigns = scaleSigns;
        }

        public static Frame Of(Transform transform)
        {
            var signs = Vector3.one;
            for (var ancestor = transform; ancestor != null; ancestor = ancestor.parent)
                signs = Vector3.Scale(signs, Signs(ancestor.localScale));
            return new Frame(transform.localToWorldMatrix, transform.worldToLocalMatrix, transform.position,
                transform.rotation, signs);
        }

        public static Frame Of(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            var matrix = Matrix4x4.TRS(position, rotation, scale);
            return new Frame(matrix, matrix.inverse, position, rotation, Signs(scale));
        }

        /// <summary>World space itself: the frame above an object at the top of a scene.</summary>
        public static Frame World => Of(Vector3.zero, Quaternion.identity, Vector3.one);

        /// <summary>
        /// The frame of a child with the given local pose, including the way a negative parent scale reflects its
        /// rotation. Scale signs are carried separately: shear can make the signs of lossyScale ambiguous.
        /// </summary>
        public Frame Child(Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
        {
            var matrix = localToWorld * Matrix4x4.TRS(localPosition, localRotation, localScale);
            return new Frame(matrix, matrix.inverse, matrix.MultiplyPoint3x4(Vector3.zero),
                TransformRotation(localRotation), Vector3.Scale(scaleSigns, Signs(localScale)));
        }

        /// <summary>The world rotation of a child with the given local Transform rotation below this frame.</summary>
        public Quaternion TransformRotation(Quaternion local) => Rotation * ReflectLocalRotation(local);

        /// <summary>The local Transform rotation that gives the requested world rotation below this frame.</summary>
        public Quaternion InverseTransformRotation(Quaternion world) =>
            ReflectLocalRotation(Quaternion.Inverse(Rotation) * world);

        private Quaternion ReflectLocalRotation(Quaternion local) =>
            new Quaternion(local.x * scaleSigns.y * scaleSigns.z, local.y * scaleSigns.x * scaleSigns.z,
                local.z * scaleSigns.x * scaleSigns.y, local.w);

        private static Vector3 Signs(Vector3 scale) =>
            new Vector3(scale.x < 0f ? -1f : 1f, scale.y < 0f ? -1f : 1f, scale.z < 0f ? -1f : 1f);

        /// <summary>The same frame at unit scale, for values their frame moves and turns but does not scale.</summary>
        public Frame WithoutScale() => Of(Position, Rotation, Vector3.one);

        /// <summary>The average size of one local unit in world space.</summary>
        public float Scale => MirrorContext.AverageScale(localToWorld.lossyScale);

        /// <summary>
        /// The size of one local unit in world space along each axis, worked out as Transform.lossyScale is: the
        /// diagonal of what is left once the rotation is taken out. Matrix4x4.lossyScale puts the sign of a mirrored
        /// axis on x, wherever the mirroring is.
        /// </summary>
        public Vector3 LossyScale
        {
            get
            {
                var scale = Matrix4x4.Rotate(Quaternion.Inverse(Rotation)) * localToWorld;
                return new Vector3(scale.m00, scale.m11, scale.m22);
            }
        }

        public Vector3 TransformPoint(Vector3 point) => localToWorld.MultiplyPoint3x4(point);
        public Vector3 InverseTransformPoint(Vector3 point) => worldToLocal.MultiplyPoint3x4(point);

        // Displacements scale like points but ignore the origin, like Transform.TransformVector
        public Vector3 TransformVector(Vector3 vector) => localToWorld.MultiplyVector(vector);
        public Vector3 InverseTransformVector(Vector3 vector) => worldToLocal.MultiplyVector(vector);

        // Directions ignore scale, like Transform.TransformDirection
        public Vector3 TransformDirection(Vector3 direction) => Rotation * direction;
        public Vector3 InverseTransformDirection(Vector3 direction) => Quaternion.Inverse(Rotation) * direction;
    }
}
