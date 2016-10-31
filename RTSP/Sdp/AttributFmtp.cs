using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Rtsp.Sdp
{
    public class AttributFmtp : Attribut
    {
        public const string NAME = "fmtp";

        public AttributFmtp()
        {
        }

        public override string Key
        {
            get
            {
                return NAME;
            }
        }

        public override string Value
        {
            get
            {
                return string.Format("{0} {1}", PayloadNumber, FormatParameter);
            }
            protected set
            {
                ParseValue(value);
            }
        }

        public int PayloadNumber { get; set; }

        // temporary aatibute to store remaning data not parsed
        public string FormatParameter { get; set; }

        protected override void ParseValue(string value)
        {
            var parts = value.Split(new char[] { ' ' }, 2);

            int payloadNumber;
            if(int.TryParse(parts[0], out payloadNumber))
            {
                this.PayloadNumber = payloadNumber;
            }
            if(parts.Length > 1)
            {
                FormatParameter = parts[1];
            }


        }
    }
}
