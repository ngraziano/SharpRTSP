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
            //String url = "rtsp://192.168.1.128/ch1.h264";    // IPS
            //String url = "rtsp://192.168.1.125/onvif-media/media.amp?profile=quality_h264"; // Axis
            //String url = "rtsp://user:password@192.168.1.102/onvif-media/media.amp?profile=quality_h264"; // Axis
            //String url = "rtsp://192.168.1.124/rtsp_tunnel?h26x=4&line=1&inst=1"; // Bosch

            //String url = "rtsp://192.168.1.33:8554/unicast";  // Raspberry Pi RPOS using Mpromonet Live555 server
            //String url = "rtsp://192.168.1.33:8554/h264";  // Raspberry Pi RPOS using Live555
            //String url = "rtsp://192.168.1.121:8554/h264";  // Raspberry Pi RPOS using Live555
            //String url = "rtsp://192.168.1.121:8554/h264m";  // Raspberry Pi RPOS using Live555 in Multicast mode

            //String url = "rtsp://127.0.0.1:8554/h264ESVideoTest"; // Live555 Cygwin
            //String url = "rtsp://192.168.1.160:8554/h264ESVideoTest"; // Live555 Cygwin
            //String url = "rtsp://127.0.0.1:8554/h264ESVideoTest"; // Live555 Cygwin
            //String url = "rtsp://wowzaec2demo.streamlock.net/vod/mp4:BigBuckBunny_115k.mov";

            String url = "rtsp://192.168.1.79:8554/amrAudioTest"; // Live555 AMR Audio Test

            // MJPEG Tests (Payload 26)
            //String url = "rtsp://192.168.1.125/onvif-media/media.amp?profile=mobile_jpeg";


            String now = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            FileStream fs_v = null;   // used to write the video
            FileStream fs_a = null;   // used to write the audio

            // Create a RTSP Client
            RTSPClient c = new RTSPClient();

            c.Received_SPS_PPS += (byte[] sps, byte[] pps) => {
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

            c.Received_NALs += (List<byte[]> nal_units) => {
                if (fs_v != null) {
                    
                    foreach (byte[] nal_unit in nal_units)
                    {
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

            // Connect to RTSP Server
            Console.WriteLine("Connecting");

            c.Connect(url, RTSPClient.RTP_TRANSPORT.TCP);

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
