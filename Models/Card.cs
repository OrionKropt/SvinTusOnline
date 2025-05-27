namespace SvinTusOnline.Models;

public class Card
{
    public string Color { get; set; } = "";
    public int Number { get; set; }
    public string Id => $"{Color}-{Number}";
    public string Image => $"/cards/{Id}.png";
}

