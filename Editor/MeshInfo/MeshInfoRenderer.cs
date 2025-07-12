using UnityEngine;
using UnityEditor;

namespace Kanameliser.EditorPlus
{
    internal class MeshInfoRenderer
    {
        private Texture2D backgroundTexture;
        private GUIStyle titleStyle;
        private GUIStyle totalStyle;
        private GUIStyle infoStyle;
        private GUIStyle diffStyle;
        private bool stylesInitialized = false;

        private void EnsureStylesInitialized()
        {
            // Reset styles if background texture was destroyed (e.g., domain reload)
            if (backgroundTexture == null)
                stylesInitialized = false;

            if (stylesInitialized)
                return;

            backgroundTexture = CreateTexture(MeshInfoConstants.WindowWidth, MeshInfoConstants.WindowHeight, MeshInfoConstants.BackgroundColor);

            titleStyle = new GUIStyle
            {
                fontSize = MeshInfoConstants.TitleFontSize,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };

            totalStyle = new GUIStyle(titleStyle)
            {
                fontSize = MeshInfoConstants.SubtitleFontSize,
                fontStyle = FontStyle.Normal
            };

            infoStyle = new GUIStyle
            {
                fontSize = MeshInfoConstants.InfoFontSize,
                normal = { textColor = Color.white }
            };

            diffStyle = new GUIStyle(infoStyle)
            {
                fontSize = MeshInfoConstants.DiffFontSize
            };

            stylesInitialized = true;
        }

        private Texture2D CreateTexture(int width, int height, Color color)
        {
            // Create a solid color texture for UI background with specified dimensions
            var texture = new Texture2D(width, height, TextureFormat.ARGB32, false);
            var pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = color;
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        public void Dispose()
        {
            if (backgroundTexture != null)
            {
                Object.DestroyImmediate(backgroundTexture);
            }
        }

        public void DrawMeshInfo(MeshInfoData currentData, MeshInfoData originalData = null,
            bool isShowingProxyInfo = false, bool hasProxyInSelection = false)
        {
            Handles.BeginGUI();
            EnsureStylesInitialized();

            var windowRect = new Rect(MeshInfoConstants.WindowPositionX, MeshInfoConstants.WindowPositionY,
                MeshInfoConstants.WindowWidth, MeshInfoConstants.WindowHeight);

            // Draw background
            GUI.DrawTexture(windowRect, backgroundTexture);

            GUILayout.BeginArea(windowRect);

            // Add padding
            GUILayout.BeginVertical();
            GUILayout.Space(MeshInfoConstants.Padding);
            GUILayout.BeginHorizontal();
            GUILayout.Space(MeshInfoConstants.Padding);
            GUILayout.BeginVertical();

            DrawTitle(currentData.HasChildObjects);
            GUILayout.Space(MeshInfoConstants.TitleSpacing);

#if NDMF_INSTALLED
            if (originalData != null)
            {
                DrawDynamicLabel("Triangles", originalData.Triangles, currentData.Triangles, isShowingProxyInfo && hasProxyInSelection);
                DrawDynamicLabel("Materials", originalData.Materials, currentData.Materials, isShowingProxyInfo && hasProxyInSelection);
                DrawDynamicLabel("Material Slots", originalData.MaterialSlots, currentData.MaterialSlots, isShowingProxyInfo && hasProxyInSelection);
                DrawDynamicLabel("Meshes", originalData.Meshes, currentData.Meshes, isShowingProxyInfo && hasProxyInSelection);
            }
            else
#endif
            {
                DrawStaticLabels(currentData);
            }

#if NDMF_INSTALLED
            DrawNDMFIndicators(isShowingProxyInfo, hasProxyInSelection);
#endif

            GUILayout.EndVertical();
            GUILayout.Space(MeshInfoConstants.Padding);
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.EndArea();
            Handles.EndGUI();
        }

        private void DrawTitle(bool hasChildObjects)
        {
            var titleRect = new Rect(MeshInfoConstants.Padding, 5, 200, 20);
            GUI.Label(titleRect, "Mesh Info", titleStyle);

            if (hasChildObjects)
            {
                var totalRect = new Rect(titleRect.x + 75, titleRect.y + 3, 50, 20);
                GUI.Label(totalRect, "(Total)", totalStyle);
            }
        }

        private void DrawStaticLabels(MeshInfoData data)
        {
            GUILayout.Label($"Triangles: {data.Triangles}", infoStyle);
            GUILayout.Label($"Materials: {data.Materials}", infoStyle);
            GUILayout.Label($"Material Slots: {data.MaterialSlots}", infoStyle);
            GUILayout.Label($"Meshes: {data.Meshes}", infoStyle);
        }

#if NDMF_INSTALLED
        private void DrawDynamicLabel(string label, int original, int current, bool showDiff)
        {
            // Show difference visualization when proxy data differs from original
            if (showDiff && original != current)
            {
                DrawLabelWithDiff(label, original, current);
            }
            else
            {
                GUILayout.Label($"{label}: {current}", infoStyle);
            }
        }

        private void DrawLabelWithDiff(string label, int original, int current)
        {
            int diff = current - original;
            string mainText = $"{label}: {current} ";
            string diffText = diff > 0 ? $"[+{diff}]" : $"[{diff}]";

            string fullText = mainText + diffText;
            var lineRect = GUILayoutUtility.GetRect(new GUIContent(fullText), infoStyle);

            GUI.Label(new Rect(lineRect.x, lineRect.y, lineRect.width, lineRect.height), mainText, infoStyle);

            var mainSize = infoStyle.CalcSize(new GUIContent(mainText));
            var diffSize = diffStyle.CalcSize(new GUIContent(diffText));
            float diffY = lineRect.y + (lineRect.height - diffSize.y);
            var diffRect = new Rect(lineRect.x + mainSize.x, diffY, diffSize.x, diffSize.y);

            var bgRect = new Rect(diffRect.x - 2, diffRect.y, diffRect.width + 4, diffRect.height);

            Color originalColor = GUI.color;
            // Use green background for decreases (optimizations) and red for increases
            if (diff < 0)
            {
                GUI.color = MeshInfoConstants.DiffDecreaseBackgroundColor;
                diffStyle.normal.textColor = Color.white;
            }
            else
            {
                GUI.color = MeshInfoConstants.DiffIncreaseBackgroundColor;
                diffStyle.normal.textColor = Color.white;
            }

            GUI.DrawTexture(bgRect, Texture2D.whiteTexture);
            GUI.color = originalColor;

            GUI.Label(diffRect, diffText, diffStyle);
        }

        private void DrawNDMFIndicators(bool isShowingProxyInfo, bool hasProxyInSelection)
        {
            if (isShowingProxyInfo)
            {
                var dotRect = new Rect(MeshInfoConstants.DotLeftMargin,
                    MeshInfoConstants.DotTopMargin, MeshInfoConstants.DotSize, MeshInfoConstants.DotSize);

                Color originalColor = GUI.color;
                GUI.color = MeshInfoConstants.PreviewDotColor;
                GUI.DrawTexture(dotRect, Texture2D.whiteTexture);
                GUI.color = originalColor;
            }
        }
#endif

    }
}