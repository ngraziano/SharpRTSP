namespace Rtsp.Sdp;

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;


public class H264Parameters : ParametersBase, IDictionary<string, string>
{
    private const string HeaderName = "sprop-parameter-sets";

    public IList<byte[]> SpropParameterSets =>
        TryGetValue(HeaderName, out var value)
            ? value.Split(',').Select(Convert.FromBase64String).ToList()
            : [];

    public byte[] SequenceParameterSet => SpropParameterSets.Count > 0 ? SpropParameterSets[0] : [];
    public byte[] PictureParameterSet => SpropParameterSets.Count > 1 ? SpropParameterSets[1] : [];

    public static H264Parameters Parse(string parameterString) => Parse<H264Parameters>(parameterString);
}