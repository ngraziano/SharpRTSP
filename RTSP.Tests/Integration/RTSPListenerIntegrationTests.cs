using NUnit.Framework;
using Rtsp.Messages;
using System;
using System.Collections.Generic;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace Rtsp.Tests.Integration;

/// <summary>
/// RtspListenerIntegrationTests - These tests use the RtspTcpTransport and RtspListener to connect to a server (e.g. MediaMTX)
/// These have been marked as Category Integration to allow filtration out of continuous integration pipelines
/// To run these tests follow the guidelines on <see cref="https://github.com/bluenviron/mediamtx?tab=readme-ov-file#rtsp-specific-features"/>
/// - Enable RTSPS in MediaMtx on port 8322 and rtsp on port 8554
/// - Use Rest API to add a remote stream to MediaMtx on the /Test path
/// - Use Wireshark to listen for traffic on local loopback with filter "tcp.port == 8322 || tcp.port == 8554"
/// - Run Test
/// - Observe TLS/SSL handshake and communication between client and server
/// </summary>
public class RTSPListenerIntegrationTests
{
    private readonly Dictionary<RtspRequest, TaskCompletionSource<RtspResponse>> _messageQueue = [];

    private readonly object _lock = new();

    [TestCase("rtsp://localhost:8554/Test")]
    [TestCase("rtsps://localhost:8322/Test")]
    [Category("Integration")]
    [Explicit("These tests require a running RTSP server and are not suitable for CI pipelines")]
    public async Task SendOption_WhenSent_Receives200OK(string uri)
    {
        // arrange
        var socket = RtspUtils.CreateRtspTransportFromUrl(new(uri), new(), AcceptAllCertificate);
        var listener = new RtspListener(socket);
        var taskCompletionSource = new TaskCompletionSource<RtspResponse>();
        listener.MessageReceived += ListenerOnMessageReceived;
        listener.Start();

        var message = new RtspRequestOptions
        {
            RtspUri = new Uri(uri)
        };

        // act
        if (listener.SendMessage(message))
        {
            lock (_lock)
            {
                _messageQueue.Add(message, taskCompletionSource);
            }

            var result = await taskCompletionSource.Task;

            Assert.That(result, Is.Not.Null);
            Assert.That(result.ReturnCode, Is.EqualTo(200));
        }
        else
        {
            Assert.Fail("Unable to send message");
        }
    }

    private bool AcceptAllCertificate(object? sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
    {
        return true;
    }

    private void ListenerOnMessageReceived(object? sender, RtspChunkEventArgs e)
    {
        if (e.Message is not RtspResponse message || message.OriginalRequest is null)
            return;

        lock (_lock)
        {
            if (_messageQueue.TryGetValue(message.OriginalRequest, out TaskCompletionSource<RtspResponse>? value))
            {
                value.SetResult(message);
                _messageQueue.Remove(message.OriginalRequest);
            }
        }
    }
}