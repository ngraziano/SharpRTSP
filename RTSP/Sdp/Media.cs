using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Rtsp.Sdp
{
    public class Media
    {
        private string p;

        public Media(string p)
        {
            // TODO: Complete member initialization
            this.p = p;
        }

        // RFC4566 Media Types
        public enum MediaType { video, audio, text, application, message };

        public Connection Connection { get; set; }

        public Bandwidth Bandwidth { get; set; }

        public EncriptionKey EncriptionKey { get; set; }

        public MediaType GetMediaType()
        {
            if (p.StartsWith("video")) return MediaType.video;
            else if (p.StartsWith("audio")) return MediaType.audio;
            else if (p.StartsWith("text")) return MediaType.text;
            else if (p.StartsWith("application")) return MediaType.application;
            else if (p.StartsWith("message")) return MediaType.message;
            else throw new InvalidDataException();
        }

        private readonly List<Attribut> attributs = new List<Attribut>();

        public IList<Attribut> Attributs
        {
            get
            {
                return attributs;
            }
        }
    }
}
