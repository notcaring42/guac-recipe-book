using recipe_book_api.Dtos;

namespace recipe_book_api.Tests;

public class LoginRequestTests
{
    [Fact]
    public void Valid_HasNoErrors()
    {
        Assert.Empty(new LoginRequest("admin", "pw").Validate());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingUsername_IsRejected(string? username)
    {
        var errors = new LoginRequest(username!, "pw").Validate();

        Assert.Equal([nameof(LoginRequest.Username)], errors.Keys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MissingPassword_IsRejected(string? password)
    {
        var errors = new LoginRequest("admin", password!).Validate();

        Assert.Equal([nameof(LoginRequest.Password)], errors.Keys);
    }

    [Fact]
    public void WhitespacePassword_IsAllowed()
    {
        // Login must accept whatever the user's password is; only creation restricts it.
        Assert.Empty(new LoginRequest("admin", "   ").Validate());
    }
}

public class CreateUserRequestTests
{
    private const string GoodPassword = "correct-horse";

    [Fact]
    public void Valid_HasNoErrors()
    {
        Assert.Empty(new CreateUserRequest("Émile", GoodPassword).Validate());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void MissingUsername_IsRejected(string? username)
    {
        var errors = new CreateUserRequest(username!, GoodPassword).Validate();

        Assert.Equal([nameof(CreateUserRequest.Username)], errors.Keys);
    }

    [Fact]
    public void UsernameAtMaxLength_IsAllowed()
    {
        var username = new string('a', CreateUserRequest.UsernameMaxLength);

        Assert.Empty(new CreateUserRequest(username, GoodPassword).Validate());
    }

    [Fact]
    public void UsernameOverMaxLength_IsRejected()
    {
        var username = new string('a', CreateUserRequest.UsernameMaxLength + 1);

        Assert.Contains(nameof(CreateUserRequest.Username), new CreateUserRequest(username, GoodPassword).Validate().Keys);
    }

    [Fact]
    public void UsernameLength_IsMeasuredAfterTrimming()
    {
        var username = "  " + new string('a', CreateUserRequest.UsernameMaxLength) + "  ";

        Assert.Empty(new CreateUserRequest(username, GoodPassword).Validate());
    }

    [Theory]
    [InlineData("bob\u200Bby")] // zero-width space
    [InlineData("bob\u202Eby")] // right-to-left override
    [InlineData("bob\nby")]
    [InlineData("bob\u2028by")] // line separator
    [InlineData("bob\u0000by")]
    public void InvisibleOrControlCharacters_AreRejected(string username)
    {
        Assert.Contains(nameof(CreateUserRequest.Username), new CreateUserRequest(username, GoodPassword).Validate().Keys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("          ")]
    public void MissingOrWhitespacePassword_IsRejected(string? password)
    {
        var errors = new CreateUserRequest("bob", password!).Validate();

        Assert.Equal([nameof(CreateUserRequest.Password)], errors.Keys);
    }

    [Fact]
    public void ShortPassword_IsRejected()
    {
        var password = new string('x', CreateUserRequest.PasswordMinLength - 1);

        Assert.Equal([nameof(CreateUserRequest.Password)], new CreateUserRequest("bob", password).Validate().Keys);
    }

    [Fact]
    public void PasswordAtMinLength_IsAllowed()
    {
        var password = new string('x', CreateUserRequest.PasswordMinLength);

        Assert.Empty(new CreateUserRequest("bob", password).Validate());
    }

    [Fact]
    public void BothFieldsInvalid_ReportsBoth()
    {
        var errors = new CreateUserRequest("", "").Validate();

        Assert.Equal(2, errors.Count);
    }
}
