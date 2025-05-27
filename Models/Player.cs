namespace SvinTusOnline.Models;
public class Player
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Avatar { get; set; } = "/avatars/default.png";
    public List<Card> Hand { get; set; } = new();
}