using System.Collections.Generic;
using UnityEngine;
using WorldGen.Core;

namespace EconSim.Renderer
{
    public enum SphereViewMode
    {
        Plates,
        Elevation,
        UltraDense,
        CratonsBasins
    }

    /// <summary>
    /// Generates a Unity mesh from a WorldGenResult Voronoi tessellation.
    /// Tab toggles between plate, elevation, and ultra-dense coloring.
    /// </summary>
    public class SphereView : MonoBehaviour
    {
        private MeshFilter meshFilter;
        private UnityEngine.MeshRenderer meshRenderer;
        private WorldGenResult result;
        private Mesh unityMesh;
        private SphereViewMode viewMode = SphereViewMode.Plates;
        private int siteCoarseCell = -1;
        private static readonly Color32 SiteHighlightColor = new Color32(255, 50, 220, 255); // magenta

        public float Radius => result?.DenseTerrain?.Mesh?.Radius ?? result?.Mesh?.Radius ?? 0f;
        public SphereViewMode ViewMode => viewMode;
        public SiteContext Site => result?.Site;
        public System.Collections.Generic.List<SiteContext> Sites => result?.Sites;

        public void Generate(WorldGenConfig config)
        {
            result = WorldGenPipeline.Generate(config);
            siteCoarseCell = result.Site?.CellIndex ?? -1;
            BuildMesh(result.DenseTerrain.Mesh);
            LogSiteSelection();
        }

        /// <summary>
        /// Switch the highlighted site and rebuild vertex colors.
        /// </summary>
        public void SetActiveSite(SiteContext site)
        {
            if (result == null) return;
            result.Site = site;
            siteCoarseCell = site?.CellIndex ?? -1;
            RebuildColors();
        }

        private void LogSiteSelection()
        {
            var site = result?.Site;
            if (site == null)
            {
                Debug.Log("SphereView: no site selected");
                return;
            }
            Debug.Log($"SphereView: site selected — cell={site.CellIndex}, " +
                      $"lat={site.Latitude:F1}° lng={site.Longitude:F1}°, " +
                      $"coastDist={site.CoastDistanceHops} hops, " +
                      $"type={site.SiteType}, " +
                      $"boundary={site.NearestBoundary} ({site.BoundaryConvergence:F2}) at {site.BoundaryDistanceHops} hops");
        }

        private void Update()
        {
            if (result == null) return;

            if (Input.GetKeyDown(KeyCode.Tab))
            {
                var prev = viewMode;
                viewMode = viewMode switch
                {
                    SphereViewMode.Plates => SphereViewMode.Elevation,
                    SphereViewMode.Elevation => SphereViewMode.CratonsBasins,
                    SphereViewMode.CratonsBasins => SphereViewMode.UltraDense,
                    SphereViewMode.UltraDense => SphereViewMode.Plates,
                    _ => SphereViewMode.Plates,
                };

                bool geometryChanged =
                    (prev == SphereViewMode.UltraDense) != (viewMode == SphereViewMode.UltraDense);

                if (geometryChanged)
                {
                    var mesh = viewMode == SphereViewMode.UltraDense
                        ? result.DenseTerrain.UltraDenseMesh
                        : result.DenseTerrain.Mesh;
                    BuildMesh(mesh);
                }
                else
                {
                    RebuildColors();
                }

                Debug.Log($"SphereView: switched to {viewMode} mode");
            }
        }

        private void BuildMesh(SphereMesh mesh)
        {
            if (mesh == null) return;

            if (meshFilter == null)
                meshFilter = GetComponent<MeshFilter>();
            if (meshRenderer == null)
                meshRenderer = GetComponent<UnityEngine.MeshRenderer>();

            // Create material if needed
            if (meshRenderer.sharedMaterial == null)
            {
                meshRenderer.sharedMaterial = new Material(Shader.Find("EconSim/VertexColorUnlit"));
            }

            // Build vertex/triangle lists (colors applied separately)
            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            int cellCount = mesh.CellCount;

            for (int c = 0; c < cellCount; c++)
            {
                int[] cellVerts = mesh.CellVertices[c];
                if (cellVerts == null || cellVerts.Length < 3)
                    continue;

                // Center vertex
                Vec3 center = mesh.CellCenters[c];
                int centerIdx = vertices.Count;
                vertices.Add(new Vector3(center.X, center.Y, center.Z));

                // Polygon vertices
                int polyStart = vertices.Count;
                for (int i = 0; i < cellVerts.Length; i++)
                {
                    Vec3 v = mesh.Vertices[cellVerts[i]];
                    vertices.Add(new Vector3(v.X, v.Y, v.Z));
                }

                // Fan triangles — CW winding so normals point outward from sphere
                int len = cellVerts.Length;
                for (int i = 0; i < len; i++)
                {
                    int next = (i + 1) % len;
                    triangles.Add(centerIdx);
                    triangles.Add(polyStart + next);
                    triangles.Add(polyStart + i);
                }
            }

            unityMesh = new Mesh();
            unityMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            unityMesh.SetVertices(vertices);
            unityMesh.SetTriangles(triangles, 0);

            // Apply colors for current mode
            unityMesh.SetColors(BuildColors(mesh));
            unityMesh.RecalculateNormals();
            unityMesh.RecalculateBounds();

            meshFilter.mesh = unityMesh;
            Debug.Log($"SphereView: built mesh with {cellCount} cells, {vertices.Count} vertices, {triangles.Count / 3} triangles ({viewMode})");
        }

        private void RebuildColors()
        {
            if (unityMesh == null || result == null) return;
            var mesh = viewMode == SphereViewMode.UltraDense
                ? result.DenseTerrain.UltraDenseMesh
                : result.DenseTerrain.Mesh;
            unityMesh.SetColors(BuildColors(mesh));
        }

        private List<Color32> BuildColors(SphereMesh mesh)
        {
            var dense = result.DenseTerrain;
            var colors = new List<Color32>();
            int cellCount = mesh.CellCount;

            bool isUltra = viewMode == SphereViewMode.UltraDense;
            int[] toCoarse = isUltra ? dense.UltraDenseToCoarse : dense.DenseToCoarse;
            float[] elev = isUltra ? dense.UltraDenseCellElevation : dense.CellElevation;

            Color32[] palette = viewMode == SphereViewMode.Plates
                ? BuildPlatePalette(result.Tectonics.PlateCount, result.Tectonics.PolarPlateCount)
                : null;

            for (int c = 0; c < cellCount; c++)
            {
                int[] cellVerts = mesh.CellVertices[c];
                if (cellVerts == null || cellVerts.Length < 3)
                    continue;

                int coarseCell = toCoarse[c];
                Color32 color;

                // Highlight selected site cell
                if (siteCoarseCell >= 0 && coarseCell == siteCoarseCell)
                {
                    color = SiteHighlightColor;
                }
                else if (viewMode == SphereViewMode.CratonsBasins)
                {
                    color = CratonBasinColor(coarseCell, elev[c], result.Tectonics);
                }
                else if (viewMode == SphereViewMode.Plates)
                {
                    color = palette[result.Tectonics.CellPlate[coarseCell]];
                }
                else
                {
                    color = ElevationColor(elev[c]);
                }

                // Center vertex + polygon vertices
                int vertCount = 1 + cellVerts.Length;
                for (int i = 0; i < vertCount; i++)
                    colors.Add(color);
            }

            return colors;
        }

        /// <summary>
        /// Debug coloring for cratons (amber) and basins (teal), elevation fallback for unmarked cells.
        /// </summary>
        private static Color32 CratonBasinColor(int coarseCell, float elev, TectonicData tectonics)
        {
            float cratonStr = tectonics.CellCratonStrength != null ? tectonics.CellCratonStrength[coarseCell] : 0f;
            int basin = tectonics.CellBasinId != null ? tectonics.CellBasinId[coarseCell] : 0;

            if (cratonStr > 0f)
            {
                // Amber: pale yellow at edges, bright amber at deep interior
                byte r = (byte)(180 + 75 * cratonStr);
                byte g = (byte)(140 + 60 * cratonStr);
                byte b = (byte)(30);
                return new Color32(r, g, b, 255);
            }

            if (basin > 0)
            {
                // Teal/cyan, hue varies by basin ID for visual distinction
                float hue = (basin * 0.618034f) % 1f; // golden ratio spacing
                Color bc = Color.HSVToRGB(0.42f + hue * 0.18f, 0.7f, 0.85f);
                return new Color32(
                    (byte)(bc.r * 255),
                    (byte)(bc.g * 255),
                    (byte)(bc.b * 255),
                    255);
            }

            return ElevationColor(elev);
        }

        /// <summary>
        /// Elevation color ramp: deep blue (0.0) → medium blue (0.5) → green (0.5) → brown (0.7) → white (1.0)
        /// </summary>
        private static Color32 ElevationColor(float elev)
        {
            // Sea level at 0.5
            if (elev < 0.5f)
            {
                // Deep blue (0.0) to medium blue (0.5)
                float t = elev / 0.5f;
                byte r = (byte)(10 * t);
                byte g = (byte)(30 + 70 * t);
                byte b = (byte)(80 + 140 * t);
                return new Color32(r, g, b, 255);
            }
            else if (elev < 0.6f)
            {
                // Green (land near sea level)
                float t = (elev - 0.5f) / 0.1f;
                byte r = (byte)(30 + 80 * t);
                byte g = (byte)(140 + 40 * t);
                byte b = (byte)(40 - 10 * t);
                return new Color32(r, g, b, 255);
            }
            else if (elev < 0.75f)
            {
                // Green → brown (highlands)
                float t = (elev - 0.55f) / 0.2f;
                byte r = (byte)(110 + 50 * t);
                byte g = (byte)(180 - 80 * t);
                byte b = (byte)(30 + 10 * t);
                return new Color32(r, g, b, 255);
            }
            else
            {
                // Brown → white (mountains/peaks)
                float t = (elev - 0.75f) / 0.25f;
                if (t > 1f) t = 1f;
                byte r = (byte)(160 + 95 * t);
                byte g = (byte)(100 + 155 * t);
                byte b = (byte)(40 + 215 * t);
                return new Color32(r, g, b, 255);
            }
        }

        /// <summary>
        /// Build a palette of evenly-spaced hues for plate coloring.
        /// </summary>
        private static Color32[] BuildPlatePalette(int plateCount, int polarPlateCount)
        {
            var palette = new Color32[plateCount];
            // Polar caps are black
            for (int i = 0; i < polarPlateCount; i++)
                palette[i] = new Color32(0, 0, 0, 255);
            // Non-polar plates get evenly-spaced hues
            int nonPolar = plateCount - polarPlateCount;
            for (int i = polarPlateCount; i < plateCount; i++)
            {
                Color c = Color.HSVToRGB((float)(i - polarPlateCount) / nonPolar, 0.6f, 0.8f);
                palette[i] = new Color32(
                    (byte)(c.r * 255),
                    (byte)(c.g * 255),
                    (byte)(c.b * 255),
                    255
                );
            }
            return palette;
        }
    }
}
