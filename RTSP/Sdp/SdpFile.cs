using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace Rtsp.Sdp
{
    public class SdpFile
    {
        private static KeyValuePair<string,string> GetKeyValue(TextReader sdpStream)
        {
            string line = sdpStream.ReadLine();
            string[] parts = line.Split(new char[]{'='},2);
            if(parts.Length != 2)
                throw new InvalidDataException();
            if(parts[0].Length != 1)
                throw new InvalidDataException();
            if(parts[1].Length != 1)
                throw new InvalidDataException();


            KeyValuePair<string, string> value = new KeyValuePair<string, string>(parts[0], parts[1]);
            return value;
        }

        public static SdpFile Read(TextReader sdpStream)
        {
            SdpFile returnValue = new SdpFile();
            KeyValuePair<string, string> value = GetKeyValue(sdpStream);

            // Version mandatory
            if (value.Key == "v")
            {
                returnValue.Version = int.Parse(value.Value);
            }
            else
                throw new InvalidDataException();
            value = GetKeyValue(sdpStream);
            
            // Origin mandatory
            if (value.Key == "o")
            {
                returnValue.Origin = new Origin(value.Value);
            }
            else
                throw new InvalidDataException();
            value = GetKeyValue(sdpStream);

            // Session mandatory
            if (value.Key == "s")
            {
                returnValue.Session = value.Value;
            }
            else
                throw new InvalidDataException();
            value = GetKeyValue(sdpStream);

            // Session optional
            if (value.Key == "i")
            {
                returnValue.SessionInformation = value.Value;
                value = GetKeyValue(sdpStream);
            }

            // Uri optional
            if (value.Key == "u")
            {
                returnValue.Url = new Uri(value.Value);
                value = GetKeyValue(sdpStream);
            }

            // Eamil optional
            if (value.Key == "e")
            {
                returnValue.Email = value.Value;
                value = GetKeyValue(sdpStream);
            }

            // Phone optional
            if (value.Key == "p")
            {
                returnValue.Phone = value.Value;
                value = GetKeyValue(sdpStream);
            }

            // Phone optional
            if (value.Key == "c")
            {
                returnValue.Connection = new Connection(value.Value);
                value = GetKeyValue(sdpStream);
            }

            return returnValue;
        }


        public int Version { get; set; }


        public Origin Origin { get; set; }

        public string Session { get; set; }

        public string SessionInformation { get; set; }

        public Uri Url { get; set; }

        public string Email { get; set; }

        public string Phone { get; set; }

        internal Connection Connection { get; set; }
    }
}
