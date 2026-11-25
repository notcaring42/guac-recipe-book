using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using recipe_book_api.Data;
using recipe_book_api.Dtos;
using recipe_book_api.Models;

namespace recipe_book_api.Tests;

/// <summary>Runs the real app against a throwaway SQLite file, with a known admin password.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string AdminPassword = "test-admin-password";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"recipe-book-tests-{Guid.NewGuid():N}.db");
    private readonly EnvVarScope _adminPassword = new(AdminSeeder.PasswordEnvVar, AdminPassword);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:DefaultConnection", $"Data Source={_dbPath}");
    }

    public RecipeBookContext CreateDbContext() =>
        Services.CreateScope().ServiceProvider.GetRequiredService<RecipeBookContext>();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _adminPassword.Dispose();
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }
}

public sealed class AuthEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string UserPassword = "user-password";

    [Theory]
    [InlineData("GET", "/auth/me")]
    [InlineData("POST", "/auth/logout")]
    [InlineData("GET", "/foods/1")]
    [InlineData("DELETE", "/foods/1")]
    [InlineData("GET", "/users/1")]
    public async Task ProtectedEndpoints_RequireLogin(string method, string path)
    {
        var response = await factory.CreateClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnauthenticatedRequest_Gets401NotRedirect()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsUserAndSetsCookie()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/login", new LoginRequest("admin", ApiFactory.AdminPassword));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.Equal("admin", body!.Username);
        Assert.True(body.IsAdmin);

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("recipe-book.auth=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("  Admin ")]
    [InlineData("ａｄｍｉｎ")]
    public async Task Login_UsernameIsNormalized(string username)
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/auth/login", new LoginRequest(username, ApiFactory.AdminPassword));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("admin", "wrong-password")]
    [InlineData("admin", "TEST-ADMIN-PASSWORD")]
    [InlineData("nobody", ApiFactory.AdminPassword)]
    public async Task Login_WithBadCredentials_Returns401WithoutCookie(string username, string password)
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/auth/login", new LoginRequest(username, password));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Login_WithMissingFields_Returns400()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/auth/login", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Me_ReturnsLoggedInUser()
    {
        var client = await LoginAsAdminAsync();

        var me = await client.GetFromJsonAsync<UserResponse>("/auth/me");

        Assert.Equal("admin", me!.Username);
        Assert.True(me.IsAdmin);
    }

    [Fact]
    public async Task Logout_EndsSession()
    {
        var client = await LoginAsAdminAsync();

        var logout = await client.PostAsync("/auth/logout", null);
        var me = await client.GetAsync("/auth/me");

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Admin_CanCreateAndFetchUser()
    {
        var admin = await LoginAsAdminAsync();
        var username = UniqueName();

        var created = await admin.PostAsJsonAsync("/users", new CreateUserRequest(username, UserPassword));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<UserResponse>();
        Assert.Equal(username, body!.Username);
        Assert.False(body.IsAdmin);
        Assert.Equal($"/users/{body.Id}", created.Headers.Location!.ToString());

        var fetched = await admin.GetFromJsonAsync<UserResponse>($"/users/{body.Id}");
        Assert.Equal(body, fetched);
    }

    [Fact]
    public async Task Admin_GetMissingUser_Returns404()
    {
        var admin = await LoginAsAdminAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/users/999999")).StatusCode);
    }

    [Fact]
    public async Task CreateUser_WithInvalidInput_Returns400()
    {
        var admin = await LoginAsAdminAsync();

        var response = await admin.PostAsJsonAsync("/users", new CreateUserRequest(UniqueName(), "short"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("ａｄｍｉｎ")]
    public async Task CreateUser_WithNormalizedDuplicate_Returns409(string username)
    {
        var admin = await LoginAsAdminAsync();

        var response = await admin.PostAsJsonAsync("/users", new CreateUserRequest(username, UserPassword));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task NewUser_CanLogInButIsNotAdmin()
    {
        var (client, username) = await CreateAndLoginUserAsync();

        var me = await client.GetFromJsonAsync<UserResponse>("/auth/me");

        Assert.Equal(username, me!.Username);
        Assert.False(me.IsAdmin);
    }

    [Fact]
    public async Task NonAdmin_IsForbiddenFromAdminEndpoints()
    {
        var (client, _) = await CreateAndLoginUserAsync();

        var create = await client.PostAsJsonAsync("/users", new CreateUserRequest(UniqueName(), UserPassword));
        var get = await client.GetAsync("/users/1");

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);
    }

    [Fact]
    public async Task NonAdmin_CanUseNonAdminEndpoints()
    {
        var (client, _) = await CreateAndLoginUserAsync();

        var response = await client.GetAsync("/foods/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ChangingSecurityStamp_RevokesExistingSession()
    {
        var (client, username) = await CreateAndLoginUserAsync();

        await using (var db = factory.CreateDbContext())
        {
            var user = db.Users.Single(u => u.NormalizedUsername == User.NormalizeUsername(username));
            user.SecurityStamp = User.NewSecurityStamp();
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/auth/me")).StatusCode);
    }

    [Fact]
    public async Task PromotingUserToAdmin_RequiresNewLoginToTakeEffect()
    {
        var (client, username) = await CreateAndLoginUserAsync();

        await using (var db = factory.CreateDbContext())
        {
            var user = db.Users.Single(u => u.NormalizedUsername == User.NormalizeUsername(username));
            user.IsAdmin = true;
            user.SecurityStamp = User.NewSecurityStamp();
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/users/1")).StatusCode);

        await client.PostAsJsonAsync("/auth/login", new LoginRequest(username, UserPassword));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/users/1")).StatusCode);
    }

    private async Task<HttpClient> LoginAsAdminAsync()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/auth/login", new LoginRequest("admin", ApiFactory.AdminPassword));
        response.EnsureSuccessStatusCode();
        return client;
    }

    private async Task<(HttpClient Client, string Username)> CreateAndLoginUserAsync()
    {
        var admin = await LoginAsAdminAsync();
        var username = UniqueName();
        (await admin.PostAsJsonAsync("/users", new CreateUserRequest(username, UserPassword))).EnsureSuccessStatusCode();

        var client = factory.CreateClient();
        (await client.PostAsJsonAsync("/auth/login", new LoginRequest(username, UserPassword))).EnsureSuccessStatusCode();
        return (client, username);
    }

    private static string UniqueName() => $"user-{Guid.NewGuid():N}"[..20];
}
