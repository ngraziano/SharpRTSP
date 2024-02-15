using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Rtsp.Sdp
{
    public class AttributFmtp : Attribut
    {
        public const string NAME = "fmtp";

        private readonly Dictionary<string, string> parameters = new(StringComparer.Ordinal);

        public AttributFmtp() : base(NAME)
        {
        }

        public override string Value
        {
            get
            {
                return string.Format(CultureInfo.InvariantCulture,  "{0} {1}", PayloadNumber, FormatParameter);
            }
            protected set
            {
                ParseValue(value);
            }
        }

        public int PayloadNumber { get; set; }

        // temporary aatibute to store remaning data not parsed
        public string? FormatParameter { get; set; }

        // Extract the Payload Number and the Format Parameters
        protected override void ParseValue(string value)
        {
            var parts = value.Split(' ', 2);

            if (int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int payloadNumber))
            {
                PayloadNumber = payloadNumber;
            }
            if (parts.Length > 1)
            {
                FormatParameter = parts[1];

                // Split on ';' to get a list of items.
                // Then Trim each item and then Split on the first '='
                // Add them to the dictionary
                parameters.Clear();
                foreach (var pair in parts[1].Split(';').Select(x => x.Trim().Split(['='], 2)))
                {
                    if (!string.IsNullOrWhiteSpace(pair[0]))
                        parameters[pair[0]] = pair.Length > 1 ? pair[1] : string.Empty;
                }
            }
        }

        public string this[string index] => parameters.TryGetValue(index, out string? value) ? value : string.Empty;
    }
}
