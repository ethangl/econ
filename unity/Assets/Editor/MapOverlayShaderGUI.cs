using UnityEditor;
using UnityEngine;

namespace EconSim.Editor
{
    public class MapOverlayShaderGUI : ShaderGUI
    {
        private enum InspectorStyle
        {
            Unknown = 0,
            Flat = 1,
            Biome = 2
        }

        // Foldout states (persisted per-editor-session via EditorPrefs)
        private static bool realmRenderingFoldout
        {
            get => EditorPrefs.GetBool("MapOverlay_RealmRendering", true);
            set => EditorPrefs.SetBool("MapOverlay_RealmRendering", value);
        }

        private static bool waterRenderingFoldout
        {
            get => EditorPrefs.GetBool("MapOverlay_WaterRendering", true);
            set => EditorPrefs.SetBool("MapOverlay_WaterRendering", value);
        }

        private static bool reliefRenderingFoldout
        {
            get => EditorPrefs.GetBool("MapOverlay_ReliefRendering", true);
            set => EditorPrefs.SetBool("MapOverlay_ReliefRendering", value);
        }

        private static bool marketRenderingFoldout
        {
            get => EditorPrefs.GetBool("MapOverlay_MarketRendering", true);
            set => EditorPrefs.SetBool("MapOverlay_MarketRendering", value);
        }

        private static bool soilRenderingFoldout
        {
            get => EditorPrefs.GetBool("MapOverlay_SoilRendering", true);
            set => EditorPrefs.SetBool("MapOverlay_SoilRendering", value);
        }

        private static bool vegetationRenderingFoldout
        {
            get => EditorPrefs.GetBool("MapOverlay_VegetationRendering", true);
            set => EditorPrefs.SetBool("MapOverlay_VegetationRendering", value);
        }

        private static bool textureMapsFoldout
        {
            get => EditorPrefs.GetBool("MapOverlay_TextureMaps", false);
            set => EditorPrefs.SetBool("MapOverlay_TextureMaps", value);
        }

        private static bool debugFoldout
        {
            get => EditorPrefs.GetBool("MapOverlay_Debug", true);
            set => EditorPrefs.SetBool("MapOverlay_Debug", value);
        }

        private static bool uiFoldout
        {
            get => EditorPrefs.GetBool("MapOverlay_UI", false);
            set => EditorPrefs.SetBool("MapOverlay_UI", value);
        }

        // Grouped properties: (shader name, display label)
        private static readonly (string name, string label)[] RealmRenderingProps = new[]
        {
            ("_RealmBorderWidth", "Realm Border Width"),
            ("_RealmBorderDarkening", "Realm Border Darkening"),
            ("_ProvinceBorderWidth", "Province Border Width"),
            ("_ProvinceBorderDarkening", "Province Border Darkening"),
            ("_CountyBorderWidth", "County Border Width"),
            ("_CountyBorderDarkening", "County Border Darkening"),
            ("_GradientRadius", "Gradient Radius (pixels)"),
            ("_GradientEdgeDarkening", "Gradient Edge Darkening"),
            ("_OverlayOpacity", "Overlay Opacity"),
        };

        private static readonly (string name, string label)[] MarketRenderingProps = new[]
        {
            ("_MarketBorderWidth", "Market Border Width"),
            ("_MarketBorderDarkening", "Market Border Darkening"),
            ("_PathOpacity", "Path Opacity"),
            ("_PathDashLength", "Path Dash Length"),
            ("_PathGapLength", "Path Gap Length"),
            ("_PathWidth", "Path Width"),
        };

        private static readonly (string name, string label)[] SoilRenderingProps = new[]
        {
            ("_SoilHeightFloor", "Soil Height Floor"),
            ("_SoilBlendRadius", "Soil Blend Radius (texels)"),
            ("_SoilBlendSharpness", "Soil Blend Sharpness"),
            ("_SoilColor0", "Permafrost"),
            ("_SoilColor1", "Saline"),
            ("_SoilColor2", "Lithosol"),
            ("_SoilColor3", "Alluvial"),
            ("_SoilColor4", "Aridisol"),
            ("_SoilColor5", "Laterite"),
            ("_SoilColor6", "Podzol"),
            ("_SoilColor7", "Chernozem"),
        };

        private static readonly (string name, string label)[] VegetationRenderingProps = new[]
        {
            ("_VegetationStippleOpacity", "Stipple Opacity"),
            ("_VegetationStippleScale", "Stipple Scale (texels)"),
            ("_VegetationStippleJitter", "Stipple Jitter"),
            ("_VegetationCoverageContrast", "Coverage Contrast"),
            ("_VegetationStippleSoftness", "Stipple Softness"),
            ("_VegetationColor0", "None"),
            ("_VegetationColor1", "Lichen / Moss"),
            ("_VegetationColor2", "Grass"),
            ("_VegetationColor3", "Shrub"),
            ("_VegetationColor4", "Deciduous"),
            ("_VegetationColor5", "Coniferous"),
            ("_VegetationColor6", "Broadleaf"),
        };

        private static readonly (string name, string label)[] WaterRenderingFlatProps = new[]
        {
            ("_WaterShallowColor", "Shallow Water Color"),
            ("_WaterShallowAlpha", "River Alpha"),
        };

        private static readonly (string name, string label)[] WaterRenderingBiomeProps = new[]
        {
            ("_WaterShallowColor", "Shallow Water Color"),
            ("_WaterShallowAlpha", "River Alpha"),
            ("_WaterDeepColor", "Deep Water Color"),
            ("_WaterAbsorption", "Ocean Absorption (RGB)"),
            ("_WaterOpticalDepth", "Ocean Optical Depth"),
            ("_WaterDepthExponent", "Ocean Depth Exponent"),
            ("_WaterRefractionStrength", "Seabed Refraction"),
            ("_WaterRefractionScale", "Refraction Scale"),
            ("_WaterRefractionSpeed", "Refraction Speed"),
            ("_ShimmerScale", "Shimmer Scale"),
            ("_ShimmerSpeed", "Shimmer Speed"),
            ("_ShimmerIntensity", "Shimmer Intensity"),
        };

        private static readonly (string name, string label)[] ReliefRenderingProps = new[]
        {
            ("_ReliefNormalStrength", "Normal Strength"),
            ("_ReliefShadeStrength", "Shade Strength"),
            ("_ReliefAmbient", "Ambient"),
            ("_ReliefLightDir", "Light Direction"),
        };

        private static readonly (string name, string label)[] TextureMapsProps = new[]
        {
            ("_PoliticalIdsTex", "Political IDs"),
            ("_GeographyBaseTex", "Geography Base"),
            ("_VegetationTex", "Vegetation Data"),
            ("_HeightmapTex", "Heightmap"),
            ("_ReliefNormalTex", "Relief Normal"),
            ("_RiverMaskTex", "River Mask"),
            ("_ModeColorResolve", "Mode Color Resolve"),
            ("_OverlayTex", "Overlay Layer"),
            ("_CellToMarketTex", "Cell To Market"),
            ("_RealmPaletteTex", "Realm Palette"),
            ("_MarketPaletteTex", "Market Palette"),
            ("_BiomePaletteTex", "Biome Palette"),
            ("_RealmBorderDistTex", "Realm Border Distance"),
            ("_ProvinceBorderDistTex", "Province Border Distance"),
            ("_CountyBorderDistTex", "County Border Distance"),
            ("_MarketBorderDistTex", "Market Border Distance"),
            ("_RoadMaskTex", "Road Mask"),
        };

        private static readonly (string name, string label)[] DebugProps = new[]
        {
            ("_DebugView", "Channel Inspector View"),
        };

        private static readonly (string name, string label)[] UIProps = new[]
        {
            ("_SelectionDimming", "Selection Dimming"),
            ("_SelectionDesaturation", "Selection Desaturation"),
        };

        // Properties set programmatically — hidden from Inspector
        private static readonly string[] HiddenProps = new[]
        {
            "_SelectedRealmId",
            "_SelectedProvinceId",
            "_SelectedCountyId",
            "_SelectedMarketId",
            "_HoveredRealmId",
            "_HoveredProvinceId",
            "_HoveredCountyId",
            "_HoveredMarketId",
            "_HoverIntensity",
            "_MapMode",
            "_UseModeColorResolve",
            "_OverlayEnabled",
            "_UseHeightDisplacement",
            "_HeightScale",
            "_SeaLevel",
        };

        private bool IsGrouped(string propName)
        {
            foreach (var (name, _) in RealmRenderingProps)
                if (name == propName) return true;
            foreach (var (name, _) in WaterRenderingFlatProps)
                if (name == propName) return true;
            foreach (var (name, _) in WaterRenderingBiomeProps)
                if (name == propName) return true;
            foreach (var (name, _) in ReliefRenderingProps)
                if (name == propName) return true;
            foreach (var (name, _) in MarketRenderingProps)
                if (name == propName) return true;
            foreach (var (name, _) in SoilRenderingProps)
                if (name == propName) return true;
            foreach (var (name, _) in VegetationRenderingProps)
                if (name == propName) return true;
            foreach (var (name, _) in TextureMapsProps)
                if (name == propName) return true;
            foreach (var (name, _) in DebugProps)
                if (name == propName) return true;
            foreach (var (name, _) in UIProps)
                if (name == propName) return true;
            return false;
        }

        private static InspectorStyle ResolveInspectorStyle(MaterialEditor materialEditor)
        {
            if (materialEditor == null || materialEditor.targets == null || materialEditor.targets.Length == 0)
                return InspectorStyle.Unknown;

            InspectorStyle resolved = InspectorStyle.Unknown;
            foreach (Object target in materialEditor.targets)
            {
                if (!(target is Material material) || material.shader == null)
                    return InspectorStyle.Unknown;

                string shaderName = material.shader.name;
                InspectorStyle current =
                    shaderName == "EconSim/MapOverlayFlat" ? InspectorStyle.Flat :
                    shaderName == "EconSim/MapOverlayBiome" ? InspectorStyle.Biome :
                    InspectorStyle.Unknown;

                if (current == InspectorStyle.Unknown)
                    return InspectorStyle.Unknown;

                if (resolved == InspectorStyle.Unknown)
                {
                    resolved = current;
                }
                else if (resolved != current)
                {
                    return InspectorStyle.Unknown;
                }
            }

            return resolved;
        }

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            InspectorStyle style = ResolveInspectorStyle(materialEditor);
            bool isFlat = style == InspectorStyle.Flat;
            bool isBiome = style == InspectorStyle.Biome;
            bool showFlatGroups = !isBiome;
            bool showBiomeGroups = !isFlat;

            // Draw ungrouped, non-hidden properties
            foreach (var prop in properties)
            {
                if (IsGrouped(prop.name)) continue;
                if (System.Array.IndexOf(HiddenProps, prop.name) >= 0) continue;

                materialEditor.ShaderProperty(prop, prop.displayName);
            }

            if (showFlatGroups)
            {
                // Realm Rendering group
                EditorGUILayout.Space();
                realmRenderingFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(realmRenderingFoldout, "Realm Rendering");
                if (realmRenderingFoldout)
                {
                    EditorGUI.indentLevel++;
                    foreach (var (name, label) in RealmRenderingProps)
                    {
                        var prop = FindProperty(name, properties, false);
                        if (prop != null)
                            materialEditor.ShaderProperty(prop, label);
                    }
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndFoldoutHeaderGroup();
            }

            if (showFlatGroups)
            {
                // Market Rendering group
                EditorGUILayout.Space();
                marketRenderingFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(marketRenderingFoldout, "Market Rendering");
                if (marketRenderingFoldout)
                {
                    EditorGUI.indentLevel++;
                    foreach (var (name, label) in MarketRenderingProps)
                    {
                        var prop = FindProperty(name, properties, false);
                        if (prop != null)
                            materialEditor.ShaderProperty(prop, label);
                    }
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndFoldoutHeaderGroup();
            }

            if (showBiomeGroups)
            {
                // Soil Rendering group
                EditorGUILayout.Space();
                soilRenderingFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(soilRenderingFoldout, "Soil Rendering");
                if (soilRenderingFoldout)
                {
                    EditorGUI.indentLevel++;
                    foreach (var (name, label) in SoilRenderingProps)
                    {
                        var prop = FindProperty(name, properties, false);
                        if (prop != null)
                            materialEditor.ShaderProperty(prop, label);
                    }
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndFoldoutHeaderGroup();
            }

            if (showBiomeGroups)
            {
                // Vegetation Rendering group
                EditorGUILayout.Space();
                vegetationRenderingFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(vegetationRenderingFoldout, "Vegetation Rendering");
                if (vegetationRenderingFoldout)
                {
                    EditorGUI.indentLevel++;
                    foreach (var (name, label) in VegetationRenderingProps)
                    {
                        var prop = FindProperty(name, properties, false);
                        if (prop != null)
                            materialEditor.ShaderProperty(prop, label);
                    }
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndFoldoutHeaderGroup();
            }

            // Water Rendering group
            EditorGUILayout.Space();
            waterRenderingFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(waterRenderingFoldout, "Water Rendering");
            if (waterRenderingFoldout)
            {
                EditorGUI.indentLevel++;
                var waterProps = isFlat ? WaterRenderingFlatProps : WaterRenderingBiomeProps;
                foreach (var (name, label) in waterProps)
                {
                    var prop = FindProperty(name, properties, false);
                    if (prop != null)
                        materialEditor.ShaderProperty(prop, label);
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            if (showBiomeGroups)
            {
                // Relief Rendering group
                EditorGUILayout.Space();
                reliefRenderingFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(reliefRenderingFoldout, "Relief Rendering");
                if (reliefRenderingFoldout)
                {
                    EditorGUI.indentLevel++;
                    foreach (var (name, label) in ReliefRenderingProps)
                    {
                        var prop = FindProperty(name, properties, false);
                        if (prop != null)
                            materialEditor.ShaderProperty(prop, label);
                    }
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndFoldoutHeaderGroup();
            }

            // UI group
            EditorGUILayout.Space();
            uiFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(uiFoldout, "UI");
            if (uiFoldout)
            {
                EditorGUI.indentLevel++;
                foreach (var (name, label) in UIProps)
                {
                    var prop = FindProperty(name, properties, false);
                    if (prop != null)
                        materialEditor.ShaderProperty(prop, label);
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // Debug group
            EditorGUILayout.Space();
            debugFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(debugFoldout, "Debug");
            if (debugFoldout)
            {
                EditorGUI.indentLevel++;
                foreach (var (name, label) in DebugProps)
                {
                    var prop = FindProperty(name, properties, false);
                    if (prop != null)
                        materialEditor.ShaderProperty(prop, label);
                }

                EditorGUILayout.HelpBox("Runtime keys: 0=Channel Inspector mode, O=cycle channels, P=toggle ID probe.", MessageType.Info);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // Texture Maps group (last, collapsed by default)
            EditorGUILayout.Space();
            textureMapsFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(textureMapsFoldout, "Texture Maps");
            if (textureMapsFoldout)
            {
                EditorGUI.indentLevel++;
                foreach (var (name, label) in TextureMapsProps)
                {
                    var prop = FindProperty(name, properties, false);
                    if (prop != null)
                        materialEditor.ShaderProperty(prop, label);
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }
    }
}
