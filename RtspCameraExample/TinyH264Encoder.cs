using System;
using System.Collections.Generic;
using System.Linq;


// Tiny H264 Encoder
// World's Smallest h.264 Encoder, by Ben Mesander.
// https://cardinalpeak.com/blog/worlds-smallest-h-264-encoder/
//
// Ported to C# by Roger Hardiman www.rjh.org.uk

// Input: YUV image that must be 128x96
// Output: H264 NAL
//
// This is a very simple lossless H264 encoder. No compression is used and so the output NAL data is as
// large as the input YUV data.
// It is used for a quick example of H264 encoding in pure .Net without needing OS specific APIs
// or cross compiled C libraries.
//
// The H264 SPS/PPS data includes the image size. As the SPS/PPS is hard coded in this example the YUV
// image size must be 128 x 96

public class TinyH264Encoder
{

    int width = 0;
    int height = 0;
    int uv_width = 0;
    int uv_height = 0;
    int y_size = 0;
    int u_size = 0;
    int v_size = 0;

    byte[] sps = { 0x67, 0x42, 0x00, 0x0a, 0xf8, 0x41, 0xa2 };
    //byte[] sps_b = { 0x00, 0x00, 0x00, 0x01, 0x67, 0x42, 0x00, 0x0a, 0xf8, 0x41, 0xa2 }; // Annex B
    //byte[] sps32 = { 0x00, 0x00, 0x00, 0x07, 0x67, 0x42, 0x00, 0x0a, 0xf8, 0x41, 0xa2 }; // 32 bit size

    byte[] pps = { 0x68, 0xce, 0x38, 0x80 };
    //byte[] pps_b = { 0x00, 0x00, 0x00, 0x01, 0x68, 0xce, 0x38, 0x80 }; // Annex B
    //byte[] pps32 = { 0x00, 0x00, 0x00, 0x04, 0x68, 0xce, 0x38, 0x80 }; // 32 bit size

    byte[] slice_header = { 0x05, 0x88, 0x84, 0x21, 0xa0 };
    //byte[] slice_header_b = { 0x00, 0x00, 0x00, 0x01, 0x05, 0x88, 0x84, 0x21, 0xa0 };
    //byte[] slice_header_32 = { 0x00, 0x00, 0x00, 0x00, 0x05, 0x88, 0x84, 0x21, 0xa0 }; // must replace size bytes
    byte[] slice_end = { 0x80 };
    byte[] macroblock_header = { 0x0d, 0x00 };

    List<byte> nal = new List<byte>();

    // Constuctor
    public TinyH264Encoder()
    {
        this.width = 128;  // Hard coded size that is embedded in the SPS/PPS data
        this.height = 96;  // Hard coded size that is embedded in the SPS/PPS data
        this.uv_width = width >> 1;
        this.uv_height = height >> 1;
        this.y_size = width * height;
        this.u_size = (width >> 1) * (height >> 1);
        this.v_size = (width >> 1) * (height >> 1);
    }

    public byte[] GetRawSPS()
    {
        return sps.ToArray();
    }

    public byte[] GetRawPPS()
    {
        return pps.ToArray();
    }

    public byte[] CompressFrame(byte[] yuv_data)
    {
        // we can only do 128 x 96
        if (width != 128) return null;
        if (height != 96) return null;

        // check size
        if (yuv_data.Length < (y_size + u_size + v_size))
        {
            // the yuv image is too small.
            return null;
        }

        nal.Clear();

        // Slice Header
        foreach (byte b in slice_header) nal.Add(b);

        // Add each macro block
        for (int i = 0; i < (height / 16); i++) {
            for (int j = 0; j < (width / 16); j++) {
                macroblock(i, j, yuv_data);
            }
        }

        // Add slice end
        foreach (byte b in slice_end) nal.Add(b);

        byte[] nal_array = nal.ToArray();

        return nal_array;
    }

    /* Write a macroblock's worth of YUV data in I_PCM mode */
    private void macroblock(int i, int j, byte[] frame)
    {
        int x, y;

        if (!((i == 0) && (j == 0)))
        {
            foreach (byte b in macroblock_header) nal.Add(b);
        }

        for (x = i * 16; x < ((i + 1) * 16); x++)
        {
            for (y = j * 16; y < ((j + 1) * 16); y++)
            {
                nal.Add(frame[(x * width) + y]);
            }
        }
        for (x = i * 8; x < (i + 1) * 8; x++)
        {
            for (y = j * 8; y < (j + 1) * 8; y++)
            {
                nal.Add(frame[y_size + (x * uv_width) + y]);
            }
        }
        for (x = i * 8; x < (i + 1) * 8; x++)
        {
            for (y = j * 8; y < (j + 1) * 8; y++)
            {
                nal.Add(frame[y_size + u_size + (x * uv_width) + y]);
            }
        }
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

