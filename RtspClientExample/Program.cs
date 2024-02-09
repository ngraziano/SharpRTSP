using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading;

namespace RtspClientExample
{
    class Program
    {
        static void Main(string[] args)
        {
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder
                    .AddFilter("Microsoft", LogLevel.Warning)
                    .AddFilter("System", LogLevel.Warning)
                    .AddFilter("RtspClientExample", LogLevel.Debug)
                    .AddFilter("Rtsp", LogLevel.Debug)
                    .AddConsole();
            });

            // Internet Test - Big Buck Bunney
            // string url = "rtsp://mafreebox.freebox.fr/fbxtv_pub/stream?flavour=hd&namespace=1&service=201";
            //string url = "rtsp://wowzaec2demo.streamlock.net/vod/mp4:BigBuckBunny_115k.mp4";
            // IPS IP Camera Tests
            //String url = "rtsp://192.168.1.128/ch1.h264";

            string url = "rtsp://192.168.0.89/media/video1";

            // string url = "http://192.168.3.72/profile1/media.smp";
            string username = "admin";
            string password = "Admin123!";
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
            //String url = "rtsp://127.0.0.1:8554/test";



            // MJPEG Tests (Payload 26)
            //String url = "rtsp://192.168.1.125/onvif-media/media.amp?profile=mobile_jpeg";

            // H265 Tests

            string now = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            FileStream? fs_v = null;   // used to write the video
            FileStream? fs_a = null;   // used to write the audio
            bool h264 = false;
            bool h265 = false;

            // Create a RTSP Client
            RTSPClient client = new(loggerFactory);

            // The SPS/PPS comes from the SDP data
            // or it is the first SPS/PPS from the H264 video stream
            client.ReceivedSpsPps += (_, args) =>
            {
                var sps = args.Sps;
                var pps = args.Pps;

                h264 = true;
                if (fs_v == null)
                {
                    string filename = "rtsp_capture_" + now + ".264";
                    fs_v = new FileStream(filename, FileMode.Create);
                }
                WriteNalToFile(fs_v, sps);
                WriteNalToFile(fs_v, pps);
                fs_v.Flush(true);

            };

            client.ReceivedVpsSpsPps += (_, args) =>
            {
                var vps = args.Vps;
                var sps = args.Sps;
                var pps = args.Pps;
                h265 = true;
                if (fs_v == null)
                {
                    String filename = "rtsp_capture_" + now + ".265";
                    fs_v = new FileStream(filename, FileMode.Create);
                }

                WriteNalToFile(fs_v, vps);
                WriteNalToFile(fs_v, sps);
                WriteNalToFile(fs_v, pps);
                fs_v.Flush(true);
            };




            // Video NALs. May also include the SPS and PPS in-band for H264
            client.ReceivedNALs += (_, args) =>
            {
                if (fs_v != null)
                {
                    foreach (var nalUnitMem in args.Data)
                    {
                        var nalUnit = nalUnitMem.Span;
                        // Output some H264 stream information
                        if (h264 && nalUnit.Length > 0)
                        {
                            int nal_ref_idc = (nalUnit[0] >> 5) & 0x03;
                            int nal_unit_type = nalUnit[0] & 0x1F;
                            string description = nal_unit_type switch
                            {
                                1 => "NON IDR NAL",
                                5 => "IDR NAL",
                                6 => "SEI NAL",
                                7 => "SPS NAL",
                                8 => "PPS NAL",
                                9 => "ACCESS UNIT DELIMITER NAL",
                                _ => "OTHER NAL",
                            };
                            Console.WriteLine("NAL Ref = " + nal_ref_idc + " NAL Type = " + nal_unit_type + " " + description);
                        }

                        // Output some H265 stream information
                        if (h265 && nalUnit.Length > 0)
                        {
                            int nal_unit_type = (nalUnit[0] >> 1) & 0x3F;
                            string description = nal_unit_type switch
                            {
                                1 => "NON IDR NAL",
                                19 => "IDR NAL",
                                32 => "VPS NAL",
                                33 => "SPS NAL",
                                34 => "PPS NAL",
                                39 => "SEI NAL",
                                _ => "OTHER NAL",
                            };
                            Console.WriteLine("NAL Type = " + nal_unit_type + " " + description);
                        }

                        WriteNalToFile(fs_v, nalUnit);
                    }
                    // fs_v.Flush(true);
                }
            };

            client.ReceivedMp2t += (_, args) =>
            {
                if (fs_a == null)
                {
                    string filename = "rtsp_capture_" + now + ".mp2";
                    fs_a = new FileStream(filename, FileMode.Create);
                }
                foreach (var data in args.Data)
                {
                    fs_a?.Write(data.Span);
                }

            };

            int indexImg = 0;
            client.ReceivedJpeg += (_, args) =>
            {
                // Ugly to do it each time.
                // The interface need to change have an event on new file
                Directory.CreateDirectory("rtsp_capture_" + now);

                foreach (var data in args.Data)
                {
                    string filename = Path.Combine("rtsp_capture_" + now ,  indexImg++ + ".jpg");
                    using var fs = new FileStream(filename, FileMode.Create);
                    fs.Write(data.Span);
                }
                
            };

            client.ReceivedG711 += (_, args) =>
            {
                if (fs_a == null && args.Format.Equals("PCMU"))
                {
                    string filename = "rtsp_capture_" + now + ".ul";
                    fs_a = new FileStream(filename, FileMode.Create);
                }

                if (fs_a == null && args.Format.Equals("PCMA"))
                {
                    string filename = "rtsp_capture_" + now + ".al";
                    fs_a = new FileStream(filename, FileMode.Create);
                }

                if (fs_a != null)
                {
                    foreach (var data in args.Data)
                    {
                        fs_a.Write(data.Span);
                    }
                }
            };

            client.ReceivedAMR += (_, args) =>
            {
                if (fs_a == null && args.Format.Equals("AMR"))
                {
                    string filename = "rtsp_capture_" + now + ".amr";
                    fs_a = new FileStream(filename, FileMode.Create);
                    byte[] header = new byte[] { 0x23, 0x21, 0x41, 0x4D, 0x52, 0x0A }; // #!AMR<0x0A>
                    fs_a.Write(header, 0, header.Length);
                }

                if (fs_a != null)
                {
                    foreach (var data in args.Data)
                    {
                        fs_a.Write(data.Span);
                    }
                }
            };


            client.ReceivedAAC += (_, args) =>
            {
                if (fs_a == null)
                {
                    string filename = "rtsp_capture_" + now + ".aac";
                    fs_a = new FileStream(filename, FileMode.Create);
                }

                if (fs_a != null)
                {
                    foreach (var data in args.Data)
                    {
                        // ASDT header format
                        int protection_absent = 1;
                        //                        int profile = 2; // Profile 2 = AAC Low Complexity (LC)
                        //                        int sample_freq = 4; // 4 = 44100 Hz
                        //                        int channel_config = 2; // 2 = Stereo

                        Rtsp.BitStream bs = new Rtsp.BitStream();
                        bs.AddValue(0xFFF, 12); // (a) Start of data
                        bs.AddValue(0, 1); // (b) Version ID, 0 = MPEG4
                        bs.AddValue(0, 2); // (c) Layer always 2 bits set to 0
                        bs.AddValue(protection_absent, 1); // (d) 1 = No CRC
                        bs.AddValue(args.ObjectType - 1, 2); // (e) MPEG Object Type / Profile, minus 1
                        bs.AddValue(args.FrequencyIndex, 4); // (f)
                        bs.AddValue(0, 1); // (g) private bit. Always zero
                        bs.AddValue(args.ChannelConfiguration, 3); // (h)
                        bs.AddValue(0, 1); // (i) originality
                        bs.AddValue(0, 1); // (j) home
                        bs.AddValue(0, 1); // (k) copyrighted id
                        bs.AddValue(0, 1); // (l) copyright id start
                        bs.AddValue(data.Length + 7, 13); // (m) AAC data + size of the ASDT header
                        bs.AddValue(2047, 11); // (n) buffer fullness ???
                        int num_acc_frames = 1;
                        bs.AddValue(num_acc_frames - 1, 1); // (o) num of AAC Frames, minus 1

                        // If Protection was On, there would be a 16 bit CRC
                        if (protection_absent == 0) bs.AddValue(0xABCD /*CRC*/, 16); // (p)

                        byte[] header = bs.ToArray();

                        fs_a.Write(header, 0, header.Length);
                        fs_a.Write(data.Span);
                    }
                }
            };

            // Connect to RTSP Server
            Console.WriteLine("Connecting");

            client.Connect(url, username, password, RTSPClient.RTP_TRANSPORT.UDP, RTSPClient.MEDIA_REQUEST.VIDEO_AND_AUDIO);

            // Wait for user to terminate programme
            // Check for null which is returned when running under some IDEs
            // OR wait for the Streaming to Finish - eg an error on the RTSP socket

            Console.WriteLine("Press ENTER to exit");

            ConsoleKeyInfo key = default;
            while (key.Key != ConsoleKey.Enter && !client.StreamingFinished())
            {

                while (!Console.KeyAvailable && !client.StreamingFinished())
                {
                    // Avoid maxing out CPU on systems that instantly return null for ReadLine

                    Thread.Sleep(250);
                }
                if (Console.KeyAvailable)
                {
                    key = Console.ReadKey();
                }
            }

            client.Stop();
            fs_v?.Close();
            Console.WriteLine("Finished");

        }

        private static void WriteNalToFile(FileStream fs_v, ReadOnlySpan<byte> nal)
        {
            // Write Start Code
            fs_v.Write([0x00, 0x00, 0x00, 0x01]);
            fs_v.Write(nal);
        }
    }
}
