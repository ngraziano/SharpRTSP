using System.Collections.Generic;
using System.Linq;

namespace Rtsp.Sdp
{
    public class Media
    {

        public Media(string mediaString)
        {
            // Example is   'video 0 RTP/AVP 26;
            var parts = mediaString.Split(new char[] { ' ' }, 4);

            if (parts.Count() >= 1)
            {
                MediaType = parts[0] switch
                {
                    "video" => MediaTypes.video,
                    "audio" => MediaTypes.audio,
                    "text" => MediaTypes.text,
                    "application" => MediaTypes.application,
                    "message" => MediaTypes.message,
                    _ => MediaTypes.unknown,// standard does allow for future types to be defined
                };
            }

            if (parts.Count() >= 4)
            {
                if (int.TryParse(parts[3], out int pt))
                {
                    PayloadType = pt;
                }
                else
                {
                    PayloadType = 0;
                }
            }
        }

        // RFC4566 Media Types
        public enum MediaTypes { video, audio, text, application, message, unknown };

        public Connection Connection { get; set; }

        public Bandwidth Bandwidth { get; set; }

        public EncriptionKey EncriptionKey { get; set; }

        public MediaTypes MediaType { get; set; }

        public int PayloadType { get; set; }

        private readonly List<Attribut> attributs = new();

        public IList<Attribut> Attributs
        {
            get
            {
                return attributs;
            }
        }
    }
}
