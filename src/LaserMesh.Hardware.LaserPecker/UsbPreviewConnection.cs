using System.Diagnostics;
using System.IO.Ports;

namespace LaserMesh.Hardware.LaserPecker;

/// <summary>Exclusive framed connection used by preview sessions and simulated transports.</summary>
public interface IPreviewConnection : IDisposable
{
    /// <summary>Writes one frame or raw file fragment.</summary>
    void Send(byte[] bytes);
    /// <summary>Writes bounded fragments while observing cancellation; implementations may override for large files.</summary>
    void Send(byte[] bytes, CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); Send(bytes); }
    /// <summary>Receives a matching frame within a bounded time, observing cancellation.</summary>
    LaserPeckerFrame Receive(Func<LaserPeckerFrame, bool> matches, CancellationToken cancellationToken);
}

/// <summary>USB serial connection using the LP2 460800 baud, 8N1 wire format.</summary>
public sealed class UsbPreviewConnection : IPreviewConnection
{
    private readonly SerialPort port;
    private readonly FrameAssembler assembler = new();
    private readonly Queue<LaserPeckerFrame> pending = new();

    /// <summary>Opens an explicitly selected COM port. Call outside the UI thread.</summary>
    public UsbPreviewConnection(string portName)
    {
        port = new(portName, 460800, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = 100, WriteTimeout = 2000, Handshake = Handshake.None,
            DtrEnable = false, RtsEnable = false
        };
        try { port.Open(); port.DiscardInBuffer(); }
        catch { port.Dispose(); throw; }
    }

    /// <inheritdoc/>
    public void Send(byte[] bytes) => Send(bytes, CancellationToken.None);

    /// <inheritdoc/>
    public void Send(byte[] bytes, CancellationToken cancellationToken)
    {
        for (var offset = 0; offset < bytes.Length; offset += 1024)
        {
            cancellationToken.ThrowIfCancellationRequested();
            port.Write(bytes, offset, Math.Min(1024, bytes.Length - offset));
        }
    }

    /// <inheritdoc/>
    public LaserPeckerFrame Receive(Func<LaserPeckerFrame, bool> matches, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var bytes = new byte[4096];
        while (clock.Elapsed < TimeSpan.FromSeconds(5))
        {
            cancellationToken.ThrowIfCancellationRequested();
            while (pending.TryDequeue(out var frame))
                if (matches(frame))
                    return frame;
            try
            {
                var count = port.Read(bytes, 0, bytes.Length);
                foreach (var frame in assembler.Feed(bytes.AsSpan(0, count)))
                    pending.Enqueue(frame);
            }
            catch (TimeoutException) { }
        }
        throw new TimeoutException("El LP2 no confirmó el paso USB.");
    }

    /// <inheritdoc/>
    public void Dispose() => port.Dispose();
}
