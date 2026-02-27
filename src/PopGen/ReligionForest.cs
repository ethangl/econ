using System;
using System.Collections.Generic;

namespace PopGen.Core
{
    /// <summary>
    /// Tree queries for religions. Unlike cultures, religions have inverted sibling dynamics:
    /// closer tree distance = more hostile (heresy), cross-tree = mild (foreign).
    /// Operates on per-world religion arrays (not static data).
    /// </summary>
    public static class ReligionForest
    {
        /// <summary>
        /// Hop count via LCA. Same = 0, parent-child = 1, siblings = 2.
        /// Cross-tree (different roots or no common ancestor) = int.MaxValue.
        /// </summary>
        public static int TreeDistance(int a, int b, PopReligion[] religions)
        {
            if (a == b) return 0;

            var byId = BuildLookup(religions);
            if (!byId.TryGetValue(a, out var nodeA) || !byId.TryGetValue(b, out var nodeB))
                return int.MaxValue;

            int rootA = GetRootId(nodeA, byId);
            int rootB = GetRootId(nodeB, byId);
            if (rootA != rootB) return int.MaxValue;

            // Collect ancestors of A with depth
            var ancestorsA = new Dictionary<int, int>();
            int depth = 0;
            var current = nodeA;
            while (current != null)
            {
                ancestorsA[current.Id] = depth;
                depth++;
                current = current.ParentId != 0 && byId.TryGetValue(current.ParentId, out var p) ? p : null;
            }

            // Walk up from B until we hit an ancestor of A
            depth = 0;
            current = nodeB;
            while (current != null)
            {
                if (ancestorsA.TryGetValue(current.Id, out int depthA))
                    return depthA + depth;
                depth++;
                current = current.ParentId != 0 && byId.TryGetValue(current.ParentId, out var p) ? p : null;
            }

            return int.MaxValue;
        }

        /// <summary>
        /// Inverted hostility: closer tree distance = more hostile.
        ///   Same religion     → 0.0
        ///   Sibling (dist=2)  → 1.0  (heresy)
        ///   Cousin (dist=3-4) → 0.7  (wrong branch)
        ///   Unrelated (cross) → 0.3  (foreign)
        /// </summary>
        public static float Hostility(int a, int b, PopReligion[] religions)
        {
            if (a == b) return 0f;

            int dist = TreeDistance(a, b, religions);
            if (dist == int.MaxValue) return 0.3f;
            if (dist <= 2) return 1.0f;
            if (dist <= 4) return 0.7f;
            return 0.3f;
        }

        static Dictionary<int, PopReligion> BuildLookup(PopReligion[] religions)
        {
            var byId = new Dictionary<int, PopReligion>(religions.Length);
            for (int i = 0; i < religions.Length; i++)
                byId[religions[i].Id] = religions[i];
            return byId;
        }

        static int GetRootId(PopReligion node, Dictionary<int, PopReligion> byId)
        {
            var current = node;
            while (current.ParentId != 0)
            {
                if (!byId.TryGetValue(current.ParentId, out current))
                    break;
            }
            return current.Id;
        }
    }
}
