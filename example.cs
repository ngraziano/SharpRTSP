using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleApplication1
{
    class Program
    {
        static void Main(string[] args)
        {
            //String url = "rtsp://192.168.1.128/ch1.h264";    // IPS
            //String url = "rtsp://192.168.1.125/onvif-media/media.amp?profile=quality_h264"; // Axis
            //String url = " rtsp://192.168.1.124/rtsp_tunnel?h26x=4&line=1&inst=1"; // Bosch

            String url = "rtsp://192.168.1.121:8554/h264";  // Raspberry Pi RPOS using Live555
            //String url = "rtsp://127.0.0.1:8554/h264ESVideoTest"; // Live555 Cygwin

            RTSPClient c = new RTSPClient(url);

            // Hack - never ends
            while (true)
            {
                Thread.Sleep(1000);
            }

        }
    }


    class RTSPClient
    {
        Rtsp.RtspTcpTransport tcp_socket = null;
        Rtsp.RtspListener rtsp_client = null;   // this wraps around a 'stream' (usually a socket stream)
        String url = "";
        int video_payload = 0;      // usuallly 96 for H264 video which is the first dynamic payload type

        // Constructor
        public RTSPClient(String url)
        {
            this.url = url;

            // Use URI to extract hostname and port
            Uri uri = new Uri(url);

            // Connect to a RTSP Server
            tcp_socket = new Rtsp.RtspTcpTransport(uri.Host, (uri.IsDefaultPort?554:uri.Port));

            if (tcp_socket.Connected == false)
            {
                Console.WriteLine("Error - did not connect");
                return;
            }

            // Connect a RTSP Listener to the TCP Socket (or other Stream) to send messages and listen for replies
            rtsp_client = new Rtsp.RtspListener(tcp_socket);

            rtsp_client.MessageReceived += Rtsp_client_MessageReceived;
            rtsp_client.DataReceived += Rtsp_client_DataReceived;

            rtsp_client.Start(); // start reading messages from the server

            // Send OPTIONS
            // In the Received Message handler we will send DESCRIBE, SETUP and PLAY
            Rtsp.Messages.RtspRequest options_message = new Rtsp.Messages.RtspRequestOptions();
            options_message.RtspUri = new Uri(url);
            rtsp_client.SendMessage(options_message);
        }

        List<byte[]> temporary_rtp_payloads = new List<byte[]>();

        private void Rtsp_client_DataReceived(object sender, Rtsp.RtspChunkEventArgs e)
        {
            // RTP Packet Header
            // 0 - Version, P, X, CC, M, PT and Sequence Number
            //32 - Timestamp
            //64 - SSRC
            //96 - CSRCs (optional)
            //nn - Extension ID and Length
            //nn - Extension header

            int rtp_version =      (e.Message.Data[0] >> 6);
            int rtp_padding =      (e.Message.Data[0] >> 5) & 0x01;
            int rtp_extension =    (e.Message.Data[0] >> 4) & 0x01;
            int rtp_csrc_count =   (e.Message.Data[0] >> 0) & 0x0F;
            int rtp_marker =       (e.Message.Data[1] >> 7) & 0x01;
            int rtp_payload_type = (e.Message.Data[1] >> 0) & 0x7F;
            uint rtp_sequence_number = ((uint)e.Message.Data[2] << 8) + (uint)(e.Message.Data[3]);
            uint rtp_timestamp = ((uint)e.Message.Data[4] <<24) + (uint)(e.Message.Data[5] << 16) + (uint)(e.Message.Data[6] << 8) + (uint)(e.Message.Data[7]);
            uint rtp_ssrc =      ((uint)e.Message.Data[8] << 24) + (uint)(e.Message.Data[9] << 16) + (uint)(e.Message.Data[10] << 8) + (uint)(e.Message.Data[11]);

            int rtp_payload_start = 4 // V,P,M,SEQ
                                + 4 // time stamp
                                + 4 // ssrc
                                + (4 * rtp_csrc_count); // zero or more csrcs

            uint rtp_extension_id = 0;
            uint rtp_extension_size = 0;
            if (rtp_extension == 1)
            {
                rtp_extension_id = ((uint)e.Message.Data[rtp_payload_start + 0] << 8) + (uint)(e.Message.Data[rtp_payload_start + 1] << 0);
                rtp_extension_size = ((uint)e.Message.Data[rtp_payload_start + 2] << 8) + (uint)(e.Message.Data[rtp_payload_start + 3] << 0);
                rtp_payload_start += 4 + (int)rtp_extension_size;  // extension header and extension payload
            }

            Console.WriteLine("RTP Data"
                               + " V=" + rtp_version
                               + " P=" + rtp_padding
                               + " X=" + rtp_extension
                               + " CC=" + rtp_csrc_count
                               + " M=" + rtp_marker
                               + " PT=" + rtp_payload_type
                               + " Seq=" + rtp_sequence_number
                               + " Time=" + rtp_timestamp
                               + " SSRC=" + rtp_ssrc
                               + " Size=" + e.Message.Data.Length);


            if (rtp_payload_type != video_payload)
            {
                Console.WriteLine("Ignoring this RTP payload");
                return; // ignore this data
            }


            // If rtp_marker is '1' then this is the final transmission for this packet.
            // If rtp_marker is '0' we need to accumulate data with the same timestamp


            // ToDo - Check Timestamp
            // ToDo - could avoid a copy if there is only one RTP frame for the data (temp list is zero)

            // Add to the tempoary_rtp List

            byte[] rtp_payload = new byte[e.Message.Data.Length - rtp_payload_start]; // payload with RTP header removed
            System.Array.Copy(e.Message.Data, rtp_payload_start, rtp_payload, 0, rtp_payload.Length); // copy payload
            temporary_rtp_payloads.Add(rtp_payload);

            if (rtp_marker == 1)
            {
                // Process the RTP frame
                Process_RTP_Frame(temporary_rtp_payloads);
                temporary_rtp_payloads.Clear();
            }
        }

        FileStream fs = null;
        byte[] nal_header = new byte[]{ 0x00, 0x00, 0x00, 0x01 };
        int norm, fu_a, fu_b, stap_a, stap_b, mtap16, mtap24 = 0;

        public void Process_RTP_Frame(List<byte[]>rtp_payloads)
        {
            Console.WriteLine("RTP Data comprised of " + rtp_payloads.Count + " rtp packets");

            if (fs == null)
            {
                String filename = "rtsp_capture_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".h264";
                fs = new FileStream(filename, FileMode.Create);
            }

            for (int payload_index = 0; payload_index < rtp_payloads.Count; payload_index++) {
                // Examine the first rtp_payload and the first byte (the NAL header)
                int nal_header_f_bit = (rtp_payloads[payload_index][0] >> 7) & 0x01;
                int nal_header_nri = (rtp_payloads[payload_index][0] >> 5) & 0x03;
                int nal_header_type = (rtp_payloads[payload_index][0] >> 0) & 0x1F;

                // If the Nal Header Type is in the range 1..23 this is a normal NAL (not fragmented)
                // So write the NAL to the file
                if (nal_header_type >= 1 && nal_header_type <= 23)
                {
                    Console.WriteLine("Normal NAL");
                    norm++;
                    fs.Write(nal_header, 0, nal_header.Length);
                    fs.Write(rtp_payloads[payload_index], 0, rtp_payloads[payload_index].Length);
                }
                // There are 4 types of Aggregation Packet (split over RTP payloads)
                else if (nal_header_type == 24)
                {
                    Console.WriteLine("Agg STAP-A");
                    stap_a++;
                }
                else if (nal_header_type == 25)
                {
                    Console.WriteLine("Agg STAP-B");
                    stap_b++;
                }
                else if (nal_header_type == 26)
                {
                    Console.WriteLine("Agg MTAP16");
                    mtap16++;
                }
                else if (nal_header_type == 27)
                {
                    Console.WriteLine("Agg MTAP24");
                    mtap24++;
                }
                else if (nal_header_type == 28)
                {
                    Console.WriteLine("Frag FU-A");
                    fu_a++;

                    // Parse Fragmentation Unit Header
                    int fu_header_s = (rtp_payloads[payload_index][1] >> 7) & 0x01;  // start marker
                    int fu_header_e = (rtp_payloads[payload_index][1] >> 6) & 0x01;  // end marker
                    int fu_header_r = (rtp_payloads[payload_index][1] >> 5) & 0x01;  // reserved. should be 0
                    int fu_header_type = (rtp_payloads[payload_index][1] >> 0) & 0x1F; // Original NAL unit header

                    Console.WriteLine("Frag FU-A s="+fu_header_s + "e="+fu_header_e);

                    // Start Flag set
                    if (fu_header_s == 1)
                    {
                        // Write 00 00 00 01 header
                        fs.Write(nal_header, 0, nal_header.Length); // 0x00 0x00 0x00 0x01

                        // Modify the NAL Header that was at the start of the RTP packet
                        // Keep the F and NRI flags but substitute the type field with the fu_header_type
                        byte reconstructed_nal_type = (byte)((nal_header_nri << 5) + fu_header_type);
                        fs.WriteByte(reconstructed_nal_type); // NAL Unit Type
                        fs.Write(rtp_payloads[payload_index], 2, rtp_payloads[payload_index].Length - 2); // start after NAL Unit Type and FU Header byte

                    }

                    if (fu_header_s == 0)
                    {
                        // append this payload to the output NAL stream
                        // Data starts after the NAL Unit Type byte and the FU Header byte

                        fs.Write(rtp_payloads[payload_index], 2, rtp_payloads[payload_index].Length-2); // start after NAL Unit Type and FU Header byte

                    }
                    // We could check the End marker but the start marker is sufficient
                }


                else if (nal_header_type == 29)
                {
                    Console.WriteLine("Frag FU-B");
                    fu_b++;
                }
                else
                {
                    Console.WriteLine("Unknown NAL header " + nal_header_type);
                }

            }
            fs.Flush(true);
            Console.WriteLine("Norm=" + norm + " ST-A=" + stap_a + " ST-B=" + stap_b + " M16=" + mtap16 + " M24=" + mtap24 + " FU-A=" + fu_a + " FU-B=" + fu_b);
        }

        private void Rtsp_client_MessageReceived(object sender, Rtsp.RtspChunkEventArgs e)
        {
            Console.WriteLine("Message Received " + e.ToString());

            Rtsp.Messages.RtspResponse message = e.Message as Rtsp.Messages.RtspResponse;

            Console.WriteLine("Received " + message.OriginalRequest.ToString());

            if (message.OriginalRequest != null && message.OriginalRequest is Rtsp.Messages.RtspRequestOptions)
            {
                // send the Describe
                Rtsp.Messages.RtspRequest describe_message = new Rtsp.Messages.RtspRequestDescribe();
                describe_message.RtspUri = new Uri(url);
                rtsp_client.SendMessage(describe_message);

            }

            if (message.OriginalRequest != null && message.OriginalRequest is Rtsp.Messages.RtspRequestDescribe)
            {

                // Got a reply for DESCRIBE
                // Examine the SDP

                Console.Write(System.Text.Encoding.UTF8.GetString(message.Data));

                Rtsp.Sdp.SdpFile sdp_data;
                using (StreamReader sdp_stream = new StreamReader(new MemoryStream(message.Data)))
                {
                    sdp_data = Rtsp.Sdp.SdpFile.Read(sdp_stream);
                }


                // Process each 'Media' Attribute in the SDP.
                // If the attribute is for Video, then carry out a SETUP and a PLAY

                for (int x = 0; x < sdp_data.Medias.Count; x++)
                {
                    if (sdp_data.Medias[x].GetMediaType() == Rtsp.Sdp.Media.MediaType.video)
                    {

                        // seach the atributes for control, fmtp and rtpmap
                        String control = "";  // the "track" or "stream id"
                        String fmtp = ""; // holds SPS and PPS
                        String rtpmap = ""; // holds Payload format, eg 96 often used with H264 as first dynamic payload value
                        foreach (Rtsp.Sdp.Attribut attrib in sdp_data.Medias[x].Attributs)
                        {
                            if (attrib.Key.Equals("control")) control = attrib.Value;
                            if (attrib.Key.Equals("fmtp")) fmtp = attrib.Value;
                            if (attrib.Key.Equals("rtpmap")) rtpmap = attrib.Value;
                        }

                        String[] split_rtpmap = rtpmap.Split(' ');
                        video_payload = 0;
                        bool result = Int32.TryParse(split_rtpmap[0], out video_payload);


                        // Transport: RTP/AVP;unicast;client_port=8000-8001
                        // Transport: RTP/AVP/TCP;interleaved=0-1

                        Rtsp.Messages.RtspRequest setup_message = new Rtsp.Messages.RtspRequestSetup();
                        setup_message.RtspUri = new Uri(url + "/" + control);
                        setup_message.AddHeader("Transport: RTP/AVP/TCP;interleaved=0");
                        rtsp_client.SendMessage(setup_message);
                    }
                }


            }

            if (message.OriginalRequest != null && message.OriginalRequest is Rtsp.Messages.RtspRequestSetup)
            {
                // Got Reply to SETUP
                Console.WriteLine("Got reply from Setup. Session is " + message.Session);

                String session = message.Session; // Session value used with Play, Pause, Teardown

                Rtsp.Messages.RtspRequest play_message = new Rtsp.Messages.RtspRequestPlay();
                play_message.RtspUri = new Uri(url);
                play_message.Session = session;
//                play_message.Timeout = 65;
                rtsp_client.SendMessage(play_message);
            }

            if (message.OriginalRequest != null && message.OriginalRequest is Rtsp.Messages.RtspRequestPlay)
            {
                // Got Reply to PLAU
                Console.WriteLine("Got reply from Play  " + message.Command);
            }

        }
    }
}
