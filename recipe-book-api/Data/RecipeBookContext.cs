using Microsoft.EntityFrameworkCore;
using recipe_book_api.Models;

namespace recipe_book_api.Data;

public class RecipeBookContext : DbContext
{
    public RecipeBookContext(DbContextOptions<RecipeBookContext> options)
        : base(options)
    {
    }

    public DbSet<Food> Foods => Set<Food>();
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(user =>
        {
            user.Property(u => u.Username)
                .HasMaxLength(Dtos.CreateUserRequest.UsernameMaxLength);
            // Lookups and uniqueness go through the normalized column, which is
            // case-insensitive for all scripts (SQLite's NOCASE only folds A-Z).
            user.HasIndex(u => u.NormalizedUsername).IsUnique();
        });
    }

    /// <summary>True when the save failed because a UNIQUE constraint was violated.</summary>
    public static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Microsoft.Data.Sqlite.SqliteException { SqliteExtendedErrorCode: 2067 }; // SQLITE_CONSTRAINT_UNIQUE
}
