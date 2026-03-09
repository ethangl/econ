using System;
using System.Collections.Generic;
using MapGen.Core;
using UnityEngine;

namespace EconSim.Mapgen4
{
    internal static class Mapgen4Constants
    {
        public const float Spacing = 5.5f;
        public const float MountainSpacing = 35f;
        public const int MeshSeed = 12345;
        public const int ConstraintSize = 128;
        public const float MapSize = 1000f;
    }

    internal sealed class Mapgen4Random
    {
        uint _state;

        public Mapgen4Random(int seed)
        {
            _state = (uint)(seed == 0 ? 0x6d2b79f5 : seed);
        }

        public float NextFloat()
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return (_state & 0x00FFFFFF) / 16777216f;
        }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            return minInclusive + Mathf.FloorToInt(NextFloat() * (maxExclusive - minInclusive));
        }
    }

    internal sealed class Mapgen4SimplexNoise
    {
        static readonly int[][] Gradients =
        {
            new[] { 1, 1 }, new[] { -1, 1 }, new[] { 1, -1 }, new[] { -1, -1 },
            new[] { 1, 0 }, new[] { -1, 0 }, new[] { 1, 0 }, new[] { -1, 0 },
            new[] { 0, 1 }, new[] { 0, -1 }, new[] { 0, 1 }, new[] { 0, -1 },
        };

        readonly short[] _perm = new short[512];
        readonly short[] _permMod12 = new short[512];

        public Mapgen4SimplexNoise(Mapgen4Random rng)
        {
            var p = new short[256];
            for (short i = 0; i < 256; i++)
            {
                p[i] = i;
            }

            for (int i = 255; i >= 0; i--)
            {
                int j = rng.NextInt(0, i + 1);
                short swap = p[i];
                p[i] = p[j];
                p[j] = swap;
            }

            for (int i = 0; i < 512; i++)
            {
                _perm[i] = p[i & 255];
                _permMod12[i] = (short)(_perm[i] % 12);
            }
        }

        public float Sample(float xin, float yin)
        {
            const float F2 = 0.3660254037844386f;
            const float G2 = 0.21132486540518713f;

            float s = (xin + yin) * F2;
            int i = FastFloor(xin + s);
            int j = FastFloor(yin + s);
            float t = (i + j) * G2;
            float x0 = xin - (i - t);
            float y0 = yin - (j - t);

            int i1;
            int j1;
            if (x0 > y0)
            {
                i1 = 1;
                j1 = 0;
            }
            else
            {
                i1 = 0;
                j1 = 1;
            }

            float x1 = x0 - i1 + G2;
            float y1 = y0 - j1 + G2;
            float x2 = x0 - 1f + 2f * G2;
            float y2 = y0 - 1f + 2f * G2;

            int ii = i & 255;
            int jj = j & 255;
            int gi0 = _permMod12[ii + _perm[jj]];
            int gi1 = _permMod12[ii + i1 + _perm[jj + j1]];
            int gi2 = _permMod12[ii + 1 + _perm[jj + 1]];

            float n0 = Contribution(gi0, x0, y0);
            float n1 = Contribution(gi1, x1, y1);
            float n2 = Contribution(gi2, x2, y2);
            return 70f * (n0 + n1 + n2);
        }

        static float Contribution(int gradientIndex, float x, float y)
        {
            float t = 0.5f - x * x - y * y;
            if (t < 0f)
            {
                return 0f;
            }

            t *= t;
            int[] grad = Gradients[gradientIndex];
            return t * t * (grad[0] * x + grad[1] * y);
        }

        static int FastFloor(float value)
        {
            return value >= 0 ? (int)value : (int)value - 1;
        }
    }

    internal sealed class Mapgen4PoissonDisk
    {
        readonly Vector2 _size;
        readonly float _radius;
        readonly float _radiusSquared;
        readonly int _tries;
        readonly float _cellSize;
        readonly int _gridWidth;
        readonly int _gridHeight;
        readonly List<Vector2> _points = new List<Vector2>();
        readonly List<int> _active = new List<int>();
        readonly int[] _grid;
        readonly Mapgen4Random _rng;

        public Mapgen4PoissonDisk(Vector2 size, float radius, int tries, Mapgen4Random rng)
        {
            _size = size;
            _radius = radius;
            _radiusSquared = radius * radius;
            _tries = tries;
            _rng = rng;
            _cellSize = radius / Mathf.Sqrt(2f);
            _gridWidth = Mathf.CeilToInt(size.x / _cellSize);
            _gridHeight = Mathf.CeilToInt(size.y / _cellSize);
            _grid = new int[_gridWidth * _gridHeight];
            Array.Fill(_grid, -1);
        }

        public bool AddPoint(Vector2 point)
        {
            if (!IsValid(point))
            {
                return false;
            }

            int index = _points.Count;
            _points.Add(point);
            _active.Add(index);
            GridIndex(point, out int gx, out int gy);
            _grid[gy * _gridWidth + gx] = index;
            return true;
        }

        public List<Vector2> Fill()
        {
            while (_active.Count > 0)
            {
                int activeIndex = _rng.NextInt(0, _active.Count);
                int pointIndex = _active[activeIndex];
                Vector2 basePoint = _points[pointIndex];
                bool found = false;

                for (int i = 0; i < _tries; i++)
                {
                    float angle = _rng.NextFloat() * Mathf.PI * 2f;
                    float distance = _radius * (1f + _rng.NextFloat());
                    Vector2 candidate = basePoint + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
                    if (!Contains(candidate) || !IsValid(candidate))
                    {
                        continue;
                    }

                    AddPoint(candidate);
                    found = true;
                    break;
                }

                if (!found)
                {
                    int last = _active.Count - 1;
                    _active[activeIndex] = _active[last];
                    _active.RemoveAt(last);
                }
            }

            return _points;
        }

        bool Contains(Vector2 point)
        {
            return point.x >= 0f && point.x < _size.x && point.y >= 0f && point.y < _size.y;
        }

        bool IsValid(Vector2 point)
        {
            if (!Contains(point))
            {
                return false;
            }

            GridIndex(point, out int gx, out int gy);
            int minX = Mathf.Max(0, gx - 2);
            int maxX = Mathf.Min(_gridWidth - 1, gx + 2);
            int minY = Mathf.Max(0, gy - 2);
            int maxY = Mathf.Min(_gridHeight - 1, gy + 2);
            for (int y = minY; y <= maxY; y++)
            {
                int row = y * _gridWidth;
                for (int x = minX; x <= maxX; x++)
                {
                    int otherIndex = _grid[row + x];
                    if (otherIndex < 0)
                    {
                        continue;
                    }

                    if ((_points[otherIndex] - point).sqrMagnitude < _radiusSquared)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        void GridIndex(Vector2 point, out int gx, out int gy)
        {
            gx = Mathf.Clamp((int)(point.x / _cellSize), 0, _gridWidth - 1);
            gy = Mathf.Clamp((int)(point.y / _cellSize), 0, _gridHeight - 1);
        }
    }

    internal sealed class Mapgen4TriangleMesh
    {
        public int NumSides { get; private set; }
        public int NumSolidSides { get; private set; }
        public int NumRegions { get; private set; }
        public int NumSolidRegions { get; private set; }
        public int NumTriangles { get; private set; }
        public int NumSolidTriangles { get; private set; }
        public int NumBoundaryRegions { get; private set; }

        int[] _halfedges;
        int[] _triangles;
        int[] _sOfR;
        readonly List<Vector2> _vertexT = new List<Vector2>();
        Vector2[] _vertexR;

        public bool[] IsBoundaryT;
        public float[] LengthS;

        public Mapgen4TriangleMesh(Vector2[] points, int[] triangles, int[] halfedges, int numBoundaryPoints, int numSolidSides)
        {
            NumBoundaryRegions = numBoundaryPoints;
            NumSolidSides = numSolidSides;
            _vertexR = points;
            _triangles = triangles;
            _halfedges = halfedges;
            UpdateDerivedData();
        }

        public static Mapgen4TriangleMesh Create(Vector2[] points, int[] triangles, int[] halfedges, int numBoundaryPoints)
        {
            int numSolidSides = triangles.Length;
            int numUnpairedSides = 0;
            int firstUnpairedSide = -1;
            var unpairedSideByRegion = new Dictionary<int, int>();
            for (int s = 0; s < numSolidSides; s++)
            {
                if (halfedges[s] != -1)
                {
                    continue;
                }

                numUnpairedSides++;
                unpairedSideByRegion[triangles[s]] = s;
                firstUnpairedSide = s;
            }

            int ghostRegion = points.Length;
            var expandedPoints = new Vector2[points.Length + 1];
            Array.Copy(points, expandedPoints, points.Length);
            expandedPoints[ghostRegion] = Vector2.zero;

            int[] expandedTriangles = new int[numSolidSides + 3 * numUnpairedSides];
            int[] expandedHalfedges = new int[expandedTriangles.Length];
            Array.Copy(triangles, expandedTriangles, triangles.Length);
            Array.Copy(halfedges, expandedHalfedges, halfedges.Length);

            for (int i = 0, s = firstUnpairedSide; i < numUnpairedSides; i++, s = unpairedSideByRegion[expandedTriangles[NextSide(s)]])
            {
                int ghostSide = numSolidSides + 3 * i;
                expandedHalfedges[s] = ghostSide;
                expandedHalfedges[ghostSide] = s;
                expandedTriangles[ghostSide] = expandedTriangles[NextSide(s)];
                expandedTriangles[ghostSide + 1] = expandedTriangles[s];
                expandedTriangles[ghostSide + 2] = ghostRegion;

                int k = numSolidSides + (3 * i + 4) % (3 * numUnpairedSides);
                expandedHalfedges[ghostSide + 2] = k;
                expandedHalfedges[k] = ghostSide + 2;
            }

            return new Mapgen4TriangleMesh(expandedPoints, expandedTriangles, expandedHalfedges, numBoundaryPoints, numSolidSides);
        }

        void UpdateDerivedData()
        {
            NumSides = _triangles.Length;
            NumRegions = _vertexR.Length;
            NumSolidRegions = NumRegions - 1;
            NumTriangles = NumSides / 3;
            NumSolidTriangles = NumSolidSides / 3;

            while (_vertexT.Count < NumTriangles)
            {
                _vertexT.Add(Vector2.zero);
            }

            _sOfR = new int[NumRegions];
            for (int s = 0; s < _triangles.Length; s++)
            {
                int endpoint = _triangles[NextSide(s)];
                if (_sOfR[endpoint] == 0 || _halfedges[s] == -1)
                {
                    _sOfR[endpoint] = s;
                }
            }

            for (int s = 0; s < _triangles.Length; s += 3)
            {
                int t = s / 3;
                Vector2 a = _vertexR[_triangles[s]];
                Vector2 b = _vertexR[_triangles[s + 1]];
                Vector2 c = _vertexR[_triangles[s + 2]];
                if (IsGhostSide(s))
                {
                    Vector2 delta = b - a;
                    float scale = 10f / delta.magnitude;
                    _vertexT[t] = 0.5f * (a + b) + new Vector2(delta.y, -delta.x) * scale;
                }
                else
                {
                    _vertexT[t] = (a + b + c) / 3f;
                }
            }

            IsBoundaryT = new bool[NumTriangles];
            for (int t = 0; t < NumTriangles; t++)
            {
                int s = 3 * t;
                IsBoundaryT[t] = IsBoundaryRegion(_triangles[s]) || IsBoundaryRegion(_triangles[s + 1]) || IsBoundaryRegion(_triangles[s + 2]);
            }

            LengthS = new float[NumSides];
            for (int s = 0; s < NumSides; s++)
            {
                int r1 = RegionBegin(s);
                int r2 = RegionEnd(s);
                LengthS[s] = Vector2.Distance(_vertexR[r1], _vertexR[r2]);
            }
        }

        public float XOfRegion(int r) => _vertexR[r].x;
        public float YOfRegion(int r) => _vertexR[r].y;
        public float XOfTriangle(int t) => _vertexT[t].x;
        public float YOfTriangle(int t) => _vertexT[t].y;
        public int RegionBegin(int s) => _triangles[s];
        public int RegionEnd(int s) => _triangles[NextSide(s)];
        public int TriangleInner(int s) => s / 3;
        public int TriangleOuter(int s) => _halfedges[s] / 3;
        public int OppositeSide(int s) => _halfedges[s];
        public int SideOfRegion(int r) => _sOfR[r];
        public bool IsGhostSide(int s) => s >= NumSolidSides;
        public bool IsGhostRegion(int r) => r == NumRegions - 1;
        public bool IsBoundaryRegion(int r) => r >= 0 && r < NumBoundaryRegions;

        public static int NextSide(int s) => (s % 3 == 2) ? s - 2 : s + 1;
        public static int PrevSide(int s) => (s % 3 == 0) ? s + 2 : s - 1;

        public IEnumerable<int> RegionsAroundTriangle(int t)
        {
            int s = 3 * t;
            yield return _triangles[s];
            yield return _triangles[s + 1];
            yield return _triangles[s + 2];
        }
    }

    internal sealed class Mapgen4Constraints
    {
        public float[] Elevation;
        public int Size;

        public static Mapgen4Constraints Generate(int seed, float island)
        {
            var noise = new Mapgen4SimplexNoise(new Mapgen4Random(seed));
            int size = Mapgen4Constants.ConstraintSize;
            float[] elevation = new float[size * size];
            float persistence = 0.5f;
            float[] amplitudes = new float[5];
            for (int octave = 0; octave < amplitudes.Length; octave++)
            {
                amplitudes[octave] = Mathf.Pow(persistence, octave);
            }

            float Fbm(float nx, float ny)
            {
                float sum = 0f;
                float amplitudeSum = 0f;
                for (int octave = 0; octave < amplitudes.Length; octave++)
                {
                    float frequency = 1 << octave;
                    sum += amplitudes[octave] * noise.Sample(nx * frequency, ny * frequency);
                    amplitudeSum += amplitudes[octave];
                }
                return sum / amplitudeSum;
            }

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int p = y * size + x;
                    float nx = 2f * x / size - 1f;
                    float ny = 2f * y / size - 1f;
                    float distance = Mathf.Max(Mathf.Abs(nx), Mathf.Abs(ny));
                    float e = 0.5f * (Fbm(nx, ny) + island * (0.75f - 2f * distance * distance));
                    e = Mathf.Clamp(e, -1f, 1f);
                    elevation[p] = e;
                    if (e > 0f)
                    {
                        float m = 0.5f * noise.Sample(nx + 30f, ny + 50f) + 0.5f * noise.Sample(2f * nx + 33f, 2f * ny + 55f);
                        float mountain = Mathf.Min(1f, e * 5f) * (1f - Mathf.Abs(m) / 0.5f);
                        if (mountain > 0f)
                        {
                            elevation[p] = Mathf.Max(e, Mathf.Min(e * 3f, mountain));
                        }
                    }
                }
            }

            return new Mapgen4Constraints { Elevation = elevation, Size = size };
        }
    }

    internal sealed class Mapgen4Map
    {
        readonly struct PrecomputedNoise
        {
            public readonly float[] Noise0;
            public readonly float[] Noise1;
            public readonly float[] Noise2;
            public readonly float[] Noise4;
            public readonly float[] Noise5;
            public readonly float[] Noise6;

            public PrecomputedNoise(int count)
            {
                Noise0 = new float[count];
                Noise1 = new float[count];
                Noise2 = new float[count];
                Noise4 = new float[count];
                Noise5 = new float[count];
                Noise6 = new float[count];
            }
        }

        const float MountainSlope = 16f;

        readonly Mapgen4TriangleMesh _mesh;
        readonly int[] _trianglePeaks;
        readonly float _spacing;
        int _seed = int.MinValue;
        float _mountainJaggedness = float.NegativeInfinity;
        float _windAngleDegrees = float.PositiveInfinity;
        PrecomputedNoise _noise;

        public readonly float[] ElevationT;
        public readonly float[] ElevationR;
        public readonly float[] HumidityR;
        public readonly float[] MoistureT;
        public readonly float[] RainfallR;
        public readonly int[] DownslopeSideT;
        public readonly int[] TriangleOrder;
        public readonly float[] FlowT;
        public readonly float[] FlowS;
        public readonly int[] WindOrderR;
        public readonly float[] WindSortR;
        public readonly float[] MountainDistanceT;

        internal Mapgen4TriangleMesh Mesh => _mesh;

        public Mapgen4Map(Mapgen4TriangleMesh mesh, int[] trianglePeaks, float spacing)
        {
            _mesh = mesh;
            _trianglePeaks = trianglePeaks;
            _spacing = spacing;
            ElevationT = new float[mesh.NumTriangles];
            ElevationR = new float[mesh.NumRegions];
            HumidityR = new float[mesh.NumRegions];
            MoistureT = new float[mesh.NumTriangles];
            RainfallR = new float[mesh.NumRegions];
            DownslopeSideT = new int[mesh.NumTriangles];
            TriangleOrder = new int[mesh.NumTriangles];
            FlowT = new float[mesh.NumTriangles];
            FlowS = new float[mesh.NumSides];
            WindOrderR = new int[mesh.NumRegions];
            WindSortR = new float[mesh.NumRegions];
            MountainDistanceT = new float[mesh.NumTriangles];
        }

        public void AssignElevation(Mapgen4Controller.ElevationParams parameters, Mapgen4Constraints constraints)
        {
            if (_seed != parameters.Seed || !Mathf.Approximately(_mountainJaggedness, parameters.MountainJagged))
            {
                _mountainJaggedness = parameters.MountainJagged;
                CalculateMountainDistance(_mesh, _trianglePeaks, _spacing, _mountainJaggedness, new Mapgen4Random(parameters.Seed), MountainDistanceT);
            }

            if (_seed != parameters.Seed)
            {
                _seed = parameters.Seed;
                _noise = PrecalculateNoise(new Mapgen4Random(parameters.Seed), _mesh);
            }

            AssignTriangleElevation(parameters, constraints);
            AssignRegionElevation();
        }

        public void AssignRainfall(Mapgen4Controller.BiomesParams parameters)
        {
            if (!Mathf.Approximately(parameters.WindAngleDeg, _windAngleDegrees))
            {
                _windAngleDegrees = parameters.WindAngleDeg;
                float radians = Mathf.Deg2Rad * _windAngleDegrees;
                Vector2 wind = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
                for (int r = 0; r < _mesh.NumRegions; r++)
                {
                    WindOrderR[r] = r;
                    WindSortR[r] = _mesh.XOfRegion(r) * wind.x + _mesh.YOfRegion(r) * wind.y;
                }
                Array.Sort(WindOrderR, (a, b) => WindSortR[a].CompareTo(WindSortR[b]));
            }

            foreach (int region in WindOrderR)
            {
                int count = 0;
                float sum = 0f;
                int s0 = _mesh.SideOfRegion(region);
                int incoming = s0;
                do
                {
                    int neighbor = _mesh.RegionBegin(incoming);
                    if (WindSortR[neighbor] < WindSortR[region])
                    {
                        count++;
                        sum += HumidityR[neighbor];
                    }
                    int outgoing = Mapgen4TriangleMesh.NextSide(incoming);
                    incoming = _mesh.OppositeSide(outgoing);
                }
                while (incoming != s0);

                float humidity = 0f;
                float rainfall = 0f;
                if (count > 0)
                {
                    humidity = sum / count;
                    rainfall += parameters.Raininess * humidity;
                }
                if (_mesh.IsBoundaryRegion(region))
                {
                    humidity = 1f;
                }
                if (ElevationR[region] < 0f)
                {
                    float evaporation = parameters.Evaporation * -ElevationR[region];
                    humidity += evaporation;
                }
                if (humidity > 1f - ElevationR[region])
                {
                    float orographic = parameters.RainShadow * (humidity - (1f - ElevationR[region]));
                    rainfall += parameters.Raininess * orographic;
                    humidity -= orographic;
                }
                RainfallR[region] = rainfall;
                HumidityR[region] = humidity;
            }
        }

        public void AssignRivers(Mapgen4Controller.RiversParams parameters)
        {
            AssignDownslope(_mesh, ElevationT, DownslopeSideT, TriangleOrder);
            AssignMoisture(_mesh, RainfallR, MoistureT);
            AssignFlow(_mesh, parameters, TriangleOrder, ElevationT, MoistureT, DownslopeSideT, FlowT, FlowS);
        }

        void AssignTriangleElevation(Mapgen4Controller.ElevationParams parameters, Mapgen4Constraints constraints)
        {
            float ConstraintAt(float x, float y)
            {
                float[] data = constraints.Elevation;
                int size = constraints.Size;
                x = Mapgen4Geometry.Clamp(x * (size - 1), 0f, size - 2);
                y = Mapgen4Geometry.Clamp(y * (size - 1), 0f, size - 2);
                int xInt = Mathf.FloorToInt(x);
                int yInt = Mathf.FloorToInt(y);
                float xFrac = x - xInt;
                float yFrac = y - yInt;
                int p = size * yInt + xInt;
                float e00 = data[p];
                float e01 = data[p + 1];
                float e10 = data[p + size];
                float e11 = data[p + size + 1];
                return ((e00 * (1f - xFrac) + e01 * xFrac) * (1f - yFrac)
                        + (e10 * (1f - xFrac) + e11 * xFrac) * yFrac);
            }

            for (int t = 0; t < _mesh.NumSolidTriangles; t++)
            {
                float e = ConstraintAt(_mesh.XOfTriangle(t) / Mapgen4Constants.MapSize, _mesh.YOfTriangle(t) / Mapgen4Constants.MapSize);
                ElevationT[t] = e + parameters.NoisyCoastlines * (1f - e * e * e * e) * (_noise.Noise4[t] + _noise.Noise5[t] * 0.5f + _noise.Noise6[t] * 0.25f);
            }

            float mountainSharpness = Mathf.Pow(2f, parameters.MountainSharpness);
            for (int t = 0; t < _mesh.NumTriangles; t++)
            {
                float e = ElevationT[t];
                if (e > 0f)
                {
                    float noisiness = 1f - 0.5f * (1f + _noise.Noise0[t]);
                    float hill = (1f + noisiness * _noise.Noise4[t] + (1f - noisiness) * _noise.Noise2[t]) * parameters.HillHeight;
                    if (hill < 0.01f)
                    {
                        hill = 0.01f;
                    }
                    float mountain = 1f - MountainSlope / mountainSharpness * MountainDistanceT[t];
                    if (mountain < 0.01f)
                    {
                        mountain = 0.01f;
                    }
                    float weight = e * e;
                    e = (1f - weight) * hill + weight * mountain;
                }
                else
                {
                    e *= parameters.OceanDepth + _noise.Noise1[t];
                }

                ElevationT[t] = Mathf.Clamp(e, -1f, 1f);
            }
        }

        void AssignRegionElevation()
        {
            for (int r = 0; r < _mesh.NumRegions; r++)
            {
                int count = 0;
                float e = 0f;
                bool water = false;
                int s0 = _mesh.SideOfRegion(r);
                int incoming = s0;
                do
                {
                    int triangle = incoming / 3;
                    e += ElevationT[triangle];
                    water |= ElevationT[triangle] < 0f;
                    int outgoing = Mapgen4TriangleMesh.NextSide(incoming);
                    incoming = _mesh.OppositeSide(outgoing);
                    count++;
                }
                while (incoming != s0);

                e /= count;
                if (water && e >= 0f)
                {
                    e = -0.001f;
                }
                ElevationR[r] = e;
            }
        }

        static void CalculateMountainDistance(Mapgen4TriangleMesh mesh, int[] peaks, float spacing, float jaggedness, Mapgen4Random rng, float[] distanceT)
        {
            Array.Fill(distanceT, -1f);
            var queue = new List<int>(peaks);
            for (int i = 0; i < queue.Count; i++)
            {
                int current = queue[i];
                for (int j = 0; j < 3; j++)
                {
                    int s = 3 * current + j;
                    int neighbor = mesh.TriangleOuter(s);
                    if (distanceT[neighbor] != -1f)
                    {
                        continue;
                    }
                    float increment = spacing * (1f + jaggedness * (rng.NextFloat() - rng.NextFloat()));
                    distanceT[neighbor] = distanceT[current] + increment;
                    queue.Add(neighbor);
                }
            }
        }

        static PrecomputedNoise PrecalculateNoise(Mapgen4Random rng, Mapgen4TriangleMesh mesh)
        {
            var simplex = new Mapgen4SimplexNoise(rng);
            var result = new PrecomputedNoise(mesh.NumTriangles);
            for (int t = 0; t < mesh.NumTriangles; t++)
            {
                float nx = (mesh.XOfTriangle(t) - 500f) / 500f;
                float ny = (mesh.YOfTriangle(t) - 500f) / 500f;
                result.Noise0[t] = simplex.Sample(nx, ny);
                result.Noise1[t] = simplex.Sample(2f * nx + 5f, 2f * ny + 5f);
                result.Noise2[t] = simplex.Sample(4f * nx + 7f, 4f * ny + 7f);
                result.Noise4[t] = simplex.Sample(16f * nx + 15f, 16f * ny + 15f);
                result.Noise5[t] = simplex.Sample(32f * nx + 31f, 32f * ny + 31f);
                result.Noise6[t] = simplex.Sample(64f * nx + 67f, 64f * ny + 67f);
            }
            return result;
        }

        static void AssignDownslope(Mapgen4TriangleMesh mesh, float[] elevationT, int[] downslopeSideT, int[] triangleOrder)
        {
            var queue = new Mapgen4PriorityQueue();
            int queueIn = 0;
            Array.Fill(downslopeSideT, -999);
            for (int t = 0; t < mesh.NumTriangles; t++)
            {
                if (elevationT[t] >= -0.1f)
                {
                    continue;
                }

                int bestSide = -1;
                float bestElevation = elevationT[t];
                for (int j = 0; j < 3; j++)
                {
                    int s = 3 * t + j;
                    float neighborElevation = elevationT[mesh.TriangleOuter(s)];
                    if (neighborElevation < bestElevation)
                    {
                        bestElevation = neighborElevation;
                        bestSide = s;
                    }
                }
                triangleOrder[queueIn++] = t;
                downslopeSideT[t] = bestSide;
                queue.Enqueue(t, elevationT[t]);
            }

            for (int queueOut = 0; queueOut < mesh.NumTriangles; queueOut++)
            {
                int current = queue.Dequeue();
                for (int j = 0; j < 3; j++)
                {
                    int s = 3 * current + j;
                    int neighbor = mesh.TriangleOuter(s);
                    if (downslopeSideT[neighbor] != -999)
                    {
                        continue;
                    }

                    downslopeSideT[neighbor] = mesh.OppositeSide(s);
                    triangleOrder[queueIn++] = neighbor;
                    queue.Enqueue(neighbor, elevationT[neighbor]);
                }
            }
        }

        static void AssignMoisture(Mapgen4TriangleMesh mesh, float[] rainfallR, float[] moistureT)
        {
            for (int t = 0; t < mesh.NumTriangles; t++)
            {
                float moisture = 0f;
                for (int i = 0; i < 3; i++)
                {
                    int s = 3 * t + i;
                    moisture += rainfallR[mesh.RegionBegin(s)] / 3f;
                }
                moistureT[t] = moisture;
            }
        }

        static void AssignFlow(Mapgen4TriangleMesh mesh, Mapgen4Controller.RiversParams parameters, int[] triangleOrder, float[] elevationT, float[] moistureT, int[] downslopeSideT, float[] flowT, float[] flowS)
        {
            Array.Fill(flowS, 0f);
            for (int t = 0; t < mesh.NumTriangles; t++)
            {
                flowT[t] = elevationT[t] >= 0f ? parameters.Flow * moistureT[t] * moistureT[t] : 0f;
            }

            for (int i = triangleOrder.Length - 1; i >= 0; i--)
            {
                int tributary = triangleOrder[i];
                int flowSide = downslopeSideT[tributary];
                if (flowSide < 0)
                {
                    continue;
                }

                int trunk = mesh.OppositeSide(flowSide) / 3;
                flowT[trunk] += flowT[tributary];
                flowS[flowSide] += flowT[tributary];
                if (elevationT[trunk] > elevationT[tributary] && elevationT[tributary] >= 0f)
                {
                    elevationT[trunk] = elevationT[tributary];
                }
            }
        }
    }

    internal sealed class Mapgen4PriorityQueue
    {
        readonly List<(int Value, float Priority)> _items = new List<(int, float)>();

        public void Enqueue(int value, float priority)
        {
            _items.Add((value, priority));
            int index = _items.Count - 1;
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (_items[parent].Priority <= _items[index].Priority)
                {
                    break;
                }
                (_items[parent], _items[index]) = (_items[index], _items[parent]);
                index = parent;
            }
        }

        public int Dequeue()
        {
            int value = _items[0].Value;
            int last = _items.Count - 1;
            _items[0] = _items[last];
            _items.RemoveAt(last);
            int index = 0;
            while (true)
            {
                int left = 2 * index + 1;
                int right = left + 1;
                if (left >= _items.Count)
                {
                    break;
                }

                int smallest = left;
                if (right < _items.Count && _items[right].Priority < _items[left].Priority)
                {
                    smallest = right;
                }
                if (_items[index].Priority <= _items[smallest].Priority)
                {
                    break;
                }
                (_items[index], _items[smallest]) = (_items[smallest], _items[index]);
                index = smallest;
            }
            return value;
        }
    }

    internal readonly struct Mapgen4VertexAttribute
    {
        public readonly Vector2 Position;
        public readonly Vector2 ElevationRainfall;

        public Mapgen4VertexAttribute(Vector2 position, Vector2 elevationRainfall)
        {
            Position = position;
            ElevationRainfall = elevationRainfall;
        }
    }

    internal readonly struct Mapgen4RiverVertex
    {
        public readonly Vector2 Position;
        public readonly Vector2 Widths;
        public readonly Vector3 Barycentric;

        public Mapgen4RiverVertex(Vector2 position, Vector2 widths, Vector3 barycentric)
        {
            Position = position;
            Widths = widths;
            Barycentric = barycentric;
        }
    }

    internal static class Mapgen4Geometry
    {
        public static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static Vector2[] BuildVertexPositions(Mapgen4TriangleMesh mesh)
        {
            var positions = new Vector2[mesh.NumRegions + mesh.NumTriangles];
            int p = 0;
            for (int r = 0; r < mesh.NumRegions; r++)
            {
                positions[p++] = mesh.IsGhostRegion(r) ? Vector2.zero : new Vector2(mesh.XOfRegion(r), mesh.YOfRegion(r));
            }
            for (int t = 0; t < mesh.NumTriangles; t++)
            {
                positions[p++] = new Vector2(mesh.XOfTriangle(t), mesh.YOfTriangle(t));
            }
            return positions;
        }

        public static int[] BuildLandIndices(Mapgen4Map map, float mountainFolds, out Vector2[] elevationRainfall)
        {
            Mapgen4TriangleMesh mesh = map.Mesh;
            elevationRainfall = new Vector2[mesh.NumRegions + mesh.NumTriangles];
            int p = 0;
            for (int r = 0; r < mesh.NumRegions; r++)
            {
                elevationRainfall[p++] = new Vector2(map.ElevationR[r], map.RainfallR[r]);
            }
            for (int t = 0; t < mesh.NumTriangles; t++)
            {
                float folded = (1f - mountainFolds * Mathf.Sqrt(Mathf.Max(0f, map.ElevationT[t]))) * map.ElevationT[t];
                int s0 = 3 * t;
                int r1 = mesh.RegionBegin(s0);
                int r2 = mesh.RegionBegin(s0 + 1);
                int r3 = mesh.RegionBegin(s0 + 2);
                float rainfall = (map.RainfallR[r1] + map.RainfallR[r2] + map.RainfallR[r3]) / 3f;
                elevationRainfall[p++] = new Vector2(folded, rainfall);
            }

            int[] indices = new int[3 * mesh.NumSolidSides];
            int i = 0;
            int regionOffset = mesh.NumRegions;
            for (int s = 0; s < mesh.NumSolidSides; s++)
            {
                int opposite = mesh.OppositeSide(s);
                int r1 = mesh.RegionBegin(s);
                int r2 = mesh.RegionBegin(opposite);
                int t1 = mesh.TriangleInner(s);
                int t2 = mesh.TriangleInner(opposite);
                bool valley = false;
                if (map.ElevationR[r1] < 0f || map.ElevationR[r2] < 0f) valley = true;
                if (map.FlowS[s] > 0f || map.FlowS[opposite] > 0f) valley = true;
                if (mesh.IsBoundaryT[t1] || mesh.IsBoundaryT[t2]) valley = false;

                if (valley)
                {
                    indices[i++] = r1;
                    indices[i++] = regionOffset + t2;
                    indices[i++] = regionOffset + t1;
                }
                else
                {
                    indices[i++] = r1;
                    indices[i++] = r2;
                    indices[i++] = regionOffset + t1;
                }
            }
            return indices;
        }

        public static Mapgen4RiverVertex[] BuildRiverVertices(Mapgen4TriangleMesh mesh, Mapgen4Map map, Mapgen4Controller.RiversParams parameters, float spacing)
        {
            float minFlow = Mathf.Exp(parameters.LogMinFlow);
            float riverWidth = Mathf.Exp(parameters.LogRiverWidth);
            float RiverSize(int side, float flow)
            {
                if (side < 0) return 1f;
                float width = Mathf.Sqrt(flow - minFlow) * spacing * riverWidth;
                return width / mesh.LengthS[side];
            }

            var result = new List<Mapgen4RiverVertex>(mesh.NumSolidTriangles * 4);
            Vector3[] barycentric =
            {
                new Vector3(1f, 0f, 0f),
                new Vector3(0f, 1f, 0f),
                new Vector3(0f, 0f, 1f),
            };

            void AddTriangle(int s1, int s2, int s3, float width1, float width2)
            {
                int r1 = mesh.RegionBegin(s1);
                int r2 = mesh.RegionBegin(s2);
                int r3 = mesh.RegionBegin(s3);
                result.Add(new Mapgen4RiverVertex(new Vector2(mesh.XOfRegion(r1), mesh.YOfRegion(r1)), new Vector2(width1, width2), barycentric[0]));
                result.Add(new Mapgen4RiverVertex(new Vector2(mesh.XOfRegion(r2), mesh.YOfRegion(r2)), new Vector2(width1, width2), barycentric[1]));
                result.Add(new Mapgen4RiverVertex(new Vector2(mesh.XOfRegion(r3), mesh.YOfRegion(r3)), new Vector2(width1, width2), barycentric[2]));
            }

            for (int t = 0; t < mesh.NumSolidTriangles; t++)
            {
                int sOut = map.DownslopeSideT[t];
                float outflow = sOut >= 0 ? map.FlowS[sOut] : 0f;
                if (sOut < 0 || outflow < minFlow)
                {
                    continue;
                }

                int sIn1 = Mapgen4TriangleMesh.NextSide(sOut);
                int sIn2 = Mapgen4TriangleMesh.NextSide(sIn1);
                float flowIn1 = map.FlowS[mesh.OppositeSide(sIn1)];
                float flowIn2 = map.FlowS[mesh.OppositeSide(sIn2)];
                if (flowIn1 >= minFlow)
                {
                    AddTriangle(sOut, sIn1, sIn2, RiverSize(sOut, outflow), RiverSize(sIn1, flowIn1));
                }
                if (flowIn2 >= minFlow)
                {
                    AddTriangle(sIn2, sOut, sIn1, RiverSize(sIn2, flowIn2), RiverSize(sOut, outflow));
                }
            }

            return result.ToArray();
        }
    }

    internal static class Mapgen4Colormap
    {
        public static Texture2D CreateTexture()
        {
            const int width = 64;
            const int height = 64;
            var pixels = new Color32[width * height];
            int p = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float e = 2f * x / width - 1f;
                    float m = (float)y / height;
                    float r;
                    float g;
                    float b;

                    if (x == width / 2 - 1)
                    {
                        r = 48; g = 120; b = 160;
                    }
                    else if (x == width / 2 - 2)
                    {
                        r = 48; g = 100; b = 150;
                    }
                    else if (x == width / 2 - 3)
                    {
                        r = 48; g = 80; b = 140;
                    }
                    else if (e < 0f)
                    {
                        r = 48 + 48 * e;
                        g = 64 + 64 * e;
                        b = 127 + 127 * e;
                    }
                    else
                    {
                        m *= 1f - e;
                        r = 210 - 100 * m;
                        g = 185 - 45 * m;
                        b = 139 - 45 * m;
                        r = 255 * e + r * (1f - e);
                        g = 255 * e + g * (1f - e);
                        b = 255 * e + b * (1f - e);
                    }
                    pixels[p++] = new Color32((byte)r, (byte)g, (byte)b, 255);
                }
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "Mapgen4Colormap"
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }

    internal sealed class Mapgen4RuntimeData
    {
        public Mapgen4TriangleMesh Mesh;
        public int[] TrianglePeaks;
        public Vector2[] VertexPositions;
        public Mapgen4Map Map;
        public float CellSpacing;
        public float MountainSpacing;

        public static Mapgen4RuntimeData Build(float cellSpacing, float mountainSpacing)
        {
            GenerateBoundaryPoints(cellSpacing * Mathf.Sqrt(2f), out List<Vector2> exteriorBoundary, out List<Vector2> interiorBoundary);

            var mountainGenerator = new Mapgen4PoissonDisk(new Vector2(Mapgen4Constants.MapSize, Mapgen4Constants.MapSize), mountainSpacing, 30, new Mapgen4Random(Mapgen4Constants.MeshSeed));
            foreach (Vector2 point in interiorBoundary)
            {
                mountainGenerator.AddPoint(point);
            }
            List<Vector2> mountainPointsAndBoundary = mountainGenerator.Fill();
            int numMountainPoints = mountainPointsAndBoundary.Count - interiorBoundary.Count;

            var generator = new Mapgen4PoissonDisk(new Vector2(Mapgen4Constants.MapSize, Mapgen4Constants.MapSize), cellSpacing, 6, new Mapgen4Random(Mapgen4Constants.MeshSeed));
            foreach (Vector2 point in mountainPointsAndBoundary)
            {
                generator.AddPoint(point);
            }
            List<Vector2> allInteriorPoints = generator.Fill();

            var allPoints = new Vector2[exteriorBoundary.Count + allInteriorPoints.Count];
            for (int i = 0; i < exteriorBoundary.Count; i++)
            {
                allPoints[i] = exteriorBoundary[i];
            }
            for (int i = 0; i < allInteriorPoints.Count; i++)
            {
                allPoints[exteriorBoundary.Count + i] = allInteriorPoints[i];
            }

            var delaunayPoints = new Vec2[allPoints.Length];
            for (int i = 0; i < allPoints.Length; i++)
            {
                delaunayPoints[i] = new Vec2(allPoints[i].x, allPoints[i].y);
            }
            Delaunay delaunay = DelaunayBuilder.Build(delaunayPoints);
            Mapgen4TriangleMesh mesh = Mapgen4TriangleMesh.Create(allPoints, delaunay.Triangles, delaunay.Halfedges, exteriorBoundary.Count);

            int mountainStart = exteriorBoundary.Count + interiorBoundary.Count;
            int[] peaks = new int[numMountainPoints];
            for (int i = 0; i < numMountainPoints; i++)
            {
                int region = mountainStart + i;
                peaks[i] = mesh.TriangleInner(mesh.SideOfRegion(region));
            }

            return new Mapgen4RuntimeData
            {
                Mesh = mesh,
                TrianglePeaks = peaks,
                VertexPositions = Mapgen4Geometry.BuildVertexPositions(mesh),
                Map = new Mapgen4Map(mesh, peaks, cellSpacing),
                CellSpacing = cellSpacing,
                MountainSpacing = mountainSpacing,
            };
        }

        static void GenerateBoundaryPoints(float spacing, out List<Vector2> exterior, out List<Vector2> interior)
        {
            const float curvature = 1f;
            const float epsilon = 1e-4f;
            float width = Mapgen4Constants.MapSize;
            float height = Mapgen4Constants.MapSize;
            int w = Mathf.CeilToInt((width - 2f * curvature) / spacing);
            int h = Mathf.CeilToInt((height - 2f * curvature) / spacing);

            interior = new List<Vector2>(2 * (w + h));
            for (int q = 0; q < w; q++)
            {
                float t = (float)q / w;
                float dx = (width - 2f * curvature) * t;
                float dy = epsilon + curvature * 4f * Mathf.Pow(t - 0.5f, 2f);
                interior.Add(new Vector2(curvature + dx, dy));
                interior.Add(new Vector2(width - curvature - dx, height - dy));
            }
            for (int r = 0; r < h; r++)
            {
                float t = (float)r / h;
                float dy = (height - 2f * curvature) * t;
                float dx = epsilon + curvature * 4f * Mathf.Pow(t - 0.5f, 2f);
                interior.Add(new Vector2(dx, height - curvature - dy));
                interior.Add(new Vector2(width - dx, curvature + dy));
            }

            float diagonal = spacing / Mathf.Sqrt(2f);
            exterior = new List<Vector2>(2 * (w + h) + 4);
            for (int q = 0; q < w; q++)
            {
                float t = (float)q / w;
                float dx = (width - 2f * curvature) * t + spacing / 2f;
                exterior.Add(new Vector2(dx, -diagonal));
                exterior.Add(new Vector2(width - dx, height + diagonal));
            }
            for (int r = 0; r < h; r++)
            {
                float t = (float)r / h;
                float dy = (height - 2f * curvature) * t + spacing / 2f;
                exterior.Add(new Vector2(-diagonal, height - dy));
                exterior.Add(new Vector2(width + diagonal, dy));
            }
            exterior.Add(new Vector2(-diagonal, -diagonal));
            exterior.Add(new Vector2(width + diagonal, -diagonal));
            exterior.Add(new Vector2(-diagonal, height + diagonal));
            exterior.Add(new Vector2(width + diagonal, height + diagonal));
        }
    }
}
