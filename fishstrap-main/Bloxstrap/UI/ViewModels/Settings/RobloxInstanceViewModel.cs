using System;
using System.Windows.Input;
using System.Windows.Media.Imaging;

using CommunityToolkit.Mvvm.Input;

using Bloxstrap.Models;
using Bloxstrap.UI.ViewModels;

namespace Bloxstrap.UI.ViewModels.Settings
{
	public class RobloxInstanceViewModel : NotifyPropertyChangedViewModel
	{
		private readonly RobloxInstance? _instance;
		private readonly Action<RobloxInstanceViewModel> _onLogin;
		private readonly Action<RobloxInstanceViewModel> _onLogout;
		private readonly Action<RobloxInstanceViewModel> _onRemove;

		public RobloxInstanceViewModel(
			RobloxInstance? instance,
			Action<RobloxInstanceViewModel> onLogin,
			Action<RobloxInstanceViewModel> onLogout,
			Action<RobloxInstanceViewModel> onRemove)
		{
			_instance = instance;
			_onLogin = onLogin;
			_onLogout = onLogout;
			_onRemove = onRemove;

			LoginCommand = new RelayCommand(() => _onLogin(this));
			LogoutCommand = new RelayCommand(() => _onLogout(this));
			RemoveCommand = new RelayCommand(() => _onRemove(this));
		}

		public int ProcessId => _instance?.ProcessId ?? 0;
		public long WindowHandle => _instance?.WindowHandle ?? 0;
		public bool IsRunning => _instance is not null;

		private long _accountUserId;
		public long AccountUserId
		{
			get => _accountUserId;
			set
			{
				_accountUserId = value;
				OnPropertyChanged(nameof(AccountUserId));
				OnPropertyChanged(nameof(HasAccount));
			}
		}

		private string _username = string.Empty;
		public string Username
		{
			get => _username;
			set
			{
				_username = value;
				OnPropertyChanged(nameof(Username));
			}
		}

		private string _displayName = string.Empty;
		public string DisplayName
		{
			get => _displayName;
			set
			{
				_displayName = value;
				OnPropertyChanged(nameof(DisplayName));
			}
		}

		private BitmapImage? _avatar;
		public BitmapImage? Avatar
		{
			get => _avatar;
			set
			{
				_avatar = value;
				OnPropertyChanged(nameof(Avatar));
			}
		}

		public bool HasAccount => _accountUserId != 0;

		public ICommand LoginCommand { get; }
		public ICommand LogoutCommand { get; }
		public ICommand RemoveCommand { get; }
	}
}