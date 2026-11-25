using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using recipe_book_api.Auth;
using recipe_book_api.Data;
using recipe_book_api.Models;

namespace recipe_book_api.Tests;

public sealed class AuthCookieTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly ServiceProvider _services;

    public AuthCookieTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
        services.AddScoped(_ => _db.CreateContext());
        _services = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _services.Dispose();
        _db.Dispose();
    }

    [Fact]
    public void CreatePrincipal_ContainsIdentityClaims()
    {
        var user = new User { Id = 42, Username = "Bob", NormalizedUsername = "BOB", PasswordHash = "" };

        var principal = AuthCookie.CreatePrincipal(user);

        Assert.True(principal.Identity!.IsAuthenticated);
        Assert.Equal("42", principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal("Bob", principal.Identity.Name);
        Assert.Equal(user.SecurityStamp, principal.FindFirstValue(AuthCookie.SecurityStampClaim));
        Assert.False(principal.IsInRole(AuthCookie.AdminRole));

        var loginTime = DateTimeOffset.FromUnixTimeSeconds(long.Parse(principal.FindFirstValue(AuthCookie.LoginTimeClaim)!));
        Assert.True(DateTimeOffset.UtcNow - loginTime < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void CreatePrincipal_AddsAdminRoleForAdmins()
    {
        var user = User.Create("root", isAdmin: true);

        Assert.True(AuthCookie.CreatePrincipal(user).IsInRole(AuthCookie.AdminRole));
    }

    [Fact]
    public async Task Validate_AcceptsCurrentUserWithMatchingStamp()
    {
        var user = await AddUserAsync();

        var context = await ValidateAsync(AuthCookie.CreatePrincipal(user));

        Assert.NotNull(context.Principal);
    }

    [Fact]
    public async Task Validate_RejectsWhenStampChanged()
    {
        var user = await AddUserAsync();
        var principal = AuthCookie.CreatePrincipal(user);

        await using (var db = _db.CreateContext())
        {
            var stored = await db.Users.FindAsync(user.Id);
            stored!.SecurityStamp = User.NewSecurityStamp();
            await db.SaveChangesAsync();
        }

        var context = await ValidateAsync(principal);

        Assert.Null(context.Principal);
    }

    [Fact]
    public async Task Validate_RejectsDeletedUser()
    {
        var user = await AddUserAsync();
        var principal = AuthCookie.CreatePrincipal(user);

        await using (var db = _db.CreateContext())
        {
            db.Users.Remove((await db.Users.FindAsync(user.Id))!);
            await db.SaveChangesAsync();
        }

        Assert.Null((await ValidateAsync(principal)).Principal);
    }

    [Fact]
    public async Task Validate_RejectsSessionOlderThanMaxAge()
    {
        var user = await AddUserAsync();
        var tooOld = DateTimeOffset.UtcNow - AuthCookie.MaxSessionAge - TimeSpan.FromMinutes(1);

        var principal = WithClaim(AuthCookie.CreatePrincipal(user), AuthCookie.LoginTimeClaim, tooOld.ToUnixTimeSeconds().ToString());

        Assert.Null((await ValidateAsync(principal)).Principal);
    }

    [Fact]
    public async Task Validate_AcceptsSessionJustUnderMaxAge()
    {
        var user = await AddUserAsync();
        var almostTooOld = DateTimeOffset.UtcNow - AuthCookie.MaxSessionAge + TimeSpan.FromMinutes(1);

        var principal = WithClaim(AuthCookie.CreatePrincipal(user), AuthCookie.LoginTimeClaim, almostTooOld.ToUnixTimeSeconds().ToString());

        Assert.NotNull((await ValidateAsync(principal)).Principal);
    }

    [Theory]
    [InlineData(ClaimTypes.NameIdentifier)]
    [InlineData(AuthCookie.SecurityStampClaim)]
    [InlineData(AuthCookie.LoginTimeClaim)]
    public async Task Validate_RejectsMissingClaim(string claimType)
    {
        var user = await AddUserAsync();

        var principal = WithClaim(AuthCookie.CreatePrincipal(user), claimType, null);

        Assert.Null((await ValidateAsync(principal)).Principal);
    }

    [Theory]
    [InlineData(ClaimTypes.NameIdentifier, "not-a-number")]
    [InlineData(AuthCookie.LoginTimeClaim, "yesterday")]
    public async Task Validate_RejectsMalformedClaim(string claimType, string value)
    {
        var user = await AddUserAsync();

        var principal = WithClaim(AuthCookie.CreatePrincipal(user), claimType, value);

        Assert.Null((await ValidateAsync(principal)).Principal);
    }

    [Fact]
    public async Task Validate_RejectsAnotherUsersStamp()
    {
        var alice = await AddUserAsync("alice");
        var bob = await AddUserAsync("bob");

        // Alice's id with Bob's (valid) stamp must not authenticate.
        var principal = WithClaim(AuthCookie.CreatePrincipal(alice), AuthCookie.SecurityStampClaim, bob.SecurityStamp);

        Assert.Null((await ValidateAsync(principal)).Principal);
    }

    [Fact]
    public async Task Validate_SignsOutRejectedCookie()
    {
        var user = await AddUserAsync();
        var principal = WithClaim(AuthCookie.CreatePrincipal(user), AuthCookie.SecurityStampClaim, "stale");

        var context = await ValidateAsync(principal);

        var setCookie = context.HttpContext.Response.Headers.SetCookie.ToString();
        Assert.Contains(CookieAuthenticationDefaults.CookiePrefix, setCookie);
        Assert.Contains("expires=Thu, 01 Jan 1970", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<User> AddUserAsync(string username = "bob")
    {
        await using var db = _db.CreateContext();
        var user = User.Create(username);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private async Task<CookieValidatePrincipalContext> ValidateAsync(ClaimsPrincipal principal)
    {
        var scope = _services.CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var scheme = new AuthenticationScheme(
            CookieAuthenticationDefaults.AuthenticationScheme, null, typeof(CookieAuthenticationHandler));
        var ticket = new AuthenticationTicket(principal, CookieAuthenticationDefaults.AuthenticationScheme);
        var context = new CookieValidatePrincipalContext(http, scheme, new CookieAuthenticationOptions(), ticket);

        await AuthCookie.ValidateAsync(context);
        return context;
    }

    /// <summary>Copies the principal with <paramref name="type"/> replaced by <paramref name="value"/> (or removed if null).</summary>
    private static ClaimsPrincipal WithClaim(ClaimsPrincipal principal, string type, string? value)
    {
        var claims = principal.Claims.Where(c => c.Type != type).ToList();
        if (value is not null)
        {
            claims.Add(new Claim(type, value));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }
}
