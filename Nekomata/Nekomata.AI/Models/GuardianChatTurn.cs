namespace Nekomata.AI.Models;

public class GuardianChatTurn
{
    public string Role { get; set; } = "user";

    public string Content { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public bool IsUser =>
        Role.Equals("user", StringComparison.OrdinalIgnoreCase);

    public string DisplayRole => IsUser ? "YOU" : "GUARDIAN";
}