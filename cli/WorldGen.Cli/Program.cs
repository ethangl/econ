using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using WorldGen.Core;
using WorldGen.Cli.Lib;

var seedOption = new Option<int>("--seed", () => 7, "Random seed");
var cellsOption = new Option<int>("--cells", () => 20400, "Dense cell count");
var widthOption = new Option<int>("--width", () => 8192, "Heightmap width");
var heightOption = new Option<int>("--height", () => 4096, "Heightmap height");
var outputOption = new Option<string>("--output", () => "heightmap.r16", "Raw little-endian 16-bit heightmap output path");
var previewOutputOption = new Option<string>("--preview-output", () => null!, "Optional preview PNG path; defaults to a sibling .preview.png file");
var previewWidthOption = new Option<int>("--preview-width", () => 2048, "Maximum width for the preview PNG");
var oceanOption = new Option<float>("--ocean", () => 0.8f, "Ocean fraction (0-1)");
var jitterOption = new Option<float>("--jitter", () => 0.5f, "Point jitter (0-1)");
var ultraOption = new Option<bool>("--ultra", () => true, "Enable ultra-dense mesh (~4x cells via subdivision)");
var blurOption = new Option<float>("--blur", () => 0.6f, "Blur strength (1.0 = 5px sigma at 8192 wide, scales with resolution)");
var detailOption = new Option<float>("--detail", () => 0.1f, "Full-map micro-relief amplitude (0-1)");

var sharpenOption = new Option<float>("--sharpen", () => 0f, "Unsharp mask amount (0=off, 1=normal, 2=strong; uses blur sigma)");
var colorOption = new Option<bool>("--color", () => true, "Apply the terrain color ramp to the preview PNG");
var coastOption = new Option<float>("--coast", () => 0.25f, "Coastal detail amplitude (0-1)");
var stepsOption = new Option<int>("--steps", () => 15, "Number of tectonic time steps (1=single-shot, ~10 Myr per step via boundary migration)");
var debugOption = new Option<bool>("--debug", () => false, "Draw debug overlays (hotspot trails, etc.) on the preview PNG");
var cpuOption = new Option<bool>("--cpu", () => false, "Force the CPU heightmap pipeline instead of the default Metal path on macOS");

var rootCommand = new RootCommand("Generate a raw 2D heightmap plus preview from spherical world generation")
{
    seedOption, cellsOption, widthOption, heightOption, outputOption, previewOutputOption, previewWidthOption, oceanOption, jitterOption, ultraOption, coastOption, blurOption, detailOption, sharpenOption, colorOption, stepsOption, debugOption, cpuOption
};

var previewPngEncoder = new PngEncoder
{
    CompressionLevel = PngCompressionLevel.BestSpeed,
    FilterMethod = PngFilterMethod.None,
};

rootCommand.SetHandler((InvocationContext ctx) =>
{
    int seed = ctx.ParseResult.GetValueForOption(seedOption);
    int cells = ctx.ParseResult.GetValueForOption(cellsOption);
    int width = ctx.ParseResult.GetValueForOption(widthOption);
    int height = ctx.ParseResult.GetValueForOption(heightOption);
    string output = ctx.ParseResult.GetValueForOption(outputOption)!;
    string previewOutput = ctx.ParseResult.GetValueForOption(previewOutputOption);
    int previewWidth = ctx.ParseResult.GetValueForOption(previewWidthOption);
    float ocean = ctx.ParseResult.GetValueForOption(oceanOption);
    float jitter = ctx.ParseResult.GetValueForOption(jitterOption);
    bool ultra = ctx.ParseResult.GetValueForOption(ultraOption);
    float blur = ctx.ParseResult.GetValueForOption(blurOption);
    float detail = ctx.ParseResult.GetValueForOption(detailOption);
    float sharpen = ctx.ParseResult.GetValueForOption(sharpenOption);
    bool color = ctx.ParseResult.GetValueForOption(colorOption);
    float coast = ctx.ParseResult.GetValueForOption(coastOption);
    int steps = ctx.ParseResult.GetValueForOption(stepsOption);
    bool debug = ctx.ParseResult.GetValueForOption(debugOption);
    bool forceCpu = ctx.ParseResult.GetValueForOption(cpuOption);
    string previewPath = string.IsNullOrWhiteSpace(previewOutput) ? GetDefaultPreviewPath(output) : previewOutput;

    if (!string.Equals(Path.GetExtension(output), ".r16", StringComparison.OrdinalIgnoreCase))
        throw new ArgumentException("Primary output must use the .r16 extension.");

    Console.WriteLine($"Generating globe: seed={seed}, cells={cells}{(ultra ? " (ultra-dense)" : "")}, ocean={ocean:F2}, jitter={jitter:F2}, steps={steps}");

    var sw = Stopwatch.StartNew();

    var config = new WorldGenConfig
    {
        Seed = seed,
        DenseCellCount = cells,
        OceanFraction = ocean,
        Jitter = jitter,
        EnableUltraDense = ultra,
        TectonicSteps = steps,
    };

    var result = WorldGenPipeline.Generate(config);

    var terrain = result.DenseTerrain;
    var renderMesh = ultra ? terrain.UltraDenseMesh : terrain.Mesh;
    var renderElev = ultra ? terrain.UltraDenseCellElevation : terrain.CellElevation;
    var globeTimings = result.Timings;
    var denseTimings = terrain.Timings;

    Console.WriteLine($"  Globe generated in {sw.Elapsed.TotalSeconds:F1}s ({renderMesh.CellCount} cells)");
    Console.WriteLine($"    Coarse mesh: points {globeTimings.CoarsePointsSeconds:F2}s, hull {globeTimings.CoarseHullSeconds:F2}s, voronoi {globeTimings.CoarseVoronoiSeconds:F2}s, areas {globeTimings.CoarseAreaSeconds:F2}s");
    Console.WriteLine($"    Tectonics: plates {globeTimings.TectonicsSeconds:F2}s, elevation {globeTimings.ElevationSeconds:F2}s, hotspots {globeTimings.HotspotsSeconds:F2}s, arcs {globeTimings.VolcanicArcsSeconds:F2}s, cratons {globeTimings.CratonsSeconds:F2}s, basins {globeTimings.BasinsSeconds:F2}s");
    Console.WriteLine($"    Dense terrain: total {denseTimings.TotalSeconds:F2}s (points {denseTimings.DensePointsSeconds:F2}s, hull {denseTimings.DenseHullSeconds:F2}s, voronoi {denseTimings.DenseVoronoiSeconds:F2}s, areas {denseTimings.DenseAreaSeconds:F2}s, map {denseTimings.DenseMappingSeconds:F2}s, elev {denseTimings.DenseElevationSeconds:F2}s)");
    if (ultra)
        Console.WriteLine($"    Ultra-dense: subdivision {denseTimings.UltraSubdivisionSeconds:F2}s (setup {denseTimings.UltraSubdivisionSetupSeconds:F2}s, restore {denseTimings.UltraSubdivisionRestoreSeconds:F2}s), voronoi {denseTimings.UltraVoronoiSeconds:F2}s, areas {denseTimings.UltraAreaSeconds:F2}s, map {denseTimings.UltraMappingSeconds:F2}s, elev {denseTimings.UltraElevationSeconds:F2}s");
    Console.WriteLine($"    Site selection: {globeTimings.SiteSelectionSeconds:F2}s");

    sw.Restart();
    Console.WriteLine($"Rendering heightmap: {width}x{height} (16-bit)");

    var renderTerrain = new DenseTerrainData
    {
        Mesh = renderMesh,
        CellElevation = renderElev,
    };

    // Pipeline: render → blur → global detail → coast detail → sharpen → raw export → preview export
    bool metalSupported = !forceCpu && MetalHeightmapPipeline.IsSupported;
    bool useMetal = metalSupported;
    float blurSigma = blur * 5f * (width / 8192f);
    var stepSw = Stopwatch.StartNew();
    Image<L16> RenderOnCpu(string mode)
    {
        var cpuImage = HeightmapRenderer.Render(renderTerrain, width, height);
        Console.WriteLine($"  Heightmap rasterized in {stepSw.Elapsed.TotalSeconds:F1}s ({mode})");

        if (blurSigma > 0f)
        {
            stepSw.Restart();
            WrapBlur.Apply(cpuImage, blurSigma);
            Console.WriteLine($"  Blur applied in {stepSw.Elapsed.TotalSeconds:F1}s (sigma {blurSigma:F2}, {mode})");
        }

        if (detail > 0f)
        {
            stepSw.Restart();
            GlobalDetail.Apply(cpuImage, detail, seed);
            Console.WriteLine($"  Global detail applied in {stepSw.Elapsed.TotalSeconds:F1}s (amount {detail:F3}, {mode})");
        }

        if (coast > 0f)
        {
            stepSw.Restart();
            CoastDetail.Apply(cpuImage, coast, seed);
            Console.WriteLine($"  Coast detail applied in {stepSw.Elapsed.TotalSeconds:F1}s (amount {coast:F2}, {mode})");
        }

        return cpuImage;
    }

    Image<L16> image;
    if (useMetal)
    {
        try
        {
            image = MetalHeightmapPipeline.Render(renderTerrain, width, height, blurSigma, detail, coast, seed, out var metalTimings);
            Console.WriteLine($"  Heightmap rasterized in {metalTimings.RasterSeconds:F3}s (Metal default)");
            if (blurSigma > 0f)
                Console.WriteLine($"  Blur applied in {metalTimings.BlurSeconds:F3}s (sigma {blurSigma:F2}, Metal default)");
            if (detail > 0f)
                Console.WriteLine($"  Global detail applied in {metalTimings.DetailSeconds:F3}s (amount {detail:F3}, Metal default)");
            if (coast > 0f)
                Console.WriteLine($"  Coast detail applied in {metalTimings.CoastSeconds:F3}s (amount {coast:F2}, Metal default)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Metal render failed; falling back to CPU ({ex.Message})");
            useMetal = false;
            stepSw.Restart();
            image = RenderOnCpu("CPU fallback");
        }
    }
    else
    {
        if (!forceCpu && OperatingSystem.IsMacOS() && !metalSupported)
            Console.WriteLine($"  Metal unavailable; falling back to CPU ({MetalHeightmapPipeline.UnavailableReason})");

        image = RenderOnCpu(forceCpu ? "CPU forced" : "CPU fallback");
    }

    using var ownedImage = image;

    // Volcanic arc cone stamping (CPU-only, sparse — works after Metal or CPU pipeline)
    if (result.Tectonics.VolcanicArcs != null && result.Tectonics.VolcanicArcs.Length > 0)
    {
        stepSw.Restart();
        VolcanicArcDetail.Apply(ownedImage, result.Tectonics, result.Mesh, seed);
        Console.WriteLine($"  Volcanic arc cones stamped in {stepSw.Elapsed.TotalSeconds:F1}s ({result.Tectonics.VolcanicArcs.Length} arcs)");
    }

    if (sharpen > 0f)
    {
        stepSw.Restart();
        WrapUnsharpMask.Apply(ownedImage, sharpen, blurSigma);
        Console.WriteLine($"  Sharpen applied in {stepSw.Elapsed.TotalSeconds:F1}s (amount {sharpen:F2})");
    }

    stepSw.Restart();
    HeightmapOutput.SaveRawL16(ownedImage, output);
    Console.WriteLine($"  Saved raw R16 in {stepSw.Elapsed.TotalSeconds:F1}s");

    stepSw.Restart();
    using var previewGray = HeightmapOutput.CreatePreview(ownedImage, previewWidth);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(previewPath))!);
    if (color)
    {
        Image<Rgb24> previewColor;
        string colorMode;
        if (useMetal)
        {
            try
            {
                previewColor = MetalColorRamp.Apply(previewGray);
                colorMode = "Metal default";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Metal preview color failed; falling back to CPU ({ex.Message})");
                previewColor = ColorRamp.Apply(previewGray);
                colorMode = "CPU fallback";
            }
        }
        else
        {
            previewColor = ColorRamp.Apply(previewGray);
            colorMode = forceCpu ? "CPU forced" : "CPU fallback";
        }

        using (previewColor)
        {
            Console.WriteLine($"  Color ramp applied to preview in {stepSw.Elapsed.TotalSeconds:F1}s ({colorMode})");

            stepSw.Restart();
            previewColor.Save(previewPath, previewPngEncoder);
            Console.WriteLine($"  Saved preview PNG in {stepSw.Elapsed.TotalSeconds:F1}s ({previewColor.Width}x{previewColor.Height})");

            if (debug)
            {
                if (result.Tectonics.Hotspots != null)
                    DrawHotspotOverlay(previewColor, result.Tectonics, result.Mesh);
                if (result.Tectonics.VolcanicArcs != null)
                    DrawVolcanicArcOverlay(previewColor, result.Tectonics, result.Mesh);
                if (result.Tectonics.CellCratonStrength != null)
                    DrawCratonOverlay(previewColor, result.Tectonics, result.Mesh);
                if (result.Tectonics.CellBasinId != null)
                    DrawBasinOverlay(previewColor, result.Tectonics, result.Mesh);
                string debugPath = Path.Combine(
                    Path.GetDirectoryName(previewPath) ?? string.Empty,
                    Path.GetFileNameWithoutExtension(previewPath) + ".debug.png");
                previewColor.Save(debugPath, previewPngEncoder);
                Console.WriteLine($"  Saved debug PNG in {stepSw.Elapsed.TotalSeconds:F1}s → {debugPath}");
            }
        }
    }
    else
    {
        previewGray.Save(previewPath, previewPngEncoder);
        Console.WriteLine($"  Saved preview PNG in {stepSw.Elapsed.TotalSeconds:F1}s ({previewGray.Width}x{previewGray.Height})");
    }

    Console.WriteLine($"  Export completed in {sw.Elapsed.TotalSeconds:F1}s → {output} (+ {previewPath})");
});

return rootCommand.Invoke(args);

static string GetDefaultPreviewPath(string outputPath)
{
    string directory = Path.GetDirectoryName(outputPath) ?? string.Empty;
    string stem = Path.GetFileNameWithoutExtension(outputPath);
    return Path.Combine(directory, $"{stem}.preview.png");
}

static void DrawHotspotOverlay(Image<Rgb24> image, WorldGen.Core.TectonicData tectonics, WorldGen.Core.SphereMesh mesh)
{
    int w = image.Width;
    int h = image.Height;
    var magenta = new Rgb24(255, 0, 255);
    int dotRadius = Math.Max(2, w / 400);

    foreach (var hotspot in tectonics.Hotspots)
    {
        for (int t = 0; t < hotspot.TrailCells.Length; t++)
        {
            int cell = hotspot.TrailCells[t];
            WorldGen.Core.Vec3 p = mesh.CellCenters[cell].Normalized;
            float lat = (float)Math.Asin(p.Y);
            float lon = (float)Math.Atan2(p.Z, p.X);

            // Equirectangular projection: lon [-pi,pi] -> x [0,w], lat [pi/2,-pi/2] -> y [0,h]
            int px = (int)((lon / MathF.PI + 1f) * 0.5f * w);
            int py = (int)((0.5f - lat / MathF.PI) * h);

            // Source cell gets a larger dot
            int r = t == 0 ? dotRadius * 2 : dotRadius;

            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    if (dx * dx + dy * dy > r * r) continue;
                    int ix = (px + dx + w) % w; // wrap horizontally
                    int iy = Math.Clamp(py + dy, 0, h - 1);
                    image[ix, iy] = magenta;
                }
            }
        }
    }
}

static void DrawVolcanicArcOverlay(Image<Rgb24> image, WorldGen.Core.TectonicData tectonics, WorldGen.Core.SphereMesh mesh)
{
    int w = image.Width;
    int h = image.Height;
    var orange = new Rgb24(255, 165, 0);
    var red = new Rgb24(255, 40, 40);
    int dotRadius = Math.Max(2, w / 400);

    foreach (var arc in tectonics.VolcanicArcs)
    {
        // Orange dots for arc cells
        foreach (int cell in arc.ArcCells)
            DrawDot(image, mesh.CellCenters[cell], orange, dotRadius, w, h);

        // Red dots for peaks (larger)
        foreach (var peak in arc.Peaks)
            DrawDot(image, peak.Position, red, dotRadius * 2, w, h);
    }
}

static void DrawCratonOverlay(Image<Rgb24> image, WorldGen.Core.TectonicData tectonics, WorldGen.Core.SphereMesh mesh)
{
    int w = image.Width;
    int h = image.Height;
    int dotRadius = Math.Max(2, w / 400);

    for (int c = 0; c < mesh.CellCount; c++)
    {
        float strength = tectonics.CellCratonStrength[c];
        if (strength <= 0f) continue;

        // Amber: pale yellow at edges, bright amber at deep interior
        byte r = (byte)(180 + 75 * strength);
        byte g = (byte)(140 + 60 * strength);
        byte b = 30;
        DrawDot(image, mesh.CellCenters[c], new Rgb24(r, g, b), dotRadius, w, h);
    }
}

static void DrawBasinOverlay(Image<Rgb24> image, WorldGen.Core.TectonicData tectonics, WorldGen.Core.SphereMesh mesh)
{
    int w = image.Width;
    int h = image.Height;
    int dotRadius = Math.Max(2, w / 400);
    var teal = new Rgb24(0, 200, 180);

    for (int c = 0; c < mesh.CellCount; c++)
    {
        if (tectonics.CellBasinId[c] <= 0) continue;

        // Teal/cyan, vary hue by basin ID
        int basin = tectonics.CellBasinId[c];
        float hue = (basin * 0.618034f) % 1f;
        // Map to teal range: H=0.42-0.60
        float h01 = 0.42f + hue * 0.18f;
        // Simple HSV→RGB for S=0.7 V=0.85
        float s = 0.7f, v = 0.85f;
        float c1 = v * s;
        float x = c1 * (1f - Math.Abs((h01 * 6f) % 2f - 1f));
        float m = v - c1;
        float rf, gf, bf;
        int hi = (int)(h01 * 6f);
        switch (hi)
        {
            case 0: rf = c1; gf = x; bf = 0; break;
            case 1: rf = x; gf = c1; bf = 0; break;
            case 2: rf = 0; gf = c1; bf = x; break;
            case 3: rf = 0; gf = x; bf = c1; break;
            case 4: rf = x; gf = 0; bf = c1; break;
            default: rf = c1; gf = 0; bf = x; break;
        }
        var color = new Rgb24((byte)((rf + m) * 255), (byte)((gf + m) * 255), (byte)((bf + m) * 255));
        DrawDot(image, mesh.CellCenters[c], color, dotRadius, w, h);
    }
}

static void DrawDot(Image<Rgb24> image, WorldGen.Core.Vec3 position, Rgb24 color, int radius, int w, int h)
{
    WorldGen.Core.Vec3 p = position.Normalized;
    float lat = (float)Math.Asin(p.Y);
    float lon = (float)Math.Atan2(p.Z, p.X);
    int px = (int)((lon / MathF.PI + 1f) * 0.5f * w);
    int py = (int)((0.5f - lat / MathF.PI) * h);

    for (int dy = -radius; dy <= radius; dy++)
    {
        for (int dx = -radius; dx <= radius; dx++)
        {
            if (dx * dx + dy * dy > radius * radius) continue;
            int ix = (px + dx + w) % w;
            int iy = Math.Clamp(py + dy, 0, h - 1);
            image[ix, iy] = color;
        }
    }
}
