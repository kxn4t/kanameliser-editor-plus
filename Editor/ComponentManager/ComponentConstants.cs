using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Kanameliser.EditorPlus
{
    /// <summary>
    /// Defines shared constants
    /// </summary>
    public static class ComponentConstants
    {
        public const float MIN_COLUMN_WIDTH = 150f;
        public const float CHECKBOX_WIDTH = 20f;
        public const float RESIZE_HANDLE_WIDTH = 8f;
        public const float COLUMN_MARGIN = 20f;
        public const float MAX_COLUMN_RATIO = 0.6f;
        public const long FILTER_DEBOUNCE_MS = 200;
    }

}
