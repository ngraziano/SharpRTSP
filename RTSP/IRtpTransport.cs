namespace Rtsp;

using System;
using System.Threading.Tasks;

public interface IRtpTransport : IDisposable
{
    event EventHandler<RtspDataEventArgs>? DataReceived;
    event EventHandler<RtspDataEventArgs>? ControlReceived;

    void Start();
    void Stop();

    /// <summary>
    /// Write to the RTP Control Port
    /// </summary>
    /// <param name="data">Buffer to send</param>
    void WriteToControlPort(ReadOnlySpan<byte> data);
    Task WriteToControlPortAsync(ReadOnlyMemory<byte> data);

    /// <summary>
    /// Write to the RTP Data Port
    /// </summary>
    /// <param name="data">Buffer to send</param>
    void WriteToDataPort(ReadOnlySpan<byte> data);
    Task WriteToDataPortAsync(ReadOnlyMemory<byte> data);
}