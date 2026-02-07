using UnityEngine;
using UnityEditor;
using EconSim.Core;
using MapGen.Core;

namespace EconSim.Editor
{
    /// <summary>
    /// Scene-view overlay that shows MapGen cell data on hover.
    /// Displays biome, soil, vegetation, resources, suitability, and political info
    /// in a status bar at the bottom of the Scene view.
    /// Active whenever a map is loaded (GameManager.IsMapReady).
    /// </summary>
    [InitializeOnLoad]
    public static class CellInspectorOverlay
    {
        private static int _hoveredCell = -1;
        private static string _hoverText = "";

        static CellInspectorOverlay()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            if (!GameManager.IsMapReady) return;
            var gm = GameManager.Instance;
            if (gm == null) return;

            var result = gm.MapGenResult;
            if (result == null) return;

            // Raycast mouse to world XZ plane (Y=0)
            // Main project: data Y -> world Z, data X -> world X
            Vector2 mousePos = Event.current.mousePosition;
            Ray ray = HandleUtility.GUIPointToWorldRay(mousePos);

            // Intersect with Y=0 plane
            if (Mathf.Abs(ray.direction.y) < 1e-6f) return;
            float t = -ray.origin.y / ray.direction.y;
            if (t < 0) return;

            // World X = data X, World Z = data Y
            float dataX = ray.origin.x + t * ray.direction.x;
            float dataY = ray.origin.z + t * ray.direction.z;

            // Account for cellScale if MapView applies one
            // MapView multiplies by cellScale, so divide back
            var mapView = gm.GetComponentInChildren<EconSim.Renderer.MapView>();
            if (mapView != null)
            {
                float scale = mapView.CellScale;
                if (scale > 0)
                {
                    dataX /= scale;
                    dataY /= scale;
                }
            }

            var mesh = result.Mesh;
            if (mesh == null) return;

            // Find nearest cell
            int nearest = -1;
            float nearestDist = float.MaxValue;
            for (int i = 0; i < mesh.CellCount; i++)
            {
                float dx = mesh.CellCenters[i].X - dataX;
                float dy = mesh.CellCenters[i].Y - dataY;
                float dist = dx * dx + dy * dy;
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = i;
                }
            }

            if (nearest < 0) return;

            // Rebuild text when cell changes
            if (nearest != _hoveredCell)
            {
                _hoveredCell = nearest;
                _hoverText = FormatCellInfo(nearest, result);
            }

            // Draw status bar
            Handles.BeginGUI();
            float barHeight = 20;
            var rect = new Rect(0, sceneView.position.height - 48, sceneView.position.width, barHeight);
            EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.85f));
            var style = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = Color.white } };
            GUI.Label(rect, " " + _hoverText, style);
            Handles.EndGUI();

            if (Event.current.type == EventType.MouseMove)
                sceneView.Repaint();
        }

        private static string FormatCellInfo(int cell, MapGenResult result)
        {
            var heights = result.Heights;
            var biomes = result.Biomes;
            var climate = result.Climate;
            var political = result.Political;

            bool isWater = heights.IsWater(cell);
            bool isLake = biomes.IsLakeCell[cell];

            if (isWater && !isLake)
                return $"Cell {cell}  |  Ocean  |  Height: {heights.Heights[cell]:F1}";

            if (isLake)
                return $"Cell {cell}  |  Lake  |  Height: {heights.Heights[cell]:F1}";

            var sb = new System.Text.StringBuilder(256);
            sb.Append($"Cell {cell}  |  {biomes.Biome[cell]}  |  {biomes.Soil[cell]}  |  {biomes.Vegetation[cell]} ({biomes.VegetationDensity[cell]:P0})");
            sb.Append($"  |  Fert: {biomes.Fertility[cell]:F2}  |  Hab: {biomes.Habitability[cell]:F0}  |  Sub: {biomes.Subsistence[cell]:F2}");
            sb.Append($"  |  H: {heights.Heights[cell]:F1}  |  T: {climate.Temperature[cell]:F1}  |  P: {climate.Precipitation[cell]:F1}");

            if (biomes.IronAbundance[cell] > 0.01f) sb.Append($"  |  Iron: {biomes.IronAbundance[cell]:F2}");
            if (biomes.GoldAbundance[cell] > 0.01f) sb.Append($"  |  Gold: {biomes.GoldAbundance[cell]:F2}");
            if (biomes.LeadAbundance[cell] > 0.01f) sb.Append($"  |  Lead: {biomes.LeadAbundance[cell]:F2}");
            if (biomes.SaltAbundance[cell] > 0.01f) sb.Append($"  |  Salt: {biomes.SaltAbundance[cell]:F2}");
            if (biomes.StoneAbundance[cell] > 0.01f) sb.Append($"  |  Stone: {biomes.StoneAbundance[cell]:F2}");

            sb.Append($"  |  Suit: {biomes.Suitability[cell]:F1} (geo: {biomes.SuitabilityGeo[cell]:F1})");

            if (political.RealmId[cell] > 0)
                sb.Append($"  |  R{political.RealmId[cell]} P{political.ProvinceId[cell]} C{political.CountyId[cell]}");

            return sb.ToString();
        }
    }
}
