using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Rtsp.Sdp
{
    public class AttributRtpMap : Attribut
    {

        public AttributRtpMap()
        {
        }

        public override string Key
        {
            get
            {
                return "rtpmap";
            }
        }

        public override string Value
        {
            get
            {
                return base.Value;
            }
            protected set
            {
                base.Value = value;
            }
        }

        protected override void ParseValue(string value)
        {

            base.ParseValue(value);
        }
    }
}
