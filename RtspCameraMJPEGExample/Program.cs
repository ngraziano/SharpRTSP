// Example software to simulate an Live RTSP Steam and an RTSP CCTV Camera in C#
// There is a very simple Video and Audio generator
// with a very simple (and not very efficient) H264 and G711 u-Law audio encoder
// to feed data into the RTSP Server
//
// Server supports TCP and UDP clients.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace RtspCameraExample
{
    static class Program
    {

        static void Main()
        {
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder
                    .AddFilter("Microsoft", LogLevel.Warning)
                    .AddFilter("System", LogLevel.Warning)
                    .AddFilter("RtspCameraExample", LogLevel.Debug)
                    .AddFilter("Rtsp", LogLevel.Debug)
                    .AddConsole();
            });
            var demo = new Demo(loggerFactory);

        }


        class Demo
        {
            private readonly RtspServer rtspServer;

            private readonly int port = 8554;
            private readonly string username = "user";      // or use NUL if there is no username
            private readonly string password = "password";  // or use NUL if there is no password


            public Demo(ILoggerFactory loggerFactory)
            {
                rtspServer = new RtspServer(port, username, password, loggerFactory);
                try
                {
                    rtspServer.StartListen();
                }
                catch
                {
                    Console.WriteLine("Error: Could not start server");
                    throw;
                }


                CancellationTokenSource cts = new();
                var sendJpeg = Task.Factory.StartNew(() => SendImages(cts.Token), cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

                Console.WriteLine($"RTSP URL is rtsp://{username}:{password}@hostname:{port}");


                string msg = "Connect RTSP client to Port=" + port;
                if (username is not null && password is not null)
                {
                    msg += " Username=" + username + " Password=" + password;
                }
                Console.WriteLine(msg);
                Console.WriteLine("Press ENTER to exit");
                Console.ReadLine();

                cts.Cancel();
                rtspServer.StopListen();
                sendJpeg.Wait();

            }

            private async Task SendImages(CancellationToken token)
            {
                var jpegFile = File.ReadAllBytes("test_1024x768.jpg");
                Stopwatch stopwatch = Stopwatch.StartNew();
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        // Send the frame to all clients
                        rtspServer.FeedInRawJPEG((uint)stopwatch.ElapsedMilliseconds, jpegFile, 1024, 768);

                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Error: " + ex.Message);
                    }
                    await Task.Delay(1000 / 5, token);
                }
                stopwatch.Stop();
            }
        }
    }
}
