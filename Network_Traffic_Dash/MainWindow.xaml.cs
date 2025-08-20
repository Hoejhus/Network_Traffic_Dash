using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace Network_Traffic_Dash;

public partial class MainWindow : Window
{
    private readonly EtwListener _etw = new();
    private readonly GeoService _geo = new();
    private FlowAggregator? _agg;
    private readonly System.Timers.Timer _tick = new System.Timers.Timer(1000); // 1 s
    private bool _mapReady;
    private string _mode = "live"; // "live" | "history"

    private ICollectionView? _liveView;
    private ICollectionView? _histView;

    public MainWindow()
    {
        InitializeComponent();
        Web.NavigationCompleted += (_, __) => _mapReady = true;
        _tick.Elapsed += (_, __) => Dispatcher.Invoke(UpdateUi);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await Web.EnsureCoreWebView2Async();
        var html = Path.Combine(AppContext.BaseDirectory, "map", "index.html");
        Web.Source = new Uri(html);

        _agg = new FlowAggregator(_geo, activeTtlSeconds: 5);
        _etw.OnPacket += p => _agg!.Ingest(p);

        try { _etw.Start(); _tick.Start(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "ETW error", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void UpdateUi()
    {
        if (_agg == null) return;

        var (liveRows, histRows, livePoints, histPoints) = _agg.Snapshot();

        TopListLive.ItemsSource = liveRows;
        _liveView ??= CollectionViewSource.GetDefaultView(TopListLive.ItemsSource);
        _liveView.Filter = LiveFilter;
        _liveView.Refresh();

        TopListHist.ItemsSource = histRows;
        _histView ??= CollectionViewSource.GetDefaultView(TopListHist.ItemsSource);
        _histView.Filter = HistFilter;
        _histView.Refresh();

        if (_mapReady)
        {
            var payload = new
            {
                mode = _mode,
                points = _mode == "live" ? livePoints : histPoints
            };
            try
            {
                var json = JsonSerializer.Serialize(payload);
                Web.CoreWebView2.PostWebMessageAsJson(json);
            }
            catch { }
        }
    }

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => _mode = (Tabs.SelectedIndex == 0) ? "live" : "history";

    // --- søgning ---
    private bool LiveFilter(object o)
    {
        if (o is not FlowAggregator.LiveRow r) return true;
        var q = SearchLive.Text?.Trim();
        if (string.IsNullOrEmpty(q)) return true;
        q = q!.ToLowerInvariant();
        return $"{r.Proc} {r.Dst} {r.Port} {r.Proto}".ToLowerInvariant().Contains(q);
    }
    private bool HistFilter(object o)
    {
        if (o is not FlowAggregator.HistRow r) return true;
        var q = SearchHist.Text?.Trim();
        if (string.IsNullOrEmpty(q)) return true;
        q = q!.ToLowerInvariant();
        return $"{r.Org} {r.Dst} {r.Country} {r.Proto}".ToLowerInvariant().Contains(q);
    }
    private void SearchLive_TextChanged(object s, TextChangedEventArgs e) => _liveView?.Refresh();
    private void SearchHist_TextChanged(object s, TextChangedEventArgs e) => _histView?.Refresh();
    private void TopListLive_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TopListLive.SelectedItem is FlowAggregator.LiveRow row)
            FocusIp(row.Dst);
    }
    private void TopListHist_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TopListHist.SelectedItem is FlowAggregator.HistRow row)
            FocusIp(row.Dst);
    }
    private void FocusIp(string ip)
    {
        if (!_mapReady || string.IsNullOrWhiteSpace(ip)) return;
        var cmd = new { cmd = "focus", ip };
        try
        {
            var json = JsonSerializer.Serialize(cmd);
            Web.CoreWebView2.PostWebMessageAsJson(json);
        }
        catch { }
    }

    private void Window_Closed(object sender, EventArgs e)
    {
        _tick.Stop();
        _etw.Dispose();
        _geo.Dispose();
    }
}
