using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using Bloxstrap.Models;

namespace Bloxstrap
{
    public class AccountManager
    {
        public ObservableCollection<ManagedAccount> Accounts { get; } = new();

        public void Load()
        {
            // Load saved accounts from disk if needed
            // Extend this when you have persistent storage
        }

        public void Save()
        {
            // Persist accounts to disk if needed
        }

        public List<RobloxInstance> GetRunningInstances()
        {
            var instances = new List<RobloxInstance>();

            const string LOG_IDENT = "AccountManager::GetRunningInstances";

            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName("RobloxPlayerBeta");
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                return instances;
            }

            foreach (var proc in processes)
            {
                try
                {

                    if (proc.HasExited)
                        continue;

                    instances.Add(new RobloxInstance
                    {
                        ProcessId = proc.Id,
                        WindowHandle = proc.MainWindowHandle.ToInt64()
                    });
                }

                catch (Exception ex)
                {
                    App.Logger.WriteException(LOG_IDENT, ex);
                }
                finally
                {
                    try { proc.Dispose(); } catch { }
                }
            }

            return instances;
        }

        public async Task<Dictionary<long, string>> GetAvatarsAsync(List<long> userIds, CancellationToken cancellationToken)
        {
            var result = new Dictionary<long, string>();

            if (userIds.Count == 0)
                return result;

            try
            {
                string ids = string.Join(",", userIds);
                string url = $"https://thumbnails.roblox.com/v1/users/avatar-headshot?userIds={ids}&size=150x150&format=Png&isCircular=false";

                var response = await App.HttpClient.GetAsync(url, cancellationToken);
                response.EnsureSuccessStatusCode();

                string json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("data", out var data))
                {
                    foreach (var entry in data.EnumerateArray())
                    {
                        long id = entry.GetProperty("targetId").GetInt64();
                        string? imageUrl = entry.GetProperty("imageUrl").GetString();
                        if (imageUrl is not null)
                            result[id] = imageUrl;
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("AccountManager::GetAvatarsAsync", ex);
            }

            return result;
        }

        public async Task<ManagedAccount?> AddAccountAsync(string cookie)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://users.roblox.com/v1/users/authenticated");
                request.Headers.Add("Cookie", $".ROBLOSECURITY={cookie}");

                var response = await App.HttpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                    return null;

                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                long userId = doc.RootElement.GetProperty("id").GetInt64();
                string username = doc.RootElement.GetProperty("name").GetString() ?? string.Empty;
                string displayName = doc.RootElement.GetProperty("displayName").GetString() ?? string.Empty;

                var account = new ManagedAccount
                {
                    UserId = userId,
                    Username = username,
                    DisplayName = displayName,
                    EncryptedCookie = cookie // store raw for now, encrypt later if needed
                };

                var existing = Accounts.FirstOrDefault(a => a.UserId == userId);
                if (existing is not null)
                    Accounts.Remove(existing);

                Accounts.Add(account);
                Save();

                return account;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("AccountManager::AddAccountAsync", ex);
                return null;
            }
        }

        /// <summary>
        /// Sets the active Roblox cookie for the given user ID by swapping it
        /// into the CookiesManager store.
        /// </summary>

        public bool Login(long userId)
        {
            const string LOG_IDENT = "AccountManager::Login";

            var account = Accounts.FirstOrDefault(a => a.UserId == userId);
            if (account is null)
                return false;

            if (string.IsNullOrEmpty(account.EncryptedCookie))
            {
                App.Logger.WriteLine(LOG_IDENT, $"No stored cookie for {account.Username} ({userId})");
                return false;
            }

            try
            {
                App.Logger.WriteLine(LOG_IDENT, $"Logging in as {account.Username} ({userId})");
                App.Cookies.SetActiveCookie(account.EncryptedCookie);
                return true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                return false;
            }
        }

        /// </summary>

        public void Logout()
        {
            const string LOG_IDENT = "AccountManager::Logout";

            App.Logger.WriteLine(LOG_IDENT, "Logging out current session");

            try
            {
                App.Cookies.ClearActiveCookie();
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        /// <summary>

        public void RemoveAccount(long userId)
        {
            var account = Accounts.FirstOrDefault(a => a.UserId == userId);
            if (account is not null)
            {
                Accounts.Remove(account);
                Save();
            }
        }
    }
}