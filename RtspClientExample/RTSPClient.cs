using Microsoft.Extensions.Logging;
using Rtsp;
using Rtsp.Messages;
using Rtsp.Sdp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace RtspClientExample
{
    class RTSPClient
    {
        private readonly ILogger _logger;

        // Events that applications can receive
        public event Received_SPS_PPS_Delegate? ReceivedSpsPps;
        public event Received_VPS_SPS_PPS_Delegate? ReceivedVpsSpsPps;
        public event Received_NALs_Delegate? ReceivedNALs;
        public event Received_G711_Delegate? ReceivedG711;
        public event Received_AMR_Delegate? ReceivedAMR;
        public event Received_AAC_Delegate? ReceivedAAC;

        // Delegated functions (essentially the function prototype)
        public delegate void Received_SPS_PPS_Delegate(byte[] sps, byte[] pps); // H264
        public delegate void Received_VPS_SPS_PPS_Delegate(byte[] vps, byte[] sps, byte[] pps); // H265
        public delegate void Received_NALs_Delegate(List<byte[]> nal_units); // H264 or H265
        public delegate void Received_G711_Delegate(String format, List<byte[]> g711);
        public delegate void Received_AMR_Delegate(String format, List<byte[]> amr);
        public delegate void Received_AAC_Delegate(String format, List<byte[]> aac, int ObjectType, int FrequencyIndex, int ChannelConfiguration);

        public enum RTP_TRANSPORT { UDP, TCP, MULTICAST, UNKNOWN };
        public enum MEDIA_REQUEST { VIDEO_ONLY, AUDIO_ONLY, VIDEO_AND_AUDIO };
        private enum RTSP_STATUS { WaitingToConnect, Connecting, ConnectFailed, Connected };

        RtspTcpTransport? rtspSocket; // RTSP connection
        volatile RTSP_STATUS rtspSocketStatus = RTSP_STATUS.WaitingToConnect;
        // this wraps around a the RTSP tcp_socket stream
        RtspListener? rtspClient;
        RTP_TRANSPORT rtpTransport = RTP_TRANSPORT.UNKNOWN; // Mode, either RTP over UDP or RTP over TCP using the RTSP socket
        UDPSocket? videoUdpPair;       // Pair of UDP ports used in RTP over UDP mode or in MULTICAST mode
        UDPSocket? audioUdpPair;       // Pair of UDP ports used in RTP over UDP mode or in MULTICAST mode
        string url = "";                 // RTSP URL (username & password will be stripped out
        string username = "";            // Username
        string password = "";            // Password
        string hostname = "";            // RTSP Server hostname or IP address
        int port = 0;                    // RTSP Server TCP Port number
        string session = "";             // RTSP Session
        string auth_type = string.Empty;         // cached from most recent WWW-Authenticate reply
        string realm = string.Empty;             // cached from most recent WWW-Authenticate reply
        string nonce = string.Empty;             // cached from most recent WWW-Authenticate reply
        uint ssrc = 12345;
        bool clientWantsVideo = false; // Client wants to receive Video
        bool clientWantsAudio = false; // Client wants to receive Audio
        Uri? video_uri = null;            // URI used for the Video Track
        int video_payload = -1;          // Payload Type for the Video. (often 96 which is the first dynamic payload value. Bosch use 35)
        int video_data_channel = -1;     // RTP Channel Number used for the video RTP stream or the UDP port number
        int video_rtcp_channel = -1;     // RTP Channel Number used for the video RTCP status report messages OR the UDP port number
        bool h264_sps_pps_fired = false; // True if the SDP included a sprop-Parameter-Set for H264 video
        bool h265_vps_sps_pps_fired = false; // True if the SDP included a sprop-vps, sprop-sps and sprop_pps for H265 video

        Uri? audio_uri = null;            // URI used for the Audio Track
        int audio_payload = -1;          // Payload Type for the Video. (often 96 which is the first dynamic payload value)
        int audio_data_channel = -1;     // RTP Channel Number used for the audio RTP stream or the UDP port number
        int audio_rtcp_channel = -1;     // RTP Channel Number used for the audio RTCP status report messages OR the UDP port number
        string audio_codec = "";         // Codec used with Payload Types (eg "PCMA" or "AMR")

        // Used with RTSP keepalive
        bool server_supports_get_parameter = false;
        bool server_supports_set_parameter = false;
        System.Timers.Timer? keepaliveTimer = null;

        IPayloadProcessor? videoPayloadProcessor = null;
        IPayloadProcessor? audioPayloadProcessor = null;

        // setup messages still to send
        readonly Queue<RtspRequestSetup> setupMessages = new();

        // Constructor
        public RTSPClient(ILogger<RTSPClient> logger)
        {
            _logger = logger;

        }


        public void Connect(string url, RTP_TRANSPORT rtpTransport, MEDIA_REQUEST mediaRequest = MEDIA_REQUEST.VIDEO_AND_AUDIO)
        {

            RtspUtils.RegisterUri();

            _logger.LogDebug("Connecting to {url} ", url);
            this.url = url;

            // Use URI to extract username and password
            // and to make a new URL without the username and password
            try
            {
                Uri uri = new(this.url);
                hostname = uri.Host;
                port = uri.Port;

                if (uri.UserInfo.Length > 0)
                {
                    username = uri.UserInfo.Split(new char[] { ':' })[0];
                    password = uri.UserInfo.Split(new char[] { ':' })[1];
                    this.url = uri.GetComponents((UriComponents.AbsoluteUri & ~UriComponents.UserInfo),
                                                 UriFormat.UriEscaped);
                }
            }
            catch
            {
                username = string.Empty;
                password = string.Empty;
            }

            // We can ask the RTSP server for Video, Audio or both. If we don't want audio we don't need to SETUP the audio channal or receive it
            clientWantsVideo = (mediaRequest == MEDIA_REQUEST.VIDEO_ONLY || mediaRequest == MEDIA_REQUEST.VIDEO_AND_AUDIO);
            clientWantsAudio = (mediaRequest == MEDIA_REQUEST.AUDIO_ONLY || mediaRequest == MEDIA_REQUEST.VIDEO_AND_AUDIO);

            // Connect to a RTSP Server. The RTSP session is a TCP connection
            rtspSocketStatus = RTSP_STATUS.Connecting;
            try
            {
                rtspSocket = new RtspTcpTransport(hostname, port);
            }
            catch
            {
                rtspSocketStatus = RTSP_STATUS.ConnectFailed;
                _logger.LogWarning("Error - did not connect");
                return;
            }

            if (rtspSocket.Connected == false)
            {
                rtspSocketStatus = RTSP_STATUS.ConnectFailed;
                _logger.LogWarning("Error - did not connect");
                return;
            }

            rtspSocketStatus = RTSP_STATUS.Connected;

            // Connect a RTSP Listener to the RTSP Socket (or other Stream) to send RTSP messages and listen for RTSP replies
            rtspClient = new RtspListener(rtspSocket)
            {
                AutoReconnect = false
            };

            rtspClient.MessageReceived += RtspMessageReceived;
            rtspClient.DataReceived += RtpDataReceived;

            rtspClient.Start(); // start listening for messages from the server (messages fire the MessageReceived event)


            // Check the RTP Transport
            // If the RTP transport is TCP then we interleave the RTP packets in the RTSP stream
            // If the RTP transport is UDP, we initialise two UDP sockets (one for video, one for RTCP status messages)
            // If the RTP transport is MULTICAST, we have to wait for the SETUP message to get the Multicast Address from the RTSP server
            this.rtpTransport = rtpTransport;
            if (rtpTransport == RTP_TRANSPORT.UDP)
            {
                videoUdpPair = new UDPSocket(50000, 51000); // give a range of 500 pairs (1000 addresses) to try incase some address are in use
                videoUdpPair.DataReceived += RtpDataReceived;
                videoUdpPair.Start(); // start listening for data on the UDP ports
                audioUdpPair = new UDPSocket(50000, 51000); // give a range of 500 pairs (1000 addresses) to try incase some address are in use
                audioUdpPair.DataReceived += RtpDataReceived;
                audioUdpPair.Start(); // start listening for data on the UDP ports
            }
            if (rtpTransport == RTP_TRANSPORT.TCP)
            {
                // Nothing to do. Data will arrive in the RTSP Listener
            }
            if (rtpTransport == RTP_TRANSPORT.MULTICAST)
            {
                // Nothing to do. Will open Multicast UDP sockets after the SETUP command
            }


            // Send OPTIONS
            // In the Received Message handler we will send DESCRIBE, SETUP and PLAY
            RtspRequest options_message = new RtspRequestOptions
            {
                RtspUri = new Uri(this.url)
            };
            rtspClient.SendMessage(options_message);
        }

        // return true if this connection failed, or if it connected but is no longer connected.
        public bool StreamingFinished() => rtspSocketStatus switch
        {
            RTSP_STATUS.ConnectFailed => true,
            RTSP_STATUS.Connected when !(rtspSocket?.Connected ?? false) => true,
            _ => false,
        };


        public void Pause()
        {
            if (rtspClient != null)
            {
                // Send PAUSE
                RtspRequest pause_message = new RtspRequestPause
                {
                    RtspUri = new Uri(url),
                    Session = session
                };
                AddAuthorization(pause_message, username, password, auth_type, realm, nonce, url);
                rtspClient.SendMessage(pause_message);
            }
        }

        public void Play()
        {
            if (rtspClient != null)
            {
                // Send PLAY
                RtspRequest play_message = new RtspRequestPlay
                {
                    RtspUri = new Uri(url),
                    Session = session
                };
                AddAuthorization(play_message, username, password, auth_type, realm, nonce, url);
                rtspClient.SendMessage(play_message);
            }
        }


        public void Stop()
        {
            // Send TEARDOWN
            RtspRequest teardown_message = new RtspRequestTeardown
            {
                RtspUri = new Uri(url),
                Session = session
            };
            AddAuthorization(teardown_message, username, password, auth_type, realm, nonce, url);
            rtspClient?.SendMessage(teardown_message);

            // Stop the keepalive timer
            keepaliveTimer?.Stop();

            // clear up any UDP sockets
            videoUdpPair?.Stop();
            audioUdpPair?.Stop();

            // Drop the RTSP session
            rtspClient?.Stop();

        }


        // RTP packet (or RTCP packet) has been received.
        public void RtpDataReceived(object? sender, RtspChunkEventArgs e)
        {
            if (e.Message is not RtspData dataReceived || dataReceived.Data is null)
                return;
            // Check which channel the Data was received on.
            // eg the Video Channel, the Video Control Channel (RTCP)
            // the Audio Channel or the Audio Control Channel (RTCP)

            if (dataReceived.Channel == video_rtcp_channel || dataReceived.Channel == audio_rtcp_channel)
            {
                _logger.LogDebug("Received a RTCP message on channel  {channel}", dataReceived.Channel);

                // RTCP Packet
                // - Version, Padding and Receiver Report Count
                // - Packet Type
                // - Length
                // - SSRC
                // - payload

                // There can be multiple RTCP packets transmitted together. Loop ever each one

                long packetIndex = 0;
                while (packetIndex < dataReceived.Data.Length)
                {

                    int rtcp_version = (dataReceived.Data[packetIndex + 0] >> 6);
                    int rtcp_padding = (dataReceived.Data[packetIndex + 0] >> 5) & 0x01;
                    int rtcp_reception_report_count = (dataReceived.Data[packetIndex + 0] & 0x1F);
                    byte rtcp_packet_type = dataReceived.Data[packetIndex + 1]; // Values from 200 to 207
                    uint rtcp_length = (uint)(dataReceived.Data[packetIndex + 2] << 8) + (uint)(dataReceived.Data[packetIndex + 3]); // number of 32 bit words
                    uint rtcp_ssrc = (uint)(dataReceived.Data[packetIndex + 4] << 24) + (uint)(dataReceived.Data[packetIndex + 5] << 16)
                        + (uint)(dataReceived.Data[packetIndex + 6] << 8) + (uint)(dataReceived.Data[packetIndex + 7]);

                    // 200 = SR = Sender Report
                    // 201 = RR = Receiver Report
                    // 202 = SDES = Source Description
                    // 203 = Bye = Goodbye
                    // 204 = APP = Application Specific Method
                    // 207 = XR = Extended Reports

                    _logger.LogDebug("RTCP Data. PacketType={rtcp_packet_type} SSRC={ssrc}", rtcp_packet_type, rtcp_ssrc);

                    if (rtcp_packet_type == 200)
                    {
                        // We have received a Sender Report
                        // Use it to convert the RTP timestamp into the UTC time

                        UInt32 ntp_msw_seconds = (uint)(dataReceived.Data[packetIndex + 8] << 24) + (uint)(dataReceived.Data[packetIndex + 9] << 16)
                        + (uint)(dataReceived.Data[packetIndex + 10] << 8) + (uint)(dataReceived.Data[packetIndex + 11]);

                        UInt32 ntp_lsw_fractions = (uint)(dataReceived.Data[packetIndex + 12] << 24) + (uint)(dataReceived.Data[packetIndex + 13] << 16)
                        + (uint)(dataReceived.Data[packetIndex + 14] << 8) + (uint)(dataReceived.Data[packetIndex + 15]);

                        UInt32 rtp_timestamp = (uint)(dataReceived.Data[packetIndex + 16] << 24) + (uint)(dataReceived.Data[packetIndex + 17] << 16)
                        + (uint)(dataReceived.Data[packetIndex + 18] << 8) + (uint)(dataReceived.Data[packetIndex + 19]);

                        double ntp = ntp_msw_seconds + (ntp_lsw_fractions / UInt32.MaxValue);

                        // NTP Most Signigicant Word is relative to 0h, 1 Jan 1900
                        // This will wrap around in 2036
                        DateTime time = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);

                        time = time.AddSeconds((double)ntp_msw_seconds); // adds 'double' (whole&fraction)

                        _logger.LogDebug("RTCP time (UTC) for RTP timestamp {timestamp} is {time}", rtp_timestamp, time);

                        // Send a Receiver Report
                        try
                        {
                            byte[] rtcp_receiver_report = new byte[8];
                            int version = 2;
                            int paddingBit = 0;
                            int reportCount = 0; // an empty report
                            int packetType = 201; // Receiver Report
                            int length = (rtcp_receiver_report.Length / 4) - 1; // num 32 bit words minus 1
                            rtcp_receiver_report[0] = (byte)((version << 6) + (paddingBit << 5) + reportCount);
                            rtcp_receiver_report[1] = (byte)(packetType);
                            rtcp_receiver_report[2] = (byte)((length >> 8) & 0xFF);
                            rtcp_receiver_report[3] = (byte)((length >> 0) & 0XFF);
                            rtcp_receiver_report[4] = (byte)((ssrc >> 24) & 0xFF);
                            rtcp_receiver_report[5] = (byte)((ssrc >> 16) & 0xFF);
                            rtcp_receiver_report[6] = (byte)((ssrc >> 8) & 0xFF);
                            rtcp_receiver_report[7] = (byte)((ssrc >> 0) & 0xFF);

                            if (rtpTransport == RTP_TRANSPORT.TCP)
                            {
                                // Send it over via the RTSP connection
                                rtspClient?.SendData(video_rtcp_channel, rtcp_receiver_report);
                            }
                            if (rtpTransport == RTP_TRANSPORT.UDP || rtpTransport == RTP_TRANSPORT.MULTICAST)
                            {
                                // Send it via a UDP Packet
                                _logger.LogDebug("TODO - Need to implement RTCP over UDP");
                            }

                        }
                        catch
                        {
                            _logger.LogDebug("Error writing RTCP packet");
                        }
                    }

                    packetIndex += ((rtcp_length + 1) * 4);
                }
                return;
            }

            if (dataReceived.Channel == video_data_channel || dataReceived.Channel == audio_data_channel)
            {
                // Received some Video or Audio Data on the correct channel.

                // RTP Packet Header
                // 0 - Version, P, X, CC, M, PT and Sequence Number
                //32 - Timestamp
                //64 - SSRC
                //96 - CSRCs (optional)
                //nn - Extension ID and Length
                //nn - Extension header

                int rtp_version = (dataReceived.Data[0] >> 6);
                int rtp_padding = (dataReceived.Data[0] >> 5) & 0x01;
                int rtp_extension = (dataReceived.Data[0] >> 4) & 0x01;
                int rtp_csrc_count = (dataReceived.Data[0] >> 0) & 0x0F;
                int rtp_marker = (dataReceived.Data[1] >> 7) & 0x01;
                int rtp_payload_type = (dataReceived.Data[1] >> 0) & 0x7F;
                uint rtp_sequence_number = ((uint)dataReceived.Data[2] << 8) + (uint)(dataReceived.Data[3]);
                uint rtp_timestamp = ((uint)dataReceived.Data[4] << 24) + (uint)(dataReceived.Data[5] << 16) + (uint)(dataReceived.Data[6] << 8) + (uint)(dataReceived.Data[7]);
                uint rtp_ssrc = ((uint)dataReceived.Data[8] << 24) + (uint)(dataReceived.Data[9] << 16) + (uint)(dataReceived.Data[10] << 8) + (uint)(dataReceived.Data[11]);

                int rtp_payload_start = 4 // V,P,M,SEQ
                                    + 4 // time stamp
                                    + 4 // ssrc
                                    + (4 * rtp_csrc_count); // zero or more csrcs

                uint rtp_extension_id = 0;
                uint rtp_extension_size = 0;
                if (rtp_extension == 1)
                {
                    rtp_extension_id = ((uint)dataReceived.Data[rtp_payload_start + 0] << 8) + (uint)(dataReceived.Data[rtp_payload_start + 1] << 0);
                    rtp_extension_size = ((uint)dataReceived.Data[rtp_payload_start + 2] << 8) + (uint)(dataReceived.Data[rtp_payload_start + 3] << 0) * 4; // units of extension_size is 4-bytes
                    rtp_payload_start += 4 + (int)rtp_extension_size;  // extension header and extension payload
                }

                _logger.LogDebug("RTP Data"
                                   + " V=" + rtp_version
                                   + " P=" + rtp_padding
                                   + " X=" + rtp_extension
                                   + " CC=" + rtp_csrc_count
                                   + " M=" + rtp_marker
                                   + " PT=" + rtp_payload_type
                                   + " Seq=" + rtp_sequence_number
                                   + " Time (MS)=" + rtp_timestamp / 90 // convert from 90kHZ clock to ms
                                   + " SSRC=" + rtp_ssrc
                                   + " Size=" + dataReceived.Data.Length);


                // Check the payload type in the RTP packet matches the Payload Type value from the SDP
                if (dataReceived.Channel == video_data_channel && rtp_payload_type != video_payload)
                {
                    _logger.LogDebug("Ignoring this Video RTP payload");
                    return; // ignore this data
                }

                // Check the payload type in the RTP packet matches the Payload Type value from the SDP
                else if (dataReceived.Channel == audio_data_channel && rtp_payload_type != audio_payload)
                {
                    _logger.LogDebug("Ignoring this Audio RTP payload");
                    return; // ignore this data
                }
                else if (dataReceived.Channel == video_data_channel
                         && rtp_payload_type == video_payload
                         && videoPayloadProcessor is H264Payload)
                {
                    // H264 RTP Packet

                    // If rtp_marker is '1' then this is the final transmission for this packet.
                    // If rtp_marker is '0' we need to accumulate data with the same timestamp

                    // ToDo - Check Timestamp
                    // Add the RTP packet to the tempoary_rtp list until we have a complete 'Frame'

                    byte[] rtp_payload = new byte[dataReceived.Data.Length - rtp_payload_start]; // payload with RTP header removed
                    System.Array.Copy(dataReceived.Data, rtp_payload_start, rtp_payload, 0, rtp_payload.Length); // copy payload

                    List<byte[]> nal_units = videoPayloadProcessor.ProcessRTPPacket(rtp_payload, rtp_marker); // this will cache the Packets until there is a Frame

                    if (nal_units.Count == 0)
                    {
                        // we have not passed in enough RTP packets to make a Frame of video
                    }
                    else
                    {
                        // If we did not have a SPS and PPS in the SDP then search for the SPS and PPS
                        // in the NALs and fire the Received_SPS_PPS event.
                        // We assume the SPS and PPS are in the same Frame.
                        if (h264_sps_pps_fired == false)
                        {

                            // Check this frame for SPS and PPS
                            byte[]? sps = null;
                            byte[]? pps = null;
                            foreach (byte[] nal_unit in nal_units)
                            {
                                if (nal_unit.Length > 0)
                                {
                                    int nal_ref_idc = (nal_unit[0] >> 5) & 0x03;
                                    int nal_unit_type = nal_unit[0] & 0x1F;

                                    if (nal_unit_type == 7) sps = nal_unit; // SPS
                                    if (nal_unit_type == 8) pps = nal_unit; // PPS
                                }
                            }
                            if (sps != null && pps != null)
                            {
                                // Fire the Event
                                ReceivedSpsPps?.Invoke(sps, pps);
                                h264_sps_pps_fired = true;
                            }
                        }



                        // we have a frame of NAL Units. Write them to the file
                        ReceivedNALs?.Invoke(nal_units);
                    }
                }
                else if (dataReceived.Channel == video_data_channel
                         && rtp_payload_type == video_payload
                         && videoPayloadProcessor is H265Payload)
                {
                    // H265 RTP Packet

                    // If rtp_marker is '1' then this is the final transmission for this packet.
                    // If rtp_marker is '0' we need to accumulate data with the same timestamp

                    // Add the RTP packet to the tempoary_rtp list until we have a complete 'Frame'

                    byte[] rtp_payload = new byte[dataReceived.Data.Length - rtp_payload_start]; // payload with RTP header removed
                    System.Array.Copy(dataReceived.Data, rtp_payload_start, rtp_payload, 0, rtp_payload.Length); // copy payload

                    List<byte[]> nal_units = videoPayloadProcessor.ProcessRTPPacket(rtp_payload, rtp_marker); // this will cache the Packets until there is a Frame

                    if (nal_units.Count == 0)
                    {
                        // we have not passed in enough RTP packets to make a Frame of video
                    }
                    else
                    {
                        // If we did not have a VPS, SPS and PPS in the SDP then search for the VPS SPS and PPS
                        // in the NALs and fire the Received_VPS_SPS_PPS event.
                        // We assume the VPS, SPS and PPS are in the same Frame.
                        if (h265_vps_sps_pps_fired == false)
                        {

                            // Check this frame for VPS, SPS and PPS
                            byte[]? vps = null;
                            byte[]? sps = null;
                            byte[]? pps = null;
                            foreach (byte[] nal_unit in nal_units)
                            {
                                if (nal_unit.Length > 0)
                                {
                                    int nal_unit_type = (nal_unit[0] >> 1) & 0x3F;

                                    if (nal_unit_type == 32) vps = nal_unit; // VPS
                                    if (nal_unit_type == 33) sps = nal_unit; // SPS
                                    if (nal_unit_type == 34) pps = nal_unit; // PPS
                                }
                            }
                            if (vps != null && sps != null && pps != null)
                            {
                                // Fire the Event
                                ReceivedVpsSpsPps?.Invoke(vps, sps, pps);
                                h265_vps_sps_pps_fired = true;
                            }
                        }

                        // we have a frame of NAL Units. Write them to the file
                        ReceivedNALs?.Invoke(nal_units);
                    }
                }
                else if (dataReceived.Channel == audio_data_channel && audioPayloadProcessor is G711Payload)
                {
                    // G711 PCMA or G711 PCMU
                    byte[] rtp_payload = new byte[dataReceived.Data.Length - rtp_payload_start]; // payload with RTP header removed
                    Array.Copy(dataReceived.Data, rtp_payload_start, rtp_payload, 0, rtp_payload.Length); // copy payload

                    List<byte[]> audio_frames = audioPayloadProcessor.ProcessRTPPacket(rtp_payload, rtp_marker);

                    if (audio_frames == null)
                    {
                        // some error
                    }
                    else
                    {
                        // Write the audio frames to the file
                        ReceivedG711?.Invoke(audio_codec, audio_frames);
                    }
                }
                else if (dataReceived.Channel == audio_data_channel
                          && rtp_payload_type == audio_payload

                          && audioPayloadProcessor is AMRPayload)
                {
                    // AMR
                    byte[] rtp_payload = new byte[dataReceived.Data.Length - rtp_payload_start]; // payload with RTP header removed
                    System.Array.Copy(dataReceived.Data, rtp_payload_start, rtp_payload, 0, rtp_payload.Length); // copy payload

                    List<byte[]> audio_frames = audioPayloadProcessor.ProcessRTPPacket(rtp_payload, rtp_marker);

                    if (audio_frames.Count == 0)
                    {
                        // some error
                    }
                    else
                    {
                        // Write the audio frames to the file
                        ReceivedAMR?.Invoke(audio_codec, audio_frames);
                    }
                }
                else if (dataReceived.Channel == audio_data_channel
                         && rtp_payload_type == audio_payload

                        && audioPayloadProcessor is AACPayload aacPayload)
                {
                    // AAC
                    byte[] rtp_payload = new byte[dataReceived.Data.Length - rtp_payload_start]; // payload with RTP header removed
                    System.Array.Copy(dataReceived.Data, rtp_payload_start, rtp_payload, 0, rtp_payload.Length); // copy payload

                    List<byte[]> audio_frames = audioPayloadProcessor.ProcessRTPPacket(rtp_payload, rtp_marker);

                    if (audio_frames.Count == 0)
                    {
                        // some error
                    }
                    else
                    {
                        // Write the audio frames to the file
                        ReceivedAAC?.Invoke(audio_codec, audio_frames, aacPayload.ObjectType, aacPayload.FrequencyIndex, aacPayload.ChannelConfiguration);
                    }
                }
                else if (dataReceived.Channel == video_data_channel && rtp_payload_type == 26)
                {
                    _logger.LogWarning("No parser has been written for JPEG RTP packets. Please help write one");
                    return; // ignore this data
                }
                else
                {
                    _logger.LogWarning("No parser for RTP payload {rtp_payload_type}", rtp_payload_type);
                }
            }
        }


        // RTSP Messages are OPTIONS, DESCRIBE, SETUP, PLAY etc
        private void RtspMessageReceived(object? sender, RtspChunkEventArgs e)
        {
            if (e.Message is not RtspResponse message)
                return;

            _logger.LogDebug("Received RTSP Message {originalReques}", message.OriginalRequest);

            // If message has a 401 - Unauthorised Error, then we re-send the message with Authorization
            // using the most recently received 'realm' and 'nonce'
            if (message.IsOk == false)
            {
                _logger.LogDebug("Got Error in RTSP Reply " + message.ReturnCode + " " + message.ReturnMessage);

                if (message.ReturnCode == 401 && message.OriginalRequest is not null && (message.OriginalRequest.Headers.ContainsKey(RtspHeaderNames.Authorization) == true))
                {
                    // the authorization failed.
                    Stop();
                    return;
                }

                // Check if the Reply has an Authenticate header.
                if (message.ReturnCode == 401 && message.Headers.ContainsKey(RtspHeaderNames.WWWAuthenticate))
                {

                    // Process the WWW-Authenticate header
                    // EG:   Basic realm="AProxy"
                    // EG:   Digest realm="AXIS_WS_ACCC8E3A0A8F", nonce="000057c3Y810622bff50b36005eb5efeae118626a161bf", stale=FALSE
                    // EG:   Digest realm="IP Camera(21388)", nonce="534407f373af1bdff561b7b4da295354", stale="FALSE"

                    string www_authenticate = message.Headers[RtspHeaderNames.WWWAuthenticate] ?? string.Empty;
                    string auth_params = "";

                    if (www_authenticate.StartsWith("basic", StringComparison.InvariantCultureIgnoreCase))
                    {
                        auth_type = "Basic";
                        auth_params = www_authenticate.Substring(5);
                    }
                    if (www_authenticate.StartsWith("digest", StringComparison.InvariantCultureIgnoreCase))
                    {
                        auth_type = "Digest";
                        auth_params = www_authenticate.Substring(6);
                    }

                    string[] items = auth_params.Split(new char[] { ',' }); // NOTE, does not handle Commas in Quotes

                    foreach (string item in items)
                    {
                        // Split on the = symbol and update the realm and nonce
                        string[] parts = item.Trim().Split(new char[] { '=' }, 2); // max 2 parts in the results array
                        if (parts.Length >= 2 && parts[0].Trim().Equals("realm"))
                        {
                            realm = parts[1].Trim(new char[] { ' ', '\"' }); // trim space and quotes
                        }
                        else if (parts.Length >= 2 && parts[0].Trim().Equals("nonce"))
                        {
                            nonce = parts[1].Trim(new char[] { ' ', '\"' }); // trim space and quotes
                        }
                    }

                    _logger.LogDebug("WWW Authorize parsed for {auth_type} {realm} {nonce}",
                         auth_type, realm, nonce);
                }

                RtspMessage? resend_message = message.OriginalRequest?.Clone() as RtspMessage;

                if (resend_message is not null)
                {
                    AddAuthorization(resend_message, username, password, auth_type, realm, nonce, url);
                    rtspClient?.SendMessage(resend_message);
                }
                return;
            }


            // If we get a reply to OPTIONS then start the Keepalive Timer and send DESCRIBE
            if (message.OriginalRequest != null && message.OriginalRequest is RtspRequestOptions)
            {

                // Check the capabilities returned by OPTIONS
                // The Public: header contains the list of commands the RTSP server supports
                // Eg   DESCRIBE, SETUP, TEARDOWN, PLAY, PAUSE, OPTIONS, ANNOUNCE, RECORD, GET_PARAMETER]}
                if (message.Headers.ContainsKey(RtspHeaderNames.Public))
                {
                    string[] parts = message.Headers[RtspHeaderNames.Public].Split(',');
                    foreach (string part in parts)
                    {
                        if (part.Trim().ToUpper().Equals("GET_PARAMETER")) server_supports_get_parameter = true;
                        if (part.Trim().ToUpper().Equals("SET_PARAMETER")) server_supports_set_parameter = true;
                    }
                }

                if (keepaliveTimer == null)
                {
                    // Start a Timer to send an Keepalive RTSP command every 20 seconds
                    keepaliveTimer = new System.Timers.Timer();
                    keepaliveTimer.Elapsed += Timer_Elapsed;
                    keepaliveTimer.Interval = 20 * 1000;
                    keepaliveTimer.Enabled = true;

                    // Send DESCRIBE
                    RtspRequest describe_message = new RtspRequestDescribe();
                    describe_message.RtspUri = new Uri(url);
                    if (auth_type != null)
                    {
                        AddAuthorization(describe_message, username, password, auth_type, realm, nonce, url);
                    }
                    rtspClient?.SendMessage(describe_message);
                }
                else
                {
                    // If the Keepalive Timer was not null, the OPTIONS reply may have come from a Keepalive
                    // So no need to generate a DESCRIBE message
                    // do nothing
                }
            }


            // If we get a reply to DESCRIBE (which was our second command), then prosess SDP and send the SETUP
            if (message.OriginalRequest != null && message.OriginalRequest is Rtsp.Messages.RtspRequestDescribe)
            {

                // Got a reply for DESCRIBE
                if (!message.IsOk)
                {
                    _logger.LogDebug("Got Error in DESCRIBE Reply " + message.ReturnCode + " " + message.ReturnMessage);
                    return;
                }

                HandleDescribeResponse(message);
            }


            // If we get a reply to SETUP (which was our third command), then we
            // (i) check if the Interleaved Channel numbers have been modified by the camera (eg Panasonic cameras)
            // (ii) check if we have any more SETUP commands to send out (eg if we are doing SETUP for Video and Audio)
            // (iii) send a PLAY command if all the SETUP command have been sent
            if (message.OriginalRequest != null && message.OriginalRequest is Rtsp.Messages.RtspRequestSetup)
            {
                // Got Reply to SETUP
                if (message.IsOk == false)
                {
                    _logger.LogWarning("Got Error in SETUP Reply {returnCode} {returnMessage}",
                        message.ReturnCode, message.ReturnMessage);
                    return;
                }

                _logger.LogDebug("Got reply from Setup. Session is {session}", message.Session);

                session = message.Session ?? ""; // Session value used with Play, Pause, Teardown and and additional Setups
                if (keepaliveTimer!=null && message.Timeout > 0 && message.Timeout > keepaliveTimer.Interval / 1000)
                {
                    keepaliveTimer.Interval = message.Timeout * 1000 / 2;
                }

                // Check the Transport header
                if (message.Headers.ContainsKey(RtspHeaderNames.Transport)
                    && message.Headers[RtspHeaderNames.Transport] != null)
                {

                    RtspTransport transport = RtspTransport.Parse(message.Headers[RtspHeaderNames.Transport]);

                    // Check if Transport header includes Multicast
                    if (transport.IsMulticast)
                    {
                        string multicast_address = transport.Destination;
                        video_data_channel = transport.Port.First;
                        video_rtcp_channel = transport.Port.Second;

                        // Create the Pair of UDP Sockets in Multicast mode
                        videoUdpPair = new UDPSocket(multicast_address, video_data_channel, multicast_address, video_rtcp_channel);
                        videoUdpPair.DataReceived += RtpDataReceived;
                        videoUdpPair.Start();

                        // TODO - Need to set audio_udp_pair for Multicast
                    }

                    // check if the requested Interleaved channels have been modified by the camera
                    // in the SETUP Reply (Panasonic have a camera that does this)
                    if (transport.LowerTransport == RtspTransport.LowerTransportType.TCP)
                    {
                        if (message.OriginalRequest.RtspUri == video_uri)
                        {
                            video_data_channel = transport.Interleaved.First;
                            video_rtcp_channel = transport.Interleaved.Second;
                        }
                        if (message.OriginalRequest.RtspUri == audio_uri)
                        {
                            audio_data_channel = transport.Interleaved.First;
                            audio_rtcp_channel = transport.Interleaved.Second;
                        }

                    }
                }


                // Check if we have another SETUP command to send, then remote it from the list
                if (setupMessages.Count > 0)
                {
                    // send the next SETUP message, after adding in the 'session'
                    RtspRequestSetup next_setup = setupMessages.Dequeue();
                    next_setup.Session = session;
                    rtspClient?.SendMessage(next_setup);
                }

                else
                {
                    // Send PLAY
                    RtspRequest play_message = new RtspRequestPlay();
                    play_message.RtspUri = new Uri(url);
                    play_message.Session = session;
                    if (auth_type != null)
                    {
                        AddAuthorization(play_message, username, password, auth_type, realm, nonce, url);
                    }
                    rtspClient?.SendMessage(play_message);
                }
            }

            // If we get a reply to PLAY (which was our fourth command), then we should have video being received
            if (message.OriginalRequest != null && message.OriginalRequest is Rtsp.Messages.RtspRequestPlay)
            {
                // Got Reply to PLAY
                if (message.IsOk == false)
                {
                    _logger.LogDebug("Got Error in PLAY Reply " + message.ReturnCode + " " + message.ReturnMessage);
                    return;
                }

                _logger.LogDebug("Got reply from Play {command} ", message.Command);
            }

        }

        private void HandleDescribeResponse(RtspResponse message)
        {
            if (message.Data == null)
            {
                _logger.LogWarning("Invalid SDP");
                return;
            }

            // Examine the SDP
            _logger.LogDebug("Sdp:\n{sdp}", Encoding.UTF8.GetString(message.Data));

            SdpFile sdp_data;
            using (StreamReader sdp_stream = new(new MemoryStream(message.Data)))
            {
                sdp_data = SdpFile.Read(sdp_stream);
            }

            // RTP and RTCP 'channels' are used in TCP Interleaved mode (RTP over RTSP)
            // These are the channels we request. The camera confirms the channel in the SETUP Reply.
            // But, a Panasonic decides to use different channels in the reply.
            int next_free_rtp_channel = 0;
            int next_free_rtcp_channel = 1;

            // Process each 'Media' Attribute in the SDP (each sub-stream)

            foreach (Media media in sdp_data.Medias)
            {
                bool audio = (media.MediaType == Media.MediaTypes.audio);
                bool video = (media.MediaType == Media.MediaTypes.video);

                if (video && video_payload != -1) continue; // have already matched a video payload. don't match another
                if (audio && audio_payload != -1) continue; // have already matched an audio payload. don't match another

                if (audio && !clientWantsAudio) continue; // client does not want audio from the RTSP server
                if (video && !clientWantsVideo) continue; // client does not want video from the RTSP server


                string? video_codec = null;
                string? audio_codec = null;
                if (audio || video)
                {
                    // search the attributes for control, rtpmap and fmtp
                    // (fmtp only applies to video)
                    string control = "";  // the "track" or "stream id"
                    AttributFmtp? fmtp = null; // holds SPS and PPS in base64 (h264 video)
                    foreach (Attribut attrib in media.Attributs)
                    {
                        if (attrib.Key.Equals("control"))
                        {
                            string sdp_control = attrib.Value;
                            if (sdp_control.ToLower().StartsWith("rtsp://"))
                            {
                                control = sdp_control; //absolute path
                            }
                            else
                            {
                                control = url + "/" + sdp_control; // relative path
                            }
                            if (video) video_uri = new Uri(control);
                            if (audio) audio_uri = new Uri(control);
                        }
                        if (attrib.Key.Equals("fmtp"))
                        {
                            fmtp = attrib as AttributFmtp;
                        }
                        if (attrib.Key.Equals("rtpmap"))
                        {
                            AttributRtpMap rtpmap = attrib as AttributRtpMap;

                            // Check if the Codec Used (EncodingName) is one we support
                            string[] valid_video_codecs = { "H264", "H265" };

                            if (video && Array.IndexOf(valid_video_codecs, rtpmap?.EncodingName?.ToUpper()) >= 0)
                            {
                                // found a valid codec
                                video_codec = rtpmap?.EncodingName?.ToUpper();
                                video_payload = media.PayloadType;
                            }

                            // Note some are "mpeg4-generic" lower case
                            string[] valid_audio_codecs = { "PCMA", "PCMU", "AMR", "MPEG4-GENERIC" /* for aac */};
                            if (audio && Array.IndexOf(valid_audio_codecs, rtpmap?.EncodingName?.ToUpper()) >= 0)
                            {
                                audio_codec = rtpmap.EncodingName.ToUpper();
                                audio_payload = media.PayloadType;
                            }
                        }
                    }

                    if (video)
                    {
                        videoPayloadProcessor = video_codec switch
                        {
                            "H264" => new H264Payload(null),
                            "H265" => new H265Payload(false),
                            _ => null,
                        };

                        // If the rtpmap contains H264 then split the fmtp to get the sprop-parameter-sets which hold the SPS and PPS in base64
                        if (videoPayloadProcessor is H264Payload && fmtp != null)
                        {
                            var param = H264Parameters.Parse(fmtp.FormatParameter);
                            var sps_pps = param.SpropParameterSets;
                            if (sps_pps.Count >= 2)
                            {
                                byte[] sps = sps_pps[0];
                                byte[] pps = sps_pps[1];
                                ReceivedSpsPps?.Invoke(sps, pps);
                                h264_sps_pps_fired = true;
                            }
                        }



                        // If the rtpmap contains H265 then split the fmtp to get the sprop-vps, sprop-sps and sprop-pps
                        // The RFC makes the VPS, SPS and PPS OPTIONAL so they may not be present. In which we pass back NULL values
                        if (videoPayloadProcessor is H265Payload && fmtp != null)
                        {
                            var param = H265Parameters.Parse(fmtp.FormatParameter);
                            var vps_sps_pps = param.SpropParameterSets;
                            if (vps_sps_pps.Count >= 3)
                            {
                                byte[] vps = vps_sps_pps[0];
                                byte[] sps = vps_sps_pps[1];
                                byte[] pps = vps_sps_pps[2];
                                ReceivedVpsSpsPps?.Invoke(vps, sps, pps);
                                h265_vps_sps_pps_fired = true;
                            }
                        }
                    }

                    if (audio)
                    {
                        if (audio_payload < 96)
                        {
                            // fixed payload type
                            audioPayloadProcessor = audio_payload switch
                            {
                                0 => new G711Payload(),
                                8 => new G711Payload(),
                                _ => null,
                            };
                        }
                        else
                        {
                            audioPayloadProcessor = audio_codec switch
                            {
                                // Create AAC RTP Parser
                                // Example fmtp is "96 profile-level-id=1;mode=AAC-hbr;sizelength=13;indexlength=3;indexdeltalength=3;config=1490"
                                // Example fmtp is ""96 streamtype=5;profile-level-id=1;mode=AAC-hbr;sizelength=13;indexlength=3;indexdeltalength=3;config=1210"
                                "MPEG4-GENERIC" when fmtp?["mode"].ToLower() == "aac-hbr" => new AACPayload(fmtp["config"]),
                                "PCMA" => new G711Payload(),
                                "PCMU" => new G711Payload(),
                                "AMR" => new AMRPayload(),
                                _ => null,
                            };
                            this.audio_codec = audio_codec ?? string.Empty;
                        }
                    }

                    // Send the SETUP RTSP command if we have a matching Payload Decoder
                    if (video && video_payload == -1) continue;
                    if (audio && audio_payload == -1) continue;

                    RtspTransport transport = null;

                    if (rtpTransport == RTP_TRANSPORT.TCP)
                    {
                        // Server interleaves the RTP packets over the RTSP connection
                        // Example for TCP mode (RTP over RTSP)   Transport: RTP/AVP/TCP;interleaved=0-1
                        if (video)
                        {
                            video_data_channel = next_free_rtp_channel;
                            video_rtcp_channel = next_free_rtcp_channel;
                        }
                        if (audio)
                        {
                            audio_data_channel = next_free_rtp_channel;
                            audio_rtcp_channel = next_free_rtcp_channel;
                        }
                        transport = new RtspTransport()
                        {
                            LowerTransport = RtspTransport.LowerTransportType.TCP,
                            Interleaved = new PortCouple(next_free_rtp_channel, next_free_rtcp_channel), // Eg Channel 0 for RTP video data. Channel 1 for RTCP status reports
                        };

                        next_free_rtp_channel += 2;
                        next_free_rtcp_channel += 2;
                    }
                    if (rtpTransport == RTP_TRANSPORT.UDP)
                    {
                        int rtp_port = 0;
                        int rtcp_port = 0;
                        // Server sends the RTP packets to a Pair of UDP Ports (one for data, one for rtcp control messages)
                        // Example for UDP mode                   Transport: RTP/AVP;unicast;client_port=8000-8001
                        if (video)
                        {
                            video_data_channel = videoUdpPair.dataPort;     // Used in DataReceived event handler
                            video_rtcp_channel = videoUdpPair.controlPort;  // Used in DataReceived event handler
                            rtp_port = videoUdpPair.dataPort;
                            rtcp_port = videoUdpPair.controlPort;
                        }
                        if (audio)
                        {
                            audio_data_channel = audioUdpPair.dataPort;     // Used in DataReceived event handler
                            audio_rtcp_channel = audioUdpPair.controlPort;  // Used in DataReceived event handler
                            rtp_port = audioUdpPair.dataPort;
                            rtcp_port = audioUdpPair.controlPort;
                        }
                        transport = new RtspTransport()
                        {
                            LowerTransport = RtspTransport.LowerTransportType.UDP,
                            IsMulticast = false,
                            ClientPort = new PortCouple(rtp_port, rtcp_port), // a UDP Port for data (video or audio). a UDP Port for RTCP status reports
                        };
                    }
                    if (rtpTransport == RTP_TRANSPORT.MULTICAST)
                    {
                        // Server sends the RTP packets to a Pair of UDP ports (one for data, one for rtcp control messages)
                        // using Multicast Address and Ports that are in the reply to the SETUP message
                        // Example for MULTICAST mode     Transport: RTP/AVP;multicast
                        if (video)
                        {
                            video_data_channel = 0; // we get this information in the SETUP message reply
                            video_rtcp_channel = 0; // we get this information in the SETUP message reply
                        }
                        if (audio)
                        {
                            audio_data_channel = 0; // we get this information in the SETUP message reply
                            audio_rtcp_channel = 0; // we get this information in the SETUP message reply
                        }
                        transport = new RtspTransport()
                        {
                            LowerTransport = RtspTransport.LowerTransportType.UDP,
                            IsMulticast = true
                        };
                    }

                    // Generate SETUP messages
                    if (transport != null)
                    {
                        RtspRequestSetup setup_message = new();
                        setup_message.RtspUri = new Uri(control);
                        setup_message.AddTransport(transport);
                        if (auth_type != null)
                        {
                            AddAuthorization(setup_message, username, password, auth_type, realm, nonce, url);
                        }

                        // Add SETUP message to list of mesages to send
                        setupMessages.Enqueue(setup_message);
                    }
                }
            }
            // Send the FIRST SETUP message and remove it from the list of Setup Messages
            rtspClient?.SendMessage(setupMessages.Dequeue());
        }

        void Timer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            // Send Keepalive message
            // The ONVIF Standard uses SET_PARAMETER as "an optional method to keep an RTSP session alive"
            // RFC 2326 (RTSP Standard) says "GET_PARAMETER with no entity body may be used to test client or server liveness("ping")"

            // This code uses GET_PARAMETER (unless OPTIONS report it is not supported, and then it sends OPTIONS as a keepalive)

            RtspRequest keepAliveMessage =
                    server_supports_get_parameter
                    ? new RtspRequestGetParameter
                    {
                        RtspUri = new Uri(url),
                        Session = session
                    }
                    : new RtspRequestOptions
                    {
                        RtspUri = new Uri(url)
                    };


            AddAuthorization(keepAliveMessage, username, password, auth_type, realm, nonce, url);
            rtspClient?.SendMessage(keepAliveMessage);
        }

        // Generate Basic or Digest Authorization
        public static void AddAuthorization(RtspMessage message, string username, string password,
            string auth_type, string realm, string nonce, string url)
        {

            if (string.IsNullOrEmpty(username)) return;
            if (string.IsNullOrEmpty(password)) return;
            if (string.IsNullOrEmpty(realm)) return;

            if (auth_type == "Digest" && string.IsNullOrEmpty(nonce))
                return;

            if (auth_type == "Basic")
            {
                byte[] credentials = Encoding.UTF8.GetBytes(username + ":" + password);
                string credentials_base64 = Convert.ToBase64String(credentials);
                string basic_authorization = "Basic " + credentials_base64;

                message.Headers.Add(RtspHeaderNames.Authorization, basic_authorization);
            }
            else if (auth_type == "Digest")
            {

                string method = message.Method; // DESCRIBE, SETUP, PLAY etc

                MD5 md5 = MD5.Create();
                string hashA1 = CalculateMD5Hash(md5, username + ":" + realm + ":" + password);
                string hashA2 = CalculateMD5Hash(md5, method + ":" + url);
                string response = CalculateMD5Hash(md5, hashA1 + ":" + nonce + ":" + hashA2);

                const string quote = "\"";
                string digest_authorization = "Digest username=" + quote + username + quote + ", "
                    + "realm=" + quote + realm + quote + ", "
                    + "nonce=" + quote + nonce + quote + ", "
                    + "uri=" + quote + url + quote + ", "
                    + "response=" + quote + response + quote;

                message.Headers.Add(RtspHeaderNames.Authorization, digest_authorization);
            }
        }

        // MD5 (lower case)
        public static string CalculateMD5Hash(
            MD5 md5_session, string input)
        {
            byte[] inputBytes = Encoding.UTF8.GetBytes(input);
            byte[] hash = md5_session.ComputeHash(inputBytes);

            StringBuilder output = new();
            for (int i = 0; i < hash.Length; i++)
            {
                output.Append(hash[i].ToString("x2"));
            }

            return output.ToString();
        }

    }
}
