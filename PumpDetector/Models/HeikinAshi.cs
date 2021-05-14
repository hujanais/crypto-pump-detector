using Skender.Stock.Indicators;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace PumpDetector.Models
{
    public class HeikinAshi
    {
        public enum HeikinAshiSignal
        {
            STRONGBUY,
            BUY,
            STRONGSELL,
            SELL,
            FLAT,
        }

        private HeikinAshiSignal signal = HeikinAshiSignal.FLAT;
        const decimal epsilon = 0.01m;

        public HeikinAshi(HeikinAshiResult rawHeikinAshi)
        {
            // Hollow or green candles with no lower "shadows" indicate a strong uptrend: 
            var isGreen = rawHeikinAshi.Close > rawHeikinAshi.Open;
            var isRed = !isGreen;

            // calculate the body length relative to total length.
            var bodyLength = Math.Abs(rawHeikinAshi.Close - rawHeikinAshi.Open);
            var totalLength = Math.Abs(rawHeikinAshi.High - rawHeikinAshi.Low);
            var bodyPercentage = bodyLength / totalLength;

            this.signal = HeikinAshiSignal.FLAT;
            if (bodyPercentage >= 0.5m)
            {
                if (isGreen)
                {
                    var tailLength = Math.Abs((rawHeikinAshi.Open - rawHeikinAshi.Low) / rawHeikinAshi.Open);
                    if (tailLength < epsilon)
                    {
                        // bullish
                        this.signal = HeikinAshiSignal.STRONGBUY;
                    } else
                    {
                        this.signal = HeikinAshiSignal.BUY;
                    }
                }

                if (isRed)
                {
                    var headLength = Math.Abs((rawHeikinAshi.High - rawHeikinAshi.Open) / rawHeikinAshi.High);
                    if (headLength < epsilon)
                    {
                        // bearish
                        this.signal = HeikinAshiSignal.STRONGSELL;
                    } else
                    {
                        this.signal = HeikinAshiSignal.SELL;
                    }
                }
            }

            //Debug.WriteLine($"{rawHeikinAshi.Open:0.0000}, {rawHeikinAshi.High:0.0000}, {rawHeikinAshi.Low:0.0000}, {rawHeikinAshi.Close:0.0000}, {bodyPercentage:0.0000}, {this.signal}");
        }
    
        public HeikinAshiSignal Signal
        {
            get => this.signal;
            set => this.signal = value;
        }
    }
}
