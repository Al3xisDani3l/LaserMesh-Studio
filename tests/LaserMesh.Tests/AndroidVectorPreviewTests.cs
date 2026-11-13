using LaserMesh.Hardware.LaserPecker;
using Xunit;

namespace LaserMesh.Tests;

public sealed class AndroidVectorPreviewTests
{
    [Fact]
    public void CenteredRectangleUsesUpperLeftDeviceOrigin()
    {
        Assert.Equal(RectanglePreviewProtocol.Start(44, 46, 12, 8),
            RectanglePreviewProtocol.StartCentered(-6, -4, 6, 4));
        Assert.Equal(RectanglePreviewProtocol.Start(55, 30, 10, 10),
            RectanglePreviewProtocol.StartCentered(5, 10, 15, 20));
        Assert.Throws<ArgumentOutOfRangeException>(() => RectanglePreviewProtocol.StartCentered(-51, 0, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => RectanglePreviewProtocol.StartCentered(0, 0, 0, 1));
    }

    [Fact]
    public void FileCarriesTheSameBigEndianIdAsThePreviewFrame()
    {
        var program = "G21\nG90\nM5\nG1 X5 Y3 F1200\n"u8;
        var file = AndroidVectorPreviewProtocol.EncodeFile(0x12345678, "Guia", program);
        Assert.Equal(64 + program.Length, file.Length);
        Assert.Equal(0x20, file[0]);
        Assert.Equal("12345678", Convert.ToHexString(file.AsSpan(5, 4)));
        Assert.Equal("Guia"u8.ToArray(), file[34..38]);
        Assert.All(file[9..34].Concat(file[38..64]), value => Assert.Equal(0, value));
        Assert.Equal(program.ToArray(), file[64..]);
        Assert.Equal("AABB11020112345678FFEC001E60040100000385",
            Convert.ToHexString(AndroidVectorPreviewProtocol.Start(0x12345678, -20, 30)));
    }

    [Fact]
    public void UploadAndStopUseSeparateCorrelationTags()
    {
        Assert.Equal("AABB0905010000006425008F", Convert.ToHexString(AndroidVectorPreviewProtocol.AnnounceFile(100)));
        Assert.Equal("AABB08FF19000000000118", Convert.ToHexString(AndroidVectorPreviewProtocol.FinishFile()));
        Assert.Equal("AABB08FF4000000000013F", Convert.ToHexString(AndroidVectorPreviewProtocol.Stop()));
    }

    [Fact]
    public void RejectsMissingIdentifiersAndInvalidFileSizes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AndroidVectorPreviewProtocol.Start(0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => AndroidVectorPreviewProtocol.AnnounceFile(64));
        Assert.Throws<ArgumentException>(() => AndroidVectorPreviewProtocol.EncodeFile(1, new string('é', 15), "M5"u8));
    }
}
