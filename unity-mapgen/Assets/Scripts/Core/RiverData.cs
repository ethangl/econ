namespace MapGen.Core
{
    public class RiverData
    {
        /// <summary>Per-vertex interpolated terrain height (avg of surrounding cells).</summary>
        public float[] VertexHeight;

        /// <summary>Per-vertex interpolated precipitation (avg of surrounding cells).</summary>
        public float[] VertexPrecip;

        /// <summary>Per-vertex water level after depression fill. >= VertexHeight. Lake where >.</summary>
        public float[] WaterLevel;

        /// <summary>Per-vertex accumulated water flux.</summary>
        public float[] VertexFlux;

        /// <summary>Per-vertex flow target: index of vertex this drains to. -1 = none.</summary>
        public int[] FlowTarget;

        /// <summary>Per-edge flux crossing this edge.</summary>
        public float[] EdgeFlux;

        /// <summary>Extracted rivers.</summary>
        public River[] Rivers;

        public CellMesh Mesh { get; }
        public int VertexCount => Mesh.VertexCount;
        public int EdgeCount => Mesh.EdgeCount;

        public RiverData(CellMesh mesh)
        {
            Mesh = mesh;
            int vCount = mesh.VertexCount;
            VertexHeight = new float[vCount];
            VertexPrecip = new float[vCount];
            WaterLevel = new float[vCount];
            VertexFlux = new float[vCount];
            FlowTarget = new int[vCount];
            EdgeFlux = new float[mesh.EdgeCount];
            Rivers = System.Array.Empty<River>();

            for (int i = 0; i < FlowTarget.Length; i++)
                FlowTarget[i] = -1;
        }

        /// <summary>Is this vertex a lake? (water level above interpolated terrain height)</summary>
        public bool IsLake(int vertex)
        {
            return !IsOcean(vertex) && WaterLevel[vertex] > VertexHeight[vertex];
        }

        /// <summary>Is this vertex in the ocean? (interpolated height at or below sea level)</summary>
        public bool IsOcean(int vertex)
        {
            return VertexHeight[vertex] <= HeightGrid.SeaLevel;
        }
    }

    public struct River
    {
        public int Id;
        public int[] Vertices;     // ordered vertex indices, mouth-first
        public int MouthVertex;    // vertex at ocean/confluence end
        public int SourceVertex;   // vertex at upstream end
        public float Discharge;    // flux at mouth
    }
}
