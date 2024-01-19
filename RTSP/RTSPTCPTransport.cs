﻿using System;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace Rtsp;

/// <summary>
/// TCP Connection for Rtsp
/// </summary>
public class RtspTcpTransport : IRtspTransport, IDisposable
{
    private readonly NetworkCredential _credentials;
    private readonly IPEndPoint _currentEndPoint;
    private TcpClient _RtspServerClient;

    private uint _commandCounter = 0;
    /// <summary>
    /// Initializes a new instance of the <see cref="RtspTcpTransport"/> class.
    /// </summary>
    /// <param name="tcpConnection">The underlying TCP connection.</param>
    public RtspTcpTransport(TcpClient tcpConnection, NetworkCredential credentials)
    {
        if (tcpConnection == null)
            throw new ArgumentNullException(nameof(tcpConnection));
        Contract.EndContractBlock();

        _currentEndPoint = (IPEndPoint)tcpConnection.Client.RemoteEndPoint;
        _RtspServerClient = tcpConnection;
        _credentials = credentials;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RtspTcpTransport"/> class.
    /// </summary>
    /// <param name="aHost">A host.</param>
    /// <param name="aPortNumber">A port number.</param>
    public RtspTcpTransport(Uri uri, NetworkCredential credentials)
        : this(new TcpClient(uri.Host, uri.Port), credentials)
    {
    }


    #region IRtspTransport Membres

    /// <summary>
    /// Gets the stream of the transport.
    /// </summary>
    /// <returns>A stream</returns>
    public Stream GetStream() => _RtspServerClient.GetStream();

    /// <summary>
    /// Gets the remote address.
    /// </summary>
    /// <value>The remote address.</value>
    public string RemoteAddress => string.Format(CultureInfo.InvariantCulture, "{0}:{1}", _currentEndPoint.Address, _currentEndPoint.Port);

    public uint CommandCounter => ++_commandCounter;

    /// <summary>
    /// Closes this instance.
    /// </summary>
    public void Close() => Dispose(true);

    /// <summary>
    /// Gets a value indicating whether this <see cref="IRtspTransport"/> is connected.
    /// </summary>
    /// <value><c>true</c> if connected; otherwise, <c>false</c>.</value>
    public bool Connected => _RtspServerClient.Client != null && _RtspServerClient.Connected;

    /// <summary>
    /// Reconnect this instance.
    /// <remarks>Must do nothing if already connected.</remarks>
    /// </summary>
    /// <exception cref="System.Net.Sockets.SocketException">Error during socket </exception>
    public void Reconnect()
    {
        if (Connected) { return; }

        _commandCounter = 0;
        _RtspServerClient = new TcpClient();
        _RtspServerClient.Connect(_currentEndPoint);
    }

    #endregion

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _RtspServerClient.Close();
        }
    }
}