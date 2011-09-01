namespace RtspMulticaster
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    class Program
    {
        private static NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
        
        static void Main(string[] args)
        {
            _logger.Info("Starting");
            RtspServer monServeur = new RtspServer(8554);

            monServeur.StartListen();
            RTSPDispatcher.Instance.StartQueue();

            while (Console.ReadLine() != "q")
            {
            }

            monServeur.StopListen();
            RTSPDispatcher.Instance.StopQueue();


        }
    }
}
