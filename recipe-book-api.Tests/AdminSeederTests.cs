using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using recipe_book_api.Data;
using recipe_book_api.Models;

namespace recipe_book_api.Tests;

public sealed class AdminSeederTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly PasswordHasher<User> _hasher = new();

    public void Dispose() => _db.Dispose();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FirstRun_WithoutPassword_Throws(string? password)
    {
        using var _ = new EnvVarScope(AdminSeeder.PasswordEnvVar, password);

        var ex = Assert.Throws<InvalidOperationException>(Seed);
        Assert.Contains(AdminSeeder.PasswordEnvVar, ex.Message);
    }

    [Fact]
    public void FirstRun_CreatesAdminWithPassword()
    {
        using var _ = new EnvVarScope(AdminSeeder.PasswordEnvVar, "first-password");

        Seed();

        var admin = LoadAdmin();
        Assert.True(admin.IsAdmin);
        Assert.Equal(AdminSeeder.AdminUsername, admin.Username);
        Assert.Equal(User.NormalizeUsername(AdminSeeder.AdminUsername), admin.NormalizedUsername);
        Assert.NotEqual(PasswordVerificationResult.Failed, Verify(admin, "first-password"));
    }

    [Fact]
    public void LaterRun_WithoutPassword_KeepsExistingAdmin()
    {
        using (new EnvVarScope(AdminSeeder.PasswordEnvVar, "first-password")) Seed();
        var before = LoadAdmin();

        using var _ = new EnvVarScope(AdminSeeder.PasswordEnvVar, null);
        Seed();

        var after = LoadAdmin();
        Assert.Equal(before.PasswordHash, after.PasswordHash);
        Assert.Equal(before.SecurityStamp, after.SecurityStamp);
    }

    [Fact]
    public void LaterRun_WithSamePassword_ChangesNothing()
    {
        using var _ = new EnvVarScope(AdminSeeder.PasswordEnvVar, "first-password");
        Seed();
        var before = LoadAdmin();

        Seed();

        var after = LoadAdmin();
        Assert.Equal(before.PasswordHash, after.PasswordHash);
        Assert.Equal(before.SecurityStamp, after.SecurityStamp);
    }

    [Fact]
    public void LaterRun_WithNewPassword_UpdatesPasswordAndRevokesSessions()
    {
        using (new EnvVarScope(AdminSeeder.PasswordEnvVar, "first-password")) Seed();
        var before = LoadAdmin();

        using var _ = new EnvVarScope(AdminSeeder.PasswordEnvVar, "second-password");
        Seed();

        var after = LoadAdmin();
        Assert.Equal(PasswordVerificationResult.Failed, Verify(after, "first-password"));
        Assert.NotEqual(PasswordVerificationResult.Failed, Verify(after, "second-password"));
        Assert.NotEqual(before.SecurityStamp, after.SecurityStamp);
    }

    [Fact]
    public void LaterRun_RestoresAdminFlagAndRevokesSessions()
    {
        using var _ = new EnvVarScope(AdminSeeder.PasswordEnvVar, "first-password");
        Seed();
        string stampBefore;
        using (var db = _db.CreateContext())
        {
            var admin = db.Users.Single();
            admin.IsAdmin = false;
            db.SaveChanges();
            stampBefore = admin.SecurityStamp;
        }

        Seed();

        var after = LoadAdmin();
        Assert.True(after.IsAdmin);
        Assert.NotEqual(stampBefore, after.SecurityStamp);
    }

    [Fact]
    public void ExistingAdminWithDifferentCase_IsReusedNotDuplicated()
    {
        using (var db = _db.CreateContext())
        {
            var existing = User.Create("ADMIN", isAdmin: true);
            existing.PasswordHash = _hasher.HashPassword(existing, "old-password");
            db.Users.Add(existing);
            db.SaveChanges();
        }

        using var _ = new EnvVarScope(AdminSeeder.PasswordEnvVar, "new-password");
        Seed();

        using var check = _db.CreateContext();
        var admin = check.Users.Single();
        Assert.Equal("ADMIN", admin.Username);
        Assert.NotEqual(PasswordVerificationResult.Failed, Verify(admin, "new-password"));
    }

    private void Seed()
    {
        using var db = _db.CreateContext();
        AdminSeeder.Seed(db, _hasher, NullLogger.Instance);
    }

    private User LoadAdmin()
    {
        using var db = _db.CreateContext();
        return db.Users.AsNoTracking().Single(u => u.NormalizedUsername == User.NormalizeUsername(AdminSeeder.AdminUsername));
    }

    private PasswordVerificationResult Verify(User user, string password) =>
        _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
}
