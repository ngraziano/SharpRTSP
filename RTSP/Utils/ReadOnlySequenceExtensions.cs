using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace Rtsp.Utils
{
    public static class ReadOnlySequenceExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (SequencePosition endOfLinePos, SequencePosition startOfLinePos)? FindEndOfLine(in this ReadOnlySequence<byte> buffer)
        {
            const byte lf = (byte)'\n';
            var crlf = "\r\n"u8;

            var startOfSegment = buffer.Start;
            var position = startOfSegment;
            var findPos = buffer.Start;
            var checkForEndOfLine = false;
            while (buffer.TryGet(ref position, out var segment))
            {
                if (segment.Length == 0)
                    continue;

                if (checkForEndOfLine)
                {
                    var nbToskip = segment.Span[0] == lf ? 1 : 0;
                    return (findPos, buffer.GetPosition(nbToskip, startOfSegment));
                }

                var pos = segment.Span.IndexOfAny(crlf);
                if (pos != -1)
                {
                    findPos = buffer.GetPosition(pos, startOfSegment);
                    // handle \n only
                    if (segment.Span[pos] == lf)
                    {
                        return (findPos, buffer.GetPosition(pos + 1, startOfSegment));
                    }
                    if (pos + 1 < segment.Length)
                    {
                        // handle \r\n ou \r other in same segment
                        var nbToskip = segment.Span[pos + 1] == lf ? 2 : 1;
                        return (findPos, buffer.GetPosition(pos + nbToskip, startOfSegment));
                    }

                    // need to wait for next segment
                    checkForEndOfLine = true;
                }

                startOfSegment = position;
            }
            return default;
        }
    }
}
