using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;

namespace Rtsp.Rtp
{
    public class RawMediaFrame : IDisposable
    {
        private bool disposedValue;
        private readonly ReadOnlySequence<byte> _data;
        private readonly IDisposable? _memoryOwner;

        public ReadOnlySequence<byte> Data
        {
            get
            {
                if (disposedValue) throw new ObjectDisposedException(nameof(RawMediaFrame));
                return _data;
            }
        }

        public required DateTime ClockTimestamp { get; init; }
        public required uint RtpTimestamp { get; init; }

        public RawMediaFrame(ReadOnlySequence<byte> data, IDisposable? memoryOwner)
        {
            _data = data;
            _memoryOwner = memoryOwner;
        }

        public bool Any() => !Data.IsEmpty;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _memoryOwner?.Dispose();
                }
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        public static RawMediaFrame Empty => new(ReadOnlySequence<byte>.Empty, null) { RtpTimestamp = 0, ClockTimestamp = DateTime.MinValue };
    }
}