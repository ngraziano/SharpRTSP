using System;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Text;

namespace Rtsp.Messages
{
    public class RtspTransport
    {
        public RtspTransport()
        {
            // Default value is true in RFC
            IsMulticast = true;
            LowerTransport = LowerTransportType.UDP;
            Mode = "PLAY";
        }
        /*
RFC
Transport           =    "Transport" ":"
                        1\#transport-spec
transport-spec      =    transport-protocol/profile[/lower-transport]
                        *parameter
transport-protocol  =    "RTP"
profile             =    "AVP"
lower-transport     =    "TCP" | "UDP"
parameter           =    ( "unicast" | "multicast" )
                   |    ";" "destination" [ "=" address ]
                   |    ";" "interleaved" "=" channel [ "-" channel ]
                   |    ";" "append"
                   |    ";" "ttl" "=" ttl
                   |    ";" "layers" "=" 1*DIGIT
                   |    ";" "port" "=" port [ "-" port ]
                   |    ";" "client_port" "=" port [ "-" port ]
                   |    ";" "server_port" "=" port [ "-" port ]
                   |    ";" "ssrc" "=" ssrc
                   |    ";" "mode" = <"> 1\#mode <">
ttl                 =    1*3(DIGIT)
port                =    1*5(DIGIT)
ssrc                =    8*8(HEX)
channel             =    1*3(DIGIT)
address             =    host
mode                =    <"> *Method <"> | Method

*/
        /// <summary>
        /// List of transport
        /// </summary>
        [Serializable]
        public enum TransportType
        {
            /// <summary>
            /// RTP for now
            /// </summary>
            RTP,
        }

        /// <summary>
        /// Profile type
        /// </summary>
        [Serializable]
        public enum ProfileType
        {
            /// <summary>
            /// RTP/AVP of now
            /// </summary>
            AVP,
        }

        /// <summary>
        /// Transport type.
        /// </summary>
        [Serializable]
        public enum LowerTransportType
        {
            /// <summary>
            /// UDP transport.
            /// </summary>
            UDP,
            /// <summary>
            /// TCP transport.
            /// </summary>
            TCP,
        }

        /// <summary>
        /// Gets or sets the transport.
        /// </summary>
        /// <value>The transport.</value>
        public TransportType Transport { get; set; }
        /// <summary>
        /// Gets or sets the profile.
        /// </summary>
        /// <value>The profile.</value>
        public ProfileType Profile { get; set; }
        /// <summary>
        /// Gets or sets the lower transport.
        /// </summary>
        /// <value>The lower transport.</value>
        public LowerTransportType LowerTransport { get; set; }
        /// <summary>
        /// Gets or sets a value indicating whether this instance is multicast.
        /// </summary>
        /// <value>
        /// 	<c>true</c> if this instance is multicast; otherwise, <c>false</c>.
        /// </value>
        public bool IsMulticast { get; set; }
        /// <summary>
        /// Gets or sets the destination.
        /// </summary>
        /// <value>The destination.</value>
        public string? Destination { get; set; }
        /// <summary>
        /// Gets or sets the source.
        /// </summary>
        /// <value>The source.</value>
        public string? Source { get; set; }
        /// <summary>
        /// Gets or sets the interleaved.
        /// </summary>
        /// <value>The interleaved.</value>
        public PortCouple? Interleaved { get; set; }
        /// <summary>
        /// Gets or sets a value indicating whether this instance is append.
        /// </summary>
        /// <value><c>true</c> if this instance is append; otherwise, <c>false</c>.</value>
        public bool IsAppend { get; set; }
        /// <summary>
        /// Gets or sets the TTL.
        /// </summary>
        /// <value>The TTL.</value>
        public int TTL { get; set; }
        /// <summary>
        /// Gets or sets the layers.
        /// </summary>
        /// <value>The layers.</value>
        public int Layers { get; set; }
        /// <summary>
        /// Gets or sets the port.
        /// </summary>
        /// <value>The port.</value>
        public PortCouple? Port { get; set; }
        /// <summary>
        /// Gets or sets the client port.
        /// </summary>
        /// <value>The client port.</value>
        public PortCouple? ClientPort { get; set; }
        /// <summary>
        /// Gets or sets the server port.
        /// </summary>
        /// <value>The server port.</value>
        public PortCouple? ServerPort { get; set; }
        /// <summary>
        /// Gets or sets the S SRC.
        /// </summary>
        /// <value>The S SRC.</value>
        public string? SSrc { get; set; }
        /// <summary>
        /// Gets or sets the mode.
        /// </summary>
        /// <value>The mode.</value>
        public string? Mode { get; set; }

        /// <summary>
        /// Parses the specified transport string.
        /// </summary>
        /// <param name="aTransportString">A transport string.</param>
        /// <returns>The transport class.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="aTransportString"/> is null.</exception>
        public static RtspTransport Parse(string aTransportString)
        {
            if (aTransportString is null)
                throw new ArgumentNullException(nameof(aTransportString));
            Contract.EndContractBlock();

            var returnValue = new RtspTransport();

            string[] transportPart = aTransportString.Split(';');
            string[] transportProtocolPart = transportPart[0].Split('/');

            ReadTransport(returnValue, transportProtocolPart);
            ReadProfile(returnValue, transportProtocolPart);
            ReadLowerTransport(returnValue, transportProtocolPart);

            foreach (string part in transportPart)
            {
                string[] subPart = part.Split('=');

                switch (subPart[0].ToUpperInvariant())
                {
                    case "UNICAST":
                        returnValue.IsMulticast = false;
                        break;
                    case "MULTICAST":
                        returnValue.IsMulticast = true;
                        break;
                    case "DESTINATION":
                        if (subPart.Length == 2)
                            returnValue.Destination = subPart[1];
                        break;
                    case "SOURCE":
                        if (subPart.Length == 2)
                            returnValue.Source = subPart[1];
                        break;
                    case "INTERLEAVED":
                        returnValue.IsMulticast = false;
                        if (subPart.Length < 2)
                            throw new ArgumentException("interleaved value invalid", nameof(aTransportString));
                        returnValue.Interleaved = PortCouple.Parse(subPart[1]);
                        break;
                    case "APPEND":
                        returnValue.IsAppend = true;
                        break;
                    case "TTL":
                        int ttl = 0;
                        if (subPart.Length < 2 || !int.TryParse(subPart[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out ttl))
                            throw new ArgumentException("TTL value invalid", nameof(aTransportString));
                        returnValue.TTL = ttl;
                        break;
                    case "LAYERS":
                        int layers = 0;
                        if (subPart.Length < 2 || !int.TryParse(subPart[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out layers))
                            throw new ArgumentException("Layers value invalid", nameof(aTransportString));
                        returnValue.TTL = layers;
                        break;
                    case "PORT":
                        if (subPart.Length < 2)
                            throw new ArgumentException("Port value invalid", nameof(aTransportString));
                        returnValue.Port = PortCouple.Parse(subPart[1]);
                        break;
                    case "CLIENT_PORT":
                        if (subPart.Length < 2)
                            throw new ArgumentException("client_port value invalid", nameof(aTransportString));
                        returnValue.ClientPort = PortCouple.Parse(subPart[1]);
                        break;
                    case "SERVER_PORT":
                        if (subPart.Length < 2)
                            throw new ArgumentException("server_port value invalid", nameof(aTransportString));
                        returnValue.ServerPort = PortCouple.Parse(subPart[1]);
                        break;
                    case "SSRC":
                        if (subPart.Length < 2)
                            throw new ArgumentException("ssrc value invalid", nameof(aTransportString));
                        returnValue.SSrc = subPart[1];
                        break;
                    case "MODE":
                        if (subPart.Length < 2)
                            throw new ArgumentException("mode value invalid", nameof(aTransportString));
                        returnValue.Mode = subPart[1];
                        break;
                    default:
                        // TODO log invalid part
                        break;
                }
            }
            return returnValue;
        }

        private static void ReadLowerTransport(RtspTransport returnValue, string[] transportProtocolPart)
        {
            if (transportProtocolPart.Length == 3)
            {
                if (!Enum.TryParse(transportProtocolPart[2], out LowerTransportType lowerTransport))
                    throw new ArgumentException("Lower transport type invalid", nameof(transportProtocolPart));
                returnValue.LowerTransport = lowerTransport;
            }
        }

        private static void ReadProfile(RtspTransport returnValue, string[] transportProtocolPart)
        {
            if (transportProtocolPart.Length < 2 || !Enum.TryParse(transportProtocolPart[1], out ProfileType profile))
                throw new ArgumentException("Transport profile type invalid", nameof(transportProtocolPart));
            returnValue.Profile = profile;
        }

        private static void ReadTransport(RtspTransport returnValue, string[] transportProtocolPart)
        {
            if (!Enum.TryParse(transportProtocolPart[0], out TransportType transport))
                throw new ArgumentException("Transport type invalid", nameof(transportProtocolPart));
            returnValue.Transport = transport;
        }

        /// <summary>
        /// Returns a <see cref="System.String"/> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="System.String"/> that represents this instance.
        /// </returns>
        public override string ToString()
        {
            var transportString = new StringBuilder();
            transportString.Append(Transport)
                .Append('/')
                .Append(Profile)
                .Append('/')
                .Append(LowerTransport);
            if (LowerTransport == LowerTransportType.TCP)
            {
                transportString.Append(";unicast");
            }
            if (LowerTransport == LowerTransportType.UDP)
            {
                transportString.Append(';');
                transportString.Append(IsMulticast ? "multicast" : "unicast");
            }
            if (Destination is not null)
            {
                transportString.Append(";destination=").Append(Destination);
            }
            if (Source is not null)
            {
                transportString.Append(";source=").Append(Source);
            }
            if (Interleaved is not null)
            {
                transportString.Append(";interleaved=").Append(Interleaved);
            }
            if (IsAppend)
            {
                transportString.Append(";append");
            }
            if (TTL > 0)
            {
                transportString.Append(";ttl=").Append(TTL);
            }
            if (Layers > 0)
            {
                transportString.Append(";layers=").Append(Layers);
            }
            if (Port is not null)
            {
                transportString.Append(";port=").Append(Port);
            }
            if (ClientPort is not null)
            {
                transportString.Append(";client_port=").Append(ClientPort);
            }
            if (ServerPort is not null)
            {
                transportString.Append(";server_port=").Append(ServerPort);
            }
            if (SSrc is not null)
            {
                transportString.Append(";ssrc=").Append(SSrc);
            }
            if (Mode != null && !string.Equals(Mode, "PLAY", StringComparison.Ordinal))
            {
                transportString.Append(";mode=").Append(Mode);
            }
            return transportString.ToString();
        }
    }
}
