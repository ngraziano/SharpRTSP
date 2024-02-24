using CommunityToolkit.HighPerformance;
using Microsoft.Extensions.Logging;
using Rtsp;
using Rtsp.Messages;
using Rtsp.Onvif;
using Rtsp.Rtp;
using Rtsp.Sdp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace RtspClientExample
{
    class RTSPClient
    {
        private readonly ILogger _logger;
        private readonly ILoggerFactory _loggerFactory;

        // Events that applications can receive
        public event EventHandler<SpsPpsEventArgs>? ReceivedSpsPps;
        public event EventHandler<VpsSpsPpsEventArgs>? ReceivedVpsSpsPps;
        public event EventHandler<SimpleDataEventArgs>? ReceivedNALs; // H264 or H265
        public event EventHandler<SimpleDataEventArgs>? ReceivedMp2t;
        public event EventHandler<SimpleDataEventArgs>? ReceivedJpeg;
        public event EventHandler<G711EventArgs>? ReceivedG711;
        public event EventHandler<AMREventArgs>? ReceivedAMR;
        public event EventHandler<AACEventArgs>? ReceivedAAC;

        public enum RTP_TRANSPORT { UDP, TCP, MULTICAST };
        public enum MEDIA_REQUEST { VIDEO_ONLY, AUDIO_ONLY, VIDEO_AND_AUDIO };
        private enum RTSP_STATUS { WaitingToConnect, Connecting, ConnectFailed, Connected };

        IRtspTransport? rtspSocket; // RTSP connection
        RTSP_STATUS rtspSocketStatus = RTSP_STATUS.WaitingToConnect;
        // this wraps around a the RTSP tcp_socket stream
        RtspListener? rtspClient;
        RTP_TRANSPORT rtpTransport = RTP_TRANSPORT.UDP; // Mode, either RTP over UDP or RTP over TCP using the RTSP socket
        UDPSocket? videoUdpPair;        // Pair of UDP ports used in RTP over UDP mode or in MULTICAST mode
        UDPSocket? audioUdpPair;        // Pair of UDP ports used in RTP over UDP mode or in MULTICAST mode
        Uri? _uri;                      // RTSP URI (username & password will be stripped out
        string session = "";             // RTSP Session
        private Authentication? _authentication;
        private NetworkCredential _credentials = new();
        readonly uint ssrc = 12345;
        bool clientWantsVideo = false; // Client wants to receive Video
        bool clientWantsAudio = false; // Client wants to receive Audio

        Uri? video_uri = null;            // URI used for the Video Track
        int video_payload = -1;          // Payload Type for the Video. (often 96 which is the first dynamic payload value. Bosch use 35)
        bool h264_sps_pps_fired = false; // True if the SDP included a sprop-Parameter-Set for H264 video
        bool h265_vps_sps_pps_fired = false; // True if the SDP included a sprop-vps, sprop-sps and sprop_pps for H265 video

        Uri? audio_uri = null;            // URI used for the Audio Track
        int audio_payload = -1;          // Payload Type for the Video. (often 96 which is the first dynamic payload value)
        string audio_codec = "";         // Codec used with Payload Types (eg "PCMA" or "AMR")

        // Used with RTSP keepalive
        bool serverSupportsGetParameter = false;
        System.Timers.Timer? keepaliveTimer = null;

        IPayloadProcessor? videoPayloadProcessor = null;
        IPayloadProcessor? audioPayloadProcessor = null;

        // setup messages still to send
        readonly Queue<RtspRequestSetup> setupMessages = new();


        /// <summary>
        /// Called when the Setup command are completed, so we can start the right Play message (with or without playback informations)
        /// </summary>
        public event EventHandler? SetupMessageCompleted;


        // Constructor
        public RTSPClient(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<RTSPClient>();
            _loggerFactory = loggerFactory;
        }


        public void Connect(string url, string username, string password, RTP_TRANSPORT rtpTransport, MEDIA_REQUEST mediaRequest = MEDIA_REQUEST.VIDEO_AND_AUDIO)
        {

            RtspUtils.RegisterUri();

            _logger.LogDebug("Connecting to {url} ", url);
            _uri = new(url);

            // Use URI to extract username and password
            // and to make a new URL without the username and password
            var hostname = _uri.Host;
            var port = _uri.Port;
            try
            {
                if (_uri.UserInfo.Length > 0)
                {
                    _credentials = new(_uri.UserInfo.Split(':')[0], _uri.UserInfo.Split(':')[1]);
                    _uri = new(_uri.GetComponents(UriComponents.AbsoluteUri & ~UriComponents.UserInfo,
                                                 UriFormat.UriEscaped));
                }
                else
                {
                    _credentials = new(username, password);
                }
            }
            catch
            {
                _credentials = new();
            }

            // We can ask the RTSP server for Video, Audio or both. If we don't want audio we don't need to SETUP the audio channal or receive it
            clientWantsVideo = (mediaRequest is MEDIA_REQUEST.VIDEO_ONLY or MEDIA_REQUEST.VIDEO_AND_AUDIO);
            clientWantsAudio = (mediaRequest is MEDIA_REQUEST.AUDIO_ONLY or MEDIA_REQUEST.VIDEO_AND_AUDIO);

            // Connect to a RTSP Server. The RTSP session is a TCP connection
            rtspSocketStatus = RTSP_STATUS.Connecting;
            try
            {
                rtspSocket = _uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.InvariantCultureIgnoreCase) ?
                    new RtspHttpTransport(_uri, _credentials) :
                    new RtspTcpTransport(_uri);
            }
            catch
            {
                rtspSocketStatus = RTSP_STATUS.ConnectFailed;
                _logger.LogWarning("Error - did not connect");
                return;
            }

            if (!rtspSocket.Connected)
            {
                rtspSocketStatus = RTSP_STATUS.ConnectFailed;
                _logger.LogWarning("Error - did not connect");
                return;
            }

            rtspSocketStatus = RTSP_STATUS.Connected;

            // Connect a RTSP Listener to the RTSP Socket (or other Stream) to send RTSP messages and listen for RTSP replies
            rtspClient = new RtspListener(rtspSocket, _loggerFactory.CreateLogger<RtspListener>())
            {
                AutoReconnect = false
            };

            rtspClient.MessageReceived += RtspMessageReceived;
            rtspClient.Start(); // start listening for messages from the server (messages fire the MessageReceived event)


            // Check the RTP Transport
            // If the RTP transport is TCP then we interleave the RTP packets in the RTSP stream
            // If the RTP transport is UDP, we initialise two UDP sockets (one for video, one for RTCP status messages)
            // If the RTP transport is MULTICAST, we have to wait for the SETUP message to get the Multicast Address from the RTSP server
            this.rtpTransport = rtpTransport;
            if (rtpTransport == RTP_TRANSPORT.UDP)
            {
                videoUdpPair = new UDPSocket(50000, 51000); // give a range of 500 pairs (1000 addresses) to try incase some address are in use
                videoUdpPair.DataReceived += VideoRtpDataReceived;
                videoUdpPair.ControlReceived += RtcpControlDataReceived;
                videoUdpPair.Start(); // start listening for data on the UDP ports

                audioUdpPair = new UDPSocket(50000, 51000); // give a range of 500 pairs (1000 addresses) to try incase some address are in use
                audioUdpPair.DataReceived += AudioRtpDataReceived;
                audioUdpPair.ControlReceived += RtcpControlDataReceived;
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
                RtspUri = _uri
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
            if (rtspSocket is null || _uri is null)
            {
                throw new InvalidOperationException("Not connected");
            }

            // Send PAUSE
            RtspRequest pause_message = new RtspRequestPause
            {
                RtspUri = _uri,
                Session = session
            };
            pause_message.AddAuthorization(_authentication, _uri, rtspSocket.NextCommandIndex());
            rtspClient?.SendMessage(pause_message);
        }

        public void Play()
        {
            if (rtspSocket is null || _uri is null)
            {
                throw new InvalidOperationException("Not connected");
            }

            // Send PLAY
            var play_message = new RtspRequestPlay
            {
                RtspUri = _uri,
                Session = session
            };
            play_message.AddAuthorization(_authentication, _uri, rtspSocket.NextCommandIndex());

            //// Need for old sony camera SNC-CS20
            play_message.Headers.Add("range", "npt=0.000-");

            rtspClient?.SendMessage(play_message);
        }

        /// <summary>
        /// Generate a Play request from required time
        /// </summary>
        /// <param name="seekTime">The playback time to start from</param>
        /// <param name="speed">Speed information (1.0 means normal speed, -1.0 backward speed), other values >1.0 and <-1.0 allow a different speed</param>
        public void Play(DateTime seekTime, double speed = 1.0)
        {
            if (rtspSocket is null || _uri is null) { throw new InvalidOperationException("Not connected"); }
            var playMessage = new RtspRequestPlay
            {
                RtspUri = _uri,
                Session = session,
            };
            playMessage.AddPlayback(seekTime, speed);
            rtspClient?.SendMessage(playMessage);
        }

        /// <summary>
        /// Generate a Play request with a time range
        /// </summary>
        /// <param name="seekTimeFrom">Starting time for playback</param>
        /// <param name="seekTimeTo">Ending time for playback</param>
        /// <param name="speed">Speed information (1.0 means normal speed, -1.0 backward speed), other values >1.0 and <-1.0 allow a different speed</param>
        /// <exception cref="InvalidOperationException"></exception>
        public void Play(DateTime seekTimeFrom, DateTime seekTimeTo, double speed = 1.0)
        {
            if (rtspSocket is null || _uri is null) { throw new InvalidOperationException("Not connected"); }
            if (seekTimeFrom > seekTimeTo) { throw new ArgumentOutOfRangeException(nameof(seekTimeFrom), "Starting seek cannot be major than ending seek."); }

            var playMessage = new RtspRequestPlay
            {
                RtspUri = _uri,
                Session = session,
            };

            playMessage.AddPlayback(seekTimeFrom, seekTimeTo, speed);
            rtspClient?.SendMessage(playMessage);
        }

        public void Stop()
        {
            if (rtspSocket is null || _uri is null)
            {
                throw new InvalidOperationException("Not connected");
            }

            // Send TEARDOWN
            RtspRequest teardown_message = new RtspRequestTeardown
            {
                RtspUri = _uri,
                Session = session
            };
            teardown_message.AddAuthorization(_authentication, _uri, rtspSocket.NextCommandIndex());
            rtspClient?.SendMessage(teardown_message);

            // Stop the keepalive timer
            keepaliveTimer?.Stop();

            // clear up any UDP sockets
            videoUdpPair?.Stop();
            audioUdpPair?.Stop();

            // Drop the RTSP session
            rtspClient?.Stop();

        }

        // A Video RTP packet has been received.
        public void VideoRtpDataReceived(object? sender, RtspDataEventArgs e)
        {
            if (e.Data.Data.IsEmpty)
                return;

            var rtpPacket = new RtpPacket(e.Data.Data.Span);


            if (rtpPacket.PayloadType != video_payload)
            {
                // Check the payload type in the RTP packet matches the Payload Type value from the SDP
                _logger.LogDebug("Ignoring this Video RTP payload");
                return; // ignore this data
            }

            if (videoPayloadProcessor is null)
            {
                _logger.LogWarning("No video Processor");
                return;
            }

            using var nal_units = videoPayloadProcessor.ProcessPacket(rtpPacket); // this will cache the Packets until there is a Frame


            if (nal_units.Any())
            {
                if (videoPayloadProcessor is H264Payload)
                {
                    // H264 RTP Packet
                    // If rtp_marker is '1' then this is the final transmission for this packet.
                    // If rtp_marker is '0' we need to accumulate data with the same timestamp
                    // ToDo - Check Timestamp
                    // Add the RTP packet to the tempoary_rtp list until we have a complete 'Frame'

                    // If we did not have a SPS and PPS in the SDP then search for the SPS and PPS
                    // in the NALs and fire the Received_SPS_PPS event.
                    // We assume the SPS and PPS are in the same Frame.
                    if (h264_sps_pps_fired == false)
                    {

                        // Check this frame for SPS and PPS
                        byte[]? sps = null;
                        byte[]? pps = null;
                        foreach (var nalUnit in nal_units.Data)
                        {
                            var nal_unit = nalUnit.Span;
                            if (nal_unit.Length > 0)
                            {
                                int nal_ref_idc = (nal_unit[0] >> 5) & 0x03;
                                int nal_unit_type = nal_unit[0] & 0x1F;

                                if (nal_unit_type == 7) sps = nal_unit.ToArray(); // SPS
                                if (nal_unit_type == 8) pps = nal_unit.ToArray(); // PPS
                            }
                        }
                        if (sps != null && pps != null)
                        {
                            // Fire the Event
                            ReceivedSpsPps?.Invoke(this, new(sps, pps));
                            h264_sps_pps_fired = true;
                        }
                    }
                    // we have a frame of NAL Units. Write them to the file
                    ReceivedNALs?.Invoke(this, new(nal_units.Data));
                }

                if (videoPayloadProcessor is H265Payload)
                {
                    // H265 RTP Packet
                    // If rtp_marker is '1' then this is the final transmission for this packet.
                    // If rtp_marker is '0' we need to accumulate data with the same timestamp
                    // Add the RTP packet to the tempoary_rtp list until we have a complete 'Frame'

                    // If we did not have a VPS, SPS and PPS in the SDP then search for the VPS SPS and PPS
                    // in the NALs and fire the Received_VPS_SPS_PPS event.
                    // We assume the VPS, SPS and PPS are in the same Frame.
                    if (h265_vps_sps_pps_fired == false)
                    {

                        // Check this frame for VPS, SPS and PPS
                        byte[]? vps = null;
                        byte[]? sps = null;
                        byte[]? pps = null;
                        foreach (var nalUnit in nal_units.Data)
                        {
                            var nal_unit = nalUnit.Span;
                            if (nal_unit.Length > 0)
                            {
                                int nal_unit_type = (nal_unit[0] >> 1) & 0x3F;

                                if (nal_unit_type == 32) vps = nal_unit.ToArray(); // VPS
                                if (nal_unit_type == 33) sps = nal_unit.ToArray(); // SPS
                                if (nal_unit_type == 34) pps = nal_unit.ToArray(); // PPS
                            }
                        }
                        if (vps != null && sps != null && pps != null)
                        {
                            // Fire the Event
                            ReceivedVpsSpsPps?.Invoke(this, new(vps, sps, pps));
                            h265_vps_sps_pps_fired = true;
                        }

                    }
                    // we have a frame of NAL Units. Write them to the file
                    ReceivedNALs?.Invoke(this, new(nal_units.Data));
                }

                if (videoPayloadProcessor is JPEGPayload)
                {
                    ReceivedJpeg?.Invoke(this, new(nal_units.Data));
                }

                if (videoPayloadProcessor is MP2TransportPayload)
                {
                    ReceivedMp2t?.Invoke(this, new(nal_units.Data));

                }
            }

            // release the memory to be used by the next data message
            e.Data.Dispose();
        }

        // RTP packet (or RTCP packet) has been received.
        public void AudioRtpDataReceived(object? sender, RtspDataEventArgs e)
        {
            if (e.Data.Data.IsEmpty)
                return;

            // Received some Audio Data on the correct channel.
            var rtpPacket = new RtpPacket(e.Data.Data.Span);

            // Check the payload type in the RTP packet matches the Payload Type value from the SDP
            if (rtpPacket.PayloadType != audio_payload)
            {
                _logger.LogDebug("Ignoring this Audio RTP payload");
                return; // ignore this data
            }

            if (audioPayloadProcessor is null)
            {
                _logger.LogWarning("No parser for RTP payload {audioPayload}", audio_payload);
                return;
            }

            var audioFrames = audioPayloadProcessor.ProcessPacket(rtpPacket);

            if (audioFrames.Any())
            {
                if (audioPayloadProcessor is G711Payload)
                {
                    // G711 PCMA or G711 PCMU
                    // Write the audio frames to the file
                    ReceivedG711?.Invoke(this, new(audio_codec, audioFrames.Data));
                }
                else if (audioPayloadProcessor is AMRPayload)
                {
                    // AMR
                    // Write the audio frames to the file
                    ReceivedAMR?.Invoke(this, new(audio_codec, audioFrames.Data));

                }
                else if (audioPayloadProcessor is AACPayload aacPayload)
                {
                    // AAC
                    // Write the audio frames to the file
                    ReceivedAAC?.Invoke(this, new(audio_codec, audioFrames.Data, aacPayload.ObjectType, aacPayload.FrequencyIndex, aacPayload.ChannelConfiguration));
                }
            }
            audioFrames.Dispose();
            e.Data.Dispose();
        }



        // RTCP packet has been received.
        public void RtcpControlDataReceived(object? sender, RtspDataEventArgs e)
        {
            if (e.Data.Data.IsEmpty)
                return;

            _logger.LogDebug("Received a RTCP message ");

            // RTCP Packet
            // - Version, Padding and Receiver Report Count
            // - Packet Type
            // - Length
            // - SSRC
            // - payload

            // There can be multiple RTCP packets transmitted together. Loop ever each one

            int packetIndex = 0;
            var span = e.Data.Data.Span;
            while (packetIndex < e.Data.Data.Length)
            {

                int rtcp_version = (span[packetIndex + 0] >> 6);
                int rtcp_padding = (span[packetIndex + 0] >> 5) & 0x01;
                int rtcp_reception_report_count = (span[packetIndex + 0] & 0x1F);
                byte rtcp_packet_type = span[packetIndex + 1]; // Values from 200 to 207
                int rtcp_length = (int)(span[packetIndex + 2] << 8) + (int)(span[packetIndex + 3]); // number of 32 bit words
                uint rtcp_ssrc = (uint)(span[packetIndex + 4] << 24) + (uint)(span[packetIndex + 5] << 16)
                    + (uint)(span[packetIndex + 6] << 8) + (uint)(span[packetIndex + 7]);

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

                    UInt32 ntp_msw_seconds = (uint)(span[packetIndex + 8] << 24) + (uint)(span[packetIndex + 9] << 16)
                    + (uint)(span[packetIndex + 10] << 8) + (uint)(span[packetIndex + 11]);

                    UInt32 ntp_lsw_fractions = (uint)(span[packetIndex + 12] << 24) + (uint)(span[packetIndex + 13] << 16)
                    + (uint)(span[packetIndex + 14] << 8) + (uint)(span[packetIndex + 15]);

                    UInt32 rtp_timestamp = (uint)(span[packetIndex + 16] << 24) + (uint)(span[packetIndex + 17] << 16)
                    + (uint)(span[packetIndex + 18] << 8) + (uint)(span[packetIndex + 19]);

                    double ntp = ntp_msw_seconds + (ntp_lsw_fractions / UInt32.MaxValue);

                    // NTP Most Signigicant Word is relative to 0h, 1 Jan 1900
                    // This will wrap around in 2036
                    var time = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);

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
                            // Todo implement
                            // rtspClient?.SendData(video_rtcp_channel.Value, rtcp_receiver_report);
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
            e.Data.Dispose();

        }


        // RTSP Messages are OPTIONS, DESCRIBE, SETUP, PLAY etc
        private void RtspMessageReceived(object? sender, RtspChunkEventArgs e)
        {
            if (e.Message is not RtspResponse message)
                return;

            _logger.LogDebug("Received RTSP response to message {originalReques}", message.OriginalRequest);

            // If message has a 401 - Unauthorised Error, then we re-send the message with Authorization
            // using the most recently received 'realm' and 'nonce'
            if (!message.IsOk)
            {
                _logger.LogDebug("Got Error in RTSP Reply {returnCode} {returnMessage}", message.ReturnCode, message.ReturnMessage);

                if (message.ReturnCode == 401
                    && message.OriginalRequest?.Headers.ContainsKey(RtspHeaderNames.Authorization) == true)
                {
                    // the authorization failed.
                    _logger.LogError("Fail to authenticate stoping here");
                    Stop();
                    return;
                }

                // Check if the Reply has an Authenticate header.
                if (message.ReturnCode == 401 && message.Headers.TryGetValue(RtspHeaderNames.WWWAuthenticate, out string? value))
                {
                    // Process the WWW-Authenticate header
                    // EG:   Basic realm="AProxy"
                    // EG:   Digest realm="AXIS_WS_ACCC8E3A0A8F", nonce="000057c3Y810622bff50b36005eb5efeae118626a161bf", stale=FALSE
                    // EG:   Digest realm="IP Camera(21388)", nonce="534407f373af1bdff561b7b4da295354", stale="FALSE"

                    string www_authenticate = value ?? string.Empty;
                    _authentication = Authentication.Create(_credentials, www_authenticate);
                    _logger.LogDebug($"WWW Authorize parsed for {_authentication}");
                }

                RtspMessage? resend_message = message.OriginalRequest?.Clone() as RtspMessage;

                if (resend_message is not null)
                {
                    resend_message.AddAuthorization(_authentication, _uri!, rtspSocket!.NextCommandIndex());
                    rtspClient?.SendMessage(resend_message);
                }
                return;
            }


            // If we get a reply to OPTIONS then start the Keepalive Timer and send DESCRIBE
            if (message.OriginalRequest is RtspRequestOptions)
            {

                // Check the capabilities returned by OPTIONS
                // The Public: header contains the list of commands the RTSP server supports
                // Eg   DESCRIBE, SETUP, TEARDOWN, PLAY, PAUSE, OPTIONS, ANNOUNCE, RECORD, GET_PARAMETER]}
                if (message.Headers.ContainsKey(RtspHeaderNames.Public))
                {
                    string[]? parts = message.Headers[RtspHeaderNames.Public]?.Split(' ');
                    if (parts != null)
                    {
                        foreach (string part in parts)
                        {
                            if (part.Trim().Equals("GET_PARAMETER", StringComparison.OrdinalIgnoreCase))
                            {
                                serverSupportsGetParameter = true;
                            }
                        }
                    }
                }

                if (keepaliveTimer == null)
                {
                    // Start a Timer to send an Keepalive RTSP command every 20 seconds
                    keepaliveTimer = new System.Timers.Timer();
                    keepaliveTimer.Elapsed += SendKeepAlive;
                    keepaliveTimer.Interval = 20 * 1000;
                    keepaliveTimer.Enabled = true;

                    // Send DESCRIBE
                    RtspRequest describe_message = new RtspRequestDescribe
                    {
                        RtspUri = _uri,
                        Headers = { { "Accept", "application/sdp" } },
                    };
                    describe_message.AddAuthorization(_authentication, _uri!, rtspSocket!.NextCommandIndex());
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
            if (message.OriginalRequest is RtspRequestDescribe)
            {
                HandleDescribeResponse(message);
            }


            // If we get a reply to SETUP (which was our third command), then we
            // (i) check if the Interleaved Channel numbers have been modified by the camera (eg Panasonic cameras)
            // (ii) check if we have any more SETUP commands to send out (eg if we are doing SETUP for Video and Audio)
            // (iii) send a PLAY command if all the SETUP command have been sent
            if (message.OriginalRequest is RtspRequestSetup)
            {
                _logger.LogDebug("Got reply from Setup. Session is {session}", message.Session);

                session = message.Session ?? ""; // Session value used with Play, Pause, Teardown and and additional Setups
                if (keepaliveTimer != null && message.Timeout > 0 && message.Timeout > keepaliveTimer.Interval / 1000)
                {
                    keepaliveTimer.Interval = message.Timeout * 1000 / 2;
                }

                // Check the Transport header
                var transportString = message.Headers[RtspHeaderNames.Transport];
                if (transportString is not null)
                {

                    RtspTransport transport = RtspTransport.Parse(transportString);

                    // Check if Transport header includes Multicast
                    if (transport.IsMulticast)
                    {
                        string? multicastAddress = transport.Destination;
                        var videoDataChannel = transport.Port?.First;
                        var videoRtcpChannel = transport.Port?.Second;

                        if (!string.IsNullOrEmpty(multicastAddress)
                            && videoDataChannel.HasValue
                            && videoRtcpChannel.HasValue)
                        {

                            // Create the Pair of UDP Sockets in Multicast mode
                            videoUdpPair = new MulticastUDPSocket(multicastAddress, videoDataChannel.Value, multicastAddress, videoRtcpChannel.Value);
                            videoUdpPair.DataReceived += VideoRtpDataReceived;
                            videoUdpPair.ControlReceived += RtcpControlDataReceived;
                            videoUdpPair.Start();

                        }
                        // TODO - Need to set audio_udp_pair for Multicast
                    }

                    // check if the requested Interleaved channels have been modified by the camera
                    // in the SETUP Reply (Panasonic have a camera that does this)
                    if (transport.LowerTransport == RtspTransport.LowerTransportType.TCP)
                    {
                        if (message.OriginalRequest.RtspUri == video_uri && rtspClient is not null)
                        {
                            var videoDataChannel = transport.Interleaved?.First;
                            var videoRtcpChannel = transport.Interleaved?.Second;
                            rtspClient.DataReceived += (object? sender, RtspChunkEventArgs e) =>
                            {
                                if (e.Message is RtspData dataMessage && !dataMessage.Data.IsEmpty)
                                {
                                    if (dataMessage.Channel == videoDataChannel)
                                    {
                                        VideoRtpDataReceived(sender, new RtspDataEventArgs(dataMessage));

                                    }
                                    else if (dataMessage.Channel == videoRtcpChannel)
                                    {
                                        RtcpControlDataReceived(sender, new RtspDataEventArgs(dataMessage));

                                    }

                                }
                            };
                        }

                        if (message.OriginalRequest.RtspUri == audio_uri && rtspClient is not null)
                        {
                            var audioDataChannel = transport.Interleaved?.First;
                            var audioRtcpChannel = transport.Interleaved?.Second;
                            rtspClient.DataReceived += (object? sender, RtspChunkEventArgs e) =>
                            {
                                if (e.Message is RtspData dataMessage && !dataMessage.Data.IsEmpty)
                                {
                                    if (dataMessage.Channel == audioDataChannel)
                                    {
                                        AudioRtpDataReceived(sender, new RtspDataEventArgs(dataMessage));
                                    }
                                    else if (dataMessage.Channel == audioRtcpChannel)
                                    {
                                        RtcpControlDataReceived(sender, new RtspDataEventArgs(dataMessage));
                                    }
                                }
                            };
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
                    // use the event for setup completed, so the main program can call the Play command with or without the playback request.
                    SetupMessageCompleted?.Invoke(this, EventArgs.Empty);

                    //// Send PLAY
                    //RtspRequest play_message = new RtspRequestPlay
                    //{
                    //    RtspUri = _uri,
                    //    Session = session
                    //};
                    //
                    //// Need for old sony camera SNC-CS20
                    //play_message.Headers.Add("range", "npt=0.000-");
                    //
                    //play_message.AddAuthorization(_authentication, _uri!, rtspSocket!.NextCommandIndex());
                    //rtspClient?.SendMessage(play_message);
                }
            }

            // If we get a reply to PLAY (which was our fourth command), then we should have video being received
            if (message.OriginalRequest is RtspRequestPlay)
            {
                _logger.LogDebug("Got reply from Play {command} ", message.Command);
            }

        }

        private void HandleDescribeResponse(RtspResponse message)
        {
            if (message.Data.IsEmpty)
            {
                _logger.LogWarning("Invalid SDP");
                return;
            }

            // Examine the SDP
            _logger.LogDebug("Sdp:\n{sdp}", Encoding.UTF8.GetString(message.Data.Span));

            SdpFile sdp_data;
            using (StreamReader sdp_stream = new(message.Data.AsStream()))
            {
                sdp_data = SdpFile.Read(sdp_stream);
            }

            // RTP and RTCP 'channels' are used in TCP Interleaved mode (RTP over RTSP)
            // These are the channels we request. The camera confirms the channel in the SETUP Reply.
            // But, a Panasonic decides to use different channels in the reply.
            int nextFreeRtpChannel = 0;

            // For old sony cameras, we need to use the control uri from the sdp
            var customControlUri = sdp_data.Attributs.FirstOrDefault(x => x.Key == "control");
            if (customControlUri is not null)
            {
                _uri = new Uri(_uri!, customControlUri.Value);
            }

            // Process each 'Media' Attribute in the SDP (each sub-stream)
            // to look for first supported video substream
            if (clientWantsVideo)
            {
                foreach (Media media in sdp_data.Medias.Where(m => m.MediaType == Media.MediaTypes.video))
                {
                    // search the attributes for control, rtpmap and fmtp
                    // holds SPS and PPS in base64 (h264 video)
                    AttributFmtp? fmtp = media.Attributs.FirstOrDefault(x => x.Key == "fmtp") as AttributFmtp;
                    AttributRtpMap? rtpmap = media.Attributs.FirstOrDefault(x => x.Key == "rtpmap") as AttributRtpMap;
                    video_uri = GetControlUri(media);

                    int fmtpPayloadNumber = -1;
                    if (fmtp != null)
                    {
                        fmtpPayloadNumber = fmtp.PayloadNumber;
                    }


                    // extract h265 donl if available...

                    bool h265HasDonl = false;

                    if ((rtpmap?.EncodingName?.ToUpper().Equals("H265") ?? false) && !string.IsNullOrEmpty(fmtp?.FormatParameter))
                    {
                        var param = H265Parameters.Parse(fmtp.FormatParameter);
                        if (param.ContainsKey("sprop-max-don-diff") && int.TryParse(param["sprop-max-don-diff"], out int donl) && donl > 0)
                        {
                            h265HasDonl = true;
                        }
                    }

                    // some cameras are really mess with the payload type.
                    // must check also the rtpmap for the corrent format to load (sending an h265 payload when giving an h264 stream [Some Bosch camera])

                    if (rtpmap != null)
                    {
                        if ((fmtpPayloadNumber > -1 && rtpmap.PayloadNumber == fmtpPayloadNumber) || fmtpPayloadNumber == -1)
                        {
                            // found a valid codec
                            videoPayloadProcessor = rtpmap?.EncodingName?.ToUpper() switch
                            {
                                "H264" => new H264Payload(null),
                                "H265" => new H265Payload(h265HasDonl, null),
                                "JPEG" => new JPEGPayload(),
                                "MP4V-ES" => new RawPayload(),
                                _ => null,
                            };
                            video_payload = media.PayloadType;
                        }
                    }
                    else
                    {
                        video_payload = media.PayloadType;
                        if (media.PayloadType < 96)
                        {
                            // PayloadType is a static value, so we can use it to determine the codec
                            videoPayloadProcessor = media.PayloadType switch
                            {
                                26 => new JPEGPayload(),
                                33 => new MP2TransportPayload(),
                                _ => null,
                            };
                        }
                    }


                    // If the rtpmap contains H264 then split the fmtp to get the sprop-parameter-sets which hold the SPS and PPS in base64
                    if (videoPayloadProcessor is H264Payload && fmtp?.FormatParameter is not null)
                    {
                        var param = H264Parameters.Parse(fmtp.FormatParameter);
                        var sps_pps = param.SpropParameterSets;
                        if (sps_pps.Count >= 2)
                        {
                            byte[] sps = sps_pps[0];
                            byte[] pps = sps_pps[1];
                            ReceivedSpsPps?.Invoke(this, new(sps, pps));
                            h264_sps_pps_fired = true;
                        }
                    }

                    // If the rtpmap contains H265 then split the fmtp to get the sprop-vps, sprop-sps and sprop-pps
                    // The RFC makes the VPS, SPS and PPS OPTIONAL so they may not be present. In which we pass back NULL values
                    if (videoPayloadProcessor is H265Payload && fmtp?.FormatParameter is not null)
                    {
                        var param = H265Parameters.Parse(fmtp.FormatParameter);
                        var vps_sps_pps = param.SpropParameterSets;
                        if (vps_sps_pps.Count >= 3)
                        {
                            byte[] vps = vps_sps_pps[0];
                            byte[] sps = vps_sps_pps[1];
                            byte[] pps = vps_sps_pps[2];
                            ReceivedVpsSpsPps?.Invoke(this, new(vps, sps, pps));
                            h265_vps_sps_pps_fired = true;
                        }
                    }

                    // Send the SETUP RTSP command if we have a matching Payload Decoder
                    if (videoPayloadProcessor is not null)
                    {
                        RtspTransport? transport = CalculateTransport(ref nextFreeRtpChannel, videoUdpPair);

                        // Generate SETUP messages
                        if (transport != null)
                        {
                            RtspRequestSetup setup_message = new()
                            {
                                RtspUri = video_uri
                            };
                            setup_message.AddTransport(transport);
                            setup_message.AddAuthorization(_authentication, _uri!, rtspSocket!.NextCommandIndex());

                            // Add SETUP message to list of mesages to send
                            setupMessages.Enqueue(setup_message);
                        }
                        break;
                    }
                }
            }

            if (clientWantsAudio)
            {
                foreach (Media media in sdp_data.Medias.Where(m => m.MediaType == Media.MediaTypes.audio))
                {
                    // search the attributes for control, rtpmap and fmtp
                    AttributFmtp? fmtp = media.Attributs.FirstOrDefault(x => x.Key == "fmtp") as AttributFmtp;
                    AttributRtpMap? rtpmap = media.Attributs.FirstOrDefault(x => x.Key == "rtpmap") as AttributRtpMap;

                    audio_uri = GetControlUri(media);
                    audio_payload = media.PayloadType;

                    if (media.PayloadType < 96)
                    {
                        // fixed payload type
                        (audioPayloadProcessor, audio_codec) = media.PayloadType switch
                        {
                            0 => (new G711Payload(), "PCMU"),
                            8 => (new G711Payload(), "PCMA"),
                            _ => (null, ""),
                        };
                    }
                    else
                    {
                        // dynamic payload type
                        audio_codec = rtpmap?.EncodingName?.ToUpper() ?? string.Empty;
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
                    }

                    // Send the SETUP RTSP command if we have a matching Payload Decoder
                    if (audioPayloadProcessor is not null)
                    {

                        RtspTransport? transport = CalculateTransport(ref nextFreeRtpChannel, audioUdpPair);

                        // Generate SETUP messages
                        if (transport != null)
                        {
                            RtspRequestSetup setup_message = new()
                            {
                                RtspUri = audio_uri,
                            };
                            setup_message.AddTransport(transport);
                            setup_message.AddAuthorization(_authentication!, _uri!, rtspSocket!.NextCommandIndex());

                            // Add SETUP message to list of mesages to send
                            setupMessages.Enqueue(setup_message);
                        }
                    }

                }
            }

            if (setupMessages.Count == 0)
            {
                // No SETUP messages were generated
                // So we cannot continue
                throw new ApplicationException("Unable to setup media stream");
            }

            // Send the FIRST SETUP message and remove it from the list of Setup Messages
            rtspClient?.SendMessage(setupMessages.Dequeue());
        }

        private Uri? GetControlUri(Media media)
        {
            Uri? controlUri = null;
            var attrib = media.Attributs.FirstOrDefault(a => a.Key == "control");
            if (attrib is not null)
            {
                string sdp_control = attrib.Value;

                if (sdp_control.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase)
                    || sdp_control.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                {
                    // the "track" or "stream id"
                    string control = sdp_control; //absolute path
                    controlUri = new Uri(control);
                }
                else
                {
                    // relative path
                    controlUri = new Uri(_uri!, sdp_control);
                }

            }

            return controlUri;
        }

        private RtspTransport? CalculateTransport(ref int nextFreeRtpChannel, UDPSocket? udp)
        {
            return rtpTransport switch
            {
                // Server interleaves the RTP packets over the RTSP connection
                // Example for TCP mode (RTP over RTSP)   Transport: RTP/AVP/TCP;interleaved=0-1
                RTP_TRANSPORT.TCP => new RtspTransport()
                {
                    LowerTransport = RtspTransport.LowerTransportType.TCP,
                    // Eg Channel 0 for RTP video data. Channel 1 for RTCP status reports
                    Interleaved = new(nextFreeRtpChannel++, nextFreeRtpChannel++)
                },
                RTP_TRANSPORT.UDP => new RtspTransport()
                {
                    LowerTransport = RtspTransport.LowerTransportType.UDP,
                    IsMulticast = false,
                    ClientPort = udp?.Ports ?? throw new ApplicationException("UDP transport asked and no udp port allocated"),
                },
                // Server sends the RTP packets to a Pair of UDP ports (one for data, one for rtcp control messages)
                // using Multicast Address and Ports that are in the reply to the SETUP message
                // Example for MULTICAST mode     Transport: RTP/AVP;multicast
                RTP_TRANSPORT.MULTICAST => new RtspTransport()
                {
                    LowerTransport = RtspTransport.LowerTransportType.UDP,
                    IsMulticast = true
                },
                _ => null,
            };
        }

        void SendKeepAlive(object? sender, System.Timers.ElapsedEventArgs e)
        {
            // Send Keepalive message
            // The ONVIF Standard uses SET_PARAMETER as "an optional method to keep an RTSP session alive"
            // RFC 2326 (RTSP Standard) says "GET_PARAMETER with no entity body may be used to test client or server liveness("ping")"

            // This code uses GET_PARAMETER (unless OPTIONS report it is not supported, and then it sends OPTIONS as a keepalive)

            RtspRequest keepAliveMessage =
                    serverSupportsGetParameter
                    ? new RtspRequestGetParameter
                    {
                        RtspUri = _uri,
                        Session = session
                    }
                    : new RtspRequestOptions
                    {
                        // RtspUri = new Uri(url)
                    };


            keepAliveMessage.AddAuthorization(_authentication, _uri!, rtspSocket!.NextCommandIndex());
            rtspClient?.SendMessage(keepAliveMessage);
        }



    }
}
