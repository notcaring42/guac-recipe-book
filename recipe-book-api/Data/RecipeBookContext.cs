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
}
