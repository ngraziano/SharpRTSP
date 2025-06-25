namespace Rtsp.Messages;

using System.Collections.Generic;
using System.Linq;

public static class RTSPHeaderUtils
{
    public static IList<string> ParsePublicHeader(string? headerValue) =>
        string.IsNullOrEmpty(headerValue) ? [] : headerValue!.Split(',').Select(m => m.Trim()).ToList();

    public static IList<string> ParsePublicHeader(RtspResponse response)
        => ParsePublicHeader(response.Headers.TryGetValue(RtspHeaderNames.Public, out var value) ? value : null);
}