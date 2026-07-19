namespace MedLoop.NextGen.Models;

public class Notification
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string RecipientUserId { get; set; } = string.Empty;
    public ApplicationUser? RecipientUser { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsRead { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
