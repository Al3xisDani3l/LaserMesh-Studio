// SPDX-License-Identifier: GPL-3.0-or-later
// Adapted from the PyroGraph preview_rect and preview_stop wire format.
namespace LaserMesh.Hardware.LaserPecker;

/// <summary>Encodes rectangular preview frames for offline validation against PyroGraph.</summary>
public static class RectanglePreviewProtocol
{
    /// <summary>Maps scene XY bounds centered at zero to the LP2 3.16 upper-left-origin field.</summary>
    /// <remarks>Verified on COM8 with a centered 12 by 8 mm rectangle. Scene Y increases upward.</remarks>
    public static byte[] StartCentered(double minimumX, double minimumY, double maximumX, double maximumY)
    {
        if (!double.IsFinite(minimumX) || !double.IsFinite(minimumY)
            || !double.IsFinite(maximumX) || !double.IsFinite(maximumY)
            || minimumX < -50 || maximumX > 50 || minimumY < -50 || maximumY > 50
            || maximumX <= minimumX || maximumY <= minimumY)
            throw new ArgumentOutOfRangeException(nameof(minimumX));
        return Start(50 + minimumX, 50 - maximumY, maximumX - minimumX, maximumY - minimumY);
    }

    /// <summary>Encodes raw device X/Y offsets and dimensions, without applying a scene calibration.</summary>
    public static byte[] Start(double x, double y, double width, double height)
        => Encode(2, Tenths(x, short.MinValue, short.MaxValue), Tenths(y, short.MinValue, short.MaxValue),
            Tenths(width, 1, 1000), Tenths(height, 1, 1000), 1);

    /// <summary>Encodes the rectangular preview stop frame.</summary>
    public static byte[] Stop() => Encode(3, 0, 0, 0, 0, 0);

    private static byte[] Encode(int state, int x, int y, int width, int height, int power)
        => LaserPeckerProtocol.EncodeFields(2, (state, 1), (width, 2), (height, 2), (x, 2),
            (y, 2), (0, 1), (4, 1), (power, 1), (0, 2));

    private static int Tenths(double value, int min, int max)
    {
        if (!double.IsFinite(value) || value < min / 10d || value > max / 10d)
            throw new ArgumentOutOfRangeException(nameof(value));
        return (int)(value * 10);
    }
}
