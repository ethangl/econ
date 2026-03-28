using System;
using System.Collections.Generic;

namespace WorldGen.Core
{
    /// <summary>
    /// Exact nearest-neighbor lookup for cell centers using a 3D kd-tree.
    /// Lower cell indices win ties to preserve brute-force behavior.
    /// </summary>
    internal sealed class NearestCellLookup
    {
        struct Node
        {
            public int PointIndex;
            public int Axis;
            public int Left;
            public int Right;
        }

        readonly Vec3[] _points;
        readonly Node[] _nodes;
        readonly int _root;
        int _nodeCount;

        public NearestCellLookup(Vec3[] points)
        {
            _points = points ?? throw new ArgumentNullException(nameof(points));
            if (points.Length == 0)
                throw new ArgumentException("Need at least one point.", nameof(points));

            _nodes = new Node[points.Length];
            int[] indices = new int[points.Length];
            for (int i = 0; i < indices.Length; i++)
                indices[i] = i;

            _root = Build(indices, 0, indices.Length, 0);
        }

        public int Nearest(in Vec3 query)
        {
            int bestIndex = 0;
            float bestDist = float.MaxValue;
            Search(_root, query, ref bestIndex, ref bestDist);
            return bestIndex;
        }

        int Build(int[] indices, int start, int length, int depth)
        {
            if (length <= 0)
                return -1;

            int axis = depth % 3;
            Array.Sort(indices, start, length, new AxisComparer(_points, axis));

            int mid = start + length / 2;
            int nodeIndex = _nodeCount++;
            _nodes[nodeIndex] = new Node
            {
                PointIndex = indices[mid],
                Axis = axis,
                Left = Build(indices, start, mid - start, depth + 1),
                Right = Build(indices, mid + 1, start + length - (mid + 1), depth + 1),
            };

            return nodeIndex;
        }

        void Search(int nodeIndex, in Vec3 query, ref int bestIndex, ref float bestDist)
        {
            if (nodeIndex < 0)
                return;

            ref readonly Node node = ref _nodes[nodeIndex];
            Vec3 point = _points[node.PointIndex];
            float dist = Vec3.SqrDistance(query, point);
            if (dist < bestDist || (dist == bestDist && node.PointIndex < bestIndex))
            {
                bestDist = dist;
                bestIndex = node.PointIndex;
            }

            float delta = GetAxis(query, node.Axis) - GetAxis(point, node.Axis);
            int near = delta <= 0f ? node.Left : node.Right;
            int far = delta <= 0f ? node.Right : node.Left;

            Search(near, query, ref bestIndex, ref bestDist);

            float planeDist = delta * delta;
            if (planeDist <= bestDist)
                Search(far, query, ref bestIndex, ref bestDist);
        }

        static float GetAxis(in Vec3 value, int axis) => axis switch
        {
            0 => value.X,
            1 => value.Y,
            _ => value.Z,
        };

        sealed class AxisComparer : IComparer<int>
        {
            readonly Vec3[] _points;
            readonly int _axis;

            public AxisComparer(Vec3[] points, int axis)
            {
                _points = points;
                _axis = axis;
            }

            public int Compare(int x, int y)
            {
                float dx = GetAxis(_points[x], _axis);
                float dy = GetAxis(_points[y], _axis);
                int cmp = dx.CompareTo(dy);
                return cmp != 0 ? cmp : x.CompareTo(y);
            }
        }
    }
}
