using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using SixLabors.ImageSharp;
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
var blurOption = new Option<float>("--blur", () => 0f, "Gaussian blur sigma in pixels (wraps horizontally)");

var sharpenOption = new Option<float>("--sharpen", () => 0f, "Unsharp mask amount (0=off, 1=normal, 2=strong)");
var sharpenRadiusOption = new Option<float>("--sharpen-radius", () => 2f, "Unsharp mask blur radius in pixels");
var colorOption = new Option<bool>("--color", () => false, "Output terrain-colored RGB instead of grayscale");
var coastOption = new Option<float>("--coast", () => 0.25f, "Coastal detail amplitude (0-1)");

var rootCommand = new RootCommand("Generate a 2D heightmap from spherical world generation")
{
    seedOption, cellsOption, widthOption, heightOption, outputOption, oceanOption, jitterOption, ultraOption, coastOption, blurOption, sharpenOption, sharpenRadiusOption, colorOption
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
    float sharpenRadius = ctx.ParseResult.GetValueForOption(sharpenRadiusOption);
    bool color = ctx.ParseResult.GetValueForOption(colorOption);
    float coast = ctx.ParseResult.GetValueForOption(coastOption);

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
    using var image = HeightmapRenderer.Render(renderTerrain, width, height);
    if (blur > 0f) WrapBlur.Apply(image, blur);
    if (sharpen > 0f) WrapUnsharpMask.Apply(image, sharpen, sharpenRadius);
    if (coast > 0f) CoastDetail.Apply(image, coast, seed);
    if (color)
    {
        using var rgb = ColorRamp.Apply(image);
        rgb.SaveAsPng(output);
    }
    else
    {
        image.SaveAsPng(output);
    }

    Console.WriteLine($"  Rendered in {sw.Elapsed.TotalSeconds:F1}s → {output}");
});

return rootCommand.Invoke(args);
