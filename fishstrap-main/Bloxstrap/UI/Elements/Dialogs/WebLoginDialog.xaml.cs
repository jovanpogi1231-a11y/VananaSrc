using System;
using System.Threading.Tasks;
using System.Windows;

using Microsoft.Web.WebView2.Core;

namespace Bloxstrap.UI.Elements.Dialogs
{
	/// <summary>
	/// Hosts Roblox's real web login page so users can sign in (including 2FA)
	/// and the resulting .ROBLOSECURITY cookie is captured.
	/// </summary>
	public partial class WebLoginDialog
	{
		private const string LOG_IDENT = "WebLoginDialog";
		private const string LoginUrl = "https://www.roblox.com/login";
		private const string AuthCookieName = ".ROBLOSECURITY";

		/// <summary>
		/// The captured authentication cookie value, if login succeeded.
		/// </summary>
		public string Cookie { get; private set; } = string.Empty;

		private bool _completed;

		public WebLoginDialog()
		{
			InitializeComponent();
			Loaded += async (_, _) => await InitializeWebViewAsync();
		}

		private async Task InitializeWebViewAsync()
		{
			try
			{
				// use a private, isolated user data folder so we always start logged out
				string userDataFolder = System.IO.Path.Combine(Paths.Temp, "WebView2Login");
				System.IO.Directory.CreateDirectory(userDataFolder);

				var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
				await WebView.EnsureCoreWebView2Async(environment);

				// start from a clean session
				WebView.CoreWebView2.CookieManager.DeleteAllCookies();

				WebView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
				WebView.CoreWebView2.Navigate(LoginUrl);
			}
			catch (Exception ex)
			{
				App.Logger.WriteException($"{LOG_IDENT}::InitializeWebViewAsync", ex);
				Frontend.ShowMessageBox(
					"Failed to start the Roblox login window. Make sure the WebView2 runtime is installed.",
					MessageBoxImage.Error);
				DialogResult = false;
				Close();
			}
		}

		private async void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
		{
			if (_completed)
				return;

			try
			{
				string? source = WebView.CoreWebView2.Source;

				// once we leave the login/2fa pages we're authenticated; check for the cookie
				if (string.IsNullOrEmpty(source) || source.Contains("/login", StringComparison.OrdinalIgnoreCase))
					return;

				var cookies = await WebView.CoreWebView2.CookieManager.GetCookiesAsync("https://www.roblox.com");

				foreach (var cookie in cookies)
				{
					if (!cookie.Name.Equals(AuthCookieName, StringComparison.Ordinal))
						continue;

					if (string.IsNullOrWhiteSpace(cookie.Value))
						continue;

					_completed = true;
					Cookie = cookie.Value;
					App.Logger.WriteLine($"{LOG_IDENT}::CoreWebView2_NavigationCompleted", "Captured authentication cookie");

					DialogResult = true;
					Close();
					return;
				}
			}
			catch (Exception ex)
			{
				App.Logger.WriteException($"{LOG_IDENT}::CoreWebView2_NavigationCompleted", ex);
			}
		}

		private void WpfUiWindow_Closed(object sender, EventArgs e)
		{
			try
			{
				WebView.Dispose();
			}
			catch (Exception ex)
			{
				App.Logger.WriteException($"{LOG_IDENT}::WpfUiWindow_Closed", ex);
			}
		}
	}
}