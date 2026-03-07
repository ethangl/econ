using System;
using System.Collections.Generic;

namespace PopGen.Core
{
    /// <summary>
    /// Static registry of culture trees. Multiple roots, depth 2, ~15 leaves.
    /// Provides tree queries (distance, affinity) and latitude-based selection.
    /// </summary>
    public static class CultureForest
    {
        static readonly CultureNode[] AllNodes;
        static readonly Dictionary<string, CultureNode> NodeById;
        static readonly Dictionary<string, List<CultureNode>> ChildrenByParent;
        static readonly CultureNode[] Roots;
        static readonly CultureNode[] Leaves;

        static CultureForest()
        {
            AllNodes = BuildNodes();
            NodeById = new Dictionary<string, CultureNode>(AllNodes.Length);
            ChildrenByParent = new Dictionary<string, List<CultureNode>>();
            var roots = new List<CultureNode>();
            var leaves = new List<CultureNode>();

            foreach (var node in AllNodes)
            {
                NodeById[node.Id] = node;
                if (node.ParentId == null)
                    roots.Add(node);
                if (node.IsLeaf)
                    leaves.Add(node);
                if (node.ParentId != null)
                {
                    if (!ChildrenByParent.TryGetValue(node.ParentId, out var list))
                    {
                        list = new List<CultureNode>();
                        ChildrenByParent[node.ParentId] = list;
                    }
                    list.Add(node);
                }
            }

            Roots = roots.ToArray();
            Leaves = leaves.ToArray();
        }

        public static CultureNode GetNode(string id)
        {
            return NodeById.TryGetValue(id, out var node) ? node : null;
        }

        public static CultureNode[] GetRoots() => Roots;

        public static CultureNode[] GetLeaves() => Leaves;

        public static List<CultureNode> GetChildren(string parentId)
        {
            if (ChildrenByParent.TryGetValue(parentId, out var list))
                return list;
            return new List<CultureNode>();
        }

        /// <summary>
        /// Find the root of the tree containing this node.
        /// </summary>
        static string GetRootId(CultureNode node)
        {
            var current = node;
            while (current.ParentId != null)
            {
                if (!NodeById.TryGetValue(current.ParentId, out current))
                    break;
            }
            return current.Id;
        }

        /// <summary>
        /// Hop count via LCA. Same node = 0, parent-child = 1, siblings = 2.
        /// Cross-tree = int.MaxValue.
        /// </summary>
        public static int TreeDistance(string a, string b)
        {
            if (a == b) return 0;
            if (!NodeById.TryGetValue(a, out var nodeA) || !NodeById.TryGetValue(b, out var nodeB))
                return int.MaxValue;

            string rootA = GetRootId(nodeA);
            string rootB = GetRootId(nodeB);
            if (rootA != rootB) return int.MaxValue;

            // Collect ancestors of A
            var ancestorsA = new Dictionary<string, int>(); // id -> depth from A
            int depth = 0;
            var current = nodeA;
            while (current != null)
            {
                ancestorsA[current.Id] = depth;
                depth++;
                current = current.ParentId != null && NodeById.TryGetValue(current.ParentId, out var p) ? p : null;
            }

            // Walk up from B until we hit an ancestor of A
            depth = 0;
            current = nodeB;
            while (current != null)
            {
                if (ancestorsA.TryGetValue(current.Id, out int depthA))
                    return depthA + depth;
                depth++;
                current = current.ParentId != null && NodeById.TryGetValue(current.ParentId, out var p) ? p : null;
            }

            return int.MaxValue;
        }

        /// <summary>
        /// 1f / (1 + distance). Cross-tree = 0.
        /// </summary>
        public static float Affinity(string a, string b)
        {
            int dist = TreeDistance(a, b);
            if (dist == int.MaxValue) return 0f;
            return 1f / (1 + dist);
        }

        /// <summary>
        /// Pick <paramref name="count"/> leaf cultures appropriate for the map's latitude band.
        /// </summary>
        public static CultureNode[] SelectForLatitudeRange(float latSouth, float latNorth, int count, int seed)
        {
            if (count <= 0) return Array.Empty<CultureNode>();

            float avgLat = (Math.Abs(latSouth) + Math.Abs(latNorth)) * 0.5f;
            float mapEquatorProximity = 1f - avgLat / 90f;
            mapEquatorProximity = Math.Max(0f, Math.Min(1f, mapEquatorProximity));

            // Score each root
            float bestScore = 0f;
            var rootScores = new float[Roots.Length];
            for (int i = 0; i < Roots.Length; i++)
            {
                float diff = Math.Abs(Roots[i].EquatorProximity - mapEquatorProximity);
                rootScores[i] = 1f / (1f + 4f * diff);
                if (rootScores[i] > bestScore)
                    bestScore = rootScores[i];
            }

            // Filter: discard roots scoring < 0.3 * best
            float threshold = 0.3f * bestScore;
            float totalScore = 0f;
            for (int i = 0; i < Roots.Length; i++)
            {
                if (rootScores[i] < threshold)
                    rootScores[i] = 0f;
                totalScore += rootScores[i];
            }

            if (totalScore <= 0f)
            {
                // Fallback: use all roots equally
                for (int i = 0; i < Roots.Length; i++)
                    rootScores[i] = 1f;
                totalScore = Roots.Length;
            }

            // Distribute leaf slots proportional to score
            var slotsPerRoot = new int[Roots.Length];
            int assigned = 0;
            for (int i = 0; i < Roots.Length; i++)
            {
                if (rootScores[i] <= 0f) continue;
                slotsPerRoot[i] = Math.Max(1, (int)Math.Round(count * rootScores[i] / totalScore));
                assigned += slotsPerRoot[i];
            }

            // Adjust to exactly count
            while (assigned > count)
            {
                // Remove from the root with most slots
                int maxIdx = -1;
                int maxSlots = 0;
                for (int i = 0; i < Roots.Length; i++)
                {
                    if (slotsPerRoot[i] > maxSlots)
                    {
                        maxSlots = slotsPerRoot[i];
                        maxIdx = i;
                    }
                }
                if (maxIdx < 0) break;
                slotsPerRoot[maxIdx]--;
                assigned--;
            }

            while (assigned < count)
            {
                // Add to highest-scoring root that has leaves
                int bestIdx = -1;
                float bestS = -1f;
                for (int i = 0; i < Roots.Length; i++)
                {
                    if (rootScores[i] > bestS && GetChildren(Roots[i].Id).Count > 0)
                    {
                        bestS = rootScores[i];
                        bestIdx = i;
                    }
                }
                if (bestIdx < 0) break;
                slotsPerRoot[bestIdx]++;
                assigned++;
            }

            // Pick leaves from each root round-robin using seed
            var result = new List<CultureNode>(count);
            uint rng = (uint)seed;

            for (int ri = 0; ri < Roots.Length; ri++)
            {
                if (slotsPerRoot[ri] <= 0) continue;
                var children = GetChildren(Roots[ri].Id);
                if (children.Count == 0) continue;

                // Shuffle children deterministically
                var shuffled = new List<CultureNode>(children);
                rng = Xorshift(rng ^ (uint)(ri * 7919));
                for (int i = shuffled.Count - 1; i > 0; i--)
                {
                    rng = Xorshift(rng);
                    int j = (int)(rng % (uint)(i + 1));
                    var tmp = shuffled[i];
                    shuffled[i] = shuffled[j];
                    shuffled[j] = tmp;
                }

                for (int s = 0; s < slotsPerRoot[ri]; s++)
                    result.Add(shuffled[s % shuffled.Count]);
            }

            // Trim or cycle to exactly count
            while (result.Count > count)
                result.RemoveAt(result.Count - 1);

            while (result.Count < count)
            {
                // Cycle from the beginning
                result.Add(result[result.Count % Math.Max(1, Leaves.Length)]);
            }

            return result.ToArray();
        }

        /// <summary>
        /// Pick cultures using both latitude and longitude for geographic specificity.
        /// Longitude adds a second scoring axis so cultures at the same latitude
        /// differentiate by east/west position.
        /// </summary>
        public static CultureNode[] SelectForPosition(float latitude, float longitude, int count, int seed)
        {
            if (count <= 0) return Array.Empty<CultureNode>();

            float mapEquatorProximity = 1f - Math.Abs(latitude) / 90f;
            mapEquatorProximity = Math.Max(0f, Math.Min(1f, mapEquatorProximity));

            // Normalize longitude from [-180,180] to [0,1]
            float normalizedLng = (longitude + 180f) / 360f;
            normalizedLng = Math.Max(0f, Math.Min(1f, normalizedLng));

            // Score each root with latitude * longitude
            float bestScore = 0f;
            var rootScores = new float[Roots.Length];
            for (int i = 0; i < Roots.Length; i++)
            {
                float latDiff = Math.Abs(Roots[i].EquatorProximity - mapEquatorProximity);
                float latScore = 1f / (1f + 4f * latDiff);
                float lngDiff = Math.Abs(normalizedLng - Roots[i].LongitudeBand);
                float lngScore = 1f / (1f + 3f * lngDiff);
                rootScores[i] = latScore * lngScore;
                if (rootScores[i] > bestScore)
                    bestScore = rootScores[i];
            }

            // Filter: discard roots scoring < 0.3 * best
            float threshold = 0.3f * bestScore;
            float totalScore = 0f;
            for (int i = 0; i < Roots.Length; i++)
            {
                if (rootScores[i] < threshold)
                    rootScores[i] = 0f;
                totalScore += rootScores[i];
            }

            if (totalScore <= 0f)
            {
                for (int i = 0; i < Roots.Length; i++)
                    rootScores[i] = 1f;
                totalScore = Roots.Length;
            }

            // Distribute leaf slots proportional to score
            var slotsPerRoot = new int[Roots.Length];
            int assigned = 0;
            for (int i = 0; i < Roots.Length; i++)
            {
                if (rootScores[i] <= 0f) continue;
                slotsPerRoot[i] = Math.Max(1, (int)Math.Round(count * rootScores[i] / totalScore));
                assigned += slotsPerRoot[i];
            }

            while (assigned > count)
            {
                int maxIdx = -1;
                int maxSlots = 0;
                for (int i = 0; i < Roots.Length; i++)
                {
                    if (slotsPerRoot[i] > maxSlots) { maxSlots = slotsPerRoot[i]; maxIdx = i; }
                }
                if (maxIdx < 0) break;
                slotsPerRoot[maxIdx]--;
                assigned--;
            }

            while (assigned < count)
            {
                int bestIdx = -1;
                float bestS = -1f;
                for (int i = 0; i < Roots.Length; i++)
                {
                    if (rootScores[i] > bestS && GetChildren(Roots[i].Id).Count > 0)
                    { bestS = rootScores[i]; bestIdx = i; }
                }
                if (bestIdx < 0) break;
                slotsPerRoot[bestIdx]++;
                assigned++;
            }

            // Pick leaves from each root using seed
            var result = new List<CultureNode>(count);
            uint rng = (uint)seed;

            for (int ri = 0; ri < Roots.Length; ri++)
            {
                if (slotsPerRoot[ri] <= 0) continue;
                var children = GetChildren(Roots[ri].Id);
                if (children.Count == 0) continue;

                var shuffled = new List<CultureNode>(children);
                rng = Xorshift(rng ^ (uint)(ri * 7919));
                for (int i = shuffled.Count - 1; i > 0; i--)
                {
                    rng = Xorshift(rng);
                    int j = (int)(rng % (uint)(i + 1));
                    var tmp = shuffled[i];
                    shuffled[i] = shuffled[j];
                    shuffled[j] = tmp;
                }

                for (int s = 0; s < slotsPerRoot[ri]; s++)
                    result.Add(shuffled[s % shuffled.Count]);
            }

            while (result.Count > count)
                result.RemoveAt(result.Count - 1);

            while (result.Count < count)
                result.Add(result[result.Count % Math.Max(1, Leaves.Length)]);

            return result.ToArray();
        }

        static uint Xorshift(uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state == 0 ? 1u : state;
        }

        static CultureNode[] BuildNodes()
        {
            return new[]
            {
                // Norse family (polar) — Partition + Cognatic
                new CultureNode { Id = "norse", DisplayName = "Norse", ParentId = null, CultureTypeIndex = 1, EquatorProximity = 0.15f, LongitudeBand = 0.3f, IsLeaf = false, SuccessionLaw = SuccessionLaw.Partition, GenderLaw = GenderLaw.Cognatic },
                new CultureNode { Id = "norse.icelandic", DisplayName = "Icelandic", ParentId = "norse", CultureTypeIndex = 1, EquatorProximity = 0.15f, IsLeaf = true, SuccessionLaw = SuccessionLaw.Partition, GenderLaw = GenderLaw.Cognatic },
                new CultureNode { Id = "norse.danish", DisplayName = "Danish", ParentId = "norse", CultureTypeIndex = 2, EquatorProximity = 0.15f, IsLeaf = true, SuccessionLaw = SuccessionLaw.Primogeniture, GenderLaw = GenderLaw.Cognatic },
                new CultureNode { Id = "norse.finnish", DisplayName = "Finnish", ParentId = "norse", CultureTypeIndex = 0, EquatorProximity = 0.15f, IsLeaf = true, SuccessionLaw = SuccessionLaw.Partition, GenderLaw = GenderLaw.Cognatic },

                // Celtic family — Elective + Cognatic
                new CultureNode { Id = "celtic", DisplayName = "Celtic", ParentId = null, CultureTypeIndex = 3, EquatorProximity = 0.30f, LongitudeBand = 0.2f, IsLeaf = false, SuccessionLaw = SuccessionLaw.Elective, GenderLaw = GenderLaw.Cognatic },
                new CultureNode { Id = "celtic.welsh", DisplayName = "Welsh", ParentId = "celtic", CultureTypeIndex = 3, EquatorProximity = 0.30f, IsLeaf = true, SuccessionLaw = SuccessionLaw.Elective, GenderLaw = GenderLaw.Cognatic },
                new CultureNode { Id = "celtic.gaelic", DisplayName = "Gaelic", ParentId = "celtic", CultureTypeIndex = 4, EquatorProximity = 0.30f, IsLeaf = true, SuccessionLaw = SuccessionLaw.Elective, GenderLaw = GenderLaw.Cognatic },
                new CultureNode { Id = "celtic.brythonic", DisplayName = "Brythonic", ParentId = "celtic", CultureTypeIndex = 5, EquatorProximity = 0.30f, IsLeaf = true, SuccessionLaw = SuccessionLaw.Partition, GenderLaw = GenderLaw.Cognatic },

                // Germanic family — Primogeniture + Agnatic
                new CultureNode { Id = "germanic", DisplayName = "Germanic", ParentId = null, CultureTypeIndex = 6, EquatorProximity = 0.40f, LongitudeBand = 0.4f, IsLeaf = false, SuccessionLaw = SuccessionLaw.Primogeniture, GenderLaw = GenderLaw.Agnatic },
                new CultureNode { Id = "germanic.lowland", DisplayName = "Lowland", ParentId = "germanic", CultureTypeIndex = 6, EquatorProximity = 0.40f, IsLeaf = true, SuccessionLaw = SuccessionLaw.Primogeniture, GenderLaw = GenderLaw.Agnatic },
                new CultureNode { Id = "germanic.highland", DisplayName = "Highland", ParentId = "germanic", CultureTypeIndex = 7, EquatorProximity = 0.40f, IsLeaf = true, SuccessionLaw = SuccessionLaw.Primogeniture, GenderLaw = GenderLaw.Agnatic },
                new CultureNode { Id = "germanic.coastal", DisplayName = "Coastal", ParentId = "germanic", CultureTypeIndex = 8, EquatorProximity = 0.40f, IsLeaf = true, SuccessionLaw = SuccessionLaw.Primogeniture, GenderLaw = GenderLaw.Agnatic },

                // Uralic family (cold) — Seniority + Cognatic
                new CultureNode { Id = "uralic", DisplayName = "Uralic", ParentId = null, CultureTypeIndex = 0, EquatorProximity = 0.20f, LongitudeBand = 0.6f, IsLeaf = false, SuccessionLaw = SuccessionLaw.Seniority, GenderLaw = GenderLaw.Cognatic },
                new CultureNode { Id = "uralic.western", DisplayName = "Western Uralic", ParentId = "uralic", CultureTypeIndex = 0, EquatorProximity = 0.20f, IsLeaf = true, SuccessionLaw = SuccessionLaw.Seniority, GenderLaw = GenderLaw.Cognatic },
                new CultureNode { Id = "uralic.eastern", DisplayName = "Eastern Uralic", ParentId = "uralic", CultureTypeIndex = 12, EquatorProximity = 0.20f, IsLeaf = true, SuccessionLaw = SuccessionLaw.Seniority, GenderLaw = GenderLaw.Cognatic },

                // Balto-Slavic family (temperate) — Partition + Agnatic
                new CultureNode { Id = "balto-slavic", DisplayName = "Balto-Slavic", ParentId = null, CultureTypeIndex = 9, EquatorProximity = 0.50f, LongitudeBand = 0.7f, IsLeaf = false, SuccessionLaw = SuccessionLaw.Partition, GenderLaw = GenderLaw.Agnatic },
                new CultureNode { Id = "balto-slavic.northern", DisplayName = "Northern Slavic", ParentId = "balto-slavic", CultureTypeIndex = 9, EquatorProximity = 0.50f, IsLeaf = true, SuccessionLaw = SuccessionLaw.Partition, GenderLaw = GenderLaw.Agnatic },
                new CultureNode { Id = "balto-slavic.southern", DisplayName = "Southern Slavic", ParentId = "balto-slavic", CultureTypeIndex = 10, EquatorProximity = 0.50f, IsLeaf = true, SuccessionLaw = SuccessionLaw.Partition, GenderLaw = GenderLaw.Agnatic },
                new CultureNode { Id = "balto-slavic.coastal", DisplayName = "Baltic", ParentId = "balto-slavic", CultureTypeIndex = 11, EquatorProximity = 0.50f, IsLeaf = true, SuccessionLaw = SuccessionLaw.Seniority, GenderLaw = GenderLaw.Agnatic },
            };
        }
    }
}
