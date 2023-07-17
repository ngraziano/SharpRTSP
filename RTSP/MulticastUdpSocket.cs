using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Rtsp
{
    public class MulticastUDPSocket : UDPSocket
    {
        private readonly IPAddress dataMulticastAddress;
        private readonly IPAddress controlMulticastAddress;

        /// <summary>
        /// Initializes a new instance of the <see cref="UDPSocket"/> class.
        /// Used with Multicast mode with the Multicast Address and Port
        /// </summary>
        public MulticastUDPSocket(string data_multicast_address, int data_multicast_port, string control_multicast_address, int control_multicast_port)
           : base(new UdpClient(), new UdpClient())
        {


            // open a pair of UDP sockets - one for data (video or audio) and one for the status channel (RTCP messages)
            DataPort = data_multicast_port;
            ControlPort = control_multicast_port;

            try
            {
                var dataEndPoint = new IPEndPoint(IPAddress.Any, DataPort);
                var controlEndPoint = new IPEndPoint(IPAddress.Any, ControlPort);

                dataMulticastAddress = IPAddress.Parse(data_multicast_address);
                controlMulticastAddress = IPAddress.Parse(control_multicast_address);

                dataSocket.Client.Bind(dataEndPoint);
                dataSocket.JoinMulticastGroup(dataMulticastAddress);

                controlSocket.Client.Bind(controlEndPoint);
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
        /// Stops this instance.
        /// </summary>
        public override void Stop()
        {
            // leave the multicast groups
            dataSocket.DropMulticastGroup(dataMulticastAddress);
            controlSocket.DropMulticastGroup(controlMulticastAddress);
            base.Stop();
        }
    }
}
