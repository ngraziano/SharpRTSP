using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics.Contracts;
using System.Globalization;

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
        public string Destination { get; set; }
        /// <summary>
        /// Gets or sets the source.
        /// </summary>
        /// <value>The source.</value>
        public string Source { get; set; }
        /// <summary>
        /// Gets or sets the interleaved.
        /// </summary>
        /// <value>The interleaved.</value>
        public int[] Interleaved { get; set; }
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
        public int[] Port { get; set; }
        /// <summary>
        /// Gets or sets the client port.
        /// </summary>
        /// <value>The client port.</value>
        public int[] ClientPort { get; set; }
        /// <summary>
        /// Gets or sets the server port.
        /// </summary>
        /// <value>The server port.</value>
        public int[] ServerPort { get; set; }
        /// <summary>
        /// Gets or sets the S SRC.
        /// </summary>
        /// <value>The S SRC.</value>
        public string SSrc { get; set; }
        /// <summary>
        /// Gets or sets the mode.
        /// </summary>
        /// <value>The mode.</value>
        public string Mode { get; set; }


        /// <summary>
        /// Parses the int values of port.
        /// </summary>
        /// <param name="aStringValue">A string value.</param>
        /// <returns>an array containg port</returns>
        private static int[] ParseIntValues(string aStringValue)
        {
            Contract.Requires(!string.IsNullOrEmpty(aStringValue));

            string[] values;
            int[] returnValues = new int[2];
            returnValues[0] = 0;
            returnValues[1] = 0;

            values = aStringValue.Split('-');
            int.TryParse(values[0], out returnValues[0]);
            if (values.Length > 1)
                int.TryParse(values[1], out returnValues[1]);
            return returnValues;

        }

        /// <summary>
        /// Parses the specified transport string.
        /// </summary>
        /// <param name="aTransportString">A transport string.</param>
        /// <returns>The transport class.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="aTransportString"/> is null.</exception>
        public static RtspTransport Parse(string aTransportString)
        {
            if (aTransportString == null)
                throw new ArgumentNullException("aTransportString");
            Contract.EndContractBlock();

            RtspTransport returnValue = new RtspTransport();

            string[] transportPart = aTransportString.Split(';');
            string[] transportProtocolPart = transportPart[0].Split('/');

            TransportType transport;
            if (!Enum.TryParse<TransportType>(transportProtocolPart[0], out transport))
                throw new ArgumentException("Transport type invalid", "aTransportString");
            returnValue.Transport = transport;

            ProfileType profile;
            if (transportPart.Length < 2 || !Enum.TryParse<ProfileType>(transportProtocolPart[1], out profile))
                throw new ArgumentException("Transport profile type invalid", "aTransportString");
            returnValue.Profile = profile;

            if (transportProtocolPart.Length == 3)
            {
                LowerTransportType lowerTransport;
                if (!Enum.TryParse<LowerTransportType>(transportProtocolPart[2], out lowerTransport))
                    throw new ArgumentException("Lower transport type invalid", "aTransportString");
                returnValue.LowerTransport = lowerTransport;
            }

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
                        if (subPart.Length < 2)
                            throw new ArgumentException("interleaved value invalid", "aTransportString");

                        returnValue.Interleaved = ParseIntValues(subPart[1]);
                        break;
                    case "APPEND":
                        returnValue.IsAppend = true;
                        break;
                    case "TTL":
                        int ttl = 0;
                        if (subPart.Length < 2 || !int.TryParse(subPart[1], out ttl))
                            throw new ArgumentException("TTL value invalid", "aTransportString");
                        returnValue.TTL = ttl;
                        break;
                    case "LAYERS":
                        int layers = 0;
                        if (subPart.Length < 2 || !int.TryParse(subPart[1], out layers))
                            throw new ArgumentException("Layers value invalid", "aTransportString");
                        returnValue.TTL = layers;
                        break;
                    case "PORT":
                        if (subPart.Length < 2)
                            throw new ArgumentException("Port value invalid", "aTransportString");
                        returnValue.Port = ParseIntValues(subPart[1]);
                        break;
                    case "CLIENT_PORT":
                        if (subPart.Length < 2)
                            throw new ArgumentException("client_port value invalid", "aTransportString");
                        returnValue.ClientPort = ParseIntValues(subPart[1]);
                        break;
                    case "SERVER_PORT":
                        if (subPart.Length < 2)
                            throw new ArgumentException("server_port value invalid", "aTransportString");
                        returnValue.ServerPort = ParseIntValues(subPart[1]);
                        break;
                    case "SSRC":
                        if (subPart.Length < 2)
                            throw new ArgumentException("ssrc value invalid", "aTransportString");
                        returnValue.SSrc = subPart[1];
                        break;
                    case "MODE":
                        if (subPart.Length < 2)
                            throw new ArgumentException("mode value invalid", "aTransportString");
                        returnValue.SSrc = subPart[1];
                        break;
                    default:
                        // TODO log invalid part
                        break;
                }
            }
            return returnValue;
        }

        /// <summary>
        /// Ports to string.
        /// </summary>
        /// <param name="ports">The ports.</param>
        /// <returns>a string representing the ports</returns>
        private static string PortsToString(int[] ports)
        {
            Contract.Requires(ports != null);
            Contract.Requires(ports.Length > 0);

            if (ports.Length < 2 || ports[1] == 0)
                return ports[0].ToString(CultureInfo.InvariantCulture);
            else
                return ports[0].ToString(CultureInfo.InvariantCulture) + "-" + ports[1].ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Returns a <see cref="System.String"/> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="System.String"/> that represents this instance.
        /// </returns>
        public override string ToString()
        {
            StringBuilder transportString = new StringBuilder();
            transportString.Append(Transport.ToString());
            transportString.Append('/');
            transportString.Append(Profile.ToString());
            transportString.Append('/');
            transportString.Append(LowerTransport.ToString());
            transportString.Append(';');
            transportString.Append(IsMulticast ? "multicast" : "unicast");
            if (Destination != null)
            {
                transportString.Append(";destination=");
                transportString.Append(Destination);
            }
            if (Source != null)
            {
                transportString.Append(";source=");
                transportString.Append(Source);
            }
            if (Interleaved != null && Interleaved.Length > 0)
            {
                transportString.Append(";interleaved=");
                transportString.Append(PortsToString(Interleaved));
            }
            if (IsAppend)
            {
                transportString.Append(";append");
            }
            if (TTL > 0)
            {
                transportString.Append(";ttl=");
                transportString.Append(TTL);
            }
            if (Layers > 0)
            {
                transportString.Append(";layers=");
                transportString.Append(Layers);
            }
            if (Port != null && Port.Length > 0)
            {
                transportString.Append(";port=");
                transportString.Append(PortsToString(Port));
            }
            if (ClientPort != null && ClientPort.Length > 0)
            {
                transportString.Append(";client_port=");
                transportString.Append(PortsToString(ClientPort));
            }
            if (ServerPort != null && ServerPort.Length > 0)
            {
                transportString.Append(";server_port=");
                transportString.Append(PortsToString(ServerPort));
            }
            if (SSrc != null)
            {
                transportString.Append(";ssrc=");
                transportString.Append(SSrc);
            }
            if (Mode != null && Mode != "PLAY")
            {
                transportString.Append(";mode=");
                transportString.Append(SSrc);
            }
            return transportString.ToString();
        }

    }
}
