using System;
using MapGen.Core;
using NUnit.Framework;

namespace EconSim.Tests
{
    [TestFixture]
    [Category("MapGenV2")]
    public class MapGenV2BiomePoliticalMetricsTests
    {
        [Test]
        public void BiomesAndPolitics_StayWithinBroadSanityBands()
        {
            foreach (HeightmapTemplateType template in Enum.GetValues(typeof(HeightmapTemplateType)))
            {
                var config = new MapGenV2Config
                {
                    Seed = 20260212,
                    CellCount = 5000,
                    Template = template,
                    MaxElevationMeters = 5000f,
                    MaxSeaDepthMeters = 1250f,
                    RiverThreshold = 180f,
                    RiverTraceThreshold = 10f,
                    MinRiverVertices = 8
                };

                MapGenV2Result result = MapGenPipelineV2.Generate(config);
                Assert.That(result.Biomes, Is.Not.Null, $"Biomes missing for {template}");
                Assert.That(result.Political, Is.Not.Null, $"Political output missing for {template}");

                int landCells = 0;
                int inhabitedCells = 0;
                float totalPopulation = 0f;

                for (int i = 0; i < result.Mesh.CellCount; i++)
                {
                    bool isLand = result.Elevation.IsLand(i) && !result.Biomes.IsLakeCell[i];

                    float suitability = result.Biomes.Suitability[i];
                    float population = result.Biomes.Population[i];
                    float movement = result.Biomes.MovementCost[i];

                    Assert.That(float.IsNaN(suitability) || float.IsInfinity(suitability), Is.False,
                        $"Invalid suitability for {template} at cell {i}");
                    Assert.That(float.IsNaN(population) || float.IsInfinity(population), Is.False,
                        $"Invalid population for {template} at cell {i}");
                    Assert.That(float.IsNaN(movement) || float.IsInfinity(movement), Is.False,
                        $"Invalid movement for {template} at cell {i}");

                    Assert.That(suitability, Is.GreaterThanOrEqualTo(0f).And.LessThanOrEqualTo(100f),
                        $"Suitability out of range for {template} at cell {i}");
                    Assert.That(movement, Is.GreaterThanOrEqualTo(0f).And.LessThanOrEqualTo(150f),
                        $"Movement cost out of broad range for {template} at cell {i}");

                    if (isLand)
                    {
                        landCells++;
                        totalPopulation += population;
                        if (suitability > 0f)
                            inhabitedCells++;

                        Assert.That(result.Political.RealmId[i], Is.GreaterThan(0),
                            $"Land cell has no realm assignment for {template} at cell {i}");
                        Assert.That(result.Political.ProvinceId[i], Is.GreaterThan(0),
                            $"Land cell has no province assignment for {template} at cell {i}");
                        Assert.That(result.Political.CountyId[i], Is.GreaterThan(0),
                            $"Land cell has no county assignment for {template} at cell {i}");
                    }
                    else
                    {
                        Assert.That(result.Political.RealmId[i], Is.EqualTo(0),
                            $"Water cell has realm assignment for {template} at cell {i}");
                        Assert.That(result.Political.ProvinceId[i], Is.EqualTo(0),
                            $"Water cell has province assignment for {template} at cell {i}");
                        Assert.That(result.Political.CountyId[i], Is.EqualTo(0),
                            $"Water cell has county assignment for {template} at cell {i}");
                    }
                }

                if (landCells > 0)
                {
                    Assert.That(inhabitedCells, Is.GreaterThan(0), $"No inhabitable land cells for {template}");
                    Assert.That(totalPopulation, Is.GreaterThan(0f), $"No total population for {template}");
                    Assert.That(result.Political.RealmCount, Is.GreaterThan(0), $"No realms for {template}");
                    Assert.That(result.Political.ProvinceCount, Is.GreaterThanOrEqualTo(result.Political.RealmCount),
                        $"Province count < realm count for {template}");
                    Assert.That(result.Political.CountyCount, Is.GreaterThanOrEqualTo(result.Political.ProvinceCount),
                        $"County count < province count for {template}");
                }
            }
        }
    }
}
