using Rtsp.Messages;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RtspClientExample
{
    class Program
    {
        static void Main(string[] args)
        {
            //String url = "rtsp://192.168.1.128/ch1.h264";    // IPS
            //String url = "rtsp://192.168.1.125/onvif-media/media.amp?profile=quality_h264"; // Axis
            //String url = "rtsp://192.168.1.124/rtsp_tunnel?h26x=4&line=1&inst=1"; // Bosch

            //String url = "rtsp://192.168.1.121:8554/h264";  // Raspberry Pi RPOS using Live555
            //String url = "rtsp://192.168.1.121:8554/h264m";  // Raspberry Pi RPOS using Live555 in Multicast mode
            
            //String url = "rtsp://127.0.0.1:8554/h264ESVideoTest"; // Live555 Cygwin
            //String url = "rtsp://192.168.1.160:8554/h264ESVideoTest"; // Live555 Cygwin
            //String url = "rtsp://127.0.0.1:8554/h264ESVideoTest"; // Live555 Cygwin
            String url = "rtsp://wowzaec2demo.streamlock.net/vod/mp4:BigBuckBunny_115k.mov";


            // Create a RTSP Client
            RTSPClient c = new RTSPClient(url, RTSPClient.RTP_TRANSPORT.TCP);

            // Wait for user to terminate programme
            Console.WriteLine("Press ENTER to exit");
            String dummy = Console.ReadLine();

            c.Stop();
            
        }
    }


    class RTSPClient
    {
        public enum RTP_TRANSPORT { UDP, TCP, MULTICAST };

        Rtsp.RtspTcpTransport rtsp_socket = null; // RTSP connection
        Rtsp.RtspListener rtsp_client = null;   // this wraps around a the RTSP tcp_socket stream
        RTP_TRANSPORT rtp_transport = RTP_TRANSPORT.TCP; // Mode, either RTP over UDP or RTP over TCP using the RTSP socket
        UDPSocket udp_pair = null;       // Pair of UDP ports used in RTP over UDP mode or in MULTICAST mode
        String url = "";                 // RTSP URL
        int video_payload = -1;          // Payload Type for the Video. (often 96 which is the first dynamic payload value)
        int video_data_channel = -1;     // RTP Channel Number used for the video stream or the UDP port number
        int video_rtcp_channel = -1;     // RTP Channel Number used for the rtcp status report messages OR the UDP port number
        byte[] video_sps = null;         // SPS from SDP prop-parameter-set
        byte[] video_pps = null;         // PPS from SDP prop-parameter-set
        List<byte[]> temporary_rtp_payloads = new List<byte[]>(); // used to assemble the RTP packets that form one RTP frame
        MemoryStream fragmented_nal = new MemoryStream(); // used to concatenate fragmented H264 NALs where NALs are split over RTP packets
        FileStream fs = null; // used to write the NALs to a .264 file
        StreamWriter fs2;     // used to write Log Messages to a file. (should switch to NLog)
        System.Timers.Timer keepalive_timer = null;

        // Constructor
        public RTSPClient(String url, RTP_TRANSPORT rtp_transport)
        {

            Rtsp.RtspUtils.RegisterUri();

            if (fs == null)
            {
                String filename = "rtsp_capture_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".264";
                fs = new FileStream(filename, FileMode.Create);

                String filename2 = "rtsp_capture_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".raw";
                fs2 = new StreamWriter(filename2);
            }

            
            Console.WriteLine("Connecting to " + url);
            this.url = url;

            // Use URI to extract hostname and port
            Uri uri = new Uri(url);

            // Connect to a RTSP Server. The RTSP session is a TCP connection
            try
            {
                rtsp_socket = new Rtsp.RtspTcpTransport(uri.Host, uri.Port);
            }
            catch
            {
                Console.WriteLine("Error - did not connect");
                return;
            }

            if (rtsp_socket.Connected == false)
            {
                Console.WriteLine("Error - did not connect");
                return;
            }

            // Connect a RTSP Listener to the RTSP Socket (or other Stream) to send RTSP messages and listen for RTSP replies
            rtsp_client = new Rtsp.RtspListener(rtsp_socket);

            rtsp_client.MessageReceived += Rtsp_MessageReceived;
            rtsp_client.DataReceived += Rtp_DataReceived;

            rtsp_client.Start(); // start listening for messages from the server (messages fire the MessageReceived event)


            // Check the RTP Transport
            // If the RTP transport is TCP then we interleave the RTP packets in the RTSP stream
            // If the RTP transport is UDP, we initialise two UDP sockets (one for video, one for RTCP status messages)
            // If the RTP transport is MULTICAST, we have to wait for the SETUP message to get the Multicast Address from the RTSP server
            this.rtp_transport = rtp_transport;
            if (rtp_transport == RTP_TRANSPORT.UDP)
            {
                udp_pair = new UDPSocket(50000, 50020); // give a range of 10 pairs (20 addresses) to try incase some address are in use
                udp_pair.DataReceived += Rtp_DataReceived;
                udp_pair.Start(); // start listening for data on the UDP ports
            }
            if (rtp_transport == RTP_TRANSPORT.TCP)
            {
                // Nothing to do. Data will arrive in the RTSP Listener
            }
            if (rtp_transport == RTP_TRANSPORT.MULTICAST)
            {
				// Nothing to do. Will open Multicast UDP sockets after the SETUP command
			}


            // Send OPTIONS
            // In the Received Message handler we will send DESCRIBE, SETUP and PLAY
            Rtsp.Messages.RtspRequest options_message = new Rtsp.Messages.RtspRequestOptions();
            options_message.RtspUri = new Uri(url);
            rtsp_client.SendMessage(options_message);
        }

        public void Stop()
        {
            Rtsp.Messages.RtspRequest teardown_message = new Rtsp.Messages.RtspRequestTeardown();
            teardown_message.RtspUri = new Uri(url);
            teardown_message.Session = session;
            rtsp_client.SendMessage(teardown_message);
            
            // clear up any UDP sockets
            if (udp_pair != null) udp_pair.Stop();
            
            // Stop the keepalive timer
            if (keepalive_timer != null) keepalive_timer.Stop();
            
            // Drop the RTSP session
            rtsp_client.Stop();
            
        }


        int rtp_count = 0; // used for statistics
        // RTP packet (or RTCP packet) has been received.
        public void Rtp_DataReceived(object sender, Rtsp.RtspChunkEventArgs e)
        {

            Rtsp.Messages.RtspData data_received = e.Message as Rtsp.Messages.RtspData;

            // Check which channel the Data was received on.
            // eg the Video Channel, the Video Control Channel (RTCP)
            // In the future would also check the Audio Channel and Audio Control Channel

            if (data_received.Channel == video_rtcp_channel)
            {
                Console.WriteLine("Received a RTCP message on channel "+ data_received.Channel);
                return;
            }

            if (data_received.Channel == video_data_channel)
            {
                // Received some Video Data on the correct channel.

                // RTP Packet Header
                // 0 - Version, P, X, CC, M, PT and Sequence Number
                //32 - Timestamp
                //64 - SSRC
                //96 - CSRCs (optional)
                //nn - Extension ID and Length
                //nn - Extension header

                int rtp_version = (e.Message.Data[0] >> 6);
                int rtp_padding = (e.Message.Data[0] >> 5) & 0x01;
                int rtp_extension = (e.Message.Data[0] >> 4) & 0x01;
                int rtp_csrc_count = (e.Message.Data[0] >> 0) & 0x0F;
                int rtp_marker = (e.Message.Data[1] >> 7) & 0x01;
                int rtp_payload_type = (e.Message.Data[1] >> 0) & 0x7F;
                uint rtp_sequence_number = ((uint)e.Message.Data[2] << 8) + (uint)(e.Message.Data[3]);
                uint rtp_timestamp = ((uint)e.Message.Data[4] << 24) + (uint)(e.Message.Data[5] << 16) + (uint)(e.Message.Data[6] << 8) + (uint)(e.Message.Data[7]);
                uint rtp_ssrc = ((uint)e.Message.Data[8] << 24) + (uint)(e.Message.Data[9] << 16) + (uint)(e.Message.Data[10] << 8) + (uint)(e.Message.Data[11]);

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

                String msg = "RTP Data " + rtp_count++
                                   + " V=" + rtp_version
                                   + " P=" + rtp_padding
                                   + " X=" + rtp_extension
                                   + " CC=" + rtp_csrc_count
                                   + " M=" + rtp_marker
                                   + " PT=" + rtp_payload_type
                    //             + " Seq=" + rtp_sequence_number
                    //             + " Time=" + rtp_timestamp
                    //             + " SSRC=" + rtp_ssrc
                                   + " Size=" + e.Message.Data.Length;
                fs2.WriteLine(msg);
                fs2.Flush();


                // Check the payload type in the RTP packet matches the Payload Type value from the SDP
                if (rtp_payload_type != video_payload)
                {
                    Console.WriteLine("Ignoring this RTP payload");
                    return; // ignore this data
                }


                // If rtp_marker is '1' then this is the final transmission for this packet.
                // If rtp_marker is '0' we need to accumulate data with the same timestamp

                // ToDo - Check Timestamp
                // ToDo - Could avoid a copy if there is only one RTP frame for the data (temp list is zero)

                // Add the RTP packet to the tempoary_rtp list

                byte[] rtp_payload = new byte[e.Message.Data.Length - rtp_payload_start]; // payload with RTP header removed
                System.Array.Copy(e.Message.Data, rtp_payload_start, rtp_payload, 0, rtp_payload.Length); // copy payload
                temporary_rtp_payloads.Add(rtp_payload);

                if (rtp_marker == 1)
                {
                    // End Marker is set. Process the RTP frame
                    Process_RTP_Frame(temporary_rtp_payloads);
                    temporary_rtp_payloads.Clear();
                }
            }
        }

        int norm, fu_a, fu_b, stap_a, stap_b, mtap16, mtap24 = 0; // used for diagnostics stats
        private string session;

        // Process an RTP Frame. A RTP Frame can consist of several RTP Packets
        public void Process_RTP_Frame(List<byte[]>rtp_payloads)
        {
            Console.WriteLine("RTP Data comprised of " + rtp_payloads.Count + " rtp packets");

            List<byte[]> nal_units = new List<byte[]>(); // Stores the NAL units for a Video Frame. May be more than one NAL unit in a video frame.

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
                    nal_units.Add(rtp_payloads[payload_index]);
                }
                // There are 4 types of Aggregation Packet (split over RTP payloads)
                else if (nal_header_type == 24)
                {
                    Console.WriteLine("Agg STAP-A");
                    stap_a++;
                    
                    // RTP packet contains multiple NALs, each with a 16 bit header
                    //   Read 16 byte size
                    //   Read NAL
                    try {
                    	int ptr = 1; // start after the nal_header_type which was '24'
                    	// if we have at least 2 more bytes (the 16 bit size) then consume more data
                    	while (ptr + 2 < (rtp_payloads[payload_index].Length-1)) {
	                    	int size = (rtp_payloads[payload_index][ptr] << 8) + (rtp_payloads[payload_index][ptr+1] << 0);
	                    	ptr = ptr + 2;
	                    	byte[] nal = new byte[size];
                            System.Array.Copy(rtp_payloads[payload_index],ptr,nal,0,size); // copy the NAL
	                    	nal_units.Add(nal); // Add to list of NALs for this RTP frame. Start Codes like 00 00 00 01 get added later
    	                	ptr = ptr + size;
        	            }
        	        } catch {
        	        	// do nothing
					}
                }
                else if (nal_header_type == 25)
                {
                    Console.WriteLine("Agg STAP-B not supported");
                    stap_b++;
                }
                else if (nal_header_type == 26)
                {
                    Console.WriteLine("Agg MTAP16 not supported");
                    mtap16++;
                }
                else if (nal_header_type == 27)
                {
                    Console.WriteLine("Agg MTAP24 not supported");
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

                    // Check Start and End flags
                    if (fu_header_s == 1 && fu_header_e == 0)
                    {
                        // Start of Fragment.
                        // Initiise the fragmented_nal byte array
                        // Build the NAL header with the original F and NRI flags but use the the Type field from the fu_header_type
                        byte reconstructed_nal_type = (byte)((nal_header_f_bit << 7) + (nal_header_nri << 5) + fu_header_type);

                        // Empty the stream
                        fragmented_nal.SetLength(0);

                        // Add reconstructed_nal_type byte to the memory stream
                        fragmented_nal.WriteByte(reconstructed_nal_type);

                         // copy the rest of the RTP payload to the memory stream
                        fragmented_nal.Write(rtp_payloads[payload_index], 2, rtp_payloads[payload_index].Length - 2);
                    }

                    if (fu_header_s == 0 && fu_header_e == 0)
                    {
                        // Middle part of Fragment
                        // Append this payload to the fragmented_nal
                        // Data starts after the NAL Unit Type byte and the FU Header byte
                        fragmented_nal.Write(rtp_payloads[payload_index], 2, rtp_payloads[payload_index].Length-2);
                    }

                    if (fu_header_s == 0 && fu_header_e == 1)
                    {
                        // End part of Fragment
                        // Append this payload to the fragmented_nal
                        // Data starts after the NAL Unit Type byte and the FU Header byte
                        fragmented_nal.Write(rtp_payloads[payload_index], 2, rtp_payloads[payload_index].Length - 2);

                        // Add the NAL to the array of NAL units
                        nal_units.Add(fragmented_nal.ToArray());
                    }
                }

                else if (nal_header_type == 29)
                {
                    Console.WriteLine("Frag FU-B not supported");
                    fu_b++;
                }
                else
                {
                    Console.WriteLine("Unknown NAL header " + nal_header_type + " not supported");
                }

            }

            // Output all the NALs that form one RTP Frame (one frame of video)
            Output_NAL(nal_units);

            // Output some statistics
            Console.WriteLine("Norm=" + norm + " ST-A=" + stap_a + " ST-B=" + stap_b + " M16=" + mtap16 + " M24=" + mtap24 + " FU-A=" + fu_a + " FU-B=" + fu_b);
        }


        // RTSP Messages are OPTIONS, DESCRIBE, SETUP, PLAY etc
        private void Rtsp_MessageReceived(object sender, Rtsp.RtspChunkEventArgs e)
        {
            Rtsp.Messages.RtspResponse message = e.Message as Rtsp.Messages.RtspResponse;

            Console.WriteLine("Received " + message.OriginalRequest.ToString());

            // If we get a reply to OPTIONS and CSEQ is 1 (which was our first command), then send the DESCRIBE
            // If we fer a reply to OPTIONS and CSEQ is not 1, it must have been a keepalive command
            if (message.OriginalRequest != null && message.OriginalRequest is Rtsp.Messages.RtspRequestOptions)
            {
            	if (message.CSeq == 1) {
            		// Start a Timer to send an OPTIONS command (for keepalive) every 20 seconds
	                keepalive_timer = new System.Timers.Timer();
	                keepalive_timer.Elapsed += Timer_Elapsed;
	    			keepalive_timer.Interval = 20 * 1000;
	    			keepalive_timer.Enabled = true;

                	// send the Describe
                	Rtsp.Messages.RtspRequest describe_message = new Rtsp.Messages.RtspRequestDescribe();
                	describe_message.RtspUri = new Uri(url);
                	rtsp_client.SendMessage(describe_message);
                } else {
                	// do nothing
        	    }
			}            
            

            // If we get a reply to DESCRIBE (which was our second command), then prosess SDP and send the SETUP
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

                // Process each 'Media' Attribute in the SDP (each sub-stream)
                // If the attribute is for Video, then carry out a SETUP and a PLAY
                // Only do this for the first Video attribute in case there is more than one in the SDP

                for (int x = 0; x < sdp_data.Medias.Count; x++)
                {
                    if (sdp_data.Medias[x].GetMediaType() == Rtsp.Sdp.Media.MediaType.video)
                    {
                        // We only want the first video sub-stream
                        if (video_payload == -1)
                        {
                            // seach the atributes for control, fmtp and rtpmap
                            String control = "";  // the "track" or "stream id"
                            Rtsp.Sdp.AttributFmtp fmtp = null; // holds SPS and PPS in base64
                            Rtsp.Sdp.AttributRtpMap rtpmap = null; // holds Payload format, eg 96 often used with H264 as first dynamic payload value
                            foreach (Rtsp.Sdp.Attribut attrib in sdp_data.Medias[x].Attributs)
                            {
                                if (attrib.Key.Equals("control")) control = attrib.Value;
                                if (attrib.Key.Equals("fmtp")) fmtp = attrib as Rtsp.Sdp.AttributFmtp;
                                if (attrib.Key.Equals("rtpmap")) rtpmap = attrib as Rtsp.Sdp.AttributRtpMap;
                            }

                            // Split the fmtp to get the sprop-parameter-sets which hold the SPS and PPS in base64
                            if(fmtp != null)
                            {
                                var param = Rtsp.Sdp.H264Parameters.Parse(fmtp.FormatParameter);
                                var sps_pps = param.SpropParameterSets;
                                if (sps_pps.Count > 0) video_sps = sps_pps[0];
                                if (sps_pps.Count > 1) video_pps = sps_pps[1];
                                Output_NAL(sps_pps); // output SPS and PPS
                            }




                            // Split the rtpmap to get the Payload Type
                            video_payload = 0;
                            if (rtpmap != null)
                                video_payload = rtpmap.PayloadNumber;
                            

                            Rtsp.Messages.RtspRequestSetup setup_message = new Rtsp.Messages.RtspRequestSetup();
                            setup_message.RtspUri = new Uri(url + "/" + control);

                            RtspTransport transport = null;
                            if (rtp_transport == RTP_TRANSPORT.TCP)
                            {
                              
                                // Server interleaves the RTP packets over the RTSP connection
                                // Example for TCP mode (RTP over RTSP)   Transport: RTP/AVP/TCP;interleaved=0-1
                                video_data_channel = 0;  // Used in DataReceived event handler
                                video_rtcp_channel = 1;  // Used in DataReceived event handler
                                transport = new RtspTransport()
                                {
                                    LowerTransport = RtspTransport.LowerTransportType.TCP,
                                    Interleaved = new PortCouple(video_data_channel, video_rtcp_channel), // Channel 0 for video. Channel 1 for RTCP status reports
                                };
                            }
                            if (rtp_transport == RTP_TRANSPORT.UDP)
                            {
                                // Server sends the RTP packets to a Pair of UDP Ports (one for data, one for rtcp control messages)
                                // Example for UDP mode                   Transport: RTP/AVP;unicast;client_port=8000-8001
                                video_data_channel = udp_pair.data_port;     // Used in DataReceived event handler
                                video_rtcp_channel = udp_pair.control_port;  // Used in DataReceived event handler
                                transport = new RtspTransport()
                                {
                                    LowerTransport = RtspTransport.LowerTransportType.UDP,
                                    IsMulticast = false,
                                    ClientPort = new PortCouple(video_data_channel, video_rtcp_channel), // a Channel for video. a Channel for RTCP status reports
                                };
                            }
                            if (rtp_transport == RTP_TRANSPORT.MULTICAST)
                            {
                            	// Server sends the RTP packets to a Pair of UDP ports (one for data, one for rtcp control messages)
                            	// using Multicast Address and Ports that are in the reply to the SETUP message
                            	// Example for MULTICAST mode     Transport: RTP/AVP;multicast
                            	video_data_channel = 0; // we get this information in the SETUP message reply
                            	video_data_channel = 0; // we get this information in the SETUP message reply
                                transport = new RtspTransport()
                                {
                                    LowerTransport = RtspTransport.LowerTransportType.UDP,
                                    IsMulticast = true
                            	};
							}
                            setup_message.AddTransport(transport);

                            rtsp_client.SendMessage(setup_message);
                        }
                    }
                }
            }


            // If we get a reply to SETUP (which was our third command), then process then send PLAY
            if (message.OriginalRequest != null && message.OriginalRequest is Rtsp.Messages.RtspRequestSetup)
            {
                // Got Reply to SETUP
                Console.WriteLine("Got reply from Setup. Session is " + message.Session);

                session = message.Session; // Session value used with Play, Pause, Teardown
                
                // Check the Transport header
                if (message.Headers.ContainsKey(RtspHeaderNames.Transport)) {

					RtspTransport transport = RtspTransport.Parse(message.Headers[RtspHeaderNames.Transport]);

					// Check if Transport header includes Multicast
					if (transport.IsMulticast) {
		                String multicast_address = transport.Destination;
		                video_data_channel = transport.Port.First;
		                video_rtcp_channel = transport.Port.Second;
		                
		                // Create the Pair of UDP Sockets in Multicast mode
		                udp_pair = new UDPSocket(multicast_address,video_data_channel,multicast_address,video_rtcp_channel);
		                udp_pair.DataReceived += Rtp_DataReceived;
		                udp_pair.Start();
		            }
				}                

                Rtsp.Messages.RtspRequest play_message = new Rtsp.Messages.RtspRequestPlay();
                play_message.RtspUri = new Uri(url);
                play_message.Session = session;
                rtsp_client.SendMessage(play_message);
            }

            // If we get a reply to PLAY (which was our fourth command), then we should have video being received
            if (message.OriginalRequest != null && message.OriginalRequest is Rtsp.Messages.RtspRequestPlay)
            {
                // Got Reply to PLAY
                Console.WriteLine("Got reply from Play  " + message.Command);
            }

        }

		void Timer_Elapsed (object sender, System.Timers.ElapsedEventArgs e)
		{
			// Send Keepalive message
        	Rtsp.Messages.RtspRequest options_message = new Rtsp.Messages.RtspRequestOptions();
			options_message.RtspUri = new Uri(url);
			rtsp_client.SendMessage(options_message);

		}


        // Output an array of NAL Units.
        // One frame of video may encoded in 1 large NAL unit, or it may be encoded in several small NAL units.
        // This function writes out all the NAL units that make one frame of video.
        // This is done to make it easier to feed H264 decoders which may require all the NAL units for a frame of video at the same time.

        // When writing to a .264 file we will add the Start Code 0x00 0x00 0x00 0x01 before each NAL unit
        // when outputting data for H264 decoders, please note that some decoders require a 32 bit size length header before each NAL unit instead of the Start Code
        private void Output_NAL(List<byte[]> nal_units)
        {
            if (fs == null) return; // check filestream initialised

            int bytes_written = 0;

            foreach (byte[] nal_unit in nal_units) {
                fs.Write(new byte[]{0x00,0x00,0x00,0x01}, 0, 4);  // Write Start Code
                fs.Write(nal_unit, 0, nal_unit.Length);           // Write NAL
                bytes_written += (nal_unit.Length + 4);
            }
            fs.Flush(true);
        }
    }

    public class UDPSocket
        {

        private UdpClient data_socket = null;
        private UdpClient control_socket = null;

        private Thread data_read_thread = null;
        private Thread control_read_thread = null;

        public int data_port = 50000;
        public int control_port = 50001;
        
        bool is_multicast = false;
        IPAddress data_mcast_addr;
		IPAddress control_mcast_addr;
        
        /// <summary>
        /// Initializes a new instance of the <see cref="UDPSocket"/> class.
		/// Creates two new UDP sockets using the start and end Port range
        /// </summary>
        public UDPSocket(int start_port, int end_port)
        {
        
        	is_multicast = false;
        	
            // open a pair of UDP sockets - one for data (video or audio) and one for the status channel (RTCP messages)
            data_port = start_port;
            control_port = start_port + 1;

            bool ok = false;
            while (ok == false && (control_port < end_port))
            {
                // Video/Audio port must be odd and command even (next one)
                try
                {
                    data_socket = new UdpClient(data_port);
                    control_socket = new UdpClient(control_port);
                    ok = true;
                }
                catch (SocketException)
                {
                    // Fail to allocate port, try again
                    if (data_socket != null)
                        data_socket.Close();
                    if (control_socket != null)
                        control_socket.Close();

                    // try next data or control port
                    data_port += 2;
                    control_port += 2;
                }
            }

            data_socket.Client.ReceiveBufferSize = 100 * 1024;

            control_socket.Client.DontFragment = false;
        }
        
        
        /// <summary>
        /// Initializes a new instance of the <see cref="UDPSocket"/> class.
		/// Used with Multicast mode with the Multicast Address and Port
        /// </summary>
        public UDPSocket(String data_multicast_address, int data_multicast_port, String control_multicast_address, int control_multicast_port)
        {
        
        	is_multicast = true;
        	
            // open a pair of UDP sockets - one for data (video or audio) and one for the status channel (RTCP messages)
            this.data_port = data_multicast_port;
            this.control_port = control_multicast_port;

            try
            {
				IPEndPoint data_ep = new IPEndPoint(IPAddress.Any,data_port);
				IPEndPoint control_ep = new IPEndPoint(IPAddress.Any,control_port);
				
				data_mcast_addr = IPAddress.Parse(data_multicast_address);
				control_mcast_addr = IPAddress.Parse(control_multicast_address);

				data_socket = new UdpClient();
				data_socket.Client.Bind(data_ep);
				data_socket.JoinMulticastGroup(data_mcast_addr);
				
                control_socket = new UdpClient();
                control_socket.Client.Bind(control_ep);
                control_socket.JoinMulticastGroup(control_mcast_addr);
                
                
                data_socket.Client.ReceiveBufferSize = 100 * 1024;

                control_socket.Client.DontFragment = false;

            }
            catch (SocketException)
            {
                // Fail to allocate port, try again
                if (data_socket != null)
                    data_socket.Close();
                if (control_socket != null)
                    control_socket.Close();

                return;
            }
        }

        /// <summary>
        /// Starts this instance.
        /// </summary>
        public void Start()
        {
            if (data_socket == null || control_socket == null)
            {
                throw new InvalidOperationException("UDP Forwader host was not initialized, can't continue");
            }

            if (data_read_thread != null)
            {
                throw new InvalidOperationException("Forwarder was stopped, can't restart it");
            }

            data_read_thread = new Thread( () => DoWorkerJob(data_socket, data_port));
            data_read_thread.Name = "DataPort " + data_port;
            data_read_thread.Start();

            control_read_thread = new Thread(() => DoWorkerJob(control_socket, control_port));
            control_read_thread.Name = "ControlPort " + control_port;
            control_read_thread.Start();
        }

        /// <summary>
        /// Stops this instance.
        /// </summary>
        public void Stop()
        {
        	if (is_multicast) {
        		// leave the multicast groups
        		data_socket.DropMulticastGroup(data_mcast_addr);
        		control_socket.DropMulticastGroup(control_mcast_addr);
			}
            data_socket.Close();
            control_socket.Close();
        }

        /// <summary>
        /// Occurs when message is received.
        /// </summary>
        public event EventHandler<Rtsp.RtspChunkEventArgs> DataReceived;

        /// <summary>
        /// Raises the <see cref="E:DataReceived"/> event.
        /// </summary>
        /// <param name="rtspChunkEventArgs">The <see cref="Rtsp.RtspChunkEventArgs"/> instance containing the event data.</param>
        protected void OnDataReceived(Rtsp.RtspChunkEventArgs rtspChunkEventArgs)
        {
            EventHandler<Rtsp.RtspChunkEventArgs> handler = DataReceived;

            if (handler != null)
                handler(this, rtspChunkEventArgs);
        }


        /// <summary>
        /// Does the video job.
        /// </summary>
        private void DoWorkerJob(System.Net.Sockets.UdpClient socket, int data_port)
        {

            IPEndPoint ipEndPoint = new IPEndPoint(IPAddress.Any, data_port);
            try
            {
                // loop until we get an exception eg the socket closed
                while (true)
                {
                    byte[] frame = socket.Receive(ref ipEndPoint);

                    // We have an RTP frame.
                    // Fire the DataReceived event with 'frame'
                    Console.WriteLine("Received RTP data on port " + data_port);

                    Rtsp.Messages.RtspChunk currentMessage = new Rtsp.Messages.RtspData();
                    // aMessage.SourcePort = ??
                    currentMessage.Data = frame;
                    ((Rtsp.Messages.RtspData)currentMessage).Channel = data_port;


                    OnDataReceived(new Rtsp.RtspChunkEventArgs(currentMessage));

                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SocketException)
            {
            }
        }
    }
}
