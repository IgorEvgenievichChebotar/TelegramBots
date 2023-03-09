namespace TelegramBots.PhotosTelegramBot;

public class Folder
{
    public string? Name { get; set; }

    public string? Type { get; set; }
    public string? Path { get; set; }
    

    public Folder()
    {
    }

    public Folder(string? path)
    {
        Path = path;
        if (path != null)
        {
            Name = path[..path.LastIndexOf('/')].Split('/').Last();
        }

        Type = "dir";
    }

    public override string? ToString()
    {
        return Name;
    }
}