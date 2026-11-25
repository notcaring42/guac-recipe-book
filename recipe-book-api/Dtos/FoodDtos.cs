namespace recipe_book_api.Dtos;

public record CreateFoodRequest(string Name, int Calories)
{
    public const int NameMaxLength = 200;

    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();

        // Name can be null at runtime: JSON deserialization doesn't enforce nullability.
        if (string.IsNullOrWhiteSpace(Name))
        {
            errors[nameof(Name)] = ["Name is required."];
        }
        else if (Name.Trim().Length > NameMaxLength)
        {
            errors[nameof(Name)] = [$"Name must be {NameMaxLength} characters or fewer."];
        }

        if (Calories < 0)
        {
            errors[nameof(Calories)] = ["Calories cannot be negative."];
        }

        return errors;
    }
}

public record FoodResponse(int Id, string Name, int Calories);
