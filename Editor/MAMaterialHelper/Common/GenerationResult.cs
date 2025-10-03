using UnityEngine;

namespace Kanameliser.Editor.MAMaterialHelper.Common
{
    /// <summary>
    /// Result of material operation generation
    /// </summary>
    public struct GenerationResult
    {
        public bool success;
        public string message;
        public GameObject createdObject;
    }
}
