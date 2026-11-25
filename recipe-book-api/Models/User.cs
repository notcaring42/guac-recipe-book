using System.Text;

namespace recipe_book_api.Models;

public class User
{
    public int Id { get; set; }

    /// <summary>The username as entered (trimmed, NFC-normalized), used for display.</summary>
    public required string Username { get; set; }

    /// <summary>
    /// Case- and compatibility-folded form of <see cref="Username"/>, used for lookups and the
    /// unique index. Set by <see cref="Create"/>.
    /// </summary>
    public required string NormalizedUsername { get; set; }

    public required string PasswordHash { get; set; }
    public bool IsAdmin { get; set; }

    /// <summary>
    /// Random value that changes whenever the password or admin flag changes. Login cookies
    /// carry the stamp they were issued with and are rejected once it no longer matches.
    /// </summary>
    public string SecurityStamp { get; set; } = NewSecurityStamp();

    public static string NewSecurityStamp() => Guid.NewGuid().ToString("N");

    /// <summary>Creates a user with both username columns filled in. Set the password hash next.</summary>
    public static User Create(string username, bool isAdmin = false) => new()
    {
        Username = CleanUsername(username),
        NormalizedUsername = NormalizeUsername(username),
        PasswordHash = "",
        IsAdmin = isAdmin,
    };

    public static string CleanUsername(string username) =>
        username.Trim().Normalize(NormalizationForm.FormC);

    /// <summary>
    /// NFKC folds compatibility forms (e.g. full-width "ａｄｍｉｎ" → "admin") and composes
    /// accents so "é" typed either way compares equal; ToUpperInvariant then folds case for
    /// all scripts, not just ASCII (so "Émile" and "émile" collide).
    /// </summary>
    public static string NormalizeUsername(string username) =>
        username.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant();
}
