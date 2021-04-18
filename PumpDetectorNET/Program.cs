using PumpDetector.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PumpDetectorNET
{
    class Program
    {
        static NLog.Logger logger = NLog.LogManager.GetLogger("*");

        static void Main(string[] args)
        {
            System.AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            Engine engine = new Engine();
            //engine.PeekAccount();
            //engine.BackTest();
            engine.StartYourEngines();

            Console.WriteLine("Press ESC to stop");
            do
            {
                while (!Console.KeyAvailable)
                {
                    Thread.Sleep(10);
                }
            } while (Console.ReadKey(true).Key != ConsoleKey.Escape);
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            logger.Trace(e.ExceptionObject.ToString());
        }
    }
}
