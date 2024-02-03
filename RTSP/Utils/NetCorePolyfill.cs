#if NETSTANDARD2_0
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace System
{
    internal static class NetCorePolyfill
    {
        public static void Write(this Stream stream, Span<byte> data)
        {
            stream.Write(data.ToArray(), 0, data.Length);
        }

        public static void Write(this Stream stream, ReadOnlySpan<byte> data)
        {
            stream.Write(data.ToArray(), 0, data.Length);
        }

        public static async Task<int> ReadAsync(this Stream stream, Memory<byte> data, CancellationToken cancellation)
        {
            byte[] buffer = new byte[data.Length];
            int ret = await stream.ReadAsync(buffer, 0, buffer.Length, cancellation).ConfigureAwait(false);
            buffer.CopyTo(data);
            return ret;
        }
    }
}
#endif