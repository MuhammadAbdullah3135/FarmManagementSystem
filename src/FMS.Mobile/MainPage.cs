using FMS.Mobile.Services;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Networking;

#if FIREBASE
using Plugin.Firebase.Crashlytics;
#endif

namespace FMS.Mobile;

public class MainPage : ContentPage
{
    private const string SiteUrl = "https://muhammadabdullah3135.github.io/FarmManagementSystem/";

    private readonly FmsWebView _webView;
    private readonly VerticalStackLayout _offlineOverlay;
    private readonly CrashReporter? _crashReporter;

#if DEBUG
    private readonly Button _testCrashButton;
    private readonly Button _shareReportButton;
#endif

    public MainPage(CrashReporter? crashReporter)
    {
        _crashReporter = crashReporter;
        Title = "FMS Mobile";

        _webView = new FmsWebView
        {
            Source = new UrlWebViewSource { Url = SiteUrl },
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        _webView.ReceivedError += OnWebViewReceivedError;

        var retryButton = new Button
        {
            Text = "Retry",
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.End,
            Margin = new Thickness(0, 0, 0, 40),
            WidthRequest = 160,
            BackgroundColor = Colors.DodgerBlue,
            TextColor = Colors.White
        };
        retryButton.Clicked += OnRetryClicked;

        _offlineOverlay = new VerticalStackLayout
        {
            IsVisible = false,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            BackgroundColor = Colors.White,
            Padding = new Thickness(24),
            Children =
            {
                new VerticalStackLayout
                {
                    VerticalOptions = LayoutOptions.Center,
                    HorizontalOptions = LayoutOptions.Center,
                    Spacing = 12,
                    Children =
                    {
                        new Label
                        {
                            Text = "No internet connection",
                            FontSize = 18,
                            HorizontalOptions = LayoutOptions.Center,
                            TextColor = Colors.DarkSlateGray
                        },
                        new Label
                        {
                            Text = "Please check your network and try again.",
                            FontSize = 14,
                            HorizontalOptions = LayoutOptions.Center,
                            TextColor = Colors.Gray
                        },
                        retryButton
                    }
                }
            }
        };

#if DEBUG
        _testCrashButton = new Button
        {
            Text = "Test Crash",
            FontSize = 11,
            BackgroundColor = Colors.Red.WithAlpha(0.5f),
            TextColor = Colors.White,
            HeightRequest = 36,
            Padding = new Thickness(8, 0),
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.End,
            Margin = new Thickness(0, 0, 12, 12),
            CornerRadius = 18
        };
        _testCrashButton.Clicked += OnTestCrashClicked;

        _shareReportButton = new Button
        {
            Text = "Share Report",
            FontSize = 11,
            BackgroundColor = Colors.SteelBlue.WithAlpha(0.5f),
            TextColor = Colors.White,
            HeightRequest = 36,
            Padding = new Thickness(8, 0),
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.End,
            Margin = new Thickness(0, 0, 12, 56),
            CornerRadius = 18
        };
        _shareReportButton.Clicked += OnShareReportClicked;
#endif

        var overlayLayer = new Grid();
        overlayLayer.Children.Add(_offlineOverlay);
#if DEBUG
        overlayLayer.Children.Add(_testCrashButton);
        overlayLayer.Children.Add(_shareReportButton);
#endif

        Content = new Grid
        {
            Children = { _webView, overlayLayer }
        };

        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (e.NetworkAccess == NetworkAccess.Internet)
            {
                _offlineOverlay.IsVisible = false;
                _webView.IsVisible = true;
                _webView.Source = new UrlWebViewSource { Url = SiteUrl };
            }
            else
            {
                _offlineOverlay.IsVisible = true;
                _webView.IsVisible = false;
            }
        });
    }

    private void OnWebViewReceivedError(object? sender, WebErrorEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _offlineOverlay.IsVisible = true;
            _webView.IsVisible = false;
        });
    }

    private void OnRetryClicked(object? sender, EventArgs e)
    {
        _offlineOverlay.IsVisible = false;
        _webView.IsVisible = true;
        _webView.Source = new UrlWebViewSource { Url = SiteUrl };
    }

#if DEBUG
    private void OnTestCrashClicked(object? sender, EventArgs e)
    {
#if FIREBASE
        try
        {
            CrossFirebaseCrashlytics.Current.RecordException(
                new InvalidOperationException("Test crash from FMS Mobile debug build"));
            _crashReporter?.LogLocal("Test crash triggered (Firebase)");
        }
        catch
        {
            _crashReporter?.LogLocal("Test crash triggered (local fallback)");
        }
#else
        _crashReporter?.LogLocal("Test crash triggered (local only)");
#endif
    }

    private async void OnShareReportClicked(object? sender, EventArgs e)
    {
        var path = _crashReporter?.LatestCrashPath;
        if (path != null && File.Exists(path))
        {
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Share Crash Report",
                File = new ShareFile(path)
            });
        }
    }
#endif

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged;
        _webView.ReceivedError -= OnWebViewReceivedError;
    }
}
