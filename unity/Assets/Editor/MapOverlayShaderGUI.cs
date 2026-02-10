using UnityEditor;
using UnityEngine;

namespace EconSim.Editor
{
    public class MapOverlayShaderGUI : ShaderGUI
    {
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

        private static bool marketRenderingFoldout
        {
            get => EditorPrefs.GetBool("MapOverlay_MarketRendering", true);
            set => EditorPrefs.SetBool("MapOverlay_MarketRendering", value);
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
            ("_GradientCenterOpacity", "Gradient Center Opacity"),
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

        private static readonly (string name, string label)[] WaterRenderingProps = new[]
        {
            ("_WaterShallowColor", "Freshwater Color"),
            ("_WaterShallowAlpha", "Freshwater Alpha"),
            ("_WaterDeepColor", "Ocean Color"),
            ("_WaterDeepAlpha", "Ocean Alpha"),
            ("_WaterDepthRange", "Ocean Depth Range"),
            ("_RiverDepth", "River Depth"),
            ("_RiverDarken", "River Darken"),
            ("_ShimmerScale", "Shimmer Scale"),
            ("_ShimmerSpeed", "Shimmer Speed"),
            ("_ShimmerIntensity", "Shimmer Intensity"),
        };

        private static readonly (string name, string label)[] TextureMapsProps = new[]
        {
            ("_PoliticalIdsTex", "Political IDs"),
            ("_GeographyBaseTex", "Geography Base"),
            ("_HeightmapTex", "Heightmap"),
            ("_RiverMaskTex", "River Mask"),
            ("_ModeColorResolve", "Mode Color Resolve"),
            ("_CellDataTex", "Cell Data (Legacy)"),
            ("_CellToMarketTex", "Cell To Market"),
            ("_RealmPaletteTex", "Realm Palette"),
            ("_MarketPaletteTex", "Market Palette"),
            ("_BiomePaletteTex", "Biome Palette"),
            ("_BiomeMatrixTex", "Biome Elevation Matrix"),
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
        };

        private bool IsGrouped(string propName)
        {
            foreach (var (name, _) in RealmRenderingProps)
                if (name == propName) return true;
            foreach (var (name, _) in WaterRenderingProps)
                if (name == propName) return true;
            foreach (var (name, _) in MarketRenderingProps)
                if (name == propName) return true;
            foreach (var (name, _) in TextureMapsProps)
                if (name == propName) return true;
            foreach (var (name, _) in DebugProps)
                if (name == propName) return true;
            return false;
        }

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            // Draw ungrouped, non-hidden properties
            foreach (var prop in properties)
            {
                if (IsGrouped(prop.name)) continue;
                if (System.Array.IndexOf(HiddenProps, prop.name) >= 0) continue;

                materialEditor.ShaderProperty(prop, prop.displayName);
            }

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

            // Water Rendering group
            EditorGUILayout.Space();
            waterRenderingFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(waterRenderingFoldout, "Water Rendering");
            if (waterRenderingFoldout)
            {
                EditorGUI.indentLevel++;
                foreach (var (name, label) in WaterRenderingProps)
                {
                    var prop = FindProperty(name, properties, false);
                    if (prop != null)
                        materialEditor.ShaderProperty(prop, label);
                }
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
