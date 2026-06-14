using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bloxstrap.Models;

namespace Bloxstrap
{
    public class AccountManager
    {
        public ObservableCollection<ManagedAccount> Accounts { get; } = new();

        // Tracks which managed account (user ID) launched which Roblox process.
        // Lets the account manager show the correct account for a running PID.
        private readonly Dictionary<int, long> _processAccountMap = new();

        private static string AccountsPath => Path.Combine(Paths.Base, "Accounts.json");

        private bool _loaded;

        public void Load()
        {
            const string LOG_IDENT = "AccountManager::Load";

            // Only load once per session; the store is the source of truth and
            // is kept in sync via Save().
            if (_loaded)
                return;

            try
            {
                if (!Paths.Initialized || !File.Exists(AccountsPath))
                {
                    _loaded = true;
                    return;
                }

                string json = File.ReadAllText(AccountsPath);
                var stored = JsonSerializer.Deserialize<List<ManagedAccount>>(json);

                Accounts.Clear();

                if (stored is not null)
                {
                    foreach (var account in stored)
                    {
                        // Cookies are stored DPAPI-encrypted at rest; decrypt
                        // back to the raw value used in memory.
                        account.EncryptedCookie = Decrypt(account.EncryptedCookie);

                        if (string.IsNullOrEmpty(account.EncryptedCookie))
                            continue;

                        Accounts.Add(account);
                    }
                }

                _loaded = true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                _loaded = true;
            }
        }

        public void Save()
        {
            const string LOG_IDENT = "AccountManager::Save";

            try
            {
                if (!Paths.Initialized)
                    return;

                // Serialize a copy with the cookie encrypted at rest. We must
                // not mutate the in-memory accounts (they hold the raw cookie).
                var toStore = Accounts.Select(a => new ManagedAccount
                {
                    UserId = a.UserId,
                    Username = a.Username,
                    DisplayName = a.DisplayName,
                    EncryptedCookie = Encrypt(a.EncryptedCookie)
                }).ToList();

                string json = JsonSerializer.Serialize(toStore);
                File.WriteAllText(AccountsPath, json);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        private static string Encrypt(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            byte[] data = Encoding.UTF8.GetBytes(value);
            byte[] encrypted = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encrypted);
        }

        private static string Decrypt(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            try
            {
                byte[] encrypted = Convert.FromBase64String(value);
                byte[] data = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch
            {
                // Either not encrypted (legacy/raw) or undecryptable on this
                // machine/user. Treat raw values as-is for backwards compat.
                return value;
            }
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

                    _processAccountMap.TryGetValue(proc.Id, out long mappedUserId);

                    instances.Add(new RobloxInstance
                    {
                        ProcessId = proc.Id,
                        WindowHandle = proc.MainWindowHandle.ToInt64(),
                        AccountUserId = mappedUserId
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

        /// <summary>
        /// Activates the given account's cookie and launches Roblox as that account.
        /// </summary>
        public async Task<bool> PlayAs(long userId)
        {
            const string LOG_IDENT = "AccountManager::PlayAs";

            var account = Accounts.FirstOrDefault(a => a.UserId == userId);
            if (account is null)
            {
                App.Logger.WriteLine(LOG_IDENT, $"No account found for {userId}");
                return false;
            }

            if (string.IsNullOrEmpty(account.EncryptedCookie))
            {
                App.Logger.WriteLine(LOG_IDENT, $"No stored cookie for {account.Username} ({userId})");
                return false;
            }

            try
            {
                App.Logger.WriteLine(LOG_IDENT, $"Requesting auth ticket for {account.Username} ({userId})");

                // Fetch a one-time authentication ticket for this account's
                // cookie. Passing it on the launch command line is what makes
                // RobloxPlayerBeta start logged in as the chosen account,
                // independent of whatever cookie is in the shared store.
                string? ticket = await App.Cookies.GetAuthTicketAsync(account.EncryptedCookie);

                if (string.IsNullOrEmpty(ticket))
                {
                    App.Logger.WriteLine(LOG_IDENT, "Could not get an authentication ticket (cookie may be expired)");
                    return false;
                }

                string executablePath = GetPlayerExecutablePath();
                if (string.IsNullOrEmpty(executablePath) || !File.Exists(executablePath))
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Roblox player executable not found at '{executablePath}'");
                    return false;
                }

                // Build the launch command line the same shape the Roblox
                // website uses. Without a place, the client opens to the home
                // page already authenticated.
                long launchTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                string launchUrl =
                    "roblox-player:1" +
                    "+launchmode:play" +
                    "+gameinfo:" + ticket +
                    "+launchtime:" + launchTime +
                    "+placelauncherurl:" +
                    "+browsertrackerid:" +
                    "+robloxLocale:en_us" +
                    "+gameLocale:en_us";


                App.Logger.WriteLine(LOG_IDENT, $"Launching Roblox as {account.Username} ({userId})");

                // Snapshot existing Roblox PIDs so we can identify the new one.
                var before = new HashSet<int>();
                foreach (var p in Process.GetProcessesByName("RobloxPlayerBeta"))
                {
                    before.Add(p.Id);
                    p.Dispose();
                }


                var startInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = launchUrl,
                    WorkingDirectory = Path.GetDirectoryName(executablePath)!,
                    UseShellExecute = false
                };

                Process.Start(startInfo);

                // Multi-instance support: launch the watcher so the singleton
                // mutex is released, allowing additional accounts to run.
                if (App.Settings.Prop.MultiInstanceLaunching)
                    StartMultiInstanceWatcher();

                // Associate the newly spawned process with this account so the
                // UI shows the correct account instead of "no account assigned".
                _ = Task.Run(() => TrackLaunchedProcess(userId, before));

                return true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                return false;
            }
        }
        private static string GetPlayerExecutablePath()
        {
            try
            {
                var appData = new AppData.RobloxPlayerData();
                return appData.ExecutablePath;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void StartMultiInstanceWatcher()
        {
            const string LOG_IDENT = "AccountManager::StartMultiInstanceWatcher";

            try
            {
                Process.Start(Paths.Application, "-multiinstancewatcher");
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        private void TrackLaunchedProcess(long userId, HashSet<int> existingPids)
        {
            const string LOG_IDENT = "AccountManager::TrackLaunchedProcess";

            try
            {
                // Poll up to ~60s; the bootstrapper may need to update/extract
                // before Roblox actually starts.
                for (int i = 0; i < 120; i++)
                {
                    var processes = Process.GetProcessesByName("RobloxPlayerBeta");
                    int? newPid = null;

                    foreach (var p in processes)
                    {
                        if (!existingPids.Contains(p.Id))
                            newPid = p.Id;
                        p.Dispose();
                    }

                    if (newPid is not null)
                    {
                        _processAccountMap[newPid.Value] = userId;
                        App.Logger.WriteLine(LOG_IDENT, $"Mapped PID {newPid} to account {userId}");
                        return;
                    }

                    Thread.Sleep(500);
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
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