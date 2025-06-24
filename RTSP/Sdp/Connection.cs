using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Rtsp.Sdp
{
    public abstract class Connection
    {
        private const string _ConnectionRegexString = @"IN (?<Type>(IP4|IP6)) (?<Address>[0-9a-zA-Z\.\/\:]*)";

        private static readonly Regex _ConnectionRegex = new(_ConnectionRegexString, RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));

        public string Host { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the number of address specifed in connection.
        /// </summary>
        /// <value>The number of the address.</value>
        public int NumberOfAddress { get; set; } = 1;

        public static Connection Parse(string value)
        {
            if (value is null)
                throw new ArgumentNullException(nameof(value));

            var matches = _ConnectionRegex.Matches(value);

            if (matches.Count > 0)
            {
                var firstMatch = matches[0];
                var type = firstMatch.Groups["Type"];
                return type.Value switch
                {
                    "IP4" => ConnectionIP4.Parse(firstMatch.Groups["Address"].Value),
                    "IP6" => ConnectionIP6.Parse(firstMatch.Groups["Address"].Value),
                    _ => throw new NotSupportedException(string.Format(CultureInfo.InvariantCulture,
                        "Address type {0} not suported", firstMatch.Groups["Address"].Value)),
                };
            }

            throw new FormatException("Unrecognised Connection value");
        }
    }
}
