using System.Globalization;
using recipe_book_api.Models;

namespace recipe_book_api.Dtos;

public record LoginRequest(string Username, string Password)
{
    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();

        // Values can be null at runtime: JSON deserialization doesn't enforce nullability.
        if (string.IsNullOrWhiteSpace(Username))
        {
            errors[nameof(Username)] = ["Username is required."];
        }

        if (string.IsNullOrEmpty(Password))
        {
            errors[nameof(Password)] = ["Password is required."];
        }

        return errors;
    }
}

public record CreateUserRequest(string Username, string Password)
{
    public const int UsernameMaxLength = 100;
    public const int PasswordMinLength = 8;

    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();

        // Values can be null at runtime: JSON deserialization doesn't enforce nullability.
        if (string.IsNullOrWhiteSpace(Username))
        {
            errors[nameof(Username)] = ["Username is required."];
        }
        else
        {
            var cleaned = User.CleanUsername(Username);
            if (cleaned.Length > UsernameMaxLength)
            {
                errors[nameof(Username)] = [$"Username must be {UsernameMaxLength} characters or fewer."];
            }
            else if (cleaned.Any(IsDisallowedUsernameChar))
            {
                errors[nameof(Username)] = ["Username cannot contain control or invisible formatting characters."];
            }
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            errors[nameof(Password)] = ["Password is required and cannot be only whitespace."];
        }
        else if (Password.Length < PasswordMinLength)
        {
            errors[nameof(Password)] = [$"Password must be at least {PasswordMinLength} characters."];
        }

        return errors;
    }

    // Letters from any script are fine; invisible characters (zero-width spaces, bidi
    // overrides, line breaks) would allow look-alike usernames, so they're rejected.
    private static bool IsDisallowedUsernameChar(char c) =>
        CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.Control
            or UnicodeCategory.Format
            or UnicodeCategory.LineSeparator
            or UnicodeCategory.ParagraphSeparator;
}

public record UserResponse(int Id, string Username, bool IsAdmin);
