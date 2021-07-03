using System;
using System.Diagnostics.Contracts;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Rtsp;
using System.Text;
using System.Collections.Generic;

// RTSP Server Example (c) Roger Hardiman, 2016, 2018, 2020
// Released uder the MIT Open Source Licence
//
// Re-uses some code from the Multiplexer example of SharpRTSP
//
// Creates a server to listen for RTSP Commands (eg OPTIONS, DESCRIBE, SETUP, PLAY)
// Accepts SPS/PPS/NAL H264 video data and sends out to RTSP clients

public class RtspServer : IDisposable
{

    const uint global_ssrc = 0x4321FADE; // 8 hex digits

    private TcpListener _RTSPServerListener;
    private ManualResetEvent _Stopping;
    private Thread _ListenTread;

    const int video_payload_type = 96; // = user defined payload, requuired for H264
    byte[] raw_sps = null;
    byte[] raw_pps = null;

    const int audio_payload_type = 0; // = Hard Coded to PCMU audio

    List<RTSPConnection> rtsp_list = new List<RTSPConnection>(); // list of RTSP Listeners

    Random rnd = new Random();
    int session_handle = 1;

	Authentication auth = null;

    /// <summary>
    /// Initializes a new instance of the <see cref="RTSPServer"/> class.
    /// </summary>
    /// <param name="aPortNumber">A numero port.</param>
	/// <param name="username">username.</param>
	/// <param name="password">password.</param>
    public RtspServer(int portNumber, String username, String password)
    {
        if (portNumber < System.Net.IPEndPoint.MinPort || portNumber > System.Net.IPEndPoint.MaxPort)
            throw new ArgumentOutOfRangeException("aPortNumber", portNumber, "Port number must be between System.Net.IPEndPoint.MinPort and System.Net.IPEndPoint.MaxPort");
        Contract.EndContractBlock();

		if (String.IsNullOrEmpty(username) == false
		    && String.IsNullOrEmpty(password) == false) {
		    String realm = "SharpRTSPServer";
		    auth = new Authentication(username,password,realm,Authentication.Type.Digest);
		} else {
			auth = null;
		}

        RtspUtils.RegisterUri();
        _RTSPServerListener = new TcpListener(IPAddress.Any, portNumber);
    }

    /// <summary>
    /// Starts the listen.
    /// </summary>
    public void StartListen()
    {
        _RTSPServerListener.Start();

        _Stopping = new ManualResetEvent(false);
        _ListenTread = new Thread(new ThreadStart(AcceptConnection));
        _ListenTread.Start();
    }


    /// <summary>
    /// Accepts the connection.
    /// </summary>
    private void AcceptConnection()
    {
        try
        {
            while (!_Stopping.WaitOne(0))
            {
                // Wait for an incoming TCP Connection
                TcpClient oneClient = _RTSPServerListener.AcceptTcpClient();
                Console.WriteLine("Connection from " + oneClient.Client.RemoteEndPoint.ToString());

                // Hand the incoming TCP connection over to the RTSP classes
                var rtsp_socket = new RtspTcpTransport(oneClient);
                RtspListener newListener = new RtspListener(rtsp_socket);
                newListener.MessageReceived += RTSP_Message_Received;
                //RTSPDispatcher.Instance.AddListener(newListener);

                // Add the RtspListener to the RTSPConnections List
                lock (rtsp_list) {
                    RTSPConnection new_connection = new RTSPConnection();
                    new_connection.listener = newListener;
                    new_connection.client_hostname = newListener.RemoteAdress.Split(':')[0];
                    new_connection.ssrc = global_ssrc;

                    new_connection.time_since_last_rtsp_keepalive = DateTime.UtcNow;
                    new_connection.video.time_since_last_rtcp_keepalive = DateTime.UtcNow;

                    rtsp_list.Add(new_connection);
                }

                newListener.Start();
            }
        }
        catch (SocketException error)
        {
            // _logger.Warn("Got an error listening, I have to handle the stopping which also throw an error", error);
        }
        catch (Exception error)
        {
            // _logger.Error("Got an error listening...", error);
            throw;
        }


    }


    public void StopListen()
    {
        _RTSPServerListener.Stop();
        _Stopping.Set();
        _ListenTread.Join();
    }

    #region IDisposable Membres

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopListen();
            _Stopping.Dispose();
        }
    }

    #endregion

    // Process each RTSP message that is received
    private void RTSP_Message_Received(object sender, RtspChunkEventArgs e)
    {
        // Cast the 'sender' and 'e' into the RTSP Listener (the Socket) and the RTSP Message
        Rtsp.RtspListener listener = sender as Rtsp.RtspListener;
        Rtsp.Messages.RtspMessage message = e.Message as Rtsp.Messages.RtspMessage;

        Console.WriteLine("RTSP message received " + message);

        
		// Check if the RTSP Message has valid authentication (validating against username,password,realm and nonce)
		if (auth != null) {
			bool authorized = false;
			if (message.Headers.ContainsKey("Authorization") == true ) {
                // The Header contained Authorization
				// Check the message has the correct Authorization
                // If it does not have the correct Authorization then close the RTSP connection
				authorized = auth.IsValid(message);

                if (authorized == false) {
                    // Send a 401 Authentication Failed reply, then close the RTSP Socket
                    Rtsp.Messages.RtspResponse authorization_response = (e.Message as Rtsp.Messages.RtspRequest).CreateResponse();
                    authorization_response.AddHeader("WWW-Authenticate: " + auth.GetHeader());
                    authorization_response.ReturnCode = 401;
                    listener.SendMessage(authorization_response);

                    lock (rtsp_list) {
                        foreach (RTSPConnection connection in rtsp_list.ToArray()){
                            if (connection.listener == listener) {
                                rtsp_list.Remove(connection);
                            }
                        }
                    }
                    listener.Dispose();
                    return;

                }
            }
            if ((message.Headers.ContainsKey("Authorization") == false)){
                // Send a 401 Authentication Failed with extra info in WWW-Authenticate
                // to tell the Client if we are using Basic or Digest Authentication
				Rtsp.Messages.RtspResponse authorization_response = (e.Message as Rtsp.Messages.RtspRequest).CreateResponse();
				authorization_response.AddHeader("WWW-Authenticate: " + auth.GetHeader()); // 'Basic' or 'Digest'
				authorization_response.ReturnCode = 401;
				listener.SendMessage(authorization_response);
				return;
			}
		}

        // Update the RTSP Keepalive Timeout
        // We could check that the message is GET_PARAMETER or OPTIONS for a keepalive but instead we will update the timer on any message
        lock (rtsp_list)
        {
            foreach (RTSPConnection connection in rtsp_list)
            {
                if (connection.listener.RemoteAdress.Equals(listener.RemoteAdress))
                {
                    // found the connection
                    connection.time_since_last_rtsp_keepalive = DateTime.UtcNow;
                    break;

                }
            }
        }


        // Handle OPTIONS message
        if (message is Rtsp.Messages.RtspRequestOptions)
        {
            // Create the reponse to OPTIONS
            Rtsp.Messages.RtspResponse options_response = (e.Message as Rtsp.Messages.RtspRequestOptions).CreateResponse();
            listener.SendMessage(options_response);
        }

        // Handle DESCRIBE message
        if (message is Rtsp.Messages.RtspRequestDescribe)
        {
            String requested_url = (message as Rtsp.Messages.RtspRequestDescribe).RtspUri.ToString();
            Console.WriteLine("Request for " + requested_url);

            // TODO. Check the requsted_url is valid. In this example we accept any RTSP URL



            // if the SPS and PPS are not defined yet, we have to return an error
            if (raw_sps == null || raw_pps == null)
            {
                Rtsp.Messages.RtspResponse describe_response2 = (e.Message as Rtsp.Messages.RtspRequestDescribe).CreateResponse();
                describe_response2.ReturnCode = 400; // 400 Bad Request
                listener.SendMessage(describe_response2);
                return;
            }


            // Make the Base64 SPS and PPS
            // raw_sps has no 0x00 0x00 0x00 0x01 or 32 bit size header
            // raw_pps has no 0x00 0x00 0x00 0x01 or 32 bit size header
            String sps_str = Convert.ToBase64String(raw_sps);
            String pps_str = Convert.ToBase64String(raw_pps);

            // Make the profile-level-id
            // Eg a string of profile-level-id=42A01E is
            // a Profile eg Constrained Baseline, Baseline, Extended, Main, High. This defines which features in H264 are used
            // a Level eg 1,2,3 or 4. This defines a max resoution for the video. 2=up to SD, 3=upto 1080p. Decoders can then reserve sufficient RAM for frame buffers
            int profile_idc = 77; // Main Profile
            int profile_iop = 0; // bit 7 (msb) is 0 so constrained_flag is false
            int level = 42; // Level 4.2


            string profile_level_id_str = profile_idc.ToString("X2") // convert to hex, padded to 2 characters
                                        + profile_iop.ToString("X2")
                                        + level.ToString("X2");

            StringBuilder sdp = new StringBuilder();

            // Generate the SDP
            // The sprop-parameter-sets provide the SPS and PPS for H264 video
            // The packetization-mode defines the H264 over RTP payloads used but is Optional
            sdp.Append("v=0\n");
            sdp.Append("o=user 123 0 IN IP4 0.0.0.0\n");
            sdp.Append("s=SharpRTSP Test Camera\n");
            sdp.Append("m=video 0 RTP/AVP 96\n");
            sdp.Append("c=IN IP4 0.0.0.0\n");
            sdp.Append("a=control:trackID=0\n");
            sdp.Append("a=rtpmap:96 H264/90000\n");
            sdp.Append("a=fmtp:96 profile-level-id=" + profile_level_id_str +"; sprop-parameter-sets=" + sps_str + "," + pps_str + ";\n");

            // AUDIO
            sdp.Append("m=audio 0 RTP/AVP 0\n"); // <---- 0 means G711 ULAW
            sdp.Append("a=control:trackID=1\n");
            sdp.Append("a=rtpmap:0 PCMU/8000\n");
            // sdp.Append(media header info if we had AAC or other audio codec)
            

            byte[] sdp_bytes = Encoding.ASCII.GetBytes(sdp.ToString());

            // Create the reponse to DESCRIBE
            // This must include the Session Description Protocol (SDP)
            Rtsp.Messages.RtspResponse describe_response = (e.Message as Rtsp.Messages.RtspRequestDescribe).CreateResponse();

            describe_response.AddHeader("Content-Base: " + requested_url);
            describe_response.AddHeader("Content-Type: application/sdp");
            describe_response.Data = sdp_bytes;
            describe_response.AdjustContentLength();
            listener.SendMessage(describe_response);
        }

        // Handle SETUP message
        if (message is Rtsp.Messages.RtspRequestSetup)
        {

            // 
            var setupMessage = message as Rtsp.Messages.RtspRequestSetup;

            // Check the RTSP transport
            // If it is UDP or Multicast, create the sockets
            // If it is RTP over RTSP we send data via the RTSP Listener

            // FIXME client may send more than one possible transport.
            // very rare
            Rtsp.Messages.RtspTransport transport = setupMessage.GetTransports()[0];


            // Construct the Transport: reply from the Server to the client
            Rtsp.Messages.RtspTransport transport_reply = new Rtsp.Messages.RtspTransport();
            transport_reply.SSrc = global_ssrc.ToString("X8"); // Convert to Hex, padded to 8 characters

            if (transport.LowerTransport == Rtsp.Messages.RtspTransport.LowerTransportType.TCP)
            {
                // RTP over RTSP mode}
                transport_reply.LowerTransport = Rtsp.Messages.RtspTransport.LowerTransportType.TCP;
                transport_reply.Interleaved = new Rtsp.Messages.PortCouple(transport.Interleaved.First, transport.Interleaved.Second);
            }

            Rtsp.UDPSocket udp_pair = null;
            if (transport.LowerTransport == Rtsp.Messages.RtspTransport.LowerTransportType.UDP
                && transport.IsMulticast == false)
            {
                Boolean udp_supported = true;
                if (udp_supported) {
                    // RTP over UDP mode
                    // Create a pair of UDP sockets - One is for the Data (eg Video/Audio), one is for the RTCP
                    udp_pair = new Rtsp.UDPSocket(50000, 51000); // give a range of 500 pairs (1000 addresses) to try incase some address are in use
                    udp_pair.DataReceived += (object local_sender, RtspChunkEventArgs local_e) => {
                        // RTCP data received
                        Console.WriteLine("RTCP data received " + local_sender.ToString() + " " + local_e.ToString());
                        // TODO - Find the Connection and update the keepalive
                    };
                    udp_pair.Start(); // start listening for data on the UDP ports

                    // Pass the Port of the two sockets back in the reply
                    transport_reply.LowerTransport = Rtsp.Messages.RtspTransport.LowerTransportType.UDP;
                    transport_reply.IsMulticast = false;
                    transport_reply.ServerPort = new Rtsp.Messages.PortCouple(udp_pair.data_port, udp_pair.control_port);
                    transport_reply.ClientPort = transport.ClientPort;
                } else {
                    transport_reply = null;
                }
            }

            if (transport.LowerTransport == Rtsp.Messages.RtspTransport.LowerTransportType.UDP
                && transport.IsMulticast == true)
            {
                // RTP over Multicast UDP mode}
                // Create a pair of UDP sockets in Multicast Mode
                // Pass the Ports of the two sockets back in the reply
                transport_reply.LowerTransport = Rtsp.Messages.RtspTransport.LowerTransportType.UDP;
                transport_reply.IsMulticast = true;
                transport_reply.Port = new Rtsp.Messages.PortCouple(7000, 7001);  // FIX

                // for now until implemented
                transport_reply = null;
            }


            if (transport_reply != null)
            {

                // Update the stream within the session with transport information
                // If a Session ID is passed in we should match SessionID with other SessionIDs but we can match on RemoteAddress
                String copy_of_session_id = "";
                lock (rtsp_list)
                {
                    foreach (RTSPConnection connection in rtsp_list)
                    {
                        if (connection.listener.RemoteAdress.Equals(listener.RemoteAdress)) {
                            // Check the Track ID to determine if this is a SETUP for the Video Stream
                            // or a SETUP for an Audio Stream.

                            // In the SDP the H264 video track is TrackID 0
                            // and the Audio Track is TrackID 1
                            RTPStream stream;
                            if (setupMessage.RtspUri.AbsolutePath.EndsWith("trackID=0")) stream = connection.video;
                            else if (setupMessage.RtspUri.AbsolutePath.EndsWith("trackID=1")) stream = connection.audio;
                            else continue; // error case - track unknown


                            // found the connection
                            // Add the transports to the stream
                            stream.client_transport = transport;
                            stream.transport_reply = transport_reply;

                            // If we are sending in UDP mode, add the UDP Socket pair and the Client Hostname
                            stream.udp_pair = udp_pair;


                            // When there is Video and Audio there are two SETUP commands.
                            // For the first SETUP command we will generate the connection.session_id and return a SessionID in the Reply.
                            // For the 2nd command the client will send is the SessionID.

                            if (connection.session_id == "") {
                                connection.session_id = session_handle.ToString();
                                session_handle++;
                            }
                            // ELSE, could check the Session passed in matches the Session we generated on last SETUP command


                            // Copy the Session ID, as we use it in the reply
                            copy_of_session_id = connection.session_id;
                            break;
                        }
                    }
                }

                Rtsp.Messages.RtspResponse setup_response = setupMessage.CreateResponse();
                setup_response.Headers[Rtsp.Messages.RtspHeaderNames.Transport] = transport_reply.ToString();
                setup_response.Session = copy_of_session_id;
                listener.SendMessage(setup_response);
            }
            else
            {
                Rtsp.Messages.RtspResponse setup_response = setupMessage.CreateResponse();
                // unsuported transport
                setup_response.ReturnCode = 461;
                listener.SendMessage(setup_response);
            }

        }

        // Handle PLAY message (Sent with a Session ID)
        if (message is Rtsp.Messages.RtspRequestPlay)
        {
            lock (rtsp_list)
            {
                // Search for the Session in the Sessions List. Change the state to "PLAY"
                bool session_found = false;
                foreach (RTSPConnection connection in rtsp_list)
                {
                    if (message.Session == connection.session_id)
                    {
                        // found the session
                        session_found = true;

                        string range = "npt=0-";   // Playing the 'video' from 0 seconds until the end
                        string rtp_info = "url="+((Rtsp.Messages.RtspRequestPlay)message).RtspUri+";seq=" + connection.video.sequence_number; // TODO Add rtptime  +";rtptime="+session.rtp_initial_timestamp;
                        // Add audio too
                        rtp_info += "," + "url="+((Rtsp.Messages.RtspRequestPlay)message).RtspUri+";seq=" + connection.audio.sequence_number; // TODO Add rtptime  +";rtptime="+session.rtp_initial_timestamp;
                        

                        //    'RTP-Info: url=rtsp://192.168.1.195:8557/h264/track1;seq=33026;rtptime=3014957579,url=rtsp://192.168.1.195:8557/h264/track2;seq=42116;rtptime=3335975101'

                        // Send the reply
                        Rtsp.Messages.RtspResponse play_response = (e.Message as Rtsp.Messages.RtspRequestPlay).CreateResponse();
                        play_response.AddHeader("Range: " + range);
                        play_response.AddHeader("RTP-Info: " + rtp_info);
                        listener.SendMessage(play_response);

                        connection.video.must_send_rtcp_packet = true;
                        connection.audio.must_send_rtcp_packet = true;

                        // Allow video and audio to go to this client
                        connection.play = true;

                        break;
                    }
                }

                if (session_found == false) {
                    // Session ID was not found in the list of Sessions. Send a 454 error
                    Rtsp.Messages.RtspResponse play_failed_response = (e.Message as Rtsp.Messages.RtspRequestPlay).CreateResponse();
                    play_failed_response.ReturnCode = 454; // Session Not Found
                    listener.SendMessage(play_failed_response);
                }

            }

        }

        // Handle PAUSE message (Sent with a Session ID)
        if (message is Rtsp.Messages.RtspRequestPause)
        {
            lock (rtsp_list)
            {
                // Search for the Session in the Sessions List. Change the state of "PLAY" 
                foreach (RTSPConnection connection in rtsp_list)
                {
                    if (message.Session == connection.session_id)
                    {
                        // found the session
                        connection.play = false;
                        break;
                    }
                }
            }

            // ToDo - only send back the OK response if the Session in the RTSP message was found
            Rtsp.Messages.RtspResponse pause_response = (e.Message as Rtsp.Messages.RtspRequestPause).CreateResponse();
            listener.SendMessage(pause_response);
        }


        // Handle GET_PARAMETER message, often used as a Keep Alive
        if (message is Rtsp.Messages.RtspRequestGetParameter)
        {
            // Create the reponse to GET_PARAMETER
            Rtsp.Messages.RtspResponse getparameter_response = (e.Message as Rtsp.Messages.RtspRequestGetParameter).CreateResponse();
            listener.SendMessage(getparameter_response);
        }


        // Handle TEARDOWN (sent with a Session ID)
        if (message is Rtsp.Messages.RtspRequestTeardown)
        {
            lock (rtsp_list)
            {
                // Search for the Session in the Sessions List.
                foreach (RTSPConnection connection in rtsp_list.ToArray()) // Convert to ToArray so we can delete from the rtp_list
                {
                    if (message.Session == connection.session_id)
                    {
                        // If this is UDP, close the transport
                        // For TCP there is no transport to close (as RTP packets were interleaved into the RTSP connection)
                        if (connection.video.udp_pair != null) {
                            connection.video.udp_pair.Stop();
                            connection.video.udp_pair = null;
                        }

                        if (connection.audio.udp_pair != null) {
                            connection.audio.udp_pair.Stop();
                            connection.audio.udp_pair = null;
                        }

                        rtsp_list.Remove(connection);

                        // Close the RTSP socket
                        listener.Dispose();
                    }
                }
            }
        }
    }

    public void checkTimeouts(out int current_rtsp_count, out int current_rtsp_play_count) {
        DateTime now = DateTime.UtcNow;
        int timeout_in_seconds = 70;  // must have a RTSP message every 70 seconds or we will close the connection

        lock (rtsp_list) {

            current_rtsp_count = rtsp_list.Count;
            current_rtsp_play_count = 0;
            foreach (RTSPConnection connection in rtsp_list.ToArray()) { // Convert to Array to allow us to delete from rtsp_list
                // RTSP Timeout (clients receiving RTP video over the RTSP session
                // do not need to send a keepalive (so we check for Socket write errors)
                Boolean sending_rtp_via_tcp = false;
                if ((connection.video.client_transport != null) &&
                    (connection.video.client_transport.LowerTransport == Rtsp.Messages.RtspTransport.LowerTransportType.TCP))
                {
                    sending_rtp_via_tcp = true;
                }

                if (sending_rtp_via_tcp == false && ((now - connection.time_since_last_rtsp_keepalive).TotalSeconds > timeout_in_seconds)) {

                    Console.WriteLine("Removing session " + connection.session_id + " due to TIMEOUT");
                    connection.play = false; // stop sending data
                    if (connection.video.udp_pair != null)
                    {
                        connection.video.udp_pair.Stop();
                        connection.video.udp_pair = null;
                    }
                    if (connection.audio.udp_pair != null)
                    {
                        connection.audio.udp_pair.Stop();
                        connection.audio.udp_pair = null;
                    }
                    connection.listener.Dispose();

                    rtsp_list.Remove(connection);
                    continue;
                }
                else if (connection.play) current_rtsp_play_count++;
            }
        }
    }


    // Feed in Raw SPS/PPS data - no 32 bit headers, no 00 00 00 01 headers
    public void FeedInRawSPSandPPS(byte[] sps_data, byte[] pps_data) // SPS data without any headers (00 00 00 01 or 32 bit lengths)
    {
        raw_sps = sps_data;
        raw_pps = pps_data;
    }

    // Feed in Raw NALs - no 32 bit headers, no 00 00 00 01 headers
    public void FeedInRawNAL(uint timestamp_ms, List<byte[]> nal_array)
    {
        DateTime now = DateTime.UtcNow;
        int current_rtsp_play_count = 0;
        int current_rtsp_count = 0;

        checkTimeouts(out current_rtsp_count, out current_rtsp_play_count);

        Console.WriteLine(current_rtsp_count + " RTSP clients connected. " + current_rtsp_play_count + " RTSP clients in PLAY mode");

        if (current_rtsp_play_count == 0) return;



        UInt32 rtp_timestamp = timestamp_ms * 90; // 90kHz clock

        // Build a list of 1 or more RTP packets
        // The last packet will have the M bit set to '1'
        List<byte[]> rtp_packets = new List<byte[]>();

        for(int x = 0; x < nal_array.Count; x++) {

            byte[] raw_nal = nal_array[x];
            Boolean last_nal = false;
            if (x == nal_array.Count - 1) {
                last_nal = true; // last NAL in our nal_array
            }

            // The H264 Payload could be sent as one large RTP packet (assuming the receiver can handle it)
            // or as a Fragmented Data, split over several RTP packets with the same Timestamp.
            bool fragmenting = false;
            int packetMTU = 65535 -8 -20 - 16; // 65535 -8 for UDP header, -20 for IP header, -16 normal RTP header len. ** LESS RTP EXTENSIONS !!!
            if (raw_nal.Length > packetMTU) fragmenting = true;

            // INDIGO VISION DOES NOT SUPPORT FRAGMENTATION. Send as one jumbo RTP packet and let OS split over MTUs.
            // NOTE TO SELF... perhaps this was because the SDP did not have the extra packetization flag
            fragmenting = false;


            if (fragmenting == false)
            {
                // Put the whole NAL into one RTP packet.
                // Note some receivers will have maximum buffers and be unable to handle large RTP packets.
                // Also with RTP over RTSP there is a limit of 65535 bytes for the RTP packet.

                byte[] rtp_packet = new byte[12 + raw_nal.Length]; // 12 is header size when there are no CSRCs or extensions
                // Create an single RTP fragment

                // RTP Packet Header
                // 0 - Version, P, X, CC, M, PT and Sequence Number
                //32 - Timestamp. H264 uses a 90kHz clock
                //64 - SSRC
                //96 - CSRCs (optional)
                //nn - Extension ID and Length
                //nn - Extension header

                int rtp_version = 2;
                int rtp_padding = 0;
                int rtp_extension = 0;
                int rtp_csrc_count = 0;
                int rtp_marker = (last_nal == true ? 1 : 0); // set to 1 if the last NAL in the array
                int rtp_payload_type = 96;

                RTPPacketUtil.WriteHeader(rtp_packet, rtp_version, rtp_padding, rtp_extension, rtp_csrc_count, rtp_marker, rtp_payload_type);

                UInt32 empty_sequence_id = 0;
                RTPPacketUtil.WriteSequenceNumber(rtp_packet, empty_sequence_id);

                RTPPacketUtil.WriteTS(rtp_packet, rtp_timestamp);

                UInt32 empty_ssrc = 0;
                RTPPacketUtil.WriteSSRC(rtp_packet, empty_ssrc);

                // Now append the raw NAL
                System.Array.Copy(raw_nal, 0, rtp_packet, 12, raw_nal.Length);

                rtp_packets.Add(rtp_packet);
            }
            else
            {
                int data_remaining = raw_nal.Length;
                int nal_pointer = 0;
                int start_bit = 1;
                int end_bit = 0;

                // consume first byte of the raw_nal. It is used in the FU header
                byte first_byte = raw_nal[0];
                nal_pointer++;
                data_remaining--;

                while (data_remaining > 0)
                {
                    int payload_size = Math.Min(packetMTU, data_remaining);
                    if (data_remaining - payload_size == 0) end_bit = 1;

                    byte[] rtp_packet = new byte[12 + 2 + payload_size]; // 12 is header size. 2 bytes for FU-A header. Then payload

                    // RTP Packet Header
                    // 0 - Version, P, X, CC, M, PT and Sequence Number
                    //32 - Timestamp. H264 uses a 90kHz clock
                    //64 - SSRC
                    //96 - CSRCs (optional)
                    //nn - Extension ID and Length
                    //nn - Extension header

                    int rtp_version = 2;
                    int rtp_padding = 0;
                    int rtp_extension = 0;
                    int rtp_csrc_count = 0;
                    int rtp_marker = (last_nal == true ? 1 : 0); // Marker set to 1 on last packet
                    int rtp_payload_type = 96;

                    RTPPacketUtil.WriteHeader(rtp_packet, rtp_version, rtp_padding, rtp_extension, rtp_csrc_count, rtp_marker, rtp_payload_type);

                    UInt32 empty_sequence_id = 0;
                    RTPPacketUtil.WriteSequenceNumber(rtp_packet, empty_sequence_id);

                    RTPPacketUtil.WriteTS(rtp_packet, rtp_timestamp);

                    UInt32 empty_ssrc = 0;
                    RTPPacketUtil.WriteSSRC(rtp_packet, empty_ssrc);

                    // Now append the Fragmentation Header (with Start and End marker) and part of the raw_nal
                    byte f_bit = 0;
                    byte nri = (byte)((first_byte >> 5) & 0x03); // Part of the 1st byte of the Raw NAL (NAL Reference ID)
                    byte type = 28; // FU-A Fragmentation

                    rtp_packet[12] = (byte)((f_bit << 7) + (nri << 5) + type);
                    rtp_packet[13] = (byte)((start_bit << 7) + (end_bit << 6) + (0 << 5) + (first_byte & 0x1F));

                    System.Array.Copy(raw_nal, nal_pointer, rtp_packet, 14, payload_size);
                    nal_pointer = nal_pointer + payload_size;
                    data_remaining = data_remaining - payload_size;

                    rtp_packets.Add(rtp_packet);

                    start_bit = 0;
                }
            }
        }

        lock (rtsp_list)
        {

            // Go through each RTSP connection and output the NAL on the Video Session
            foreach (RTSPConnection connection in rtsp_list.ToArray()) // ToArray makes a temp copy of the list.
                                                               // This lets us delete items in the foreach
                                                               // eg when there is Write Error
            {
                // Only process Sessions in Play Mode
                if (connection.play == false) continue;

                String connection_type = "";
                if (connection.video.client_transport.LowerTransport == Rtsp.Messages.RtspTransport.LowerTransportType.TCP) connection_type = "TCP";
                if (connection.video.client_transport.LowerTransport == Rtsp.Messages.RtspTransport.LowerTransportType.UDP
                    && connection.video.client_transport.IsMulticast == false) connection_type = "UDP";
                if (connection.video.client_transport.LowerTransport == Rtsp.Messages.RtspTransport.LowerTransportType.UDP
                    && connection.video.client_transport.IsMulticast == true) connection_type = "Multicast";


                Console.WriteLine("Sending video session " + connection.session_id + " " + connection_type + " Timestamp(ms)=" + timestamp_ms + ". RTP timestamp=" + rtp_timestamp + ". Sequence="+ connection.video.sequence_number);


                if (connection.video.must_send_rtcp_packet)
                {
                    // build and send RTCP Sender Report (SR) packet
                    byte[] rtcp_sender_report = new byte[28];
                    int version = 2;
                    int paddingBit = 0;
                    int reportCount = 0; // an empty report
                    int packetType = 200; // Sender Report
                    int length = (rtcp_sender_report.Length / 4) - 1; // num 32 bit words minus 1
                    rtcp_sender_report[0] = (byte)((version << 6) + (paddingBit << 5) + reportCount);
                    rtcp_sender_report[1] = (byte)(packetType);
                    rtcp_sender_report[2] = (byte)((length >> 8) & 0xFF);
                    rtcp_sender_report[3] = (byte)((length >> 0) & 0XFF);
                    rtcp_sender_report[4] = (byte)((connection.ssrc >> 24) & 0xFF);
                    rtcp_sender_report[5] = (byte)((connection.ssrc >> 16) & 0xFF);
                    rtcp_sender_report[6] = (byte)((connection.ssrc >> 8) & 0xFF);
                    rtcp_sender_report[7] = (byte)((connection.ssrc >> 0) & 0xFF);

                    // Bytes 8, 9, 10, 11 and 12,13,14,15 are the Wall Clock
                    // Bytes 16,17,18,19 are the RTP payload timestamp

                    // NTP Most Signigicant Word is relative to 0h, 1 Jan 1900
                    // This will wrap around in 2036
                    DateTime ntp_start_time = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);

                    TimeSpan tmpTime = now - ntp_start_time;
                    double totalSeconds = tmpTime.TotalSeconds; // Seconds and fractions of a second


                    UInt32 ntp_msw_seconds = (UInt32)Math.Truncate(totalSeconds); // whole number of seconds
                    UInt32 ntp_lsw_fractions = (UInt32)((totalSeconds % 1) * UInt32.MaxValue); // fractional part, scaled between 0 and MaxInt

                    // cross check...   double ntp = ntp_msw_seconds + (ntp_lsw_fractions / UInt32.MaxValue);

                    rtcp_sender_report[8] = (byte)((ntp_msw_seconds >> 24) & 0xFF);
                    rtcp_sender_report[9] = (byte)((ntp_msw_seconds >> 16) & 0xFF);
                    rtcp_sender_report[10] = (byte)((ntp_msw_seconds >> 8) & 0xFF);
                    rtcp_sender_report[11] = (byte)((ntp_msw_seconds >> 0) & 0xFF);

                    rtcp_sender_report[12] = (byte)((ntp_lsw_fractions >> 24) & 0xFF);
                    rtcp_sender_report[13] = (byte)((ntp_lsw_fractions >> 16) & 0xFF);
                    rtcp_sender_report[14] = (byte)((ntp_lsw_fractions >> 8) & 0xFF);
                    rtcp_sender_report[15] = (byte)((ntp_lsw_fractions >> 0) & 0xFF);

                    rtcp_sender_report[16] = (byte)((rtp_timestamp >> 24) & 0xFF);
                    rtcp_sender_report[17] = (byte)((rtp_timestamp >> 16) & 0xFF);
                    rtcp_sender_report[18] = (byte)((rtp_timestamp >> 8) & 0xFF);
                    rtcp_sender_report[19] = (byte)((rtp_timestamp >> 0) & 0xFF);

                    rtcp_sender_report[20] = (byte)((connection.video.rtp_packet_count >> 0) & 0xFF);
                    rtcp_sender_report[21] = (byte)((connection.video.rtp_packet_count >> 0) & 0xFF);
                    rtcp_sender_report[22] = (byte)((connection.video.rtp_packet_count >> 0) & 0xFF);
                    rtcp_sender_report[23] = (byte)((connection.video.rtp_packet_count >> 0) & 0xFF);

                    rtcp_sender_report[24] = (byte)((connection.video.octet_count >> 0) & 0xFF);
                    rtcp_sender_report[25] = (byte)((connection.video.octet_count >> 0) & 0xFF);
                    rtcp_sender_report[26] = (byte)((connection.video.octet_count >> 0) & 0xFF);
                    rtcp_sender_report[27] = (byte)((connection.video.octet_count >> 0) & 0xFF);

                    // Bytes 28 and onwards. Would contain Reception Report messages if the size in he header was non zero



                    // Send RTCP over RTSP (Interleaved)
                    if (connection.video.transport_reply.LowerTransport == Rtsp.Messages.RtspTransport.LowerTransportType.TCP)
                    {
                        int video_rtcp_channel = connection.video.transport_reply.Interleaved.Second; // second is for RTCP status messages)
                        try
                        {
                            connection.listener.SendData(video_rtcp_channel, rtcp_sender_report);
                        }
                        catch
                        {
                            Console.WriteLine("Error writing RTCP Sender Report to listener " + connection.listener.RemoteAdress);
                        }
                    }

                    // Send RTCP over UDP
                    if (connection.video.transport_reply.LowerTransport == Rtsp.Messages.RtspTransport.LowerTransportType.UDP && connection.video.transport_reply.IsMulticast == false)
                    {
                        try
                        {
                            // Send to the IP address of the Client
                            // Send to the UDP Port the Client gave us in the SETUP command
                            connection.video.udp_pair.Write_To_Data_Port(rtcp_sender_report, connection.client_hostname, connection.video.client_transport.ClientPort.Second);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("UDP Write Exception " + e.ToString());
                            Console.WriteLine("Error writing RTCP to listener " + connection.listener.RemoteAdress);
                        }
                    }

                    if (connection.video.transport_reply.LowerTransport == Rtsp.Messages.RtspTransport.LowerTransportType.UDP && connection.video.transport_reply.IsMulticast == true)
                    {
                        // TODO. Add Multicast
                    }


                    // Clear the flag. A timer may set this to True again at some point to send regular Sender Reports
                    //HACK  connection.must_send_rtcp_packet = false; // A Timer may set this to true again later in case it is used as a Keepalive (eg IndigoVision)
                }


                // There could be more than 1 RTP packet (if the data is fragmented)
                Boolean write_error = false;
                foreach (byte[] rtp_packet in rtp_packets)
                {
                    // Add the specific data for each transmission
                    RTPPacketUtil.WriteSequenceNumber(rtp_packet, connection.video.sequence_number);
                    connection.video.sequence_number++;

                    // Add the specific SSRC for each transmission
                    RTPPacketUtil.WriteSSRC(rtp_packet, connection.ssrc);


                    // Send as RTP over RTSP (Interleaved)
                    if (connection.video.transport_reply.LowerTransport == Rtsp.Messages.RtspTransport.LowerTransportType.TCP)
                    {
                        int video_channel = connection.video.transport_reply.Interleaved.First; // second is for RTCP status messages)
                        object state = new object();
                        try
                        {
                            // send the whole NAL. With RTP over RTSP we do not need to Fragment the NAL (as we do with UDP packets or Multicast)
                            //session.listener.BeginSendData(video_channel, rtp_packet, new AsyncCallback(session.listener.EndSendData), state);
                            connection.listener.SendData(video_channel, rtp_packet);
                        }
                        catch
                        {
                            Console.WriteLine("Error writing to listener " + connection.listener.RemoteAdress);
                            write_error = true;
                            break; // exit out of foreach loop
                        }
                    }

                    // Send as RTP over UDP
                    if (connection.video.transport_reply.LowerTransport == Rtsp.Messages.RtspTransport.LowerTransportType.UDP && connection.video.transport_reply.IsMulticast == false)
                    {
                        try
                        {
                            // send the whole NAL. ** We could fragment the RTP packet into smaller chuncks that fit within the MTU
                            // Send to the IP address of the Client
                            // Send to the UDP Port the Client gave us in the SETUP command
                            connection.video.udp_pair.Write_To_Data_Port(rtp_packet,connection.client_hostname,connection.video.client_transport.ClientPort.First);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("UDP Write Exception " + e.ToString());
                            Console.WriteLine("Error writing to listener " + connection.listener.RemoteAdress);
                            write_error = true;
                            break; // exit out of foreach loop
                        }
                    }
                    
                    // TODO. Add Multicast
                }
                if (write_error)
                {
                    Console.WriteLine("Removing session " + connection.session_id + " due to write error");
                    connection.play = false; // stop sending data
                    if (connection.video.udp_pair != null) {
                        connection.video.udp_pair.Stop();
                        connection.video.udp_pair = null;
                    }
                    if (connection.audio.udp_pair != null) {
                        connection.audio.udp_pair.Stop();
                        connection.audio.udp_pair = null;
                    }
                    connection.listener.Dispose();
                    rtsp_list.Remove(connection); // remove the session. It is dead
                }

                connection.video.rtp_packet_count += (UInt32)rtp_packets.Count;

                for (int x = 0; x < nal_array.Count; x++)
                {
                    connection.video.octet_count += (UInt32)nal_array[x].Length; // QUESTION - Do I need to include the RTP header bytes/fragmenting bytes
                }
            }
        }
    }


    public void FeedInAudioPacket(uint timestamp_ms, byte[] audio_packet)
    {
        DateTime now = DateTime.UtcNow;
        int current_rtsp_play_count = 0;
        int current_rtsp_count = 0;

        checkTimeouts(out current_rtsp_count, out current_rtsp_play_count);

        Console.WriteLine(current_rtsp_count + " RTSP clients connected. " + current_rtsp_play_count + " RTSP clients in PLAY mode");

        if (current_rtsp_play_count == 0) return;


        Console.WriteLine(current_rtsp_count + " RTSP clients connected. " + current_rtsp_play_count + " RTSP clients in PLAY mode");

        if (current_rtsp_play_count == 0) return;



        UInt32 rtp_timestamp = timestamp_ms * 8; // 8kHz clock

        // Build a list of 1 or more RTP packets
        // The last packet will have the M bit set to '1'
        List<byte[]> rtp_packets = new List<byte[]>();

        // Put the whole Audio Packet into one RTP packet.

        byte[] rtp_packet = new byte[12 + audio_packet.Length]; // 12 is header size when there are no CSRCs or extensions
        // Create an single RTP fragment

        // RTP Packet Header
        // 0 - Version, P, X, CC, M, PT and Sequence Number
        //32 - Timestamp. H264 uses a 90kHz clock
        //64 - SSRC
        //96 - CSRCs (optional)
        //nn - Extension ID and Length
        //nn - Extension header

        int rtp_version = 2;
        int rtp_padding = 0;
        int rtp_extension = 0;
        int rtp_csrc_count = 0;
        int rtp_marker = 1; // always 1 as this is the last (and only) RTP packet for this audio timestamp
        int rtp_payload_type = audio_payload_type; // 0 for PCMU

        RTPPacketUtil.WriteHeader(rtp_packet, rtp_version, rtp_padding, rtp_extension, rtp_csrc_count, rtp_marker, rtp_payload_type);

        UInt32 empty_sequence_id = 0;
        RTPPacketUtil.WriteSequenceNumber(rtp_packet, empty_sequence_id); // placeholder to be completed later

        RTPPacketUtil.WriteTS(rtp_packet, rtp_timestamp);

        UInt32 empty_ssrc = 0;
        RTPPacketUtil.WriteSSRC(rtp_packet, empty_ssrc);

        // Now append the audio packet
        System.Array.Copy(audio_packet, 0, rtp_packet, 12, audio_packet.Length);


        // SEND THE RTSP PACKET
        lock (rtsp_list)
        {

            // Go through each RTSP connection and output the NAL on the Video Session
            foreach (RTSPConnection connection in rtsp_list.ToArray()) // ToArray makes a temp copy of the list.
                                                               // This lets us delete items in the foreach
                                                               // eg when there is Write Error
            {
                // Only process Sessions in Play Mode
                if (connection.play == false) continue;

                // The client may have only subscribed to Video. Check if the client wants audio
                if (connection.audio.client_transport == null) continue;

                String connection_type = "";
                if (connection.audio.client_transport.LowerTransport == Rtsp.Messages.RtspTransport.LowerTransportType.TCP) connection_type = "TCP";
                if (connection.audio.client_transport.LowerTransport == Rtsp.Messages.RtspTransport.LowerTransportType.UDP
                    && connection.audio.client_transport.IsMulticast == false) connection_type = "UDP";
                if (connection.audio.client_transport.LowerTransport == Rtsp.Messages.RtspTransport.LowerTransportType.UDP
                    && connection.audio.client_transport.IsMulticast == true) connection_type = "Multicast";


                Console.WriteLine("Sending audio session " + connection.session_id + " " + connection_type + " Timestamp(ms)=" + timestamp_ms + ". RTP timestamp=" + rtp_timestamp + ". Sequence="+ connection.audio.sequence_number);


                if (connection.audio.must_send_rtcp_packet)
                {
                    // build and send RTCP Sender Report (SR) packet
                    byte[] rtcp_sender_report = new byte[28];
                    int version = 2;
                    int paddingBit = 0;
                    int reportCount = 0; // an empty report
                    int packetType = 200; // Sender Report
                    int length = (rtcp_sender_report.Length / 4) - 1; // num 32 bit words minus 1
                    rtcp_sender_report[0] = (byte)((version << 6) + (paddingBit << 5) + reportCount);
                    rtcp_sender_report[1] = (byte)(packetType);
                    rtcp_sender_report[2] = (byte)((length >> 8) & 0xFF);
                    rtcp_sender_report[3] = (byte)((length >> 0) & 0XFF);
                    rtcp_sender_report[4] = (byte)((connection.ssrc >> 24) & 0xFF);
                    rtcp_sender_report[5] = (byte)((connection.ssrc >> 16) & 0xFF);
                    rtcp_sender_report[6] = (byte)((connection.ssrc >> 8) & 0xFF);
                    rtcp_sender_report[7] = (byte)((connection.ssrc >> 0) & 0xFF);

                    // Bytes 8, 9, 10, 11 and 12,13,14,15 are the Wall Clock
                    // Bytes 16,17,18,19 are the RTP payload timestamp

                    // NTP Most Signigicant Word is relative to 0h, 1 Jan 1900
                    // This will wrap around in 2036
                    DateTime ntp_start_time = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);

                    TimeSpan tmpTime = now - ntp_start_time;
                    double totalSeconds = tmpTime.TotalSeconds; // Seconds and fractions of a second


                    UInt32 ntp_msw_seconds = (UInt32)Math.Truncate(totalSeconds); // whole number of seconds
                    UInt32 ntp_lsw_fractions = (UInt32)((totalSeconds % 1) * UInt32.MaxValue); // fractional part, scaled between 0 and MaxInt

                    // cross check...   double ntp = ntp_msw_seconds + (ntp_lsw_fractions / UInt32.MaxValue);

                    rtcp_sender_report[8] = (byte)((ntp_msw_seconds >> 24) & 0xFF);
                    rtcp_sender_report[9] = (byte)((ntp_msw_seconds >> 16) & 0xFF);
                    rtcp_sender_report[10] = (byte)((ntp_msw_seconds >> 8) & 0xFF);
                    rtcp_sender_report[11] = (byte)((ntp_msw_seconds >> 0) & 0xFF);

                    rtcp_sender_report[12] = (byte)((ntp_lsw_fractions >> 24) & 0xFF);
                    rtcp_sender_report[13] = (byte)((ntp_lsw_fractions >> 16) & 0xFF);
                    rtcp_sender_report[14] = (byte)((ntp_lsw_fractions >> 8) & 0xFF);
                    rtcp_sender_report[15] = (byte)((ntp_lsw_fractions >> 0) & 0xFF);

                    rtcp_sender_report[16] = (byte)((rtp_timestamp >> 24) & 0xFF);
                    rtcp_sender_report[17] = (byte)((rtp_timestamp >> 16) & 0xFF);
                    rtcp_sender_report[18] = (byte)((rtp_timestamp >> 8) & 0xFF);
                    rtcp_sender_report[19] = (byte)((rtp_timestamp >> 0) & 0xFF);

                    rtcp_sender_report[20] = (byte)((connection.audio.rtp_packet_count >> 0) & 0xFF);
                    rtcp_sender_report[21] = (byte)((connection.audio.rtp_packet_count >> 0) & 0xFF);
                    rtcp_sender_report[22] = (byte)((connection.audio.rtp_packet_count >> 0) & 0xFF);
                    rtcp_sender_report[23] = (byte)((connection.audio.rtp_packet_count >> 0) & 0xFF);

                    rtcp_sender_report[24] = (byte)((connection.audio.octet_count >> 0) & 0xFF);
                    rtcp_sender_report[25] = (byte)((connection.audio.octet_count >> 0) & 0xFF);
                    rtcp_sender_report[26] = (byte)((connection.audio.octet_count >> 0) & 0xFF);
                    rtcp_sender_report[27] = (byte)((connection.audio.octet_count >> 0) & 0xFF);

                    // Bytes 28 and onwards. Would contain Reception Report messages if the size in he header was non zero



                    // Send RTCP over RTSP (Interleaved)
                    if (connection.audio.transport_reply.LowerTransport == Rtsp.Messages.RtspTransport.LowerTransportType.TCP)
                    {
                        int audio_rtcp_channel = connection.audio.transport_reply.Interleaved.Second; // second is for RTCP status messages)
                        try
                        {
                            connection.listener.SendData(audio_rtcp_channel, rtcp_sender_report);
                        }
                        catch
                        {
                            Console.WriteLine("Error writing RTCP Sender Report to listener " + connection.listener.RemoteAdress);
                        }
                    }

                    // Send RTCP over UDP
                    if (connection.audio.transport_reply.LowerTransport == Rtsp.Messages.RtspTransport.LowerTransportType.UDP && connection.audio.transport_reply.IsMulticast == false)
                    {
                        try
                        {
                            // Send to the IP address of the Client
                            // Send to the UDP Port the Client gave us in the SETUP command
                            connection.audio.udp_pair.Write_To_Data_Port(rtcp_sender_report, connection.client_hostname, connection.audio.client_transport.ClientPort.Second);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("UDP Write Exception " + e.ToString());
                            Console.WriteLine("Error writing RTCP to listener " + connection.listener.RemoteAdress);
                        }
                    }

                    if (connection.audio.transport_reply.LowerTransport == Rtsp.Messages.RtspTransport.LowerTransportType.UDP && connection.audio.transport_reply.IsMulticast == true)
                    {
                        // TODO. Add Multicast
                    }


                    // Clear the flag. A timer may set this to True again at some point to send regular Sender Reports
                    //HACK  connection.must_send_rtcp_packet = false; // A Timer may set this to true again later in case it is used as a Keepalive (eg IndigoVision)
                }


                // There could be more than 1 RTP packet (if the data is fragmented)
                Boolean write_error = false;
                {
                    // Add the specific data for each transmission
                    RTPPacketUtil.WriteSequenceNumber(rtp_packet, connection.audio.sequence_number);
                    connection.audio.sequence_number++;

                    // Add the specific SSRC for each transmission
                    RTPPacketUtil.WriteSSRC(rtp_packet, connection.ssrc);


                    // Send as RTP over RTSP (Interleaved)
                    if (connection.audio.transport_reply.LowerTransport == Rtsp.Messages.RtspTransport.LowerTransportType.TCP)
                    {
                        int audio_channel = connection.audio.transport_reply.Interleaved.First; // second is for RTCP status messages)
                        object state = new object();
                        try
                        {
                            // send the whole NAL. With RTP over RTSP we do not need to Fragment the NAL (as we do with UDP packets or Multicast)
                            //session.listener.BeginSendData(audio_channel, rtp_packet, new AsyncCallback(session.listener.EndSendData), state);
                            connection.listener.SendData(audio_channel, rtp_packet);
                        }
                        catch
                        {
                            Console.WriteLine("Error writing to listener " + connection.listener.RemoteAdress);
                            write_error = true;
                            break; // exit out of foreach loop
                        }
                    }

                    // Send as RTP over UDP
                    if (connection.audio.transport_reply.LowerTransport == Rtsp.Messages.RtspTransport.LowerTransportType.UDP && connection.audio.transport_reply.IsMulticast == false)
                    {
                        try
                        {
                            // send the whole RTP packet
                            // Send to the IP address of the Client
                            // Send to the UDP Port the Client gave us in the SETUP command
                            connection.audio.udp_pair.Write_To_Data_Port(rtp_packet,connection.client_hostname,connection.audio.client_transport.ClientPort.First);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("UDP Write Exception " + e.ToString());
                            Console.WriteLine("Error writing to listener " + connection.listener.RemoteAdress);
                            write_error = true;
                            break; // exit out of foreach loop
                        }
                    }
                    
                    // TODO. Add Multicast
                }
                if (write_error)
                {
                    Console.WriteLine("Removing session " + connection.session_id + " due to write error");
                    connection.play = false; // stop sending data
                    if (connection.video.udp_pair != null) {
                        connection.video.udp_pair.Stop();
                        connection.video.udp_pair = null;
                    }
                    if (connection.audio.udp_pair != null) {
                        connection.audio.udp_pair.Stop();
                        connection.audio.udp_pair = null;
                    }
                    connection.listener.Dispose();
                    rtsp_list.Remove(connection); // remove the session. It is dead
                }

                connection.audio.rtp_packet_count += (UInt32)rtp_packets.Count;

                connection.audio.octet_count += (UInt32)audio_packet.Length; // QUESTION - Do I need to include the RTP header bytes/fragmenting bytes
            }
        }
    }

    // An RTPStream can be a Video Stream, Audio Stream or a MetaData Stream
    public class RTPStream {
        public int trackID;
        public bool must_send_rtcp_packet = false; // when true will send out a RTCP packet to match Wall Clock Time to RTP Payload timestamps
        public UInt16 sequence_number = 1;         // 16 bit RTP packet sequence number used with this client connection
        public Rtsp.Messages.RtspTransport client_transport; // Transport: string from the client to the server
        public Rtsp.Messages.RtspTransport transport_reply; // Transport: reply from the server to the client
        public Rtsp.UDPSocket udp_pair = null;     // Pair of UDP sockets (data and control) used when sending via UDP
        public DateTime time_since_last_rtcp_keepalive = DateTime.UtcNow; // Time since last RTCP message received - used to spot dead UDP clients
        public UInt32 rtp_packet_count = 0;       // Used in the RTCP Sender Report to state how many RTP packets have been transmitted (for packet loss)
        public UInt32 octet_count = 0;        // number of bytes of video that have been transmitted (for average bandwidth monitoring)
    }

    public class RTSPConnection
    {
        public Rtsp.RtspListener listener = null;  // The RTSP client connection
        public bool play = false;                  // set to true when Session is in Play mode
        public DateTime time_since_last_rtsp_keepalive = DateTime.UtcNow; // Time since last RTSP message received - used to spot dead UDP clients
        public UInt32 ssrc = 0x12345678;           // SSRC value used with this client connection
        public String client_hostname = "";        // Client Hostname/IP Address
        public int videoTrackID = 0;
        public int audioTrackID = 1;

        public String session_id = "";             // RTSP Session ID used with this client connection

        public RTPStream video = new RTPStream();
        public RTPStream audio = new RTPStream();
    }
    public static class RTPPacketUtil
    {

        public static void WriteHeader(byte[] rtp_packet, int rtp_version, int rtp_padding, int rtp_extension, int rtp_csrc_count, int rtp_marker, int rtp_payload_type)
        {
            rtp_packet[0] = (byte)((rtp_version << 6) | (rtp_padding << 5) | (rtp_extension << 4) | rtp_csrc_count);
            rtp_packet[1] = (byte)((rtp_marker << 7) | (rtp_payload_type & 0x7F));
        }

        public static void WriteSequenceNumber(byte[] rtp_packet, uint empty_sequence_id)
        {
            rtp_packet[2] = ((byte)((empty_sequence_id >> 8) & 0xFF));
            rtp_packet[3] = ((byte)((empty_sequence_id >> 0) & 0xFF));
        }

        public static void WriteTS(byte[] rtp_packet, uint ts)
        {
            rtp_packet[4] = ((byte)((ts >> 24) & 0xFF));
            rtp_packet[5] = ((byte)((ts >> 16) & 0xFF));
            rtp_packet[6] = ((byte)((ts >> 8) & 0xFF));
            rtp_packet[7] = ((byte)((ts >> 0) & 0xFF));
        }

        public static void WriteSSRC(byte[] rtp_packet, uint ssrc)
        {
            rtp_packet[8] = ((byte)((ssrc >> 24) & 0xFF));
            rtp_packet[9] = ((byte)((ssrc >> 16) & 0xFF));
            rtp_packet[10] = ((byte)((ssrc >> 8) & 0xFF));
            rtp_packet[11] = ((byte)((ssrc >> 0) & 0xFF));
        }
    }
}
