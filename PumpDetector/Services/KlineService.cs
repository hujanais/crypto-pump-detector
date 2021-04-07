using ExchangeSharp;
using PumpDetector.Models;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace PumpDetector.Services
{
    public class KlineService
    {
        IWebSocket klineSocket;
        Dictionary<string, List<MarketCandle>> TickerKLines = new Dictionary<string, List<MarketCandle>>();
        private ExchangeAPI api;

        NLog.Logger logger = NLog.LogManager.GetLogger("*");

        public KlineService (ExchangeAPI api)
        {
            this.api = api;
        }


        public async Task<IWebSocket> GetWebsocketKlines(IEnumerable<string> streamTickers, KlineInterval interval)
        {
            string combined = string.Join("/", streamTickers.Select(s => s.ToLower() + "@" + interval.ToString()));
            return await api.ConnectWebSocketAsync($"/stream?streams={combined}", (_socket, msg) =>
            {
                string json = msg.ToStringFromUTF8();
                //logger.Trace($"Kline Data:{json}");

                var klinedata = JsonConvert.DeserializeObject<KlineStream>(json);
                DateTime dt = CryptoUtility.ParseTimestamp(klinedata.Data.EventTime, TimestampType.UnixMilliseconds);
                /*
                logger.Trace($"EventTime:" + dt);
                logger.Trace($"KLineData:{klinedata.Data.kline.kLineClosed}:{klinedata.Data.MarketSymbol}:{klinedata.Data.kline.OpenTimestamp}");
                logger.Trace($"KLineData:{klinedata.Data.kline.kLineClosed}:{klinedata.Data.MarketSymbol}:{klinedata.Data.kline.Open}:{klinedata.Data.kline.High}:{klinedata.Data.kline.Low}:{klinedata.Data.kline.Close}");
                logger.Trace($"KLineData:{klinedata.Data.kline.kLineClosed}:{klinedata.Data.MarketSymbol}:{klinedata.Data.kline.BaseVolume}:{klinedata.Data.kline.QuoteVolume}");
                logger.Trace($"KLineData:{klinedata.Data.kline.kLineClosed}:{klinedata.Data.MarketSymbol}:{klinedata.Data.kline.TakerBaseVolume}:{klinedata.Data.kline.TakerQuoteVolume}");
                */

                if (klinedata.Data.kline.kLineClosed == true)
                {
                    /*
                    logger.Trace($"EventTime:" + dt);
                    logger.Trace($"KLineData:{klinedata.Data.kline.kLineClosed}:{klinedata.Data.MarketSymbol}:{klinedata.Data.kline.OpenTimestamp}");
                    logger.Trace($"KLineData:{klinedata.Data.kline.kLineClosed}:{klinedata.Data.MarketSymbol}:{klinedata.Data.kline.Open}:{klinedata.Data.kline.High}:{klinedata.Data.kline.Low}:{klinedata.Data.kline.Close}");
                    logger.Trace($"KLineData:{klinedata.Data.kline.kLineClosed}:{klinedata.Data.MarketSymbol}:{klinedata.Data.kline.BaseVolume}:{klinedata.Data.kline.QuoteVolume}");
                    logger.Trace($"KLineData:{klinedata.Data.kline.kLineClosed}:{klinedata.Data.MarketSymbol}:{klinedata.Data.kline.TakerBaseVolume}:{klinedata.Data.kline.TakerQuoteVolume}");
                    */
                    MarketCandle candle = new MarketCandle();
                    candle.OpenPrice = klinedata.Data.kline.Open;
                    candle.HighPrice = klinedata.Data.kline.High;
                    candle.LowPrice = klinedata.Data.kline.Low;
                    candle.ClosePrice = klinedata.Data.kline.Close;
                    candle.PeriodSeconds = (int)interval;

                    candle.BaseCurrencyVolume = (double)klinedata.Data.kline.BaseVolume;
                    candle.QuoteCurrencyVolume = (double)klinedata.Data.kline.QuoteVolume;
                    /*
                    candle.BaseCurrencyVolume = (double)klinedata.Data.kline.TakerBaseVolume;
                    candle.QuoteCurrencyVolume = (double)klinedata.Data.kline.TakerQuoteVolume;
                    */
                    candle.Timestamp = klinedata.Data.kline.OpenTimestamp;
                    if (!TickerKLines.ContainsKey(klinedata.Data.MarketSymbol))
                        TickerKLines[klinedata.Data.MarketSymbol] = new List<MarketCandle>();
                    TickerKLines[klinedata.Data.MarketSymbol].Add(candle);
                }
                /*
                string marketSymbol = update.Data.MarketSymbol;
                ExchangeOrderBook book = new ExchangeOrderBook { SequenceId = update.Data.FinalUpdate, MarketSymbol = marketSymbol, LastUpdatedUtc = CryptoUtility.UnixTimeStampToDateTimeMilliseconds(update.Data.EventTime) };
                foreach (List<object> ask in update.Data.Asks)
                {
                    var depth = new ExchangeOrderPrice { Price = ask[0].ConvertInvariant<decimal>(), Amount = ask[1].ConvertInvariant<decimal>() };
                    book.Asks[depth.Price] = depth;
                }
                foreach (List<object> bid in update.Data.Bids)
                {
                    var depth = new ExchangeOrderPrice { Price = bid[0].ConvertInvariant<decimal>(), Amount = bid[1].ConvertInvariant<decimal>() };
                    book.Bids[depth.Price] = depth;
                }
                callback(book);
                */
                return Task.CompletedTask;
            }, (_sock) =>
            {
                logger.Trace("KLine socket connected");
                return Task.CompletedTask;
            });
        }

        private List<MarketCandle> AggregateCandlesIntoRequestedTimePeriod(KlineInterval rawPeriod, KlineInterval requestedPeriod, List<MarketCandle> candles)
        {
            if (rawPeriod != requestedPeriod)
            {
                int rawPeriodDivisor = (int)requestedPeriod / (int)rawPeriod;
                candles = candles
                            .GroupBy(g => new { TimeBoundary = new DateTime(g.Timestamp.Year, g.Timestamp.Month, g.Timestamp.Day, g.Timestamp.Hour, (g.Timestamp.Minute / rawPeriodDivisor) * rawPeriodDivisor, 0) })
                            //.Where(g => g.Count() == rawPeriodDivisor)
                            .Select(s => new MarketCandle
                            {
                                Timestamp = s.Key.TimeBoundary,
                                HighPrice = s.Max(z => z.HighPrice),
                                LowPrice = s.Min(z => z.LowPrice),
                                OpenPrice = s.First().OpenPrice,
                                ClosePrice = s.Last().ClosePrice,
                                BaseCurrencyVolume = s.Sum(z => z.BaseCurrencyVolume),
                                QuoteCurrencyVolume = s.Sum(z => z.QuoteCurrencyVolume),
                            })
                            /*
                            .Select(s => new Candle
                            {
                                Period = requestedPeriod,
                                Timestamp = s.Key.TimeBoundary,
                                High = s.Max(z => z.High),
                                Low = s.Min(z => z.Low),
                                Open = s.First().Open,
                                Close = s.Last().Close,
                                BuyVolume = s.Sum(z => z.BuyVolume),
                                SellVolume = s.Sum(z => z.SellVolume),
                            })
                            */
                            .OrderBy(o => o.Timestamp)
                            .ToList();
            }

            return candles;
        }

        public async Task<List<MarketCandle>> GetKlines(string symbol, KlineInterval interval)
        {
            List<MarketCandle> candles = (await api.GetCandlesAsync(symbol, (int)interval, null, null, 100)).ToList();
            return candles;
        }
        public List<MarketCandle> GetKlinesWebsocket(string symbol, KlineInterval interval)
        {
            List<MarketCandle> candles = TickerKLines[symbol];
            candles = AggregateCandlesIntoRequestedTimePeriod(KlineInterval.kline_1m, interval, candles);
            return candles;
        }
    }
}
