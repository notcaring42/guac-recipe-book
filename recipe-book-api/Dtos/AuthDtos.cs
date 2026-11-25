using System.Globalization;
using System.Text;
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
        else if (!IsValidUtf16(Username))
        {
            // Unpaired surrogates aren't real text, and string.Normalize throws on them.
            errors[nameof(Username)] = ["Username contains invalid characters."];
        }
        else
        {
            var cleaned = User.CleanUsername(Username);
            if (cleaned.Length > UsernameMaxLength)
            {
                errors[nameof(Username)] = [$"Username must be {UsernameMaxLength} characters or fewer."];
            }
            else if (cleaned.EnumerateRunes().Any(IsDisallowedUsernameRune))
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
    // overrides, line breaks, tag characters, variation selectors) would allow look-alike
    // usernames, so they're rejected. Checked per code point (Rune), not per UTF-16 char,
    // so characters outside the BMP are classified correctly instead of as surrogate halves.
    private static bool IsDisallowedUsernameRune(Rune r) =>
        Rune.GetUnicodeCategory(r) is UnicodeCategory.Control
            or UnicodeCategory.Format
            or UnicodeCategory.LineSeparator
            or UnicodeCategory.ParagraphSeparator
        || IsInvisibleNonFormat(r.Value);

    // Default-ignorable code points that Unicode doesn't class as Format: variation selectors,
    // the combining grapheme joiner, and Hangul/halfwidth fillers that render as blank space.
    private static bool IsInvisibleNonFormat(int cp) =>
        cp is (>= 0xFE00 and <= 0xFE0F)       // variation selectors
            or (>= 0xE0100 and <= 0xE01EF)    // variation selectors supplement
            or (>= 0x180B and <= 0x180F)      // Mongolian free variation selectors
            or 0x034F                         // combining grapheme joiner
            or 0x115F or 0x1160 or 0x3164 or 0xFFA0; // Hangul fillers

    private static bool IsValidUtf16(string s)
    {
        var span = s.AsSpan();
        while (!span.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(span, out _, out var consumed) != System.Buffers.OperationStatus.Done)
            {
                return false;
            }
            span = span[consumed..];
        }
        return true;
    }
}

public record UserResponse(int Id, string Username, bool IsAdmin);
