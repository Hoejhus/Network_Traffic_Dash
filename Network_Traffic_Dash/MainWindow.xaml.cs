using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

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

        // Live-liste + søgning
        TopListLive.ItemsSource = liveRows;
        _liveView ??= CollectionViewSource.GetDefaultView(TopListLive.ItemsSource);
        _liveView.Filter = LiveFilter;
        _liveView.Refresh();

        // Historik-liste + søgning
        TopListHist.ItemsSource = histRows;
        _histView ??= CollectionViewSource.GetDefaultView(TopListHist.ItemsSource);
        _histView.Filter = HistFilter;
        _histView.Refresh();

        // Map payload (sender kun aktivt sæt)
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
            catch { /* små races kan ignoreres */ }
        }
    }

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => _mode = (Tabs.SelectedIndex == 0) ? "live" : "history";

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

    private void Window_Closed(object sender, EventArgs e)
    {
        _tick.Stop();
        _etw.Dispose();
        _geo.Dispose();
    }
}
