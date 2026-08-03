using System;
using LibreSpotUWP.Exceptions;
using LibreSpotUWP.Helpers;
using LibreSpotUWP.Services;
using Microsoft.Web.WebView2.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace LibreSpotUWP
{
    public sealed partial class XboxLoginPage : Page
    {
        private const string CallbackPrefix = "http://127.0.0.1:8898/login";

        private bool _codeHandled;

        public XboxLoginPage()
        {
            InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            var loginUri = App.SpotifyAuth?.PreparePkceLoginUri();
            if (loginUri == null)
            {
                ShowError("Could not build the Spotify sign-in URL. No client ID is available.");
                return;
            }

            try
            {
                SetStatus("Starting browser...");
                await LoginView.EnsureCoreWebView2Async();

                var core = LoginView.CoreWebView2;
                if (core == null)
                {
                    ShowError("The WebView2 runtime is not available on this console, so in-app sign-in cannot run.");
                    return;
                }

                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.IsStatusBarEnabled = false;

                core.NavigationStarting += Core_NavigationStarting;
                TrySubscribeExternalScheme(core);

                SetStatus("Loading Spotify...");
                LoginView.Source = loginUri;
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "In-app login failed to start");
                ShowError("Sign-in could not start: " + ex.Message);
            }
        }

        private void TrySubscribeExternalScheme(CoreWebView2 core)
        {
            try
            {
                core.LaunchingExternalUriScheme += (s, args) =>
                {
                    args.Cancel = true;
                    HandleCandidateUri(args.Uri);
                };
            }
            catch (Exception ex)
            {
                LogService.Warn("LaunchingExternalUriScheme unavailable: " + ex.Message);
            }
        }

        private void Core_NavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
        {
            if (args.Uri != null &&
                args.Uri.StartsWith(CallbackPrefix, StringComparison.OrdinalIgnoreCase))
            {
                args.Cancel = true;
                HandleCandidateUri(args.Uri);
                return;
            }

            if (StatusOverlay.Visibility == Visibility.Visible && !_codeHandled)
            {
                StatusOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private async void HandleCandidateUri(string uri)
        {
            if (_codeHandled || string.IsNullOrEmpty(uri)) { return; }
            if (!uri.StartsWith(CallbackPrefix, StringComparison.OrdinalIgnoreCase)) { return; }

            _codeHandled = true;

            string code = GetQueryValue(uri, "code");
            string error = GetQueryValue(uri, "error");

            if (!string.IsNullOrEmpty(error))
            {
                ShowError("Spotify declined the sign-in: " + error);
                return;
            }

            if (string.IsNullOrEmpty(code))
            {
                ShowError("The redirect did not contain an authorisation code.");
                return;
            }

            StatusOverlay.Visibility = Visibility.Visible;
            SetStatus("Signing in...");

            try
            {
                await App.SpotifyAuth.ExchangePkceCodeAsync(code);
                LogService.Info("In-app sign-in completed.");
                GoBack();
            }
            catch (SpotifyPremiumRequiredException ex)
            {
                await PremiumRequiredDialog.ShowAsync(ex);
                GoBack();
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "Token exchange failed");
                ShowError("Signed in, but the token exchange failed: " + ex.Message);
            }
        }

        private static string GetQueryValue(string uri, string key)
        {
            int q = uri.IndexOf('?');
            if (q < 0 || q == uri.Length - 1) { return null; }

            foreach (var pair in uri.Substring(q + 1).Split('&'))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0) { continue; }

                if (string.Equals(pair.Substring(0, eq), key, StringComparison.OrdinalIgnoreCase))
                {
                    return Uri.UnescapeDataString(pair.Substring(eq + 1));
                }
            }
            return null;
        }

        private void SetStatus(string message)
        {
            StatusText.Text = message;
            LogService.Info("XboxLoginPage: " + message);
        }

        private void ShowError(string message)
        {
            StatusOverlay.Visibility = Visibility.Visible;
            Spinner.IsActive = false;
            StatusText.Text = message;
            BtnBack.Visibility = Visibility.Visible;
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e) => GoBack();

        private void GoBack()
        {
            if (Frame != null && Frame.CanGoBack)
            {
                Frame.GoBack();
            }
        }
    }
}
