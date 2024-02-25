using System;
using System.Diagnostics.Contracts;

namespace Rtsp.Sdp
{
    public class SdpTimeZone
    {
        public required string RawValue { get; init; }

        public static SdpTimeZone ParseInvariant(string value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            Contract.EndContractBlock();

            return new()
            {
                RawValue = value,
            };
        }
    }
}
