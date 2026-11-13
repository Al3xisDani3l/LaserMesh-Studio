using System.Diagnostics;
using System.Threading.Channels;

namespace LaserMesh.Hardware.LaserPecker;

/// <summary>Owns one USB preview session, live replacements, monitoring and confirmed shutdown.</summary>
public sealed class UsbPreviewSession
{
    /// <summary>True when shutdown could not be confirmed. Blocks another start on this instance.</summary>
    public bool UnknownState { get; private set; }

    /// <summary>Runs on a worker thread. Cancellation, errors and the 60-second limit trigger a stop attempt.</summary>
    public Task RunAsync(Func<IPreviewConnection> connect, ChannelReader<UsbPreviewJob> updates,
        IProgress<string> progress, CancellationToken cancellationToken) => Task.Run(() =>
    {
        if (UnknownState)
            throw new InvalidOperationException("Estado del láser desconocido. Verifica reposo antes de otro inicio.");
        using var link = connect();
        byte[]? stop = null;
        try
        {
            var initial = Status(cancellationToken);
            RequireIdle(initial);
            link.Send(LaserPeckerProtocol.Query(3));
            var version = LaserPeckerReplies.Version(link.Receive(f => f.Function == 0 && f.Payload.Length is >= 6 and < 21,
                cancellationToken));
            if (version.Software != 316 || initial.ActiveLink != 2)
                throw new InvalidDataException("Esta previsualización USB requiere LP2 firmware 3.16 y enlace USB.");

            var clock = Stopwatch.StartNew();
            var nextPoll = TimeSpan.Zero;
            while (clock.Elapsed < TimeSpan.FromSeconds(60))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (updates.TryRead(out var job))
                {
                    while (updates.TryRead(out var newer)) job = newer;
                    if (stop is not null)
                    {
                        StopConfirmed();
                        stop = null;
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    // Also stop after a partial/failed upload; never abandon an unconfirmed device state.
                    stop = job.Stop;
                    if (job.File is { } file)
                    {
                        progress.Report("USB · transfiriendo contorno…");
                        link.Send(AndroidVectorPreviewProtocol.AnnounceFile(file.Length));
                        Ack(1);
                        link.Send(file[..64]);
                        Pause(100, cancellationToken);
                        link.Send(file[64..]);
                        Ack(2);
                        // USB storage ACK completes transfer. Android FinishFile is not used here.
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    if (updates.TryPeek(out _))
                        continue;
                    link.Send(job.Start);
                    Pause(400, cancellationToken);
                    var active = Status(cancellationToken);
                    if (active.Mode != 2 || active.Error != 0)
                        throw new IOException("El LP2 no confirmó previsualización.");
                    progress.Report($"EN VIVO · {(job.File is null ? "rectángulo" : "contorno")} · USB · nivel 1 · límite 60 s · mueve la pieza o el encuadre");
                    nextPoll = clock.Elapsed + TimeSpan.FromSeconds(1);
                }
                if (stop is not null && clock.Elapsed >= nextPoll)
                {
                    var active = Status(cancellationToken);
                    if (active.Mode != 2 || active.Error != 0)
                        throw new IOException("La previsualización se interrumpió en el LP2.");
                    nextPoll = clock.Elapsed + TimeSpan.FromSeconds(1);
                }
                Pause(100, cancellationToken);
            }
        }
        finally
        {
            if (stop is not null)
            {
                progress.Report("USB · deteniendo proyección…");
                StopConfirmed();
                progress.Report("USB · parada confirmada · equipo en reposo");
            }
        }

        WorkingStatus Status(CancellationToken token)
        {
            link.Send(LaserPeckerProtocol.Query(0));
            return LaserPeckerReplies.Status(link.Receive(f => f.Function == 0 && f.Payload.Length >= 21, token));
        }
        void Ack(byte state)
        {
            var reply = link.Receive(f => f.Function == 5 && f.Payload.Length >= 3
                && f.Payload.Span[0] == state && f.Payload.Span[2] == 0x25, cancellationToken);
            if (LaserPeckerReplies.FileAcknowledgement(reply) != 1)
                throw new IOException("LP2 rechazó la transferencia.");
        }
        void StopConfirmed()
        {
            try
            {
                link.Send(stop!);
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                Pause(1200, deadline.Token);
                RequireIdle(Status(deadline.Token));
            }
            catch (Exception ex)
            {
                UnknownState = true;
                throw new IOException("Estado del láser desconocido. Usa la parada física; otro inicio queda bloqueado.", ex);
            }
        }
    }, CancellationToken.None);

    private static void RequireIdle(WorkingStatus status)
    {
        if (!status.Finished || status.Error != 0)
            throw new IOException("El LP2 no está en reposo o reporta un error.");
    }

    private static void Pause(int milliseconds, CancellationToken token)
    {
        token.WaitHandle.WaitOne(milliseconds);
        token.ThrowIfCancellationRequested();
    }
}
