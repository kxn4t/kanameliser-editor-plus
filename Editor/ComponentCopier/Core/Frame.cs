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
}
