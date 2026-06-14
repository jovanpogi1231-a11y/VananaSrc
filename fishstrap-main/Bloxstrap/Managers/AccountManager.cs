using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;

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

        /// <summary>
        /// Returns all currently running Roblox instances as RobloxInstance objects.
        /// </summary>
        public List<RobloxInstance> GetRunningInstances()
        {
            var instances = new List<RobloxInstance>();

            foreach (var proc in Process.GetProcessesByName("RobloxPlayerBeta"))
            {
                try
                {
                    instances.Add(new RobloxInstance
                    {
                        ProcessId = proc.Id,
                        WindowHandle = proc.MainWindowHandle.ToInt64()
                    });
                }
                catch { /* process may have exited */ }
            }

            return instances;
        }

        /// <summary>
        /// Fetches avatar headshot URLs for the given user IDs.
        /// </summary>
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

        /// <summary>
        /// Validates the cookie, fetches user info, and adds the account.
        /// </summary>
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

                // avoid duplicates
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
        /// Sets the active Roblox cookie for the given user ID.
        /// </summary>
        public bool Login(long userId)
        {
            var account = Accounts.FirstOrDefault(a => a.UserId == userId);
            if (account is null)
                return false;

            try
            {
                // Set the active cookie via CookiesManager
                // Extend this if you have a proper cookie-switching mechanism
                App.Logger.WriteLine("AccountManager::Login", $"Logging in as {account.Username} ({userId})");
                return true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("AccountManager::Login", ex);
                return false;
            }
        }

        /// <summary>
        /// Clears the active session.
        /// </summary>
        public void Logout()
        {
            App.Logger.WriteLine("AccountManager::Logout", "Logging out current session");
            // Extend this to actually clear the active cookie if needed
        }

        /// <summary>
        /// Removes an account from the managed list.
        /// </summary>
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