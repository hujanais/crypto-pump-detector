using ExchangeSharp;
using GalaSoft.MvvmLight;
using LiveCharts;
using LiveCharts.Configurations;
using LiveCharts.Defaults;
using LiveCharts.Wpf;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace KlineViewer.ViewModels
{
    class ViewModel : ViewModelBase
    {
        private ChartValues<OhlcPoint> ohlcChartValues = new ChartValues<OhlcPoint>();
        private ChartValues<double> volumeChartValues = new ChartValues<double>();

        private double y2Max = 0;

        public SeriesCollection SeriesCollection { get; private set; }

        public CartesianMapper<double> CustomMapper { get; private set; }

        public ViewModel()
        {
            this.SeriesCollection = new SeriesCollection();

            CustomMapper = new CartesianMapper<double>()
                .X((value, index) => index + 1)
                .Y((value, index) => value)
                .Fill((value, index) => value > 50000 ? Brushes.Green : Brushes.Salmon)
                .Stroke((value, index) => value > 50000 ? Brushes.Green : Brushes.Salmon);

            this.SeriesCollection.Add(new OhlcSeries() { Values = ohlcChartValues, ScalesYAt = 0, Fill = Brushes.Transparent });
            this.SeriesCollection.Add(new ColumnSeries() { Values = volumeChartValues, ScalesYAt = 1, Configuration = CustomMapper });
            this.LoadJson(@"C:\Users\tlee\Downloads\hotusdt_900.json");
        }

        public void LoadJson(string fileFullPath)
        {
            try
            {
                ohlcChartValues.Clear();
                volumeChartValues.Clear();
                string jsonString = File.ReadAllText(fileFullPath);
                var candles = JsonConvert.DeserializeObject<List<MarketCandle>>(jsonString);

                foreach (var candle in candles.Take(200))
                {
                    ohlcChartValues.Add(new OhlcPoint((double)candle.OpenPrice, (double)candle.HighPrice, (double)candle.LowPrice, (double)candle.ClosePrice));
                    volumeChartValues.Add(candle.QuoteCurrencyVolume);
                }

                Y2Max = volumeChartValues.Max() * 5;    // stretch the y-axis by 5x.
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        public double Y2Max
        {
            get => this.y2Max;
            set
            {
                this.y2Max = value;
                this.RaisePropertyChanged(nameof(Y2Max));
            }
        }
    }
}
