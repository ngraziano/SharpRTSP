using Rtsp.Messages;
using System;
using System.Collections.Generic;
using System.Net;

namespace Rtsp
{
    // WWW-Authentication and Authorization Headers
    public abstract class Authentication
    {
        public NetworkCredential Credentials { get; }

        protected Authentication(NetworkCredential credentials)
        {
            Credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        }

        public abstract string GetServerResponse();
        public abstract string GetResponse(uint nonceCounter, string uri, string method, byte[] entityBodyBytes);
        public abstract bool IsValid(RtspRequest receivedMessage);

        public static Authentication Create(NetworkCredential credential, string authenticateHeader)
        {
            authenticateHeader = authenticateHeader ??
                                 throw new ArgumentNullException(nameof(authenticateHeader));

            if (authenticateHeader.StartsWith("Basic", StringComparison.OrdinalIgnoreCase))
            {
                int spaceIndex = authenticateHeader.IndexOf(' ', StringComparison.Ordinal);

                if (spaceIndex != -1)
                {
                    string parameters = authenticateHeader[++spaceIndex..];

                    Dictionary<string, string> parameterNameToValueMap = ParseParameters(parameters);
                    if (!parameterNameToValueMap.TryGetValue("REALM", out var realm) || realm is null)
                        throw new ArgumentException("\"realm\" parameter is not found in header", nameof(authenticateHeader));
                    return new AuthenticationBasic(credential, realm);
                }
            }

            if (authenticateHeader.StartsWith("Digest", StringComparison.OrdinalIgnoreCase))
            {
                int spaceIndex = authenticateHeader.IndexOf(' ', StringComparison.Ordinal);

                if (spaceIndex != -1)
                {
                    string parameters = authenticateHeader[++spaceIndex..];

                    Dictionary<string, string> parameterNameToValueMap = ParseParameters(parameters);

                    if (!parameterNameToValueMap.TryGetValue("REALM", out var realm) || realm is null)
                        throw new ArgumentException("\"realm\" parameter is not found in header", nameof(authenticateHeader));
                    if (!parameterNameToValueMap.TryGetValue("NONCE", out var nonce) || nonce is null)
                        throw new ArgumentException("\"nonce\" parameter is not found in header", nameof(authenticateHeader));

                    parameterNameToValueMap.TryGetValue("QOP", out var qop);

                    // algorithm parameter is optional (and we default to MD5 if it is not present)
                    AuthenticationDigest.HashAlgorithm algorithm = AuthenticationDigest.HashAlgorithm.MD5;
                    if (parameterNameToValueMap.TryGetValue("ALGORITHM", out var algorithm_string))
                    {
                        if (string.Equals(algorithm_string, "SHA-256")) algorithm = AuthenticationDigest.HashAlgorithm.SHA256;
                        else if (string.Equals(algorithm_string, "MD5")) algorithm = AuthenticationDigest.HashAlgorithm.MD5;
                        else throw new ArgumentException("\"algorithm\" parameter invalid", nameof(authenticateHeader));
                    }

                    return new AuthenticationDigest(credential, realm, nonce, qop, algorithm);
                }
            }

            throw new ArgumentOutOfRangeException(nameof(authenticateHeader),
                $"Invalid authenticate header: {authenticateHeader}");
        }

        private static Dictionary<string, string> ParseParameters(string parameters)
        {
            Dictionary<string, string> parameterNameToValueMap = new(StringComparer.OrdinalIgnoreCase);

            int parameterStartOffset = 0;
            while (parameterStartOffset < parameters.Length)
            {
                int equalsSignIndex = parameters.IndexOf('=', parameterStartOffset);

                if (equalsSignIndex == -1) { break; }

                int parameterNameLength = equalsSignIndex - parameterStartOffset;
                string parameterName = parameters.Substring(parameterStartOffset, parameterNameLength).Trim().ToUpperInvariant();

                ++equalsSignIndex;

                int nonSpaceIndex = equalsSignIndex;

                if (nonSpaceIndex == parameters.Length) { break; }

                while (parameters[nonSpaceIndex] == ' ')
                {
                    if (++nonSpaceIndex == parameters.Length)
                    { break; }
                }

                int parameterValueStartPos;
                int parameterValueEndPos;
                int commaIndex;

                if (parameters[nonSpaceIndex] == '\"')
                {
                    parameterValueStartPos = parameters.IndexOf('\"', equalsSignIndex);

                    if (parameterValueStartPos == -1) { break; }

                    ++parameterValueStartPos;

                    parameterValueEndPos = parameters.IndexOf('\"', parameterValueStartPos);

                    if (parameterValueEndPos == -1) { break; }

                    commaIndex = parameters.IndexOf(',', parameterValueEndPos + 1);

                    parameterStartOffset = commaIndex != -1 ? ++commaIndex : parameters.Length;
                }
                else
                {
                    parameterValueStartPos = nonSpaceIndex;

                    commaIndex = parameters.IndexOf(',', ++nonSpaceIndex);

                    if (commaIndex != -1)
                    {
                        parameterValueEndPos = commaIndex;
                        parameterStartOffset = ++commaIndex;
                    }
                    else
                    {
                        parameterValueEndPos = parameters.Length;
                        parameterStartOffset = parameterValueEndPos;
                    }
                }

                int parameterValueLength = parameterValueEndPos - parameterValueStartPos;
                parameterNameToValueMap[parameterName] = parameters.Substring(parameterValueStartPos, parameterValueLength);
            }

            return parameterNameToValueMap;
        }
    }
}
