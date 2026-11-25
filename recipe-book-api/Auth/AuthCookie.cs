using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using recipe_book_api.Data;
using recipe_book_api.Models;

namespace recipe_book_api.Auth;

public static class AuthCookie
{
    public const string AdminRole = "Admin";

    /// <summary>A login lasts at most this long, even if sliding expiration keeps renewing it.</summary>
    public static readonly TimeSpan MaxSessionAge = TimeSpan.FromDays(7);

    internal const string SecurityStampClaim = "security_stamp";
    internal const string LoginTimeClaim = "login_time";

    public static ClaimsPrincipal CreatePrincipal(User user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(SecurityStampClaim, user.SecurityStamp),
            new(LoginTimeClaim, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()),
        };
        if (user.IsAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, AdminRole));
        }

        return new ClaimsPrincipal(
            new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    /// <summary>
    /// Runs on every authenticated request. Rejects the cookie if the user was deleted, their
    /// password or admin flag changed since login (security stamp mismatch), or the login is
    /// older than <see cref="MaxSessionAge"/>.
    /// </summary>
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        var stamp = principal?.FindFirstValue(SecurityStampClaim);

        var valid = int.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            && long.TryParse(principal?.FindFirstValue(LoginTimeClaim), out var loginTime)
            && DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(loginTime) <= MaxSessionAge
            && stamp is not null
            && await context.HttpContext.RequestServices
                .GetRequiredService<RecipeBookContext>()
                .Users.AnyAsync(u => u.Id == userId && u.SecurityStamp == stamp);

        if (!valid)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }
}
