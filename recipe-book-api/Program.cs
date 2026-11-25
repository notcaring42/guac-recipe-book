using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using recipe_book_api.Auth;
using recipe_book_api.Data;
using recipe_book_api.Dtos;
using recipe_book_api.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

// Resolve a relative SQLite path against the content root so the app and the
// EF tooling always use the same database file regardless of working directory.
var sqliteBuilder = new SqliteConnectionStringBuilder(connectionString);
if (!string.IsNullOrEmpty(sqliteBuilder.DataSource)
    && sqliteBuilder.DataSource != ":memory:"
    && !Path.IsPathRooted(sqliteBuilder.DataSource))
{
    sqliteBuilder.DataSource = Path.Combine(builder.Environment.ContentRootPath, sqliteBuilder.DataSource);
}

builder.Services.AddDbContext<RecipeBookContext>(options =>
    options.UseSqlite(sqliteBuilder.ToString()));

builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "recipe-book.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        // Never send the cookie over plain HTTP outside Development. Development keeps
        // SameAsRequest so the http launch profile (http://localhost:5007) still works.
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        // Re-check the user on every request so password/role changes and deletions
        // take effect immediately, and cap the total session length.
        options.Events.OnValidatePrincipal = AuthCookie.ValidateAsync;
        // This is an API: return status codes instead of redirecting to a login page.
        options.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorizationBuilder()
    // Every endpoint requires a logged-in user unless it opts out with AllowAnonymous.
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy("Admin", policy => policy.RequireRole(AuthCookie.AdminRole));

// Throttle login attempts per client IP to slow down password guessing. Limits are
// configurable (RateLimiting:Login:PermitLimit / WindowSeconds) so tests can adjust them.
var loginPermitLimit = builder.Configuration.GetValue("RateLimiting:Login:PermitLimit", 10);
var loginWindow = TimeSpan.FromSeconds(builder.Configuration.GetValue("RateLimiting:Login:WindowSeconds", 60));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(LoginRateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = loginPermitLimit,
            Window = loginWindow,
            QueueLimit = 0,
        }));
});

var app = builder.Build();

// Apply any pending migrations so the schema exists on a fresh clone,
// then make sure the admin user exists.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<RecipeBookContext>();
    db.Database.Migrate();
    AdminSeeder.Seed(
        db,
        scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>(),
        app.Logger);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapPost("/auth/login", async (
    LoginRequest request,
    RecipeBookContext db,
    IPasswordHasher<User> hasher,
    HttpContext http) =>
{
    var errors = request.Validate();
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var normalized = User.NormalizeUsername(request.Username);
    var user = await db.Users.SingleOrDefaultAsync(u => u.NormalizedUsername == normalized);
    if (user is null)
    {
        // Hash anyway so unknown usernames take about as long as wrong passwords.
        hasher.HashPassword(User.Create(request.Username), request.Password);
        return Results.Unauthorized();
    }

    var result = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
    if (result == PasswordVerificationResult.Failed)
    {
        return Results.Unauthorized();
    }

    if (result == PasswordVerificationResult.SuccessRehashNeeded)
    {
        user.PasswordHash = hasher.HashPassword(user, request.Password);
        await db.SaveChangesAsync();
    }

    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, AuthCookie.CreatePrincipal(user));

    return Results.Ok(new UserResponse(user.Id, user.Username, user.IsAdmin));
}).AllowAnonymous().RequireRateLimiting(LoginRateLimitPolicy);

app.MapPost("/auth/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.NoContent();
});

app.MapGet("/auth/me", (ClaimsPrincipal user) => Results.Ok(new UserResponse(
    int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!),
    user.Identity!.Name!,
    user.IsInRole(AuthCookie.AdminRole))));

app.MapPost("/users", async (
    CreateUserRequest request,
    RecipeBookContext db,
    IPasswordHasher<User> hasher) =>
{
    var errors = request.Validate();
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var user = User.Create(request.Username);
    var conflict = Results.Conflict(new { error = $"Username '{user.Username}' is already taken." });
    if (await db.Users.AnyAsync(u => u.NormalizedUsername == user.NormalizedUsername))
    {
        return conflict;
    }

    user.PasswordHash = hasher.HashPassword(user, request.Password);
    db.Users.Add(user);
    try
    {
        await db.SaveChangesAsync();
    }
    catch (DbUpdateException ex) when (RecipeBookContext.IsUniqueViolation(ex))
    {
        // Another request created the same username between the check above and this save.
        return conflict;
    }

    return Results.Created($"/users/{user.Id}", new UserResponse(user.Id, user.Username, user.IsAdmin));
}).RequireAuthorization("Admin");

app.MapGet("/users/{id:int}", async (int id, RecipeBookContext db) =>
{
    var user = await db.Users.FindAsync(id);
    return user is null
        ? Results.NotFound()
        : Results.Ok(new UserResponse(user.Id, user.Username, user.IsAdmin));
}).RequireAuthorization("Admin");

app.MapPost("/foods", async (CreateFoodRequest request, RecipeBookContext db) =>
{
    var errors = request.Validate();
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var food = new Food
    {
        Name = request.Name.Trim(),
        Calories = request.Calories
    };

    db.Foods.Add(food);
    await db.SaveChangesAsync();

    var response = new FoodResponse(food.Id, food.Name, food.Calories);
    return Results.Created($"/foods/{food.Id}", response);
});

app.MapGet("/foods/{id:int}", async (int id, RecipeBookContext db) =>
{
    var food = await db.Foods.FindAsync(id);
    return food is null
        ? Results.NotFound()
        : Results.Ok(new FoodResponse(food.Id, food.Name, food.Calories));
});

app.MapDelete("/foods/{id:int}", async (int id, RecipeBookContext db) =>
{
    var food = await db.Foods.FindAsync(id);
    if (food is null)
    {
        return Results.NotFound();
    }

    db.Foods.Remove(food);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

app.Run();

public partial class Program
{
    private const string LoginRateLimitPolicy = "login";
}
