namespace Rtsp.Messages
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Text.RegularExpressions;

    public class RtspMessage : RtspChunk
    {
        /// <summary>
        /// The regex to validate the Rtsp message.
        /// </summary>
        private static readonly Regex _rtspVersionTest = new(@"^RTSP/\d\.\d", RegexOptions.Compiled, TimeSpan.FromMilliseconds(10));

        // TODO Move in factory
        /// <summary>
        /// Create the good type of Rtsp Message from the header.
        /// </summary>
        /// <param name="aRequestLine">A request line.</param>
        /// <returns>An Rtsp message</returns>
        public static RtspMessage GetRtspMessage(string aRequestLine)
        {
            // We can't determine the message 
            if (string.IsNullOrEmpty(aRequestLine))
                return new RtspMessage();
            string[] requestParts = aRequestLine.Split(' ', 3);
            RtspMessage returnValue;
            if (requestParts.Length == 3)
            {
                // A request is : Method SP Request-URI SP RTSP-Version
                // A response is : RTSP-Version SP Status-Code SP Reason-Phrase
                // RTSP-Version = "RTSP" "/" 1*DIGIT "." 1*DIGIT
                if (_rtspVersionTest.IsMatch(requestParts[2]))
                {
                    returnValue = RtspRequest.GetRtspRequest(requestParts);
                }
                else if (_rtspVersionTest.IsMatch(requestParts[0]))
                {
                    returnValue = new RtspResponse();
                }
                else
                {
                    //  _logger.Warn(CultureInfo.InvariantCulture, "Got a strange message {0}", aRequestLine);
                    returnValue = new RtspMessage();
                }
            }
            else
            {
                // _logger.Warn(CultureInfo.InvariantCulture, "Got a strange message {0}", aRequestLine);
                returnValue = new RtspMessage();
            }
            returnValue.Command = aRequestLine;
            return returnValue;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RtspMessage"/> class.
        /// </summary>
        public RtspMessage()
        {
            Data = Array.Empty<byte>();
            Creation = DateTime.Now;
        }

        protected internal string[] commandArray = new string[] { string.Empty };

        /// <summary>
        /// Gets or sets the creation time.
        /// </summary>
        /// <value>The creation time.</value>
        public DateTime Creation { get; }

        /// <summary>
        /// Gets or sets the command of the message (first line).
        /// </summary>
        /// <value>The command.</value>
        public string Command
        {
            get => commandArray is null ? string.Empty : string.Join(' ', commandArray);
            set => commandArray = value is null ? new string[] { string.Empty } : value.Split(' ', 3);
        }

        /// <summary>
        /// Gets the Method of the message (eg OPTIONS, DESCRIBE, SETUP, PLAY).
        /// </summary>
        /// <value>The Method</value>
        public string Method => commandArray is null ? string.Empty : commandArray[0];

        /// <summary>
        /// Gets the headers of the message.
        /// </summary>
        /// <value>The headers.</value>
        public Dictionary<string, string?> Headers { get; } = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Adds one header from a string.
        /// </summary>
        /// <param name="line">The string containing header of format Header: Value.</param>
        /// <exception cref="ArgumentNullException"><paramref name="line"/> is null</exception>
        public void AddHeader(string line)
        {
            if (line is null)
            {
                throw new ArgumentNullException(nameof(line));
            }

            //spliter
            string[] elements = line.Split(':', 2);
            if (elements.Length == 2)
            {
                Headers[elements[0].Trim()] = elements[1].TrimStart();
            }
            else
            {
                // _logger.Warn(CultureInfo.InvariantCulture, "Invalid Header received : -{0}-", line);
            }
        }

        /// <summary>
        /// Gets or sets the Ccommande Seqquence number.
        /// <remarks>If the header is not define or not a valid number it return 0</remarks>
        /// </summary>
        /// <value>The sequence number.</value>
        public int CSeq
        {
            get
            {
                if (!(Headers.TryGetValue("CSeq", out string? returnStringValue) &&
                    int.TryParse(returnStringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int returnValue)))
                {
                    returnValue = 0;
                }

                return returnValue;
            }
            set
            {
                Headers["CSeq"] = value.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// Gets the session ID.
        /// </summary>
        /// <value>The session ID.</value>
        public virtual string? Session
        {
            get
            {
                if (!Headers.TryGetValue("Session", out string? value))
                    return null;

                return value;
            }
            set
            {
                Headers["Session"] = value;
            }
        }

        /// <summary>
        /// Initialises the length of the data byte array from content lenth header.
        /// </summary>
        public void InitialiseDataFromContentLength()
        {
            if (!(Headers.ContainsKey("Content-Length")
                && int.TryParse(Headers["Content-Length"], NumberStyles.Integer, CultureInfo.InvariantCulture, out int dataLength)))
            {
                dataLength = 0;
            }
            Data = new byte[dataLength];
        }

        /// <summary>
        /// Adjusts the content length header.
        /// </summary>
        public void AdjustContentLength()
        {
            if (Data?.Length > 0)
            {
                Headers["Content-Length"] = Data.Length.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                Headers.Remove("Content-Length");
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
            if (stream is null)
                throw new ArgumentNullException(nameof(stream));
            if (!stream.CanWrite)
            {
                throw
                  new ArgumentException("Stream CanWrite == false, can't send message to it", nameof(stream));
            }
            // </pex>
            Contract.EndContractBlock();

            Encoding encoder = Encoding.UTF8;
            var outputString = new StringBuilder();

            AdjustContentLength();

            // output header
            outputString.Append(Command);
            outputString.Append("\r\n");
            foreach (var item in Headers)
            {
                outputString.AppendFormat(CultureInfo.InvariantCulture, "{0}: {1}\r\n", item.Key, item.Value);
            }
            outputString.Append("\r\n");
            byte[] buffer = encoder.GetBytes(outputString.ToString());
            lock (stream)
            {
                stream.Write(buffer, 0, buffer.Length);

                // Output data
                if (Data?.Length > 0)
                    stream.Write(Data.AsSpan());
            }
            stream.Flush();
        }

        /// <summary>
        /// Create a string of the message for debug.
        /// </summary>
        public override string ToString()
        {
            var stringBuilder = new StringBuilder();

            stringBuilder.Append("Commande : ").AppendLine(Command);
            foreach (KeyValuePair<string, string?> item in Headers)
            {
                stringBuilder.Append("Header : ").Append(item.Key).Append(": ").AppendLine(item.Value);
            }

            if (Data?.Length > 0)
            {
                stringBuilder.Append("Data :-").Append(Encoding.ASCII.GetString(Data)).Append('-').AppendLine();
            }

            return stringBuilder.ToString();
        }

        /// <summary>
        /// Crée un nouvel objet qui est une copie de l'instance en cours.
        /// </summary>
        /// <returns>
        /// Nouvel objet qui est une copie de cette instance.
        /// </returns>
        public override object Clone()
        {
            RtspMessage returnValue = GetRtspMessage(Command);

            foreach (var item in Headers)
            {
                returnValue.Headers.Add(item.Key, item.Value);
            }
            returnValue.Data = Data != null ? Data.Clone() as byte[] : null;
            returnValue.SourcePort = SourcePort;

            return returnValue;
        }
    }
}
