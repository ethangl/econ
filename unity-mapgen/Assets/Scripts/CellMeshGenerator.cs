using System;
using UnityEngine;
using MapGen.Core;

namespace MapGen
{
    /// <summary>
    /// Generates and holds a CellMesh. Entry point for map generation.
    /// 1 mesh unit = 1 km. Dimensions derived from CellCount + AspectRatio.
    /// </summary>
    public class CellMeshGenerator : MonoBehaviour
    {
        [System.NonSerialized] public int Seed = 12345;
        [System.NonSerialized] public int CellCount = 10000;
        [System.NonSerialized] public float AspectRatio = 16f / 9f;

        const float CellSizeKm = 2.5f;

        public CellMesh Mesh { get; private set; }

        public static (float Width, float Height) ComputeMapSize(int cellCount, float aspectRatio)
        {
            float cellArea = CellSizeKm * CellSizeKm; // 6.25 km²
            float mapArea = cellCount * cellArea;
            float width = (float)Math.Sqrt(mapArea * aspectRatio);
            float height = width / aspectRatio;
            return (width, height);
        }

        public void Generate()
        {
            var (mapWidth, mapHeight) = ComputeMapSize(CellCount, AspectRatio);
            var (gridPoints, spacing) = PointGenerator.JitteredGrid(mapWidth, mapHeight, CellCount, Seed);
            var boundaryPoints = PointGenerator.BoundaryPoints(mapWidth, mapHeight, spacing);

            Mesh = VoronoiBuilder.Build(mapWidth, mapHeight, gridPoints, boundaryPoints);
            Mesh.ComputeAreas();

            Debug.Log($"Generated mesh: {Mesh.CellCount} cells, {Mesh.VertexCount} vertices, {Mesh.EdgeCount} edges, " +
                      $"size {mapWidth:F0} x {mapHeight:F0} km");
        }

    }
}
