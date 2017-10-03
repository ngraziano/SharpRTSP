using System;
using System.Threading;

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
            String readline = null;
            while (readline == null) {
                readline = Console.ReadLine();

                // Avoid maxing out CPU on systems that instantly return null for ReadLine
                if (readline == null) Thread.Sleep(500);
            }

            s.StopListen();

        }
    }
}
