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
        decimal balanceLowWaterMark = 1000m;
        string QUOTECURRENCY = "USD";
        decimal PUMPTHRESHOLDPERCENT = 2;
        double PUMPVOLUMETIMES = 2;

        const int FIVEMINUTES = 5 * 60 * 1000;
        Timer timer = null;


        // https://makolyte.com/nlog-split-trace-logging-into-its-own-file/
        NLog.Logger logger = NLog.LogManager.GetLogger("*");

        public Engine()
        {
            api = new ExchangeBinanceUSAPI();

            // load in the api keys.
            api.LoadAPIKeysUnsecure(ConfigurationManager.AppSettings.Get("PublicKey"), ConfigurationManager.AppSettings.Get("SecretKey"));

            logger.Trace($"Starting {QUOTECURRENCY} trading. LiveTrading={isLiveTrading}, PumpThreshold={PUMPTHRESHOLDPERCENT}, StakeSize={stakeSize}, LowWaterMark={balanceLowWaterMark}");
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
            var remainder = (currentTime.Minute * 60 + currentTime.Second) % 300;
            var secondsAway = 300 - remainder + 15;// don't start exactly on the hour so that the server has prepared the 5minute candle.  offset by 15 seconds.

            logger.Trace($"App starting in {secondsAway} seconds.");

            // Update 5-minute candle once per 5-minutes.
            timer = new Timer(async (objState) => await doWork(objState));
            timer.Change(secondsAway * 1000, FIVEMINUTES); 

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
                            asset.UpdatePrices(foundTkr.Value.Last, foundTkr.Value.Ask, foundTkr.Value.Bid);

                            if (asset.HasTrade)
                            {
                                asset.adjustStopLoss();
                            
                                if (asset.Price < asset.StopLoss && asset.CanSell)
                                {
                                    this.doSell(asset);
                                }
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

            logger.Trace($"Wallet: {myWallet[QUOTECURRENCY]}");

            int numTickers = this.Assets.Count();

            for (int i = 0; i < numTickers; i++)
            {
                var ticker = this.Assets[i].Ticker;
                try
                {
                    var candle = (await api.GetCandlesAsync(ticker, 5*60, null, null, 100)).ToArray();
                    var ohlc = candle[candle.Length - 2];   // note that this is called a minute after the 15/30/45/60 minute so we need to look at the previous candle.
                    var ohlcPrevious = candle[candle.Length - 3];
                    var asset = this.Assets[i];
                    asset.UpdateOHLC(ohlc.Timestamp, ohlc.OpenPrice, ohlc.HighPrice, ohlc.LowPrice, ohlc.ClosePrice, ohlc.QuoteCurrencyVolume);
                    Console.Write(".");

                    bool volumeTrigger = ohlc.QuoteCurrencyVolume > (PUMPVOLUMETIMES * ohlcPrevious.QuoteCurrencyVolume);
                    bool priceTrigger = (asset.percentagePriceChange > PUMPTHRESHOLDPERCENT);

                    if (volumeTrigger && priceTrigger && !asset.HasTrade)
                    {
                        if (asset.IsGreenCandle)
                        {
                            logger.Trace($"{asset.Ticker}, {ohlc.QuoteCurrencyVolume}, {PUMPVOLUMETIMES * ohlcPrevious.QuoteCurrencyVolume}, {asset.percentagePriceChange}");
                            doBuy(asset);  // initial stoploss calculated in here.
                        } else
                        {
                            logger.Trace($"{asset.Ticker}. IsGreenCandle not met. {asset}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.Trace(ex);
                }
            }

            // print out some trade summary
            var tradedAssets = this.Assets.Where(a => a.HasTrade);
            var timeStamps = this.Assets.Select(a => a.TimeStamp).Distinct();
            logger.Trace(String.Join(",", timeStamps));
            foreach (var tA in tradedAssets)
            {
                logger.Trace($"{tA.Ticker}, {tA.BuyPrice:0.000}, { tA.StopLoss:0.000}, {tA.ClosePrice:0.000}, {tA.IsActiveTrailingStops}");
            }

            logger.Trace("End getCandles");
        }

        public async void doBuy(Asset asset)
        {
            try
            {
                bool isSuccess = false;
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

                    if (result.Result != ExchangeAPIOrderResult.Error)
                    {
                        isSuccess = true;
                        // manually reduce the walletsize.  it will be for real once every 15 minutes.
                        myWallet[QUOTECURRENCY] = cashAvail - stakeSize;
                    }
                }

                if (isSuccess || !isLiveTrading)
                {
                    asset.HasTrade = true;
                    asset.BuyPrice = asset.Ask;
                    asset.LastBuyTime = DateTime.UtcNow;

                    // set the initial stoploss to be 50% of the candle.
                    var midpoint = (asset.LowPrice + asset.HighPrice) / 2;
                    asset.StopLoss = midpoint;

                    logger.Info($"Buy, {asset.Ticker}, BuyPrice: {asset.BuyPrice}, StopLoss: {asset.StopLoss}, {asset.percentagePriceChange:0.00}");
                }
            }
            catch (Exception ex)
            {
                logger.Info($"Buy - ERROR, {asset.Ticker}");
                logger.Trace($"Buy {asset.Ticker} failed with {ex.Message}");
            }
        }

        public async void doSell(Asset asset)
        {
            try
            {
                asset.HasTrade = false; // clear the trade no matter what.  set this flag early to prevent over firing from websocket.

                if (isLiveTrading)
                {
                    // check to see if we have anything to sell.
                    decimal amountAvail = 0;

                    // if wallet is empty, refresh wallet once.
                    if (!myWallet.ContainsKey(asset.BaseCurrency))
                    {
                        myWallet = await getWallet();
                    }

                    if (myWallet.ContainsKey(asset.BaseCurrency))
                    {
                        amountAvail = myWallet[asset.BaseCurrency];

                        logger.Trace($"TrySell: PlaceOrderAsync. {asset.Ticker}. Amount={amountAvail}");

                        var result = await api.PlaceOrderAsync(new ExchangeOrderRequest
                        {
                            Amount = amountAvail,
                            IsBuy = false,
                            Price = asset.Bid,
                            MarketSymbol = asset.Ticker
                        });

                        logger.Trace($"Sell: PlaceOrderAsync. {result.MarketSymbol}. Price: ${result.Price}.  Result={result.Result}.  OrderId={result.OrderId}");
                    }
                    else
                    {
                        throw new Exception("Insufficient fund");
                    }
                }

                asset.HasTrade = false;
                asset.SellPrice = asset.Bid;
                asset.LastSellTime = DateTime.UtcNow;
                logger.Info($"Sell, {asset.Ticker}, {asset.BuyPrice}, {asset.SellPrice}, {asset.StopLoss}, {asset.PL:0.00}");

            }
            catch (Exception ex)
            {
                logger.Info($"Sell {asset.Ticker} failed with {ex.Message}");
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

        //public async void BackTest()
        //{

        //    logger.Trace("Starting BackTest");
        //    this.isLiveTrading = false;

        //    IList<Asset> CompletedTrades = new List<Asset>();

        //    bool hasTrade = false;
        //    decimal buyPrice = 0;
        //    decimal sellPrice = 0;
        //    decimal stopLossPrice = 0;
        //    double percentageVolume = 0.0;
        //    DateTime buyTime = DateTime.Now;
        //    DateTime sellTime = DateTime.Now;
        //    Asset trade = null;

        //    // conditions.

        //    decimal STOPLOSSPERCENT = 0.5m;

        //    int volumeWindow = 4;

        //    // coins to remove like stable-coins and BTC, ETH.
        //    string[] coinsToRemove = { "USDC", "BUSD", "USDT", "DAI", "BTC", "ETH", "PAX", "XRP" };

        //    var tickers = await api.GetTickersAsync();
        //    tickers = tickers.Where(t => t.Value.Volume.QuoteCurrency == "USD" &&
        //        !coinsToRemove.Contains(t.Value.Volume.BaseCurrency) // && t.Key.Contains("B")
        //    ).Take(500);

        //    foreach (var ticker in tickers)
        //    {
        //        this.Assets.Add(new Asset { Ticker = ticker.Key, BaseCurrency = ticker.Value.Volume.BaseCurrency });
        //    }

        //    int numTickers = this.Assets.Count();
        //    for (int idx = 0; idx < numTickers; idx++)
        //    {
        //        var asset = this.Assets[idx];

        //        // get all 15-minute historical candle data.
        //        var candle = (await api.GetCandlesAsync(asset.Ticker, 15 * 60, null, null, 500)).ToArray();

        //        // generate SMA 7, 25.
        //        List<IQuote> ohlcs = new List<IQuote>();
        //        foreach (var pt in candle)
        //        {
        //            ohlcs.Add(new Quote { Open = pt.OpenPrice, High = pt.HighPrice, Low = pt.LowPrice, Close = pt.ClosePrice, Date = pt.Timestamp, Volume = (decimal)pt.QuoteCurrencyVolume });
        //        }

        //        var sma7 = Indicator.GetSma(ohlcs, 7).ToArray();
        //        var sma25 = Indicator.GetSma(ohlcs, 25).ToArray();

        //        // candle, sma7, sma25, priceChange%, isPump
        //        List<Tuple<MarketCandle, decimal, decimal, decimal, bool>> arr = new List<Tuple<MarketCandle, decimal, decimal, decimal, bool>>();

        //        for (int i = volumeWindow; i < candle.Count() - 4; i++)
        //        {
        //            var ts = candle[i].Timestamp;
        //            var openPrice = candle[i].OpenPrice;
        //            var closePrice = candle[i].ClosePrice;
        //            var highPrice = candle[i].HighPrice;
        //            var lowPrice = candle[i].LowPrice;
        //            var volume = candle[i].QuoteCurrencyVolume;
        //            var percentage = (closePrice - openPrice) / openPrice * 100;
        //            var avgVolume = candle.Skip(i - volumeWindow).Take(volumeWindow).Average(p => p.QuoteCurrencyVolume);

        //            var isPump = (candle[i + 1].ClosePrice > candle[i].ClosePrice) && (candle[i + 2].ClosePrice > candle[i + 1].ClosePrice);

        //            arr.Add(new Tuple<MarketCandle, decimal, decimal, decimal, bool>(candle[i], sma7[i].Sma.GetValueOrDefault(), sma25[i].Sma.GetValueOrDefault(), percentage, isPump));

        //            asset.UpdateOHLC(ts, openPrice, highPrice, lowPrice, closePrice, volume);

        //            // don't really have ask/bid data so simulate.
        //            asset.Price = asset.ClosePrice;
        //            asset.Ask = asset.Price;
        //            asset.Bid = asset.Price;

        //            // adjust the stoploss.
        //            if (asset.HasTrade)
        //            {
        //                asset.adjustStopLoss();
        //            }

        //            if (asset.HasTrade)
        //            {
        //                // pull the 1 minute candle.
        //                var candle1 = (await api.GetCandlesAsync(asset.Ticker, 1 * 60, asset.LastBuyTime.AddMinutes(1), null, 500)).ToArray();
        //                foreach (var c in candle1)
        //                {
        //                    if (c.LowPrice < asset.StopLoss)
        //                    {
        //                        asset.Bid = c.LowPrice;
        //                        this.doSell(asset);
        //                    }
        //                }

        //            }

        //            // buy condition
        //            if (asset.percentagePriceChange > PUMPTHRESHOLDPERCENT && !asset.HasTrade)
        //            {
        //                // the buy is happening on the candle + 1.
        //                asset.Price = candle[i].OpenPrice;
        //                doBuy(asset);
        //                asset.LastBuyTime = candle[i].Timestamp;
        //            }

        //            // simulate sell strategy.
        //            //if (hasTrade && trade != null)
        //            //{
        //            //    if (candle[i].LowPrice < stopLossPrice)
        //            //    {
        //            //        // if low price is under stoploss, exit.
        //            //        hasTrade = false;
        //            //        sellPrice = candle[i].LowPrice;
        //            //        sellTime = candle[i].Timestamp;
        //            //        var pl = (stopLossPrice - buyPrice) / buyPrice * 100;
        //            //        // Debug.WriteLine($"{ticker.Key} : {buyTime} to {sellTime}. {pl:0.00}");
        //            //        trade.SellPrice = stopLossPrice;
        //            //        CompletedTrades.Add(trade);
        //            //        trade = null;
        //            //    }
        //            //    else if (candle[i].HighPrice > buyPrice)
        //            //    {
        //            //        // if high price is higher than buyprice, reset stoploss.
        //            //        stopLossPrice = candle[i].HighPrice * (1 - STOPLOSSPERCENT / 100);
        //            //        trade.StopLoss = stopLossPrice;
        //            //    }
        //            //}

        //            //if (percentage > PUMPTHRESHOLDPERCENT && !hasTrade && trade == null)
        //            //{
        //            //    trade = new Asset();
        //            //    hasTrade = true;
        //            //    buyPrice = candle[i + 1].OpenPrice;
        //            //    buyTime = candle[i + 1].Timestamp;
        //            //    percentageVolume = (candle[i].QuoteCurrencyVolume - avgVolume) / avgVolume * 100;

        //            //    trade.Ticker = ticker.Key;
        //            //    trade.TimeStamp = candle[i].Timestamp;
        //            //    trade.BuyPrice = buyPrice;

        //            //    stopLossPrice = buyPrice * (1 - STOPLOSSPERCENT / 100);
        //            //}
        //        }

        //        var sortedArray = arr.OrderByDescending(p => p.Item4);
        //    }

        //    var sortedTrade = CompletedTrades.OrderBy(c => c.TimeStamp);
        //    foreach (var ct in sortedTrade)
        //    {
        //        Debug.WriteLine($"{ct.Ticker}, {ct.TimeStamp}, {ct.BuyPrice}, {ct.SellPrice}, {ct.PL:0.00}");
        //    }
        //}
    }
}
