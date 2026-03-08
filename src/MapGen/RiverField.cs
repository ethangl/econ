namespace MapGen.Core
{
    public class RiverField
    {
        /// <summary>Per-vertex interpolated signed elevation in meters.</summary>
        public float[] VertexElevationMeters;

        /// <summary>Per-vertex water level (max elevation along flow path to ocean).
        /// Used for lake detection: if WaterLevel - Elevation > threshold, vertex is in a lake.</summary>
        public float[] WaterLevelMeters;

        /// <summary>Per-vertex accumulated water flux (unitless).</summary>
        public float[] VertexFlux;

        /// <summary>Per-vertex: index of the edge water flows out through, -1 = none.</summary>
        public int[] DownslopeEdge;

        /// <summary>Per-vertex flow target: index of downstream vertex, -1 = none.
        /// Derived from DownslopeEdge for legacy consumers.</summary>
        public int[] FlowTarget;

        /// <summary>Per-edge flux crossing this edge.</summary>
        public float[] EdgeFlux;

        /// <summary>Traversal order: vertices ordered root-first (ocean outward).
        /// Reverse for leaf-first accumulation.</summary>
        public int[] VertexOrder;

        /// <summary>Extracted river polylines (legacy — will be replaced by edge-based rendering).</summary>
        public RiverPath[] Rivers;

        /// <summary>Flux threshold for "major river" (mouth-level).</summary>
        public float RiverThreshold;

        /// <summary>Flux threshold for "any visible stream" (trace-level).</summary>
        public float RiverTraceThreshold;

        public CellMesh Mesh { get; }
        public int VertexCount => Mesh.VertexCount;
        public int EdgeCount => Mesh.EdgeCount;

        public RiverField(CellMesh mesh)
        {
            Mesh = mesh;
            int vCount = mesh.VertexCount;
            VertexElevationMeters = new float[vCount];
            WaterLevelMeters = new float[vCount];
            VertexFlux = new float[vCount];
            DownslopeEdge = new int[vCount];
            FlowTarget = new int[vCount];
            EdgeFlux = new float[mesh.EdgeCount];
            VertexOrder = System.Array.Empty<int>();
            Rivers = System.Array.Empty<RiverPath>();

            for (int i = 0; i < vCount; i++)
            {
                DownslopeEdge[i] = -1;
                FlowTarget[i] = -1;
            }
        }

        public const float MinLakeDepthMeters = 25f;

        public bool IsLake(int vertex)
        {
            return !IsOcean(vertex) && WaterLevelMeters[vertex] - VertexElevationMeters[vertex] > MinLakeDepthMeters;
        }

        public bool IsOcean(int vertex)
        {
            return VertexElevationMeters[vertex] <= 0f;
        }
    }

    public struct RiverPath
    {
        public int Id;
        public int[] Vertices;
        public int MouthVertex;
        public int SourceVertex;
        public float Discharge;
    }
}
