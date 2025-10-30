namespace Rtsp.Sdp;

using System.Collections.Generic;

public class AV1Parameters : ParametersBase, IDictionary<string, string>
{
    public static AV1Parameters Parse(string parameterString) => Parse<AV1Parameters>(parameterString);

}
