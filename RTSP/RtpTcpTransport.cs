using Rtsp.Messages;
using System;

namespace Rtsp
{
    public class RtpTcpTransport : IRtpTransport
    {
        private bool disposedValue;
        private readonly RtspListener rtspListener;

        public event EventHandler<RtspDataEventArgs>? DataReceived;
        public event EventHandler<RtspDataEventArgs>? ControlReceived;

        public int ControlChannel { get; set; } = int.MaxValue;
        public int DataChannel { get; set; } = int.MaxValue;

        public PortCouple Channels => new(DataChannel, ControlChannel);

        public RtpTcpTransport(RtspListener rtspListener)
        {
            this.rtspListener = rtspListener;
        }

        public void WriteToControlPort(ReadOnlySpan<byte> data)
        {
            rtspListener.SendData(ControlChannel, data);
        }

        public void WriteToDataPort(ReadOnlySpan<byte> data)
        {
            rtspListener.SendData(DataChannel, data);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    Stop();
                }
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Ne changez pas ce code. Placez le code de nettoyage dans la méthode 'Dispose(bool disposing)'
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        public void Start()
        {
            rtspListener.DataReceived += RtspListenerDataReceived;
        }

        public void Stop()
        {
            rtspListener.DataReceived -= RtspListenerDataReceived;
        }
        private void RtspListenerDataReceived(object? sender, RtspChunkEventArgs e)
        {
            if (e.Message is RtspData dataMessage && !dataMessage.Data.IsEmpty)
            {
                if (dataMessage.Channel == ControlChannel)
                {
                    ControlReceived?.Invoke(this, new RtspDataEventArgs(dataMessage));
                }
                else if (dataMessage.Channel == DataChannel)
                {
                    DataReceived?.Invoke(this, new RtspDataEventArgs(dataMessage));
                }
            }
        }
    }
}
