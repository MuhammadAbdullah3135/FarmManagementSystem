using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Activity;

namespace FMS.Mobile;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        OnBackPressedDispatcher.AddCallback(new FmsBackPressedCallback(this));
    }

    internal sealed class FmsBackPressedCallback : OnBackPressedCallback
    {
        private readonly WeakReference<MainActivity> _activityRef;

        public FmsBackPressedCallback(MainActivity activity)
            : base(true)
        {
            _activityRef = new WeakReference<MainActivity>(activity);
        }

        public override void HandleOnBackPressed()
        {
            var webView = FmsWebView.Current;
            if (webView?.CanGoBack == true)
            {
                webView.GoBack();
                return;
            }

            if (_activityRef.TryGetTarget(out var activity))
            {
                Enabled = false;
                activity.OnBackPressedDispatcher.OnBackPressed();
            }
        }
    }
}