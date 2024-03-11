using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;

namespace Rtsp.Rtp
{
    public class RawMediaFrame : IDisposable
    {
        private bool disposedValue;
        private readonly IEnumerable<ReadOnlyMemory<byte>> _data;
        private readonly IEnumerable<IMemoryOwner<byte>> _owners;

        public IEnumerable<ReadOnlyMemory<byte>> Data
        {
            get
            {
                if (disposedValue) throw new ObjectDisposedException(nameof(RawMediaFrame));
                return _data;
            }
        }

        public DateTime Timestamp { get; }

        public RawMediaFrame() : this([], [], DateTime.MinValue)
        {
        }

        public RawMediaFrame(IEnumerable<ReadOnlyMemory<byte>> data, IEnumerable<IMemoryOwner<byte>> owners, DateTime timestamp)
        {
            _data = data;
            _owners = owners;
            Timestamp = timestamp;
        }

        public bool Any() => Data.Any();


        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    foreach (var owner in _owners)
                    {
                        owner.Dispose();
                    }
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