using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace RtspClientExample
{
    public static class Program
    {
        private static ILogger logger = null!;

        private const string ProfileMJPEG = "JPEG";
        private const string ProfileH264 = "H264";
        private const string ProfileH265 = "H265";
        private const string ProfileMP2T = "MP2T";

        private const string ProfilePCMU = "PCMU";
        private const string ProfilePCMA = "PCMA";
        private const string ProfileAMR = "AMR";
        private const string ProfileAAC = "AAC";

        private static readonly byte[] halStartCode = [0x00, 0x00, 0x00, 0x01];

        static void Main()
        {
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder
                    .AddFilter("Microsoft", LogLevel.Warning)
                    .AddFilter("System", LogLevel.Warning)
                    .AddFilter("RtspClientExample", LogLevel.Debug)
                    .AddFilter("Rtsp", LogLevel.Debug)
                    .AddSimpleConsole(o =>
                    {
                        o.SingleLine = true;
                    });
            });

            logger = loggerFactory.CreateLogger("Main");

            // Internet Test - Big Buck Bunney
            // string url = "rtsp://mafreebox.freebox.fr/fbxtv_pub/stream?flavour=hd&namespace=1&service=201";
            //string url = "rtsp://wowzaec2demo.streamlock.net/vod/mp4:BigBuckBunny_115k.mp4";
            // IPS IP Camera Tests
            //String url = "rtsp://192.168.1.128/ch1.h264";

            // string url = "rtsp://192.168.0.89/media/video2";

             string url = "http://192.168.3.72/profile1/media.smp";

            bool usePlayback = false;
            // string url = "rtsp://192.168.3.72/ProfileG/Recording-1/recording/play.smp";

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

            // Happytime RTSP Server
            //string url = "rtsp://127.0.0.1/screenlive";
            //string url = "http://127.0.0.1:8044/screenlive";

            // MJPEG Tests (Payload 26)
            //String url = "rtsp://192.168.1.125/onvif-media/media.amp?profile=mobile_jpeg";

            // H265 Tests

            // Create a RTSP Client
            RTSPClient client = new(loggerFactory);

            client.NewVideoStream += (_, args) =>
            {
                switch (args.StreamType)
                {
                    case "H264":
                        NewH264Stream(args, client);
                        break;
                    case "H265":
                        NewH265Stream(args, client);
                        break;
                    case "JPEG":
                        NewMJPEGStream(client);
                        break;
                    case "MP2T":
                        NewMP2Stream(client);
                        break;
                    default:
                        logger.LogWarning("Unknow Video format {streamtype}", args.StreamType);
                        break;
                }
            };

            client.NewAudioStream += (_, arg) =>
            {
                switch (arg.StreamType)
                {
                    case "PCMU":
                        NewGenericAudio(client, "ul");
                        break;
                    case "PCMA":
                        NewGenericAudio(client, "al");
                        break;
                    case "AMR":
                        NewAMRAudioStream(client);
                        break;
                    case "AAC":
                        NewAACAudioStream(arg, client);
                        break;
                    default:
                        logger.LogWarning("Unknow Audio format {streamtype}", arg.StreamType);
                        break;
                }
            };

            client.SetupMessageCompleted += (_, _) =>
            {
                if (usePlayback)
                {
                    // for demonstration play one hour in past
                    DateTime startTime = DateTime.UtcNow.AddHours(-1);
                    client.Play(startTime, startTime.AddMinutes(10), 1.0);
                }
                else
                {
                    client.Play();
                }
            };

            // Connect to RTSP Server
            Console.WriteLine("Connecting");

            client.Connect(url, username, password, RTSPClient.RTP_TRANSPORT.TCP, RTSPClient.MEDIA_REQUEST.VIDEO_AND_AUDIO, usePlayback);

            //client.Pause();
            //DateTime startTime = DateTime.Now.AddHours(-1);
            //client.Play(startTime, startTime.AddMinutes(1), 1.0);

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
            Console.WriteLine("Finished");
        }

        private static void NewAACAudioStream(NewStreamEventArgs arg, RTSPClient client)
        {
            string now = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string filename = "rtsp_capture_" + now + ".aac";
            var fs_a = new FileStream(filename, FileMode.Create);
            var config = arg.StreamConfigurationData as AacStreamConfigurationData;
            Debug.Assert(config != null, "config is invalid");

            void ReceiveAudioAAC(RTSPClient client, SimpleDataEventArgs dataArgs)
            {
                foreach (var data in dataArgs.Data)
                {
                    // ASDT header format
                    int protection_absent = 1;
                    //                        int profile = 2; // Profile 2 = AAC Low Complexity (LC)
                    //                        int sample_freq = 4; // 4 = 44100 Hz
                    //                        int channel_config = 2; // 2 = Stereo

                    Rtsp.BitStream bs = new();
                    bs.AddValue(0xFFF, 12); // (a) Start of data
                    bs.AddValue(0, 1); // (b) Version ID, 0 = MPEG4
                    bs.AddValue(0, 2); // (c) Layer always 2 bits set to 0
                    bs.AddValue(protection_absent, 1); // (d) 1 = No CRC
                    bs.AddValue(config.ObjectType - 1, 2); // (e) MPEG Object Type / Profile, minus 1
                    bs.AddValue(config.FrequencyIndex, 4); // (f)
                    bs.AddValue(0, 1); // (g) private bit. Always zero
                    bs.AddValue(config.ChannelConfiguration, 3); // (h)
                    bs.AddValue(0, 1); // (i) originality
                    bs.AddValue(0, 1); // (j) home
                    bs.AddValue(0, 1); // (k) copyrighted id
                    bs.AddValue(0, 1); // (l) copyright id start
                    bs.AddValue(data.Length + 7, 13); // (m) AAC data + size of the ASDT header
                    bs.AddValue(2047, 11); // (n) buffer fullness ???
                    int num_acc_frames = 1;
                    bs.AddValue(num_acc_frames - 1, 1); // (o) num of AAC Frames, minus 1

                    // If Protection was On, there would be a 16 bit CRC
                    if (protection_absent == 0) bs.AddValue(0xABCD, 16); // (p)

                    byte[] header = bs.ToArray();

                    fs_a.Write(header, 0, header.Length);
                    fs_a.Write(data.Span);
                }
            }
            ;
            client.SetupAudioPayload(ProfileAAC, ReceiveAudioAAC);
        }

        private static void NewAMRAudioStream(RTSPClient client)
        {
            string now = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            string filename = "rtsp_capture_" + now + ".amr";
            FileStream fs_a = new(filename, FileMode.Create);
            fs_a.Write("#!AMR\n"u8);
            void ReceiveAudioAMR(RTSPClient client, SimpleDataEventArgs dataArgs)
            {
                foreach (var data in dataArgs.Data)
                {
                    fs_a.Write(data.Span);
                }
            };
            client.SetupAudioPayload(ProfileAMR, ReceiveAudioAMR);
        }

        private static void NewGenericAudio(RTSPClient client, string extension)
        {
            string now = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string filename = "rtsp_capture_" + now + "." + extension;
            FileStream fs_a = new(filename, FileMode.Create);
            void ReceiveAudioPCMA(RTSPClient client, SimpleDataEventArgs dataArgs)
            {
                foreach (var data in dataArgs.Data)
                {
                    fs_a.Write(data.Span);
                }
            };
            client.SetupAudioPayload(ProfilePCMA, ReceiveAudioPCMA);
        }

        private static void NewMP2Stream(RTSPClient client)
        {
            string now = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            string filename = "rtsp_capture_" + now + ".mp2";
            FileStream fs_v = new(filename, FileMode.Create);
            void ReceivedVideoData_MPT2(RTSPClient client, SimpleDataEventArgs dataArgs)
            {
                foreach (var data in dataArgs.Data)
                {
                    fs_v?.Write(data.Span);
                }
            }
            ;
            client.SetupVideoPayload(ProfileMP2T, ReceivedVideoData_MPT2);
        }

        private static void NewMJPEGStream(RTSPClient client)
        {
            string now = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            Directory.CreateDirectory("rtsp_capture_" + now);
            var indexImg = 0;
            void ReceivedVideoData_MJPEG(RTSPClient client, SimpleDataEventArgs dataArgs)
            {
                // Ugly to do it each time.
                // The interface need to change have an event on new file

                
                foreach (var data in dataArgs.Data)
                {
                    string filename = Path.Combine("rtsp_capture_" + now, indexImg++ + ".jpg");
                    using var fs = new FileStream(filename, FileMode.Create);
                    fs.Write(data.Span);
                }
            }
            ;
            client.SetupVideoPayload(ProfileMJPEG, ReceivedVideoData_MJPEG);
        }

        private static void NewH265Stream(NewStreamEventArgs args, RTSPClient client)
        {
            string now = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string filename = "rtsp_capture_" + now + ".265";
            FileStream fs_v = new(filename, FileMode.Create);
            if (args.StreamConfigurationData is H265StreamConfigurationData h265StreamConfigurationData)
            {
                WriteNalToFile(fs_v, h265StreamConfigurationData.VPS);
                WriteNalToFile(fs_v, h265StreamConfigurationData.SPS);
                WriteNalToFile(fs_v, h265StreamConfigurationData.PPS);
            }
            void ReceivedVideoData_H265(RTSPClient client, SimpleDataEventArgs dataArgs)
            {
                if (fs_v != null)
                {
                    foreach (var nalUnitMem in dataArgs.Data)
                    {
                        var nalUnit = nalUnitMem.Span;
                        // Output some H264 stream information
                        if (nalUnit.Length > 5)
                        {
                            int nal_unit_type = (nalUnit[4] >> 1) & 0x3F;
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
                            logger.LogInformation("NAL Type = {nal_unit_type} {description}", nal_unit_type, description);
                        }
                        fs_v.Write(nalUnit);
                    }
                }
            }
            ;
            client.SetupVideoPayload(ProfileH265, ReceivedVideoData_H265);
        }

        private static void NewH264Stream(NewStreamEventArgs args, RTSPClient client)
        {
            string now = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string filename = "rtsp_capture_" + now + ".264";
            FileStream fs_v = new(filename, FileMode.Create);
            if (args.StreamConfigurationData is H264StreamConfigurationData h264StreamConfigurationData)
            {
                WriteNalToFile(fs_v, h264StreamConfigurationData.SPS);
                WriteNalToFile(fs_v, h264StreamConfigurationData.PPS);
            }

            void ReceivedVideoData_H264(RTSPClient client, SimpleDataEventArgs dataArgs)
            {
                foreach (var nalUnitMem in dataArgs.Data)
                {
                    var nalUnit = nalUnitMem.Span;
                    // Output some H264 stream information
                    if (nalUnit.Length > 5)
                    {
                        int nal_ref_idc = (nalUnit[4] >> 5) & 0x03;
                        int nal_unit_type = nalUnit[4] & 0x1F;
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
                        logger.LogInformation("NAL Ref = {nal_ref_idc} NAL Type = {nal_unit_type} {description}", nal_ref_idc, nal_unit_type, description);
                    }
                    fs_v.Write(nalUnit);
                }
            }
            ;
            client.SetupVideoPayload(ProfileH264, ReceivedVideoData_H264);
        }

        private static void WriteNalToFile(FileStream fs_v, ReadOnlySpan<byte> nal)
        {
            // Write Start Code
            fs_v.Write([0x00, 0x00, 0x00, 0x01]);
            fs_v.Write(nal);
        }
    }
}
