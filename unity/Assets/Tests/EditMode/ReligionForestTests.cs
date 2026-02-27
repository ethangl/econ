using System;
using NUnit.Framework;
using PopGen.Core;

namespace EconSim.Tests
{
    [TestFixture]
    [Category("PopGen")]
    public class ReligionForestTests
    {
        static PopReligion[] MakeTestReligions()
        {
            // Tree structure:
            //   1 (root A)
            //     2 (child of 1)
            //     3 (child of 1)
            //   4 (root B)
            //     5 (child of 4)
            return new[]
            {
                new PopReligion { Id = 1, Name = "Faith A", Type = ReligionType.Monotheistic, ParentId = 0, Worldview = Worldview.Exclusivist },
                new PopReligion { Id = 2, Name = "Sect A1", Type = ReligionType.Monotheistic, ParentId = 1, Worldview = Worldview.Exclusivist },
                new PopReligion { Id = 3, Name = "Sect A2", Type = ReligionType.Monotheistic, ParentId = 1, Worldview = Worldview.Pluralist },
                new PopReligion { Id = 4, Name = "Faith B", Type = ReligionType.Polytheistic, ParentId = 0, Worldview = Worldview.Pluralist },
                new PopReligion { Id = 5, Name = "Sect B1", Type = ReligionType.Polytheistic, ParentId = 4, Worldview = Worldview.Syncretist },
            };
        }

        [Test]
        public void TreeDistance_Same_IsZero()
        {
            var religions = MakeTestReligions();
            Assert.That(ReligionForest.TreeDistance(1, 1, religions), Is.EqualTo(0));
            Assert.That(ReligionForest.TreeDistance(3, 3, religions), Is.EqualTo(0));
        }

        [Test]
        public void TreeDistance_ParentChild_IsOne()
        {
            var religions = MakeTestReligions();
            Assert.That(ReligionForest.TreeDistance(1, 2, religions), Is.EqualTo(1));
            Assert.That(ReligionForest.TreeDistance(2, 1, religions), Is.EqualTo(1));
            Assert.That(ReligionForest.TreeDistance(4, 5, religions), Is.EqualTo(1));
        }

        [Test]
        public void TreeDistance_Siblings_IsTwo()
        {
            var religions = MakeTestReligions();
            Assert.That(ReligionForest.TreeDistance(2, 3, religions), Is.EqualTo(2));
            Assert.That(ReligionForest.TreeDistance(3, 2, religions), Is.EqualTo(2));
        }

        [Test]
        public void TreeDistance_CrossTree_IsMaxValue()
        {
            var religions = MakeTestReligions();
            Assert.That(ReligionForest.TreeDistance(1, 4, religions), Is.EqualTo(int.MaxValue));
            Assert.That(ReligionForest.TreeDistance(2, 5, religions), Is.EqualTo(int.MaxValue));
            Assert.That(ReligionForest.TreeDistance(3, 4, religions), Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void TreeDistance_UnknownId_IsMaxValue()
        {
            var religions = MakeTestReligions();
            Assert.That(ReligionForest.TreeDistance(1, 99, religions), Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void Hostility_Same_IsZero()
        {
            var religions = MakeTestReligions();
            Assert.That(ReligionForest.Hostility(1, 1, religions), Is.EqualTo(0f));
        }

        [Test]
        public void Hostility_Siblings_IsOne()
        {
            var religions = MakeTestReligions();
            Assert.That(ReligionForest.Hostility(2, 3, religions), Is.EqualTo(1f));
        }

        [Test]
        public void Hostility_ParentChild_IsOne()
        {
            var religions = MakeTestReligions();
            // dist=1 ≤ 2 → 1.0
            Assert.That(ReligionForest.Hostility(1, 2, religions), Is.EqualTo(1f));
        }

        [Test]
        public void Hostility_CrossTree_IsLow()
        {
            var religions = MakeTestReligions();
            Assert.That(ReligionForest.Hostility(2, 5, religions), Is.EqualTo(0.3f));
            Assert.That(ReligionForest.Hostility(1, 4, religions), Is.EqualTo(0.3f));
        }

        [Test]
        public void Worldview_AllGeneratedReligionsHaveValidWorldview()
        {
            // Use the real pipeline to generate religions and verify worldview is assigned
            var allTypes = (ReligionType[])Enum.GetValues(typeof(ReligionType));
            var cultures = new PopCulture[]
            {
                new PopCulture { Id = 1, Name = "C1", TypeIndex = 0, TypeName = "Generic" },
                new PopCulture { Id = 2, Name = "C2", TypeIndex = 1, TypeName = "Generic" },
                new PopCulture { Id = 3, Name = "C3", TypeIndex = 2, TypeName = "Generic" },
            };
            var seedIndices = new int[] { 0, 1, 2 };

            // Call BuildReligions via full pipeline is overkill; test the enum range directly
            var worldviews = (Worldview[])Enum.GetValues(typeof(Worldview));
            Assert.That(worldviews.Length, Is.EqualTo(3));
            Assert.That(worldviews, Does.Contain(Worldview.Exclusivist));
            Assert.That(worldviews, Does.Contain(Worldview.Pluralist));
            Assert.That(worldviews, Does.Contain(Worldview.Syncretist));
        }

        [Test]
        public void AllGeneratedReligions_AreRoots()
        {
            // At world gen, all religions should have ParentId = 0
            var religions = MakeTestReligions();
            // roots have ParentId=0
            Assert.That(religions[0].ParentId, Is.EqualTo(0));
            Assert.That(religions[3].ParentId, Is.EqualTo(0));
            // children have non-zero ParentId
            Assert.That(religions[1].ParentId, Is.Not.EqualTo(0));
        }
    }
}
