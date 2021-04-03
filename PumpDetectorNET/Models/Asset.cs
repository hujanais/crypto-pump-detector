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
        /// Prevent selling of coin for first 3 minutes after buy.
        /// </summary>
        public bool CanSell
        {
            get
            {
                var timeElapsed = DateTime.UtcNow - LastBuyTime;
                return timeElapsed.TotalSeconds > (3 * 60);
            }
        }

        /// <summary>
        /// Signal whether this is a real pump
        /// Open price lower 1/3 candle
        /// Close price upper 1/3 candle
        /// 
        /// </summary>
        public bool IsGreenCandle
        {
            get
            {
                var candlelength = HighPrice - LowPrice;
                var deltaOpen = OpenPrice - LowPrice;
                var deltaClose = ClosePrice - LowPrice;
                var iSOpenGood = deltaOpen < (0.3333m * candlelength);
                var isCloseGood = deltaClose > (0.6667m * candlelength);

                return iSOpenGood & isCloseGood;
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

        public override string ToString()
        {
            return $"{Ticker}, {OpenPrice:0.0000}, {HighPrice: 0.0000}, {LowPrice: 0.0000}, {ClosePrice: 0.0000}, {Volume}, {IsGreenCandle}, {percentagePriceChange:0.00}, {PL:0.00}, {HasTrade}";
        }

        // Reset everthing after selling the asset.
        public void Reset()
        {

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
        /// <param name="asset"></param>
        public void adjustStopLoss()
        {
            // re-adjust stoploss when profit > 3%.
            double plPercent = (double)((this.Price - this.BuyPrice) / this.BuyPrice);

            if (plPercent > 0.03)
            {
                if (!IsActiveTrailingStops)
                {
                    logger.Trace($"Activate OCO on {Ticker}.");
                }
                IsActiveTrailingStops = true; // turn on active trailing stops.
                this.MaxPrice = this.Price;
            }

            if (IsActiveTrailingStops)
            {
                if (this.Price > this.MaxPrice) {
                    this.MaxPrice = this.Price;
                    // set stoploss to be 1% under maxprice.
                    this.StopLoss = 0.99m * this.MaxPrice;
                }
            }
        }

        #endregion
    }
}
