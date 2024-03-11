using System;

namespace Rtsp
{
    public class RtpTcpTransport : IRtpTransport
    {
        private bool disposedValue;
        private readonly RtspListener rtspListener;

        public int ControlChannel { get; set; }
        public int DataChannel { get; set; }

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
                    // TODO: supprimer l'état managé (objets managés)
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
    }
}
