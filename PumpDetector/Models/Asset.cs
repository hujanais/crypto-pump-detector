using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace PumpDetector.Models
{
    public class Asset
    {
        NLog.Logger logger = NLog.LogManager.GetLogger("*");

        public string BaseCurrency { get; set; }
        public string Ticker { get; set; }
        public DateTime TimeStamp { get; set; }
        public double Volume { get; private set; }
        public decimal BuyPrice { get; set; }
        public decimal SellPrice { get; set; }
        public decimal StopLoss { get; set; }
        public bool HasTrade { get; set; }
        public decimal OpenPrice { get; private set; }
        public decimal HighPrice { get; private set; }
        public decimal LowPrice { get; private set; }
        public decimal ClosePrice { get; private set; }
        public decimal Price { get; set; }
        public decimal Bid { get; set; }
        public decimal Ask { get; set; }
        public decimal MaxPrice { get; set; }

        public decimal RSI { get; set; }

        public decimal WalletSize { get; set; }

        /// <summary>
        /// Use to handle cooldown time.
        /// </summary>
        public DateTime LastSellTime { get; set; }

        /// <summary>
        /// Used to prevent immediate trade out after buying.  
        /// </summary>
        public DateTime LastBuyTime { get; set; }

        /// <summary>
        /// Flag to indicate that we are now in active trailing stop management.
        /// </summary>
        public bool IsActiveTrailingStops { get; set; }

        /// <summary>
        /// Signal whether this is a real pump
        /// Open price lower 1/3 candle
        /// Close price upper 1/3 candle
        /// 
        /// </summary>
        //public bool IsGreenCandle
        //{
        //    get
        //    {
        //        var candlelength = HighPrice - LowPrice;
        //        var deltaOpen = OpenPrice - LowPrice;
        //        var deltaClose = ClosePrice - LowPrice;
        //        var iSOpenGood = deltaOpen < (0.3333m * candlelength);
        //        var isCloseGood = deltaClose > (0.90m * candlelength);

        //        return iSOpenGood & isCloseGood;
        //    }
        //}

        public decimal PL
        {
            get
            {
                if (BuyPrice != 0)
                {
                    return ((SellPrice - BuyPrice) / BuyPrice) * 100;
                }
                else
                {
                    return 0;
                }
            }
        }

        public Asset ()
        {
            this.Reset();
        }

        #region Methods

        public void Reset()
        {
            this.BuyPrice = 0;
            this.SellPrice = 0;
            this.StopLoss = 0;
            this.MaxPrice = 0;
            this.HasTrade = false;
            this.IsActiveTrailingStops = false;
        }

        public void UpdatePrices(decimal last, decimal ask, decimal bid)
        {
            Price = last;
            Ask = ask;
            Bid = bid;
        }

        public void UpdateOHLC(DateTime ts, decimal o, decimal h, decimal l, decimal c, double vol)
        {
            this.TimeStamp = ts;
            this.OpenPrice = o;
            this.HighPrice = h;
            this.LowPrice = l;
            this.ClosePrice = c;
            this.Volume = vol;
        }

        /// <summary>
        /// Re-evalute stoploss based on the maximum price.
        /// </summary>
        /// <param name="percent">percentage to start trailing stoploss.</param>
        public void adjustStopLoss(double percent)
        {
            double deltaP = (double)(this.Price / this.BuyPrice);

            double threshold = 1.0 + percent / 100.0;
            if (deltaP > threshold)
            {
                if (!IsActiveTrailingStops)
                {
                    logger.Trace($"Activate OCO on {Ticker}.");
                }
                IsActiveTrailingStops = true; // turn on active trailing stops.
            }

            if (IsActiveTrailingStops)
            {
                if (this.Price > this.MaxPrice)
                {
                    this.MaxPrice = this.Price;
                    // set stoploss to be 1% under maxprice.
                    this.StopLoss = 0.99m * this.MaxPrice;
                    Debug.Write($"Adjust TrailingStopLoss: {this.StopLoss: 0.00}");
                }
            }
        }

        #endregion
    }
}
