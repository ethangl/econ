using UnityEngine;
using MapGen.Core;

namespace MapGen
{
    /// <summary>
    /// Generates and holds a CellMesh. Entry point for map generation.
    /// </summary>
    public class CellMeshGenerator : MonoBehaviour
    {
        public int Seed = 12345;
        public int CellCount = 10000;
        public float MapWidth = 1920;
        public float MapHeight = 1080;

        public CellMesh Mesh { get; private set; }

        public void Generate()
        {
            var (gridPoints, spacing) = PointGenerator.JitteredGrid(MapWidth, MapHeight, CellCount, Seed);
            var boundaryPoints = PointGenerator.BoundaryPoints(MapWidth, MapHeight, spacing);

            Mesh = VoronoiBuilder.Build(MapWidth, MapHeight, gridPoints, boundaryPoints);

            Debug.Log($"Generated mesh: {Mesh.CellCount} cells, {Mesh.VertexCount} vertices, {Mesh.EdgeCount} edges");
        }

        void Start()
        {
            Generate();
        }
    }
}
