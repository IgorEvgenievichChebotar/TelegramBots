using System.Diagnostics;

namespace TelegramBots.PhotosTelegramBot;

public class Urls
{
    public static string TargetFolder { get; set; } = "PublicPhotos";
    public const int CacheCount = 20;
    private const string BaseApiUrl = "https://cloud-api.yandex.net/v1/disk/resources";

    public static string OpenInBrowserUrl() =>
        $"https://disk.yandex.ru/client/disk/{TargetFolder}?" +
        $"idApp=client&dialog=slider&idDialog=%2Fdisk%2F{TargetFolder}%2F";

    public static string GetImages(int offset = 0, int limit = 10000)
    {
        return $"{BaseApiUrl}?" +
               $"path=disk:/{TargetFolder}/&" +
               "media_type=image&" +
               "fields=" +
               "_embedded.items.name, " +
               "_embedded.items.file, " +
               "_embedded.items.mime_type, " +
               "_embedded.items.size, " +
               "_embedded.items.preview, " +
               "_embedded.items.path, " +
               "_embedded.items.preview, " +
               "_embedded.items.type, " +
               "_embedded.items.exif.date_time&" +
               $"limit={limit}&" +
               $"offset={offset}&" +
               "preview_size=XXXL";
    }

    public static string GetFolderByChatId(long chatId) =>
        $"{BaseApiUrl}?" +
        $"path=disk:/Общий доступ/{chatId}/";

    public static string DeleteImage(string imgName) =>
        $"{BaseApiUrl}?" +
        $"path=disk:/{TargetFolder}/{imgName}/&" +
        "permanently=false&";

    public static string PublishFolder(long chatId) =>
        $"{BaseApiUrl}/publish?" +
        $"path=disk:/Общий доступ/{chatId}/";

    public static string GetLikedImagesByChatId(long chatId, int limit = 10000) =>
        $"{BaseApiUrl}?" +
        $"path=disk:/Общий доступ/{chatId}/&" +
        "media_type=image&" +
        "fields=" +
        "_embedded.items.name, " +
        "_embedded.items.file, " +
        "_embedded.items.mime_type, " +
        "_embedded.items.size, " +
        "_embedded.items.preview, " +
        "_embedded.items.path, " +
        "_embedded.items.preview, " +
        "_embedded.items.type, " +
        "_embedded.items.exif.date_time&" +
        "sort=-modified&" +
        $"limit={limit}&" +
        "preview_size=XXXL";

    public static string CopyImageToFolder(string imgName, string currentPath, long chatId)
    {
        Debug.Assert(currentPath.StartsWith("disk:/") && currentPath.EndsWith("/"));
        return $"{BaseApiUrl}/copy?" +
               $"from={currentPath}{imgName}&" +
               $"path=disk:/Общий доступ/{chatId}/{imgName}&" +
               "overwrite=true";
    }

    public static string GetFolders() =>
        $"{BaseApiUrl}?" +
        "path=disk:/&" +
        "fields=" +
        "_embedded.items.name, " +
        "_embedded.items.type&" +
        "limit=100";

    public static string GetTargetFolder() =>
        $"{BaseApiUrl}?path=disk:/{TargetFolder}&limit=0";
}