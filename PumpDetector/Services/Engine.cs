using System;
using System.Collections.Generic;
using System.Text;
using ExchangeSharp;
using System.Linq;
using LinqStatistics;
using System.Diagnostics;
using Skender.Stock.Indicators;
using PumpDetector.Models;
using System.Threading;
using System.Threading.Tasks;
using System.Configuration;

namespace PumpDetector.Services
{
    public class Engine : IDisposable
    {
        private ExchangeAPI api;
        private IWebSocket socket;
        private IList<Asset> Assets = new List<Asset>();
        private Dictionary<string, decimal> myWallet;

        private bool isLiveTrading = true;
        decimal stakeSize = 50m;
        decimal balanceLowWaterMark = 3200m;
        string QUOTECURRENCY = "USD";

        const int FIFTEENMINUTES = 15 * 60 * 1000;
        Timer timer = null;


        // https://makolyte.com/nlog-split-trace-logging-into-its-own-file/
        NLog.Logger logger = NLog.LogManager.GetLogger("*");

        public Engine()
        {
            api = new ExchangeBinanceUSAPI();

            // load in the api keys.
            api.LoadAPIKeysUnsecure(ConfigurationManager.AppSettings.Get("PublicKey"), ConfigurationManager.AppSettings.Get("SecretKey"));

            logger.Trace($"Starting {QUOTECURRENCY} trading. LiveTrading={isLiveTrading}, StakeSize={stakeSize}, LowWaterMark={balanceLowWaterMark}");
        }

        public void Dispose()
        {

        }

        public async void StartYourEngines()
        {
            logger.Trace("Enumerate Markets");

            // enumerate all markets
            // coins to remove like stable-coins and BTC, ETH.
            string[] coinsToRemove = { "USDC", "BUSD", "USDT", "DAI", "BTC", "ETH", "PAX", "XRP" };

            var tickers = await api.GetTickersAsync();
            tickers = tickers.Where(t => t.Value.Volume.QuoteCurrency == QUOTECURRENCY &&
                !coinsToRemove.Contains(t.Value.Volume.BaseCurrency)
            );

            logger.Trace($"Enumerated {tickers.Count()} markets.");

            foreach (var ticker in tickers)
            {
                this.Assets.Add(new Asset { Ticker = ticker.Key, BaseCurrency = ticker.Value.Volume.BaseCurrency });
            }

            var currentTime = DateTime.UtcNow;
            var minutesAway1 = 60 - currentTime.Minute;
            var minutesAway2 = 45 - currentTime.Minute;
            var minutesAway3 = 30 - currentTime.Minute;
            var minutesAway4 = 15 - currentTime.Minute;
            var minutesAway = new int[] { minutesAway1, minutesAway2, minutesAway3, minutesAway4 }.Where(a => a > 0).Min();

            logger.Trace($"App starting in {minutesAway} minutes.");

            // Update 15-minute candle once per 15-minutes.
            timer = new Timer(async (objState) => await doWork(objState));
            timer.Change((minutesAway * 60 + 15) * 1000, FIFTEENMINUTES); // don't start exactly on the hour so that the server has prepared the 15minute candle.  offset by 15 seconds.

            // start up web socket.
            if (socket == null)
            {
                var numOfTickers = this.Assets.Count();
                socket = await api.GetTickersWebSocketAsync(items =>
                {
                    for (int i = 0; i < numOfTickers; i++)
                    {
                        var tkr = this.Assets[i].Ticker;
                        var foundTkr = items.FirstOrDefault(p => p.Key == tkr);
                        var asset = this.Assets[i];
                        if (foundTkr.Value != null)
                        {

                            asset.Price = foundTkr.Value.Last;
                            asset.Ask = foundTkr.Value.Ask;
                            asset.Bid = foundTkr.Value.Bid;

                            // adjust the stoploss.
                            if (asset.HasTrade)
                            {
                                asset.adjustStopLoss();
                            }

                            if (asset.HasTrade && asset.CanSell && (asset.Price < asset.StopLoss))
                            {
                                this.doSell(asset);
                            }
                        }
                    }
                }, tickers.Select(t => t.Key).ToArray());
            }
        }

        public async Task doWork(object objState)
        {
            logger.Trace("Start getCandles");

            // update the wallet.
            this.myWallet = await getWallet();

            int numTickers = this.Assets.Count();
            for (int i = 0; i < numTickers; i++)
            {
                var ticker = this.Assets[i].Ticker;
                var candle = (await api.GetCandlesAsync(ticker, 900, null, null, 100)).ToArray();
                var ohlc = candle.Last();
                var asset = this.Assets[i];
                asset.UpdateOHLC(ohlc.Timestamp, ohlc.OpenPrice, ohlc.HighPrice, ohlc.LowPrice, ohlc.ClosePrice, ohlc.QuoteCurrencyVolume);

                if (asset.percentagePriceChange > 2.5m)
                {
                    logger.Trace($"Trigger: {asset.Ticker}, {asset.percentagePriceChange:0.00}, {asset.HasTrade}, {asset.CanBuy}");
                }

                if (asset.percentagePriceChange > 2.5m && !asset.HasTrade && asset.CanBuy)
                {
                    doBuy(asset);
                }
            }

            logger.Trace("End getCandles");
        }

        public async void doBuy(Asset asset)
        {
            try
            {
                if (this.isLiveTrading)
                {
                    decimal cashAvail = 0;
                    // check to see if we have enough cash.
                    if (myWallet.ContainsKey(QUOTECURRENCY))
                    {
                        cashAvail = myWallet[QUOTECURRENCY];
                    }
                    if (cashAvail < balanceLowWaterMark)
                    {
                        throw new Exception($"Insufficient funds to buy {asset.Ticker}.  Only have {cashAvail}");
                    }

                    // get the number of shares to buy.
                    var shares = RoundShares.GetRoundedShares(stakeSize, asset.Price);
                    var order = new ExchangeOrderRequest
                    {
                        Amount = shares,
                        IsBuy = true,
                        Price = asset.Ask,
                        MarketSymbol = asset.Ticker
                    };
                    
                    var result = await api.PlaceOrderAsync(order);
                    logger.Trace($"BUY: PlaceOrderAsync. {result.MarketSymbol} ${result.Price}.  {result.Result}. {result.OrderId}");

                    // manually reduce the walletsize.  it will be for real once every 15 minutes.
                    myWallet[QUOTECURRENCY] = cashAvail - stakeSize;
                }

                asset.HasTrade = true;
                asset.BuyPrice = asset.Ask;
                asset.StopLoss = 0.99m * asset.BuyPrice;
                asset.LastBuyTime = DateTime.UtcNow;

                logger.Info($"Buy, {asset.Ticker}, {asset.BuyPrice}, {asset.percentagePriceChange}");
            }
            catch (Exception ex)
            {
                logger.Info($"Buy - ERROR, {asset.Ticker}");
                logger.Trace($"Buy {asset.Ticker} failed with {ex.Message}");
            }
        }

        public void doSell(Asset asset)
        {
            try
            {
                if (isLiveTrading)
                {
                    // check to see if we have anything to sell.
                    decimal amountAvail = 0;
                    if (myWallet.ContainsKey(asset.BaseCurrency))
                    {
                        amountAvail = myWallet[asset.BaseCurrency];

                        var order = new ExchangeOrderRequest
                        {
                            Amount = amountAvail,
                            IsBuy = false,
                            Price = asset.Bid,
                            MarketSymbol = asset.Ticker
                        };
                    }
                }

                asset.HasTrade = false;
                asset.SellPrice = asset.Bid;
                asset.LastSellTime = DateTime.UtcNow;
                logger.Info($"Sell, {asset.Ticker}, {asset.BuyPrice}, {asset.SellPrice}, {asset.StopLoss}, {asset.PL:0.00}");

            } catch (Exception ex)
            {
                logger.Info($"Sell - ERROR, {asset.Ticker}");
                logger.Trace($"Sell {asset.Ticker} failed with {ex.Message}");
            } finally
            {
                asset.HasTrade = false; // something went wrong but we will just clear this trade.
            }
        }

        /// <summary>
        /// Return the latest wallet state.
        /// </summary>
        public async Task<Dictionary<string, decimal>> getWallet()
        {
            // update wallet if live trading.
            Dictionary<string, decimal> wallet = new Dictionary<string, decimal>();
            if (isLiveTrading)
            {
                wallet = await api.GetAmountsAvailableToTradeAsync();
            }

            return wallet;
        }

        public async void BackTest()
        {
            IList<Asset> CompletedTrades = new List<Asset>();

            bool hasTrade = false;
            decimal buyPrice = 0;
            decimal sellPrice = 0;
            decimal stopLossPrice = 0;
            double percentageVolume = 0.0;
            DateTime buyTime = DateTime.Now;
            DateTime sellTime = DateTime.Now;
            Asset trade = null;

            // conditions.
            decimal PUMPTHRESHOLDPERCENT = 2.5m;
            decimal STOPLOSSPERCENT = 0.5m;

            int volumeWindow = 4;

            // coins to remove like stable-coins and BTC, ETH.
            string[] coinsToRemove = { "USDC", "BUSD", "USDT", "DAI", "BTC", "ETH", "PAX", "XRP" };

            var tickers = await api.GetTickersAsync();
            tickers = tickers.Where(t => t.Value.Volume.QuoteCurrency == "USD" &&
                !coinsToRemove.Contains(t.Value.Volume.BaseCurrency) // && t.Key.Contains("B")
            ).Take(500);

            foreach (var ticker in tickers)
            {
                Debug.WriteLine(ticker);

                // get 15 minute candle data for the past 2 hours.
                var candle = (await api.GetCandlesAsync(ticker.Key, 15 * 60, null, null, 500)).ToArray();

                // generate SMA 7, 25.
                List<IQuote> ohlcs = new List<IQuote>();
                foreach (var pt in candle)
                {
                    ohlcs.Add(new Quote { Open = pt.OpenPrice, High = pt.HighPrice, Low = pt.LowPrice, Close = pt.ClosePrice, Date = pt.Timestamp, Volume = (decimal)pt.QuoteCurrencyVolume });
                }

                var sma7 = Indicator.GetSma(ohlcs, 7).ToArray();
                var sma25 = Indicator.GetSma(ohlcs, 25).ToArray();

                // candle, sma7, sma25, priceChange%, isPump
                List<Tuple<MarketCandle, decimal, decimal, decimal, bool>> arr = new List<Tuple<MarketCandle, decimal, decimal, decimal, bool>>();

                for (int i = volumeWindow; i < candle.Count() - 4; i++)
                {
                    var openPrice = candle[i].OpenPrice;
                    var closePrice = candle[i].ClosePrice;
                    var percentage = (closePrice - openPrice) / openPrice * 100;
                    var avgVolume = candle.Skip(i - volumeWindow).Take(volumeWindow).Average(p => p.QuoteCurrencyVolume);

                    var isPump = (candle[i + 1].ClosePrice > candle[i].ClosePrice) && (candle[i + 2].ClosePrice > candle[i + 1].ClosePrice);

                    arr.Add(new Tuple<MarketCandle, decimal, decimal, decimal, bool>(candle[i], sma7[i].Sma.GetValueOrDefault(), sma25[i].Sma.GetValueOrDefault(), percentage, isPump));

                    // simulate sell strategy.
                    if (hasTrade && trade != null)
                    {
                        if (candle[i].LowPrice < stopLossPrice)
                        {
                            // if low price is under stoploss, exit.
                            hasTrade = false;
                            sellPrice = candle[i].LowPrice;
                            sellTime = candle[i].Timestamp;
                            var pl = (stopLossPrice - buyPrice) / buyPrice * 100;
                            // Debug.WriteLine($"{ticker.Key} : {buyTime} to {sellTime}. {pl:0.00}");
                            trade.SellPrice = stopLossPrice;
                            CompletedTrades.Add(trade);
                            trade = null;
                        }
                        else if (candle[i].HighPrice > buyPrice)
                        {
                            // if high price is higher than buyprice, reset stoploss.
                            stopLossPrice = candle[i].HighPrice * (1 - STOPLOSSPERCENT / 100);
                            trade.StopLoss = stopLossPrice;
                        }
                    }

                    // buy condition when percentage > 3%.
                    if (percentage > PUMPTHRESHOLDPERCENT && !hasTrade && trade == null)
                    {
                        trade = new Asset();
                        hasTrade = true;
                        buyPrice = candle[i + 1].OpenPrice;
                        buyTime = candle[i + 1].Timestamp;
                        percentageVolume = (candle[i].QuoteCurrencyVolume - avgVolume) / avgVolume * 100;

                        trade.Ticker = ticker.Key;
                        trade.TimeStamp = candle[i].Timestamp;
                        trade.BuyPrice = buyPrice;

                        stopLossPrice = buyPrice * (1 - STOPLOSSPERCENT / 100);
                    }
                }

                var sortedArray = arr.OrderByDescending(p => p.Item4);
            }

            var sortedTrade = CompletedTrades.OrderBy(c => c.TimeStamp);
            foreach (var ct in sortedTrade)
            {
                Debug.WriteLine($"{ct.Ticker}, {ct.TimeStamp}, {ct.BuyPrice}, {ct.SellPrice}, {ct.PL:0.00}");
            }
        }
    }
}
