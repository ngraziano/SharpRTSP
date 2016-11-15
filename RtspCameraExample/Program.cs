using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RtspCameraExample
{
    class Program
    {
        static void Main(string[] args)
        {
            RtspServer s = new RtspServer(8554);
            s.StartListen();

            // Wait for user to terminate programme
            Console.WriteLine("Press ENTER to exit");
            String dummy = Console.ReadLine();

            s.StopListen();

        }
    }
}
