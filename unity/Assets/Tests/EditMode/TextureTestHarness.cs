using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using EconSim.Core.Data;
using EconSim.Core.Import;
using EconSim.Renderer;
using MapGen.Core;
using NUnit.Framework;
using UnityEngine;

namespace EconSim.Tests
{
    internal readonly struct TextureBaselineCase
    {
        public readonly int Seed;
        public readonly HeightmapTemplateType Template;
        public readonly int CellCount;

        public TextureBaselineCase(int seed, HeightmapTemplateType template, int cellCount)
        {
            Seed = seed;
            Template = template;
            CellCount = cellCount;
        }
    }

    internal sealed class OverlayFixture : IDisposable
    {
        public MapData MapData { get; }
        public Material Material { get; }
        public MapOverlayManager OverlayManager { get; }
        private readonly bool previousTextureDebugEnabled;

        public OverlayFixture(MapData mapData, Material material, MapOverlayManager overlayManager, bool previousTextureDebugEnabled)
        {
            MapData = mapData;
            Material = material;
            OverlayManager = overlayManager;
            this.previousTextureDebugEnabled = previousTextureDebugEnabled;
        }

        public void Dispose()
        {
            OverlayManager?.Dispose();
            if (Material != null)
                UnityEngine.Object.DestroyImmediate(Material);
            TextureDebugger.Enabled = previousTextureDebugEnabled;
        }
    }

    internal static class TextureTestHarness
    {
        private const string BaselineFileRelativePath = "Tests/EditMode/MapGenRegressionBaselines.json";
        private const string TextureHashBaselineFileRelativePath = "Tests/EditMode/MapTextureRegressionHashBaselines.json";
        private const string UpdateBaselineEnvVar = "M3_UPDATE_TEXTURE_BASELINES";
        private static readonly object BaselineFileLock = new object();
        private static readonly object MapDataCacheLock = new object();
        private static readonly Dictionary<string, MapData> MapDataCache = new Dictionary<string, MapData>();

        internal static IEnumerable BaselineCases
        {
            get
            {
                foreach (TextureBaselineCase baseline in LoadBaselineCases())
                {
                    yield return new object[] { baseline.Seed, baseline.Template, baseline.CellCount };
                }
            }
        }

        internal static TextureBaselineCase GetPrimaryBaselineCase()
        {
            foreach (TextureBaselineCase baseline in LoadBaselineCases())
                return baseline;

            Assert.Fail("Baseline file has no cases.");
            return default;
        }

        internal static OverlayFixture CreateOverlayFixture(TextureBaselineCase baseline, int resolutionMultiplier = 2)
        {
            bool previousTextureDebugEnabled = TextureDebugger.Enabled;
            TextureDebugger.Enabled = false;

            var mapData = GetOrCreateMapData(baseline);

            var shader = Shader.Find("EconSim/MapOverlay");
            Assert.That(shader, Is.Not.Null, "Shader EconSim/MapOverlay not found.");

            var material = new Material(shader);
            var manager = new MapOverlayManager(mapData, material, resolutionMultiplier);
            return new OverlayFixture(mapData, material, manager, previousTextureDebugEnabled);
        }

        internal static string HashTextureFromMaterial(Material material, string propertyName)
        {
            var texture = material.GetTexture(propertyName) as Texture2D;
            Assert.That(texture, Is.Not.Null, $"Expected Texture2D bound to material property {propertyName}");
            return HashTexture(texture);
        }

        internal static string HashTexture(Texture2D texture)
        {
            bool createdReadableCopy = false;
            Texture2D readable = texture;

            try
            {
                // Throws if texture is not readable.
                _ = texture.GetRawTextureData<byte>();
            }
            catch (UnityException)
            {
                readable = CreateReadableCopy(texture);
                createdReadableCopy = true;
            }

            var rawNative = readable.GetRawTextureData<byte>();
            var raw = new byte[rawNative.Length];
            rawNative.CopyTo(raw);
            string header = $"{readable.width}x{readable.height}:{readable.format}";
            byte[] headerBytes = Encoding.ASCII.GetBytes(header);
            byte[] payload = new byte[headerBytes.Length + raw.Length];
            Buffer.BlockCopy(headerBytes, 0, payload, 0, headerBytes.Length);
            Buffer.BlockCopy(raw, 0, payload, headerBytes.Length, raw.Length);

            byte[] digest;
            using (var sha = SHA256.Create())
            {
                digest = sha.ComputeHash(payload);
            }

            if (createdReadableCopy)
                UnityEngine.Object.DestroyImmediate(readable);

            return BitConverter.ToString(digest).Replace("-", string.Empty);
        }

        internal static Dictionary<string, string> LoadExpectedTextureHashes(int seed, HeightmapTemplateType template, int cellCount)
        {
            string fullPath = Path.Combine(Application.dataPath, TextureHashBaselineFileRelativePath);
            Assert.That(File.Exists(fullPath), Is.True, $"Missing texture hash baseline file at: {fullPath}");

            string json = File.ReadAllText(fullPath);
            var root = JsonUtility.FromJson<TextureHashBaselineFile>(json);
            Assert.That(root, Is.Not.Null, "Failed to parse texture hash baseline JSON.");
            Assert.That(root.cases, Is.Not.Null, "Texture hash baseline JSON must contain 'cases'.");

            TextureHashBaselineJsonCase match = null;
            for (int i = 0; i < root.cases.Length; i++)
            {
                TextureHashBaselineJsonCase candidate = root.cases[i];
                if (candidate.seed != seed || candidate.template != template.ToString())
                    continue;

                if (candidate.cellCount > 0 && candidate.cellCount != cellCount)
                    continue;

                match = candidate;
                break;
            }

            Assert.That(match, Is.Not.Null,
                $"No texture hash baseline entry for seed={seed}, template={template}, cellCount={cellCount}");
            Assert.That(match.textures, Is.Not.Null.And.Not.Empty,
                $"Texture hash baseline entry has no textures for seed={seed}, template={template}");

            var expected = new Dictionary<string, string>(match.textures.Length);
            for (int i = 0; i < match.textures.Length; i++)
            {
                TextureHashEntry entry = match.textures[i];
                Assert.That(entry.property, Is.Not.Null.And.Not.Empty, "Baseline texture entry is missing property name.");
                Assert.That(entry.sha256, Is.Not.Null.And.Not.Empty, $"Baseline texture entry missing hash for {entry.property}");
                expected[entry.property] = entry.sha256;
            }

            return expected;
        }

        private static MapData GetOrCreateMapData(TextureBaselineCase baseline)
        {
            string key = $"{baseline.Seed}|{baseline.Template}|{baseline.CellCount}";
            lock (MapDataCacheLock)
            {
                if (MapDataCache.TryGetValue(key, out var cached))
                    return cached;
            }

            var config = new MapGenConfig
            {
                Seed = baseline.Seed,
                Template = baseline.Template,
                CellCount = baseline.CellCount
            };

            var mapResult = MapGenPipeline.Generate(config);
            var mapData = MapGenAdapter.Convert(mapResult);

            lock (MapDataCacheLock)
            {
                MapDataCache[key] = mapData;
            }

            return mapData;
        }

        internal static bool IsBaselineUpdateModeEnabled()
        {
            string value = Environment.GetEnvironmentVariable(UpdateBaselineEnvVar);
            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        internal static void UpsertExpectedTextureHashes(int seed, HeightmapTemplateType template, int cellCount, Dictionary<string, string> hashes)
        {
            Assert.That(hashes, Is.Not.Null.And.Not.Empty, "Cannot write empty texture-hash baseline entry.");

            lock (BaselineFileLock)
            {
                string fullPath = Path.Combine(Application.dataPath, TextureHashBaselineFileRelativePath);
                TextureHashBaselineFile root = LoadTextureHashBaselineFileForWrite(fullPath);

                int matchIndex = -1;
                for (int i = 0; i < root.cases.Length; i++)
                {
                    TextureHashBaselineJsonCase candidate = root.cases[i];
                    if (candidate.seed == seed &&
                        candidate.template == template.ToString() &&
                        candidate.cellCount == cellCount)
                    {
                        matchIndex = i;
                        break;
                    }
                }

                var entries = new List<TextureHashEntry>(hashes.Count);
                foreach (var kvp in hashes)
                {
                    entries.Add(new TextureHashEntry
                    {
                        property = kvp.Key,
                        sha256 = kvp.Value
                    });
                }
                entries.Sort((a, b) => string.CompareOrdinal(a.property, b.property));

                var newCase = new TextureHashBaselineJsonCase
                {
                    seed = seed,
                    template = template.ToString(),
                    cellCount = cellCount,
                    textures = entries.ToArray()
                };

                var cases = new List<TextureHashBaselineJsonCase>(root.cases);
                if (matchIndex >= 0)
                    cases[matchIndex] = newCase;
                else
                    cases.Add(newCase);

                cases.Sort((a, b) =>
                {
                    int seedCompare = a.seed.CompareTo(b.seed);
                    if (seedCompare != 0) return seedCompare;
                    return string.CompareOrdinal(a.template, b.template);
                });

                root.cases = cases.ToArray();
                string json = JsonUtility.ToJson(root, true);
                File.WriteAllText(fullPath, json);
            }
        }

        private static TextureHashBaselineFile LoadTextureHashBaselineFileForWrite(string fullPath)
        {
            if (!File.Exists(fullPath))
                return new TextureHashBaselineFile { cases = Array.Empty<TextureHashBaselineJsonCase>() };

            string json = File.ReadAllText(fullPath);
            if (string.IsNullOrWhiteSpace(json))
                return new TextureHashBaselineFile { cases = Array.Empty<TextureHashBaselineJsonCase>() };

            var root = JsonUtility.FromJson<TextureHashBaselineFile>(json);
            if (root == null)
                return new TextureHashBaselineFile { cases = Array.Empty<TextureHashBaselineJsonCase>() };
            if (root.cases == null)
                root.cases = Array.Empty<TextureHashBaselineJsonCase>();
            return root;
        }

        private static Texture2D CreateReadableCopy(Texture2D source)
        {
            var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, rt);

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            readable.Apply();
            RenderTexture.active = prev;

            RenderTexture.ReleaseTemporary(rt);
            return readable;
        }

        private static IEnumerable<TextureBaselineCase> LoadBaselineCases()
        {
            string fullPath = Path.Combine(Application.dataPath, BaselineFileRelativePath);
            Assert.That(File.Exists(fullPath), Is.True, $"Missing baseline file at: {fullPath}");

            string json = File.ReadAllText(fullPath);
            var root = JsonUtility.FromJson<TextureBaselineFile>(json);
            Assert.That(root, Is.Not.Null, "Failed to parse baseline JSON.");
            Assert.That(root.cases, Is.Not.Null, "Baseline JSON must contain 'cases'.");
            Assert.That(root.cases.Length, Is.GreaterThan(0), "Baseline JSON has no cases.");

            for (int i = 0; i < root.cases.Length; i++)
            {
                TextureBaselineJsonCase entry = root.cases[i];
                yield return new TextureBaselineCase(
                    entry.seed,
                    (HeightmapTemplateType)Enum.Parse(typeof(HeightmapTemplateType), entry.template),
                    entry.cellCount
                );
            }
        }
    }

    [Serializable]
    internal class TextureBaselineFile
    {
        public TextureBaselineJsonCase[] cases;
    }

    [Serializable]
    internal class TextureBaselineJsonCase
    {
        public int seed;
        public string template;
        public int cellCount;
    }

    [Serializable]
    internal class TextureHashBaselineFile
    {
        public TextureHashBaselineJsonCase[] cases;
    }

    [Serializable]
    internal class TextureHashBaselineJsonCase
    {
        public int seed;
        public string template;
        public int cellCount;
        public TextureHashEntry[] textures;
    }

    [Serializable]
    internal class TextureHashEntry
    {
        public string property;
        public string sha256;
    }
}
