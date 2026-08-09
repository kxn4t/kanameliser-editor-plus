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
            bool isShowingProxyInfo = false, bool hasProxyInSelection = false, bool showParticleInfo = true)
        {
            Handles.BeginGUI();
            EnsureStylesInitialized();

            GetParticleLineVisibility(currentData, originalData, showParticleInfo,
                out bool showParticleSystems, out bool showParticleSlots, out bool showTrailLineSlots);
            int particleLineCount = (showParticleSystems ? 1 : 0) + (showParticleSlots ? 1 : 0) + (showTrailLineSlots ? 1 : 0);

            // Extend the window below the base layout when the particle section is visible
            float windowHeight = MeshInfoConstants.WindowHeight;
            if (particleLineCount > 0)
            {
                windowHeight += MeshInfoConstants.SeparatorSpacing * 2 + MeshInfoConstants.SeparatorHeight
                    + particleLineCount * MeshInfoConstants.InfoLineHeight;
            }

            var windowRect = new Rect(MeshInfoConstants.WindowPositionX, MeshInfoConstants.WindowPositionY,
                MeshInfoConstants.WindowWidth, windowHeight);

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

            if (particleLineCount > 0)
            {
                DrawSeparator();
#if NDMF_INSTALLED
                if (originalData != null)
                {
                    bool showDiff = isShowingProxyInfo && hasProxyInSelection;
                    if (showParticleSystems)
                        DrawDynamicLabel("Particle Systems", originalData.ParticleSystems, currentData.ParticleSystems, showDiff);
                    if (showParticleSlots)
                        DrawDynamicLabel("Particle Slots", originalData.ParticleMaterialSlots, currentData.ParticleMaterialSlots, showDiff);
                    if (showTrailLineSlots)
                        DrawDynamicLabel("Trail/Line Slots", originalData.TrailLineMaterialSlots, currentData.TrailLineMaterialSlots, showDiff);
                }
                else
#endif
                {
                    if (showParticleSystems)
                        GUILayout.Label($"Particle Systems: {currentData.ParticleSystems}", infoStyle);
                    if (showParticleSlots)
                        GUILayout.Label($"Particle Slots: {currentData.ParticleMaterialSlots}", infoStyle);
                    if (showTrailLineSlots)
                        GUILayout.Label($"Trail/Line Slots: {currentData.TrailLineMaterialSlots}", infoStyle);
                }
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

        private static void GetParticleLineVisibility(MeshInfoData current, MeshInfoData original, bool showParticleInfo,
            out bool showParticleSystems, out bool showParticleSlots, out bool showTrailLineSlots)
        {
            showParticleSystems = false;
            showParticleSlots = false;
            showTrailLineSlots = false;

            if (!showParticleInfo)
                return;

            // Compare against both current and original data so removals (e.g. by NDMF plugins) stay visible as diffs
            int particleSystems = Mathf.Max(current.ParticleSystems, original != null ? original.ParticleSystems : 0);
            int trailLineSlots = Mathf.Max(current.TrailLineMaterialSlots, original != null ? original.TrailLineMaterialSlots : 0);

            showParticleSystems = particleSystems > 0;
            showParticleSlots = particleSystems > 0;
            showTrailLineSlots = trailLineSlots > 0;
        }

        private void DrawSeparator()
        {
            GUILayout.Space(MeshInfoConstants.SeparatorSpacing);

            var separatorRect = GUILayoutUtility.GetRect(1f, MeshInfoConstants.SeparatorHeight, GUILayout.ExpandWidth(true));
            Color originalColor = GUI.color;
            GUI.color = MeshInfoConstants.SeparatorColor;
            GUI.DrawTexture(separatorRect, Texture2D.whiteTexture);
            GUI.color = originalColor;

            GUILayout.Space(MeshInfoConstants.SeparatorSpacing);
        }

#if NDMF_INSTALLED
        private static readonly GUIContent scratchContent = new GUIContent();

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

            scratchContent.text = mainText + diffText;
            var lineRect = GUILayoutUtility.GetRect(scratchContent, infoStyle);

            scratchContent.text = mainText;
            GUI.Label(new Rect(lineRect.x, lineRect.y, lineRect.width, lineRect.height), scratchContent, infoStyle);
            var mainSize = infoStyle.CalcSize(scratchContent);

            scratchContent.text = diffText;
            var diffSize = diffStyle.CalcSize(scratchContent);
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

            GUI.Label(diffRect, scratchContent, diffStyle);
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
