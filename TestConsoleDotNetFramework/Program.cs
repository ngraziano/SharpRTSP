using Rtsp;
using Rtsp.Messages;
using Rtsp.Rtp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;

namespace TestConsole
{
    internal class Program
    {
        static void Main(string[] args)
        {
            RtspUtils.RegisterUri();
            RtspTcpTransport transport = new RtspTcpTransport(new Uri(args[0]));
            RtspListener listener = new RtspListener(transport);

            listener.MessageReceived += (sender, e) =>
            {
                Console.WriteLine("Received " + e.Message);
            };
            listener.Start();

            RtspRequest optionsMessage = new RtspRequestOptions();
            listener.SendMessage(optionsMessage);

            RtspRequest describeMessage = new RtspRequestDescribe();
            listener.SendMessage(describeMessage);

            

            Console.WriteLine("Press enter to exit");
            Console.ReadLine();
        }
    }
}
