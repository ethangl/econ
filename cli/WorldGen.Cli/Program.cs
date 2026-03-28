using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using WorldGen.Core;
using WorldGen.Cli.Lib;

var seedOption = new Option<int>("--seed", () => 42, "Random seed");
var cellsOption = new Option<int>("--cells", () => 20400, "Dense cell count");
var widthOption = new Option<int>("--width", () => 8192, "Output image width");
var heightOption = new Option<int>("--height", () => 4096, "Output image height");
var outputOption = new Option<string>("--output", () => "heightmap.png", "Output file path");
var oceanOption = new Option<float>("--ocean", () => 0.6f, "Ocean fraction (0-1)");
var jitterOption = new Option<float>("--jitter", () => 0.5f, "Point jitter (0-1)");
var ultraOption = new Option<bool>("--ultra", () => false, "Enable ultra-dense mesh (~4x cells via subdivision)");
var blurOption = new Option<float>("--blur", () => 1.0f, "Blur strength (1.0 = 5px sigma at 8192 wide, scales with resolution)");

var sharpenOption = new Option<float>("--sharpen", () => 0f, "Unsharp mask amount (0=off, 1=normal, 2=strong; uses blur sigma)");
var colorOption = new Option<bool>("--color", () => false, "Output terrain-colored RGB instead of grayscale");
var coastOption = new Option<float>("--coast", () => 0.25f, "Coastal detail amplitude (0-1)");
var cpuOption = new Option<bool>("--cpu", () => false, "Force the CPU heightmap pipeline instead of the default Metal path on macOS");

var rootCommand = new RootCommand("Generate a 2D heightmap from spherical world generation")
{
    seedOption, cellsOption, widthOption, heightOption, outputOption, oceanOption, jitterOption, ultraOption, coastOption, blurOption, sharpenOption, colorOption, cpuOption
};

var pngEncoder = new PngEncoder
{
    CompressionLevel = PngCompressionLevel.BestSpeed,
};

rootCommand.SetHandler((InvocationContext ctx) =>
{
    int seed = ctx.ParseResult.GetValueForOption(seedOption);
    int cells = ctx.ParseResult.GetValueForOption(cellsOption);
    int width = ctx.ParseResult.GetValueForOption(widthOption);
    int height = ctx.ParseResult.GetValueForOption(heightOption);
    string output = ctx.ParseResult.GetValueForOption(outputOption)!;
    float ocean = ctx.ParseResult.GetValueForOption(oceanOption);
    float jitter = ctx.ParseResult.GetValueForOption(jitterOption);
    bool ultra = ctx.ParseResult.GetValueForOption(ultraOption);
    float blur = ctx.ParseResult.GetValueForOption(blurOption);
    float sharpen = ctx.ParseResult.GetValueForOption(sharpenOption);
    bool color = ctx.ParseResult.GetValueForOption(colorOption);
    float coast = ctx.ParseResult.GetValueForOption(coastOption);
    bool forceCpu = ctx.ParseResult.GetValueForOption(cpuOption);

    Console.WriteLine($"Generating globe: seed={seed}, cells={cells}{(ultra ? " (ultra-dense)" : "")}, ocean={ocean:F2}, jitter={jitter:F2}");

    var sw = Stopwatch.StartNew();

    var config = new WorldGenConfig
    {
        Seed = seed,
        DenseCellCount = cells,
        OceanFraction = ocean,
        Jitter = jitter,
        EnableUltraDense = ultra,
    };

    var result = WorldGenPipeline.Generate(config);

    var terrain = result.DenseTerrain;
    var renderMesh = ultra ? terrain.UltraDenseMesh : terrain.Mesh;
    var renderElev = ultra ? terrain.UltraDenseCellElevation : terrain.CellElevation;

    Console.WriteLine($"  Globe generated in {sw.Elapsed.TotalSeconds:F1}s ({renderMesh.CellCount} cells)");

    sw.Restart();
    Console.WriteLine($"Rendering heightmap: {width}x{height} (16-bit)");

    var renderTerrain = new DenseTerrainData
    {
        Mesh = renderMesh,
        CellElevation = renderElev,
    };

    // Pipeline: render → blur → sharpen → coast detail → color → save
    bool useMetal = !forceCpu && MetalHeightmapPipeline.IsSupported;
    float blurSigma = blur * 5f * (width / 8192f);
    var stepSw = Stopwatch.StartNew();
    Image<L16> image;
    if (useMetal)
    {
        image = MetalHeightmapPipeline.Render(renderTerrain, width, height, blurSigma, coast, seed, out var metalTimings);
        Console.WriteLine($"  Heightmap rasterized in {metalTimings.RasterSeconds:F3}s (Metal default)");
        if (blurSigma > 0f)
            Console.WriteLine($"  Blur applied in {metalTimings.BlurSeconds:F3}s (sigma {blurSigma:F2}, Metal default)");
        if (coast > 0f)
            Console.WriteLine($"  Coast detail applied in {metalTimings.CoastSeconds:F3}s (amount {coast:F2}, Metal default)");
    }
    else
    {
        image = HeightmapRenderer.Render(renderTerrain, width, height);
        string rasterMode = forceCpu ? "CPU forced" : "CPU fallback";
        Console.WriteLine($"  Heightmap rasterized in {stepSw.Elapsed.TotalSeconds:F1}s ({rasterMode})");

        if (blurSigma > 0f)
        {
            stepSw.Restart();
            WrapBlur.Apply(image, blurSigma);
            string blurMode = forceCpu ? "CPU forced" : "CPU fallback";
            Console.WriteLine($"  Blur applied in {stepSw.Elapsed.TotalSeconds:F1}s (sigma {blurSigma:F2}, {blurMode})");
        }

        if (coast > 0f)
        {
            stepSw.Restart();
            CoastDetail.Apply(image, coast, seed);
            string reason = forceCpu ? "CPU forced" : "CPU fallback";
            Console.WriteLine($"  Coast detail applied in {stepSw.Elapsed.TotalSeconds:F1}s (amount {coast:F2}, {reason})");
        }
    }

    using var ownedImage = image;

    if (sharpen > 0f)
    {
        stepSw.Restart();
        WrapUnsharpMask.Apply(ownedImage, sharpen, blurSigma);
        Console.WriteLine($"  Sharpen applied in {stepSw.Elapsed.TotalSeconds:F1}s (amount {sharpen:F2})");
    }

    if (color)
    {
        stepSw.Restart();
        using var rgb = useMetal
            ? MetalColorRamp.Apply(ownedImage)
            : ColorRamp.Apply(ownedImage);
        string colorMode = useMetal ? "Metal default" : (forceCpu ? "CPU forced" : "CPU fallback");
        Console.WriteLine($"  Color ramp applied in {stepSw.Elapsed.TotalSeconds:F1}s ({colorMode})");

        stepSw.Restart();
        rgb.Save(output, pngEncoder);
        Console.WriteLine($"  Saved PNG in {stepSw.Elapsed.TotalSeconds:F1}s");
    }
    else
    {
        stepSw.Restart();
        ownedImage.Save(output, pngEncoder);
        Console.WriteLine($"  Saved PNG in {stepSw.Elapsed.TotalSeconds:F1}s");
    }

    Console.WriteLine($"  Render pipeline completed in {sw.Elapsed.TotalSeconds:F1}s → {output}");
});

return rootCommand.Invoke(args);
