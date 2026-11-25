using Microsoft.AspNetCore.Identity;
using recipe_book_api.Models;

namespace recipe_book_api.Data;

public static class AdminSeeder
{
    public const string AdminUsername = "admin";
    public const string PasswordEnvVar = "GUAC_ADMIN_PWD";

    /// <summary>
    /// Ensures the "admin" user exists and that its password matches GUAC_ADMIN_PWD.
    /// The env var is required only when the admin user doesn't exist yet; if it's set
    /// later with a new value, the stored password is updated to match and existing
    /// admin sessions are invalidated.
    /// </summary>
    public static void Seed(RecipeBookContext db, IPasswordHasher<User> hasher, ILogger logger)
    {
        var password = Environment.GetEnvironmentVariable(PasswordEnvVar);
        var normalized = User.NormalizeUsername(AdminUsername);
        var admin = db.Users.SingleOrDefault(u => u.NormalizedUsername == normalized);

        if (admin is null)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException(
                    $"Environment variable '{PasswordEnvVar}' must be set to seed the '{AdminUsername}' user.");
            }

            admin = User.Create(AdminUsername, isAdmin: true);
            admin.PasswordHash = hasher.HashPassword(admin, password);
            db.Users.Add(admin);
            db.SaveChanges();
            logger.LogInformation("Seeded '{Username}' user.", AdminUsername);
            return;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("'{EnvVar}' is not set; keeping existing '{Username}' password.", PasswordEnvVar, AdminUsername);
            return;
        }

        var changed = false;
        if (hasher.VerifyHashedPassword(admin, admin.PasswordHash, password) == PasswordVerificationResult.Failed)
        {
            admin.PasswordHash = hasher.HashPassword(admin, password);
            changed = true;
            logger.LogInformation("Updated '{Username}' password from '{EnvVar}'.", AdminUsername, PasswordEnvVar);
        }

        if (!admin.IsAdmin)
        {
            admin.IsAdmin = true;
            changed = true;
        }

        if (changed)
        {
            // Log out every existing session for this user.
            admin.SecurityStamp = User.NewSecurityStamp();
            db.SaveChanges();
        }
    }
}
