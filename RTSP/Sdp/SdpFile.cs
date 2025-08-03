using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Rtsp.Sdp
{
    public class SdpFile
    {
        private static KeyValuePair<char, string> GetKeyValue(TextReader sdpStream)
        {
            string? line = sdpStream.ReadLine();

            // end of file ?
            if (string.IsNullOrEmpty(line))
                return new('\0', string.Empty);

            string[] parts = line.Split('=', 2);
            if (parts.Length != 2)
                throw new InvalidDataException();
            if (parts[0].Length != 1)
                throw new InvalidDataException();

            return new(parts[0][0], parts[1]);
        }

        /// <summary>
        /// Reads the specified SDP stream.
        /// As define in RFC 4566
        /// </summary>
        /// <param name="sdpStream">The SDP stream.</param>
        /// <param name="strictParsing">if set to <see langword="false"/> accept some error seen with camera.</param>
        /// <returns>Parsed SDP file</returns>
        [Obsolete("Use ReadStrict(TextReader) or ReadLoose(TextReader) instead")]
        public static SdpFile Read(TextReader sdpStream, bool strictParsing = false)
        {
            if (strictParsing)
            {
                return ReadStrict(sdpStream);
            }

            return ReadLoose(sdpStream);
        }

        /// <summary>
        /// Reads the specified SDP stream.
        /// Respect the RFC 4566, may not work with old camera.
        /// </summary>
        /// <param name="sdpStream">Sdp stream text</param>
        /// <returns>Parsed SDP file</returns>
        /// <exception cref="InvalidDataException">Throw if encouter invalid data</exception>
// Hard to make shorter
#pragma warning disable MA0051 // Method is too long
        public static SdpFile ReadStrict(TextReader sdpStream)
#pragma warning restore MA0051 // Method is too long
        {
            SdpFile returnValue = new();
            var value = GetKeyValue(sdpStream);

            // Version mandatory
            if (value.Key == 'v')
            {
                returnValue.Version = int.Parse(value.Value, CultureInfo.InvariantCulture);
                value = GetKeyValue(sdpStream);
            }
            else
            {
                throw new InvalidDataException("version missing");
            }

            // Origin mandatory
            if (value.Key == 'o')
            {
                returnValue.Origin = Origin.Parse(value.Value);
                value = GetKeyValue(sdpStream);
            }
            else
            {
                throw new InvalidDataException("origin missing");
            }

            // Session mandatory.

            if (value.Key == 's')
            {
                returnValue.Session = value.Value;
                value = GetKeyValue(sdpStream);
            }
            else
            {
                throw new InvalidDataException("session missing");
            }

            // Session Information optional
            if (value.Key == 'i')
            {
                returnValue.SessionInformation = value.Value;
                value = GetKeyValue(sdpStream);
            }

            // Uri optional
            if (value.Key == 'u')
            {
                try
                {
                    returnValue.Url = new Uri(value.Value);
                }
                catch (UriFormatException err)
                {
                    throw new InvalidDataException($"uri value invalid {value.Value}", err);
                }
                value = GetKeyValue(sdpStream);
            }

            // Email optional
            if (value.Key == 'e')
            {
                returnValue.Email = value.Value;
                value = GetKeyValue(sdpStream);
            }

            // Phone optional
            if (value.Key == 'p')
            {
                returnValue.Phone = value.Value;
                value = GetKeyValue(sdpStream);
            }

            // Connection optional
            if (value.Key == 'c')
            {
                returnValue.Connection = Connection.Parse(value.Value);
                value = GetKeyValue(sdpStream);
            }

            // bandwidth optional
            if (value.Key == 'b')
            {
                returnValue.Bandwidth = Bandwidth.Parse(value.Value);
                value = GetKeyValue(sdpStream);
            }

            // Timing mandatory
            while (value.Key == 't')
            {
                string timing = value.Value;
                value = GetKeyValue(sdpStream);
                if (value.Key == 'r')
                {
                    // TODO parse repeat
                    value = GetKeyValue(sdpStream);
                }
                returnValue.Timings.Add(Timing.Parse(timing));
            }

            // timezone optional
            if (value.Key == 'z')
            {
                returnValue.TimeZone = SdpTimeZone.ParseInvariant(value.Value);
                value = GetKeyValue(sdpStream);
            }

            // encryption key optional
            if (value.Key == 'k')
            {
                // Obsolete in RFC 8866 ignored
                value = GetKeyValue(sdpStream);
            }

            //Attribute optional multiple
            while (value.Key == 'a')
            {
                returnValue.Attributs.Add(Attribut.ParseInvariant(value.Value));
                value = GetKeyValue(sdpStream);
            }

            if (value.Key != 'm' && value.Key != '\0')
            {
                throw new InvalidDataException("Unexpected key/value pair");
            }

            // Media
            while (value.Key == 'm')
            {
                Media newMedia = ReadMedia(sdpStream, ref value);
                returnValue.Medias.Add(newMedia);
            }

            return returnValue;
        }

        private static Media ReadMedia(TextReader sdpStream, ref KeyValuePair<char, string> value)
        {
            Media returnValue = new(value.Value);
            value = GetKeyValue(sdpStream);

            // Media title
            if (value.Key == 'i')
            {
                value = GetKeyValue(sdpStream);
            }

            // Connexion optional and multiple in media
            while (value.Key == 'c')
            {
                returnValue.Connections.Add(Connection.Parse(value.Value));
                value = GetKeyValue(sdpStream);
            }

            // bandwidth optional multiple value possible
            while (value.Key == 'b')
            {
                returnValue.Bandwidths.Add(Bandwidth.Parse(value.Value));
                value = GetKeyValue(sdpStream);
            }

            // encryption key optional
            if (value.Key == 'k')
            {
                // Obsolete in RFC 8866 ignored
                value = GetKeyValue(sdpStream);
            }

            //Attribut optional multiple
            while (value.Key == 'a')
            {
                returnValue.Attributs.Add(Attribut.ParseInvariant(value.Value));
                value = GetKeyValue(sdpStream);
            }

            return returnValue;
        }

        /// <summary>
        /// Reads the specified SDP stream.
        /// </summary>
        /// <remarks>
        /// In CCTV context, a lot of camera do not respect the RFC 4566.
        /// Read without checking the content, some attribute may be empty after read.
        /// </remarks>
        /// <param name="sdpStream">Sdp stream text</param>
        /// <returns>SdpFile object with all the value that can be extracted</returns>
        public static SdpFile ReadLoose(TextReader sdpStream)
        {
            SdpFile returnValue = new();
            KeyValuePair<char, string> value;

            while ((value = GetKeyValue(sdpStream)).Key != '\0')
            {
                switch (value.Key)
                {
                    case 'v':
                        returnValue.Version = int.Parse(value.Value, CultureInfo.InvariantCulture);
                        break;
                    case 'o':
                        returnValue.Origin = Origin.ParseLoose(value.Value);
                        break;
                    case 's':
                        returnValue.Session = value.Value;
                        break;
                    case 'i':
                        returnValue.SessionInformation = value.Value;
                        break;
                    case 'u':
                        try
                        {
                            returnValue.Url = new Uri(value.Value);
                        }
                        catch (UriFormatException)
                        {
                            // ignore invalid uri, happen with some camera
                        }
                        break;
                    case 'e':
                        returnValue.Email = value.Value;
                        break;
                    case 'p':
                        returnValue.Phone = value.Value;
                        break;
                    case 'c':
                        try { returnValue.Connection = Connection.Parse(value.Value); }
                        catch (FormatException)
                        { }
                        catch (NotSupportedException)
                        { }
                        break;
                    case 'b':
                        try { returnValue.Bandwidth = Bandwidth.Parse(value.Value); }
                        catch (ArgumentOutOfRangeException)
                        { }
                        break;
                    case 't':
                        try { returnValue.Timings.Add(Timing.Parse(value.Value)); }
                        catch (ArgumentException)
                        { }
                        break;
                    case 'r':
                        // TODO parse repeat
                        break;
                    case 'z':
                        returnValue.TimeZone = SdpTimeZone.ParseInvariant(value.Value);
                        break;
                    case 'a':
                        returnValue.Attributs.Add(Attribut.ParseInvariant(value.Value));
                        break;
                    case 'm':
                        while (value.Key == 'm')
                        {
                            Media newMedia = ReadMediaLoose(sdpStream, ref value);
                            returnValue.Medias.Add(newMedia);
                        }
                        break;
                }
            }

            return returnValue;
        }

        private static Media ReadMediaLoose(TextReader sdpStream, ref KeyValuePair<char, string> value)
        {
            Media returnValue = new(value.Value);

            while (true)
            {
                value = GetKeyValue(sdpStream);

                // Media title
                if (value.Key == 'i')
                {
                }

                // Connexion optional and multiple in media
                else if (value.Key == 'c')
                {
                    returnValue.Connections.Add(Connection.Parse(value.Value));
                }

                // bandwidth optional multiple value possible
                else if (value.Key == 'b')
                {
                    returnValue.Bandwidths.Add(Bandwidth.Parse(value.Value));
                }

                // encryption key optional
                else if (value.Key == 'k')
                {
                    // Obsolete in RFC 8866 ignored
                }

                //Attribut optional multiple
                else if (value.Key == 'a')
                {
                    returnValue.Attributs.Add(Attribut.ParseInvariant(value.Value));
                }
                else
                {
                    break;
                }
            }

            return returnValue;
        }

        public const int VERSION_NOT_SET = -1;

        public int Version { get; set; } = VERSION_NOT_SET;

        public Origin? Origin { get; set; }

        public string? Session { get; set; }

        public string? SessionInformation { get; set; }

        public Uri? Url { get; set; }

        public string? Email { get; set; }

        public string? Phone { get; set; }

        public Connection? Connection { get; set; }

        public Bandwidth? Bandwidth { get; set; }

        public IList<Timing> Timings { get; } = [];

        public SdpTimeZone? TimeZone { get; set; }

        public IList<Attribut> Attributs { get; } = [];

        public IList<Media> Medias { get; } = [];
    }
}