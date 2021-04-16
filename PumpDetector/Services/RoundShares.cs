using System;
using System.Collections.Generic;
using System.Text;

namespace PumpDetector.Services
{
    public abstract class RoundShares
    {
        /// <summary>
        /// Round down to nearest 0.1 coin.
        /// </summary>
        /// <param name="stake"></param>
        /// <param name="price"></param>
        /// <returns></returns>
        public static decimal GetRoundedShares(decimal stake, decimal price)
        {
            decimal numOfShares = stake / price;

            double decimalPlaces = 2;
            var power = Convert.ToDecimal(Math.Pow(10, decimalPlaces));
            numOfShares = Math.Floor(numOfShares * power) / power;

            return numOfShares;
        }
    }
}