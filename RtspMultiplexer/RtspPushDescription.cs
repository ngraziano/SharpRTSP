using RtspMulticaster;
using System;
using System.Collections.Generic;

public class RtspPushDescription
{
    public string Sdp { get; }
    public string AbsolutePath { get; }

    private string pushSession;
    private readonly Dictionary<string, Forwarder> forwarders = new Dictionary<string, Forwarder>();

    public RtspPushDescription(string absolutePath, string sdp)
    {
        AbsolutePath = absolutePath;
        Sdp = sdp;
    }

    public void AddForwarders(string session, string path, Forwarder forwarder)
    {
        if (string.IsNullOrEmpty(pushSession))
        {
            pushSession = session;

        }
        else
        {
            // TODO better session management
            if (pushSession != session)
                throw new System.Exception("Invalid state");
        }
        forwarders.Add(path, forwarder);

        forwarder.ToMulticast = true;
        forwarder.ForwardHostVideo = "239.0.0.1";
        forwarder.ForwardPortVideo = forwarder.FromForwardVideoPort;

    }

    public Forwarder GetForwarderFor(string path)
    {
        // TODO change to return only info and not all forwarder
       return forwarders[path];
    }

    public void Start(string session)
    {
        // TODO better session management
        if (pushSession != session)
            throw new System.Exception("Invalid state");
        foreach(var forwarder in forwarders.Values)
        {
            forwarder.Start();
        }
    }

    internal void Stop(string session)
    {
        // TODO better session management
        if (pushSession != session)
            throw new System.Exception("Invalid state");
        foreach (var forwarder in forwarders.Values)
        {
            forwarder.Stop();
        }
        forwarders.Clear();
        pushSession = null;
    }
}