using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading.Tasks;
using NUnit.Framework;
using Rtsp.Messages;

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
    private readonly Dictionary<RtspRequest, (TaskCompletionSource<RtspResponse>, DateTime)> _messageQueue
        = new();

    private readonly object _lock = new();
    
    [TestCase("localhost", 8554, "rtsp://localhost:8554/Test", false)]
    [TestCase("localhost", 8322,"rtsps://localhost:8322/Test", true)]
    [Category("Integration")]
    public async Task SendOption_WhenSent_Receives200OK(string address, int port, string uri, bool enableSsl)
    {
        // arrange
        var socket = new RtspTcpTransport(new TcpClient(address, port));
        var listener = new RtspListener(socket, enableSsl:enableSsl);
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
                _messageQueue.Add(message, (taskCompletionSource, DateTime.Now));
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

    private void ListenerOnMessageReceived(object? sender, RtspChunkEventArgs e)
    {
        RtspResponse? message = e.Message as RtspResponse;

        lock (_lock)
        {
            if (_messageQueue.ContainsKey(message.OriginalRequest))
            {
                _messageQueue[message.OriginalRequest].Item1.SetResult(message);
                _messageQueue.Remove(message.OriginalRequest);
            }
        }
    }
}