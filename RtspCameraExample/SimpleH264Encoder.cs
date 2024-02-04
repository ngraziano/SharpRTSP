using System;
using System.Collections.Generic;
using System.Linq;

namespace RtspCameraExample
{


    // Simple H264 Encoder
    // Written by Jordi Cenzano (www.jordicenzano.name)
    //
    // Ported to C# by Roger Hardiman www.rjh.org.uk

    // This is a very simple lossless H264 encoder. No compression is used and so the output NAL data is as
    // large as the input YUV data.
    // It is used for a quick example of H264 encoding in pure .Net without needing OS specific APIs
    // or cross compiled C libraries.
    //
    // SimpleH264Encoder can use any image Width or Height


    public class SimpleH264Encoder
    {
        private readonly CJOCh264encoder h264encoder = new();

        // Constuctor
        public SimpleH264Encoder(int width, int height, uint fps)
        {
            // We have the ability to set the aspect ratio (SAR).
            // For now we set to 1:1
            uint SARw = 1;
            uint SARh = 1;

            // Initialise H264 encoder.
            h264encoder.IniCoder(width, height, fps, CJOCh264encoder.SampleFormat.SAMPLE_FORMAT_YUV420p, SARw, SARh);
            // NAL array will contain SPS and PPS

        }

        // Raw SPS with no Size Header and no 00 00 00 01 headers
        public byte[] GetRawSPS() => h264encoder?.sps?.Skip(4).ToArray() ?? [];

        public byte[] GetRawPPS() => h264encoder?.pps?.Skip(4).ToArray() ?? [];

        public byte[] CompressFrame(Span<byte> yuv_data)
        {
            h264encoder.CodeAndSaveFrame(yuv_data);

            // Get the NAL (which has the 00 00 00 01 header)
            byte[] nal_with_header = h264encoder.nal ?? [0x00, 0x00, 0x00, 0x01];
            byte[] nal = new byte[nal_with_header.Length - 4];
            Array.Copy(nal_with_header, 4, nal, 0, nal.Length);
            return nal;
        }


        public void ChangeAnnexBto32BitSize(byte[] data)
        {

            if (data.Length < 4) return;

            // change data from 0x00 0x00 0x00 0x01 format to 32 bit size
            int len = data.Length - 4;// subtract Annex B header size

            if (BitConverter.IsLittleEndian)
            {
                data[0] = (byte)((len >> 24) & 0xFF);
                data[1] = (byte)((len >> 16) & 0xFF);
                data[2] = (byte)((len >> 8) & 0xFF);
                data[3] = (byte)((len << 0) & 0xFF);
            }
            else
            {
                data[0] = (byte)((len >> 0) & 0xFF);
                data[1] = (byte)((len >> 8) & 0xFF);
                data[2] = (byte)((len >> 16) & 0xFF);
                data[3] = (byte)((len >> 24) & 0xFF);
            }
        }
    }
}