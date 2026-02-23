using System;
using System.Collections.Generic;
using NUnit.Framework;
using PopGen.Core;

namespace EconSim.Tests
{
    [TestFixture]
    [Category("MapGen")]
    public class CultureForestTests
    {
        [Test]
        public void AllNodes_HaveValidCultureTypeIndex()
        {
            var allTypes = CultureTypes.All;
            foreach (var leaf in CultureForest.GetLeaves())
            {
                Assert.That(leaf.CultureTypeIndex, Is.GreaterThanOrEqualTo(0),
                    $"Node {leaf.Id} has negative CultureTypeIndex.");
                Assert.That(leaf.CultureTypeIndex, Is.LessThan(allTypes.Length),
                    $"Node {leaf.Id} has CultureTypeIndex {leaf.CultureTypeIndex} >= {allTypes.Length}.");
            }

            foreach (var root in CultureForest.GetRoots())
            {
                Assert.That(root.CultureTypeIndex, Is.GreaterThanOrEqualTo(0),
                    $"Root {root.Id} has negative CultureTypeIndex.");
                Assert.That(root.CultureTypeIndex, Is.LessThan(allTypes.Length),
                    $"Root {root.Id} has CultureTypeIndex {root.CultureTypeIndex} >= {allTypes.Length}.");
            }
        }

        [Test]
        public void AllLeaves_HaveParent()
        {
            foreach (var leaf in CultureForest.GetLeaves())
            {
                Assert.That(leaf.IsLeaf, Is.True, $"Node {leaf.Id} in leaves array but IsLeaf=false.");
                Assert.That(leaf.ParentId, Is.Not.Null.And.Not.Empty,
                    $"Leaf {leaf.Id} has null/empty ParentId.");
                Assert.That(CultureForest.GetNode(leaf.ParentId), Is.Not.Null,
                    $"Leaf {leaf.Id} references non-existent parent '{leaf.ParentId}'.");
            }
        }

        [Test]
        public void AllRoots_HaveNullParent()
        {
            foreach (var root in CultureForest.GetRoots())
            {
                Assert.That(root.ParentId, Is.Null,
                    $"Root {root.Id} has non-null ParentId '{root.ParentId}'.");
                Assert.That(root.IsLeaf, Is.False,
                    $"Root {root.Id} is marked as leaf.");
            }
        }

        [Test]
        public void NoOrphans_AllNodesReachableFromRoots()
        {
            var reachable = new HashSet<string>();
            foreach (var root in CultureForest.GetRoots())
            {
                reachable.Add(root.Id);
                foreach (var child in CultureForest.GetChildren(root.Id))
                    reachable.Add(child.Id);
            }

            foreach (var leaf in CultureForest.GetLeaves())
            {
                Assert.That(reachable.Contains(leaf.Id), Is.True,
                    $"Leaf {leaf.Id} is not reachable from any root.");
            }
        }

        [Test]
        public void EquatorProximity_InValidRange()
        {
            foreach (var root in CultureForest.GetRoots())
            {
                Assert.That(root.EquatorProximity, Is.GreaterThanOrEqualTo(0f).And.LessThanOrEqualTo(1f),
                    $"Root {root.Id} has EquatorProximity {root.EquatorProximity} outside [0,1].");
            }
        }

        [Test]
        public void TreeDistance_SameNode_IsZero()
        {
            Assert.That(CultureForest.TreeDistance("norse.danish", "norse.danish"), Is.EqualTo(0));
        }

        [Test]
        public void TreeDistance_ParentChild_IsOne()
        {
            Assert.That(CultureForest.TreeDistance("norse", "norse.danish"), Is.EqualTo(1));
            Assert.That(CultureForest.TreeDistance("norse.danish", "norse"), Is.EqualTo(1));
        }

        [Test]
        public void TreeDistance_Siblings_IsTwo()
        {
            Assert.That(CultureForest.TreeDistance("norse.danish", "norse.icelandic"), Is.EqualTo(2));
            Assert.That(CultureForest.TreeDistance("celtic.welsh", "celtic.gaelic"), Is.EqualTo(2));
        }

        [Test]
        public void TreeDistance_CrossTree_IsMaxValue()
        {
            Assert.That(CultureForest.TreeDistance("norse.danish", "celtic.welsh"), Is.EqualTo(int.MaxValue));
            Assert.That(CultureForest.TreeDistance("uralic.western", "germanic.lowland"), Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void Affinity_SameNode_IsOne()
        {
            Assert.That(CultureForest.Affinity("norse.danish", "norse.danish"), Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void Affinity_Siblings_IsOneThird()
        {
            Assert.That(CultureForest.Affinity("norse.danish", "norse.icelandic"),
                Is.EqualTo(1f / 3f).Within(0.001f));
        }

        [Test]
        public void Affinity_CrossTree_IsZero()
        {
            Assert.That(CultureForest.Affinity("norse.danish", "celtic.welsh"), Is.EqualTo(0f));
        }

        [Test]
        public void SelectForLatitudeRange_PolarLat_FavorsNorseUralic()
        {
            // Lat -70 to -60 → mapEquatorProximity ≈ 0.28 → norse (0.15) and uralic (0.20) should score high
            var selected = CultureForest.SelectForLatitudeRange(-70f, -60f, 4, 42);
            Assert.That(selected.Length, Is.EqualTo(4));

            var rootIds = new HashSet<string>();
            foreach (var node in selected)
            {
                var root = CultureForest.GetNode(node.ParentId);
                rootIds.Add(root.Id);
            }

            Assert.That(rootIds.Contains("norse") || rootIds.Contains("uralic"), Is.True,
                "Expected polar latitudes to include norse or uralic cultures.");
        }

        [Test]
        public void SelectForLatitudeRange_TemperateLat_FavorsGermanicBaltoSlavic()
        {
            // Lat -50 to -35 → mapEquatorProximity ≈ 0.53 → balto-slavic (0.50) and germanic (0.40)
            var selected = CultureForest.SelectForLatitudeRange(-50f, -35f, 4, 42);
            Assert.That(selected.Length, Is.EqualTo(4));

            var rootIds = new HashSet<string>();
            foreach (var node in selected)
            {
                var root = CultureForest.GetNode(node.ParentId);
                rootIds.Add(root.Id);
            }

            Assert.That(rootIds.Contains("balto-slavic") || rootIds.Contains("germanic"), Is.True,
                "Expected temperate latitudes to include balto-slavic or germanic cultures.");
        }

        [Test]
        public void SelectForLatitudeRange_ReturnsExactCount()
        {
            for (int count = 1; count <= 8; count++)
            {
                var selected = CultureForest.SelectForLatitudeRange(-45f, -30f, count, 12345);
                Assert.That(selected.Length, Is.EqualTo(count),
                    $"Expected exactly {count} cultures, got {selected.Length}.");
            }
        }

        [Test]
        public void SelectForLatitudeRange_AllSelectedAreLeaves()
        {
            var selected = CultureForest.SelectForLatitudeRange(-45f, -30f, 6, 99);
            foreach (var node in selected)
            {
                Assert.That(node.IsLeaf, Is.True,
                    $"Selected node {node.Id} is not a leaf.");
            }
        }

        [Test]
        public void SelectForLatitudeRange_DeterministicForSameSeed()
        {
            var first = CultureForest.SelectForLatitudeRange(-45f, -30f, 5, 777);
            var second = CultureForest.SelectForLatitudeRange(-45f, -30f, 5, 777);
            Assert.That(first.Length, Is.EqualTo(second.Length));
            for (int i = 0; i < first.Length; i++)
            {
                Assert.That(first[i].Id, Is.EqualTo(second[i].Id),
                    $"Selection mismatch at index {i}: {first[i].Id} vs {second[i].Id}.");
            }
        }

        [Test]
        public void SelectForLatitudeRange_DifferentSeedsCanDiffer()
        {
            var a = CultureForest.SelectForLatitudeRange(-45f, -30f, 5, 111);
            var b = CultureForest.SelectForLatitudeRange(-45f, -30f, 5, 999);
            bool differs = false;
            for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
            {
                if (a[i].Id != b[i].Id)
                {
                    differs = true;
                    break;
                }
            }

            // This isn't guaranteed but is very likely with different seeds
            // If it fails, it's a sign the seed isn't being used
            Assert.That(differs || a.Length != b.Length, Is.True,
                "Expected different seeds to produce different selections (probabilistic).");
        }

        [Test]
        public void ForestHas5Roots()
        {
            Assert.That(CultureForest.GetRoots().Length, Is.EqualTo(5));
        }

        [Test]
        public void ForestHasAtLeast13Leaves()
        {
            Assert.That(CultureForest.GetLeaves().Length, Is.GreaterThanOrEqualTo(13));
        }
    }
}
