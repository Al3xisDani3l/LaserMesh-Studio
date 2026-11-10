using System.Globalization;
using System.Numerics;

namespace LaserMesh.Engraving;

/// <summary>Reads and exports the supported linear laser G-code subset without device access.</summary>
public sealed class GCodeCodec
{
    private const int MaxSegments = 100_000;
    private const int MaxFileBytes = 8 * 1024 * 1024;
    private const double RapidFeed = 3000;

    /// <summary>Reads a G-code file on a worker thread and converts coordinates to millimetres.</summary>
    public Task<Toolpath> OpenAsync(string path, CancellationToken cancellationToken)
        => Task.Run(() =>
        {
            using var stream = File.OpenRead(path);
            if (stream.Length > MaxFileBytes)
                throw new InvalidDataException("El archivo G-code excede 8 MiB.");
            using var reader = new StreamReader(stream);
            return Parse(reader, cancellationToken);
        }, cancellationToken);

    /// <summary>Parses linear movements, units, positioning, feed and laser commands.</summary>
    public Toolpath Parse(TextReader reader, CancellationToken cancellationToken)
    {
        var state = new State();
        var segments = new List<ToolpathSegment>();
        var lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lineNumber++;
            try
            {
                if (line.Length > 4096)
                    throw new FormatException("Bloque demasiado largo.");
                if (ReadBlock(line, state, segments))
                    break;
            }
            catch (FormatException ex)
            {
                throw new InvalidDataException($"G-code, línea {lineNumber}: {ex.Message}", ex);
            }
        }
        if (segments.Count == 0)
            throw new InvalidDataException("El programa no contiene movimientos.");
        return new Toolpath(segments);
    }

    /// <summary>Exports the simulated geometry as absolute linear movements in millimetres.</summary>
    public async Task SaveAsync(string path, Toolpath toolpath, CancellationToken cancellationToken)
    {
        var destination = Path.GetFullPath(path);
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".gcode-{Guid.NewGuid():N}.tmp");
        try
        {
            await Task.Run(() =>
            {
                using var writer = File.CreateText(temporary);
                Write(writer, toolpath, cancellationToken);
            }, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    /// <summary>Writes normalized toolpath blocks. The output is generic G-code.</summary>
    public void Write(TextWriter writer, Toolpath toolpath, CancellationToken cancellationToken)
    {
        writer.WriteLine("; LaserMesh Studio - trayectoria lineal generica");
        writer.WriteLine("G21 G90 M5");
        var mode = LaserMode.Off;
        var power = 0d;
        foreach (var segment in toolpath.Segments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (mode != segment.Laser || power != segment.Power)
            {
                writer.WriteLine(segment.Laser switch
                {
                    LaserMode.Constant => FormattableString.Invariant($"M3 S{Number(segment.Power)}"),
                    LaserMode.Dynamic => FormattableString.Invariant($"M4 S{Number(segment.Power)}"),
                    _ => "M5"
                });
                mode = segment.Laser;
                power = segment.Power;
            }
            var p = segment.End;
            var move = FormattableString.Invariant($"X{Number(p.X)} Y{Number(p.Y)} Z{Number(p.Z)}");
            writer.WriteLine(segment.Rapid ? $"G0 {move}" : $"G1 {move} F{Number(segment.Feed)}");
        }
        writer.WriteLine("M5");
        writer.WriteLine("M2");
    }

    private static string Number(double value) => value.ToString("0.####################", CultureInfo.InvariantCulture);

    private static bool ReadBlock(string line, State state, List<ToolpathSegment> segments)
    {
        double? x = null, y = null, z = null, feed = null;
        var seen = 0;
        for (var i = 0; i < line.Length;)
        {
            var letter = char.ToUpperInvariant(line[i++]);
            if (char.IsWhiteSpace(letter))
                continue;
            if (letter == ';')
                break;
            if (letter == '(')
            {
                i = line.IndexOf(')', i);
                if (i < 0)
                    throw new FormatException("Comentario sin cierre.");
                i++;
                continue;
            }
            var start = i;
            if (i < line.Length && line[i] is '+' or '-')
                i++;
            while (i < line.Length && (char.IsAsciiDigit(line[i]) || line[i] == '.'))
                i++;
            if (!double.TryParse(line.AsSpan(start, i - start), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
                throw new FormatException($"Valor inválido para {letter}.");

            switch (letter, value)
            {
                case ('G', 0): SetGroup(1); state.Rapid = true; break;
                case ('G', 1): SetGroup(1); state.Rapid = false; break;
                case ('G', 20): SetGroup(2); state.Units = 25.4; break;
                case ('G', 21): SetGroup(2); state.Units = 1; break;
                case ('G', 90): SetGroup(4); state.Absolute = true; break;
                case ('G', 91): SetGroup(4); state.Absolute = false; break;
                case ('M', 3): SetGroup(8); state.Laser = LaserMode.Constant; break;
                case ('M', 4): SetGroup(8); state.Laser = LaserMode.Dynamic; break;
                case ('M', 5): SetGroup(8); state.Laser = LaserMode.Off; break;
                case ('M', 2 or 30): SetGroup(16); break;
                case ('X', _): SetGroup(32); x = value; break;
                case ('Y', _): SetGroup(64); y = value; break;
                case ('Z', _): SetGroup(128); z = value; break;
                case ('F', _): SetGroup(256); feed = value; break;
                case ('S', >= 0 and <= 1e6): SetGroup(512); state.Power = value; break;
                case ('N', >= 0) when value == Math.Truncate(value): SetGroup(1024); break;
                default: throw new FormatException($"Comando o valor sin soporte: {letter}{Number(value)}.");
            }
        }
        if (feed.HasValue)
        {
            state.Feed = feed.Value * state.Units;
            if (state.Feed is < 0.001 or > 1e6)
                throw new FormatException("Avance fuera de rango.");
        }
        if (x.HasValue || y.HasValue || z.HasValue)
        {
            var current = state.Position;
            var target = new Vector3(Coordinate(x, current.X), Coordinate(y, current.Y), Coordinate(z, current.Z));
            if (target != current)
            {
                if (segments.Count >= MaxSegments)
                    throw new FormatException("Se alcanzó el límite de 100 000 segmentos.");
                var speed = state.Rapid ? RapidFeed : state.Feed;
                if (speed <= 0)
                    throw new FormatException("G1 requiere un avance F positivo.");
                var mode = state.Rapid || state.Power == 0 ? LaserMode.Off : state.Laser;
                state.Seconds += Vector3.Distance(current, target) * 60 / speed;
                segments.Add(new(current, target, state.Rapid, speed, mode, mode == LaserMode.Off ? 0 : state.Power,
                    state.Seconds));
                state.Position = target;
            }
        }
        return (seen & 16) != 0;

        float Coordinate(double? value, float current)
        {
            if (!value.HasValue)
                return current;
            var mm = value.Value * state.Units + (state.Absolute ? 0 : current);
            if (Math.Abs(mm) > 1e6)
                throw new FormatException("Coordenada fuera de ±1 000 000 mm.");
            return (float)Math.Round(mm, 6);
        }

        void SetGroup(int group)
        {
            if ((seen & group) != 0)
                throw new FormatException("Palabra repetida o modos incompatibles en el mismo bloque.");
            seen |= group;
        }
    }

    private sealed class State
    {
        public Vector3 Position;
        public double Units = 1;
        public bool Absolute = true;
        public bool Rapid = true;
        public double Feed;
        public LaserMode Laser;
        public double Power;
        public double Seconds;
    }
}
