using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using recipe_book_api.Data;

// Tests set the process-wide GUAC_ADMIN_PWD environment variable, so they must not run concurrently.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace recipe_book_api.Tests;

/// <summary>An in-memory SQLite database with the real migrations applied, alive until disposed.</summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection;

    public TestDb()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        using var db = CreateContext();
        db.Database.Migrate();
    }

    public DbContextOptions<RecipeBookContext> Options =>
        new DbContextOptionsBuilder<RecipeBookContext>().UseSqlite(_connection).Options;

    /// <summary>A fresh context, so reads don't come from another context's change tracker.</summary>
    public RecipeBookContext CreateContext() => new(Options);

    public void Dispose() => _connection.Dispose();
}

/// <summary>Sets an environment variable for the lifetime of the object, then restores it.</summary>
public sealed class EnvVarScope : IDisposable
{
    private readonly string _name;
    private readonly string? _original;

    public EnvVarScope(string name, string? value)
    {
        _name = name;
        _original = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    public void Dispose() => Environment.SetEnvironmentVariable(_name, _original);
}
