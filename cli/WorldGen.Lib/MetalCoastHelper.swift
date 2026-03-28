import Foundation
import Metal

struct Params
{
    var width: UInt32
    var height: UInt32
    var amplitude: Float
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

let shaderSource = """
#include <metal_stdlib>
using namespace metal;

struct Params
{
    uint width;
    uint height;
    float amplitude;
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
"""

do
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
