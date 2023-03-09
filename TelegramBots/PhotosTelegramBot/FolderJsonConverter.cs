using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TelegramBots.PhotosTelegramBot;

public class FolderJsonConverter : JsonConverter<Folder>
{
    public override Folder ReadJson(JsonReader reader, Type objectType, Folder existingValue, bool hasExistingValue,
        JsonSerializer serializer)
    {
        var jo = JObject.Load(reader);
        var folder = new Folder();
        try
        {
            folder = new Folder
            {
                Name = (string)jo["name"],
                Path = (string)jo["path"],
                Type = (string)jo["type"]
            };
        }
        catch (Exception)
        {
            Console.WriteLine($"Ошибка преобразования папки {(string)jo["name"]}");
        }

        return folder;
    }

    public override void WriteJson(JsonWriter writer, Folder value, JsonSerializer serializer)
    {

    }
}