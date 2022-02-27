using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Rtsp
{
    public class UDPSocket
    {

        private readonly UdpClient dataSocket;
        private readonly UdpClient controlSocket;

        private Thread? data_read_thread;
        private Thread? control_read_thread;

        public int dataPort;
        public int controlPort;

        bool isMulticast = false;
        IPAddress? dataMulticastAddress;
        IPAddress? controlMulticastAddress;

        /// <summary>
        /// Initializes a new instance of the <see cref="UDPSocket"/> class.
        /// Creates two new UDP sockets using the start and end Port range
        /// </summary>
        public UDPSocket(int startPort, int endPort)
        {

            isMulticast = false;

            // open a pair of UDP sockets - one for data (video or audio) and one for the status channel (RTCP messages)
            dataPort = startPort;
            controlPort = startPort + 1;

            bool ok = false;
            while (ok == false && (controlPort < endPort))
            {
                // Video/Audio port must be odd and command even (next one)
                try
                {
                    dataSocket = new UdpClient(dataPort);
                    controlSocket = new UdpClient(controlPort);
                    ok = true;
                }
                catch (SocketException)
                {
                    // Fail to allocate port, try again
                    if (dataSocket != null)
                        dataSocket.Close();
                    if (controlSocket != null)
                        controlSocket.Close();

                    // try next data or control port
                    dataPort += 2;
                    controlPort += 2;
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


        /// <summary>
        /// Initializes a new instance of the <see cref="UDPSocket"/> class.
        /// Used with Multicast mode with the Multicast Address and Port
        /// </summary>
        public UDPSocket(string data_multicast_address, int data_multicast_port, string control_multicast_address, int control_multicast_port)
        {

            isMulticast = true;

            // open a pair of UDP sockets - one for data (video or audio) and one for the status channel (RTCP messages)
            this.dataPort = data_multicast_port;
            this.controlPort = control_multicast_port;

            try
            {
                IPEndPoint data_ep = new IPEndPoint(IPAddress.Any, dataPort);
                IPEndPoint control_ep = new IPEndPoint(IPAddress.Any, controlPort);

                dataMulticastAddress = IPAddress.Parse(data_multicast_address);
                controlMulticastAddress = IPAddress.Parse(control_multicast_address);

                dataSocket = new UdpClient();
                dataSocket.Client.Bind(data_ep);
                dataSocket.JoinMulticastGroup(dataMulticastAddress);

                controlSocket = new UdpClient();
                controlSocket.Client.Bind(control_ep);
                controlSocket.JoinMulticastGroup(controlMulticastAddress);


                dataSocket.Client.ReceiveBufferSize = 100 * 1024;
                dataSocket.Client.SendBufferSize = 65535; // default is 8192. Make it as large as possible for large RTP packets which are not fragmented


                controlSocket.Client.DontFragment = false;

            }
            catch (SocketException)
            {
                // Fail to allocate port, try again
                if (dataSocket != null)
                    dataSocket.Close();
                if (controlSocket != null)
                    controlSocket.Close();
                throw;
            }

            if (dataSocket == null || controlSocket == null)
            {
                throw new InvalidOperationException("UDP Forwader host was not initialized, can't continue");
            }
        }

        /// <summary>
        /// Starts this instance.
        /// </summary>
        public void Start()
        {
            if (data_read_thread != null)
            {
                throw new InvalidOperationException("Forwarder was stopped, can't restart it");
            }

            data_read_thread = new Thread(() => DoWorkerJob(dataSocket, dataPort))
            {
                Name = "DataPort " + dataPort
            };
            data_read_thread.Start();

            control_read_thread = new Thread(() => DoWorkerJob(controlSocket, controlPort))
            {
                Name = "ControlPort " + controlPort
            };
            control_read_thread.Start();
        }

        /// <summary>
        /// Stops this instance.
        /// </summary>
        public void Stop()
        {
            if (isMulticast)
            {
                // leave the multicast groups
                dataSocket.DropMulticastGroup(dataMulticastAddress);
                controlSocket.DropMulticastGroup(controlMulticastAddress);
            }
            dataSocket.Close();
            controlSocket.Close();
        }

        /// <summary>
        /// Occurs when message is received.
        /// </summary>
        public event EventHandler<RtspChunkEventArgs>? DataReceived;

        /// <summary>
        /// Raises the <see cref="E:DataReceived"/> event.
        /// </summary>
        /// <param name="rtspChunkEventArgs">The <see cref="Rtsp.RtspChunkEventArgs"/> instance containing the event data.</param>
        protected void OnDataReceived(RtspChunkEventArgs rtspChunkEventArgs)
        {
            DataReceived?.Invoke(this, rtspChunkEventArgs);
        }


        /// <summary>
        /// Does the video job.
        /// </summary>
        private void DoWorkerJob(UdpClient socket, int data_port)
        {

            IPEndPoint ipEndPoint = new IPEndPoint(IPAddress.Any, data_port);
            try
            {
                // loop until we get an exception eg the socket closed
                while (true)
                {
                    byte[] frame = socket.Receive(ref ipEndPoint);

                    // We have an RTP frame.
                    // Fire the DataReceived event with 'frame'
                    Console.WriteLine("Received RTP data on port " + data_port);

                    Rtsp.Messages.RtspChunk currentMessage = new Rtsp.Messages.RtspData();
                    // aMessage.SourcePort = ??
                    currentMessage.Data = frame;
                    ((Rtsp.Messages.RtspData)currentMessage).Channel = data_port;


                    OnDataReceived(new RtspChunkEventArgs(currentMessage));

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
            dataSocket.Send(data, data.Length, hostname, port);
        }

    }
}
