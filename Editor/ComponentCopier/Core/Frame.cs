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

        /// <summary>World space itself: the frame above an object at the top of a scene.</summary>
        public static Frame World => Of(Vector3.zero, Quaternion.identity, Vector3.one);

        /// <summary>
        /// The frame of a child with the given local pose, as Unity composes it, except below a parent with a negative
        /// scale: Unity mirrors the rotation of the child there as well, while this takes the parent's rotation times
        /// the local one, like <see cref="MirrorContext.MirrorRotation"/> does.
        /// </summary>
        public Frame Child(Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
        {
            var matrix = localToWorld * Matrix4x4.TRS(localPosition, localRotation, localScale);
            return new Frame(matrix, matrix.inverse, matrix.MultiplyPoint3x4(Vector3.zero), Rotation * localRotation);
        }

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
