using System;

namespace Rtsp
{
    public interface IRtpTransport : IDisposable
    {
        event EventHandler<RtspDataEventArgs>? DataReceived;
        event EventHandler<RtspDataEventArgs>? ControlReceived;

        void Start();
        void Stop();
        void WriteToControlPort(ReadOnlySpan<byte> data);
        void WriteToDataPort(ReadOnlySpan<byte> data);
    }
}