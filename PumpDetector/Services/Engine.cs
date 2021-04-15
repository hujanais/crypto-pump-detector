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
        private KlineService klineService;
        private IWebSocket socket;
        private IList<Asset> Assets = new List<Asset>();
        private Dictionary<string, decimal> myWallet;

        private bool isLiveTrading = true;
        decimal stakeSize = 200m;
        decimal balanceLowWaterMark = 1000m;
        string QUOTECURRENCY = "USD";
        decimal PUMPTHRESHOLDPERCENT = 2.5m;
        double PUMPVOLUMETIMES = 3;

        const int FIVEMINUTES = 5 * 60 * 1000;
        Timer timer = null;

        // https://makolyte.com/nlog-split-trace-logging-into-its-own-file/
        NLog.Logger logger = NLog.LogManager.GetLogger("*");

        public Engine()
        {
            api = new ExchangeBinanceUSAPI();

            this.klineService = new KlineService(api);

            // load in the api keys.
            api.LoadAPIKeysUnsecure(ConfigurationManager.AppSettings.Get("PublicKey"), ConfigurationManager.AppSettings.Get("SecretKey"));

            logger.Trace($"Starting {QUOTECURRENCY} trading. LiveTrading={isLiveTrading}, PumpVolumeThreshold={PUMPVOLUMETIMES}, PumpThreshold={PUMPTHRESHOLDPERCENT}, StakeSize={stakeSize}, LowWaterMark={balanceLowWaterMark}");
        }

        public void Dispose()
        {

        }

        public async void StartYourEngines()
        {
            logger.Trace("Enumerate Markets");

            // enumerate all markets
            // coins to remove like stable-coins and BTC, ETH.
            string[] coinsToRemove = { "USDC", "BUSD", "USDT", "DAI", "BTC", "ETH", "PAX", "XRP", "MKR" };

            var tickers = await api.GetTickersAsync();
            tickers = tickers.Where(t => t.Value.Volume.QuoteCurrency == QUOTECURRENCY &&
                !coinsToRemove.Contains(t.Value.Volume.BaseCurrency)
            );

            logger.Trace($"Enumerated {tickers.Count()} markets.");

            // Recovery bot from crash.
            var openTickers = new ValueTuple<string, decimal>[] {
                ("ONTUSD", 1.8626m),
                ("KNCUSD", 3.458m),
                ("HNTUSD", 15.2751m),
                ("XTZUSD", 6.3322m),
                ("ENJUSD", 2.83m),
                ("ICXUSD", 2.4107m),
                ("ALGOUSD", 1.424m),
            };

            foreach (var ticker in tickers)
            {
                var newAsset = new Asset { Ticker = ticker.Key, BaseCurrency = ticker.Value.Volume.BaseCurrency };

                var foundTkr = openTickers.FirstOrDefault(o => o.Item1 == newAsset.Ticker);
                if (foundTkr.Item1 != null)
                {
                    newAsset.HasTrade = true;
                    newAsset.BuyPrice = foundTkr.Item2;
                }
                this.Assets.Add(newAsset);
            }

            var currentTime = DateTime.UtcNow;
            int PERIODSECS = 1800;  // every 30mins.
            var remainder = (currentTime.Minute * 60 + currentTime.Second) % PERIODSECS;
            var secondsAway = PERIODSECS - remainder + 60;// don't start exactly on the hour so that the server has prepared the 5minute candle.  offset by 60 seconds.

            logger.Trace($"App starting in {secondsAway} seconds.");

            // Update 5-minute candle once per 5-minutes.
            timer = new Timer(async (objState) => await doWork(objState));
            timer.Change(secondsAway * 1000, PERIODSECS * 1000);
        }

        public async Task doWork(object objState)
        {
            logger.Trace("Start getCandles");

            // retry 10 times and if still fails, just skip this timestamp.
            for (int retry = 0; retry < 10; retry++)
            {
                try
                {
                    // update the wallet.
                    this.myWallet = await getWallet();
                    logger.Trace($"Wallet: {myWallet[QUOTECURRENCY]}");
                    break;
                } catch (Exception ex)
                {
                    logger.Trace($"retry-{retry} wallet. {ex}");

                    if (retry >= 9)
                    {
                        throw new Exception("Give up after 10 tries");
                    }

                    Thread.Sleep(30000);
                }
            }

            int numTickers = this.Assets.Count();

            for (int i = 0; i < numTickers; i++)
            {
                var asset = this.Assets[i];
                var ticker = asset.Ticker;
                try
                {
                    Console.Write(".");
                    var candles = (await api.GetCandlesAsync(ticker, 30 * 60, null, null, 200)).ToArray();
                    var cc = candles[candles.Count() - 2];

                    IList<Quote> history = new List<Quote>();
                    foreach (var candle in candles)
                    {
                        history.Add(new Quote
                        {
                            Date = candle.Timestamp,
                            Open = candle.OpenPrice,
                            High = candle.HighPrice,
                            Low = candle.LowPrice,
                            Close = candle.ClosePrice,
                            Volume = (decimal)candle.QuoteCurrencyVolume
                        });
                    }

                    // calculate RSI(14)
                    IList<RsiResult> rsi = Indicator.GetRsi(history, 14).ToArray();
                    var completedRSI = rsi[rsi.Count - 2];

                    asset.UpdateOHLC(cc.Timestamp, cc.OpenPrice, cc.HighPrice, cc.LowPrice, cc.ClosePrice, cc.QuoteCurrencyVolume);
                    asset.RSI = completedRSI.Rsi.Value;

                    // remember to throw out the last candle because that is the active candle that is not completed yet.
                    if (completedRSI.Rsi < 30 && !asset.HasTrade)
                    {
                        var et = await api.GetTickerAsync(asset.Ticker);
                        asset.UpdatePrices(et.Last, et.Ask, et.Bid);
                        doBuy(asset);
                    }
                    else if (completedRSI.Rsi > 70 && asset.HasTrade)
                    {
                        var et = await api.GetTickerAsync(asset.Ticker);
                        asset.UpdatePrices(et.Last, et.Ask, et.Bid);
                        doSell(asset);
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
                logger.Trace($"{tA.Ticker}, BuyPrice: {tA.BuyPrice:0.000}, ClosePrice:{tA.ClosePrice:0.000}, RSI: {tA.RSI:0.00}");
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
                        MarketSymbol = asset.Ticker,
                        OrderType = OrderType.Market
                    };

                    var result = await api.PlaceOrderAsync(order);
                    logger.Trace($"TRY BUY, {result.MarketSymbol}, {result.Price}, {result.Result}, {result.OrderId}");

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
                    asset.MaxPrice = asset.BuyPrice;
                    asset.LastBuyTime = DateTime.UtcNow;

                    // set the initial stoploss as 0.5% of buy price.
                    asset.StopLoss = 0.99m * asset.BuyPrice;

                    logger.Info($"Buy, {asset.Ticker}, BuyPrice: {asset.BuyPrice}, StopLoss: {asset.StopLoss}");
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
                        var quoteCurrencyValue = amountAvail * asset.Price;

                        logger.Trace($"TrySell: PlaceOrderAsync. {asset.Ticker}. Amount={amountAvail}");
                        var result = await api.PlaceOrderAsync(new ExchangeOrderRequest
                        {
                            Amount = amountAvail,
                            IsBuy = false,
                            Price = asset.Bid,
                            MarketSymbol = asset.Ticker,
                            OrderType = OrderType.Market
                        });

                        logger.Trace($"Sell: PlaceOrderAsync. {result.MarketSymbol}. Price: ${result.Price}.  Result={result.Result}.  OrderId={result.OrderId}");
                    }
                    else
                    {
                        throw new Exception("Insufficient fund");
                    }
                }

                asset.SellPrice = asset.Bid;
                asset.LastSellTime = DateTime.UtcNow;
                logger.Info($"Sell, {asset.Ticker}, {asset.BuyPrice}, {asset.SellPrice}, {asset.StopLoss}, {asset.PL:0.00}");
            }
            catch (Exception ex)
            {
                logger.Info($"Sell {asset.Ticker} failed with {ex.Message}");
            }
            finally
            {
                asset.Reset();  // reset the trade even in failure.
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
            logger.Trace("Starting BackTest");

            IList<Asset> BackTestAssets = new List<Asset>();
            IList<Asset> CompletedTrades = new List<Asset>();

            // flags.
            bool hasTrade = false;
            MarketCandle buyCandle = null, sellCandle = null;

            // coins to remove like stable-coins and BTC, ETH.
            string[] coinsToRemove = { "USDC", "BUSD", "USDT", "DAI", "BTC", "ETH", "PAX", "XRP" };

            var tickers = await api.GetTickersAsync();
            tickers = tickers.Where(t => t.Value.Volume.QuoteCurrency == "USD" &&
                !coinsToRemove.Contains(t.Value.Volume.BaseCurrency)
            );

            foreach (var ticker in tickers)
            {
                BackTestAssets.Add(new Asset { Ticker = ticker.Key, BaseCurrency = ticker.Value.Volume.BaseCurrency });
            }

            int numTickers = BackTestAssets.Count();
            for (int idx = 0; idx < numTickers; idx++)
            {
                var asset = BackTestAssets[idx];

                // get 1-hour candles
                var candles = (await api.GetCandlesAsync(asset.Ticker, 30 * 60, null, null, 500)).ToArray();

                IList<Quote> history = new List<Quote>();
                foreach (var candle in candles)
                {
                    history.Add(new Quote
                    {
                        Date = candle.Timestamp,
                        Open = candle.OpenPrice,
                        High = candle.HighPrice,
                        Low = candle.LowPrice,
                        Close = candle.ClosePrice,
                        Volume = (decimal)candle.QuoteCurrencyVolume
                    });
                }

                // calculate RSI(14)
                IList<RsiResult> rsi = Indicator.GetRsi(history, 14).ToArray();

                hasTrade = false;
                for (int j = 0; j < candles.Length - 1; j++)
                {
                    if (rsi[j].Rsi < 30 && !hasTrade)
                    {
                        hasTrade = true;
                        buyCandle = candles[j + 1];
                        // Debug.WriteLine($"Buy, {buyCandle.OpenPrice}, {buyCandle.HighPrice}");
                    }
                    else if (rsi[j].Rsi > 70 && hasTrade)
                    {
                        hasTrade = false;
                        sellCandle = candles[j + 1];
                        var lowPL = (sellCandle.LowPrice - buyCandle.HighPrice) / buyCandle.HighPrice * 100;
                        var pl = (sellCandle.OpenPrice - buyCandle.OpenPrice) / buyCandle.OpenPrice * 100;
                        Debug.WriteLine($"{asset.Ticker}, {buyCandle.Timestamp}, {sellCandle.Timestamp}, {sellCandle.LowPrice}, {sellCandle.HighPrice}, {lowPL:0.00}, {pl:0.00}");
                    }
                }
            }

            Debug.WriteLine("========= end backtesting =========");
        }

        public async void PeekAccount()
        {
            var openTickers = new ValueTuple<string, decimal>[] {
                ("ONTUSD", 1.8626m),
                ("KNCUSD", 3.458m),
                ("HNTUSD", 15.2751m),
                ("XTZUSD", 6.3322m),
                ("ENJUSD", 2.83m),
                ("ICXUSD", 2.4107m),
                ("ALGOUSD", 1.424m),
            };

            var response = (await api.GetTickersAsync()).ToList();
            foreach (var ot in openTickers)
            {
                var foundTkr = response.FirstOrDefault(r => r.Key == ot.Item1);
                var buyPrice = ot.Item2;
                var currentPrice = foundTkr.Value.Last;
                var plPercentage = (currentPrice - buyPrice) / buyPrice * 100;

                Debug.WriteLine($"{ot.Item1}, {plPercentage: 0.00}");
            }

        }
    }
}
