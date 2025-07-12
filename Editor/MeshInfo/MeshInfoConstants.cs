using UnityEngine;

namespace Kanameliser.EditorPlus
{
    internal static class MeshInfoConstants
    {
        public const string MenuPath = "Tools/Kanameliser Editor Plus/Show Mesh Info Display";
        public const string PreferenceKey = "MeshInfoDisplayVisible";

        public const int WindowWidth = 170;
        public const int WindowHeight = 95;
        public const int WindowPositionX = 50;
        public const int WindowPositionY = 10;

        public const int Padding = 10;
        public const int TitleFontSize = 14;
        public const int InfoFontSize = 12;
        public const int SubtitleFontSize = 10;
        public const int DiffFontSize = 10;
        public const int WarningFontSize = 8;

        public const float DotSize = 6f;
        public const int DotLeftMargin = 2;
        public const int DotTopMargin = 2;

        public const int TitleSpacing = 20;
        public const int WarningSpacing = 5;

        public const double UpdateIntervalSeconds = 0.2;

        public static readonly Color BackgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.3f);
        public static readonly Color DiffIncreaseBackgroundColor = new Color(1f, 0.3f, 0.3f, 0.6f);
        public static readonly Color DiffDecreaseBackgroundColor = new Color(0.2f, 0.8f, 0.2f, 0.6f);
        public static readonly Color PreviewDotColor = new Color(0.3f, 1f, 0.3f, 1f);
        public static readonly Color WarningTextColor = new Color(1f, 1f, 0.5f);
    }
}