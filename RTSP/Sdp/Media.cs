using System;
using System.Collections.Generic;
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

        public Connection Connection { get; set; }

        public Bandwidth Bandwidth { get; set; }

        public EncriptionKey EncriptionKey { get; set; }

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
