using LancachePrefill.Services;
using Microsoft.Extensions.Localization;

namespace LancachePrefill.Api;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapGet("/status", async (SteamSession session, DepotDownloader? downloader) =>
        {
            var lancacheIp = downloader != null ? await downloader.DetectLancacheAsync() : null;
            return Results.Ok(new
            {
                loggedIn = session.SteamId != null,
                hasCredentials = session.HasCredentials,
                lancacheDetected = lancacheIp != null,
                lancacheIp
            });
        });

        group.MapPost("/login", async (LoginRequest req, SteamSession session) =>
        {
            try
            {
                var result = await session.LoginAsync(req.Username, req.Password, req.TwoFactorCode, req.EmailCode);
                return result == null
                    ? Results.Ok(new { success = true, sessionToken = session.SessionToken, steamId = session.SteamId?.ToString() })
                    : Results.Ok(new { success = false, next = result, sessionToken = (string?)null, steamId = (string?)null });
            }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });

        group.MapPost("/logout", (SteamSession session, JobCoordinator jobs) =>
        {
            jobs.CancelJob();
            session.InvalidateSession();
            return Results.Ok(new { success = true });
        });

        group.MapPost("/auto-login", async (SteamSession session) =>
        {
            if (session.SteamId != null) return Results.Ok(new { success = true, sessionToken = session.SessionToken });
            if (!session.HasCredentials) return Results.Ok(new { success = false, sessionToken = (string?)null });
            try
            {
                var result = await session.LoginAsync();
                return result == null
                    ? Results.Ok(new { success = true, sessionToken = session.SessionToken })
                    : Results.Ok(new { success = false, sessionToken = (string?)null });
            }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });

        return group;
    }
}
