using System;
using System.Collections.Generic;
using System.Linq;

namespace Rtsp.Sdp
{
    public class AttributFmtp : Attribut
    {
        public const string NAME = "fmtp";

        private readonly Dictionary<string, string> parameters = new ();

        public AttributFmtp() : base(NAME)
        {
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


        // Extract the Payload Number and the Format Parameters
        protected override void ParseValue(string value)
        {
            var parts = value.Split(new char[] { ' ' }, 2);

            int payloadNumber;
            if (int.TryParse(parts[0], out payloadNumber))
            {
                this.PayloadNumber = payloadNumber;
            }
            if (parts.Length > 1)
            {
                FormatParameter = parts[1];

                // Split on ';' to get a list of items.
                // Then Trim each item and then Split on the first '='
                // Add them to the dictionary
                parameters.Clear();
                foreach (var pair in parts[1].Split(';').Select(x => x.Trim().Split(new char[] { '=' }, 2)))
                {
                    if (!string.IsNullOrWhiteSpace(pair[0]))
                        parameters[pair[0]] = pair.Length > 1 ? pair[1] : null;
                }
            }
        }

        public string this[string index]
        {
            get
            {
                if (parameters.ContainsKey(index)) return parameters[index];
                else return "";
            }
        }

    }
}
