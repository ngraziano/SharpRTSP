using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rtsp.Messages;
using System.Diagnostics.Contracts;

namespace RtspMulticaster
{
    public class RtspPushManager
    {

        private Random sessionGenerator = new Random();

        private readonly Dictionary<string, RtspPushDescription> pushDescriptions = new Dictionary<string, RtspPushDescription>();
        public Dictionary<string, RtspPushDescription> PushDescriptions { get { return pushDescriptions; } }

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

            if (PushDescriptions.ContainsKey(request.RtspUri.AbsolutePath))
            {
                response.ReturnCode = 403;

            }
            else
            {
                response.ReturnCode = 200;
                string sdp = Encoding.UTF8.GetString(request.Data);
                var session = new RtspPushDescription(request.RtspUri.AbsolutePath, sdp);
                PushDescriptions[request.RtspUri.AbsolutePath] = session;
                foreach (var path in GetAllControlPath(sdp, request.RtspUri))
                {
                    PushDescriptions[path] = session;
                }

            }

            return response;
        }

        private IList<string> GetAllControlPath(string sdp, Uri basepath)
        {
            var paths = new List<string>();
            // hugly , must be improved
            var basepathcompleted = new Uri(basepath.ToString() + "/");
            foreach (var line in sdp.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("a=control:"))
                {


                    // TODO handle full url, etc...
                    var part = line.Remove(0, "a=control:".Length);
                    paths.Add(new Uri(basepathcompleted, part).AbsolutePath);

                }
            }


            return paths;
        }

        internal RtspResponse HandleSetup(RtspRequestSetup request)
        {
            Contract.Requires(request != null);
            Contract.Ensures(Contract.Result<RtspResponse>() != null);

            var response = request.CreateResponse();
            if (string.IsNullOrEmpty(response.Session))
            {
                // TODO Allocate a real session ID
                response.Session = sessionGenerator.Next().ToString();
            }

            RtspPushDescription description;
            if (!PushDescriptions.TryGetValue(request.RtspUri.AbsolutePath, out description))
            {
                response.ReturnCode = 404;
                return response;
            }


            bool configok = false;
            foreach (var transport in request.GetTransports())
            {
                if (transport.LowerTransport == RtspTransport.LowerTransportType.UDP
                   && !transport.IsMulticast
                    )
                {
                    var forwarder = new UDPForwarder();
                    description.AddForwarders(response.Session, request.RtspUri.AbsolutePath, forwarder);
                    transport.ServerPort = new PortCouple(forwarder.ListenVideoPort);
                    response.Headers[RtspHeaderNames.Transport] = transport.ToString();
                    configok = true;
                }
            }

            if (!configok)
                response.ReturnCode = 461;
            return response;
        }

        internal RtspResponse HandleRecord(RtspRequestRecord request)
        {
            Contract.Requires(request != null);
            Contract.Ensures(Contract.Result<RtspResponse>() != null);


            var response = request.CreateResponse();
            RtspPushDescription description;
            if (!PushDescriptions.TryGetValue(request.RtspUri.AbsolutePath, out description))
            {
                response.ReturnCode = 404;
                return response;
            }

            description.Start(request.Session);



            return response;

        }

        internal RtspResponse HandleTeardown(RtspRequestTeardown request)
        {
            Contract.Requires(request != null);
            Contract.Ensures(Contract.Result<RtspResponse>() != null);


            var response = request.CreateResponse();
            RtspPushDescription description;
            if (!PushDescriptions.TryGetValue(request.RtspUri.AbsolutePath, out description))
            {
                response.ReturnCode = 404;
                return response;
            }

            description.Stop(request.Session);



            return response;
        }

        internal RtspResponse HandlePullDescribe(RtspRequestDescribe request)
        {
            var pushUri = GetPushUri(request.RtspUri.AbsolutePath);
            var response = request.CreateResponse();

            RtspPushDescription description;
            if (PushDescriptions.TryGetValue(pushUri, out description))
            {
                byte[] sdp_bytes = Encoding.ASCII.GetBytes(description.Sdp);

                response.AddHeader("Content-Base: " + request.RtspUri);
                response.AddHeader("Content-Type: application/sdp");
                response.Data = sdp_bytes;
            }
            else
            {
                response.ReturnCode = 404;
            }
            return response;
        }

        private string GetPushUri(string absolutePath)
        {
            return absolutePath.Replace("/PULL/", "/PUSH/");
        }

        internal RtspResponse HandlePullSetup(RtspRequestSetup request)
        {
            var response = request.CreateResponse();
            if (string.IsNullOrEmpty(response.Session))
            {
                // TODO Allocate a real session ID
                response.Session = sessionGenerator.Next().ToString();
            }

            var pushUri = GetPushUri(request.RtspUri.AbsolutePath);

            RtspPushDescription description;
            if (PushDescriptions.TryGetValue(pushUri, out description))
            {
                //TODO get port and multicast address from description.
                var forwarder = description.GetForwarderFor(pushUri);
                var transport = new RtspTransport();

                RtspTransport newTransport = new RtspTransport()
                {
                    IsMulticast = true,
                    Destination = forwarder.ForwardHostVideo,
                    Port = new PortCouple(forwarder.ForwardPortVideo, forwarder.ListenCommandPort)
                };
                response.Headers[RtspHeaderNames.Transport] = newTransport.ToString();
            }
            else
            {
                response.ReturnCode = 404;
            }
            return response;
        }

        internal RtspResponse HandlePullPlay(RtspRequestPlay request)
        {
            var response = request.CreateResponse();
            return response;
        }

        internal RtspResponse HandlePullGetParameter(RtspRequestGetParameter request)
        {
            var response = request.CreateResponse();
            return response;
        }


    }
}