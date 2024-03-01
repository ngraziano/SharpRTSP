using Microsoft.Extensions.Logging;
using Rtsp;
using Rtsp.Messages;
using Rtsp.Rtp;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace RtspCameraExample
{
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

        private readonly TcpListener _RTSPServerListener;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger _logger;
        private CancellationTokenSource? _Stopping;
        private Thread? _ListenTread;
        private uint _nonceCounter = 0;

        const int jpegPayloadType = 26;

        private readonly List<RTSPConnection> rtspConnectionList = []; // list of RTSP Listeners

        int session_handle = 1;
        private readonly NetworkCredential credential;
        private readonly Authentication? auth;

        /// <summary>
        /// Initializes a new instance of the <see cref="RTSPServer"/> class.
        /// </summary>
        /// <param name="portNumber">A numero port.</param>
        /// <param name="username">username.</param>
        /// <param name="password">password.</param>
        public RtspServer(int portNumber, string username, string password, ILoggerFactory loggerFactory)
        {
            if (portNumber < IPEndPoint.MinPort || portNumber > IPEndPoint.MaxPort)
            {
                throw new ArgumentOutOfRangeException(nameof(portNumber), portNumber, "Port number must be between System.Net.IPEndPoint.MinPort and System.Net.IPEndPoint.MaxPort");
            }

            Contract.EndContractBlock();

            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                const string realm = "SharpRTSPServer";
                credential = new(username, password);
                // auth = new AuthenticationBasic(credential,realm);
                auth = new AuthenticationDigest(credential, realm, new Random().Next(100000000, 999999999).ToString(), string.Empty);

            }
            else
            {
                credential = new();
                auth = null;
            }

            RtspUtils.RegisterUri();
            _RTSPServerListener = new TcpListener(IPAddress.Any, portNumber);
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<RtspServer>();
        }

        /// <summary>
        /// Starts the listen.
        /// </summary>
        public void StartListen()
        {
            _RTSPServerListener.Start();

            _Stopping = new CancellationTokenSource();
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
                while (_Stopping?.IsCancellationRequested == false)
                {
                    // Wait for an incoming TCP Connection
                    TcpClient oneClient = _RTSPServerListener.AcceptTcpClient();
                    _logger.LogDebug("Connection from {remoteEndPoint}", oneClient.Client.RemoteEndPoint);

                    // Hand the incoming TCP connection over to the RTSP classes
                    var rtsp_socket = new RtspTcpTransport(oneClient);
                    RtspListener newListener = new(rtsp_socket, _loggerFactory.CreateLogger<RtspListener>());
                    newListener.MessageReceived += RTSPMessageReceived;

                    // Add the RtspListener to the RTSPConnections List
                    lock (rtspConnectionList)
                    {
                        RTSPConnection new_connection = new()
                        {
                            Listener = newListener,
                            ClientHostname = newListener.RemoteAdress.Split(':')[0],
                            ssrc = global_ssrc,
                        };
                        rtspConnectionList.Add(new_connection);
                    }

                    newListener.Start();
                }
            }
            catch (SocketException)
            {
                // _logger.Warn("Got an error listening, I have to handle the stopping which also throw an error", error);
            }
            catch (Exception)
            {
                // _logger.Error("Got an error listening...", error);
                throw;
            }
        }

        public void StopListen()
        {
            _RTSPServerListener.Stop();
            _Stopping?.Cancel();
            _ListenTread?.Join();
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
                _Stopping?.Dispose();
            }
        }

        #endregion

        // Process each RTSP message that is received
        private void RTSPMessageReceived(object? sender, RtspChunkEventArgs e)
        {
            // Cast the 'sender' and 'e' into the RTSP Listener (the Socket) and the RTSP Message
            RtspListener listener = sender as RtspListener ?? throw new ArgumentException("Invalid sender", nameof(sender));

            if (e.Message is not RtspRequest message)
            {
                _logger.LogWarning("RTSP message is not a request. Invalid dialog.");
                return;
            }

            Console.WriteLine("RTSP message received " + message);

            // Check if the RTSP Message has valid authentication (validating against username,password,realm and nonce)
            if (auth != null)
            {
                if (message.Headers.ContainsKey("Authorization"))
                {
                    // The Header contained Authorization
                    // Check the message has the correct Authorization
                    // If it does not have the correct Authorization then close the RTSP connection
                    if (!auth.IsValid(message))
                    {
                        // Send a 401 Authentication Failed reply, then close the RTSP Socket
                        RtspResponse authorization_response = message.CreateResponse();
                        authorization_response.AddHeader("WWW-Authenticate: " + auth.GetServerResponse());
                        authorization_response.ReturnCode = 401;
                        listener.SendMessage(authorization_response);

                        lock (rtspConnectionList)
                        {
                            rtspConnectionList.RemoveAll(c => c.Listener == listener);
                        }
                        listener.Dispose();
                        return;
                    }
                }
                else
                {
                    // Send a 401 Authentication Failed with extra info in WWW-Authenticate
                    // to tell the Client if we are using Basic or Digest Authentication
                    RtspResponse authorization_response = message.CreateResponse();
                    authorization_response.AddHeader("WWW-Authenticate: " + auth.GetServerResponse());
                    authorization_response.ReturnCode = 401;
                    listener.SendMessage(authorization_response);
                    return;
                }
            }

            // Update the RTSP Keepalive Timeout
            lock (rtspConnectionList)
            {
                foreach (var connection in rtspConnectionList.Where(connection => connection.Listener.RemoteAdress == listener.RemoteAdress))
                {
                    // found the connection
                    connection.UpdateKeepAlive();
                    break;
                }
            }

            // Handle OPTIONS message
            if (message is RtspRequestOptions)
            {
                // Create the reponse to OPTIONS
                RtspResponse options_response = message.CreateResponse();
                listener.SendMessage(options_response);
            }

            // Handle DESCRIBE message
            if (message is RtspRequestDescribe describeMEssage)
            {
                Console.WriteLine("Request for " + message.RtspUri);

                // TODO. Check the requsted_url is valid. In this example we accept any RTSP URL
                StringBuilder sdp = new();
                // Generate the SDP
                sdp.Append("v=0\n");
                sdp.Append("o=user 123 0 IN IP4 0.0.0.0\n");
                sdp.Append("s=SharpRTSP Test Camera MJPEG\n");
                sdp.Append($"m=video 0 RTP/AVP {jpegPayloadType}\n");
                sdp.Append("c=IN IP4 0.0.0.0\n");
                // sdp.Append("a=control:trackID=0\n");

                byte[] sdp_bytes = Encoding.ASCII.GetBytes(sdp.ToString());

                // Create the reponse to DESCRIBE
                // This must include the Session Description Protocol (SDP)
                RtspResponse describe_response = message.CreateResponse();

                describe_response.AddHeader("Content-Base: " + message.RtspUri);
                describe_response.AddHeader("Content-Type: application/sdp");
                describe_response.Data = sdp_bytes;
                describe_response.AdjustContentLength();
                listener.SendMessage(describe_response);
            }

            // Handle SETUP message
            if (message is RtspRequestSetup setupMessage)
            {
                // Check the RTSP transport
                // If it is UDP or Multicast, create the sockets
                // If it is RTP over RTSP we send data via the RTSP Listener

                // FIXME client may send more than one possible transport.
                // very rare
                RtspTransport transport = setupMessage.GetTransports()[0];

                // Construct the Transport: reply from the Server to the client
                RtspTransport? transport_reply = null;

                if (transport.LowerTransport == RtspTransport.LowerTransportType.TCP)
                {
                    Debug.Assert(transport.Interleaved != null, "If transport.Interleaved is null here the program did not handle well connection problem");

                    // RTP over RTSP mode
                    transport_reply = new()
                    {
                        SSrc = global_ssrc.ToString("X8"), // Convert to Hex, padded to 8 characters
                        LowerTransport = RtspTransport.LowerTransportType.TCP,
                        Interleaved = new PortCouple(transport.Interleaved.First, transport.Interleaved.Second)
                    };
                }

                UDPSocket? udp_pair = null;
                if (transport.LowerTransport == RtspTransport.LowerTransportType.UDP && !transport.IsMulticast)
                {
                    Debug.Assert(transport.ClientPort != null, "If transport.ClientPort is null here the program did not handle well connection problem");

                    // RTP over UDP mode
                    // Create a pair of UDP sockets - One is for the Data (eg Video/Audio), one is for the RTCP
                    udp_pair = new UDPSocket(50000, 51000); // give a range of 500 pairs (1000 addresses) to try incase some address are in use
                    udp_pair.SetDataDestination(listener.RemoteAdress.Split(":")[0], transport.ClientPort.First);
                    udp_pair.SetControlDestination(listener.RemoteAdress.Split(":")[0], transport.ClientPort.Second);
                    udp_pair.DataReceived += (local_sender, local_e) =>
                    {
                        // RTCP data received
                        Console.WriteLine($"RTCP data received {local_sender} {local_e}");
                        // TODO - Find the Connection and update the keepalive
                    };
                    udp_pair.Start(); // start listening for data on the UDP ports

                    // Pass the Port of the two sockets back in the reply
                    transport_reply = new()
                    {
                        SSrc = global_ssrc.ToString("X8"), // Convert to Hex, padded to 8 characters
                        LowerTransport = RtspTransport.LowerTransportType.UDP,
                        IsMulticast = false,
                        ServerPort = new PortCouple(udp_pair.DataPort, udp_pair.ControlPort),
                        ClientPort = transport.ClientPort
                    };

                }

                if (transport.LowerTransport == RtspTransport.LowerTransportType.UDP && transport.IsMulticast)
                {
                    // RTP over Multicast UDP mode}
                    // Create a pair of UDP sockets in Multicast Mode
                    // Pass the Ports of the two sockets back in the reply
                    transport_reply = new()
                    {
                        SSrc = global_ssrc.ToString("X8"), // Convert to Hex, padded to 8 characters
                        LowerTransport = RtspTransport.LowerTransportType.UDP,
                        IsMulticast = true,
                        Port = new PortCouple(7000, 7001)  // FIX
                    };

                    // for now until implemented
                    transport_reply = null;
                }

                if (transport_reply != null)
                {
                    // Update the stream within the session with transport information
                    // If a Session ID is passed in we should match SessionID with other SessionIDs but we can match on RemoteAddress
                    string copy_of_session_id = "";
                    lock (rtspConnectionList)
                    {
                        foreach (var connection in rtspConnectionList.Where(connection => connection.Listener.RemoteAdress == listener.RemoteAdress))
                        {
                            // Check the Track ID to determine if this is a SETUP for the Video Stream
                            // or a SETUP for an Audio Stream.
                            RTPStream stream = connection.video;
                            stream.client_transport = transport;
                            stream.transport_reply = transport_reply;
                            // If we are sending in UDP mode, add the UDP Socket pair and the Client Hostname
                            stream.udp_pair = udp_pair;
                            // When there is Video and Audio there are two SETUP commands.
                            // For the first SETUP command we will generate the connection.session_id and return a SessionID in the Reply.
                            // For the 2nd command the client will send is the SessionID.
                            if (string.IsNullOrEmpty(connection.session_id))
                            {
                                connection.session_id = session_handle.ToString();
                                session_handle++;
                            }
                            // ELSE, could check the Session passed in matches the Session we generated on last SETUP command
                            // Copy the Session ID, as we use it in the reply
                            copy_of_session_id = connection.session_id;
                            break;
                        }
                    }

                    RtspResponse setup_response = setupMessage.CreateResponse();
                    setup_response.Headers[RtspHeaderNames.Transport] = transport_reply.ToString();
                    setup_response.Session = copy_of_session_id;
                    listener.SendMessage(setup_response);
                }
                else
                {
                    RtspResponse setup_response = setupMessage.CreateResponse();
                    // unsuported transport
                    setup_response.ReturnCode = 461;
                    listener.SendMessage(setup_response);
                }
            }

            // Handle PLAY message (Sent with a Session ID)
            if (message is RtspRequestPlay)
            {
                lock (rtspConnectionList)
                {
                    // Search for the Session in the Sessions List. Change the state to "PLAY"
                    bool session_found = false;
                    foreach (RTSPConnection connection in rtspConnectionList)
                    {
                        if (message.Session == connection.session_id)
                        {
                            // found the session
                            session_found = true;

                            const string range = "npt=0-";   // Playing the 'video' from 0 seconds until the end
                            string rtp_info = "url=" + message.RtspUri + ";seq=" + connection.video.sequenceNumber; // TODO Add rtptime  +";rtptime="+session.rtp_initial_timestamp;
                                                                                                                    // Add audio too

                            //    'RTP-Info: url=rtsp://192.168.1.195:8557/h264/track1;seq=33026;rtptime=3014957579,url=rtsp://192.168.1.195:8557/h264/track2;seq=42116;rtptime=3335975101'

                            // Send the reply
                            RtspResponse play_response = message.CreateResponse();
                            play_response.AddHeader("Range: " + range);
                            play_response.AddHeader("RTP-Info: " + rtp_info);
                            listener.SendMessage(play_response);

                            connection.video.must_send_rtcp_packet = true;

                            // Allow video and audio to go to this client
                            connection.play = true;

                            break;
                        }
                    }

                    if (!session_found)
                    {
                        // Session ID was not found in the list of Sessions. Send a 454 error
                        RtspResponse play_failed_response = message.CreateResponse();
                        play_failed_response.ReturnCode = 454; // Session Not Found
                        listener.SendMessage(play_failed_response);
                    }
                }
            }

            // Handle PAUSE message (Sent with a Session ID)
            if (message is RtspRequestPause)
            {
                lock (rtspConnectionList)
                {
                    // Search for the Session in the Sessions List. Change the state of "PLAY" 
                    foreach (RTSPConnection connection in rtspConnectionList)
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
                RtspResponse pause_response = message.CreateResponse();
                listener.SendMessage(pause_response);
            }

            // Handle GET_PARAMETER message, often used as a Keep Alive
            if (message is RtspRequestGetParameter)
            {
                // Create the reponse to GET_PARAMETER
                RtspResponse getparameter_response = message.CreateResponse();
                listener.SendMessage(getparameter_response);
            }

            // Handle TEARDOWN (sent with a Session ID)
            if (message is RtspRequestTeardown)
            {
                lock (rtspConnectionList)
                {
                    // Search for the Session in the Sessions List.
                    foreach (RTSPConnection connection in rtspConnectionList.ToArray()) // Convert to ToArray so we can delete from the rtp_list
                    {
                        if (message.Session == connection.session_id)
                        {
                            // If this is UDP, close the transport
                            // For TCP there is no transport to close (as RTP packets were interleaved into the RTSP connection)
                            if (connection.video.udp_pair != null)
                            {
                                connection.video.udp_pair.Stop();
                                connection.video.udp_pair = null;
                            }
                            rtspConnectionList.Remove(connection);

                            // Close the RTSP socket
                            listener.Dispose();
                        }
                    }
                }
            }
        }

        public void CheckTimeouts(out int current_rtsp_count, out int current_rtsp_play_count)
        {
            DateTime now = DateTime.UtcNow;
            int timeout_in_seconds = 70;  // must have a RTSP message every 70 seconds or we will close the connection

            lock (rtspConnectionList)
            {
                current_rtsp_count = rtspConnectionList.Count;
                current_rtsp_play_count = 0;
                foreach (RTSPConnection connection in rtspConnectionList.ToArray())
                {
                    // Convert to Array to allow us to delete from rtsp_list
                    // RTSP Timeout (clients receiving RTP video over the RTSP session
                    // do not need to send a keepalive (so we check for Socket write errors)
                    bool sending_rtp_via_tcp = connection.video.client_transport?.LowerTransport == RtspTransport.LowerTransportType.TCP;

                    if (!sending_rtp_via_tcp && (now - connection.TimeSinceLastRtspKeepalive).TotalSeconds > timeout_in_seconds)
                    {
                        _logger.LogDebug("Removing session {sessionId} due to TIMEOUT", connection.session_id);
                        connection.play = false; // stop sending data
                        if (connection.video.udp_pair != null)
                        {
                            connection.video.udp_pair.Stop();
                            connection.video.udp_pair = null;
                        }
                        connection.Listener.Dispose();
                        rtspConnectionList.Remove(connection);
                    }
                    else if (connection.play)
                    {
                        current_rtsp_play_count++;
                    }
                }
            }
        }


        public void FeedInRawJPEG(uint timestamp_ms, ReadOnlySpan<byte> jpegImage, int width, int height)
        {

            DateTime now = DateTime.UtcNow;
            CheckTimeouts(out int current_rtsp_count, out int current_rtsp_play_count);
            if (current_rtsp_play_count == 0) return;

            int begin = 0;
            begin += 2; // skip the 0xFF 0xD8 marker
            int nbQuantizationTables = 0;
            ReadOnlySpan<byte> firstQuantizationtable = [];
            ReadOnlySpan<byte> secondQuantizationtable = [];
            // skip the APPx marker
            while (true)
            {
                var header = BinaryPrimitives.ReadUInt16BigEndian(jpegImage[begin..]);
                var size = BinaryPrimitives.ReadUInt16BigEndian(jpegImage[(begin + 2)..]);
                if (header >= 0xFFE0 && header <= 0xFFEF)
                {
                    begin += size + 2;
                }
                else if (header == 0xFFDB)
                {
                    nbQuantizationTables++;
                    if (nbQuantizationTables == 1)
                    {
                        firstQuantizationtable = jpegImage[(begin + 4)..(begin + size)];
                    }
                    else if (nbQuantizationTables == 2)
                    {
                        secondQuantizationtable = jpegImage[(begin + 4)..(begin + size)];
                    }
                    else
                    {
                        Console.WriteLine("Error: More than 2 quantization tables in JPEG image");
                        return;
                    }
                    begin += size + 2;
                }
                else
                {
                    break;
                }
            }

            byte type = 1;
            byte q = 255;

            var data = jpegImage[begin..];




            uint rtp_timestamp = timestamp_ms * 90; // 90kHz clock


            // Build a list of 1 or more RTP packets
            // The last packet will have the M bit set to '1'
            List<Memory<byte>> rtp_packets = [];
            List<IMemoryOwner<byte>> memoryOwners = [];

            bool endOfFrame = false;


            // -8 for UDP header, -20 for IP header, -16 normal RTP header len. ** LESS RTP EXTENSIONS !!!
            int packetMTU = 1400; // 65535; 


            int dataRemaining = data.Length;
            int dataPointer = 0;

            while (dataRemaining > 0)
            {
                bool shouldSendQuantizationTables = dataPointer == 0 && q > 127;

                int payloadSize = Math.Min(packetMTU, dataRemaining);
                if (dataRemaining == payloadSize) endOfFrame = true;

                // 12 is header size. then jpeg header, then payload
                var destSize = 12 + 8 + payloadSize;
                var owner = MemoryPool<byte>.Shared.Rent(destSize);
                memoryOwners.Add(owner);
                var rtp_packet = owner.Memory[..destSize];

                // RTP Packet Header
                // 0 - Version, P, X, CC, M, PT and Sequence Number
                //32 - Timestamp. H264 uses a 90kHz clock
                //64 - SSRC
                //96 - CSRCs (optional)
                //nn - Extension ID and Length
                //nn - Extension header

                const bool rtpPadding = false;
                const bool rtpHasExtension = false;
                const int rtp_csrc_count = 0;

                RTPPacketUtil.WriteHeader(rtp_packet.Span, RTPPacketUtil.RTP_VERSION,
                    rtpPadding, rtpHasExtension, rtp_csrc_count, endOfFrame, jpegPayloadType);

                // sequence number and SSRC are set just before send
                RTPPacketUtil.WriteTS(rtp_packet.Span, rtp_timestamp);

                int currentDestination = 12;
                BinaryPrimitives.WriteInt32BigEndian(rtp_packet.Span[currentDestination..], dataPointer & 0x00FFFFFF);
                currentDestination += 4;
                rtp_packet.Span[currentDestination++] = type;
                rtp_packet.Span[currentDestination++] = q;
                rtp_packet.Span[currentDestination++] = (byte)(width / 8);
                rtp_packet.Span[currentDestination++] = (byte)(height / 8);

                // write quantization tables
                var savedCurrentDestination = currentDestination;
                if (shouldSendQuantizationTables && nbQuantizationTables == 1)
                {
                    rtp_packet.Span[currentDestination++] = (byte)(firstQuantizationtable[0] >> 0 & 0xf);
                    rtp_packet.Span[currentDestination++] = (byte)(firstQuantizationtable[0] >> 4 & 0xf);
                    BinaryPrimitives.WriteInt16BigEndian(rtp_packet.Span[currentDestination..], (short)(firstQuantizationtable.Length - 1));
                    currentDestination += 2;
                    firstQuantizationtable[1..].CopyTo(rtp_packet.Span[currentDestination..]);
                    currentDestination += firstQuantizationtable.Length - 1;

                }
                if (shouldSendQuantizationTables && nbQuantizationTables == 2)
                {
                    rtp_packet.Span[currentDestination++] = 0;
                    rtp_packet.Span[currentDestination++] = (byte)(firstQuantizationtable[0] >> 4 & 0xf);
                    BinaryPrimitives.WriteInt16BigEndian(rtp_packet.Span[currentDestination..],
                        (short)(firstQuantizationtable.Length - 1 + secondQuantizationtable.Length - 1));
                    currentDestination += 2;
                    firstQuantizationtable[1..].CopyTo(rtp_packet.Span[currentDestination..]);
                    currentDestination += firstQuantizationtable.Length - 1;
                    secondQuantizationtable[1..].CopyTo(rtp_packet.Span[currentDestination..]);
                    currentDestination += secondQuantizationtable.Length - 1;
                }
                var quantizationTablesSizeWritten = currentDestination - savedCurrentDestination;
                payloadSize -= quantizationTablesSizeWritten;

                data[dataPointer..(dataPointer + payloadSize)].CopyTo(rtp_packet[currentDestination..].Span);
                dataPointer += payloadSize;
                dataRemaining -= payloadSize;

                rtp_packets.Add(rtp_packet);
            }

            lock (rtspConnectionList)
            {
                // Go through each RTSP connection and output the NAL on the Video Session
                foreach (RTSPConnection connection in rtspConnectionList.ToArray()) // ToArray makes a temp copy of the list.
                                                                                    // This lets us delete items in the foreach
                                                                                    // eg when there is Write Error
                {
                    // Only process Sessions in Play Mode
                    if (!connection.play) continue;

                    if (connection.video.client_transport is null) continue;

                    Console.WriteLine("Sending video session " + connection.session_id + " " + TransportLogName(connection.video.client_transport) + " Timestamp(ms)=" + timestamp_ms + ". RTP timestamp=" + rtp_timestamp + ". Sequence=" + connection.video.sequenceNumber);

                    if (connection.video.must_send_rtcp_packet)
                    {

                        // build and send RTCP Sender Report (SR) packet
                        using var rtcp_owner = MemoryPool<byte>.Shared.Rent(28);
                        var rtcpSenderReport = rtcp_owner.Memory[..28].Span;
                        const bool hasPadding = false;
                        const int reportCount = 0; // an empty report
                        int length = (rtcpSenderReport.Length / 4) - 1; // num 32 bit words minus 1
                        RTCPUtils.WriteRTCPHeader(rtcpSenderReport, RTCPUtils.RTCP_VERSION, hasPadding, reportCount,
                            RTCPUtils.RTCP_PACKET_TYPE_SENDER_REPORT, length, connection.ssrc);
                        RTCPUtils.WriteSenderReport(rtcpSenderReport, now, rtp_timestamp, connection.video.rtp_packet_count, connection.video.octet_count);

                        Debug.Assert(connection.video.transport_reply != null, "If connection.video.transport_reply is null here the program did not handle well connection problem");

                        // Send RTCP over RTSP (Interleaved)
                        if (connection.video.transport_reply.LowerTransport == RtspTransport.LowerTransportType.TCP)
                        {
                            Debug.Assert(connection.video.transport_reply?.Interleaved != null, "If connection.video.transport_reply.Interleaved is null here the program did not handle well connection problem");

                            int video_rtcp_channel = connection.video.transport_reply.Interleaved.Second; // second is for RTCP status messages)
                            try
                            {
                                connection.Listener.SendData(video_rtcp_channel, rtcpSenderReport);
                            }
                            catch
                            {
                                Console.WriteLine("Error writing RTCP Sender Report to listener " + connection.Listener.RemoteAdress);
                            }
                        }

                        // Send RTCP over UDP
                        if (connection.video.transport_reply.LowerTransport == RtspTransport.LowerTransportType.UDP
                            && !connection.video.transport_reply.IsMulticast)
                        {
                            Debug.Assert(connection.video.client_transport?.ClientPort != null, "If connection.video.client_transport?.ClientPort is null here the program did not handle well connection problem");
                            try
                            {
                                // Send to the IP address of the Client
                                // Send to the UDP Port the Client gave us in the SETUP command
                                connection.video.udp_pair!.WriteToControlPort(rtcpSenderReport);
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine("UDP Write Exception " + e);
                                Console.WriteLine("Error writing RTCP to listener " + connection.Listener.RemoteAdress);
                            }
                        }

                        if (connection.video.transport_reply.LowerTransport == RtspTransport.LowerTransportType.UDP
                            && connection.video.transport_reply.IsMulticast)
                        {
                            // TODO. Add Multicast
                        }

                        // Clear the flag. A timer may set this to True again at some point to send regular Sender Reports
                        //HACK  connection.must_send_rtcp_packet = false; // A Timer may set this to true again later in case it is used as a Keepalive (eg IndigoVision)
                    }

                    // There could be more than 1 RTP packet (if the data is fragmented)
                    bool write_error = false;
                    foreach (var rtp_packet in rtp_packets)
                    {
                        // Add the specific data for each transmission
                        RTPPacketUtil.WriteSequenceNumber(rtp_packet.Span, connection.video.sequenceNumber);
                        connection.video.sequenceNumber++;

                        // Add the specific SSRC for each transmission
                        RTPPacketUtil.WriteSSRC(rtp_packet.Span, connection.ssrc);

                        Debug.Assert(connection.video.transport_reply != null, "If connection.video.transport_reply is null here the program did not handle well connection problem");

                        // Send as RTP over RTSP (Interleaved)
                        if (connection.video.transport_reply.LowerTransport == RtspTransport.LowerTransportType.TCP)
                        {
                            Debug.Assert(connection.video.transport_reply.Interleaved != null, "If connection.video.transport_reply.Interleaved is null here the program did not handle well connection problem");
                            int video_channel = connection.video.transport_reply.Interleaved.First; // second is for RTCP status messages)
                            try
                            {
                                // send the whole NAL. With RTP over RTSP we do not need to Fragment the NAL (as we do with UDP packets or Multicast)
                                connection.Listener.SendData(video_channel, rtp_packet.Span);
                            }
                            catch
                            {
                                Console.WriteLine("Error writing to listener " + connection.Listener.RemoteAdress);
                                write_error = true;
                                break; // exit out of foreach loop
                            }
                        }

                        // Send as RTP over UDP
                        if (connection.video.transport_reply.LowerTransport == RtspTransport.LowerTransportType.UDP && !connection.video.transport_reply.IsMulticast)
                        {
                            Debug.Assert(connection.video.client_transport?.ClientPort != null, "If connection.video.client_transport?.ClientPort is null here the program did not handle well connection problem");
                            Debug.Assert(connection.video.udp_pair != null, "If connection.video.udp_pair is null here the program did not handle well connection problem");
                            try
                            {
                                // send the whole NAL. ** We could fragment the RTP packet into smaller chuncks that fit within the MTU
                                // Send to the IP address of the Client
                                // Send to the UDP Port the Client gave us in the SETUP command
                                connection.video.udp_pair.WriteToDataPort(rtp_packet.Span);
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine("UDP Write Exception " + e);
                                Console.WriteLine("Error writing to listener " + connection.Listener.RemoteAdress);
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
                        if (connection.video.udp_pair != null)
                        {
                            connection.video.udp_pair.Stop();
                            connection.video.udp_pair = null;
                        }
                        connection.Listener.Dispose();
                        rtspConnectionList.Remove(connection); // remove the session. It is dead
                    }

                    connection.video.rtp_packet_count += (uint)rtp_packets.Count;
                    connection.video.octet_count += (uint)rtp_packets.Sum(p => (uint)p.Length);
                }
            }
            foreach (var owner in memoryOwners)
            {
                owner.Dispose();
            }
        }

        private static string TransportLogName(RtspTransport? transport)
        {
            return transport switch
            {
                { LowerTransport: RtspTransport.LowerTransportType.TCP } => "TCP",
                { LowerTransport: RtspTransport.LowerTransportType.UDP, IsMulticast: false } => "UDP",
                { LowerTransport: RtspTransport.LowerTransportType.UDP, IsMulticast: true } => "Multicast",
                _ => "",
            };
        }

        // An RTPStream can be a Video Stream, Audio Stream or a MetaData Stream
        public class RTPStream
        {
            public int trackID;
            public bool must_send_rtcp_packet = false; // when true will send out a RTCP packet to match Wall Clock Time to RTP Payload timestamps
                                                       // 16 bit RTP packet sequence number used with this client connection
            public ushort sequenceNumber = 1;
            public RtspTransport? client_transport; // Transport: string from the client to the server
            public RtspTransport? transport_reply; // Transport: reply from the server to the client
            public UDPSocket? udp_pair;     // Pair of UDP sockets (data and control) used when sending via UDP
            public DateTime time_since_last_rtcp_keepalive = DateTime.UtcNow; // Time since last RTCP message received - used to spot dead UDP clients
            public uint rtp_packet_count = 0;       // Used in the RTCP Sender Report to state how many RTP packets have been transmitted (for packet loss)
            public uint octet_count = 0;        // number of bytes of video that have been transmitted (for average bandwidth monitoring)
        }

        public class RTSPConnection
        {
            // The RTSP client connection
            public required RtspListener Listener { get; init; }
            public bool play = false;                  // set to true when Session is in Play mode
                                                       // Time since last RTSP message received - used to spot dead UDP clients
            public DateTime TimeSinceLastRtspKeepalive { get; private set; } = DateTime.UtcNow;
            public uint ssrc = 0x12345678;           // SSRC value used with this client connection
                                                     // Client Hostname/IP Address
            public required string ClientHostname { get; init; }

            public string session_id = "";             // RTSP Session ID used with this client connection

            public RTPStream video = new();

            public void UpdateKeepAlive()
            {
                TimeSinceLastRtspKeepalive = DateTime.UtcNow;
            }
        }
    }
}