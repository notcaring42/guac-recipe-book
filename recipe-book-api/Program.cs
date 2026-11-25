using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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

var app = builder.Build();

// Apply any pending migrations so the schema exists on a fresh clone.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<RecipeBookContext>();
    db.Database.Migrate();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

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
