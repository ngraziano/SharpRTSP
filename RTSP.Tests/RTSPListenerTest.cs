using NSubstitute;
using NUnit.Framework;
using Rtsp.Messages;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rtsp.Tests
{
    [TestFixture]
    public class RtspListenerTest
    {
        IRtspTransport _mockTransport;
        bool _connected = true;
        readonly object _lock = new();
        List<RtspChunk> _receivedMessage;
        List<RtspChunk> _receivedData;


        private void MessageReceived(object sender, RtspChunkEventArgs e)
        {
            lock (_lock)
            {
                _receivedMessage.Add(e.Message);
            }
        }

        private void DataReceived(object sender, RtspChunkEventArgs e)
        {
            lock (_lock)
            {
                _receivedData.Add(e.Message);
            }
        }

        private async Task WaitNMessageOrTimeout(int nbMessage, int timeout)
        {
            const int interval = 10;
            int time = 0;
            while (time < timeout)
            {
                lock (_lock)
                {
                    if (_receivedMessage.Count + _receivedData.Count >= nbMessage)
                    {
                        return;
                    }
                }
                await Task.Delay(interval);
                time += interval;
            }
        }


        [SetUp]
        public void Init()
        {
            // Setup a mock
            _mockTransport = Substitute.For<IRtspTransport>();
            _connected = true;
            _mockTransport.Connected.Returns(_ => _connected);
            _mockTransport.When(x => x.Close()).Do(_ => _connected = false);
            _mockTransport.When(x => x.Reconnect()).Do(_ => _connected = true);

            _receivedData = [];
            _receivedMessage = [];
        }

        [Test]
        public async Task ReceiveOptionsMessage()
        {
            const string message =
                """
                OPTIONS * RTSP/1.0
                CSeq: 1
                Require: implicit-play
                Proxy-Require: gzipped-messages
                
                """;
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(message));
            _mockTransport.GetStream().Returns(stream);

            // Setup test object.
            var testedListener = new RtspListener(_mockTransport);
            testedListener.MessageReceived += MessageReceived;
            testedListener.DataReceived += DataReceived;

            // Run
            testedListener.Start();
            await WaitNMessageOrTimeout(1, 100);
            testedListener.Stop();

            // Check the transport was closed.
            _mockTransport.Received().Close();
            //Check the message recevied
            Assert.That(_receivedMessage, Has.Count.EqualTo(1));
            RtspChunk theMessage = _receivedMessage[0];
            Assert.That(theMessage, Is.InstanceOf<RtspRequest>());
            Assert.Multiple(() =>
            {
                Assert.That(theMessage.Data.Length, Is.EqualTo(0));
                Assert.That(theMessage.SourcePort, Is.SameAs(testedListener));
            });

            RtspRequest theRequest = theMessage as RtspRequest;
            Assert.Multiple(() =>
            {
                Assert.That(theRequest.RequestTyped, Is.EqualTo(RtspRequest.RequestType.OPTIONS));
                Assert.That(theRequest.Headers, Has.Count.EqualTo(3));
                Assert.That(theRequest.CSeq, Is.EqualTo(1));
            });
            Assert.That(theRequest.Headers.Keys, Does.Contain("Require"));
            Assert.Multiple(() =>
            {
                Assert.That(theRequest.Headers.Keys, Does.Contain("Proxy-Require"));
                Assert.That(theRequest.RtspUri, Is.EqualTo(null));

                Assert.That(_receivedData, Is.Empty);
            });
        }

        [Test]
        public async Task ReceivePlayMessage()
        {
            string message = string.Empty;
            message += "PLAY rtsp://audio.example.com/audio RTSP/1.0\r\n";
            message += "CSeq: 835\r\n";
            message += "\r\n";
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(message));
            _mockTransport.GetStream().Returns(stream);

            // Setup test object.
            var testedListener = new RtspListener(_mockTransport);
            testedListener.MessageReceived += MessageReceived;
            testedListener.DataReceived += DataReceived;

            // Run
            testedListener.Start();
            await WaitNMessageOrTimeout(1, 100);
            testedListener.Stop();

            // Check the transport was closed.
            _mockTransport.Received().Close();
            //Check the message recevied
            Assert.That(_receivedMessage, Has.Count.EqualTo(1));
            RtspChunk theMessage = _receivedMessage[0];
            Assert.That(theMessage, Is.InstanceOf<RtspRequest>());
            Assert.Multiple(() =>
            {
                Assert.That(theMessage.Data.Length, Is.EqualTo(0));
                Assert.That(theMessage.SourcePort, Is.SameAs(testedListener));
            });

            RtspRequest theRequest = theMessage as RtspRequest;
            Assert.Multiple(() =>
            {
                Assert.That(theRequest.RequestTyped, Is.EqualTo(RtspRequest.RequestType.PLAY));
                Assert.That(theRequest.Headers, Has.Count.EqualTo(1));
                Assert.That(theRequest.CSeq, Is.EqualTo(835));
                Assert.That(theRequest.RtspUri.ToString(), Is.EqualTo("rtsp://audio.example.com/audio"));

                Assert.That(_receivedData, Is.Empty);
            });
        }

        [Test]
        public async Task ReceiveResponseMessage()
        {
            string message = string.Empty;
            message += "RTSP/1.0 551 Option not supported\n";
            message += "CSeq: 302\n";
            message += "Unsupported: funky-feature\n";
            message += "\r\n";
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(message));
            _mockTransport.GetStream().Returns(stream);

            // Setup test object.
            var testedListener = new RtspListener(_mockTransport);
            testedListener.MessageReceived += MessageReceived;
            testedListener.DataReceived += DataReceived;

            // Run
            testedListener.Start();
            await WaitNMessageOrTimeout(1, 100);
            testedListener.Stop();

            // Check the transport was closed.
            _mockTransport.Received().Close();
            //Check the message recevied
            Assert.That(_receivedMessage, Has.Count.EqualTo(1));
            RtspChunk theMessage = _receivedMessage[0];
            Assert.That(theMessage, Is.InstanceOf<RtspResponse>());
            Assert.Multiple(() =>
            {
                Assert.That(theMessage.Data.Length, Is.EqualTo(0));
                Assert.That(theMessage.SourcePort, Is.SameAs(testedListener));
            });

            RtspResponse theResponse = theMessage as RtspResponse;
            Assert.Multiple(() =>
            {
                Assert.That(theResponse.ReturnCode, Is.EqualTo(551));
                Assert.That(theResponse.ReturnMessage, Is.EqualTo("Option not supported"));
                Assert.That(theResponse.Headers, Has.Count.EqualTo(2));
                Assert.That(theResponse.CSeq, Is.EqualTo(302));

                Assert.That(_receivedData, Is.Empty);
            });
        }

        [Test]
        public async Task ReceiveData()
        {
            var rnd = new Random();
            byte[] data = new byte[0x0234];
            rnd.NextBytes(data);

            byte[] buffer = new byte[data.Length + 4];
            buffer[0] = 0x24; // $
            buffer[1] = 11;
            buffer[2] = 0x02;
            buffer[3] = 0x34;
            Buffer.BlockCopy(data, 0, buffer, 4, data.Length);

            var stream = new MemoryStream(buffer);
            _mockTransport.GetStream().Returns(stream);

            // Setup test object.
            var testedListener = new RtspListener(_mockTransport);
            testedListener.MessageReceived += MessageReceived;
            testedListener.DataReceived += DataReceived;

            // Run
            testedListener.Start();
            await WaitNMessageOrTimeout(1, 500);
            testedListener.Stop();

            // Check the transport was closed.
            _mockTransport.Received().Close();
            Assert.Multiple(() =>
            {
                //Check the message recevied
                Assert.That(_receivedMessage, Is.Empty);
                Assert.That(_receivedData, Has.Count.EqualTo(1));
            });
            Assert.That(_receivedData[0], Is.InstanceOf<RtspData>());
            var dataMessage = _receivedData[0] as RtspData;

            Assert.Multiple(() =>
            {
                Assert.That(dataMessage.Channel, Is.EqualTo(11));
                Assert.That(dataMessage.SourcePort, Is.SameAs(testedListener));
                Assert.That(dataMessage.Data.ToArray(), Is.EqualTo(data));
            });
        }

        [Test]
        public async Task ReceiveDataInTwoPart()
        {
            var rnd = new Random();
            byte[] data = new byte[0x0234];
            rnd.NextBytes(data);

            byte[] buffer = new byte[data.Length + 4];
            buffer[0] = 0x24; // $
            buffer[1] = 11;
            buffer[2] = 0x02;
            buffer[3] = 0x34;
            Buffer.BlockCopy(data, 0, buffer, 4, data.Length);

            using var pipeServer = new AnonymousPipeServerStream();
            using var pipeClient = new AnonymousPipeClientStream(pipeServer.GetClientHandleAsString());

            _mockTransport.GetStream().Returns(pipeClient);

            // Setup test object.
            var testedListener = new RtspListener(_mockTransport);
            testedListener.MessageReceived += MessageReceived;
            testedListener.DataReceived += DataReceived;

            // Run
            testedListener.Start();
            // first message in two part
            pipeServer.Write(buffer.AsSpan()[0..100]);
            await Task.Delay(20);
            pipeServer.Write(buffer.AsSpan()[100..]);
            // second message
            pipeServer.Write(buffer);
            // add some data not finished
            pipeServer.Write(buffer.AsSpan()[100..]);
            await WaitNMessageOrTimeout(2, 500);
            testedListener.Stop();

            // Check the transport was closed.
            _mockTransport.Received().Close();
            Assert.Multiple(() =>
            {
                //Check the message recevied
                Assert.That(_receivedMessage, Is.Empty);
                Assert.That(_receivedData, Has.Count.EqualTo(2));
            });
            Assert.That(_receivedData[0], Is.InstanceOf<RtspData>());
            var dataMessage = _receivedData[0] as RtspData;

            Assert.Multiple(() =>
            {
                Assert.That(dataMessage.Channel, Is.EqualTo(11));
                Assert.That(dataMessage.SourcePort, Is.SameAs(testedListener));
                Assert.That(dataMessage.Data.ToArray(), Is.EqualTo(data));
            });

            Assert.That(_receivedData[1], Is.InstanceOf<RtspData>());
            dataMessage = _receivedData[1] as RtspData;

            Assert.Multiple(() =>
            {
                Assert.That(dataMessage.Channel, Is.EqualTo(11));
                Assert.That(dataMessage.SourcePort, Is.SameAs(testedListener));
                Assert.That(dataMessage.Data.ToArray(), Is.EqualTo(data));
            });
        }

        [Test]
        public async Task ReceiveNoMessage()
        {
            string message = string.Empty;
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(message));
            _mockTransport.GetStream().Returns(stream);

            // Setup test object.
            var testedListener = new RtspListener(_mockTransport);
            testedListener.MessageReceived += MessageReceived;
            testedListener.DataReceived += DataReceived;

            // Run
            testedListener.Start();
            await WaitNMessageOrTimeout(1, 100);
            testedListener.Stop();

            // Check the transport was closed.
            _mockTransport.Received().Close();
            Assert.Multiple(() =>
            {
                Assert.That(_receivedMessage, Is.Empty);
                Assert.That(_receivedData, Is.Empty);
            });
        }

        [Test]
        public async Task ReceiveMessageInterrupt()
        {
            string message = string.Empty;
            message += "PLAY rtsp://audio.example.com/audio RTSP/1.";
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(message));
            _mockTransport.GetStream().Returns(stream);

            // Setup test object.
            var testedListener = new RtspListener(_mockTransport);
            testedListener.MessageReceived += MessageReceived;
            testedListener.DataReceived += DataReceived;

            // Run
            testedListener.Start();
            await WaitNMessageOrTimeout(1, 100);
            // No exception should be generate.
            stream.Close();

            // Check the transport was closed.
            _mockTransport.Received().Close();
            Assert.Multiple(() =>
            {
                //Check the message recevied
                Assert.That(_receivedMessage, Is.Empty);
                Assert.That(_receivedData, Is.Empty);
            });
        }

        [Test]
        public void SendMessage()
        {
            var stream = new MemoryStream();
            _mockTransport.GetStream().Returns(stream);

            // Setup test object.
            var testedListener = new RtspListener(_mockTransport);
            testedListener.MessageReceived += MessageReceived;
            testedListener.DataReceived += DataReceived;

            RtspMessage message = new RtspRequestOptions();

            // Run
            var isSuccess = testedListener.SendMessage(message);

            Assert.That(isSuccess, Is.True);
            string result = Encoding.UTF8.GetString(stream.GetBuffer());
            result = result.TrimEnd('\0');
            Assert.That(result, Does.StartWith("OPTIONS * RTSP/1.0\r\n"));
            // packet without payload must end with double return
            Assert.That(result, Does.EndWith("\r\n\r\n"));
        }

        [Test]
        public void SendDataBeginEnd()
        {
            const int dataLenght = 300;

            var stream = new MemoryStream();
            _mockTransport.GetStream().Returns(stream);

            // Setup test object.
            var testedListener = new RtspListener(_mockTransport);
            testedListener.MessageReceived += MessageReceived;
            testedListener.DataReceived += DataReceived;

            var data = new RtspData
            {
                Channel = 12,
                Data = Enumerable.Range(0, dataLenght).Select(x => (byte)x).ToArray()
            };

            // Run
            var asyncResult = testedListener.BeginSendData(data, null, null);
            testedListener.EndSendData(asyncResult);

            var result = stream.GetBuffer();

            int index = 0;
            Assert.That(result[index++], Is.EqualTo((byte)'$'));
            Assert.That(result[index++], Is.EqualTo(data.Channel));
            Assert.That(result[index++], Is.EqualTo((dataLenght & 0xFF00) >> 8));
            Assert.That(result[index++], Is.EqualTo(dataLenght & 0x00FF));
            byte[] dataArray = data.Data.ToArray();
            for (int i = 0; i < dataLenght; i++)
            {
                Assert.That(result[index++], Is.EqualTo(dataArray[i]));
            }
        }

        [Test]
        public void SendDataSync()
        {
            const int dataLenght = 45;

            var stream = new MemoryStream();
            _mockTransport.GetStream().Returns(stream);

            // Setup test object.
            var testedListener = new RtspListener(_mockTransport);
            testedListener.MessageReceived += MessageReceived;
            testedListener.DataReceived += DataReceived;

            var data = new RtspData
            {
                Channel = 12,
                Data = Enumerable.Range(0, dataLenght).Select(x => (byte)x).ToArray()
            };

            // Run
            testedListener.SendData(data.Channel, data.Data);

            var result = stream.GetBuffer();

            int index = 0;
            Assert.That(result[index++], Is.EqualTo((byte)'$'));
            Assert.That(result[index++], Is.EqualTo(data.Channel));
            Assert.That(result[index++], Is.EqualTo((dataLenght & 0xFF00) >> 8));
            Assert.That(result[index++], Is.EqualTo(dataLenght & 0x00FF));
            byte[] dataArray = data.Data.ToArray();
            for (int i = 0; i < dataLenght; i++)
            {
                Assert.That(result[index++], Is.EqualTo(dataArray[i]));
            }
        }

        [Test]
        public void SendDataTooLargeBeginEnd()
        {
            const int dataLenght = 0x10001;

            var stream = new MemoryStream();
            _mockTransport.GetStream().Returns(stream);

            // Setup test object.
            var testedListener = new RtspListener(_mockTransport);
            testedListener.MessageReceived += MessageReceived;
            testedListener.DataReceived += DataReceived;

            var data = new RtspData
            {
                Channel = 12,
                Data = new byte[dataLenght]
            };

            Assert.That(
                () => testedListener.BeginSendData(data, null, null)
                , Throws.InstanceOf<ArgumentException>());
        }

        [Test]
        public void SendDataTooLargeSync()
        {
            const int dataLenght = 0x10001;

            var stream = new MemoryStream();
            _mockTransport.GetStream().Returns(stream);

            // Setup test object.
            var testedListener = new RtspListener(_mockTransport);
            testedListener.MessageReceived += MessageReceived;
            testedListener.DataReceived += DataReceived;

            var data = new RtspData
            {
                Channel = 12,
                Data = new byte[dataLenght]
            };

            Assert.That(
                () => testedListener.SendData(data.Channel, data.Data),
                Throws.InstanceOf<ArgumentException>());
        }
    }
}