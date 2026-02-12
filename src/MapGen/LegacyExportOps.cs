using System;
using System.Collections.Generic;

namespace MapGen.Core
{
    /// <summary>
    /// Neutral export payload for legacy adapters that still consume display-style river data.
    /// </summary>
    public struct RiverExport
    {
        public int Id;
        public Vec2[] Points;
        public float Width;
        public int Discharge;
    }

    /// <summary>
    /// Neutral RGBA byte color.
    /// </summary>
    public struct Rgba32
    {
        public byte R;
        public byte G;
        public byte B;
        public byte A;

        public Rgba32(byte r, byte g, byte b, byte a = 255)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }
    }

    /// <summary>
    /// Legacy biome catalog entry for adapter-facing biome metadata.
    /// </summary>
    public struct BiomeCatalogEntry
    {
        public int Id;
        public string Name;
        public Rgba32 Color;
        public int Habitability;
        public int MovementCost;
    }

    /// <summary>
    /// Exports compatibility payloads derived from canonical MapGen fields.
    /// </summary>
    public static class LegacyExportOps
    {
        static readonly BiomeCatalogEntry[] LegacyBiomeCatalog =
        {
            new BiomeCatalogEntry { Id = 0,  Name = "Glacier",             Color = new Rgba32(220, 235, 250, 255), Habitability = 2,  MovementCost = 200 },
            new BiomeCatalogEntry { Id = 1,  Name = "Tundra",              Color = new Rgba32(180, 210, 200, 255), Habitability = 8,  MovementCost = 140 },
            new BiomeCatalogEntry { Id = 2,  Name = "Salt Flat",           Color = new Rgba32(230, 220, 200, 255), Habitability = 3,  MovementCost = 80  },
            new BiomeCatalogEntry { Id = 3,  Name = "Coastal Marsh",       Color = new Rgba32(140, 175, 140, 255), Habitability = 25, MovementCost = 160 },
            new BiomeCatalogEntry { Id = 4,  Name = "Alpine Barren",       Color = new Rgba32(170, 170, 170, 255), Habitability = 5,  MovementCost = 180 },
            new BiomeCatalogEntry { Id = 5,  Name = "Mountain Shrub",      Color = new Rgba32(140, 160, 120, 255), Habitability = 15, MovementCost = 150 },
            new BiomeCatalogEntry { Id = 6,  Name = "Floodplain",          Color = new Rgba32(90,  160, 70,  255), Habitability = 80, MovementCost = 100 },
            new BiomeCatalogEntry { Id = 7,  Name = "Wetland",             Color = new Rgba32(100, 150, 120, 255), Habitability = 20, MovementCost = 170 },
            new BiomeCatalogEntry { Id = 8,  Name = "Hot Desert",          Color = new Rgba32(220, 200, 140, 255), Habitability = 5,  MovementCost = 120 },
            new BiomeCatalogEntry { Id = 9,  Name = "Cold Desert",         Color = new Rgba32(200, 195, 170, 255), Habitability = 8,  MovementCost = 110 },
            new BiomeCatalogEntry { Id = 10, Name = "Scrubland",           Color = new Rgba32(180, 180, 100, 255), Habitability = 30, MovementCost = 100 },
            new BiomeCatalogEntry { Id = 11, Name = "Tropical Rainforest", Color = new Rgba32(40,  120, 40,  255), Habitability = 40, MovementCost = 160 },
            new BiomeCatalogEntry { Id = 12, Name = "Tropical Dry Forest", Color = new Rgba32(100, 140, 60,  255), Habitability = 50, MovementCost = 130 },
            new BiomeCatalogEntry { Id = 13, Name = "Savanna",             Color = new Rgba32(170, 180, 80,  255), Habitability = 45, MovementCost = 90  },
            new BiomeCatalogEntry { Id = 14, Name = "Boreal Forest",       Color = new Rgba32(70,  110, 80,  255), Habitability = 20, MovementCost = 140 },
            new BiomeCatalogEntry { Id = 15, Name = "Temperate Forest",    Color = new Rgba32(60,  140, 60,  255), Habitability = 60, MovementCost = 120 },
            new BiomeCatalogEntry { Id = 16, Name = "Grassland",           Color = new Rgba32(150, 190, 90,  255), Habitability = 70, MovementCost = 80  },
            new BiomeCatalogEntry { Id = 17, Name = "Woodland",            Color = new Rgba32(90,  150, 70,  255), Habitability = 55, MovementCost = 110 },
            new BiomeCatalogEntry { Id = 18, Name = "Lake",                Color = new Rgba32(80,  130, 190, 255), Habitability = 0,  MovementCost = 250 },
        };

        public static RiverExport[] BuildRiverExports(RiverField riverData)
        {
            var mesh = riverData.Mesh;
            var rivers = new List<RiverExport>(riverData.Rivers.Length);

            for (int r = 0; r < riverData.Rivers.Length; r++)
            {
                ref var mgRiver = ref riverData.Rivers[r];
                if (mgRiver.Vertices == null || mgRiver.Vertices.Length < 2)
                    continue;

                var points = new List<Vec2>(mgRiver.Vertices.Length + 2);
                for (int vi = mgRiver.Vertices.Length - 1; vi >= 0; vi--)
                {
                    int vertIdx = mgRiver.Vertices[vi];
                    if ((uint)vertIdx >= (uint)mesh.VertexCount)
                        continue;
                    points.Add(mesh.Vertices[vertIdx]);
                }

                if (points.Count < 2)
                    continue;

                int sourceVert = mgRiver.SourceVertex;
                if ((uint)sourceVert < (uint)mesh.VertexCount)
                {
                    int[] sourceNeighbors = mesh.VertexNeighbors[sourceVert];
                    if (sourceNeighbors != null)
                    {
                        for (int i = 0; i < sourceNeighbors.Length; i++)
                        {
                            int nb = sourceNeighbors[i];
                            if ((uint)nb < (uint)mesh.VertexCount && riverData.IsLake(nb))
                            {
                                points.Insert(0, mesh.Vertices[nb]);
                                break;
                            }
                        }
                    }
                }

                int mouthVert = mgRiver.MouthVertex;
                if ((uint)mouthVert < (uint)mesh.VertexCount && mouthVert != mgRiver.Vertices[0])
                    points.Add(mesh.Vertices[mouthVert]);

                if ((uint)mouthVert < (uint)riverData.FlowTarget.Length)
                {
                    int flowTarget = riverData.FlowTarget[mouthVert];
                    if ((uint)flowTarget < (uint)mesh.VertexCount && riverData.IsOcean(flowTarget))
                        points.Add(mesh.Vertices[flowTarget]);
                }

                int riverId = r + 1;
                rivers.Add(new RiverExport
                {
                    Id = riverId,
                    Points = points.ToArray(),
                    Width = Math.Min(5f, Math.Max(0.5f, (float)Math.Log(mgRiver.Discharge + 1f) * 0.4f)),
                    Discharge = (int)mgRiver.Discharge
                });
            }

            return rivers.ToArray();
        }

        public static IReadOnlyList<BiomeCatalogEntry> GetLegacyBiomeCatalog()
        {
            return LegacyBiomeCatalog;
        }
    }
}
