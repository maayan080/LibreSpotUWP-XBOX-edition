using LibreSpotUWP.Controls;
using LibreSpotUWP.Helpers;
using LibreSpotUWP.Models;
using LibreSpotUWP.Services;
using System;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace LibreSpotUWP
{
    public sealed partial class OobePage : Page
    {
        private static readonly Uri LoginHelperProjectUri =
            new Uri("https://github.com/megabytesme/LibreSpotUWPLoginHelper/releases/latest");

        private const int SignInPageIndex = 2;
        private const int ReadyPageIndex = 3;
        private static bool _returningFromQrScanner;

        private bool _checkingAuthState;
        private bool _listeningForAuthState;

        public OobePage()
        {
            InitializeComponent();

            Loaded += OobePage_Loaded;
            Unloaded += OobePage_Unloaded;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            RestoreQrScannerReturnPage();
            await QrLoginHelper.TryConsumePendingScanAsync(App.SpotifyAuth, SetBusy);
            await RefreshSignedInStateAsync();
        }

        private async void OobePage_Loaded(object sender, RoutedEventArgs e)
        {
            if (App.SpotifyAuth != null && !_listeningForAuthState)
            {
                App.SpotifyAuth.AuthStateChanged += SpotifyAuth_AuthStateChanged;
                _listeningForAuthState = true;
            }

            UpdateDirectSignInUi();
            await RefreshSignedInStateAsync();
        }

        private void OobePage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (App.SpotifyAuth != null && _listeningForAuthState)
            {
                App.SpotifyAuth.AuthStateChanged -= SpotifyAuth_AuthStateChanged;
                _listeningForAuthState = false;
            }
        }

        private async void SpotifyAuth_AuthStateChanged(object sender, AuthState e)
        {
            await RefreshSignedInStateAsync();
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            _ = MoveToNextOobePageAsync();
        }

        private async void BtnOpenHelper_Click(object sender, RoutedEventArgs e)
        {
            await Launcher.LaunchUriAsync(LoginHelperProjectUri);
        }

        private void BtnScanQr_Click(object sender, RoutedEventArgs e)
        {
            _returningFromQrScanner = true;

            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                Frame?.Navigate(typeof(ScannerPage));
            });
        }

        private async void BtnPasteDetails_Click(object sender, RoutedEventArgs e)
        {
            await PasteSignInDetailsAsync();
        }

        private void BtnSaveClientId_Click(object sender, RoutedEventArgs e)
        {
            UserSettings.SpotifyCustomClientId = ClientIdTextBox.Text;
            UpdateDirectSignInUi();
        }

        private async void BtnSpotifySignIn_Click(object sender, RoutedEventArgs e)
        {
            if (OSHelper.IsXboxFamily)
            {
                Frame?.Navigate(typeof(XboxLoginPage));
                return;
            }

            TxtAuthStatus.Text = "Waiting for Spotify to return to LibreSpotUWP...";
            await App.SpotifyAuth.BeginPkceLoginAsync();
        }

        private void BtnLaunch_Click(object sender, RoutedEventArgs e)
        {
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                Frame?.BackStack.Clear();
                Frame?.Navigate(NavigationHelper.GetPageType("Shell"));
            });
        }

        private async Task PasteSignInDetailsAsync()
        {
            var textBox = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 220,
                PlaceholderText = "Paste sign-in details from LibreSpotUWP Login Helper"
            };

            var container = new StackPanel();
            container.Children.Add(new TextBlock
            {
                Text = "Paste the full sign-in details text. It contains the same session data as the QR code.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });
            container.Children.Add(textBox);

            var dialog = new ContentDialog
            {
                Title = "Paste Sign-in Details",
                Content = container,
                PrimaryButtonText = "Import",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
                return;

            await QrLoginHelper.ImportQrLoginAsync(textBox.Text, App.SpotifyAuth, SetBusy);
            await RefreshSignedInStateAsync();
        }

        private async Task RefreshSignedInStateAsync()
        {
            if (_checkingAuthState || App.SpotifyAuth == null)
                return;

            _checkingAuthState = true;

            try
            {
                var token = await App.SpotifyAuth.GetAccessToken();
                var isSignedIn = !string.IsNullOrEmpty(token);

                BtnReadyNext.IsEnabled = isSignedIn;
                BtnLaunch.IsEnabled = isSignedIn;

                if (!isSignedIn)
                {
                    TxtAuthStatus.Text = "No Spotify account is connected yet.";
                    TxtReadyAccount.Text = "Finish sign-in first, then LibreSpotUWP will open.";
                    return;
                }

                TxtAuthStatus.Text = "Spotify account connected. You can continue.";
                TxtReadyAccount.Text = "Your Spotify account is connected.";

                await TryLoadCurrentUserAsync();

                if (OobeFlipView.SelectedIndex >= SignInPageIndex && OobeFlipView.SelectedIndex < ReadyPageIndex)
                    await MoveToOobePageAsync(ReadyPageIndex);
            }
            finally
            {
                _checkingAuthState = false;
            }
        }

        private void RestoreQrScannerReturnPage()
        {
            if (!_returningFromQrScanner)
                return;

            _returningFromQrScanner = false;

            if (OobeFlipView.SelectedIndex < SignInPageIndex)
                OobeFlipView.SelectedIndex = SignInPageIndex;
        }

        private async Task MoveToNextOobePageAsync()
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                var nextIndex = OobeFlipView.SelectedIndex + 1;
                if (nextIndex < OobeFlipView.Items.Count)
                    OobeFlipView.SelectedIndex = nextIndex;
            });
        }

        private async Task MoveToOobePageAsync(int pageIndex)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (pageIndex >= 0 && pageIndex < OobeFlipView.Items.Count)
                    OobeFlipView.SelectedIndex = pageIndex;
            });
        }

        private async Task TryLoadCurrentUserAsync()
        {
            if (App.SpotifyWeb == null)
                return;

            try
            {
                var profile = await App.SpotifyWeb.GetCurrentUserProfileAsync(forceRefresh: false);
                var user = profile?.Value;
                SpotifyAccountManager.Instance.SetUser(user);

                var name = !string.IsNullOrWhiteSpace(user?.DisplayName)
                    ? user.DisplayName
                    : user?.Id;

                if (!string.IsNullOrWhiteSpace(name))
                    TxtReadyAccount.Text = "Signed in as " + name + ".";
            }
            catch (Exception ex)
            {
                LogService.Warn($"Unable to load current user during OOBE: {ex.Message}");
            }
        }

        private void UpdateDirectSignInUi()
        {
            // SPOTBOX FORK: on Xbox show one button and hide every other sign-in option.
            if (OSHelper.IsXboxFamily)
            {
                ApplyXboxSignInUi();
                return;
            }

            DirectSignInPanel.Visibility = OSHelper.SupportsBrowserSpotifyLogin
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (!OSHelper.SupportsBrowserSpotifyLogin)
                return;

            ClientIdTextBox.Text = UserSettings.SpotifyCustomClientId;

            var hasClientId = UserSettings.HasSpotifyCustomClientId;

            BtnSpotifySignIn.Visibility = Visibility.Visible;

            DirectSignInStatusText.Text = hasClientId
                ? "Direct browser sign-in is enabled for this device."
                : "Sign in with your Spotify account in the browser. A custom client ID is optional - leave it blank to use the built-in one.";
        }

        private void ApplyXboxSignInUi()
        {
            HowItWorksIntro.Text =
                "Sign in with your Spotify account on the next screen. You log in right here on " +
                "the console, using the normal Spotify sign-in page.";
            HowItWorksDirect.Visibility = Visibility.Collapsed;
            HowItWorksQrWarning.Visibility = Visibility.Collapsed;

            SignInDescription.Text =
                "Press the button to sign in. A Spotify Premium account is required.";

            BtnOpenHelper.Visibility = Visibility.Collapsed;
            BtnScanQr.Visibility = Visibility.Collapsed;
            BtnPasteDetails.Visibility = Visibility.Collapsed;
            DirectSignInPanel.Visibility = Visibility.Collapsed;

            XboxSignInPanel.Visibility = Visibility.Visible;
        }

        private void BtnXboxSignIn_Click(object sender, RoutedEventArgs e)
        {
            Frame?.Navigate(typeof(XboxLoginPage));
        }

        private void SetBusy(bool isBusy)
        {
            LoginProgressRing.IsActive = isBusy;
            LoginProgressRing.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;

            BtnOpenHelper.IsEnabled = !isBusy;
            BtnScanQr.IsEnabled = !isBusy;
            BtnPasteDetails.IsEnabled = !isBusy;
            BtnSaveClientId.IsEnabled = !isBusy;
            BtnSpotifySignIn.IsEnabled = !isBusy;
            BtnXboxSignIn.IsEnabled = !isBusy;
        }
    }
}
