﻿using PumpDetector.Services;
using System;

namespace PumpDetector
{
    class Program
    {
        static void Main(string[] args)
        {
            Engine engine = new Engine();
            //engine.BackTest();
            engine.StartYourEngines();

            Console.ReadKey();
        }
    }
}
