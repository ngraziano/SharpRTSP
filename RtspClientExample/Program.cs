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
            String url = "rtsp://192.168.1.125/onvif-media/media.amp?profile=quality_h264"; // Axis
            //String url = "rtsp://user:password@192.168.1.102/onvif-media/media.amp?profile=quality_h264"; // Axis
            //String url = "rtsp://192.168.1.124/rtsp_tunnel?h26x=4&line=1&inst=1"; // Bosch

            //String url = "rtsp://192.168.1.121:8554/h264";  // Raspberry Pi RPOS using Live555
            //String url = "rtsp://192.168.1.121:8554/h264m";  // Raspberry Pi RPOS using Live555 in Multicast mode

            //String url = "rtsp://127.0.0.1:8554/h264ESVideoTest"; // Live555 Cygwin
            //String url = "rtsp://192.168.1.160:8554/h264ESVideoTest"; // Live555 Cygwin
            //String url = "rtsp://127.0.0.1:8554/h264ESVideoTest"; // Live555 Cygwin
            // String url = "rtsp://wowzaec2demo.streamlock.net/vod/mp4:BigBuckBunny_115k.mov";

            // MJPEG Tests (Payload 26)
            //String url = "rtsp://192.168.1.125/onvif-media/media.amp?profile=mobile_jpeg";


            // Create a RTSP Client
            RTSPClient c = new RTSPClient(url, RTSPClient.RTP_TRANSPORT.TCP);

            // Wait for user to terminate programme
            // Check for null which is returned when running under some IDEs
            Console.WriteLine("Press ENTER to exit");

            String readline = null;
            while (readline == null) {
                readline = Console.ReadLine();

                // Avoid maxing out CPU on systems that instantly return null for ReadLine
                if (readline == null) Thread.Sleep(500);
            }

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
        String url = "";                 // RTSP URL (username & password will be stripped out
        String username = "";            // Username
        String password = "";            // Password
        String session = "";             // RTSP Session
        String realm = null;             // cached from most recent WWW-Authenticate reply
        String nonce = null;             // cached from most recent WWW-Authenticate reply
        int video_payload = -1;          // Payload Type for the Video. (often 96 which is the first dynamic payload value)
        int video_data_channel = -1;     // RTP Channel Number used for the video stream or the UDP port number
        int video_rtcp_channel = -1;     // RTP Channel Number used for the rtcp status report messages OR the UDP port number
        string video_codec = "";         // Codec used with Payload Types 96..127 (eg "H264")
        FileStream fs = null;    // used to write the NALs to a .264 file
        StreamWriter fs2 = null; // used to write Log Messages to a file. (should switch to NLog)
        System.Timers.Timer keepalive_timer = null;
        H264Payload h264Payload = new H264Payload();

        // Constructor
        public RTSPClient(String url, RTP_TRANSPORT rtp_transport)
        {

            Rtsp.RtspUtils.RegisterUri();

            if (fs == null)
            {
                String now = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                String filename = "rtsp_capture_" + now + ".264";
                fs = new FileStream(filename, FileMode.Create);

                String filename2 = "rtsp_capture_" + now + ".raw";
                fs2 = new StreamWriter(filename2);
            }


            Console.WriteLine("Connecting to " + url);
            this.url = url;

            // Use URI to extract host, port, username and password
            Uri uri = new Uri(this.url);
            if (uri.UserInfo.Length > 0) {
                try {
                    username = uri.UserInfo.Split(new char[] {':'})[0];
                    password = uri.UserInfo.Split(new char[] {':'})[1];
                    this.url = uri.GetComponents((UriComponents.AbsoluteUri &~ UriComponents.UserInfo),
                                        UriFormat.UriEscaped);
                    uri = new Uri(this.url);
                } catch {
                    username = null;
                    password = null;
                }
            }

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
            options_message.RtspUri = new Uri(this.url);
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
                Console.WriteLine("Received a RTCP message on channel " + data_received.Channel);
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
                    rtp_extension_size = ((uint)e.Message.Data[rtp_payload_start + 2] << 8) + (uint)(e.Message.Data[rtp_payload_start + 3] << 0) * 4; // units of extension_size is 4-bytes
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
                                   + " Time (MS)=" + rtp_timestamp / 90 // convert from 90kHZ clock to ms
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
                if (fs2 != null) fs2.WriteLine(msg);
                if (fs2 != null) fs2.Flush();


                // Check the payload type in the RTP packet matches the Payload Type value from the SDP
                if (rtp_payload_type != video_payload)
                {
                    Console.WriteLine("Ignoring this RTP payload");
                    return; // ignore this data
                }

                if (rtp_payload_type >= 96 && rtp_payload_type <= 127 && video_codec.Equals("H264")) {
                    // H264 RTP Packet

                    // If rtp_marker is '1' then this is the final transmission for this packet.
                    // If rtp_marker is '0' we need to accumulate data with the same timestamp

                    // ToDo - Check Timestamp
                    // Add the RTP packet to the tempoary_rtp list until we have a complete 'Frame'

                    byte[] rtp_payload = new byte[e.Message.Data.Length - rtp_payload_start]; // payload with RTP header removed
                    System.Array.Copy(e.Message.Data, rtp_payload_start, rtp_payload, 0, rtp_payload.Length); // copy payload

                    List<byte[]> nal_units = h264Payload.Process_H264_RTP_Packet(rtp_payload, rtp_marker); // this will cache the Packets until there is a Frame

                    if (nal_units == null) {
                        // we have not passed in enough RTP packets to make a Frame of video
                    } else {
                        // we have a frame of NAL Units. Write them to the file
                        Output_NAL(nal_units);
                    }
                }

                else if (rtp_payload_type == 26) {
                    Console.WriteLine("No parser for JPEG RTP packets");

                } else {
                    Console.WriteLine("No parser for this RTP payload");
                }
            }
        }


        // RTSP Messages are OPTIONS, DESCRIBE, SETUP, PLAY etc
        private void Rtsp_MessageReceived(object sender, Rtsp.RtspChunkEventArgs e)
        {
            Rtsp.Messages.RtspResponse message = e.Message as Rtsp.Messages.RtspResponse;

            Console.WriteLine("Received " + message.OriginalRequest.ToString());

            // Check if the Message has an Authenticate header. If so we update the 'realm' and 'nonce'
            if (message.Headers.ContainsKey(RtspHeaderNames.WWWAuthenticate)) {
                String www_authenticate = message.Headers[RtspHeaderNames.WWWAuthenticate];

                // Parse www_authenticate
                // EG:   Digest realm="AXIS_WS_ACCC8E3A0A8F", nonce="000057c3Y810622bff50b36005eb5efeae118626a161bf", stale=FALSE
                string[] items = www_authenticate.Split(new char[] { ',' , ' ' });
                foreach (string item in items) {
                    // Split on the = symbol and load in
                    string[] parts = item.Split(new char[] { '=' });
                    if (parts.Count() >= 2 && parts[0].Trim().Equals("realm"))
                        realm = parts[1].Trim(new char[] {' ','\"'}); // trim space and quotes
                    else if (parts.Count() >= 2 && parts[0].Trim().Equals("nonce"))
                        nonce = parts[1].Trim(new char[] {' ','\"'}); // trim space and quotes
                }

                Console.WriteLine("WWW Authorize parsed for " + realm + " " + nonce);
            }


            // If we get a reply to OPTIONS and CSEQ is 1 (which was our first command), then send the DESCRIBE
            // If we fer a reply to OPTIONS and CSEQ is not 1, it must have been a keepalive command
            if (message.OriginalRequest != null && message.OriginalRequest is Rtsp.Messages.RtspRequestOptions)
            {
                if (message.CSeq == 1)
                {
                    // Start a Timer to send an OPTIONS command (for keepalive) every 20 seconds
                    keepalive_timer = new System.Timers.Timer();
                    keepalive_timer.Elapsed += Timer_Elapsed;
                    keepalive_timer.Interval = 20 * 1000;
                    keepalive_timer.Enabled = true;

                    // send the DESCRIBE. First time around we have no WWW-Authorise
                    Rtsp.Messages.RtspRequest describe_message = new Rtsp.Messages.RtspRequestDescribe();
                    describe_message.RtspUri = new Uri(url);
                    rtsp_client.SendMessage(describe_message);
                }
                else
                {
                    // do nothing
                }
            }


            // If we get a reply to DESCRIBE (which was our second command), then prosess SDP and send the SETUP
            if (message.OriginalRequest != null && message.OriginalRequest is Rtsp.Messages.RtspRequestDescribe)
            {

                // Got a reply for DESCRIBE

                // First time we send DESCRIBE we will not have the authorization Nonce so we
                // handle the Unauthorized 401 error here and send a new DESCRIBE message

                if (message.IsOk == false) {
                    Console.WriteLine("Got Error in DESCRIBE Reply " + message.ReturnCode + " " + message.ReturnMessage);

                    if (message.ReturnCode == 401 && (message.OriginalRequest.Headers.ContainsKey(RtspHeaderNames.Authorization)==false)) {
                        // Error 401 - Unauthorized, but the request did not use Authorizarion.

                        if (username == null || password == null) {
                            // we do nothave a username or password. Abort
                            return;
                        }
                        // Send a new DESCRIBE with authorization
                        String digest_authorization = GenerateDigestAuthorization(username,password,
                                                                                realm,nonce,url,"DESCRIBE");
                        
                        Rtsp.Messages.RtspRequest describe_message = new Rtsp.Messages.RtspRequestDescribe();
                        describe_message.RtspUri = new Uri(url);
                        if (digest_authorization!=null) describe_message.Headers.Add(RtspHeaderNames.Authorization,digest_authorization);
                        rtsp_client.SendMessage(describe_message);
                        return;
                    } else if (message.ReturnCode == 401 && (message.OriginalRequest.Headers.ContainsKey(RtspHeaderNames.Authorization)==true)) {
                        // Authorization failed
                        return;
                    } else {
                        // some other error
                        return;
                    }
                            
                }

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
                    if (sdp_data.Medias[x].MediaType == Rtsp.Sdp.Media.MediaTypes.video)
                    {

                        // We only want the first video sub-stream
                        if (video_payload == -1)
                        {
                            video_payload = sdp_data.Medias[x].PayloadType;

                            // search the attributes for control, fmtp and rtpmap
                            String control = "";  // the "track" or "stream id"
                            Rtsp.Sdp.AttributFmtp fmtp = null; // holds SPS and PPS in base64 (h264)
                            Rtsp.Sdp.AttributRtpMap rtpmap = null; // custom payload (>=96) details
                            foreach (Rtsp.Sdp.Attribut attrib in sdp_data.Medias[x].Attributs)
                            {
                                if (attrib.Key.Equals("control")) {
                                    String sdp_control = attrib.Value;
                                    if (sdp_control.ToLower().StartsWith("rtsp://")) {
                                        control = sdp_control; //absolute path
                                    } else {
                                        control = url + "/" + sdp_control; // relative path
                                    }
                                }
                                if (attrib.Key.Equals("fmtp")) fmtp = attrib as Rtsp.Sdp.AttributFmtp;
                                if (attrib.Key.Equals("rtpmap")) rtpmap = attrib as Rtsp.Sdp.AttributRtpMap;
                            }

                            // If the rtpmap contains H264 then split the fmtp to get the sprop-parameter-sets which hold the SPS and PPS in base64
                            if (rtpmap!= null && rtpmap.Value.Contains("H264") && fmtp != null)
                            {
                                video_codec = "H264";
                                var param = Rtsp.Sdp.H264Parameters.Parse(fmtp.FormatParameter);
                                var sps_pps = param.SpropParameterSets;
                                if (sps_pps.Count() >= 2) {
                                    byte[] sps = sps_pps[0];
                                    byte[] pps = sps_pps[1];
                                    Output_SPS_PPS(sps,pps); // output SPS and PPS
                                }
                            }

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
                                video_rtcp_channel = 0; // we get this information in the SETUP message reply
                                transport = new RtspTransport()
                                {
                                    LowerTransport = RtspTransport.LowerTransportType.UDP,
                                    IsMulticast = true
                                };
                            }

                            // Add authorization (if there is a username and password)
                            String digest_authorization = GenerateDigestAuthorization(username,password,
                                                                                  realm,nonce,url,"SETUP");

                            // Send SETUP
                            Rtsp.Messages.RtspRequestSetup setup_message = new Rtsp.Messages.RtspRequestSetup();
                            setup_message.RtspUri = new Uri(control);
                            setup_message.AddTransport(transport);
                            if (digest_authorization!=null) setup_message.Headers.Add("Authorization",digest_authorization);
                            rtsp_client.SendMessage(setup_message);
                        }
                    }
                }
            }


            // If we get a reply to SETUP (which was our third command), then process and then send PLAY
            if (message.OriginalRequest != null && message.OriginalRequest is Rtsp.Messages.RtspRequestSetup)
            {
                // Got Reply to SETUP
                if (message.IsOk == false) {
                    Console.WriteLine("Got Error in SETUP Reply " + message.ReturnCode + " " + message.ReturnMessage);
                    return;
                }

                Console.WriteLine("Got reply from Setup. Session is " + message.Session);

                session = message.Session; // Session value used with Play, Pause, Teardown

                // Check the Transport header
                if (message.Headers.ContainsKey(RtspHeaderNames.Transport))
                {

                    RtspTransport transport = RtspTransport.Parse(message.Headers[RtspHeaderNames.Transport]);

                    // Check if Transport header includes Multicast
                    if (transport.IsMulticast)
                    {
                        String multicast_address = transport.Destination;
                        video_data_channel = transport.Port.First;
                        video_rtcp_channel = transport.Port.Second;

                        // Create the Pair of UDP Sockets in Multicast mode
                        udp_pair = new UDPSocket(multicast_address, video_data_channel, multicast_address, video_rtcp_channel);
                        udp_pair.DataReceived += Rtp_DataReceived;
                        udp_pair.Start();
                    }
                }

                // Send PLAY
                Rtsp.Messages.RtspRequest play_message = new Rtsp.Messages.RtspRequestPlay();
                play_message.RtspUri = new Uri(url);
                play_message.Session = session;
                rtsp_client.SendMessage(play_message);
            }

            // If we get a reply to PLAY (which was our fourth command), then we should have video being received
            if (message.OriginalRequest != null && message.OriginalRequest is Rtsp.Messages.RtspRequestPlay)
            {
                // Got Reply to PLAY
                if (message.IsOk == false) {
                    Console.WriteLine("Got Error in PLAY Reply " + message.ReturnCode + " " + message.ReturnMessage);
                    return;
                }

                Console.WriteLine("Got reply from Play  " + message.Command);
            }

        }

        void Timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            // Send Keepalive message
            Rtsp.Messages.RtspRequest options_message = new Rtsp.Messages.RtspRequestOptions();
            options_message.RtspUri = new Uri(url);
            rtsp_client.SendMessage(options_message);

        }

        // Generate Digest Authorization
        public string GenerateDigestAuthorization(string username, string password,
            string realm, string nonce, string url, string command)  {

            if (username == null || username.Length == 0) return null;
            if (password == null || password.Length == 0) return null;
            if (realm == null || realm.Length == 0) return null;
            if (nonce == null || nonce.Length == 0) return null;
           
            MD5 md5 = System.Security.Cryptography.MD5.Create();
            String hashA1 = CalculateMD5Hash(md5, username+":"+realm+":"+password);
            String hashA2 = CalculateMD5Hash(md5, command + ":" + url);
            String response = CalculateMD5Hash(md5, hashA1 + ":" + nonce + ":" + hashA2);

            const String quote = "\"";
            String digest_authorization = "Digest username=" + quote + username + quote +", "
                + "realm=" + quote + realm + quote + ", "
                + "nonce=" + quote + nonce + quote + ", "
                + "uri=" + quote + url + quote + ", "
                + "response=" + quote + response + quote;

            return digest_authorization;
            
        }

        // MD5 (lower case)
        public string CalculateMD5Hash(MD5 md5_session, string input)
        {
            byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes(input);
            byte[] hash = md5_session.ComputeHash(inputBytes);

            StringBuilder output = new StringBuilder();
            for (int i = 0; i < hash.Length; i++) {
                output.Append(hash[i].ToString("x2"));
            }

            return output.ToString();
        }


        // Output SPS and PPS
        // When writing to a .264 file we will add the Start Code 0x00 0x00 0x00 0x01 before the SPS and before the PPS
        private void Output_SPS_PPS(byte[] sps, byte[] pps) {
            if (fs == null) return; // check filestream initialised

            fs.Write(new byte[] { 0x00, 0x00, 0x00, 0x01 }, 0, 4);  // Write Start Code
            fs.Write(sps, 0, sps.Length);                           // Write SPS
            fs.Write(new byte[] { 0x00, 0x00, 0x00, 0x01 }, 0, 4);  // Write Start Code
            fs.Write(pps, 0, pps.Length);                           // Write PPS
            fs.Flush(true);
        }

        // Output an array of NAL Units.
        // One frame of video may be encoded in 1 large NAL unit, or it may be encoded in several small NAL units.
        // This function writes out all the NAL units that make one frame of video.
        // This is done to make it easier to feed H264 decoders which may require all the NAL units for a frame of video at the same time.

        // When writing to a .264 file we will add the Start Code 0x00 0x00 0x00 0x01 before each NAL unit
        // when outputting data for H264 decoders, please note that some decoders require a 32 bit size length header before each NAL unit instead of the Start Code
        private void Output_NAL(List<byte[]> nal_units)
        {
            if (fs == null) return; // check filestream initialised

            foreach (byte[] nal_unit in nal_units)
            {
                fs.Write(new byte[] { 0x00, 0x00, 0x00, 0x01 }, 0, 4);  // Write Start Code
                fs.Write(nal_unit, 0, nal_unit.Length);           // Write NAL
            }
            fs.Flush(true);
        }
    }
}
