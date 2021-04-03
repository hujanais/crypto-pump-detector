﻿using PumpDetector.Services;
using System;

namespace PumpDetector
{
    class Program
    {
        static NLog.Logger logger = NLog.LogManager.GetLogger("*");

        static void Main(string[] args)
        {
            System.AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            Engine engine = new Engine();
            // engine.BackTest();
            engine.StartYourEngines();

            Console.WriteLine("Press Enter key to stop");
            Console.Read();  // This works with Linux.
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            logger.Trace(e.ExceptionObject.ToString());
        }
    }
}
