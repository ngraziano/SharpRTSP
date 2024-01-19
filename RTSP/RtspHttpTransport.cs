﻿using Rtsp.Utils;
using System.Buffers;
using System.Collections.Specialized;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System;

namespace Rtsp;

public class RtspHttpTransport : IRtspTransport
{
    private readonly NetworkCredential _credentials;
    private readonly Uri _uri;

    private Socket? _streamDataClient;
    private Socket? _commandsClient;

    private Stream _dataNetworkStream = null!;

    private Authentication? _authentication;


    private uint _commandCounter = 0;
    private string _sessionCookie = string.Empty;

    public RtspHttpTransport(Uri uri, NetworkCredential credentials)
    {
        _credentials = credentials;
        _uri = uri;

        Reconnect();
    }

    public string RemoteAddress => $"{_uri}";
    public bool Connected => _streamDataClient != null && _streamDataClient.Connected;

    public uint CommandCounter => ++_commandCounter;

    public void Close()
    {
        _streamDataClient?.Close();
        _commandsClient?.Close();
    }

    public Stream GetStream()
    {
        if (_streamDataClient == null || !_streamDataClient.Connected)
            throw new InvalidOperationException("Client is not connected");

        return _dataNetworkStream;
    }

    public void Reconnect()
    {
        if (Connected) { return; }

        _commandCounter = 0;
        _sessionCookie = Guid.NewGuid().ToString("N")[..10];
        _streamDataClient = Utils.NetworkClientFactory.CreateTcpClient();

        int httpPort = _uri.Port != -1 ? _uri.Port : 80;
        _streamDataClient.Connect(_uri.Host, httpPort);
        _dataNetworkStream = new NetworkStream(_streamDataClient, false);

        string request = ComposeGetRequest();
        byte[] requestByte = Encoding.ASCII.GetBytes(request);

        _dataNetworkStream.Write(requestByte, 0, requestByte.Length);

        byte[] buffer = ArrayPool<byte>.Shared.Rent(RtspConstants.MaxResponseHeadersSize);
        int read = ReadUntilEndOfHeaders(_dataNetworkStream, buffer, RtspConstants.MaxResponseHeadersSize);

        using MemoryStream ms = new(buffer, 0, read);
        using StreamReader streamReader = new(ms, Encoding.ASCII);

        string responseLine = streamReader.ReadLine();
        if (string.IsNullOrEmpty(responseLine)) { throw new HttpBadResponseException("Empty response"); }

        string[] tokens = responseLine.Split(' ');
        if (tokens.Length != 3) { throw new HttpRequestException("Invalid first response line"); }

        HttpStatusCode statusCode = (HttpStatusCode)int.Parse(tokens[1]);
        if (statusCode == HttpStatusCode.OK) { return; }

        if (statusCode == HttpStatusCode.Unauthorized &&
            !_credentials.IsEmpty() &&
            _authentication == null)
        {
            NameValueCollection headers = HeadersParser.ParseHeaders(streamReader);
            string authenticateHeader = headers.Get(WellKnownHeaders.WwwAuthenticate);

            if (string.IsNullOrEmpty(authenticateHeader))
                throw new HttpBadResponseCodeException(statusCode);

            _authentication = Authentication.Create(_credentials, authenticateHeader);

            _streamDataClient.Dispose();

            Reconnect();
            return;
        }

        throw new HttpBadResponseCodeException(statusCode);
    }

    public void Write(byte[] buffer, int offset, int count)
    {
        using (_commandsClient = NetworkClientFactory.CreateTcpClient())
        {
            int httpPort = _uri.Port != -1 ? _uri.Port : 80;

            _commandsClient.Connect(_uri.Host, httpPort);

            string base64CodedCommandString = Convert.ToBase64String(buffer, offset, count);
            byte[] base64CommandBytes = Encoding.ASCII.GetBytes(base64CodedCommandString);

            string request = ComposePostRequest(base64CommandBytes);
            byte[] requestBytes = Encoding.ASCII.GetBytes(request);

            ArraySegment<byte>[] sendList = [new(requestBytes), new(base64CommandBytes)];

            _commandsClient.Send(sendList, SocketFlags.None);
        }
    }

    private string ComposeGetRequest()
    {
        //string authorizationHeader = GetAuthorizationHeader(CommandCounter, "GET", []);

        return $"GET {_uri.PathAndQuery} HTTP/1.0\r\n" +
               $"x-sessioncookie: {_sessionCookie}\r\n\r\n";

    }

    private string ComposePostRequest(byte[] commandBytes)
    {
        //string authorizationHeader = GetAuthorizationHeader(CommandCounter, "POST", commandBytes);

        return $"POST {_uri.PathAndQuery} HTTP/1.0\r\n" +
               $"x-sessioncookie: {_sessionCookie}\r\n" +
               "Content-Type: application/x-rtsp-tunnelled\r\n" +
               $"Content-Length: {commandBytes.Length}\r\n\r\n";
    }

    private string GetAuthorizationHeader(uint counter, string method, byte[] requestBytes)
    {
        string authorizationHeader;

        if (_authentication != null)
        {
            string headerValue = _authentication.GetResponse(counter, _uri.PathAndQuery, method, requestBytes);
            authorizationHeader = $"Authorization: {headerValue}\r\n";
        }
        else
        {
            authorizationHeader = string.Empty;
        }

        return authorizationHeader;
    }

    private static int ReadUntilEndOfHeaders(Stream stream, byte[] buffer, int length)
    {
        int offset = 0;

        int endOfHeaders;
        int totalRead = 0;

        do
        {
            int count = length - totalRead;

            if (count == 0)
                throw new InvalidOperationException($"Response is too large (> {length / 1024} KB)");

            int read = stream.Read(buffer, offset, count);

            if (read == 0)
                throw new EndOfStreamException("End of http stream");

            totalRead += read;

            int startIndex = offset - (RtspConstants.DoubleCrlfBytes.Length - 1);

            if (startIndex < 0)
                startIndex = 0;

            endOfHeaders = ArrayUtils.IndexOfBytes(buffer, RtspConstants.DoubleCrlfBytes, startIndex,
                totalRead - startIndex);

            offset += read;
        } while (endOfHeaders == -1);

        return totalRead;
    }
}
