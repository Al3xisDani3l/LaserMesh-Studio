using System.Numerics;
using Clipper2Lib;
using LaserMesh.Geometry;

namespace LaserMesh.Engraving;

/// <summary>Determines the outline used to position a piece.</summary>
public enum PlacementMode
{
    /// <summary>Exterior boundaries of the top-down projection.</summary>
    Silhouette,

    /// <summary>Axis-aligned rectangle of maximum extents in scene XY.</summary>
    Rectangle
}

/// <summary>One or more closed exterior loops in scene XY, in millimetres.</summary>
public sealed class PlacementGuide
{
    private const double UnitsPerMillimetre = 1000;
    private const int MaxSegments = 100_000;
    private const double MaxCoordinate = 1_000_000;
    private const double Feed = 1200;
    private const double RapidFeed = 3000;

    private PlacementGuide(Paths64 paths)
    {
        Loops = Array.AsReadOnly(paths.Select(path => (IReadOnlyList<Vector2>)Array.AsReadOnly(
            path.Select(p => new Vector2((float)(p.X / UnitsPerMillimetre), (float)(p.Y / UnitsPerMillimetre)))
                .ToArray())).ToArray());
        Minimum = Loops.SelectMany(loop => loop).Aggregate(Vector2.Min);
        Maximum = Loops.SelectMany(loop => loop).Aggregate(Vector2.Max);
    }

    /// <summary>Unclosed vertex lists. Each loop closes implicitly from its last vertex to its first.</summary>
    public IReadOnlyList<IReadOnlyList<Vector2>> Loops { get; }

    /// <summary>Minimum scene XY coordinate.</summary>
    public Vector2 Minimum { get; }

    /// <summary>Maximum scene XY coordinate.</summary>
    public Vector2 Maximum { get; }

    /// <summary>Creates a guide from a mesh transformed into scene XY, on a millimetre grid of 0.001 mm.</summary>
    public static PlacementGuide Create(TriangleMesh mesh, Matrix3x2 placement, PlacementMode mode,
        CancellationToken cancellationToken)
    {
        if (mode is not (PlacementMode.Silhouette or PlacementMode.Rectangle))
            throw new ArgumentOutOfRangeException(nameof(mode));

        var triangles = new Paths64();
        var min = new Point64(long.MaxValue, long.MaxValue);
        var max = new Point64(long.MinValue, long.MinValue);
        var hasArea = false;
        var positions = mesh.Positions;
        for (var i = 0; i < positions.Length; i += 3)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var a = Quantize(positions[i]);
            var b = Quantize(positions[i + 1]);
            var c = Quantize(positions[i + 2]);
            var area = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
            if (area == 0)
                continue;
            hasArea = true;
            if (mode == PlacementMode.Silhouette)
                triangles.Add(area > 0 ? new Path64 { a, b, c } : new Path64 { a, c, b });
        }

        if (!hasArea)
            throw new InvalidDataException("La malla no tiene superficie proyectable sobre XY.");

        var paths = new Paths64();
        if (mode == PlacementMode.Rectangle)
        {
            paths.Add(new Path64 { min, new(max.X, min.Y), max, new(min.X, max.Y) });
        }
        else
        {
            var clipper = new Clipper64 { PreserveCollinear = false };
            clipper.AddSubject(triangles);
            var tree = new PolyTree64();
            if (!clipper.Execute(ClipType.Union, FillRule.NonZero, tree))
                throw new InvalidDataException("No se pudo unir la proyección de la malla.");
            for (var i = 0; i < tree.Count; i++)
            {
                if (tree[i].Polygon is { Count: >= 3 } outside)
                    paths.Add(outside);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (paths.Count == 0 || paths.Sum(path => path.Count + 1L) > MaxSegments)
            throw new InvalidDataException("La guía está vacía o excede el límite de 100 000 movimientos.");

        return new PlacementGuide(paths);

        Point64 Quantize(Vector3 p)
        {
            var xy = Vector2.Transform(new(p.X, p.Y), placement);
            if (!(Math.Abs(xy.X) <= MaxCoordinate && Math.Abs(xy.Y) <= MaxCoordinate))
                throw new InvalidDataException("La proyección excede ±1 000 000 mm.");
            var q = new Point64((long)Math.Round(xy.X * UnitsPerMillimetre),
                (long)Math.Round(xy.Y * UnitsPerMillimetre));
            min = new Point64(Math.Min(min.X, q.X), Math.Min(min.Y, q.Y));
            max = new Point64(Math.Max(max.X, q.X), Math.Max(max.Y, q.Y));
            return q;
        }
    }

    /// <summary>Creates an absolute XY program with non-emitting linear moves and rapids between loops.</summary>
    public Toolpath ToToolpath(CancellationToken cancellationToken)
    {
        var segments = new List<ToolpathSegment>();
        var position = Vector3.Zero;
        var seconds = 0d;
        foreach (var loop in Loops)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Move(loop[0], rapid: true);
            for (var i = 1; i < loop.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Move(loop[i], rapid: false);
            }
            Move(loop[0], rapid: false);
        }
        return new Toolpath(segments);

        void Move(Vector2 xy, bool rapid)
        {
            var target = new Vector3(xy, 0);
            if (target == position)
                return;
            var speed = rapid ? RapidFeed : Feed;
            seconds += Vector3.Distance(position, target) * 60 / speed;
            segments.Add(new(position, target, rapid, speed, LaserMode.Off, 0, seconds));
            position = target;
        }
    }
}
