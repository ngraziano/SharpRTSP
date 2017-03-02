using System;
using System.Collections.Generic;


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
    CJOCh264encoder h264encoder = null;

    uint width = 0;
    uint height = 0;

    List<byte> nal = new List<byte>();

    // Constuctor
    public SimpleH264Encoder(uint width, uint height, uint fps)
    {
        // We have the ability to set the aspect ratio (SAR).
        // For now we set to 1:1
        uint SARw = 1;
        uint SARh = 1;

        // Initialise H264 encoder. The original C++ code writes to a file. In this port it writes to a List<byte>
        h264encoder = new CJOCh264encoder(nal);
        h264encoder.IniCoder(width, height, fps, CJOCh264encoder.enSampleFormat.SAMPLE_FORMAT_YUV420p, SARw, SARh);

        this.width = width;
        this.height = height;

        // NAL array will contain SPS and PPS

    }

    // Raw SPS with no Size Header and no 00 00 00 01 headers
    public byte[] GetRawSPS()
    {
        byte[] sps_with_header = h264encoder.sps;
        byte[] sps = new byte[sps_with_header.Length - 4];
        System.Array.Copy(sps_with_header, 4, sps, 0, sps.Length);
        return sps;
    }

    public byte[] GetRawPPS()
    {
        byte[] pps_with_header = h264encoder.pps;
        byte[] pps = new byte[pps_with_header.Length - 4];
        System.Array.Copy(pps_with_header, 4, pps, 0, pps.Length);
        return pps;
    }

    public byte[] CompressFrame(byte[] yuv_data)
    {
        byte[] image = h264encoder.GetFramePtr();
        // copy over the YUV image
        System.Array.Copy(yuv_data, image, image.Length);

//        // HACK. Set the YUV pixels all to 127
//        for (int hack = 0; hack < image.Length; hack++) image[hack] = 127;

        h264encoder.CodeAndSaveFrame();

        // Get the NAL (which has the 00 00 00 01 header)
        byte[] nal_with_header = h264encoder.nal;
        byte[] nal = new byte[nal_with_header.Length - 4];
        System.Array.Copy(nal_with_header, 4, nal, 0, nal.Length);
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

