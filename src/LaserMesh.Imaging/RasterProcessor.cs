// SPDX-License-Identifier: GPL-3.0-or-later
// Floyd-Steinberg and raster packing ported from PyroGraph imaging.py. See NOTICE.md.
namespace LaserMesh.Imaging;

/// <summary>One encoded monochrome raster, zero burns and 255 skips.</summary>
/// <param name="Width">Pixel width.</param>
/// <param name="Height">Pixel height.</param>
/// <param name="Payload">Encoded rows.</param>
/// <param name="Packed">Whether rows use MSB-first bits with byte padding.</param>
public sealed record RasterImage(int Width, int Height, byte[] Payload, bool Packed);

/// <summary>Portable deterministic raster preparation without image-decoder or UI dependencies.</summary>
public static class RasterProcessor
{
    /// <summary>Applies conventional brightness/contrast and PyroGraph-equivalent Floyd-Steinberg dithering.</summary>
    public static RasterImage Prepare(ReadOnlySpan<byte> gray, int width, int height, bool packed = false,
        double brightness = 0, double contrast = 0, bool inverse = false, CancellationToken cancellationToken = default)
    {
        if (width is <= 0 or > 8192 || height is <= 0 or > 8192 || (long)width * height > 16_777_216
            || gray.Length != width * height || !(brightness is >= -100 and <= 100) || !(contrast is >= -100 and <= 100))
            throw new ArgumentOutOfRangeException(nameof(width));
        var values = new double[gray.Length];
        var c = contrast * 2.55; var factor = 259 * (c + 255) / (255 * (259 - c));
        for (var i = 0; i < values.Length; i++)
            values[i] = brightness == 0 && contrast == 0 ? gray[i]
                : Math.Clamp(Math.Round(factor * (gray[i] - 128) + 128 + brightness * 2.55), 0, 255);
        for (var y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++)
            {
                var i = y * width + x; var old = values[i]; var next = old >= 128 ? 255 : 0;
                values[i] = next; var error = old - next;
                if (x + 1 < width) values[i + 1] += error * 7 / 16;
                if (y + 1 < height)
                {
                    if (x > 0) values[i + width - 1] += error * 3 / 16;
                    values[i + width] += error * 5 / 16;
                    if (x + 1 < width) values[i + width + 1] += error / 16;
                }
            }
        }
        var mono = values.Select(v => (byte)((v >= 128) != inverse ? 255 : 0)).ToArray();
        if (!packed) return new(width, height, mono, false);
        var stride = (width + 7) / 8; var bits = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++) if (mono[y * width + x] != 0) bits[y * stride + (x >> 3)] |= (byte)(0x80 >> (x & 7));
        }
        return new(width, height, bits, true);
    }
}
