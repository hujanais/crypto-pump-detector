using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace PumpDetector.Models
{
    public class Asset
    {
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

        /// <summary>
        /// Use to handle cooldown time.
        /// </summary>
        public DateTime LastSellTime { get; set; }

        /// <summary>
        /// Used to prevent immediate trade out after buying.  
        /// </summary>
        public DateTime LastBuyTime { get; set; }

        /// <summary>
        /// Cooldown period of 60 minutes after last sell.
        /// </summary>
        public bool CanBuy
        {
            get
            {
                var timeElapsed = DateTime.UtcNow - LastSellTime;
                return timeElapsed.TotalMinutes > 60;
            }
        }

        /// <summary>
        /// Cooldown period of 1 minute after buy before enabling stoploss setting.
        /// </summary>
        public bool CanSell
        {
            get
            {
                var timeElapsed = DateTime.UtcNow - LastBuyTime;
                return timeElapsed.TotalMinutes > 1;

            }
        }

        public decimal PL
        {
            get => ((SellPrice - BuyPrice) / BuyPrice) * 100;
        }

        public decimal percentagePriceChange
        {
            get
            {
                var percentage = (this.ClosePrice - this.OpenPrice) / this.OpenPrice * 100;
                return percentage;
            }
        }

        #region Methods

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
        /// <param name="asset"></param>
        public void adjustStopLoss()
        {
            if (this.Price > this.MaxPrice)
            {
                this.MaxPrice = this.Price;

                // set stoploss to be 1% under maxprice.
                this.StopLoss = 0.99m * this.MaxPrice;
            }
        }

        #endregion
    }
}
