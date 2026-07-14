using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.IO.Ports;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Threading;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.Kernel.Sketches;
using SkiaSharp;

namespace BuildingMonitorApp
{
    public sealed partial class MainWindow : Window
    {
        private SerialPort? _serialPort;
        private string _buffer = "";

        private bool _isAutoAlarmTriggered = false;
        private bool _isManualOverride     = false;
        private string _lastMotionTime     = "--:--:--";
        private int _lastSeq = -1;
        private int _lostPackets = 0;

        public ObservableCollection<ObservableValue> TempValues  { get; set; } = new();
        public ObservableCollection<ObservableValue> HumidValues { get; set; } = new();
        
        public IEnumerable<ISeries>?          ChartSeries { get; set; }
        public IEnumerable<ICartesianAxis>?   XAxes       { get; set; }
        public IEnumerable<ICartesianAxis>?   YAxes       { get; set; }

        public MainWindow()
        {
            this.InitializeComponent();
            this.Title = "Building Monitor Dashboard";
            InitializeChart();
            LoadAvailablePorts();
        }

        private void InitializeChart()
        {
            ChartSeries = new ISeries[]
            {
                new LineSeries<ObservableValue>
                {
                    Values        = TempValues,
                    Name          = "Temp (°C)",
                    Stroke        = new SolidColorPaint(SKColors.OrangeRed) { StrokeThickness = 3 },
                    GeometrySize  = 0, 
                    Fill          = null
                },
                new LineSeries<ObservableValue>
                {
                    Values        = HumidValues,
                    Name          = "Humidity (%)",
                    Stroke        = new SolidColorPaint(SKColors.CornflowerBlue) { StrokeThickness = 3 },
                    GeometrySize  = 0,
                    Fill          = null
                }
            };
            YAxes = new ICartesianAxis[] { new Axis { Name = "Sensor Values", NameTextSize = 14, NamePaint = new SolidColorPaint(SKColors.LightGray), LabelsPaint = new SolidColorPaint(SKColors.Gray) } };
            XAxes = new ICartesianAxis[] { new Axis { Name = "Time (Last 60s)", NameTextSize = 14, NamePaint = new SolidColorPaint(SKColors.LightGray), LabelsPaint = new SolidColorPaint(SKColors.Transparent) } };
        }

        private void LoadAvailablePorts()
        {
            string[] ports = SerialPort.GetPortNames();
            PortSelector.ItemsSource = ports;
            if (ports.Length > 0) PortSelector.SelectedIndex = 0;
        }

        private void ConnectBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                _serialPort.Close();
                ConnectBtn.Content = "Connect";
                ConnectBtn.Background = new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue);
                UpdateStatus("🔌 Disconnected.", Microsoft.UI.Colors.LightGray);
                return;
            }
            if (PortSelector.SelectedItem == null) { UpdateStatus("❌ Please select a COM port first.", Microsoft.UI.Colors.Red); return; }
            ConnectToSTM32(PortSelector.SelectedItem.ToString()!);
        }

        private void ConnectToSTM32(string portName)
        {
            try
            {
                _serialPort = new SerialPort(portName, 115200);
                _serialPort.NewLine = "\r\n";
                _serialPort.DataReceived += OnDataReceived;
                _serialPort.Open();
                ConnectBtn.Content = "Disconnect";
                ConnectBtn.Background = new SolidColorBrush(Microsoft.UI.Colors.OrangeRed);
                UpdateStatus($"✅ Connected to {portName}!", Microsoft.UI.Colors.LightGreen);
            }
            catch (Exception ex) { UpdateStatus($"❌ Failed: {ex.Message}", Microsoft.UI.Colors.Red); }
        }

        private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                if (_serialPort == null || !_serialPort.IsOpen) return;
                _buffer += _serialPort.ReadExisting();
                int idx;
                while ((idx = _buffer.IndexOf('\n')) >= 0)
                {
                    string line = _buffer.Substring(0, idx).Trim();
                    _buffer = _buffer.Substring(idx + 1);
                    if (line.StartsWith("{")) ParseAndDisplay(line);
                }
            }
            catch (Exception ex) { UpdateStatus($"❌ Buffer Error: {ex.Message}", Microsoft.UI.Colors.Red); }
        }

        private void ParseAndDisplay(string json)
        {
            try
            {
                if (json.Contains("\"ack\":1")) { UpdateStatus("✅ Command Confirmed", Microsoft.UI.Colors.LightGreen); return; }

                int sStart = json.IndexOf("\"seq\":") + 6; int sEnd = json.IndexOf(',', sStart);
                int tStart = json.IndexOf("\"t\":") + 4; int tEnd = json.IndexOf(',', tStart);
                int hStart = json.IndexOf("\"h\":") + 4; int hEnd = json.IndexOf(',', hStart);
                int mStart = json.IndexOf("\"m\":") + 4; int mEnd = json.IndexOf(',', mStart);
                int pStart = json.IndexOf("\"p\":") + 4; int pEnd = json.IndexOf('}', pStart);

                int seq = int.Parse(json.Substring(sStart, sEnd - sStart).Trim());
                int temp = int.Parse(json.Substring(tStart, tEnd - tStart).Trim());
                int humidity = int.Parse(json.Substring(hStart, hEnd - hStart).Trim());
                int motion = int.Parse(json.Substring(mStart, mEnd - mStart).Trim());
                int potAngle = int.Parse(json.Substring(pStart, pEnd - pStart).Trim());

                if (_lastSeq != -1 && seq != _lastSeq + 1 && !(seq == 0 && _lastSeq == 65535))
                    Interlocked.Add(ref _lostPackets, seq - _lastSeq - 1);
                _lastSeq = seq;

                if (!_isManualOverride)
                {
                    if (temp > 35 && !_isAutoAlarmTriggered) { _isAutoAlarmTriggered = true; SendCommand("{\"a\":1}\n"); UpdateStatus("⚠️ AUTO-ALARM ACTIVE", Microsoft.UI.Colors.OrangeRed); SetAlarmBanner(true); }
                    else if (temp < 33 && _isAutoAlarmTriggered) { _isAutoAlarmTriggered = false; SendCommand("{\"a\":0}\n"); UpdateStatus("✅ Auto-Alarm Reset", Microsoft.UI.Colors.LightGreen); SetAlarmBanner(false); }
                }

                DispatcherQueue.TryEnqueue(() => {
                    TempText.Text = $"{temp} °C"; HumidText.Text = $"{humidity} %"; AngleText.Text = $"{potAngle}°";
                    RawText.Text = $"Last: {json} | Lost: {_lostPackets}";
                    MotionText.Text = (motion == 1) ? "⚠️ DETECTED" : "✅ Clear";
                    MotionText.Foreground = new SolidColorBrush((motion == 1) ? Microsoft.UI.Colors.OrangeRed : Microsoft.UI.Colors.LightGreen);
                    TempValues.Add(new ObservableValue(temp)); HumidValues.Add(new ObservableValue(humidity));
                    if (TempValues.Count > 60) { TempValues.RemoveAt(0); HumidValues.RemoveAt(0); }
                });
            }
            catch (Exception ex) { UpdateStatus($"❌ Parse Error: {ex.Message}", Microsoft.UI.Colors.Red); }
        }

        private void AlarmOnBtn_Click(object sender, RoutedEventArgs e) { _isManualOverride = true; SendCommand("{\"a\":1}\n"); SetAlarmBanner(true); }
        private void AlarmOffBtn_Click(object sender, RoutedEventArgs e) { _isManualOverride = true; _isAutoAlarmTriggered = false; SendCommand("{\"a\":0}\n"); SetAlarmBanner(false); }
        private void ResumeAutoBtn_Click(object sender, RoutedEventArgs e) { _isManualOverride = false; UpdateStatus("🔄 Auto-Monitoring Resumed", Microsoft.UI.Colors.LightSkyBlue); }
        private void SendCommand(string command) { try { if (_serialPort != null && _serialPort.IsOpen) _serialPort.Write(command); } catch { } }
        private void UpdateStatus(string m, Windows.UI.Color c) { DispatcherQueue.TryEnqueue(() => { StatusText.Text = m; StatusText.Foreground = new SolidColorBrush(c); }); }
        private void SetAlarmBanner(bool a) { DispatcherQueue.TryEnqueue(() => AlarmBanner.Visibility = a ? Visibility.Visible : Visibility.Collapsed); }
    }
}
