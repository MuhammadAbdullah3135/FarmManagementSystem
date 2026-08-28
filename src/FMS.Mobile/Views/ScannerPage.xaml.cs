using CommunityToolkit.Mvvm.Input;
using ZXing.Net.Maui;

namespace FMS.Mobile.Views;

public partial class ScannerPage : ContentPage
{
    public event EventHandler<string>? TagScanned;

    public ScannerPage()
    {
        InitializeComponent();
    }

    private void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        var first = e.Results?.FirstOrDefault();
        if (first is null) return;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            BarcodeReader.IsDetecting = false;
            var tag = first.Value;
            await Navigation.PopAsync();
            TagScanned?.Invoke(this, tag);
        });
    }

    [RelayCommand]
    private async Task OnManualEntryAsync()
    {
        var tag = ManualTagEntry.Text?.Trim();
        if (string.IsNullOrEmpty(tag)) return;

        BarcodeReader.IsDetecting = false;
        await Navigation.PopAsync();
        TagScanned?.Invoke(this, tag);
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        BarcodeReader.IsDetecting = false;
        await Navigation.PopAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        BarcodeReader.IsDetecting = false;
    }
}
