using System;

namespace Rtsp
{
    public interface IRtpTransport : IDisposable
    {
        void WriteToControlPort(ReadOnlySpan<byte> data);
        void WriteToDataPort(ReadOnlySpan<byte> data);
    }
}