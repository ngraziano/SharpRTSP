namespace Rtsp.Sdp;

using System;
using System.Collections.Generic;
using System.Linq;

// Parse 'fmtp' attribute in SDP
// Extract H266 fields
public class H266Parameters : ParametersBase, IDictionary<string, string>
{
    public IList<byte[]> SpropParameterSets
    {
        get
        {
            List<byte[]> result = [];

            if (ContainsKey("sprop-dci") && this["sprop-dci"] != null)
            {
                result.AddRange(this["sprop-dci"].Split(',').Select(x => Convert.FromBase64String(x)));
            }
            else
            {
                result.Add(Array.Empty<byte>());
            }

            if (ContainsKey("sprop-vps") && this["sprop-vps"] != null)
            {
                result.AddRange(this["sprop-vps"].Split(',').Select(x => Convert.FromBase64String(x)));
            }
            else
            {
                result.Add(Array.Empty<byte>());
            }

            if (ContainsKey("sprop-sps") && this["sprop-sps"] != null)
            {
                result.AddRange(this["sprop-sps"].Split(',').Select(x => Convert.FromBase64String(x)));
            }
            else
            {
                result.Add(Array.Empty<byte>());
            }

            if (ContainsKey("sprop-pps") && this["sprop-pps"] != null)
            {
                result.AddRange(this["sprop-pps"].Split(',').Select(x => Convert.FromBase64String(x)));
            }
            else
            {
                result.Add(Array.Empty<byte>());
            }

            if (ContainsKey("sprop-sei") && this["sprop-sei"] != null)
            {
                result.AddRange(this["sprop-sei"].Split(',').Select(x => Convert.FromBase64String(x)));
            }
            else
            {
                result.Add(Array.Empty<byte>());
            }

            return result;
        }
    }

    public byte[] DecodingCapabilityInformation => ParameterFromBase64String("sprop-dci");
    public IList<byte[]> VideoParameterSet => ParameterListFromBase64String("sprop-vps");
    public IList<byte[]> SequenceParameterSet => ParameterListFromBase64String("sprop-sps");
    public IList<byte[]> PictureParameterSet => ParameterListFromBase64String("sprop-pps");
    public IList<byte[]> SEIMessages => ParameterListFromBase64String("sprop-sei");

    public static H266Parameters Parse(string parameterString) => Parse<H266Parameters>(parameterString);
}