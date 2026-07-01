namespace SlotFramework.Models;

public class Symbol
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsWild { get; set; }
    public bool IsScatter { get; set; }

    public Symbol() { }

    public Symbol(int id, string name, bool isWild = false, bool isScatter = false)
    {
        Id = id;
        Name = name;
        IsWild = isWild;
        IsScatter = isScatter;
    }
}
