using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rtsp.Messages;
using System.Diagnostics.Contracts;

public class RtspPushManager
{

    public Dictionary<string, RtspPushSession> PushSessions { get; } = new Dictionary<string, RtspPushSession>();

    internal RtspResponse HandleOptions(RtspRequestOptions request)
    {
        Contract.Requires(request != null);
        Contract.Ensures(Contract.Result<RtspResponse>() != null);

        var response = request.CreateResponse();
        response.ReturnCode = 200;
        return response;
    }

    internal RtspResponse HandleAnnounce(RtspRequestAnnounce request)
    {
        Contract.Requires(request != null);
        Contract.Ensures(Contract.Result<RtspResponse>() != null);

        var response = request.CreateResponse();

        if(PushSessions.ContainsKey(request.RtspUri.AbsolutePath))
        {
            response.ReturnCode = 403;

        }
        else
        {
            response.ReturnCode = 200;
            string sdp = Encoding.UTF8.GetString(request.Data);
            PushSessions[request.RtspUri.AbsolutePath] = new RtspPushSession(request.RtspUri.AbsolutePath, sdp);
        }

        return response;
    }

    internal RtspResponse HandleSetup(RtspRequestSetup request)
    {
        Contract.Requires(request != null);
        Contract.Ensures(Contract.Result<RtspResponse>() != null);

        var response = request.CreateResponse();
        // TODO Allocate a real session ID
        response.Session = "TTTT";
        // TODO Transport
        response.Headers[RtspHeaderNames.Transport] = request.GetTransports()[0].ToString();


        return response;
    }

    internal RtspResponse HandleRecord(RtspRequestRecord request)
    {
        Contract.Requires(request != null);
        Contract.Ensures(Contract.Result<RtspResponse>() != null);


        var response = request.CreateResponse();


        return response;

    }
}
