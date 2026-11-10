using System.Globalization;
using System.Numerics;
using LaserMesh.Engraving;
using LaserMesh.Geometry;
using Xunit;

namespace LaserMesh.Tests;

public sealed class PlacementGuideTests
{
    [Fact]
    public void SilhouetteKeepsBothLowerTabsButRectangleUsesMaximumExtents()
    {
        var mesh = Mesh((0, 0, 4, 10, 3), (-2, 1, 0, 3, 1), (-2, 7, 0, 9, 1));
        var contour = PlacementGuide.Create(mesh, Matrix3x2.Identity, PlacementMode.Silhouette, default);
        var outline = Assert.Single(contour.Loops);
        Assert.Equal(12, outline.Count);
        Assert.Contains(new Vector2(0, 3), outline);
        Assert.Contains(new Vector2(0, 7), outline);
        Assert.Contains(new Vector2(-2, 1), outline);
        Assert.Contains(new Vector2(-2, 9), outline);

        var rectangle = PlacementGuide.Create(mesh, Matrix3x2.Identity, PlacementMode.Rectangle, default);
        Assert.Equal(4, Assert.Single(rectangle.Loops).Count);
        Assert.Equal(new Vector2(-2, 0), rectangle.Minimum);
        Assert.Equal(new Vector2(4, 10), rectangle.Maximum);
        Assert.Equal(contour.Minimum, rectangle.Minimum);
        Assert.Equal(contour.Maximum, rectangle.Maximum);
    }

    [Fact]
    public void UnionOmitsInnerHoleAndRetainsSeparateExteriorComponents()
    {
        var mesh = Mesh((0, 0, 5, 1, 0), (0, 4, 5, 5, 0), (0, 1, 1, 4, 0), (4, 1, 5, 4, 0),
            (8, 0, 9, 1, 0));
        var guide = PlacementGuide.Create(mesh, Matrix3x2.Identity, PlacementMode.Silhouette, default);
        Assert.Equal(2, guide.Loops.Count);
        Assert.All(guide.Loops, loop => Assert.Equal(4, loop.Count));
    }

    [Theory]
    [InlineData(PlacementMode.Silhouette)]
    [InlineData(PlacementMode.Rectangle)]
    public void ScenePoseAndExportUseTheSameCoordinates(PlacementMode mode)
    {
        var mesh = Mesh((0, 0, 2, 5, 3), (10, 0, 11, 1, 3));
        var pose = Matrix3x2.CreateScale(2) * Matrix3x2.CreateRotation(MathF.PI / 2)
            * Matrix3x2.CreateTranslation(3, -4);
        var guide = PlacementGuide.Create(mesh, pose, mode, default);
        Assert.Equal(new Vector2(-7, -4), guide.Minimum);
        Assert.Equal(new Vector2(3, 18), guide.Maximum);

        var codec = new GCodeCodec();
        using var output = new StringWriter();
        var source = guide.ToToolpath(default);
        codec.Write(output, source, default);
        var text = output.ToString();
        Assert.Contains("M5", text);
        Assert.DoesNotContain("M3", text);
        Assert.DoesNotContain("M4", text);
        using var input = new StringReader(text);
        var program = codec.Parse(input, default);
        Assert.Equal(source.Segments, program.Segments);
        Assert.Equal(guide.Loops.Count, program.Segments.Count(segment => segment.Rapid));
        Assert.All(program.Segments, segment => Assert.Equal(LaserMode.Off, segment.Laser));
        var loopEnds = program.Segments.Where(segment => !segment.Rapid).Select(segment => segment.End);
        Assert.All(guide.Loops.SelectMany(loop => loop), vertex => Assert.Contains(new Vector3(vertex, 0), loopEnds));
    }

    [Theory]
    [InlineData(PlacementMode.Silhouette)]
    [InlineData(PlacementMode.Rectangle)]
    public void RejectsCollapsedProjectionAndHonorsCancellation(PlacementMode mode)
    {
        var mesh = Mesh((0, 0, 2, 2, 0));
        Assert.Throws<InvalidDataException>(() => PlacementGuide.Create(mesh, Matrix3x2.CreateScale(0), mode, default));
        Assert.ThrowsAny<OperationCanceledException>(() =>
            PlacementGuide.Create(mesh, Matrix3x2.Identity, mode, new CancellationToken(true)));
    }

    private static TriangleMesh Mesh(params (float X1, float Y1, float X2, float Y2, float Z)[] quads)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        writer.WriteLine("solid fixture");
        foreach (var (x1, y1, x2, y2, z) in quads)
        {
            var a = new Vector3(x1, y1, z);
            var b = new Vector3(x2, y1, z);
            var c = new Vector3(x2, y2, z);
            var d = new Vector3(x1, y2, z);
            Add(a, b, c);
            Add(d, c, a); // Deliberately reversed winding.
        }
        writer.WriteLine("endsolid fixture");
        using var stream = new MemoryStream(System.Text.Encoding.ASCII.GetBytes(writer.ToString()));
        return new StlCodec().Read(stream, stream.Length, 1, default);

        void Add(Vector3 a, Vector3 b, Vector3 c)
        {
            writer.WriteLine("facet normal 0 0 1");
            writer.WriteLine("outer loop");
            foreach (var p in new[] { a, b, c })
                writer.WriteLine(FormattableString.Invariant($"vertex {p.X} {p.Y} {p.Z}"));
            writer.WriteLine("endloop");
            writer.WriteLine("endfacet");
        }
    }
}
