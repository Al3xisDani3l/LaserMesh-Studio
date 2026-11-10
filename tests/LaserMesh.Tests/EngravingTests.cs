using System.Numerics;
using LaserMesh.Engraving;
using Xunit;

namespace LaserMesh.Tests;

public sealed class EngravingTests
{
    private readonly GCodeCodec codec = new();

    [Fact]
    public void ModalWordsConvertInchesAndIncrementalCoordinates()
    {
        var path = Parse("""
            N10 G20 G91 G0X1
            N20 G1 Y.5 F10 M4 S80 (marcado)
            N30 X-1
            N40 M5 G21 G90 X0 Y0
            N50 M2
            """);
        Assert.Equal(4, path.Segments.Count);
        Assert.True(path.Segments[0].Rapid);
        Assert.Equal(LaserMode.Off, path.Segments[0].Laser);
        Assert.Equal(new Vector3(25.4f, 12.7f, 0), path.Segments[1].End);
        Assert.Equal(254, path.Segments[1].Feed);
        Assert.Equal(LaserMode.Dynamic, path.Segments[1].Laser);
        Assert.Equal(80, path.Segments[1].Power);
        Assert.Equal(new Vector3(0, 12.7f, 0), path.Segments[2].End);
        Assert.Equal(LaserMode.Off, path.Segments[3].Laser);
    }

    [Fact]
    public void PlaybackInterpolatesAndClampsToTheProgram()
    {
        var path = Parse("G21 G90 G1 F60 X10");
        Assert.Equal(10, path.DurationSeconds);
        Assert.Equal(Vector3.Zero, path.PositionAt(-1));
        Assert.Equal(new Vector3(5, 0, 0), path.PositionAt(5));
        Assert.Equal(new Vector3(10, 0, 0), path.PositionAt(30));
    }

    [Fact]
    public void ExportPreservesSegmentsAndDisarmsRapidAndProgramEnd()
    {
        var path = Parse("G0 X2\nM3 S80 G1 F600 X12\nG0 X4\nM4 S30 G1 X8\nM5 G1 X10");
        using var writer = new StringWriter();
        codec.Write(writer, path, default);
        var output = writer.ToString();
        Assert.Contains("M5" + Environment.NewLine + "G0 X4", output);
        Assert.EndsWith("M5" + Environment.NewLine + "M2" + Environment.NewLine, output);
        Assert.Equal(path.Segments, Parse(output).Segments);
    }

    [Theory]
    [InlineData("G2 X10 Y20")]
    [InlineData("G90 G91 X5")]
    [InlineData("G1 X10")]
    [InlineData("G0 X99999999")]
    [InlineData("M3 S-1")]
    [InlineData("G0 X10 (sin cierre")]
    public void RejectsUnsupportedOrAmbiguousBlocksWithLineNumber(string program)
    {
        var error = Assert.Throws<InvalidDataException>(() => Parse("G21\n" + program));
        Assert.Contains("línea 2", error.Message);
    }

    [Fact]
    public void CancellationStopsParsing()
    {
        using var reader = new StringReader("G0 X10");
        Assert.ThrowsAny<OperationCanceledException>(() => codec.Parse(reader, new(true)));
    }

    [Fact]
    public void FieldScalesWithHeightButNarrowsTowardLens()
    {
        var nominal = new EngravingField(110);
        Assert.Equal(100, nominal.PlaneWidth);
        Assert.True(nominal.Contains(new Vector3(50, 50, 0)));
        Assert.False(nominal.Contains(new Vector3(50, 50, 1)));
        Assert.False(nominal.Contains(new Vector3(0, 0, -1)));
        var raised = new EngravingField(220);
        Assert.Equal(200, raised.PlaneWidth);
        Assert.True(raised.Contains(new Vector3(50, 50, 110)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EngravingField(double.NaN));
    }

    private Toolpath Parse(string program)
    {
        using var reader = new StringReader(program);
        return codec.Parse(reader, default);
    }
}
