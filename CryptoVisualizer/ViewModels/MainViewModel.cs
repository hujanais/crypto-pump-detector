using ExchangeSharp;
using GalaSoft.MvvmLight;
using LiveCharts;
using LiveCharts.Defaults;
using LiveCharts.Wpf;
using Skender.Stock.Indicators;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace CryptoVisualizer.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private ExchangeAPI api;
        private int selectedCandleSize = 8;
        private string selectedTicker = "";
        private ChartValues<OhlcPoint> ohlcChartValues = new ChartValues<OhlcPoint>();

        public MainViewModel()
        {
            api = new ExchangeBinanceUSAPI();
            this.Tickers = new ObservableCollection<string>();
            enumerateMarkets();

            // Pre-allocate memory
            this.SeriesCollection = new SeriesCollection();
            this.SeriesCollection.Add(new OhlcSeries() { Values = ohlcChartValues, ScalesYAt = 0, Fill = Brushes.Transparent });

            this.CandleSizes = new int[] { 1, 2, 4, 8, 12 };
        }

        public IList<int> CandleSizes { get; private set; }
        public SeriesCollection SeriesCollection { get; private set; }

        public string SelectedTicker
        {
            get => this.selectedTicker;
            set
            {
                this.selectedTicker = value;
                this.RaisePropertyChanged(nameof(this.SelectedTicker));
                this.updateData();
            }
        }

        public int SelectedCandleSize
        {
            get => this.selectedCandleSize;
            set
            {
                this.selectedCandleSize = value;
                this.RaisePropertyChanged(nameof(this.SelectedCandleSize));
                this.updateData();
            }
        }

        public IList<string> Tickers { get; private set; }

        private async void enumerateMarkets()
        {
            // enumerate all the markets.
            string QUOTECURRENCY = "USD";
            string[] coinsToRemove = { "USDC", "BUSD", "USDT", "DAI", "PAXG" };
            var tickers = await api.GetTickersAsync();
            tickers = tickers.OrderBy(t => t.Key).Where(t => t.Value.Volume.QuoteCurrency == QUOTECURRENCY &&
                !coinsToRemove.Contains(t.Value.Volume.BaseCurrency)
            );

            foreach (var ticker in tickers.Select(t => t.Key))
            {
                this.Tickers.Add(ticker);
            }
        }

        private async void updateData()
        {
            try
            {
                int periodSeconds = this.selectedCandleSize * 60 * 60;
                var candles = await api.GetCandlesAsync(this.selectedTicker, periodSeconds);
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

                var heikinAshi = Indicator.GetHeikinAshi(history);
                IList<OhlcPoint> ohlcPoints = new List<OhlcPoint>();
                foreach (var ha in heikinAshi)
                {
                    ohlcPoints.Add(new OhlcPoint { Open = (double)ha.Open, High = (double)ha.High, Low = (double)ha.Low, Close = (double)ha.Close});
                }


                ohlcChartValues.Clear();
                ohlcChartValues.AddRange(ohlcPoints.Reverse().Take(100).Reverse());
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }
    }
}
