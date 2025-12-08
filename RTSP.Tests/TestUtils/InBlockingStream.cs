using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RTSP.Tests.TestUtils
{
    public class InBlockingStream : Stream
    {
        private bool isDisposed = false;
        private readonly CancellationTokenSource cancellationTokenSource = new();

        public override bool CanRead => !cancellationTokenSource.IsCancellationRequested;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException("Simulate NetworkStream");

        public override long Position { get => throw new NotSupportedException("Simulate NetworkStream"); set => throw new NotSupportedException("Simulate NetworkStream"); }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(InBlockingStream));
            // simulate blocking read for data
            // only unlock on close
            cancellationTokenSource.Token.WaitHandle.WaitOne();
            throw new IOException("Stream closed");
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException("Simulate NetworkStream");
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException("Simulate NetworkStream");
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("Simulate only read");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !isDisposed)
            {
                isDisposed = true;
                cancellationTokenSource.Cancel();
                cancellationTokenSource.Dispose();
            }
        }
    }
}
