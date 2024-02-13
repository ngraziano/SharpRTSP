using Rtsp.Messages;
using System;
using System.Buffers;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Rtsp
{
    public class UDPSocket
    {
        protected readonly UdpClient dataSocket;
        protected readonly UdpClient controlSocket;

        private Task? _dataReadTask;
        private Task? _controlReadTask;

        public int DataPort { get; protected set; }
        public int ControlPort { get; protected set; }

        public PortCouple Ports => new(DataPort, ControlPort);

        /// <summary>
        /// Initializes a new instance of the <see cref="UDPSocket"/> class.
        /// Creates two new UDP sockets using the start and end Port range
        /// </summary>
        public UDPSocket(int startPort, int endPort)
        {
            // open a pair of UDP sockets - one for data (video or audio) and one for the status channel (RTCP messages)
            DataPort = startPort;
            ControlPort = startPort + 1;

            bool ok = false;
            while (!ok && (ControlPort < endPort))
            {
                // Video/Audio port must be odd and command even (next one)
                try
                {
                    dataSocket = new UdpClient(DataPort);
                    controlSocket = new UdpClient(ControlPort);
                    ok = true;
                }
                catch (SocketException)
                {
                    // Fail to allocate port, try again
                    dataSocket?.Close();
                    controlSocket?.Close();

                    // try next data or control port
                    DataPort += 2;
                    ControlPort += 2;
                }

                if (ok)
                {
                    dataSocket!.Client.ReceiveBufferSize = 100 * 1024;
                    dataSocket!.Client.SendBufferSize = 65535; // default is 8192. Make it as large as possible for large RTP packets which are not fragmented

                    controlSocket!.Client.DontFragment = false;
                }
            }

            if (dataSocket == null || controlSocket == null)
            {
                throw new InvalidOperationException("UDP Forwader host was not initialized, can't continue");
            }
        }

        protected UDPSocket(UdpClient dataSocket, UdpClient controlSocket)
        {
            this.dataSocket = dataSocket;
            this.controlSocket = controlSocket;
        }

        /// <summary>
        /// Starts this instance.
        /// </summary>
        public void Start()
        {
            if (_dataReadTask != null)
            {
                throw new InvalidOperationException("Forwarder was stopped, can't restart it");
            }

            _dataReadTask = Task.Factory.StartNew(async () => await DoWorkerJobAsync(dataSocket, OnDataReceived, DataPort), TaskCreationOptions.LongRunning);
            _controlReadTask = Task.Factory.StartNew(async () => await DoWorkerJobAsync(controlSocket, OnControlReceived, ControlPort), TaskCreationOptions.LongRunning);
        }

        /// <summary>
        /// Stops this instance.
        /// </summary>
        public virtual void Stop()
        {
            dataSocket.Close();
            controlSocket.Close();
        }

        /// <summary>
        /// Occurs when data is received.
        /// </summary>
        public event EventHandler<RtspDataEventArgs>? DataReceived;

        /// <summary>
        /// Raises the <see cref="E:DataReceived"/> event.
        /// </summary>
        protected void OnDataReceived(RtspDataEventArgs rtspDataEventArgs)
        {
            DataReceived?.Invoke(this, rtspDataEventArgs);
        }

        /// <summary>
        /// Occurs when control is received.
        /// </summary>
        public event EventHandler<RtspDataEventArgs>? ControlReceived;

        /// <summary>
        /// Raises the <see cref="E:ControlReceived"/> event.
        /// </summary>
        protected void OnControlReceived(RtspDataEventArgs rtspDataEventArgs)
        {
            ControlReceived?.Invoke(this, rtspDataEventArgs);
        }

        /// <summary>
        /// Does the video job.
        /// </summary>
        private static async Task DoWorkerJobAsync(UdpClient client, Action<RtspDataEventArgs> handler, int port)
        {
            try
            {
                // to be compatible with netstandard2.0 we can't use the memory directly for the receive call 
                byte[] buffer = new byte[65536];
                // loop until we get an exception eg the socket closed
                while (true)
                {
#if NET7_0_OR_GREATER
                    var size = await client.Client.ReceiveAsync(buffer).ConfigureAwait(false);
#else
                    var size = client.Client.Receive(buffer);
#endif
                    var bufferOwner = MemoryPool<byte>.Shared.Rent(size);
                    buffer.AsSpan()[..size].CopyTo(bufferOwner.Memory.Span);

                    handler(new RtspDataEventArgs(new RtspData(bufferOwner, size)
                    {
                        Channel = port,
                    }));
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SocketException)
            {
            }
        }

        /// <summary>
        /// Write to the RTP Data Port
        /// </summary>
        public void WriteToDataPort(byte[] data, string hostname, int port)
        {
            dataSocket.Send(data, data.Length, hostname, port);
        }

        /// <summary>
        /// Write to the RTP Control Port
        /// </summary>
        public void WriteToControlPort(byte[] data, string hostname, int port)
        {
            controlSocket.Send(data, data.Length, hostname, port);
        }
    }
}
