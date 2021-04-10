using ExchangeSharp;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using LiveCharts;
using LiveCharts.Configurations;
using LiveCharts.Defaults;
using LiveCharts.Wpf;
using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace KlineViewer.ViewModels
{
    class ViewModel : ViewModelBase
    {
        private ChartValues<OhlcPoint> ohlcChartValues = new ChartValues<OhlcPoint>();
        private ChartValues<VolumePair> volumeChartValues = new ChartValues<VolumePair>();

        private double y2Max = 100; // initialize to non-zero or else chart will crash.

        public SeriesCollection SeriesCollection { get; private set; }
        public CartesianMapper<VolumePair> CustomMapper { get; private set; }
        public ICommand LoadJsonCommand { get; private set; }

        public ViewModel()
        {
            this.LoadJsonCommand = new RelayCommand(this.doLoadJson);

            this.SeriesCollection = new SeriesCollection();

            CustomMapper = new CartesianMapper<VolumePair>()
                .X((value, index) => index + 1)
                .Y((value, index) => value.Volume)
                .Fill((value, index) => value.IsGreen ? Brushes.LawnGreen : Brushes.Red)
                .Stroke((value, index) => value.IsGreen ? Brushes.LawnGreen : Brushes.Red);

            this.SeriesCollection.Add(new OhlcSeries() { Values = ohlcChartValues, ScalesYAt = 0, Fill = Brushes.Transparent });
            this.SeriesCollection.Add(new ColumnSeries() { Values = volumeChartValues, ScalesYAt = 1, Configuration = CustomMapper });
        }

        private void LoadJson(string fileFullPath)
        {
            try
            {
                ohlcChartValues.Clear();
                volumeChartValues.Clear();
                string jsonString = File.ReadAllText(fileFullPath);
                var candles = JsonConvert.DeserializeObject<List<MarketCandle>>(jsonString);

                foreach (var candle in candles.Take(250))
                {
                    var ohlc = new OhlcPoint((double)candle.OpenPrice, (double)candle.HighPrice, (double)candle.LowPrice, (double)candle.ClosePrice);
                    ohlcChartValues.Add(ohlc);
                    volumeChartValues.Add(new VolumePair(candle.QuoteCurrencyVolume, (ohlc.Close >= ohlc.Open)));
                }

                Y2Max = volumeChartValues.Select(v => v.Volume).Max() * 5;    // stretch the y-axis by 5x.
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

        private void doLoadJson()
        {
            try
            {
                OpenFileDialog ofn = new OpenFileDialog();
                ofn.Filter = "json files (*.json)|*.json|All files (*.*)|*.*";
                ofn.RestoreDirectory = true;
                if (ofn.ShowDialog().GetValueOrDefault())
                {
                    this.LoadJson(ofn.FileName);
                }
            } catch(Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }
    }

    internal class VolumePair
    {
        public VolumePair(double vol, bool isGreen)
        {
            this.Volume = vol;
            this.IsGreen = isGreen;
        }

        public double Volume { get; set; }
        public bool IsGreen { get; set; }
    }
}
