using System;
using System.Threading;

namespace RtspCameraExample
{
    class Program
    {
        static void Main(string[] args)
        {
			int port = 8554;
			string username = "user";      // or use NUL if there is no username
			string password = "password";  // or use NUL if there is no password
            
            RtspServer s = new RtspServer(port,username,password);
            try {
                s.StartListen();
            } catch {
                Console.WriteLine("Error: Could not start server");
                return;
            }

            // Wait for user to terminate programme
			String msg = "Connect RTSP client to Port=" + port;
			if (username != null && password != null) {
				msg += " Username=" + username + " Password=" + password;
			}
			Console.WriteLine(msg);
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
