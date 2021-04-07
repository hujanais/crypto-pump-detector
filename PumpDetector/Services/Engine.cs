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
        decimal stakeSize = 50m;
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

            // startup the kline websocket
            // await klineService.GetWebsocketKlines(this.Assets.Select(s => s.Ticker.ToLowerInvariant()), KlineInterval.kline_1m);

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
                    var candle = (await api.GetCandlesAsync(ticker, 5 * 60, null, null, 100)).ToArray();
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
                        }
                        else
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
                logger.Trace($"{tA.Ticker}, BuyPrice: {tA.BuyPrice:0.000}, StopLoss: { tA.StopLoss:0.000}, Price:{tA.Price:0.000}, ActiveTrailingStop: {tA.IsActiveTrailingStops}");
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
                    asset.MaxPrice = asset.BuyPrice;
                    asset.LastBuyTime = DateTime.UtcNow;

                    // set the initial stoploss as 0.5% of buy price.
                    asset.StopLoss = 0.99m*asset.BuyPrice;

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

            // coins to remove like stable-coins and BTC, ETH.
            string[] coinsToRemove = { "USDC", "BUSD", "USDT", "DAI", "BTC", "ETH", "PAX", "XRP" };

            var tickers = await api.GetTickersAsync();
            tickers = tickers.Where(t => t.Value.Volume.QuoteCurrency == "USD" &&
                !coinsToRemove.Contains(t.Value.Volume.BaseCurrency)
            ).Take(500);

            foreach (var ticker in tickers)
            {
                BackTestAssets.Add(new Asset { Ticker = ticker.Key, BaseCurrency = ticker.Value.Volume.BaseCurrency });
            }

            int numTickers = BackTestAssets.Count();
            for (int idx = 0; idx < numTickers; idx++)
            {
                var asset = BackTestAssets[idx];

                // get all 5-minute historical candle data.
                var candles = (await api.GetCandlesAsync(asset.Ticker, 5 * 60, null, null, 500)).ToArray();

                hasTrade = false;

                for (int j = 1; j < candles.Count() - 1; j++)
                {
                    var ohlc = candles[j];
                    var ohlcPrevious = candles[j - 1];

                    asset.UpdateOHLC(ohlc.Timestamp, ohlc.OpenPrice, ohlc.HighPrice, ohlc.LowPrice, ohlc.ClosePrice, ohlc.QuoteCurrencyVolume);

                    // check for sell condition
                    if (hasTrade)
                    {
                        var plPercentage = (asset.ClosePrice - asset.BuyPrice) / asset.BuyPrice * 100;
                        if (asset.LowPrice < asset.StopLoss ||
                            asset.HighPrice > (1.03m * asset.BuyPrice))
                        {
                            Debug.WriteLine($"{asset.Ticker}, {plPercentage:0.00}");
                            hasTrade = false;
                        }
                    }

                    // check for buy condition.
                    if (!hasTrade)
                    {
                        bool volumeTrigger = ohlc.QuoteCurrencyVolume > (2 * ohlcPrevious.QuoteCurrencyVolume);
                        bool priceTrigger = (asset.percentagePriceChange > 2);
                        bool isGreenCandle = asset.IsGreenCandle;

                        if (volumeTrigger && priceTrigger && isGreenCandle)
                        {
                            asset.BuyPrice = candles[j + 1].OpenPrice;  // buy the opening price of next candle.
                            //asset.StopLoss = 0.99m * asset.BuyPrice;
                            asset.StopLoss = (candles[j].HighPrice + candles[j].LowPrice) / 2;
                            hasTrade = true;
                        }
                    }
                }

            }

            Debug.WriteLine("========= end backtesting =========");
        }
    }
}
