Sharp RTSP
==========

[![Build Status](https://ci.appveyor.com/api/projects/status/github/ngraziano/SharpRTSP?branch=master&svg=true)](https://ci.appveyor.com/project/ngraziano/sharprtsp)
[![Coverage Status](https://coveralls.io/repos/github/ngraziano/SharpRTSP/badge.svg?branch=master)](https://coveralls.io/github/ngraziano/SharpRTSP?branch=master)

A C# library to build RTSP Clients, RTSP Servers and handle RTP data streams. The library has several examples.
* RTSP Client Example - will connect to a RTSP server and receive Video and Audio in H264, H265/HEVC, G711, AAC and AMR formats. UDP, TCP and Multicast are supported. The data received is written to files.
* RTSP Camera Server Example - A YUV Image Generator and a very simple H264 Encoder generate H264 NALs which are then delivered via a RTSP Server to clients
* RTP Receiver - will recieve RTP and RTCP packets and pass them to a transport handler
* RTSP Server - will accept RTSP connections and talk to clients
* RTP Sender - will send RTP packets to clients
* Transport Handler - Transport hanlders for H264, H265/HEVC, G711 and AMR are provided.

**:warning: : This library does not handle the decoding of the video or audio (eg converting H264 into a bitmap). SharpRTSP is limited to the transport layer and generates the raw data that you need to feed into a video decoder or audio decoder. Many people use FFMPEG or use Hardware Accelerated Operating System APIs to do the decoding.**



Walkthrough of the RTSP Client Example
======================================
This is a walkthrough of an **old version** of the RTSP Client Example which highlights the main way to use the library.


* STEP 1 - Open TCP Socket connection to the RTSP Server

  ```C#
            // Connect to a RTSP Server
            tcp_socket = new Rtsp.RtspTcpTransport(host,port);

            if (tcp_socket.Connected == false)
            {
                Console.WriteLine("Error - did not connect");
                return;
            }
  ```

  This opens a connection for a 'TCP' mode RTSP/RTP session where RTP packets are set in the RTSP socket.


* STEP 2 - Create a RTSP Listener and attach it to the RTSP TCP Socket

  ```C#
            // Connect a RTSP Listener to the TCP Socket to send messages and listen for replies
            rtsp_client = new Rtsp.RtspListener(tcp_socket);

            rtsp_client.MessageReceived += Rtsp_client_MessageReceived;
            rtsp_client.DataReceived += Rtsp_client_DataReceived;

            rtsp_client.Start(); // start reading messages from the server
  ```

  The RTSP Listener class lets you SEND messages to the RTSP Server (see below).  
  The RTSP Listner class has a worker thread that listens for replies from the RTSP Server.  
  When replies are received the MessageReceived Event is fired.  
  When RTP packets are received the DataReceived Event is fired.


* STEP 3 - Send Messages to the RTSP Server

  The samples below show how to send messages.

  Send OPTIONS with this code :

  ```C#
            Rtsp.Messages.RtspRequest options_message = new Rtsp. Messages.RtspRequestOptions();
            options_message.RtspUri = new Uri(url);
            rtsp_client.SendMessage(options_message);
  ```

  Send DESCRIBE with this code :

  ```C#
            // send the Describe
            Rtsp.Messages.RtspRequest describe_message = new Rtsp.Messages.RtspRequestDescribe();
            describe_message.RtspUri = new Uri(url);
            rtsp_client.SendMessage(describe_message);
            // The reply will include the SDP data
  ```

  Send SETUP with this code :

  ```C#
            // the value of 'control' comes from parsing the SDP for the desired video or audio sub-stream
            Rtsp.Messages.RtspRequest setup_message = new Rtsp.Messages.RtspRequestSetup();
            setup_message.RtspUri = new Uri(url + "/" + control);
            setup_message.AddHeader("Transport: RTP/AVP/TCP;interleaved=0");
            rtsp_client.SendMessage(setup_message);
            // The reply will include the Session
  ```

  Send PLAY with this code :

  ```C#
            // the value of 'session' comes from the reply of the SETUP command
            Rtsp.Messages.RtspRequest play_message = new Rtsp.Messages.RtspRequestPlay();
            play_message.RtspUri = new Uri(url);
            play_message.Session = session;
            rtsp_client.SendMessage(play_message);
  ```

* STEP 4 - Handle Replies when the MessageReceived event is fired
  
  This example assumes the main program sends an OPTIONS Command.  
  It looks for a reply from the server for OPTIONS and then sends DESCRIBE.  
  It looks for a reply from the server for DESCRIBE and then sends SETUP (for the video stream)  
  It looks for a reply from the server for SETUP and then sends PLAY.  
  Once PLAY has been sent the video, in the form of RTP packets, will be received.

  ```C#
        private void Rtsp_client_MessageReceived(object sender, Rtsp.RtspChunkEventArgs e)
        {
            Rtsp.Messages.RtspResponse message = e.Message as Rtsp.Messages.RtspResponse;

            Console.WriteLine("Received " + message.OriginalRequest.ToString());

            if (message.OriginalRequest != null && message.OriginalRequest is Rtsp.Messages.RtspRequestOptions)
            {
                // send the DESCRIBE
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
                // If the attribute is for Video, then send a SETUP
                for (int x = 0; x < sdp_data.Medias.Count; x++)
                {
                    if (sdp_data.Medias[x].GetMediaType() == Rtsp.Sdp.Media.MediaType.video)
                    {
                        // seach the atributes for control, fmtp and rtpmap
                        String control = "";  // the "track" or "stream id"
                        String fmtp = ""; // holds SPS and PPS
                        String rtpmap = ""; // holds the Payload format, 96 is often used with H264
                        foreach (Rtsp.Sdp.Attribut attrib in sdp_data.Medias[x].Attributs)
                        {
                            if (attrib.Key.Equals("control")) control = attrib.Value;
                            if (attrib.Key.Equals("fmtp")) fmtp = attrib.Value;
                            if (attrib.Key.Equals("rtpmap")) rtpmap = attrib.Value;
                        }
                        
                        // Get the Payload format number for the Video Stream
                        String[] split_rtpmap = rtpmap.Split(' ');
                        video_payload = 0;
                        bool result = Int32.TryParse(split_rtpmap[0], out video_payload);

                        // Send SETUP for the Video Stream
                        // using Interleaved mode (RTP frames over the RTSP socket)
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
                
                // Send PLAY
                Rtsp.Messages.RtspRequest play_message = new Rtsp.Messages.RtspRequestPlay();
                play_message.RtspUri = new Uri(url);
                play_message.Session = session;
                rtsp_client.SendMessage(play_message);
            }

            if (message.OriginalRequest != null && message.OriginalRequest is Rtsp.Messages.RtspRequestPlay)
            {
                // Got Reply to PLAY
                Console.WriteLine("Got reply from Play  " + message.Command);
            }
        }
  ```

* STEP 5 - Handle RTP Video

  This code handles each incoming RTP packet, combining RTP packets that are all part of the same frame of vdeo (using the Marker Bit).
  Once a full frame is received it can be passed to a De-packetiser to get the compressed video data

  ```C#
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

            // ToDo - Check Timestamp matches

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
  ```

* STEP 6 - Process RTP frame

  An RTP frame consists of 1 or more RTP packets  
  H264 video is packed into one or more RTP packets and this sample extracts Normal Packing and
  Fragmented Unit type A packing (the common two)  
  This example writes the video to a .264 file which can be played with FFPLAY

  ```C#
        FileStream fs = null;
        byte[] nal_header = new byte[]{ 0x00, 0x00, 0x00, 0x01 };
        int norm, fu_a, fu_b, stap_a, stap_b, mtap16, mtap24 = 0; // stats counters

        public void Process_RTP_Frame(List<byte[]>rtp_payloads)
        {
            Console.WriteLine("RTP Data comprised of " + rtp_payloads.Count + " rtp packets");

            if (fs == null)
            {
                // Create the file
                String filename = "rtsp_capture_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".h264";
                fs = new FileStream(filename, FileMode.Create);
                
                // TODO. Get SPS and PPS from the SDP Attributes (the fmtp attribute) and write to the file
                // for IP cameras that only out the SPS and PPS out-of-band
            }

            for (int payload_index = 0; payload_index < rtp_payloads.Count; payload_index++) {
                // Examine the first rtp_payload and the first byte (the NAL header)
                int nal_header_f_bit = (rtp_payloads[payload_index][0] >> 7) & 0x01;
                int nal_header_nri = (rtp_payloads[payload_index][0] >> 5) & 0x03;
                int nal_header_type = (rtp_payloads[payload_index][0] >> 0) & 0x1F;

                // If the NAL Header Type is in the range 1..23 this is a normal NAL (not fragmented)
                // So write the NAL to the file
                if (nal_header_type >= 1 && nal_header_type <= 23)
                {
                    Console.WriteLine("Normal NAL");
                    norm++;
                    fs.Write(nal_header, 0, nal_header.Length);
                    fs.Write(rtp_payloads[payload_index], 0, rtp_payloads[payload_index].Length);
                }
                else if (nal_header_type == 24)
                {
                    // There are 4 types of Aggregation Packet (multiple NALs in one RTP packet)
                    Console.WriteLine("Agg STAP-A not supported");
                    stap_a++;
                }
                else if (nal_header_type == 25)
                {
                    // There are 4 types of Aggregation Packet (multiple NALs in one RTP packet)
                    Console.WriteLine("Agg STAP-B not supported");
                    stap_b++;
                }
                else if (nal_header_type == 26)
                {
                    // There are 4 types of Aggregation Packet (multiple NALs in one RTP packet)
                    Console.WriteLine("Agg MTAP16 not supported");
                    mtap16++;
                }
                else if (nal_header_type == 27)
                {
                    // There are 4 types of Aggregation Packet (multiple NALs in one RTP packet)
                    Console.WriteLine("Agg MTAP24 not supported");
                    mtap24++;
                }
                else if (nal_header_type == 28)
                {
                    Console.WriteLine("Fragmented Packet Type FU-A");
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
                }

                else if (nal_header_type == 29)
                {
                    Console.WriteLine("Fragmented Packet  FU-B not supported");
                    fu_b++;
                }
                else
                {
                    Console.WriteLine("Unknown NAL header " + nal_header_type);
                }

            }
            // ensure video is written to disk
            fs.Flush(true);
            
            // Print totals
            Console.WriteLine("Norm=" + norm + " ST-A=" + stap_a + " ST-B=" + stap_b + " M16=" + mtap16 + " M24=" + mtap24 + " FU-A=" + fu_a + " FU-B=" + fu_b);
        }
  ```





