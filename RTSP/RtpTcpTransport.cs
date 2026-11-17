using Rtsp.Messages;
using System;
using System.Threading.Tasks;

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

        /// <summary>
        /// Get the control and data channels numbers
        /// </summary>
        public PortCouple Channels => new(DataChannel, ControlChannel);

        public RtpTcpTransport(RtspListener rtspListener)
        {
            this.rtspListener = rtspListener;
        }

        public void WriteToControlPort(ReadOnlySpan<byte> data) => rtspListener.SendData(ControlChannel, data);

        public Task WriteToControlPortAsync(ReadOnlyMemory<byte> data) => rtspListener.SendDataAsync(ControlChannel, data);

        public void WriteToDataPort(ReadOnlySpan<byte> data) => rtspListener.SendData(DataChannel, data);

        public Task WriteToDataPortAsync(ReadOnlyMemory<byte> data) => rtspListener.SendDataAsync(DataChannel, data);

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
        // the arguments, filled again for each packet: valid only during the call
        private RtspDataEventArgs? _eventArgs;

        private void RtspListenerDataReceived(object? sender, RtspChunkEventArgs e)
        {
            if (e.Message is not RtspData dataMessage || dataMessage.Data.IsEmpty) return;
            _eventArgs ??= new RtspDataEventArgs(dataMessage);
            _eventArgs.Data = dataMessage;
            if (dataMessage.Channel == ControlChannel)
            {
                ControlReceived?.Invoke(this, _eventArgs);
            }
            else if (dataMessage.Channel == DataChannel)
            {
                DataReceived?.Invoke(this, _eventArgs);
            }
        }
    }
}
