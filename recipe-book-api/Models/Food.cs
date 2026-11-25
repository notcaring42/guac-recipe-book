namespace recipe_book_api.Models;

public class Food
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int Calories { get; set; }
}
