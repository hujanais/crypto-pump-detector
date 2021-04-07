using ExchangeSharp;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace PumpDetector.Models
{
    public enum KlineInterval
    {
        kline_1m = 60,
        kline_3m = 3 * 60,
        kilne_5m = 5 * 60,
        kline_15m = 15 * 60,
        kline_30m = 30 * 60,
        kline_1h = 60 * 60,
        kline_2h = 2 * 60 * 60,
        kline_4h = 4 * 60 * 60,
        kline_6h = 6 * 60 * 60,
        kline_8h = 8 * 60 * 60,
        kline_12h = 12 * 60 * 60,
        kline_1d = 24 * 60 * 60,
        kline_3d = 3 * 24 * 60 * 60,
        kline_1w = 7 * 24 * 60 * 60
    }

    class Kline
    {
        [JsonProperty("t")]
        public long opents
        {
            set
            {
                _openTS = CryptoUtility.ParseTimestamp(value, TimestampType.UnixMilliseconds);
            }
        }

        [JsonProperty("T")]
        public long closets
        {
            set
            {
                _closeTS = CryptoUtility.ParseTimestamp(value, TimestampType.UnixMilliseconds);
            }
        }

        [JsonProperty("s")]
        public string MarketSymbol { get; set; }

        [JsonProperty("i")]
        public string Interval { get; set; }

        [JsonProperty("f")]
        public long fTID { get; set; }

        [JsonProperty("L")]
        public long lTID { get; set; }

        [JsonProperty("o")]
        public decimal Open { get; set; }

        [JsonProperty("c")]
        public decimal Close { get; set; }

        [JsonProperty("h")]
        public decimal High { get; set; }

        [JsonProperty("l")]
        public decimal Low { get; set; }

        [JsonProperty("v")]
        public decimal BaseVolume { get; set; }

        [JsonProperty("n")]
        public long Trades { get; set; }

        [JsonProperty("x")]
        public bool kLineClosed { get; set; }

        [JsonProperty("q")]
        public decimal QuoteVolume { get; set; }

        [JsonProperty("V")]
        public decimal TakerBaseVolume { get; set; }

        [JsonProperty("Q")]
        public decimal TakerQuoteVolume { get; set; }

        [JsonProperty("B")]
        public long Ignored { get; set; }

        DateTime _openTS;
        public DateTime OpenTimestamp
        {
            get
            {
                return _openTS;
                //return CryptoUtility.ParseTimestamp(opents, TimestampType.UnixMilliseconds);
            }
        }

        DateTime _closeTS;
        public DateTime CloseTimestamp
        {
            get
            {
                return _closeTS;
                //return CryptoUtility.ParseTimestamp(opents, TimestampType.UnixMilliseconds);
            }
        }

    }
    class KlineData
    {
        [JsonProperty("e")]
        public string EventType { get; set; }

        [JsonProperty("E")]
        public long EventTime { get; set; }

        [JsonProperty("s")]
        public string MarketSymbol { get; set; }

        [JsonProperty("k")]
        public Kline kline { get; set; }

    }
    class KlineStream
    {
        [JsonProperty("stream")]
        public string Stream { get; set; }

        [JsonProperty("data")]
        public KlineData Data { get; set; }
    }
}
