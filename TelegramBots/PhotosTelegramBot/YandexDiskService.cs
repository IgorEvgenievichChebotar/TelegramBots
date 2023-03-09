using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Flurl.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TelegramBots.PhotosTelegramBot;

public interface IYandexDiskService
{
    Image GetRandomImage();
    Image? GetRandomImage(DateTime date);
    Image? FindImageByName(string imgName);
    void OpenImageInBrowser(string name);
    Task<List<Folder>> GetFoldersAsync();
    Task LoadImagesAsync();
    Task<byte[]> LoadThumbnailImageAsync(Image img);
    Task<byte[]> LoadOriginalImageAsync(Image img);
    Task<Dictionary<string, byte[]>> GetLikesAsync(long chatId);
    Task<Dictionary<string, byte[]>> LoadLikesAsync(long chatId);
    Task<string> GetPublicFolderUrlByChatIdAsync(long chatId);
    Task AddToLikes(long chatId, Image img);
    void DeleteImage(string imgName);
    void DeleteAllImagesFromCache();
    DateTime FindClosestDateBefore(DateTime date);
    DateTime FindClosestDateAfter(DateTime date);
}

public class YandexDiskService : IYandexDiskService
{
    private readonly List<Image> Images = new();
    private readonly Dictionary<long, Dictionary<string, byte[]>> LikesCache = new();
    private readonly HttpClient _httpClient = new();

    public YandexDiskService()
    {
        _httpClient.DefaultRequestHeaders.Add(
            "Authorization",
            $"OAuth {Secrets.OAuthYandexDisk}"
        );
        FlurlHttp.Configure(settings =>
            settings.BeforeCall = call =>
                call.Request.WithHeader("Authorization", $"OAuth {Secrets.OAuthYandexDisk}")
        );
    }

    public async Task<byte[]> LoadThumbnailImageAsync(Image img)
    {
        var sw = Stopwatch.StartNew();
        var response = await _httpClient.GetAsync(img.Preview, HttpCompletionOption.ResponseHeadersRead);
        var content = await response.Content.ReadAsByteArrayAsync();
        Console.WriteLine(response.IsSuccessStatusCode
            ? $"{DateTime.Now} | Photo {img.Name} downloaded successfully in {sw.ElapsedMilliseconds}ms"
            : $"{DateTime.Now} | Error downloading {img.Name}: {response.StatusCode} {response.ReasonPhrase}");

        return content;
    }

    public async Task<byte[]> LoadOriginalImageAsync(Image img)
    {
        var sw = Stopwatch.StartNew();
        var response = await _httpClient.GetAsync(img.File, HttpCompletionOption.ResponseHeadersRead);
        var content = await response.Content.ReadAsByteArrayAsync();
        Console.WriteLine(response.IsSuccessStatusCode
            ? $"{DateTime.Now} | Photo {img.Name} downloaded successfully in {sw.ElapsedMilliseconds}ms"
            : $"{DateTime.Now} | Error downloading {img.Name}");

        return content;
    }

    public async Task<Dictionary<string, byte[]>> GetLikesAsync(long chatId) // 2 запроса
    {
        if (LikesCache.ContainsKey(chatId))
        {
            return LikesCache[chatId];
        }

        return await LoadLikesAsync(chatId);
    }

    public async Task<Dictionary<string, byte[]>> LoadLikesAsync(long chatId)
    {
        var urlFolderOnDisk = Secrets.LikedImagesByChatIdUrl(chatId, limit: 10);
        var response = await _httpClient.GetAsync(urlFolderOnDisk);
        if (response.StatusCode == HttpStatusCode.NotFound) // папки нет
        {
            await GetPublicFolderUrlByChatIdAsync(chatId); //создать папку
            response = await _httpClient.GetAsync(urlFolderOnDisk); // повторный запрос
        }

        var jsonString = await response.Content.ReadAsStringAsync();
        var images = DeserializeImagesFromJsonString(jsonString);

        LikesCache[chatId] = new Dictionary<string, byte[]>();

        await Parallel.ForEachAsync(images, async (i, _) =>
            LikesCache[chatId].Add(i.Name, (await LoadThumbnailImageAsync(i)))
        );

        return LikesCache[chatId];
    }

    private static List<Image> DeserializeImagesFromJsonString(string jsonString)
    {
        return JsonConvert.DeserializeObject<List<Image>>(jsonString[22..^2],
                new ImageExifConverter())
            .Where(i => i.Name.Contains(".jpg"))
            .Where(i => i.MimeType.Contains("image/jpeg"))
            .ToList();
    }

    public async Task<string> GetPublicFolderUrlByChatIdAsync(long chatId)
    {
        var createFolderUrl = Secrets.FolderByChatIdUrl(chatId);
        var publishFolderUrl = Secrets.PublishFolderUrl(chatId);

        string publicUrl;
        var folderResponse = await _httpClient.GetAsync(createFolderUrl);
        if (folderResponse.StatusCode == HttpStatusCode.NotFound)
        {
            var createdFolderResponse = await _httpClient.PutAsync(createFolderUrl, null); //создаем
            var publishedFolderResponse = await _httpClient.PutAsync(publishFolderUrl, null); //публикуем

            if (createdFolderResponse.IsSuccessStatusCode && publishedFolderResponse.IsSuccessStatusCode)
            {
                var response = await _httpClient.GetAsync(createFolderUrl);
                var content = await response.Content.ReadAsStringAsync();
                var json = JsonDocument.Parse(content);
                publicUrl = json.RootElement.GetProperty("public_url").GetString()!;
                Console.WriteLine($"{DateTime.Now} | Successful CreatePublicFolderByChatId: " +
                                  $"{response.StatusCode} {response.ReasonPhrase}");
            }

            Console.WriteLine($"{DateTime.Now} | Error CreatePublicFolderByChatId: " +
                              $"{createdFolderResponse.StatusCode} {publishedFolderResponse.ReasonPhrase}");
        }

        var existingFolderRequest = await folderResponse.Content.ReadAsStringAsync();
        var jsonDocument = JsonDocument.Parse(existingFolderRequest);
        publicUrl = jsonDocument.RootElement.GetProperty("public_url").GetString()!;
        Console.WriteLine($"{DateTime.Now} | Successful CreatePublicFolderByChatId: " +
                          $"{folderResponse.StatusCode} {folderResponse.ReasonPhrase}");
        return publicUrl;
    }

    public async Task AddToLikes(long chatId, Image img)
    {
        var urlCopyImageToFolderOnDisk = Secrets.CopyImageToFolderUrl(
            chatId: chatId,
            currentPath: "disk:/" + Secrets.TargetFolder + "/",
            imgName: img.Name);

        var task = _httpClient.PostAsync(urlCopyImageToFolderOnDisk, null);

        var bytes = await LoadThumbnailImageAsync(img);

        var dict = await GetLikesAsync(chatId);
        if (!dict.ContainsKey(img.Name))
        {
            dict.Add(img.Name, bytes);
        }
    }

    public void DeleteImage(string imgName)
    {
        var url = Secrets.DeleteImageUrl(imgName);
        _httpClient.DeleteAsync(url);
        var image = Images.Find(i => i.Name == imgName);
        Images.Remove(image!);
    }

    public void DeleteAllImagesFromCache()
    {
        Images.RemoveAll(i => i.ParentFolder!.Name != Secrets.TargetFolder);
        Console.WriteLine("удалены все фотки, текущее кол-во: " + Images.Count);
    }

    public DateTime FindClosestDateBefore(DateTime date)
    {
        var closestDate = DateTime.MinValue;
        var closestDiff = TimeSpan.MaxValue;

        foreach (var image in Images)
        {
            if (image.DateTime <= date && date - image.DateTime < closestDiff)
            {
                closestDiff = date - image.DateTime;
                closestDate = image.DateTime;
            }
        }

        if (closestDate == DateTime.MinValue)
        {
            closestDate = Images.Min(image => image.DateTime);
        }

        return closestDate;
    }

    public DateTime FindClosestDateAfter(DateTime date)
    {
        var closestDate = DateTime.MaxValue;
        var closestDiff = TimeSpan.MaxValue;

        foreach (var image in Images)
        {
            if (image.DateTime >= date && image.DateTime - date < closestDiff)
            {
                closestDiff = image.DateTime - date;
                closestDate = image.DateTime;
            }
        }

        if (closestDate == DateTime.MaxValue)
        {
            closestDate = Images.Max(image => image.DateTime);
        }

        return closestDate;
    }


    public Image GetRandomImage()
    {
        var img = Images
            .Where(i => Secrets.TargetFolder == i.ParentFolder!.Name)
            .OrderBy(_ => Guid.NewGuid())
            .First();
        return img;
    }

    public Image? GetRandomImage(DateTime date)
    {
        var randomImage = Images
            .Where(i => i.DateTime.Date == date)
            .MinBy(_ => Guid.NewGuid());
        return randomImage;
    }

    public Image? FindImageByName(string imgName)
    {
        return Images
            .Where(i => Secrets.TargetFolder == i.ParentFolder!.Name)
            .FirstOrDefault(i => i.Name.ToLower().Contains(imgName.ToLower()));
    }

    public async Task<List<Folder>> GetFoldersAsync()
    {
        var url = Secrets.FoldersUrl();
        var response = await _httpClient.GetAsync(url);
        var jsonString = await response.Content.ReadAsStringAsync();
        var folders = DeserializeFoldersFromJsonString(jsonString);
        return folders;
    }

    private static List<Folder> DeserializeFoldersFromJsonString(string jsonString)
    {
        return JsonConvert.DeserializeObject<List<Folder>>(
                jsonString[22..^2],
                new FolderJsonConverter())
            .Where(pf => pf.Type == "dir")
            .ToList();
    }

    public async Task LoadImagesAsync()
    {
        if (Images.Any(i => Secrets.TargetFolder == i.ParentFolder!.Name))
        {
            Console.WriteLine(
                $"{DateTime.Now} | Фотки в папке {Secrets.TargetFolder} уже есть; " +
                $"Всего: {Images.Count}");
            return;
        }

        await LoadCacheImages();
        var task = LoadRemainingImages();

        async Task LoadCacheImages()
        {
            var response = await _httpClient.GetAsync(Secrets.ImagesUrl(limit: Secrets.CacheCount));
            var cacheCount = await LoadAndAddImages(response);
            Console.WriteLine(
                $"{DateTime.Now} | {cacheCount} фоток кэша загружено из папки {Secrets.TargetFolder}. " +
                $"Всего: {Images.Count}");
        }

        async Task LoadRemainingImages()
        {
            var total = await await Secrets.FolderUrl(Secrets.TargetFolder)
                .GetJsonAsync<JObject>()
                .ContinueWith(async t => (int)(await t)["_embedded"]["total"]);

            var tasks = new List<Task<HttpResponseMessage>>();

            for (var offset = Secrets.CacheCount; offset < total; offset += Secrets.CacheCount)
            {
                tasks.Add(_httpClient.GetAsync(Secrets.ImagesUrl(
                    limit: Secrets.CacheCount,
                    offset: offset)));
            }

            var before = Images.Count;
            var timer = Stopwatch.StartNew();
            await Parallel.ForEachAsync(tasks, async (t, _) =>
                await LoadAndAddImages(await t)
            );
            Console.WriteLine(
                $"{DateTime.Now} | {Images.Count - before} оставшихся фоток загружено " +
                $"из папки {Secrets.TargetFolder} за {timer.ElapsedMilliseconds}мс. " +
                $"Всего: {Images.Count}");
        }

        async Task<int> LoadAndAddImages(HttpResponseMessage response)
        {
            var images = new List<Image>();
            var jsonString = await response.Content.ReadAsStringAsync();
            if (!jsonString.Contains("image/jpeg")) return images.Count;
            images = DeserializeImagesFromJsonString(jsonString);
            Images.AddRange(images);
            Images.RemoveAll(i => i is null);

            return images.Count;
        }
    }


    public void OpenImageInBrowser(string name)
    {
        var img = FindImageByName(name);
        if (img == null) return;
        var url = Secrets.OpenInBrowserUrl + img.Name;
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    public List<Image> GetImagesByDate(DateTime date)
    {
        var images = Images.Where(i => i.DateTime.Date == date).ToList();
        return images;
    }
}