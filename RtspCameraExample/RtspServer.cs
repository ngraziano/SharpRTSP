using System;
using System.Diagnostics.Contracts;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Rtsp;
using System.Text;
using System.Collections.Generic;

// RTSP Server Example by Roger Hardiman, 2016
// Re-uses some code from the Multiplexer example of SharpRTSP
//
// This example simulates a live RTSP video stream, for example a CCTV Camera
// It creates a Video Source (a test card) that creates a YUV Image
// The image is then encoded as H264 data
// The H264 data is sent to the RTSP clients

// The Tiny H264 Encoder is a 100% .NET encoder which is lossless and creates large bitstreams as
// there is no compression. It is limited to 128x96 resolution. However it makes it easy to write a quick
// demo without needing native APIs or cross compiled C libraries for H264

public class RtspServer : IDisposable
{
    const int h264_width = 192;
    const int h264_height = 128;
    const int h264_fps = 25;

    private TcpListener _RTSPServerListener;
    private ManualResetEvent _Stopping;
    private Thread _ListenTread;

    private TestCard video_source = null;
    private SimpleH264Encoder h264_encoder = null;

    List<RTPSession> rtp_list = new List<RTPSession>(); // list of RTSP Listeners, used when sending RTP over RTSP

    Random rnd = new Random();
    int session_count = 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="RTSPServer"/> class.
    /// </summary>
    /// <param name="aPortNumber">A numero port.</param>
    public RtspServer(int portNumber)
    {
        if (portNumber < System.Net.IPEndPoint.MinPort || portNumber > System.Net.IPEndPoint.MaxPort)
            throw new ArgumentOutOfRangeException("aPortNumber", portNumber, "Port number must be between System.Net.IPEndPoint.MinPort and System.Net.IPEndPoint.MaxPort");
        Contract.EndContractBlock();

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

        // Initialise the H264 encoder
        h264_encoder = new SimpleH264Encoder(h264_width, h264_height, h264_fps);

        // Start the VideoSource
        video_source = new TestCard(h264_width, h264_height, h264_fps);
        video_source.ReceivedYUVFrame += video_source_ReceivedYUVFrame;
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
                TcpClient oneClient = _RTSPServerListener.AcceptTcpClient();
                Console.WriteLine("Connection from " + oneClient.Client.RemoteEndPoint.ToString());

                var rtsp_socket = new RtspTcpTransport(oneClient);
                RtspListener newListener = new RtspListener(rtsp_socket);
                newListener.MessageReceived += RTSP_Message_Received;
                //RTSPDispatcher.Instance.AddListener(newListener);
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

            // Make the Base64 SPS and PPS
            byte[] raw_sps = h264_encoder.GetRawSPS(); // no 0x00 0x00 0x00 0x01 or 32 bit size header
            byte[] raw_pps = h264_encoder.GetRawPPS(); // no 0x00 0x00 0x00 0x01 or 32 bit size header
            String sps_str = Convert.ToBase64String(raw_sps);
            String pps_str = Convert.ToBase64String(raw_pps);

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
            sdp.Append("a=fmtp:96 profile-level-id=42A01E; sprop-parameter-sets=" + sps_str + "," + pps_str + ";\n");

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

            if (transport.LowerTransport == Rtsp.Messages.RtspTransport.LowerTransportType.TCP)
            {
                // RTP over RTSP mode}
                transport_reply.LowerTransport = Rtsp.Messages.RtspTransport.LowerTransportType.TCP;
                transport_reply.Interleaved = new Rtsp.Messages.PortCouple(transport.Interleaved.First, transport.Interleaved.Second);
            }

            if (transport.LowerTransport == Rtsp.Messages.RtspTransport.LowerTransportType.UDP
                && transport.IsMulticast == false)
            {
                // RTP over UDP mode}
                // Create a pair of UDP sockets
                // Pass the Port of the two sockets back in the reply
                transport_reply.LowerTransport = Rtsp.Messages.RtspTransport.LowerTransportType.UDP;
                transport_reply.IsMulticast = false;
                transport_reply.ClientPort = transport.ClientPort;  // FIX
                                                                    // for now until implemented
                transport_reply = null;
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

                RTPSession new_session = new RTPSession();
                new_session.listener = listener;
                new_session.sequence_number = (UInt16)rnd.Next(65535); // start with a random 16 bit sequence number
                new_session.ssrc = 1;

                // Add the transports to the Session
                new_session.client_transport = transport;
                new_session.transport_reply = transport_reply;

                lock (rtp_list)
                {
                    // Create a 'Session' and add it to the Session List
                    // ToDo - Check the Track ID. In the SDP the H264 video track is TrackID 0
                    // Place Lock() here so the Session Count and the addition to the list is locked
                    new_session.session_id = session_count.ToString();

                    // Add the new session to the Sessions List
                    rtp_list.Add(new_session);
                    session_count++;
                }


                Rtsp.Messages.RtspResponse setup_response = setupMessage.CreateResponse();
                setup_response.Headers[Rtsp.Messages.RtspHeaderNames.Transport] = transport_reply.ToString();
                setup_response.Session = new_session.session_id;
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

        // Handle PLAY message
        if (message is Rtsp.Messages.RtspRequestPlay)
        {
            lock (rtp_list)
            {
                // Search for the Session in the Sessions List. Change the state of "PLAY"
                foreach (RTPSession session in rtp_list)
                {
                    if (session.session_id.Equals(message.Session))
                    {
                        // found the session
                        session.play = true;
                        break;
                    }
                }
            }

            // ToDo - only send back the OK response if the Session in the RTSP message was found
            Rtsp.Messages.RtspResponse play_response = (e.Message as Rtsp.Messages.RtspRequestPlay).CreateResponse();
            listener.SendMessage(play_response);
        }

        // Handle PLAUSE message
        if (message is Rtsp.Messages.RtspRequestPause)
        {
            lock (rtp_list)
            {
                // Search for the Session in the Sessions List. Change the state of "PLAY" 
                foreach (RTPSession session in rtp_list)
                {
                    if (session.session_id.Equals(message.Session))
                    {
                        // found the session
                        session.play = false;
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


        // Handle TEARDOWN
        if (message is Rtsp.Messages.RtspRequestTeardown)
        {
            lock (rtp_list)
            {
                // Search for the Session in the Sessions List.
                foreach (RTPSession session in rtp_list.ToArray()) // Convert to ToArray so we can delete from the rtp_list
                {
                    if (session.session_id.Equals(message.Session))
                    {
                        // TODO - Close UDP or Multicast transport
                        // For TCP there is no transport to close
                        rtp_list.Remove(session);
                        // Close the RTSP socket
                        listener.Dispose();
                    }
                }
            }
        }


    }

    
    void video_source_ReceivedYUVFrame(uint timestamp_ms, int width, int height, byte[] yuv_data)
    {

        // Check if there are any clients. Only run the encoding if someone is connected
        // Could exand this to check if someone is connected and in PLAY mode
        int current_rtp_count = rtp_list.Count;

        if (current_rtp_count == 0) return;

        // Take the YUV image and encode it into a H264 NAL
        // This returns a NAL with no headers (no 00 00 00 01 header and no 32 bit sizes)
        Console.WriteLine("Compressing video at time(ms) " + timestamp_ms + "    " + current_rtp_count + " RTSP clients connected");
        byte[] raw_nal = h264_encoder.CompressFrame(yuv_data);

        UInt32 ts = timestamp_ms * 90; // 90kHz clock

        // The H264 Payload could be sent as one large RTP packet (assuming the receiver can handle it)
        // or as a Fragmented Data, split over several RTP packets with the same Timestamp.
        bool fragmenting = false;
        if (raw_nal.Length > 1400) fragmenting = true;

        // Build a list of 1 or more RTP packets
        List<byte[]> rtp_packets = new List<byte[]>();

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
            int rtp_marker = 1;
            int rtp_payload_type = 96;

            RTPPacketUtil.WriteHeader(rtp_packet, rtp_version, rtp_padding, rtp_extension, rtp_csrc_count, rtp_marker, rtp_payload_type);

            UInt32 empty_sequence_id = 0;
            RTPPacketUtil.WriteSequenceNumber(rtp_packet, empty_sequence_id);

            Console.WriteLine("adjusted TS at 90khz=" + ts);
            RTPPacketUtil.WriteTS(rtp_packet, ts);

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
                int payload_size = Math.Min(1400, data_remaining);
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
                int rtp_marker = (end_bit == 1 ? 1 : 0); // Marker set to 1 on last packet
                int rtp_payload_type = 96;

                RTPPacketUtil.WriteHeader(rtp_packet, rtp_version, rtp_padding, rtp_extension, rtp_csrc_count, rtp_marker, rtp_payload_type);

                UInt32 empty_sequence_id = 0;
                RTPPacketUtil.WriteSequenceNumber(rtp_packet, empty_sequence_id);

                Console.WriteLine("adjusted TS at 90khz=" + ts);
                RTPPacketUtil.WriteTS(rtp_packet, ts);

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

        lock (rtp_list)
        {

            // Go through each RTSP session and output the NAL
            foreach (RTPSession session in rtp_list.ToArray()) // ToArray makes a temp copy of the list.
                                                               // This lets us delete items in the foreach
            {
                // Only process Sessions in Play Mode
                if (session.play == false) continue;

                // There could be more than 1 RTP packet (if the data is fragmented)
                Boolean write_error = false;
                foreach (byte[] rtp_packet in rtp_packets)
                {
                    // Add the specific data for each transmission
                    RTPPacketUtil.WriteSequenceNumber(rtp_packet, session.sequence_number);
                    session.sequence_number++;

                    // Add the specific SSRC for each transmission
                    RTPPacketUtil.WriteSSRC(rtp_packet, session.ssrc);


                    // Send as RTP over RTSP
                    if (session.transport_reply.LowerTransport == Rtsp.Messages.RtspTransport.LowerTransportType.TCP)
                    {
                        int video_channel = session.transport_reply.Interleaved.First; // second is for RTCP status messages)
                        object state = new object();
                        try
                        {
                            // send the whole NAL. With RTP over RTSP we do not need to Fragment the NAL (as we do with UDP packets or Multicast)
                            //session.listener.BeginSendData(video_channel, rtp_packet, new AsyncCallback(session.listener.EndSendData), state);
                            session.listener.SendData(video_channel, rtp_packet);
                        }
                        catch
                        {
                            Console.WriteLine("Error writing to listener " + session.listener.RemoteAdress);
                            write_error = true;
                            break; // exit out of foreach loop
                        }
                    }
                    // TODO. Add UDP and Multicast
                }
                if (write_error)
                {
                    session.play = false; // stop sending data
                    session.listener.Dispose();
                    rtp_list.Remove(session); // remove the session. It is dead
                    Console.WriteLine(rtp_list.Count + " remaining sessions open");
                }
            }
        }
    }

    public class RTPSession
    {
        public Rtsp.RtspListener listener = null;  // The RTSP client connection
        public UInt16 sequence_number = 1;         // 16 bit RTP packet sequence number used with this client connection
        public String session_id = "";             // RTSP Session ID used with this client connection
        public uint ssrc = 1;                       // SSRC value used with this client connection
        public bool play = false;                  // set to true when Session is in Play mode
        public Rtsp.Messages.RtspTransport client_transport; // Transport: string from the client to the server
        public Rtsp.Messages.RtspTransport transport_reply; // Transport: reply from the server to the client

    }
}


