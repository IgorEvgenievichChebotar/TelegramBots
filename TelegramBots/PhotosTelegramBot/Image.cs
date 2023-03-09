namespace TelegramBots.PhotosTelegramBot;

public class Image
{
    public string Name { get; set; }
    public string File { get; set; }
    public string MimeType { get; set; }
    public long? Size { get; set; }
    public DateTime DateTime { get; set; }
    public Folder? ParentFolder { get; set; }
    public string Path { get; set; }
    public string Preview { get; set; }

    public override string ToString()
    {
        return Name!;
    }
}