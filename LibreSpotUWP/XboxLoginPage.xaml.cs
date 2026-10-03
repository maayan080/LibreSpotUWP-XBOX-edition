using System;
using LibreSpotUWP.Constants;
using LibreSpotUWP.Exceptions;
using LibreSpotUWP.Helpers;
using LibreSpotUWP.Models;
using LibreSpotUWP.Services;
using Microsoft.Web.WebView2.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace LibreSpotUWP
{
    public sealed partial class XboxLoginPage : Page
    {
        // Page parameter: skip the account sign-in and run only the playback authorization.
        public const string PlaybackOnlyParameter = "playback";

        private const string CallbackPrefix = SpotifyConfig.LoopbackRedirectUri;

        // Sign-in is two steps in one WebView: (1) library access via the PKCE flow above,
        // (2) the separate "streaming" playback authorization Spotify now requires
        // (SpotifyPlaybackAuthService). Both end on a 127.0.0.1 redirect we intercept.
        private bool _playbackPhase;
        private bool _codeHandled;
        private string _accountId;

        public XboxLoginPage()
        {
            InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            _playbackPhase = (e.Parameter as string) == PlaybackOnlyParameter;

            Uri loginUri;
            try
            {
                loginUri = _playbackPhase
                    ? await BeginPlaybackAuthorizationAsync()
                    : App.SpotifyAuth?.PreparePkceLoginUri();
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "Playback authorization could not start");
                ShowError("Playback authorization could not start: " + ex.Message);
                return;
            }

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
                ConfigureForSpotifyChallenge(core);

                SetStatus("Loading Spotify...");
                LoginView.Source = loginUri;
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "In-app login failed to start");
                ShowError("Sign-in could not start: " + ex.Message);
            }
        }

        // Spotify's "confirm you're human" step can open a pop-up window or serve a different
        // challenge to a browser that says it is an Xbox. WebView2 drops pop-ups on its own, which
        // looks like a Continue button that does nothing, so open them in this same view and
        // present a desktop user agent. Navigation is logged (without query strings, which can
        // hold the auth code) so a failed challenge can be diagnosed from the app log.
        private void ConfigureForSpotifyChallenge(CoreWebView2 core)
        {
            try
            {
                var ua = core.Settings.UserAgent;
                var desktop = System.Text.RegularExpressions.Regex.Replace(
                    ua ?? string.Empty, @";\s*Xbox[^;)]*", string.Empty);
                if (!string.IsNullOrWhiteSpace(desktop) && desktop != ua)
                {
                    core.Settings.UserAgent = desktop;
                    LogService.Info("XboxLoginPage: user agent changed from '" + ua + "' to '" + desktop + "'");
                }
            }
            catch (Exception ex)
            {
                LogService.Warn("XboxLoginPage: user agent not changed - " + ex.Message);
            }

            core.NewWindowRequested += (s, args) =>
            {
                LogService.Info("XboxLoginPage: new window requested -> " + StripQuery(args.Uri));
                args.Handled = true;
                if (!string.IsNullOrEmpty(args.Uri))
                    core.Navigate(args.Uri);
            };

            core.NavigationCompleted += (s, args) =>
                LogService.Info("XboxLoginPage: navigated " + StripQuery(core.Source) +
                                " success=" + args.IsSuccess + " status=" + args.WebErrorStatus);

            core.FrameNavigationStarting += (s, args) =>
                LogService.Info("XboxLoginPage: frame -> " + StripQuery(args.Uri));

            core.ProcessFailed += (s, args) =>
                LogService.Warn("XboxLoginPage: web process failed - " + args.ProcessFailedKind);
        }

        private static string StripQuery(string uri)
        {
            if (string.IsNullOrEmpty(uri)) { return string.Empty; }
            int q = uri.IndexOfAny(new[] { '?', '#' });
            return q < 0 ? uri : uri.Substring(0, q);
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

        private string ActiveCallbackPrefix =>
            _playbackPhase ? SpotifyConfig.PlaybackRedirectUri : CallbackPrefix;

        private async System.Threading.Tasks.Task<Uri> BeginPlaybackAuthorizationAsync()
        {
            var profile = await App.SpotifyWeb.GetCurrentUserProfileAsync(forceRefresh: true);
            _accountId = profile?.Value?.Id;
            if (string.IsNullOrWhiteSpace(_accountId))
                throw new InvalidOperationException("The signed-in Spotify account could not be identified.");

            return await App.SpotifyPlaybackAuth.BeginBrowserAuthorizationAsync();
        }

        private async void StartPlaybackPhase()
        {
            try
            {
                StatusOverlay.Visibility = Visibility.Visible;
                SetStatus("Authorizing playback...");

                var uri = await BeginPlaybackAuthorizationAsync();
                _playbackPhase = true;
                _codeHandled = false;
                LoginView.CoreWebView2.Navigate(uri.AbsoluteUri);
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "Playback authorization could not start");
                ShowError("Signed in, but playback authorization could not start: " + ex.Message);
            }
        }

        private void Core_NavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
        {
            if (args.Uri != null &&
                args.Uri.StartsWith(ActiveCallbackPrefix, StringComparison.OrdinalIgnoreCase))
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
            if (!uri.StartsWith(ActiveCallbackPrefix, StringComparison.OrdinalIgnoreCase)) { return; }

            _codeHandled = true;

            if (_playbackPhase)
            {
                await CompletePlaybackAuthorizationAsync(uri);
                return;
            }

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

                if (App.SpotifyPlaybackAuth?.Current?.Status == PlaybackAuthorizationStatus.Ready)
                {
                    GoBack();
                    return;
                }

                StartPlaybackPhase();
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

        private async System.Threading.Tasks.Task CompletePlaybackAuthorizationAsync(string callbackUri)
        {
            StatusOverlay.Visibility = Visibility.Visible;
            SetStatus("Finishing playback authorization...");

            try
            {
                await App.SpotifyPlaybackAuth.CompleteBrowserAuthorizationAsync(callbackUri, _accountId);
                LogService.Info("In-app playback authorization completed.");
                GoBack();
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "Playback authorization failed");
                ShowError("Playback authorization failed: " + ex.Message);
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
