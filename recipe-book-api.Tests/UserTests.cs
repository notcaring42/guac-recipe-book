using recipe_book_api.Models;

namespace recipe_book_api.Tests;

public class UserTests
{
    [Theory]
    [InlineData("admin", "ADMIN")]
    [InlineData("  Admin  ", "ADMIN")]
    [InlineData("ａｄｍｉｎ", "ADMIN")] // full-width compatibility forms fold to ASCII
    [InlineData("émile", "ÉMILE")] // non-ASCII letters fold case too
    [InlineData("Émile", "ÉMILE")]
    public void NormalizeUsername_FoldsCaseWidthAndWhitespace(string input, string expected)
    {
        Assert.Equal(expected, User.NormalizeUsername(input));
    }

    [Fact]
    public void NormalizeUsername_ComposedAndDecomposedAccentsAreEqual()
    {
        var composed = "\u00E9mile"; // é as one code point
        var decomposed = "e\u0301mile"; // e + combining acute accent

        Assert.Equal(User.NormalizeUsername(composed), User.NormalizeUsername(decomposed));
    }

    [Fact]
    public void CleanUsername_TrimsAndComposesButKeepsCase()
    {
        Assert.Equal("\u00C9mile", User.CleanUsername("  E\u0301mile "));
    }

    [Fact]
    public void Create_SetsBothUsernameColumnsAndAStamp()
    {
        var user = User.Create("  Bob ", isAdmin: true);

        Assert.Equal("Bob", user.Username);
        Assert.Equal("BOB", user.NormalizedUsername);
        Assert.True(user.IsAdmin);
        Assert.False(string.IsNullOrEmpty(user.SecurityStamp));
    }

    [Fact]
    public void NewSecurityStamp_IsDifferentEachTime()
    {
        Assert.NotEqual(User.NewSecurityStamp(), User.NewSecurityStamp());
    }
}
