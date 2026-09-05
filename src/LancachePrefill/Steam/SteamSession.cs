using System.Security.Cryptography;
using SteamKit2;
using SteamKit2.Authentication;
using SteamKit2.CDN;

namespace LancachePrefill;

public sealed class SteamSession : ISteamSession, IDisposable
{
    private readonly string _configDir;
    private readonly ILogger<SteamSession> _log;

    private SteamClient _client = null!;
    private CallbackManager _callbacks = null!;
    private SteamUser _steamUser = null!;
    private CancellationTokenSource? _callbackPumpCts;

    public SteamApps SteamApps { get; private set; } = null!;
    public SteamContent SteamContent { get; private set; } = null!;
    public Client CdnClient { get; private set; } = null!;
    public SteamID? SteamId { get; private set; }
    public HashSet<uint> OwnedAppIds { get; } = new();
    public HashSet<uint> OwnedDepotIds { get; } = new();
    public List<uint> OwnedPackageIds { get; } = new();
    public int ResolvedPackageCount { get; private set; }

    /// <summary>Web session token; reads as null once expired, forcing re-issue via auto-login.</summary>
    public string? SessionToken =>
        _sessionToken != null && DateTime.UtcNow - _sessionTokenIssuedAt < SessionTokenLifetime
            ? _sessionToken : null;

    private static readonly TimeSpan SessionTokenLifetime = TimeSpan.FromDays(7);
    private string? _sessionToken;
    private DateTime _sessionTokenIssuedAt;

    private bool _isConnected, _licensesReceived;
    private SteamUser.LoggedOnCallback? _logonResult;
    private int _loginAttempts;
    private DateTime _lastLoginAttempt = DateTime.MinValue;
    private readonly SemaphoreSlim _loginLock = new(1, 1);

    private string TokenPath => Path.Combine(_configDir, "token.enc");
    private string UsernamePath => Path.Combine(_configDir, "username.txt");
    private string CellIdPath => Path.Combine(_configDir, "cellid.txt");

    private uint CellId
    {
        get => File.Exists(CellIdPath) ? uint.Parse(File.ReadAllText(CellIdPath)) : 0u;
        set => File.WriteAllText(CellIdPath, value.ToString());
    }

    public SteamSession(string configDir, ILogger<SteamSession> log)
    {
        _configDir = configDir;
        _log = log;
        Directory.CreateDirectory(configDir);
        MigratePlaintextToken();
    }

    public bool HasCredentials => File.Exists(TokenPath) && File.Exists(UsernamePath);

    public async Task<string?> LoginAsync(string? username = null, string? password = null,
        string? twoFactorCode = null, string? emailCode = null)
    {
        // Serialize logins — the UI, page-load auto-login, and background schedulers
        // can all try to (re)establish the session concurrently.
        await _loginLock.WaitAsync();
        try { return await LoginLockedAsync(username, password, twoFactorCode, emailCode); }
        finally { _loginLock.Release(); }
    }

    /// <summary>
    /// Ensures a live, logged-in Steam session, re-logging in with the stored
    /// refresh token when needed. Never throws — safe for background schedulers.
    /// </summary>
    public async Task<bool> EnsureConnectedAsync()
    {
        if (SteamId != null && _isConnected) return true;
        if (!HasCredentials) return false;
        try
        {
            var result = await LoginAsync();
            if (result != null) _log.LogWarning("Background Steam re-login failed: {Reason}", result);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Background Steam re-login failed");
        }
        return SteamId != null && _isConnected;
    }

    private async Task<string?> LoginLockedAsync(string? username, string? password,
        string? twoFactorCode, string? emailCode)
    {
        // Another caller may have completed the login while we waited on the lock.
        if (SteamId != null && _isConnected && username == null)
        {
            // The Steam session is live but the web token may have expired — re-mint
            // so auto-login returns a usable token instead of null.
            MintSessionTokenIfNeeded();
            return null;
        }

        if (_loginAttempts >= 5 && DateTime.UtcNow - _lastLoginAttempt < TimeSpan.FromMinutes(5))
            return "too_many_attempts";
        if (DateTime.UtcNow - _lastLoginAttempt >= TimeSpan.FromMinutes(5))
            _loginAttempts = 0;
        _loginAttempts++;
        _lastLoginAttempt = DateTime.UtcNow;

        InitClient();

        if (!_isConnected)
        {
            _client.Connect();
            await WaitFor(() => _isConnected, TimeSpan.FromSeconds(30), "connect to Steam");
        }

        var storedToken = LoadToken();
        var storedUsername = File.Exists(UsernamePath) ? File.ReadAllText(UsernamePath).Trim() : null;

        if (!string.IsNullOrEmpty(storedToken) && !string.IsNullOrEmpty(storedUsername))
        {
            _logonResult = null; // clear any stale result from a previous login/reconnect
            _steamUser.LogOn(new SteamUser.LogOnDetails
                { Username = storedUsername, AccessToken = storedToken, ShouldRememberPassword = true });
            await WaitFor(() => _logonResult != null, TimeSpan.FromSeconds(30), "log in");

            if (_logonResult!.Result == EResult.OK)
            {
                await PostLogin();
                // Re-save so the token migrates to the current-best encryption key
                // (e.g. after TOKEN_ENCRYPTION_KEY is introduced).
                SaveToken(storedToken);
                return null;
            }
            _logonResult = null;
        }

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            return "credentials_required";

        try
        {
            var authSession = await _client.Authentication.BeginAuthSessionViaCredentialsAsync(
                new AuthSessionDetails
                {
                    Username = username, Password = password,
                    IsPersistentSession = true,
                    Authenticator = new WebAuthenticator(twoFactorCode, emailCode)
                });

            var pollResponse = await authSession.PollingWaitForResultAsync();
            SaveToken(pollResponse.RefreshToken);
            File.WriteAllText(UsernamePath, username);

            _logonResult = null; // clear any stale result before the fresh LogOn
            _steamUser.LogOn(new SteamUser.LogOnDetails
                { Username = username, AccessToken = pollResponse.RefreshToken, ShouldRememberPassword = true });
            await WaitFor(() => _logonResult != null, TimeSpan.FromSeconds(30), "log in");

            if (_logonResult!.Result != EResult.OK)
                return $"login_failed:{_logonResult.Result}";

            await PostLogin();
            return null;
        }
        catch (AuthenticationException ex) when (ex.Result == EResult.AccountLoginDeniedNeedTwoFactor) { return "2fa_required"; }
        catch (AuthenticationException ex) when (ex.Result == EResult.AccountLogonDenied) { return "email_code_required"; }
        catch (AuthenticationException ex) when (ex.Result == EResult.TwoFactorCodeMismatch) { return "2fa_invalid"; }
        catch (AuthenticationException ex) when (ex.Result == EResult.InvalidPassword) { return "invalid_password"; }
    }

    private async Task PostLogin()
    {
        SteamId = _logonResult!.ClientSteamID;
        CellId = _logonResult.CellID;
        _loginAttempts = 0;
        _log.LogInformation("Logged in as {SteamId}", SteamId);

        try
        {
            await WaitFor(() => _licensesReceived, TimeSpan.FromSeconds(30), "receive licenses");
            await ResolvePackagesToAppsAsync(OwnedPackageIds);
            // Keep a live web session valid across background Steam re-logins; mint a
            // new token only when there is none (fresh login, after logout, or expired).
            MintSessionTokenIfNeeded();
        }
        catch
        {
            SteamId = null;
            _sessionToken = null;
            throw;
        }
    }

    private void SaveToken(string token)
    {
        File.WriteAllText(TokenPath, TokenProtection.Encrypt(token, _configDir));
        if (OperatingSystem.IsLinux())
            File.SetUnixFileMode(TokenPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private string? LoadToken() =>
        File.Exists(TokenPath) ? TokenProtection.Decrypt(File.ReadAllText(TokenPath).Trim(), _configDir) : null;

    private void MigratePlaintextToken()
    {
        var oldPath = Path.Combine(_configDir, "token.txt");
        if (!File.Exists(oldPath) || File.Exists(TokenPath)) return;
        var plaintext = File.ReadAllText(oldPath).Trim();
        if (!string.IsNullOrEmpty(plaintext)) SaveToken(plaintext);
        File.Delete(oldPath);
    }

    private void InitClient()
    {
        if (_client != null) return;

        _client = new SteamClient(SteamConfiguration.Create(c =>
            c.WithCellID(CellId).WithConnectionTimeout(TimeSpan.FromSeconds(15))));

        _steamUser = _client.GetHandler<SteamUser>()!;
        SteamApps = _client.GetHandler<SteamApps>()!;
        SteamContent = _client.GetHandler<SteamContent>()!;
        CdnClient = new Client(_client);
        Client.RequestTimeout = TimeSpan.FromSeconds(60);

        _callbacks = new CallbackManager(_client);
        _callbacks.Subscribe<SteamClient.ConnectedCallback>(_ => _isConnected = true);
        _callbacks.Subscribe<SteamClient.DisconnectedCallback>(_ =>
        {
            _isConnected = false;
            // Steam CMs routinely drop idle connections (nightly, plus Tuesday
            // maintenance). Clear SteamId so schedulers and endpoints see the truth
            // and re-login with the stored refresh token instead of failing mid-job.
            if (SteamId != null)
                _log.LogInformation("Steam disconnected — will re-login on next use");
            SteamId = null;
        });
        _callbacks.Subscribe<SteamUser.LoggedOnCallback>(cb => { _logonResult = cb; CellId = cb.CellID; });
        _callbacks.Subscribe<SteamApps.LicenseListCallback>(OnLicenseList);

        _callbackPumpCts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            while (!_callbackPumpCts.Token.IsCancellationRequested)
            {
                _callbacks.RunWaitAllCallbacks(TimeSpan.FromMilliseconds(100));
                try { await Task.Delay(50, _callbackPumpCts.Token); }
                catch (TaskCanceledException) { break; }
            }
        }, _callbackPumpCts.Token);
    }

    private void OnLicenseList(SteamApps.LicenseListCallback cb)
    {
        if (cb.Result != EResult.OK) { _log.LogError("License list failed: {Result}", cb.Result); }
        else { OwnedPackageIds.Clear(); foreach (var l in cb.LicenseList) OwnedPackageIds.Add(l.PackageID); }
        _licensesReceived = true;
    }

    public async Task ResolvePackagesToAppsAsync(IEnumerable<uint> packageIds)
    {
        var requests = packageIds.Select(id => new SteamApps.PICSRequest(id)).ToList();
        if (requests.Count == 0) return;
        ResolvedPackageCount = 0;

        foreach (var batch in requests.Chunk(25))
        {
            var batchList = batch.ToList();
            bool resolved = false;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    var result = await SteamApps.PICSGetProductInfo([], batchList).ToTask();
                    if (result.Results != null)
                        foreach (var pkg in result.Results.SelectMany(r => r.Packages).Select(p => p.Value))
                        {
                            foreach (var c in pkg.KeyValues["appids"].Children) OwnedAppIds.Add(c.AsUnsignedInteger());
                            foreach (var c in pkg.KeyValues["depotids"].Children) OwnedDepotIds.Add(c.AsUnsignedInteger());
                        }
                    ResolvedPackageCount += batchList.Count;
                    resolved = true;
                    break;
                }
                catch (AsyncJobFailedException) when (attempt < 2)
                {
                    _log.LogWarning("PICS package batch timed out, retry {N}/2", attempt + 1);
                    await Task.Delay(3000 * (attempt + 1));
                }
            }
            if (!resolved)
                _log.LogError("Failed to resolve {Count} packages after 3 attempts", batchList.Count);
        }
        _log.LogInformation("Resolved {Resolved}/{Total} packages → {Apps} apps, {Depots} depots",
            ResolvedPackageCount, requests.Count, OwnedAppIds.Count, OwnedDepotIds.Count);
    }

    private void MintSessionTokenIfNeeded()
    {
        if (SessionToken == null) // none yet, invalidated, or expired
        {
            _sessionToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            _sessionTokenIssuedAt = DateTime.UtcNow;
        }
    }

    public void InvalidateSession()
    {
        _sessionToken = null;
        _log.LogInformation("Session invalidated");
    }

    public void Disconnect() { _callbackPumpCts?.Cancel(); if (_isConnected) _client?.Disconnect(); }

    private async Task WaitFor(Func<bool> condition, TimeSpan timeout, string description)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(100);
        if (!condition()) throw new TimeoutException($"Timed out waiting to {description}");
    }

    public void Dispose() { _callbackPumpCts?.Cancel(); CdnClient?.Dispose(); }
}

file class WebAuthenticator(string? twoFactorCode, string? emailCode) : IAuthenticator
{
    public Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect) => Task.FromResult(twoFactorCode ?? "");
    public Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect) => Task.FromResult(emailCode ?? "");
    public Task<bool> AcceptDeviceConfirmationAsync() => Task.FromResult(true);
}
