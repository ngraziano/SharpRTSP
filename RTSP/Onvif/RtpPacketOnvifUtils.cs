using System;
using System.Buffers.Binary;

namespace Rtsp.Onvif;
public static class RtpPacketOnvifUtils
{
    private const ushort MARKER_TS_EXT = 0xABAC;
    private const ushort MARKER_SOF0 = 0xffc0;          // start-of-frame, baseline scan
    private const ushort MARKER_SOI = 0xffd8;           // start of image


    /// <summary>
    /// Provide the Jpeg frame extension method, for frame size > 2048x2048
    /// </summary>
    /// <param name="extension">The header to check</param>
    /// <returns>Frame width and height</returns>
    public static (ushort,ushort) ProcessJpegFrameExtension(ReadOnlySpan<byte> extension)
    {
        var headerPosition = 0;
        ushort frameWidth = 0;
        ushort frameHeight = 0;
        int extensionType = BinaryPrimitives.ReadUInt16BigEndian(extension[headerPosition..]);
        if (extensionType == MARKER_SOI)
        {
            // 2 for type, 2 for length
            headerPosition += sizeof(ushort) + sizeof(ushort);
            int extensionSize = extension.Length;
            while (headerPosition < (extensionSize - (sizeof(ushort) + sizeof(ushort))))
            {
                ushort blockType = BinaryPrimitives.ReadUInt16BigEndian(extension[headerPosition..]);
                ushort blockSize = BinaryPrimitives.ReadUInt16BigEndian(extension[(headerPosition + 2)..]);

                if (blockType == MARKER_SOF0)
                {
                    frameHeight = BinaryPrimitives.ReadUInt16BigEndian(extension[(headerPosition + 5)..]);
                    frameWidth = BinaryPrimitives.ReadUInt16BigEndian(extension[(headerPosition + 7)..]);
                }
                headerPosition += (blockSize + 2);
            }
        }
        return (frameWidth, frameHeight);
    }

    /// <summary>
    /// Extract timestamp from jpeg extension.
    /// </summary>
    /// <param name="extension">The extension header</param>
    /// <param name="headerPosition">returns position after read. Used when JPEG extension is appended to this extension</param>
    /// <returns>Timestamp, as number of milliseconds from 19000101T000000</returns>
    public static ulong ProcessRTPTimestampExtension(ReadOnlySpan<byte> extension, out int headerPosition)
    {
        headerPosition = 0;
        // RTP extension has a minmum length of 4 32bit words (more if JPEG extension is appended).
        if (extension.Length < 4 * 4)
        {
            return 0;
        }

        int extensionType = BinaryPrimitives.ReadUInt16BigEndian(extension);
        if (extensionType != MARKER_TS_EXT)
        {
            return 0;
        }

        headerPosition += sizeof(ushort);
        // var headerLength = BinaryPrimitives.ReadUInt16BigEndian(extension[headerPosition..]);
        //if (headerLength == 3)
        {
            headerPosition += sizeof(ushort);

            uint seconds = BinaryPrimitives.ReadUInt32BigEndian(extension[headerPosition..]);
            uint fractions = BinaryPrimitives.ReadUInt32BigEndian(extension[(headerPosition + sizeof(uint))..]);

            headerPosition += sizeof(uint) + sizeof(uint);

            //uint data = BinaryPrimitives.ReadUInt16BigEndian(extension[headerPosition..]);
            // C        [1 bit]     -> all
            // E        [1 bit]     -> all
            // D        [1 bit]     -> all
            // T        [1 bit]     -> only not jpeg
            // MBZ      [4/5 bits]  -> [4 if not jpeg, 5 in jpeg] reserved
            // CSeq     [8 bits]    -> 1 byte
            // padding  [8 bits]    -> just padding values.

            headerPosition += sizeof(uint);
            ulong msec = fractions * 1000 / uint.MaxValue;
            DateTime dt = new(1900, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            DateTime ret = dt.AddSeconds(seconds).AddMilliseconds(msec);

            return (ulong)ret.Subtract(dt).TotalMilliseconds;
        }
    }
}
