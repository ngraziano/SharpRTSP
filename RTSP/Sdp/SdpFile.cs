﻿using System;
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
        /// <param name="strictParsing">if set to <c>false</c> accept some error seen with camera.</param>
        /// <returns>Parsed SDP file</returns>
// Hard to make shorter
#pragma warning disable MA0051 // Method is too long
        public static SdpFile Read(TextReader sdpStream, bool strictParsing = false)
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
                // However the MuxLab HDMI Encoder (TX-500762) Firmware 1.0.6
                // does not include the 'Session' so supress InvalidDatarException
                if (strictParsing)
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
                    /* skip if cannot parse, some cams returns empty or invalid values for optional ones */
                    if (strictParsing)
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
                string repeat = string.Empty;
                value = GetKeyValue(sdpStream);
                if (value.Key == 'r')
                {
                    repeat = value.Value;
                    value = GetKeyValue(sdpStream);
                }
                returnValue.Timings.Add(Timing.Parse(timing, repeat));
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

            // Hack for MuxLab HDMI Encoder (TX-500762) Firmware 1.0.6
            // Skip over all other Key/Value pairs until the 'm=' key
            while (value.Key != 'm' && value.Key != '\0')
            {
                if (strictParsing)
                    throw new InvalidDataException("Unexpected key/value pair");

                value = GetKeyValue(sdpStream);

                // For old sony SNC-CS20 we need to collect all attributes
                //Attribute optional multiple
                while (value.Key == 'a')
                {
                    returnValue.Attributs.Add(Attribut.ParseInvariant(value.Value));
                    value = GetKeyValue(sdpStream);
                }
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

        public int Version { get; set; }

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
