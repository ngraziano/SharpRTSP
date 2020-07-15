using System;
using System.Collections.Generic;
using System.Threading;

namespace RtspCameraExample
{
    class Program
    {

        static void Main(string[] args)
        {
            var demo = new Demo();
        }


        class Demo
        {
            RtspServer rtspServer = null;
            SimpleH264Encoder h264_encoder = null;
            byte[] raw_sps = null;
            byte[] raw_pps = null;

            int port = 8554;
            string username = "user";      // or use NUL if there is no username
            string password = "password";  // or use NUL if there is no password

            uint width = 192;
            uint height = 128;
            uint fps = 25;

            public Demo()
            {
                // Our programme needs 3 things...
                //   1) The RTSP Server to send the NALs to RTSP Clients
                //   2) A H264 Encoder to convert the YUV video into NALs
                //   3) A YUV Video Source (in this case I use a dummy Test Card)

                // Step 1 - Start the RTSP Server
                rtspServer = new RtspServer(port, username, password);
                try
                {
                    rtspServer.StartListen();
                }
                catch
                {
                    Console.WriteLine("Error: Could not start server");
                    return;
                }

                Console.WriteLine("RTSP URL is rtsp://" + username + ":" + password + "@" + "hostname:" + port);


                // Step 2 - Create the H264 Encoder. It will feed NALs into the RTSP server
                h264_encoder = new SimpleH264Encoder(width, height, fps);
                //h264_encoder = new TinyH264Encoder(); // hard coded to 192x128
                raw_sps = h264_encoder.GetRawSPS();
                raw_pps = h264_encoder.GetRawPPS();


                // Step 3 - Start the Video Test Card (dummy YUV image)
                // It will feed YUV Images into the event handler, which will compress the video into NALs and pass them into the RTSP Server
                TestCard video_source = new TestCard((int)width, (int)height, (int)fps);
                video_source.ReceivedYUVFrame += Video_source_ReceivedYUVFrame; // the event handler is where all the magic happens


                // Wait for user to terminate programme
                String msg = "Connect RTSP client to Port=" + port;
                if (username != null && password != null)
                {
                    msg += " Username=" + username + " Password=" + password;
                }
                Console.WriteLine(msg);
                Console.WriteLine("Press ENTER to exit");
                String readline = null;
                while (readline == null)
                {
                    readline = Console.ReadLine();

                    // Avoid maxing out CPU on systems that instantly return null for ReadLine
                    if (readline == null) Thread.Sleep(500);
                }

                rtspServer.StopListen();
            }


            private void Video_source_ReceivedYUVFrame(uint timestamp_ms, int width, int height, byte[] yuv_data)
            {
                // Compress the YUV and feed into the RTSP Server
                byte[] raw_video_nal = h264_encoder.CompressFrame(yuv_data);
                bool isKeyframe = true; // the Simple/Tiny H264 Encoders only return I-Frames for every video frame.


                // Put the NALs into a List
                List<byte[]> nal_array = new List<byte[]>();

                // We may want to add the SPS and PPS to the H264 stream as in-band data.
                // This may be of use if the client did not parse the SPS/PPS in the SDP or if the H264 encoder
                // changes properties (eg a new resolution or framerate which gives a new SPS or PPS).
                // Also looking towards H265, the VPS/SPS/PPS do not need to be in the SDP so would be added here.

                Boolean add_sps_pps_to_keyframe = true;
                if (add_sps_pps_to_keyframe && isKeyframe)
                {
                    nal_array.Add(raw_sps);
                    nal_array.Add(raw_pps);
                }

                // add the rest of the NALs
                nal_array.Add(raw_video_nal);

                // Pass the NAL array into the RTSP Server
                rtspServer.FeedInRawSPSandPPS(raw_sps, raw_pps);
                rtspServer.FeedInRawNAL(timestamp_ms, nal_array);
            }
        }
    }
}
