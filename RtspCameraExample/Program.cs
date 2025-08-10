// Example software to simulate an Live RTSP Steam and an RTSP CCTV Camera in C#
// There is a very simple Video and Audio generator
// with a very simple (and not very efficient) H264 and G711 u-Law audio encoder
// to feed data into the RTSP Server
//
// Server supports TCP and UDP clients.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace RtspCameraExample
{
    static class Program
    {
        static void Main()
        {
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder
                    .AddFilter("Microsoft", LogLevel.Warning)
                    .AddFilter("System", LogLevel.Warning)
                    .AddFilter("RtspCameraExample", LogLevel.Debug)
                    .AddFilter("Rtsp", LogLevel.Debug)
                    .AddConsole();
            });
            var demo = new Demo(loggerFactory);
        }

        class Demo
        {
            private readonly RtspServer rtspServer;
            private readonly SimpleH264Encoder h264Encoder;

            private readonly byte[] raw_sps;
            private readonly byte[] raw_pps;

            private readonly int port = 8554;
            private readonly string username = "user";      // or use NUL if there is no username
            private readonly string password = "password";  // or use NUL if there is no password
            private readonly bool useRTSPS = false;         // Set True if you want to accept RTSPS connections
            private readonly string pfxFile = "c:\\tls_certificate\\server.pfx";           // PFX file used by RTSPS Server

            // You can make a Self Signed PFX Certificate file with these steps
            // 1) Run   "mkdir c:\tls_certificate"
            //          "cd c:\tls_certificate"

            // 2) Run   "\Program Files (x86)\Windows Kits\10\bin\10.0.22621.0\x64\MakeCert.exe" -r -pe -n "CN=192.168.26.94" -sky exchange -sv server.pvk server.cer
            //    NOTES 1) The CN wants to be the IP address or Hostname of the RTSPS server. This is only a basic example. A proper certificate would use SubjectAltNames
            //          2) When MAkeCert it asks for the PASSWORD in the Pop-up windows, click on the 'NONE' button.
            //             (would be better to have a password on the certificate, but I could not make it work)

            // 3) Run   "\Program Files (x86)\Windows Kits\10\bin\10.0.22621.0\x64\pvk2pfx.exe" -pvk server.pvk -spc server.cer -pfx server.pfx

            // 4) You now have a file called server.pfx which can be used by this application

            private readonly int width =  192;
            private readonly int height = 128;
            private readonly uint fps = 25;

            public Demo(ILoggerFactory loggerFactory)
            {
                // Our programme needs several things...
                //   1) The RTSP Server to send the NALs to RTSP Clients
                //   2) A H264 Encoder to convert the YUV video into NALs
                //   3) A G.711 u-Law audio encoder to convert PCM audio into G711 data
                //   4) A YUV Video Source and PCM Audo Souce (in this case I use a dummy Test Card)

                /////////////////////////////////////////
                // Step 1 - Start the RTSP Server
                /////////////////////////////////////////
                if (!useRTSPS)
                    rtspServer = new RtspServer(port, username, password, false, loggerFactory);
                else
                    rtspServer = new RtspServer(port, username, password, pfxFile, loggerFactory); // rtsps:// needs a PFX File
                try
                {
                    rtspServer.StartListen();
                }
                catch
                {
                    Console.WriteLine("Error: Could not start server");
                    throw;
                }

                if (!useRTSPS)
                    Console.WriteLine($"RTSP URL is rtsp://{username}:{password}@hostname:{port}");
                else
                    Console.WriteLine($"Encrypted RTSP URL is rtsps://{username}:{password}@hostname:{port}");

                /////////////////////////////////////////
                // Step 2 - Create the H264 Encoder. It will feed NALs into the RTSP server
                /////////////////////////////////////////
                h264Encoder = new SimpleH264Encoder(width, height);
                //h264_encoder = new TinyH264Encoder(); // hard coded to 192x128
                raw_sps = h264Encoder.GetRawSPS();
                raw_pps = h264Encoder.GetRawPPS();

                /////////////////////////////////////////
                // Step 3 - Start the Video and Audio Test Card (dummy YUV image and dummy PCM audio)
                // It will feed YUV Images into the event handler, which will compress the video into NALs and pass them into the RTSP Server
                // It will feed PCM Audio into the event handler, which will compress the audio into G711 uLAW packets and pass them into the RTSP Server
                /////////////////////////////////////////
                TestCard av_source = new(width, height, (int)fps);
                av_source.ReceivedYUVFrame += Video_source_ReceivedYUVFrame; // the event handler is where all the magic happens
                av_source.ReceivedAudioFrame += Audio_source_ReceivedAudioFrame; // the event handler is where all the magic happens

                /////////////////////////////////////////
                // Wait for user to terminate programme
                // Everything else happens in Timed Events from av_source
                // or Worker Threads in the RTSP library
                /////////////////////////////////////////
                String msg = "Connect RTSP client to Port=" + port;
                if (username != null && password != null)
                {
                    msg += " Username=" + username + " Password=" + password;
                }
                Console.WriteLine(msg);
                Console.WriteLine("Press ENTER to exit");
                Console.ReadLine();

                /////////////////////////////////////////
                // Shutdown
                /////////////////////////////////////////
                av_source.ReceivedYUVFrame -= Video_source_ReceivedYUVFrame;
                av_source.ReceivedAudioFrame -= Audio_source_ReceivedAudioFrame;
                av_source.Disconnect();
                rtspServer.StopListen();
            }

            private void Video_source_ReceivedYUVFrame(uint timestamp_ms, int width, int height, Span<byte> yuv_data)
            {
                // Compress the YUV and feed into the RTSP Server
                var raw_video_nal = h264Encoder.CompressFrame(yuv_data);
                const bool isKeyframe = true; // the Simple/Tiny H264 Encoders only return I-Frames for every video frame.

                // Put the NALs into a List
                List<byte[]> nal_array = [];

                // We may want to add the SPS and PPS to the H264 stream as in-band data.
                // This may be of use if the client did not parse the SPS/PPS in the SDP or if the H264 encoder
                // changes properties (eg a new resolution or framerate which gives a new SPS or PPS).
                // Also looking towards H265, the VPS/SPS/PPS do not need to be in the SDP so would be added here.

                const bool add_sps_pps_to_keyframe = true;
                if (add_sps_pps_to_keyframe && isKeyframe)
                {
                    nal_array.Add(raw_sps);
                    nal_array.Add(raw_pps);
                }

                // add the rest of the NALs
                nal_array.Add(raw_video_nal.ToArray());

                // Pass the NAL array into the RTSP Server
                rtspServer.FeedInRawSPSandPPS(raw_sps, raw_pps);
                rtspServer.FeedInRawNAL(timestamp_ms, nal_array);
            }

            private void Audio_source_ReceivedAudioFrame(uint timestamp_ms, short[] audio_frame)
            {
                // Compress the audio into G711 and feed into the RTSP Server
                byte[] g711_data = SimpleG711Encoder.EncodeULaw(audio_frame);

                // Pass the audio data into the RTSP Server
                rtspServer.FeedInAudioPacket(timestamp_ms, g711_data);
            }
        }
    }
}
