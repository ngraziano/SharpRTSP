using CommunityToolkit.HighPerformance;
using Microsoft.Extensions.Logging;
using Rtsp;
using Rtsp.Messages;
using Rtsp.Onvif;
using Rtsp.Rtcp;
using Rtsp.Rtp;
using Rtsp.Sdp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Text;

namespace RtspClientExample;

class RTSPClient
{
    private class KeepAliveContext()
    {
    }

    private static readonly KeepAliveContext keepAliveContext = new();

    private readonly ILogger _logger;
    private readonly ILoggerFactory _loggerFactory;

    // Events that applications can receive
    public event EventHandler<NewStreamEventArgs>? NewVideoStream;
    public event EventHandler<NewStreamEventArgs>? NewAudioStream;

    /// <summary>
    /// Used to inform user that connection is broken ([4/5]XX errors)
    /// </summary>
    public event EventHandler? ConnectionError;

    /// <summary>
    /// Just if no video uri is available
    /// </summary>
    public event EventHandler? NoVideoPayload;

    public enum RTP_TRANSPORT
    {
        UDP,
        TCP,
        MULTICAST
    };

    public enum MEDIA_REQUEST
    {
        VIDEO_ONLY,
        AUDIO_ONLY,
        VIDEO_AND_AUDIO
    };

    private enum RTSP_STATUS
    {
        WaitingToConnect,
        Connecting,
        ConnectFailed,
        Connected
    };


    string? _setupPreferredVideoRtpMap = null;
    string? _setupPreferredAudioRtpMap = null;


    IRtspTransport? rtspSocket; // RTSP connection

    RTSP_STATUS rtspSocketStatus = RTSP_STATUS.WaitingToConnect;

    // this wraps around a the RTSP tcp_socket stream
    RtspListener? rtspClient;

    RTP_TRANSPORT rtpTransport = RTP_TRANSPORT.UDP; // Mode, either RTP over UDP or RTP over TCP using the RTSP socket

    // Communication for the RTP (video and audio) 
    IRtpTransport? videoRtpTransport;
    IRtpTransport? audioRtpTransport;

    Uri? _uri; // RTSP URI (username & password will be stripped out
    string session = ""; // RTSP Session
    private Authentication? _authentication;
    private NetworkCredential _credentials = new();
    readonly uint ssrc = 12345;
    bool clientWantsVideo = false; // Client wants to receive Video
    bool clientWantsAudio = false; // Client wants to receive Audio

    private int videoBaseClock = 0; // Should be used for appropiate Video decodification
    private int audioBaseClock = 0; // Shpuld be used for appropiate Audio decodification

    private bool _ready = false;    // Helper to avoid sending any method before setup has been completed.
                                    // If this will happen, all the chain will break (and goodbye to connection, without errors)...

    /// <summary>
    /// All the given Video Media playback on Setup request.
    /// </summary>
    private readonly Dictionary<int, string> videoPayloadMapping = [];
    /// <summary>
    /// The Payload associated with the video id
    /// </summary>
    private readonly Dictionary<int, IPayloadProcessor> videoPayloadProcessors = [];
    /// <summary>
    /// The video uris supported.
    /// </summary>
    private readonly List<Uri> video_uris = [];

    private readonly Dictionary<int, string> audioPayloadMapping = [];
    private readonly Dictionary<int, IPayloadProcessor> audioPayloadProcessors = [];
    private readonly List<Uri> audio_uris = [];

    private readonly Dictionary<string, Action<RTSPClient, SimpleDataEventArgs>> audioPayloadEvents = [];
    /// <summary>
    /// This returns the appropiate video chunks to caller.
    /// </summary>
    private readonly Dictionary<string, Action<RTSPClient, SimpleDataEventArgs>> videoPayloadEvents = [];


    /// <summary>
    /// If true, the client must send an "onvif-replay" header on every play request.
    /// </summary>
    bool _playbackSession = false;

    // Used with RTSP keepalive
    bool serverSupportsGetParameter = false;
    private readonly System.Timers.Timer keepaliveTimer;

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

        keepaliveTimer = new()
        {
            Interval = 20 * 1000,
        };
        keepaliveTimer.Elapsed += SendKeepAlive;
    }

    #region Payload Utilities

    //This section contains methods to grab informations for the video/audio payload.
    //May be used from caller to known what I am asking for. (not able to explain better, sorry)


    public string GetVideoPayloadName(int payloadType)
    {
        if (videoPayloadMapping.TryGetValue(payloadType, out string? name)) { return name; }
        return string.Empty;
    }
    public string GetAudioPayloadName(int payloadType)
    {
        if (audioPayloadMapping.TryGetValue(payloadType, out string? name)) { return name; }
        return string.Empty;
    }

    public void SetupVideoPayload(string payloadType, Action<RTSPClient, SimpleDataEventArgs> handler)
    {
        if (!videoPayloadEvents.TryGetValue(payloadType, out _))
        {
            videoPayloadEvents.Add(payloadType, handler);
        }
    }
    public bool RemoveVideoPayload(string payloadType) => videoPayloadEvents.Remove(payloadType);
    public void ClearVideoPayloads()
    {
        videoPayloadEvents.Clear();
    }


    public void SetupAudioPayload(string payloadType, Action<RTSPClient, SimpleDataEventArgs> handler)
    {
        if (!audioPayloadEvents.TryGetValue(payloadType, out _))
        {
            audioPayloadEvents.Add(payloadType, handler);
        }
    }
    public bool RemoveAudioPayload(string payloadType) => audioPayloadEvents.Remove(payloadType);
    public void ClearAudioPayloads()
    {
        audioPayloadEvents.Clear();
    }

    #endregion Payload Utilities

    /// <summary>
    /// Connect the required rtsp url.
    /// </summary>
    /// <param name="url">Url to connect</param>
    /// <param name="username">Username</param>
    /// <param name="password">Password</param>
    /// <param name="rtpTransport">Which rtp transport to use</param>
    /// <param name="mediaRequest">Audio, video or both?</param>
    /// <param name="playbackSession">Is a playback session?</param>
    /// <param name="rtpMapVideo">Do you need a specific video payback?</param>
    /// <param name="rtpMapAudio">Do you need a speficic audio payback?</param>
    /// <param name="userCertificateSelectionCallback">Needed for broken ssl certificates...</param>
    public void Connect(
        string url,
        string username,
        string password,
        RTP_TRANSPORT rtpTransport,
        MEDIA_REQUEST mediaRequest = MEDIA_REQUEST.VIDEO_AND_AUDIO,
        bool playbackSession = false,
        string? rtpMapVideo = null,
        string? rtpMapAudio = null,
        RemoteCertificateValidationCallback? userCertificateSelectionCallback = null)
    {
        RtspUtils.RegisterUri();

        _logger.LogDebug("Connecting to {url} ", url);
        _uri = new(url);

        _playbackSession = playbackSession;
        _setupPreferredVideoRtpMap = rtpMapVideo;
        _setupPreferredAudioRtpMap = rtpMapAudio;

        // Use URI to extract username and password
        // and to make a new URL without the username and password
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
        catch (Exception err)
        {
            _logger.LogWarning(err, "Fail to extract credential");
            _credentials = new();
        }

        // We can ask the RTSP server for Video, Audio or both. If we don't want audio we don't need to SETUP the audio channal or receive it
        clientWantsVideo = mediaRequest.HasFlag(MEDIA_REQUEST.VIDEO_ONLY);
        clientWantsAudio = mediaRequest.HasFlag(MEDIA_REQUEST.AUDIO_ONLY);

        // Connect to a RTSP Server. The RTSP session is a TCP connection
        rtspSocketStatus = RTSP_STATUS.Connecting;
        try
        {
            rtspSocket = RtspUtils.CreateRtspTransportFromUrl(_uri, _credentials, userCertificateSelectionCallback);
            //rtspSocket = _uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.InvariantCultureIgnoreCase)
            //    ? new RtspHttpTransport(_uri, _credentials)
            //    : new RtspTcpTransport(_uri);
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
            videoRtpTransport =
                new UDPSocket(50000,
                    51000); // give a range of 500 pairs (1000 addresses) to try incase some address are in use
            audioRtpTransport =
                new UDPSocket(50000,
                    51000); // give a range of 500 pairs (1000 addresses) to try incase some address are in use
        }

        if (rtpTransport == RTP_TRANSPORT.TCP)
        {
            int nextFreeRtpChannel = 0;
            videoRtpTransport = new RtpTcpTransport(rtspClient)
            {
                DataChannel = nextFreeRtpChannel++,
                ControlChannel = nextFreeRtpChannel++,
            };
            audioRtpTransport = new RtpTcpTransport(rtspClient)
            {
                DataChannel = nextFreeRtpChannel++,
                ControlChannel = nextFreeRtpChannel++,
            };
        }

        if (rtpTransport == RTP_TRANSPORT.MULTICAST)
        {
            // Nothing to do. Will open Multicast UDP sockets after the SETUP command
        }

        // Send OPTIONS
        // In the Received Message handler we will send DESCRIBE, SETUP and PLAY
        RtspRequest optionsMessage = new RtspRequestOptions
        {
            RtspUri = _uri
        };
        rtspClient.SendMessage(optionsMessage);

        _ready = false;
    }

    // return true if this connection failed, or if it connected but is no longer connected.
    public bool StreamingFinished() => rtspSocketStatus switch
    {
        RTSP_STATUS.ConnectFailed => true,
        RTSP_STATUS.Connected when !(rtspSocket?.Connected ?? false) => true,
        _ => false,
    };

    public bool Pause()
    {
        if (rtspSocket is null || _uri is null)
        {
            _logger.LogInformation("Not connected");
            return false;
        }
        if (!_ready) { return false; }

        // Send PAUSE
        RtspRequest pauseMessage = new RtspRequestPause
        {
            RtspUri = _uri,
            Session = session
        };
        pauseMessage.AddAuthorization(_authentication, _uri, rtspSocket.NextCommandIndex());
        rtspClient?.SendMessage(pauseMessage);
        return true;
    }

    public bool Play()
    {
        if (rtspSocket is null || _uri is null)
        {
            _logger.LogInformation("Not connected");
            return false;
        }
        if (!_ready) { return false; }

        // Send PLAY
        var playMessage = new RtspRequestPlay
        {
            RtspUri = _uri,
            Session = session
        };
        playMessage.AddAuthorization(_authentication, _uri, rtspSocket.NextCommandIndex());

        //// Need for old sony camera SNC-CS20
        playMessage.Headers.Add("range", "npt=0.000-");
        if (_playbackSession)
        {
            playMessage.AddRequireOnvifRequest();
            playMessage.AddRateControlOnvifRequest(false);
        }

        rtspClient?.SendMessage(playMessage);
        return true;
    }

    /// <summary>
    /// Generate a Play request from required time
    /// </summary>
    /// <param name="seekTime">The playback time to start from</param>
    /// <param name="speed">Speed information (1.0 means normal speed, -1.0 backward speed), other values >1.0 and <-1.0 allow a different speed</param>
    public bool Play(DateTime seekTime, double speed = 1.0)
    {
        if (rtspSocket is null || _uri is null)
        {
            _logger.LogInformation("Not connected");
            return false;
        }
        if (!_ready)
        {
            return false;
        }

        var playMessage = new RtspRequestPlay
        {
            RtspUri = _uri,
            Session = session,
        };
        playMessage.AddAuthorization(_authentication, _uri, rtspSocket.NextCommandIndex());
        playMessage.AddPlayback(seekTime, speed);
        if (_playbackSession)
        {
            playMessage.AddRequireOnvifRequest();
            playMessage.AddRateControlOnvifRequest(false);
        }

        rtspClient?.SendMessage(playMessage);
        return true;
    }

    /// <summary>
    /// Generate a Play request with a time range
    /// </summary>
    /// <param name="seekTimeFrom">Starting time for playback</param>
    /// <param name="seekTimeTo">Ending time for playback</param>
    /// <param name="speed">Speed information (1.0 means normal speed, -1.0 backward speed), other values >1.0 and <-1.0 allow a different speed</param>
    /// <exception cref="InvalidOperationException"></exception>
    public bool Play(DateTime seekTimeFrom, DateTime seekTimeTo, double speed = 1.0)
    {
        if (rtspSocket is null || _uri is null)
        {
            _logger.LogInformation("Not connected");
            return false;
        }
        if (_ready) { return false; }

        if (seekTimeFrom > seekTimeTo)
        {
            throw new ArgumentOutOfRangeException(nameof(seekTimeFrom),
                "Starting seek cannot be major than ending seek.");
        }

        var playMessage = new RtspRequestPlay
        {
            RtspUri = _uri,
            Session = session,
        };

        playMessage.AddAuthorization(_authentication, _uri, rtspSocket.NextCommandIndex());
        playMessage.AddPlayback(seekTimeFrom, seekTimeTo, speed);
        if (_playbackSession)
        {
            playMessage.AddRequireOnvifRequest();
            playMessage.AddRateControlOnvifRequest(false);
        }

        rtspClient?.SendMessage(playMessage);
        return true;
    }

    public bool Stop()
    {
        if (rtspSocket is null || _uri is null)
        {
            _logger.LogInformation("Not connected");
            return false;
        }
        if (_ready) { return false; }

        // Send TEARDOWN
        RtspRequest teardownMessage = new RtspRequestTeardown
        {
            RtspUri = _uri,
            Session = session
        };
        teardownMessage.AddAuthorization(_authentication, _uri!, rtspSocket?.NextCommandIndex() ?? 0);
        rtspClient?.SendMessage(teardownMessage);

        // Stop the keepalive timer
        keepaliveTimer.Stop();

        // clear up any UDP sockets
        videoRtpTransport?.Stop();
        audioRtpTransport?.Stop();

        // Drop the RTSP session
        rtspClient?.Stop();
        // forget current auth state
        _authentication = null;

        return true;
    }

    /// <summary>
    /// A Video RTP packet has been received.
    /// </summary>
    private void VideoRtpDataReceived(object? sender, RtspDataEventArgs e)
    {
        if (e.Data.Data.IsEmpty)
            return;

        using var data = e.Data;
        var rtpPacket = new RtpPacket(data.Data.Span);


        if (!videoPayloadProcessors.TryGetValue(rtpPacket.PayloadType, out IPayloadProcessor? videoPayloadProcessor))
        {
            _logger.LogWarning($"No videopayload for this type.");
            return;
        }

        if (!videoPayloadMapping.TryGetValue(rtpPacket.PayloadType, out string? payloadName))
        {
            _logger.LogWarning($"No videopayload mapping for this type.");
            return;
        }
        if (videoPayloadProcessor is null)
        {
            _logger.LogDebug("No video Processor");
            return;
        }

        using RawMediaFrame rawMediaFrame = videoPayloadProcessor.ProcessPacket(rtpPacket);

        if (rawMediaFrame.Any() && videoPayloadEvents.TryGetValue(payloadName, out Action<RTSPClient, SimpleDataEventArgs>? action))
        {
            action?.Invoke(this, new([.. rawMediaFrame.Data], rawMediaFrame.ClockTimestamp, rawMediaFrame.RtpTimestamp, videoBaseClock, rtpPacket.PayloadType));
        }
    }

    // RTP packet (or RTCP packet) has been received.
    private void AudioRtpDataReceived(object? sender, RtspDataEventArgs e)
    {
        if (e.Data.Data.IsEmpty)
            return;

        // Received some Audio Data on the correct channel.
        RtpPacket rtpPacket = new(e.Data.Data.Span);


        if (!audioPayloadProcessors.TryGetValue(rtpPacket.PayloadType, out IPayloadProcessor? audioPayloadProcessor))
        {
            _logger.LogDebug($"No videopayload for this type.");
            return;
        }

        if (!audioPayloadMapping.TryGetValue(rtpPacket.PayloadType, out string? payloadName))
        {
            _logger.LogDebug($"No videopayload mapping for this type.");
            return;
        }

        using RawMediaFrame rawMediaFrame = audioPayloadProcessor.ProcessPacket(rtpPacket);

        if (rawMediaFrame.Any() && audioPayloadEvents.TryGetValue(payloadName, out Action<RTSPClient, SimpleDataEventArgs>? action))
        {
            action?.Invoke(this, new([.. rawMediaFrame.Data], rawMediaFrame.ClockTimestamp, rawMediaFrame.RtpTimestamp, audioBaseClock, rtpPacket.PayloadType));
        }
    }

    // RTCP packet has been received.
    public void RtcpControlDataReceived(object? sender, RtspDataEventArgs e)
    {
        if (e.Data.Data.IsEmpty)
            return;

        if (sender is not IRtpTransport transport)
        {
            _logger.LogWarning("No RTP Transport");
            return;
        }

        _logger.LogDebug("Received a RTCP message ");

        // RTCP Packet
        // - Version, Padding and Receiver Report Count
        // - Packet Type
        // - Length
        // - SSRC
        // - payload

        // There can be multiple RTCP packets transmitted together. Loop ever each one

        var rtcpPacket = new RtcpPacket(e.Data.Data.Span);
        while (!rtcpPacket.IsEmpty)
        {
            if (!rtcpPacket.IsWellFormed)
            {
                _logger.LogWarning("Invalid RTCP packet");
                break;
            }


            // 200 = SR = Sender Report
            // 201 = RR = Receiver Report
            // 202 = SDES = Source Description
            // 203 = Bye = Goodbye
            // 204 = APP = Application Specific Method
            // 207 = XR = Extended Reports

            _logger.LogDebug("RTCP Data. PacketType={rtcp_packet_type}", rtcpPacket.PacketType);

            if (rtcpPacket.PacketType == RtcpPacketUtil.RTCP_PACKET_TYPE_SENDER_REPORT)
            {
                // We have received a Sender Report
                // Use it to convert the RTP timestamp into the UTC time
                var time = rtcpPacket.SenderReport.Clock;
                var rtpTimestamp = rtcpPacket.SenderReport.RtpTimestamp;

                _logger.LogDebug("RTCP time (UTC) for RTP timestamp {timestamp} is {time} SSRC {ssrc}", rtpTimestamp,
                    time, rtcpPacket.SenderSsrc);
                _logger.LogDebug("Packet Count {packetCount} Octet Count {octetCount}",
                    rtcpPacket.SenderReport.PacketCount, rtcpPacket.SenderReport.OctetCount);

                // Send a Receiver Report
                try
                {
                    byte[] rtcp_receiver_report = new byte[8];
                    const int reportCount = 0; // an empty report
                    int length = (rtcp_receiver_report.Length / 4) - 1; // num 32 bit words minus 1
                    RtcpPacketUtil.WriteHeader(
                        rtcp_receiver_report,
                        RtcpPacketUtil.RTCP_VERSION,
                        false,
                        reportCount,
                        RtcpPacketUtil.RTCP_PACKET_TYPE_RECEIVER_REPORT,
                        length,
                        ssrc);

                    transport.WriteToControlPort(rtcp_receiver_report);
                }
                catch
                {
                    _logger.LogDebug("Error writing RTCP packet");
                }
            }

            rtcpPacket = rtcpPacket.Next;
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
            _logger.LogDebug("Got Error in RTSP Reply {returnCode} {returnMessage}", message.ReturnCode,
                message.ReturnMessage);

            // The server may send a new nonce after a time, which will cause our keepalives to return a 401
            // error. We do not fail on keepalive and will reauthenticate a failed keepalive.
            // The Axis M5525 Camera has been observed to send a new nonce every 150 seconds.

            if (message.ReturnCode == 401
                && message.OriginalRequest?.Headers.ContainsKey(RtspHeaderNames.Authorization) == true
                && message.OriginalRequest?.ContextData != keepAliveContext)
            {
                // the authorization failed.
                _logger.LogError("Fail to authenticate stopping here");
                Stop();
                return;
            }

            if (message.ReturnCode == 400)
            {
                _logger.LogError("[400] Bad request.");
                ConnectionError?.Invoke(this, EventArgs.Empty);
                Stop();
                return;
            }

            if (message.ReturnCode == 403)
            {
                _logger.LogError("[403] User cannot access required resource.");
                ConnectionError?.Invoke(this, EventArgs.Empty);
                Stop();
                return;
            }

            if (message.ReturnCode == 501)
            {
                _logger.LogError("[501] Method not implemented.");
                ConnectionError?.Invoke(this, EventArgs.Empty);
                Stop();
                return;
            }

            if (message.ReturnCode == 503)
            {
                _logger.LogError("[503] Not available.");
                ConnectionError?.Invoke(this, EventArgs.Empty);
                Stop();
                return;
            }

            // Check if the Reply has an Authenticate header.
            if (message.ReturnCode == 401 &&
                message.Headers.TryGetValue(RtspHeaderNames.WWWAuthenticate, out string? value))
            {
                // Process the WWW-Authenticate header
                // EG:   Basic realm="AProxy"
                // EG:   Digest realm="AXIS_WS_ACCC8E3A0A8F", nonce="000057c3Y810622bff50b36005eb5efeae118626a161bf", stale=FALSE
                // EG:   Digest realm="IP Camera(21388)", nonce="534407f373af1bdff561b7b4da295354", stale="FALSE"

                string wwwAuthenticate = value ?? string.Empty;
                _authentication = Authentication.Create(_credentials, wwwAuthenticate);
                _logger.LogDebug("WWW Authorize parsed for {authentication}", _authentication);
            }

            if (message.OriginalRequest?.Clone() is RtspRequest resendMessage)
            {
                resendMessage.AddAuthorization(_authentication, _uri!, rtspSocket!.NextCommandIndex());
                rtspClient?.SendMessage(resendMessage);
            }

            return;
        }

        switch (message.OriginalRequest)
        {
            // If we get a reply to OPTIONS then start the Keepalive Timer and send DESCRIBE
            case RtspRequestOptions when message.OriginalRequest.ContextData != keepAliveContext:
                {
                    // Check the capabilities returned by OPTIONS
                    // The Public: header contains the list of commands the RTSP server supports
                    // Eg   DESCRIBE, SETUP, TEARDOWN, PLAY, PAUSE, OPTIONS, ANNOUNCE, RECORD, GET_PARAMETER]}
                    var supportedCommand = RTSPHeaderUtils.ParsePublicHeader(message);
                    serverSupportsGetParameter = supportedCommand.Contains("GET_PARAMETER", StringComparer.OrdinalIgnoreCase);
                    // Start a Timer to send an Keepalive RTSP command every 20 seconds
                    keepaliveTimer.Enabled = true;

                    // Send DESCRIBE
                    RtspRequest describeMessage = new RtspRequestDescribe
                    {
                        RtspUri = _uri,
                        Headers = { { "Accept", "application/sdp" } },
                    };
                    describeMessage.AddAuthorization(_authentication, _uri!, rtspSocket!.NextCommandIndex());
                    rtspClient?.SendMessage(describeMessage);
                    break;
                }
            // If we get a reply to DESCRIBE (which was our second command), then process SDP and send the SETUP
            case RtspRequestDescribe:
                HandleDescribeResponse(message);
                break;
            // If we get a reply to SETUP (which was our third command), then we
            // (i) check if the Interleaved Channel numbers have been modified by the camera (eg Panasonic cameras)
            // (ii) check if we have any more SETUP commands to send out (eg if we are doing SETUP for Video and Audio)
            // (iii) send a PLAY command if all the SETUP command have been sent
            case RtspRequestSetup:
                {
                    HandleSetupResponse(message);

                    break;
                }
            // If we get a reply to PLAY (which was our fourth command), then we should have video being received
            case RtspRequestPlay:
                _logger.LogDebug("Got reply from Play {command} ", message.Command);
                break;
        }
    }

    private void HandleSetupResponse(RtspResponse message)
    {
        Debug.Assert(message.OriginalRequest is RtspRequestSetup, "Expected a SETUP request");

        _logger.LogDebug("Got reply from Setup. Session is {session}", message.Session);

        // Session value used with Play, Pause, Teardown and and additional Setups
        session = message.Session ?? "";
        if (keepaliveTimer != null && message.Timeout > 0 && message.Timeout > keepaliveTimer.Interval / 1000)
        {
            keepaliveTimer.Interval = message.Timeout * 1000 / 2;
        }

        bool isVideoChannel = message.OriginalRequest.RtspUri != null && video_uris.Contains(message.OriginalRequest.RtspUri); // == video_uri;
        bool isAudioChannel = message.OriginalRequest.RtspUri != null && audio_uris.Contains(message.OriginalRequest.RtspUri); // == audio_uri;
        Debug.Assert(isVideoChannel || isAudioChannel, "Unknown channel response");

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
                    if (isVideoChannel)
                    {
                        videoRtpTransport = new MulticastUDPSocket(multicastAddress, videoDataChannel.Value,
                            multicastAddress, videoRtcpChannel.Value);
                    }
                    else if (isAudioChannel)
                    {
                        audioRtpTransport = new MulticastUDPSocket(multicastAddress, videoDataChannel.Value,
                            multicastAddress, videoRtcpChannel.Value);
                    }
                }
            }

            // check if the requested Interleaved channels have been modified by the camera
            // in the SETUP Reply (Panasonic have a camera that does this)
            if (transport.LowerTransport == RtspTransport.LowerTransportType.TCP)
            {
                RtpTcpTransport? tcpTransport = null;
                if (isVideoChannel)
                {
                    tcpTransport = videoRtpTransport as RtpTcpTransport;
                }

                if (isAudioChannel)
                {
                    tcpTransport = audioRtpTransport as RtpTcpTransport;
                }

                if (tcpTransport is not null)
                {
                    tcpTransport.DataChannel = transport.Interleaved?.First ?? tcpTransport.DataChannel;
                    tcpTransport.ControlChannel = transport.Interleaved?.Second ?? tcpTransport.ControlChannel;
                }
            }
            else if (!transport.IsMulticast)
            {
                UDPSocket? udpSocket = null;
                if (isVideoChannel)
                {
                    udpSocket = videoRtpTransport as UDPSocket;
                }

                if (isAudioChannel)
                {
                    udpSocket = audioRtpTransport as UDPSocket;
                }

                if (udpSocket is not null)
                {
                    udpSocket.SetDataDestination(_uri!.Host, transport.ServerPort?.First ?? 0);
                    udpSocket.SetControlDestination(_uri!.Host, transport.ServerPort?.Second ?? 0);
                }
            }

            if (isVideoChannel && videoRtpTransport is not null)
            {
                videoRtpTransport.DataReceived += VideoRtpDataReceived;
                videoRtpTransport.ControlReceived += RtcpControlDataReceived;
                videoRtpTransport.Start();
            }

            if (isAudioChannel && audioRtpTransport is not null)
            {
                audioRtpTransport.DataReceived += AudioRtpDataReceived;
                audioRtpTransport.ControlReceived += RtcpControlDataReceived;
                audioRtpTransport.Start();
            }
        }

        // Check if we have another SETUP command to send, then remote it from the list
        if (setupMessages.Count > 0)
        {
            // send the next SETUP message, after adding in the 'session'
            RtspRequestSetup nextSetup = setupMessages.Dequeue();
            nextSetup.Session = session;
            rtspClient?.SendMessage(nextSetup);
        }
        else
        {
            // setup is completed, we can receive now all the events we want...
            _ready = true;
            // use the event for setup completed, so the main program can call the Play command with or without the playback request.
            SetupMessageCompleted?.Invoke(this, EventArgs.Empty);
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
            sdp_data = SdpFile.ReadLoose(sdp_stream);
        }

        Attribut? customControlUri = sdp_data.Attributs.FirstOrDefault(x => string.Equals(x.Key, "control", StringComparison.OrdinalIgnoreCase));
        if (customControlUri is not null && !string.Equals(customControlUri.Value, "*", StringComparison.OrdinalIgnoreCase))
        {
            _uri = new Uri(_uri!, customControlUri.Value);
        }

        // Process each 'Media' Attribute in the SDP (each sub-stream)
        // to look for first supported video substream
        if (clientWantsVideo)
        {
            foreach (Media media in sdp_data.Medias.Where(m => m.MediaType == Media.MediaTypes.video))
            {
                int video_payload = -1;
                IPayloadProcessor? videoPayloadProcessor = null;

                // search the attributes for control, rtpmap and fmtp
                // holds SPS and PPS in base64 (h264 video)
                AttributFmtp? fmtp = media.Attributs.FirstOrDefault(x => x.Key == "fmtp") as AttributFmtp;
                AttributRtpMap? rtpmap = media.Attributs.FirstOrDefault(x => x.Key == "rtpmap") as AttributRtpMap;
                Uri? video_uri = GetControlUri(media);

                if (!string.IsNullOrEmpty(_setupPreferredVideoRtpMap) && !(rtpmap?.EncodingName?.Equals(_setupPreferredVideoRtpMap, StringComparison.OrdinalIgnoreCase) ?? true))
                {
                    _logger.LogDebug($"Not requested one.");
                    continue;
                }

                int fmtpPayloadNumber = -1;
                if (fmtp != null)
                {
                    fmtpPayloadNumber = fmtp.PayloadNumber;
                }

                if (int.TryParse(rtpmap?.ClockRate, NumberStyles.Integer, NumberFormatInfo.CurrentInfo, out int clockRate))
                {
                    // a rtsp client can have a single clockrate by url (I hope)...
                    videoBaseClock = clockRate;
                }

                // extract h265 donl if available...
                bool h265HasDonl = false;

                if ((rtpmap?.EncodingName?.ToUpper().Equals("H265") ?? false) &&
                    !string.IsNullOrEmpty(fmtp?.FormatParameter))
                {
                    var param = H265Parameters.Parse(fmtp.FormatParameter);
                    if (param.ContainsKey("sprop-max-don-diff") &&
                        int.TryParse(param["sprop-max-don-diff"], NumberStyles.Integer, CultureInfo.InvariantCulture, out int donl) && donl > 0)
                    {
                        h265HasDonl = true;
                    }
                }

                // some cameras are really mess with the payload type.
                // must check also the rtpmap for the corrent format to load (sending an h265 payload when giving an h264 stream [Some Bosch camera])

                string payloadName = string.Empty;
                if (rtpmap != null
                    && (((fmtpPayloadNumber > -1 && rtpmap.PayloadNumber == fmtpPayloadNumber) ||
                         fmtpPayloadNumber == -1)
                        && rtpmap.EncodingName != null))
                {
                    // found a valid codec
                    payloadName = rtpmap.EncodingName.ToUpper();
                    videoPayloadProcessor = payloadName switch
                    {
                        "H264" => new H264Payload(),
                        "H265" => new H265Payload(h265HasDonl),
                        "JPEG" => new JPEGPayload(),
                        "MP4V-ES" => new RawPayload(),
                        _ => null,
                    };
                    video_payload = media.PayloadType;
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
                        payloadName = media.PayloadType switch
                        {
                            26 => "JPEG",
                            33 => "MP2T",
                            _ => string.Empty,
                        };

                    }
                    else if (rtpmap != null)
                    {
                        payloadName = rtpmap.EncodingName?.ToUpperInvariant() ?? string.Empty;
                        videoPayloadProcessor = payloadName switch
                        {
                            "H264" => new H264Payload(),
                            "H265" => new H265Payload(h265HasDonl),
                            "JPEG" => new JPEGPayload(),
                            "MP4V-ES" => new RawPayload(),
                            _ => null,
                        };
                        video_payload = media.PayloadType;
                    }
                }

                IStreamConfigurationData? streamConfigurationData = null;

                if (videoPayloadProcessor is H264Payload && fmtp?.FormatParameter is not null)
                {
                    // If the rtpmap contains H264 then split the fmtp to get the sprop-parameter-sets which hold the SPS and PPS in base64
                    var param = H264Parameters.Parse(fmtp.FormatParameter);
                    if (param.SpropParameterSets.Count >= 2)
                    {
                        streamConfigurationData = new H264StreamConfigurationData()
                        {
                            OutOfBandNal = [.. param.SpropParameterSets],
                        };
                    }
                }
                else if (videoPayloadProcessor is H265Payload && fmtp?.FormatParameter is not null)
                {
                    // If the rtpmap contains H265 then split the fmtp to get the sprop-vps, sprop-sps and sprop-pps
                    // The RFC makes the VPS, SPS and PPS OPTIONAL so they may not be present. In which we pass back NULL values
                    var param = H265Parameters.Parse(fmtp.FormatParameter);
                    streamConfigurationData = new H265StreamConfigurationData()
                    {
                        OutOfBandNal = [.. param.VideoParameterSet, ..param.SequenceParameterSet, .. param.PictureParameterSet, ..param.SEIMessages],
                    };
                }

                // Send the SETUP RTSP command if we have a matching Payload Decoder
                if (videoPayloadProcessor is not null)
                {
                    var transport = CalculateTransport(videoRtpTransport);

                    // Generate SETUP messages
                    if (transport != null)
                    {
                        RtspRequestSetup setupMessage = new()
                        {
                            RtspUri = video_uri
                        };
                        setupMessage.AddTransport(transport);
                        setupMessage.AddAuthorization(_authentication, _uri!, rtspSocket!.NextCommandIndex());
                        if (_playbackSession)
                        {
                            setupMessage.AddRequireOnvifRequest();
                        }

                        // Add SETUP message to list of messages to send
                        setupMessages.Enqueue(setupMessage);

                        NewVideoStream?.Invoke(this, new(payloadName, streamConfigurationData));
                    }

                    if (!videoPayloadProcessors.TryGetValue(video_payload, out _))
                    {
                        videoPayloadProcessors.Add(video_payload, videoPayloadProcessor);
                    }
                    if (!videoPayloadMapping.TryGetValue(video_payload, out _))
                    {
                        videoPayloadMapping.Add(video_payload, payloadName);
                    }

                    if (video_uri != null && !video_uris.Contains(video_uri)) { video_uris.Add(video_uri); }

                    if (!string.IsNullOrEmpty(_setupPreferredVideoRtpMap))
                    {
                        // break here, the requested one has been setup.
                        // there should be no other video stream setup now...
                        break;
                    }
                }
            }

            if (videoPayloadProcessors.Count == 0)
            {
                // send an info about video not available?
                NoVideoPayload?.Invoke(this, EventArgs.Empty);
            }
        }

        if (clientWantsAudio)
        {
            foreach (var media in sdp_data.Medias.Where(m => m.MediaType == Media.MediaTypes.audio))
            {
                int audio_payload = -1;
                string audio_codec;
                IPayloadProcessor? audioPayloadProcessor = null;

                // search the attributes for control, rtpmap and fmtp
                AttributFmtp? fmtp = media.Attributs.FirstOrDefault(x => x.Key == "fmtp") as AttributFmtp;
                AttributRtpMap? rtpmap = media.Attributs.FirstOrDefault(x => x.Key == "rtpmap") as AttributRtpMap;

                Uri? audio_uri = GetControlUri(media);
                audio_payload = media.PayloadType;

                IStreamConfigurationData? streamConfigurationData = null;
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
                    if (audioPayloadProcessor is AACPayload aacPayloadProcessor)
                    {
                        audio_codec = "AAC";
                        streamConfigurationData = new AacStreamConfigurationData()
                        {
                            ObjectType = aacPayloadProcessor.ObjectType,
                            FrequencyIndex = aacPayloadProcessor.FrequencyIndex,
                            SamplingFrequency = aacPayloadProcessor.SamplingFrequency,
                            ChannelConfiguration = aacPayloadProcessor.ChannelConfiguration
                        };
                    }
                }

                // Send the SETUP RTSP command if we have a matching Payload Decoder
                if (audioPayloadProcessor is not null)
                {
                    RtspTransport? transport = CalculateTransport(audioRtpTransport);

                    // Generate SETUP messages
                    if (transport != null)
                    {
                        RtspRequestSetup setupMessage = new()
                        {
                            RtspUri = audio_uri,
                        };
                        setupMessage.AddTransport(transport);
                        setupMessage.AddAuthorization(_authentication, _uri!, rtspSocket!.NextCommandIndex());
                        if (_playbackSession)
                        {
                            setupMessage.AddRequireOnvifRequest();
                            setupMessage.AddRateControlOnvifRequest(false);
                        }

                        // Add SETUP message to list of messages to send
                        setupMessages.Enqueue(setupMessage);
                        NewAudioStream?.Invoke(this, new(audio_codec, streamConfigurationData));
                    }

                    if (!videoPayloadProcessors.TryGetValue(audio_payload, out _))
                    {
                        videoPayloadProcessors.Add(audio_payload, audioPayloadProcessor);
                    }
                    if (!videoPayloadMapping.TryGetValue(audio_payload, out _))
                    {
                        videoPayloadMapping.Add(audio_payload, audio_codec);
                    }

                    if (audio_uri != null && !video_uris.Contains(audio_uri)) { audio_uris.Add(audio_uri); }

                    if (!string.IsNullOrEmpty(_setupPreferredAudioRtpMap))
                    {
                        // break here, the requested one has been setup.
                        // there should be no other video stream setup now...
                        break;
                    }
                }
            }
        }

        if (setupMessages.Count == 0)
        {
            ConnectionError?.Invoke(this, EventArgs.Empty);
            // No SETUP messages were generated
            // So we cannot continue
            throw new ApplicationException("Unable to setup media stream");
        }

        // Send the FIRST SETUP message and remove it from the list of Setup Messages
        rtspClient?.SendMessage(setupMessages.Dequeue());
    }

    private Uri? GetControlUri(Media media)
    {
        var attrib = media.Attributs.FirstOrDefault(a => a.Key == "control");
        if (attrib is null) return null;

        var sdpControl = attrib.Value;
        if (
            sdpControl.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase)
            || sdpControl.StartsWith("rtsps://", StringComparison.OrdinalIgnoreCase)
            || sdpControl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || sdpControl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            // the "track" or "stream id"
            return new(sdpControl);
        }

        // add trailing / if necessary
        var baseUriWithTrailingSlash = _uri!.ToString().EndsWith('/') ? _uri : new($"{_uri}/");
        // relative path
        return new(baseUriWithTrailingSlash, sdpControl);
    }

    private RtspTransport? CalculateTransport(IRtpTransport? transport)
    {
        return rtpTransport switch
        {
            // Server interleaves the RTP packets over the RTSP connection
            // Example for TCP mode (RTP over RTSP)   Transport: RTP/AVP/TCP;interleaved=0-1
            RTP_TRANSPORT.TCP => new()
            {
                LowerTransport = RtspTransport.LowerTransportType.TCP,
                // Eg Channel 0 for RTP video data. Channel 1 for RTCP status reports
                Interleaved = (transport as RtpTcpTransport)?.Channels ??
                              throw new ApplicationException("TCP transport asked and no tcp channel allocated"),
            },
            RTP_TRANSPORT.UDP => new()
            {
                LowerTransport = RtspTransport.LowerTransportType.UDP,
                IsMulticast = false,
                ClientPort = (transport as UDPSocket)?.Ports ??
                             throw new ApplicationException("UDP transport asked and no udp port allocated"),
            },
            // Server sends the RTP packets to a Pair of UDP ports (one for data, one for rtcp control messages)
            // using Multicast Address and Ports that are in the reply to the SETUP message
            // Example for MULTICAST mode     Transport: RTP/AVP;multicast
            RTP_TRANSPORT.MULTICAST => new()
            {
                LowerTransport = RtspTransport.LowerTransportType.UDP,
                IsMulticast = true,
                ClientPort = new(5000, 5001)
            },
            _ => null,
        };
    }

    private void SendKeepAlive(object? sender, System.Timers.ElapsedEventArgs e)
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

        keepAliveMessage.ContextData = keepAliveContext;
        keepAliveMessage.AddAuthorization(_authentication, _uri!, rtspSocket!.NextCommandIndex());
        rtspClient?.SendMessage(keepAliveMessage);
    }
}