using System.Globalization;

namespace Rtsp.Sdp
{
    public class AttributRtpMap : Attribut
    {
        // Format
        //   rtpmap:<payload type> <encoding name>/<clock rate> [/<encoding parameters>] 
        // Examples
        //   rtpmap:96 H264/90000
        //   rtpmap:8 PCMA/8000

        public const string NAME = "rtpmap";

        public AttributRtpMap() : base(NAME)
        {
        }

        public override string Value
        {
            get
            {
                if (string.IsNullOrEmpty(EncodingParameters))
                {
                    return string.Format(CultureInfo.InvariantCulture, "{0} {1}/{2}", PayloadNumber, EncodingName, ClockRate);
                }
                return string.Format(CultureInfo.InvariantCulture, "{0} {1}/{2}/{3}", PayloadNumber, EncodingName, ClockRate, EncodingParameters);
            }
            protected set
            {
                ParseValue(value);
            }
        }

        public int PayloadNumber { get; set; }
        public string? EncodingName { get; set; }
        public string? ClockRate { get; set; }
        public string? EncodingParameters { get; set; }

        protected override void ParseValue(string value)
        {
            var parts = value.Split([' ', '/']);

            if (parts.Length >= 1 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int tmp_payloadNumber))
            {
                PayloadNumber = tmp_payloadNumber;
            }
            if (parts.Length >= 2)
            {
                EncodingName = parts[1];
            }
            if (parts.Length >= 3)
            {
                ClockRate = parts[2];
            }
            if (parts.Length >= 4)
            {
                EncodingParameters = parts[3];
            }
        }
    }
}
