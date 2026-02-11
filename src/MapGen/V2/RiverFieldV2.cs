namespace MapGen.Core
{
    public class RiverFieldV2
    {
        /// <summary>Per-vertex interpolated signed elevation in meters.</summary>
        public float[] VertexElevationMeters;

        /// <summary>Per-vertex precipitation contribution in normalized flux units.</summary>
        public float[] VertexPrecipFlux;

        /// <summary>Per-vertex water level after depression fill in meters.</summary>
        public float[] WaterLevelMeters;

        /// <summary>Per-vertex accumulated water flux (unitless, thresholded by config).</summary>
        public float[] VertexFlux;

        /// <summary>Per-vertex flow target: index of downstream vertex, -1 = none.</summary>
        public int[] FlowTarget;

        /// <summary>Per-edge flux crossing this edge.</summary>
        public float[] EdgeFlux;

        /// <summary>Extracted river polylines.</summary>
        public RiverV2[] Rivers;

        public CellMesh Mesh { get; }
        public int VertexCount => Mesh.VertexCount;
        public int EdgeCount => Mesh.EdgeCount;

        public RiverFieldV2(CellMesh mesh)
        {
            Mesh = mesh;
            int vCount = mesh.VertexCount;
            VertexElevationMeters = new float[vCount];
            VertexPrecipFlux = new float[vCount];
            WaterLevelMeters = new float[vCount];
            VertexFlux = new float[vCount];
            FlowTarget = new int[vCount];
            EdgeFlux = new float[mesh.EdgeCount];
            Rivers = System.Array.Empty<RiverV2>();

            for (int i = 0; i < FlowTarget.Length; i++)
                FlowTarget[i] = -1;
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

    public struct RiverV2
    {
        public int Id;
        public int[] Vertices;
        public int MouthVertex;
        public int SourceVertex;
        public float Discharge;
    }
}
