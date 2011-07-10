using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;
using System.Diagnostics.Contracts;

namespace RTSP.Messages
{
    public class RTSPMessage : RTSPChunk
    {
        private static NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

        /// <summary>
        /// The regex to validate the RTSP message.
        /// </summary>
        private static readonly Regex _rtspVersionTest = new Regex(@"^RTSP/\d\.\d", RegexOptions.Compiled);
        /// <summary>
        /// Create the good type of RTSP Message from the header.
        /// </summary>
        /// <param name="aRequestLine">A request line.</param>
        /// <returns>An RTSP message</returns>
        public static RTSPMessage GetRTSPMessage(string aRequestLine)
        {
            // We can't determine the message 
            if (string.IsNullOrEmpty(aRequestLine))
                return new RTSPMessage();
            string[] requestParts = aRequestLine.Split(new char[] { ' ' }, 3);
            RTSPMessage returnValue;
            if (requestParts.Length == 3)
            {
                // A request is : Method SP Request-URI SP RTSP-Version
                // A response is : RTSP-Version SP Status-Code SP Reason-Phrase
                // RTSP-Version = "RTSP" "/" 1*DIGIT "." 1*DIGIT
                if (_rtspVersionTest.IsMatch(requestParts[2]))
                    returnValue = RTSPRequest.GetRTSPRequest(requestParts);
                else if (_rtspVersionTest.IsMatch(requestParts[0]))
                    returnValue = new RTSPResponse();
                else
                {
                    _logger.Warn("Got a strange message {0}", aRequestLine);
                    returnValue = new RTSPMessage();
                }
            }
            else
            {
                _logger.Warn("Got a strange message {0}", aRequestLine);
                returnValue = new RTSPMessage();
            }
            returnValue.Command = aRequestLine;
            return returnValue;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RTSPMessage"/> class.
        /// </summary>
        public RTSPMessage()
        {
            Data = new byte[0];
            Creation = DateTime.Now;
        }

        Dictionary<string, string> _headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        protected string[] _command;

        /// <summary>
        /// Gets or sets the creation time.
        /// </summary>
        /// <value>The creation time.</value>
        public DateTime Creation { get; private set; }

        /// <summary>
        /// Gets or sets the command of the message (first line).
        /// </summary>
        /// <value>The command.</value>
        public string Command
        {
            get
            {
                if (_command == null)
                    return string.Empty;
                return string.Join(" ", _command);
            }
            set
            {
                if (value == null)
                    _command = new string[] { String.Empty };
                else
                    _command = value.Split(new char[] {' '}, 3);
            }
        }



        /// <summary>
        /// Gets the headers of the message.
        /// </summary>
        /// <value>The headers.</value>
        public Dictionary<string, string> Headers
        {
            get
            {
                return _headers;
            }
        }

        /// <summary>
        /// Adds one header from a string.
        /// </summary>
        /// <param name="line">The string containing header of format Header: Value.</param>
        /// <exception cref="ArgumentNullException"><paramref name="line"/> is null</exception>
        public void AddHeader(string line)
        {
            if (line == (string)null)
                throw new ArgumentNullException("line");

            //spliter
            string[] elements = line.Split(new char[] { ':' }, 2);
            if (elements.Length == 2)
            {
                _headers[elements[0].Trim()] = elements[1].TrimStart();
            }
            else
            {
                _logger.Warn("Invalid Header received : -{0}-", line);
            }
        }

        /// <summary>
        /// Gets or sets the Ccommande Seqquence number.
        /// <remarks>If the header is not define it return 0</remarks>
        /// </summary>
        /// <value>The seqquence number.</value>
        public int CSeq
        {
            get
            {
                string returnStringValue;
                int returnValue = 0;
                if (_headers.TryGetValue("CSeq", out returnStringValue))
                {
                    int.TryParse(returnStringValue, out returnValue);
                }
                else
                    returnValue = 0;

                return returnValue;
            }
            set
            {
                _headers["CSeq"] = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// Gets the session ID.
        /// </summary>
        /// <value>The session ID.</value>
        public string Session
        {
            get
            {
                if (!_headers.ContainsKey("Session"))
                    return null;

                return _headers["Session"].Split(';')[0];
            }
            set
            {
                _headers["Session"] = value + "; Timeout=" + Timeout.ToString();
            }
        }

        /// <summary>
        /// Gets the timeout in second.
        /// <remarks>The default timeout is 60.</remarks>
        /// </summary>
        /// <value>The timeout.</value>
        public int Timeout
        {
            get
            {
                int returnValue = 60;
                if (_headers.ContainsKey("Session"))
                {
                    string[] parts = _headers["Session"].Split(';');
                    if (parts.Length > 1)
                    {
                        string[] subParts = parts[1].Split('=');
                        if (subParts.Length > 1 &&
                            subParts[0].ToLowerInvariant() == "timeout")
                            if (!int.TryParse(subParts[1], out returnValue))
                                returnValue = 60;
                    }
                }
                return returnValue;
            }
        }


        /// <summary>
        /// Initialises the length of the data byte array from content lenth header.
        /// </summary>
        public void InitialiseDataFromContentLength()
        {
            int dataLength = 0;
            if (_headers.ContainsKey("Content-Length"))
            {
                int.TryParse(_headers["Content-Length"], out dataLength);
            }
            this.Data = new byte[dataLength];
        }

        /// <summary>
        /// Adjusts the content length header.
        /// </summary>
        public void AdjustContentLength()
        {
            if (Data.Length > 0)
            {
                _headers["Content-Length"] = Data.Length.ToString();
            }
            else
            {
                _headers.Remove("Content-Length");
            }
        }

        /// <summary>
        /// Sends to the message to a stream.
        /// </summary>
        /// <param name="stream">The stream.</param>
        /// <exception cref="ArgumentNullException"><paramref name="stream"/> is empty</exception>
        /// <exception cref="ArgumentException"><paramref name="stream"/> can't be written.</exception>
        public void SendTo(Stream stream)
        {
            // <pex>
            if (stream == null)
                throw new ArgumentNullException("stream");
            if (!stream.CanWrite)
                throw
                  new ArgumentException("Stream CanWrite == false, can't send message to it", "stream");
            // </pex>
            Contract.EndContractBlock();

            Encoding encoder = ASCIIEncoding.UTF8;
            StringBuilder outputString = new StringBuilder();

            AdjustContentLength();

            // output header
            outputString.Append(Command);
            outputString.Append("\r\n");
            foreach (KeyValuePair<string, string> item in _headers)
            {
                outputString.AppendFormat("{0}: {1}\r\n", item.Key, item.Value);
            }
            outputString.Append("\r\n");
            byte[] buffer = encoder.GetBytes(outputString.ToString());
            stream.Write(buffer, 0, buffer.Length);

            // Output data
            if (Data.Length > 0)
                stream.Write(Data, 0, Data.Length);

            stream.Flush();
        }



        /// <summary>
        /// Logs the message.
        /// </summary>
        /// <param name="aLevel">A log level.</param>
        public override void LogMessage(NLog.LogLevel aLevel)
        {
            // Default value to debug
            if (aLevel == null)
                aLevel = NLog.LogLevel.Debug;
            // if the level is not logged directly return
            if (!_logger.IsEnabled(aLevel))
                return;

            _logger.Log(aLevel, "Commande : {0}", Command);
            foreach (KeyValuePair<string, string> item in _headers)
            {
                _logger.Log(aLevel, "Header : {0}: {1}", item.Key, item.Value);
            }

            if (Data.Length > 0)
            {
                _logger.Log(aLevel, "Data :-{0}-", ASCIIEncoding.ASCII.GetString(Data));
            }
        }

        /// <summary>
        /// Crée un nouvel objet qui est une copie de l'instance en cours.
        /// </summary>
        /// <returns>
        /// Nouvel objet qui est une copie de cette instance.
        /// </returns>
        public override object Clone()
        {
            RTSPMessage returnValue = GetRTSPMessage(this.Command);

            foreach (var item in this.Headers)
            {
                returnValue.Headers.Add(item.Key.Clone() as string, item.Value.Clone() as string);
            }
            returnValue.Data = this.Data.Clone() as byte[];
            returnValue.SourcePort = this.SourcePort;

            return returnValue;
        }

    }
}
