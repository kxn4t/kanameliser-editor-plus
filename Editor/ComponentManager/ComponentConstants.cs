using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Kanameliser.EditorPlus
{
    /// <summary>
    /// 共通の定数を定義するクラス
    /// </summary>
    public static class ComponentConstants
    {
        public const float MIN_COLUMN_WIDTH = 150f;
        public const float MAX_COLUMN_WIDTH = 500f;
        public const float CHECKBOX_WIDTH = 20f;
        public const float RESIZE_HANDLE_WIDTH = 8f;
        public const float ICON_WIDTH = 20f;
        public const float ROW_HEIGHT = 18f;
        public const float MIN_ROW_HEIGHT = 36f;
        public const float COLUMN_MARGIN = 20f;
        public const float MAX_COLUMN_RATIO = 0.6f;
    }

}
