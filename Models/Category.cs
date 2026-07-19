namespace MedLoop.NextGen.Models;

public class Category
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string? Color { get; set; }

    public ICollection<Product> Products { get; set; } = new List<Product>();
}
