import Foundation
import Metal

struct Params
{
    var width: UInt32
    var height: UInt32
    var amplitude: Float
}

struct RenderParams
{
    var width: UInt32
    var height: UInt32
    var radius: Float
    var latBuckets: Int32
    var lonBuckets: Int32
}

func argument(named name: String) throws -> String
{
    let args = CommandLine.arguments
    guard let index = args.firstIndex(of: name), index + 1 < args.count else
    {
        throw NSError(domain: "MetalCoastHelper", code: 1, userInfo: [NSLocalizedDescriptionKey: "Missing argument \(name)"])
    }

    return args[index + 1]
}

func optionalArgument(named name: String) -> String?
{
    let args = CommandLine.arguments
    guard let index = args.firstIndex(of: name), index + 1 < args.count else
    {
        return nil
    }

    return args[index + 1]
}

func readUInt16Array(from path: String) throws -> [UInt16]
{
    let data = try Data(contentsOf: URL(fileURLWithPath: path))
    return data.withUnsafeBytes { rawBuffer in
        Array(rawBuffer.bindMemory(to: UInt16.self))
    }
}

func readInt32Array(from path: String) throws -> [Int32]
{
    let data = try Data(contentsOf: URL(fileURLWithPath: path))
    return data.withUnsafeBytes { rawBuffer in
        Array(rawBuffer.bindMemory(to: Int32.self))
    }
}

func readFloatArray(from path: String) throws -> [Float]
{
    let data = try Data(contentsOf: URL(fileURLWithPath: path))
    return data.withUnsafeBytes { rawBuffer in
        Array(rawBuffer.bindMemory(to: Float.self))
    }
}

let shaderSource = """
#include <metal_stdlib>
using namespace metal;

struct Params
{
    uint width;
    uint height;
    float amplitude;
};

struct RenderParams
{
    uint width;
    uint height;
    float radius;
    int latBuckets;
    int lonBuckets;
};

inline int floor_bug(float v)
{
    return v >= 0.0f ? int(v) : int(v) - 1;
}

inline float fade_func(float t)
{
    return t * t * t * (t * (t * 6.0f - 15.0f) + 10.0f);
}

inline float lerp_func(float a, float b, float t)
{
    return a + t * (b - a);
}

inline float grad_func(int hash, float x, float y, float z)
{
    int h = hash & 15;
    float u = h < 8 ? x : y;
    float v = h < 4 ? y : ((h == 12 || h == 14) ? x : z);
    return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
}

inline float sample_noise(const device int* perm, float x, float y, float z)
{
    int xi = floor_bug(x);
    int yi = floor_bug(y);
    int zi = floor_bug(z);
    float xf = x - float(xi);
    float yf = y - float(yi);
    float zf = z - float(zi);
    xi &= 255;
    yi &= 255;
    zi &= 255;

    float u = fade_func(xf);
    float v = fade_func(yf);
    float w = fade_func(zf);

    int a  = perm[xi] + yi;
    int aa = perm[a] + zi;
    int ab = perm[a + 1] + zi;
    int b  = perm[xi + 1] + yi;
    int ba = perm[b] + zi;
    int bb = perm[b + 1] + zi;

    float x1 = lerp_func(grad_func(perm[aa], xf, yf, zf), grad_func(perm[ba], xf - 1.0f, yf, zf), u);
    float x2 = lerp_func(grad_func(perm[ab], xf, yf - 1.0f, zf), grad_func(perm[bb], xf - 1.0f, yf - 1.0f, zf), u);
    float y1 = lerp_func(x1, x2, v);

    float x3 = lerp_func(grad_func(perm[aa + 1], xf, yf, zf - 1.0f), grad_func(perm[ba + 1], xf - 1.0f, yf, zf - 1.0f), u);
    float x4 = lerp_func(grad_func(perm[ab + 1], xf, yf - 1.0f, zf - 1.0f), grad_func(perm[bb + 1], xf - 1.0f, yf - 1.0f, zf - 1.0f), u);
    float y2 = lerp_func(x3, x4, v);

    return lerp_func(y1, y2, w);
}

inline float fractal6(const device int* perm, float x, float y, float z)
{
    float sum = 0.0f;
    float amp = 1.0f;
    float freq = 1.0f;
    float maxAmp = 0.0f;

    sum += sample_noise(perm, x * freq, y * freq, z * freq) * amp;
    maxAmp += amp;
    freq *= 2.0f;
    amp *= 0.5f;

    sum += sample_noise(perm, x * freq, y * freq, z * freq) * amp;
    maxAmp += amp;
    freq *= 2.0f;
    amp *= 0.5f;

    sum += sample_noise(perm, x * freq, y * freq, z * freq) * amp;
    maxAmp += amp;
    freq *= 2.0f;
    amp *= 0.5f;

    sum += sample_noise(perm, x * freq, y * freq, z * freq) * amp;
    maxAmp += amp;
    freq *= 2.0f;
    amp *= 0.5f;

    sum += sample_noise(perm, x * freq, y * freq, z * freq) * amp;
    maxAmp += amp;
    freq *= 2.0f;
    amp *= 0.5f;

    sum += sample_noise(perm, x * freq, y * freq, z * freq) * amp;
    maxAmp += amp;

    return sum / maxAmp;
}

inline int lat_index(float lat, int latBuckets)
{
    float t = (lat + float(M_PI_F) / 2.0f) / float(M_PI_F);
    int idx = int(t * float(latBuckets));
    return clamp(idx, 0, latBuckets - 1);
}

inline int lon_index(float lon, int lonBuckets)
{
    float t = (lon + float(M_PI_F)) / (2.0f * float(M_PI_F));
    int idx = int(t * float(lonBuckets));
    return clamp(idx, 0, lonBuckets - 1);
}

inline int longitude_search_radius(float lat, int lonBuckets)
{
    float cosLat = cos(lat);
    return cosLat > 0.01f ? min(int(ceil(1.0f / cosLat)), lonBuckets / 2) : lonBuckets / 2;
}

kernel void apply_coast(
    const device ushort* inputPixels [[buffer(0)]],
    device ushort* outputPixels [[buffer(1)]],
    const device int* perm [[buffer(2)]],
    constant Params& params [[buffer(3)]],
    uint gid [[thread_position_in_grid]])
{
    uint pixelCount = params.width * params.height;
    if (gid >= pixelCount)
    {
        return;
    }

    uint x = gid % params.width;
    uint y = gid / params.width;
    ushort packed = inputPixels[gid];
    float elev = float(packed) / 65535.0f;
    float e = elev - 0.5f;
    float e2 = e * e;
    float fade = 1.0f - e2 * e2 * 16.0f;
    if (fade <= 0.0f)
    {
        outputPixels[gid] = packed;
        return;
    }

    float width = float(params.width);
    float height = float(params.height);
    float lat = float(M_PI_F) / 2.0f - (float(y) + 0.5f) / height * float(M_PI_F);
    float lon = (float(x) + 0.5f) / width * 2.0f * float(M_PI_F) - float(M_PI_F);
    float cosLat = cos(lat);
    float sinLat = sin(lat);
    float px = cosLat * cos(lon);
    float py = sinLat;
    float pz = cosLat * sin(lon);
    float freq = width / (2.0f * float(M_PI_F) * 50.0f);
    float n = fractal6(perm, px * freq, py * freq, pz * freq);
    float result = elev + n * params.amplitude * fade;
    outputPixels[gid] = ushort(clamp(result * 65535.0f, 0.0f, 65535.0f));
}

kernel void render_heightmap(
    const device float* centerX [[buffer(0)]],
    const device float* centerY [[buffer(1)]],
    const device float* centerZ [[buffer(2)]],
    const device int* bucketOffsets [[buffer(3)]],
    const device int* bucketCounts [[buffer(4)]],
    const device int* bucketCells [[buffer(5)]],
    const device float* elevation [[buffer(6)]],
    constant RenderParams& params [[buffer(7)]],
    device ushort* outputPixels [[buffer(8)]],
    uint gid [[thread_position_in_grid]])
{
    uint pixelCount = params.width * params.height;
    if (gid >= pixelCount)
    {
        return;
    }

    uint x = gid % params.width;
    uint y = gid / params.width;
    float width = float(params.width);
    float height = float(params.height);
    float lat = float(M_PI_F) / 2.0f - (float(y) + 0.5f) / height * float(M_PI_F);
    float lon = (float(x) + 0.5f) / width * 2.0f * float(M_PI_F) - float(M_PI_F);
    float cosLat = cos(lat);
    float px = params.radius * cosLat * cos(lon);
    float py = params.radius * sin(lat);
    float pz = params.radius * cosLat * sin(lon);

    int latIdx = lat_index(lat, params.latBuckets);
    int lonIdx = lon_index(lon, params.lonBuckets);
    int lonRadius = longitude_search_radius(lat, params.lonBuckets);

    float bestDist = FLT_MAX;
    int bestCell = 0;

    for (int dLat = -1; dLat <= 1; dLat++)
    {
        int li = latIdx + dLat;
        if (li < 0 || li >= params.latBuckets)
        {
            continue;
        }

        int rowOffset = li * params.lonBuckets;
        for (int dLon = -lonRadius; dLon <= lonRadius; dLon++)
        {
            int lj = (lonIdx + dLon + params.lonBuckets) % params.lonBuckets;
            int bucketIndex = rowOffset + lj;
            int start = bucketOffsets[bucketIndex];
            int end = start + bucketCounts[bucketIndex];

            for (int i = start; i < end; i++)
            {
                int c = bucketCells[i];
                float dx = px - centerX[c];
                float dy = py - centerY[c];
                float dz = pz - centerZ[c];
                float dist = dx * dx + dy * dy + dz * dz;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestCell = c;
                }
            }
        }
    }

    float elev = clamp(elevation[bestCell], 0.0f, 1.0f);
    outputPixels[gid] = ushort(elev * 65535.0f);
}
"""

func runCoast() throws
{
    let inputPath = try argument(named: "--input")
    let outputPath = try argument(named: "--output")
    let permPath = try argument(named: "--perm")
    let width = try UInt32(argument(named: "--width")).unwrap("width")
    let height = try UInt32(argument(named: "--height")).unwrap("height")
    let amplitude = try Float(argument(named: "--amplitude")).unwrap("amplitude")

    let pixelCount = Int(width * height)
    let inputPixels = try readUInt16Array(from: inputPath)
    let perm = try readInt32Array(from: permPath)

    guard inputPixels.count == pixelCount else
    {
        throw NSError(domain: "MetalCoastHelper", code: 2, userInfo: [NSLocalizedDescriptionKey: "Unexpected input pixel count \(inputPixels.count), expected \(pixelCount)"])
    }

    guard perm.count == 512 else
    {
        throw NSError(domain: "MetalCoastHelper", code: 3, userInfo: [NSLocalizedDescriptionKey: "Unexpected permutation count \(perm.count), expected 512"])
    }

    guard let device = MTLCreateSystemDefaultDevice() else
    {
        throw NSError(domain: "MetalCoastHelper", code: 4, userInfo: [NSLocalizedDescriptionKey: "No Metal device available"])
    }

    let library = try device.makeLibrary(source: shaderSource, options: nil)
    guard let function = library.makeFunction(name: "apply_coast") else
    {
        throw NSError(domain: "MetalCoastHelper", code: 5, userInfo: [NSLocalizedDescriptionKey: "Failed to create Metal function"])
    }

    let pipeline = try device.makeComputePipelineState(function: function)
    guard let queue = device.makeCommandQueue() else
    {
        throw NSError(domain: "MetalCoastHelper", code: 6, userInfo: [NSLocalizedDescriptionKey: "Failed to create Metal command queue"])
    }

    let inputBuffer = try inputPixels.withUnsafeBytes { bytes in
        guard let buffer = device.makeBuffer(bytes: bytes.baseAddress!, length: bytes.count, options: .storageModeShared) else
        {
            throw NSError(domain: "MetalCoastHelper", code: 7, userInfo: [NSLocalizedDescriptionKey: "Failed to create input buffer"])
        }
        return buffer
    }

    guard let outputBuffer = device.makeBuffer(length: pixelCount * MemoryLayout<UInt16>.stride, options: .storageModeShared) else
    {
        throw NSError(domain: "MetalCoastHelper", code: 8, userInfo: [NSLocalizedDescriptionKey: "Failed to create output buffer"])
    }

    let permBuffer = try perm.withUnsafeBytes { bytes in
        guard let buffer = device.makeBuffer(bytes: bytes.baseAddress!, length: bytes.count, options: .storageModeShared) else
        {
            throw NSError(domain: "MetalCoastHelper", code: 9, userInfo: [NSLocalizedDescriptionKey: "Failed to create permutation buffer"])
        }
        return buffer
    }

    var params = Params(width: width, height: height, amplitude: amplitude)
    let paramsBuffer = try withUnsafeBytes(of: &params) { bytes in
        guard let buffer = device.makeBuffer(bytes: bytes.baseAddress!, length: bytes.count, options: .storageModeShared) else
        {
            throw NSError(domain: "MetalCoastHelper", code: 10, userInfo: [NSLocalizedDescriptionKey: "Failed to create params buffer"])
        }
        return buffer
    }

    guard let commandBuffer = queue.makeCommandBuffer(),
          let encoder = commandBuffer.makeComputeCommandEncoder() else
    {
        throw NSError(domain: "MetalCoastHelper", code: 11, userInfo: [NSLocalizedDescriptionKey: "Failed to create command buffer or encoder"])
    }

    encoder.setComputePipelineState(pipeline)
    encoder.setBuffer(inputBuffer, offset: 0, index: 0)
    encoder.setBuffer(outputBuffer, offset: 0, index: 1)
    encoder.setBuffer(permBuffer, offset: 0, index: 2)
    encoder.setBuffer(paramsBuffer, offset: 0, index: 3)

    let threadWidth = min(pipeline.maxTotalThreadsPerThreadgroup, 256)
    let threadsPerGroup = MTLSize(width: threadWidth, height: 1, depth: 1)
    let threadsPerGrid = MTLSize(width: pixelCount, height: 1, depth: 1)
    encoder.dispatchThreads(threadsPerGrid, threadsPerThreadgroup: threadsPerGroup)
    encoder.endEncoding()

    commandBuffer.commit()
    commandBuffer.waitUntilCompleted()

    if let error = commandBuffer.error
    {
        throw error
    }

    let outputData = Data(bytes: outputBuffer.contents(), count: pixelCount * MemoryLayout<UInt16>.stride)
    try outputData.write(to: URL(fileURLWithPath: outputPath), options: .atomic)
}

func runRender() throws
{
    let outputPath = try argument(named: "--output")
    let centerXPath = try argument(named: "--center-x")
    let centerYPath = try argument(named: "--center-y")
    let centerZPath = try argument(named: "--center-z")
    let bucketOffsetsPath = try argument(named: "--bucket-offsets")
    let bucketCountsPath = try argument(named: "--bucket-counts")
    let bucketCellsPath = try argument(named: "--bucket-cells")
    let elevationPath = try argument(named: "--elevation")
    let width = try UInt32(argument(named: "--width")).unwrap("width")
    let height = try UInt32(argument(named: "--height")).unwrap("height")
    let radius = try Float(argument(named: "--radius")).unwrap("radius")
    let latBuckets = try Int32(argument(named: "--lat-buckets")).unwrap("lat-buckets")
    let lonBuckets = try Int32(argument(named: "--lon-buckets")).unwrap("lon-buckets")

    let centerX = try readFloatArray(from: centerXPath)
    let centerY = try readFloatArray(from: centerYPath)
    let centerZ = try readFloatArray(from: centerZPath)
    let bucketOffsets = try readInt32Array(from: bucketOffsetsPath)
    let bucketCounts = try readInt32Array(from: bucketCountsPath)
    let bucketCells = try readInt32Array(from: bucketCellsPath)
    let elevation = try readFloatArray(from: elevationPath)

    guard centerX.count == centerY.count, centerX.count == centerZ.count, centerX.count == elevation.count else
    {
        throw NSError(domain: "MetalCoastHelper", code: 20, userInfo: [NSLocalizedDescriptionKey: "Center/elevation array size mismatch"])
    }

    guard bucketOffsets.count == Int(latBuckets * lonBuckets), bucketCounts.count == Int(latBuckets * lonBuckets) else
    {
        throw NSError(domain: "MetalCoastHelper", code: 21, userInfo: [NSLocalizedDescriptionKey: "Bucket metadata size mismatch"])
    }

    guard let device = MTLCreateSystemDefaultDevice() else
    {
        throw NSError(domain: "MetalCoastHelper", code: 22, userInfo: [NSLocalizedDescriptionKey: "No Metal device available"])
    }

    let library = try device.makeLibrary(source: shaderSource, options: nil)
    guard let function = library.makeFunction(name: "render_heightmap") else
    {
        throw NSError(domain: "MetalCoastHelper", code: 23, userInfo: [NSLocalizedDescriptionKey: "Failed to create Metal render function"])
    }

    let pipeline = try device.makeComputePipelineState(function: function)
    guard let queue = device.makeCommandQueue() else
    {
        throw NSError(domain: "MetalCoastHelper", code: 24, userInfo: [NSLocalizedDescriptionKey: "Failed to create Metal command queue"])
    }

    func makeBuffer<T>(from values: [T], code: Int, label: String) throws -> MTLBuffer
    {
        try values.withUnsafeBytes { bytes in
            guard let buffer = device.makeBuffer(bytes: bytes.baseAddress!, length: bytes.count, options: .storageModeShared) else
            {
                throw NSError(domain: "MetalCoastHelper", code: code, userInfo: [NSLocalizedDescriptionKey: "Failed to create \(label) buffer"])
            }
            return buffer
        }
    }

    let centerXBuffer = try makeBuffer(from: centerX, code: 25, label: "center-x")
    let centerYBuffer = try makeBuffer(from: centerY, code: 26, label: "center-y")
    let centerZBuffer = try makeBuffer(from: centerZ, code: 27, label: "center-z")
    let bucketOffsetsBuffer = try makeBuffer(from: bucketOffsets, code: 28, label: "bucket-offsets")
    let bucketCountsBuffer = try makeBuffer(from: bucketCounts, code: 29, label: "bucket-counts")
    let bucketCellsBuffer = try makeBuffer(from: bucketCells, code: 30, label: "bucket-cells")
    let elevationBuffer = try makeBuffer(from: elevation, code: 31, label: "elevation")

    let pixelCount = Int(width * height)
    guard let outputBuffer = device.makeBuffer(length: pixelCount * MemoryLayout<UInt16>.stride, options: .storageModeShared) else
    {
        throw NSError(domain: "MetalCoastHelper", code: 32, userInfo: [NSLocalizedDescriptionKey: "Failed to create output buffer"])
    }

    var params = RenderParams(width: width, height: height, radius: radius, latBuckets: latBuckets, lonBuckets: lonBuckets)
    let paramsBuffer = try withUnsafeBytes(of: &params) { bytes in
        guard let buffer = device.makeBuffer(bytes: bytes.baseAddress!, length: bytes.count, options: .storageModeShared) else
        {
            throw NSError(domain: "MetalCoastHelper", code: 33, userInfo: [NSLocalizedDescriptionKey: "Failed to create render params buffer"])
        }
        return buffer
    }

    guard let commandBuffer = queue.makeCommandBuffer(),
          let encoder = commandBuffer.makeComputeCommandEncoder() else
    {
        throw NSError(domain: "MetalCoastHelper", code: 34, userInfo: [NSLocalizedDescriptionKey: "Failed to create render command buffer or encoder"])
    }

    encoder.setComputePipelineState(pipeline)
    encoder.setBuffer(centerXBuffer, offset: 0, index: 0)
    encoder.setBuffer(centerYBuffer, offset: 0, index: 1)
    encoder.setBuffer(centerZBuffer, offset: 0, index: 2)
    encoder.setBuffer(bucketOffsetsBuffer, offset: 0, index: 3)
    encoder.setBuffer(bucketCountsBuffer, offset: 0, index: 4)
    encoder.setBuffer(bucketCellsBuffer, offset: 0, index: 5)
    encoder.setBuffer(elevationBuffer, offset: 0, index: 6)
    encoder.setBuffer(paramsBuffer, offset: 0, index: 7)
    encoder.setBuffer(outputBuffer, offset: 0, index: 8)

    let threadWidth = min(pipeline.maxTotalThreadsPerThreadgroup, 256)
    let threadsPerGroup = MTLSize(width: threadWidth, height: 1, depth: 1)
    let threadsPerGrid = MTLSize(width: pixelCount, height: 1, depth: 1)
    encoder.dispatchThreads(threadsPerGrid, threadsPerThreadgroup: threadsPerGroup)
    encoder.endEncoding()

    commandBuffer.commit()
    commandBuffer.waitUntilCompleted()

    if let error = commandBuffer.error
    {
        throw error
    }

    let outputData = Data(bytes: outputBuffer.contents(), count: pixelCount * MemoryLayout<UInt16>.stride)
    try outputData.write(to: URL(fileURLWithPath: outputPath), options: .atomic)
}

do
{
    let mode = optionalArgument(named: "--mode") ?? "coast"
    switch mode
    {
    case "coast":
        try runCoast()
    case "render":
        try runRender()
    default:
        throw NSError(domain: "MetalCoastHelper", code: 35, userInfo: [NSLocalizedDescriptionKey: "Unsupported mode \(mode)"])
    }
}
catch
{
    FileHandle.standardError.write(Data((error.localizedDescription + "\n").utf8))
    exit(1)
}

extension Optional
{
    func unwrap(_ name: String) throws -> Wrapped
    {
        guard let value = self else
        {
            throw NSError(domain: "MetalCoastHelper", code: 12, userInfo: [NSLocalizedDescriptionKey: "Invalid \(name)"])
        }
        return value
    }
}
