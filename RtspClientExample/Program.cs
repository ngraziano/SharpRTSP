using Rtsp.Messages;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Security.Cryptography;

namespace RtspClientExample
{
    class Program
    {
        static void Main(string[] args)
        {
            // Internet Test - Big Buck Bunney
             String url = "rtsp://wowzaec2demo.streamlock.net/vod/mp4:BigBuckBunny_115k.mov";

            // IPS IP Camera Tests
            //String url = "rtsp://192.168.1.128/ch1.h264";

            // Axis Tests
            //String url = "rtsp://192.168.1.125/onvif-media/media.amp?profile=quality_h264";
            //String url = "rtsp://user:password@192.168.1.102/onvif-media/media.amp?profile=quality_h264";

            // Bosch Tests
            //String url = "rtsp://192.168.1.124/rtsp_tunnel?h26x=4&line=1&inst=1";

            // 360 Vision Tests
            //String url = "rtsp://192.168.1.187/h264main";

            // Live555 Server Tests (ONVIF RPOS PROJECT)
            //String url = "rtsp://192.168.1.33:8554/unicast";  // Raspberry Pi RPOS using Mpromonet Live555 server
            //String url = "rtsp://192.168.1.33:8554/h264";  // Raspberry Pi RPOS using Live555
            //String url = "rtsp://192.168.1.121:8554/h264";  // Raspberry Pi RPOS using Live555
            //String url = "rtsp://192.168.1.121:8554/h264m";  // Raspberry Pi RPOS using Live555 in Multicast mode

            // Live555 Server Tests
            //String url = "rtsp://127.0.0.1:8554/h264ESVideoTest";
            //String url = "rtsp://192.168.1.160:8554/h264ESVideoTest";
            //String url = "rtsp://127.0.0.1:8554/h264ESVideoTest";
            //String url = "rtsp://192.168.1.79:8554/amrAudioTest";

            // VLC Server Tests
            // String url = "rtsp://192.168.1.150:8554/test";


            // MJPEG Tests (Payload 26)
            //String url = "rtsp://192.168.1.125/onvif-media/media.amp?profile=mobile_jpeg";

            // H265 Tests

            String now = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            FileStream fs_v = null;   // used to write the video
            FileStream fs_a = null;   // used to write the audio
            bool h264 = false;
            bool h265 = false;

            // Create a RTSP Client
            RTSPClient c = new RTSPClient();

            // The SPS/PPS comes from the SDP data
            // or it is the first SPS/PPS from the H264 video stream
            c.Received_SPS_PPS += (byte[] sps, byte[] pps) => {
                h264 = true;
                if (fs_v == null) {
                    String filename = "rtsp_capture_" + now + ".264";
                    fs_v = new FileStream(filename, FileMode.Create);
                }

                if (fs_v != null) {
                    fs_v.Write(new byte[] { 0x00, 0x00, 0x00, 0x01 }, 0, 4);  // Write Start Code
                    fs_v.Write(sps, 0, sps.Length);
                    fs_v.Write(new byte[] { 0x00, 0x00, 0x00, 0x01 }, 0, 4);  // Write Start Code
                    fs_v.Write(pps, 0, pps.Length);
                    fs_v.Flush(true);
                }
            };

            c.Received_VPS_SPS_PPS += (byte[] vps, byte[] sps, byte[] pps) => {
                h265 = true;
                if (fs_v == null)
                {
                    String filename = "rtsp_capture_" + now + ".265";
                    fs_v = new FileStream(filename, FileMode.Create);
                }

                if (fs_v != null)
                {
                    fs_v.Write(new byte[] { 0x00, 0x00, 0x00, 0x01 }, 0, 4);  // Write Start Code
                    fs_v.Write(vps, 0, vps.Length);
                    fs_v.Write(new byte[] { 0x00, 0x00, 0x00, 0x01 }, 0, 4);  // Write Start Code
                    fs_v.Write(sps, 0, sps.Length);
                    fs_v.Write(new byte[] { 0x00, 0x00, 0x00, 0x01 }, 0, 4);  // Write Start Code
                    fs_v.Write(pps, 0, pps.Length);
                    fs_v.Flush(true);
                }
            };



            // Video NALs. May also include the SPS and PPS in-band for H264
            c.Received_NALs += (List<byte[]> nal_units) => {
                if (fs_v != null) {
                    foreach (byte[] nal_unit in nal_units)
                    {
                        // Output some H264 stream information
                        if (h264 && nal_unit.Length > 0) {
                            int nal_ref_idc  = (nal_unit[0] >> 5) & 0x03;
                            int nal_unit_type = nal_unit[0] & 0x1F;
                            String description = "";
                            if (nal_unit_type == 1) description = "NON IDR NAL";
                            else if (nal_unit_type == 5) description = "IDR NAL";
                            else if (nal_unit_type == 6) description = "SEI NAL";
                            else if (nal_unit_type == 7) description = "SPS NAL";
                            else if (nal_unit_type == 8) description = "PPS NAL";
                            else if (nal_unit_type == 9) description = "ACCESS UNIT DELIMITER NAL";
                            else description = "OTHER NAL";
                            Console.WriteLine("NAL Ref = " + nal_ref_idc + " NAL Type = " + nal_unit_type + " " + description);
                        }

                        // Output some H265 stream information
                        if (h265 && nal_unit.Length > 0)
                        {
                            int nal_unit_type = (nal_unit[0] >> 1) & 0x3F;
                            String description = "";
                            if (nal_unit_type == 1) description = "NON IDR NAL";
                            else if (nal_unit_type == 19) description = "IDR NAL";
                            else if (nal_unit_type == 32) description = "VPS NAL";
                            else if (nal_unit_type == 33) description = "SPS NAL";
                            else if (nal_unit_type == 34) description = "PPS NAL";
                            else if (nal_unit_type == 39) description = "SEI NAL";
                            else description = "OTHER NAL";
                            Console.WriteLine("NAL Type = " + nal_unit_type + " " + description);
                        }

                        fs_v.Write(new byte[] { 0x00, 0x00, 0x00, 0x01 }, 0, 4);  // Write Start Code
                        fs_v.Write(nal_unit, 0, nal_unit.Length);                 // Write NAL
                    }
                    fs_v.Flush(true);
                }
            };

            c.Received_G711 += (string format, List<byte[]> g711) => {
                if (fs_a == null && format.Equals("PCMU")) {
                    String filename = "rtsp_capture_" + now + ".ul";
                    fs_a = new FileStream(filename, FileMode.Create);
                }

                if (fs_a == null && format.Equals("PCMA")) {
                    String filename = "rtsp_capture_" + now + ".al";
                    fs_a = new FileStream(filename, FileMode.Create);
                }

                if (fs_a != null) {
                    foreach (byte[] data in g711) {
                        fs_a.Write(data, 0, data.Length);
                    }
                }
            };

            c.Received_AMR += (string format, List<byte[]> amr) => {
                if (fs_a == null && format.Equals("AMR")) {
                    String filename = "rtsp_capture_" + now + ".amr";
                    fs_a = new FileStream(filename, FileMode.Create);
                    byte[] header = new byte[]{0x23,0x21,0x41,0x4D,0x52,0x0A}; // #!AMR<0x0A>
                    fs_a.Write(header,0,header.Length);
                }

                if (fs_a != null) {
                    foreach (byte[] data in amr) {
                        fs_a.Write(data, 0, data.Length);
                    }
                }
            };
            c.Received_AAC += (string format, List<byte[]> aac, uint ObjectType, uint FrequencyIndex, uint ChannelConfiguration) => {
                if (fs_a == null)
                {
                    String filename = "rtsp_capture_" + now + ".aac";
                    fs_a = new FileStream(filename, FileMode.Create);
                }

                if (fs_a != null)
                {
                    foreach (byte[] data in aac)
                    {
                        // ASDT header format
                        int protection_absent = 1;
//                        int profile = 2; // Profile 2 = AAC Low Complexity (LC)
//                        int sample_freq = 4; // 4 = 44100 Hz
//                        int channel_config = 2; // 2 = Stereo

                        Rtsp.BitStream bs = new Rtsp.BitStream();
                        bs.AddValue(0xFFF,12); // (a) Start of data
                        bs.AddValue(0,1); // (b) Version ID, 0 = MPEG4
                        bs.AddValue(0,2); // (c) Layer always 2 bits set to 0
                        bs.AddValue(protection_absent,1); // (d) 1 = No CRC
                        bs.AddValue((int)ObjectType-1,2); // (e) MPEG Object Type / Profile, minus 1
                        bs.AddValue((int)FrequencyIndex,4); // (f)
                        bs.AddValue(0, 1); // (g) private bit. Always zero
                        bs.AddValue((int)ChannelConfiguration,3); // (h)
                        bs.AddValue(0,1); // (i) originality
                        bs.AddValue(0,1); // (j) home
                        bs.AddValue(0,1); // (k) copyrighted id
                        bs.AddValue(0,1); // (l) copyright id start
                        bs.AddValue(data.Length + 7,13); // (m) AAC data + size of the ASDT header
                        bs.AddValue(2047,11); // (n) buffer fullness ???
                        int num_acc_frames = 1;
                        bs.AddValue(num_acc_frames-1,1); // (o) num of AAC Frames, minus 1

                        // If Protection was On, there would be a 16 bit CRC
                        if (protection_absent == 0) bs.AddValue(0xABCD /*CRC*/,16); // (p)

                        byte[] header = bs.ToArray();

                        fs_a.Write(header, 0, header.Length);
                        fs_a.Write(data, 0, data.Length);
                    }
                }
            };

            // Connect to RTSP Server
            Console.WriteLine("Connecting");

            c.Connect(url, RTSPClient.RTP_TRANSPORT.TCP, RTSPClient.MEDIA_REQUEST.VIDEO_AND_AUDIO);

            // Wait for user to terminate programme
            // Check for null which is returned when running under some IDEs
            // OR wait for the Streaming to Finish - eg an error on the RTSP socket

            Console.WriteLine("Press ENTER to exit");

            String readline = null;
            while (readline == null && c.StreamingFinished() == false) {
                readline = Console.ReadLine();

                // Avoid maxing out CPU on systems that instantly return null for ReadLine
                if (readline == null) Thread.Sleep(500);
            }

            c.Stop();
            Console.WriteLine("Finished");

        }
    }
}
