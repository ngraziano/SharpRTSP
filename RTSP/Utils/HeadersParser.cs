﻿using System.Collections.Specialized;
using System.IO;

namespace Rtsp.Utils;
static class HeadersParser
{
    public static NameValueCollection ParseHeaders(StreamReader headersReader)
    {
        var headers = new NameValueCollection();

        string header;

        while (!string.IsNullOrEmpty(header = headersReader.ReadLine()))
        {
            int colonPos = header.IndexOf(':');

            if (colonPos == -1) { continue; }

            string key = header[..colonPos].Trim().ToUpperInvariant();
            string value = header[++colonPos..].Trim();

            headers.Add(key, value);
        }

        return headers;
    }
}