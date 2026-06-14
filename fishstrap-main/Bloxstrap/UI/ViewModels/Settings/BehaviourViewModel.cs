using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using Bloxstrap.AppData;
using Bloxstrap.Enums;
using Bloxstrap.Integrations;
using Bloxstrap.Models;
using Bloxstrap.Models.APIs.Roblox;
using Bloxstrap.RobloxInterfaces;
using Bloxstrap.UI.Elements.Dialogs;
using Wpf.Ui.Common.Interfaces;

namespace Bloxstrap.UI.ViewModels.Settings
{
    public class BehaviourViewModel : NotifyPropertyChangedViewModel, INavigationAware
    {

        public BehaviourViewModel()
        {
            App.Cookies.StateChanged += (object? _, CookieState state) => CookieLoadingFailed = state != CookieState.Success && state != CookieState.Unknown;
            AddAccountCommand = new RelayCommand(async () => await AddAccountAsync());
            RefreshInstancesCommand = new RelayCommand(async () => await RefreshInstancesAsync());
        }

        public void OnNavigatedTo() 
        {
            OnPropertyChanged(nameof(VulkanFullscreenAllowed));
            OnPropertyChanged(nameof(EnableFakeBorderlessFullscreen));
            if (CookieAccess)
                _ = RefreshInstancesAsync();
        }

        public void OnNavigatedFrom() { } // has to be here because of INavigationAware, we will just leave it empty

        public bool IsRobloxInstallationMissing => String.IsNullOrEmpty(App.RobloxState.Prop.Player.VersionGuid) && String.IsNullOrEmpty(App.RobloxState.Prop.Studio.VersionGuid);
        public bool VulkanFullscreenAllowed => WindowManipulation.WindowManipulationAvailable && (App.FastFlags.GetPreset("Rendering.Mode.Vulkan") ?? "False").Equals("True", StringComparison.OrdinalIgnoreCase);

        public bool CookieAccess
        {
            get => App.Settings.Prop.AllowCookieAccess;
            set
            {
                App.Settings.Prop.AllowCookieAccess = value;
                if (value)
                {
                    Task.Run(App.Cookies.LoadCookies);
                    App.Accounts.Load();
                    _ = RefreshInstancesAsync();
                }
                OnPropertyChanged(nameof(CookieAccess));
            }
        }
        public bool MultiInstanceLaunching
        {
            get => App.Settings.Prop.MultiInstanceLaunching;
            set => App.Settings.Prop.MultiInstanceLaunching = value;
        }

        #region Account manager
 
        public ObservableCollection<RobloxInstanceViewModel> RobloxInstances { get; } = new();

        private bool _hasRunningInstances;
        public bool HasRunningInstances
        {
            get => _hasRunningInstances;
            set
            {
                _hasRunningInstances = value;
                OnPropertyChanged(nameof(HasRunningInstances));
            }
        }

        public ICommand AddAccountCommand { get; }
        public ICommand RefreshInstancesCommand { get; }

        private async Task RefreshInstancesAsync()
        {
            if (!CookieAccess)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    RobloxInstances.Clear();
                    HasRunningInstances = false;
                });
                return;
            }

            var instances = App.Accounts.GetRunningInstances();

            AuthenticatedUser? activeUser = null;
            string? activeCookie = App.Cookies.GetActiveCookie();
            if (activeCookie is not null)
                activeUser = await App.Cookies.GetAuthenticatedForCookie(activeCookie);

            Application.Current.Dispatcher.Invoke(() =>
            {
                RobloxInstances.Clear();

                foreach (var instance in instances)
                {
                    var vm = new RobloxInstanceViewModel(
                        instance,
                        OnInstanceLogin,
                        OnInstanceLogout,
                        OnInstanceRemove);

                    if (activeUser is not null)
                    {
                        vm.AccountUserId = activeUser.Id;
                        vm.Username = activeUser.Username;
                        vm.DisplayName = activeUser.Displayname;
                    }

                    RobloxInstances.Add(vm);
                }

                HasRunningInstances = RobloxInstances.Count > 0;
            });

            await LoadAvatarsAsync();
        }

        private async Task LoadAvatarsAsync()
        {
            var userIds = RobloxInstances
                .Where(x => x.AccountUserId != 0)
                .Select(x => x.AccountUserId)
                .ToList();

            if (userIds.Count == 0)
                return;

            var avatars = await App.Accounts.GetAvatarsAsync(userIds, CancellationToken.None);

            Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var vm in RobloxInstances)
                {
                    if (vm.AccountUserId == 0)
                        continue;

                    if (avatars.TryGetValue(vm.AccountUserId, out string? url) && !string.IsNullOrEmpty(url))
                    {
                        try
                        {
                            var image = new BitmapImage();
                            image.BeginInit();
                            image.CacheOption = BitmapCacheOption.OnLoad;
                            image.UriSource = new System.Uri(url);
                            image.EndInit();
                            vm.Avatar = image;
                        }
                        catch (System.Exception ex)
                        {
                            App.Logger.WriteException("BehaviourViewModel::LoadAvatarsAsync", ex);
                        }
                    }
                }
            });
        }

        private async Task AddAccountAsync()
        {
            if (!CookieAccess)
                return;

            var dialog = new AddAccountDialog();
            if (dialog.ShowDialog() != true)
                return;

            string cookie = dialog.Cookie;
            if (string.IsNullOrWhiteSpace(cookie))
                return;

            var account = await App.Accounts.AddAccountAsync(cookie);

            if (account is null)
            {
                Frontend.ShowMessageBox(Strings.Menu_Behaviour_AccountManager_AddFailed, MessageBoxImage.Error);
                return;
            }

            await RefreshInstancesAsync();
        }

        private void OnInstanceLogin(RobloxInstanceViewModel vm)
        {
            var account = App.Accounts.Accounts.LastOrDefault();
            if (account is null)
            {
                Frontend.ShowMessageBox(Strings.Menu_Behaviour_AccountManager_NoAccounts, MessageBoxImage.Information);
                return;
            }

            if (App.Accounts.Login(account.UserId))
            {
                vm.AccountUserId = account.UserId;
                vm.Username = account.Username;
                vm.DisplayName = account.DisplayName;
                _ = LoadAvatarsAsync();
            }
        }

        private void OnInstanceLogout(RobloxInstanceViewModel vm)
        {
            App.Accounts.Logout();
            vm.AccountUserId = 0;
            vm.Username = string.Empty;
            vm.DisplayName = string.Empty;
            vm.Avatar = null;
        }

        private void OnInstanceRemove(RobloxInstanceViewModel vm)
        {
            if (vm.AccountUserId != 0)
                App.Accounts.RemoveAccount(vm.AccountUserId);

            vm.AccountUserId = 0;
            vm.Username = string.Empty;
            vm.DisplayName = string.Empty;
            vm.Avatar = null;
        }

        #endregion
        // guh
        private bool _cookieLoadingFailed;
        public bool CookieLoadingFailed
        {
            get => _cookieLoadingFailed;
            set
            {
                _cookieLoadingFailed = value;
                OnPropertyChanged(nameof(CookieLoadingFailed));
            }
        }

        public bool EnableBetterMatchmaking
        {
            get => App.Settings.Prop.EnableBetterMatchmaking;
            set => App.Settings.Prop.EnableBetterMatchmaking = value;
        }

        public bool EnableBetterMatchmakingRandomization
        {
            get => App.Settings.Prop.EnableBetterMatchmakingRandomization;
            set => App.Settings.Prop.EnableBetterMatchmakingRandomization = value;
        }
        public bool EnableFakeBorderlessFullscreen
        {
            get => App.Settings.Prop.FakeBorderlessFullscreen;
            set => App.Settings.Prop.FakeBorderlessFullscreen = value;
        }

        public bool ConfirmLaunches
        {
            get => App.Settings.Prop.ConfirmLaunches;
            set => App.Settings.Prop.ConfirmLaunches = value;
        }

        public bool ForceRobloxLanguage
        {
            get => App.Settings.Prop.ForceRobloxLanguage;
            set => App.Settings.Prop.ForceRobloxLanguage = value;
        }

        public bool BackgroundUpdates
        {
            get => App.Settings.Prop.BackgroundUpdatesEnabled;
            set => App.Settings.Prop.BackgroundUpdatesEnabled = value;
        }

        public CleanerOptions SelectedCleanUpMode
        {
            get => App.Settings.Prop.CleanerOptions;
            set => App.Settings.Prop.CleanerOptions = value;
        }

        public IEnumerable<CleanerOptions> CleanerOptions { get; } = CleanerOptionsEx.Selections;

        public CleanerOptions CleanerOption
        {
            get => App.Settings.Prop.CleanerOptions;
            set
            {
                App.Settings.Prop.CleanerOptions = value;
            }
        }

        private List<string> CleanerItems = App.Settings.Prop.CleanerDirectories;

        public bool CleanerLogs
        {
            get => CleanerItems.Contains("RobloxLogs");
            set
            {
                if (value)
                    CleanerItems.Add("RobloxLogs");
                else
                    CleanerItems.Remove("RobloxLogs"); // should we try catch it?
            }
        }

        public bool CleanerCache
        {
            get => CleanerItems.Contains("RobloxCache");
            set
            {
                if (value)
                    CleanerItems.Add("RobloxCache");
                else
                    CleanerItems.Remove("RobloxCache");
            }
        }

        public bool CleanerStudioCache
        {
            get => CleanerItems.Contains("RobloxStudioCache");
            set
            {
                if (value)
                    CleanerItems.Add("RobloxStudioCache");
                else
                    CleanerItems.Remove("RobloxStudioCache");
            }
        }

        public bool CleanerVanStrap
        {
            get => CleanerItems.Contains("VanStrapLogs");
            set
            {
                if (value)
                    CleanerItems.Add("VanStrapLogs");
                else
                    CleanerItems.Remove("VanStrapLogs");
            }
        }
    }
}
