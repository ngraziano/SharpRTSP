using System.Net.Sockets;

namespace Rtsp.Utils;
internal static class NetworkClientFactory
{
    private const int TcpReceiveBufferDefaultSize = 64 * 1024;  // 64 kb
    private const int UdpReceiveBufferDefaultSize = 128 * 1024; // 128 kb
    private const int SIO_UDP_CONNRESET = -1744830452;
    private static readonly byte[] EmptyOptionInValue = [0, 0, 0, 0];

    public static Socket CreateTcpClient() => new(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp)
    {
        ReceiveBufferSize = TcpReceiveBufferDefaultSize,
        DualMode = true,
        NoDelay = true,
    };
    public static Socket CreateUdpClient()
    {
        Socket socket = new(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp)
        {
            ReceiveBufferSize = UdpReceiveBufferDefaultSize,
            DualMode = true,
        };
        socket.IOControl((IOControlCode)SIO_UDP_CONNRESET, EmptyOptionInValue, null);
        return socket;
    }
}
