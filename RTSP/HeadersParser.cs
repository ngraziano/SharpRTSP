namespace Rtsp;

using System;
using System.Collections.Specialized;
using System.IO;

internal static class HeadersParser
{
    public static NameValueCollection ParseHeaders(StreamReader headersReader)
    {
        NameValueCollection headers = new(StringComparer.InvariantCultureIgnoreCase);
        string? header;
        while (!string.IsNullOrEmpty(header = headersReader.ReadLine()))
        {
            int colonPos = header.IndexOf(':', StringComparison.InvariantCulture);
            if (colonPos == -1) { continue; }
            string key = header[..colonPos].Trim().ToUpperInvariant();
            string value = header[++colonPos..].Trim();

            headers.Add(key, value);
        }
        return headers;
    }
}
