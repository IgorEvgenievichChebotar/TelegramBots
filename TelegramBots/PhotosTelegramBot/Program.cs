using System.Globalization;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace TelegramBots.PhotosTelegramBot;

public class Program
{
    private readonly TelegramBotClient _bot;
    private readonly IYandexDiskService _diskService;
    private readonly ReplyKeyboardMarkup defaultReplyKeyboardMarkup;
    private readonly CancellationTokenSource _cts;

    public Program()
    {
        _cts = new CancellationTokenSource();
        _bot = new TelegramBotClient($"{Secrets.TelegramBotToken}");
        _diskService = new YandexDiskService();
        defaultReplyKeyboardMarkup = new ReplyKeyboardMarkup(new[]
        {
            new KeyboardButton("Ещё"),
            new KeyboardButton("Сменить папку"),
            new KeyboardButton("Избранные")
        }) { ResizeKeyboard = true };
    }

    public void Run(string[] args)
    {
        _diskService.LoadImagesAsync().Wait(_cts.Token);

        _bot.StartReceiving(
            updateHandler: UpdateHandler(),
            pollingErrorHandler: PollingErrorHandler(),
            cancellationToken: _cts.Token
        );

        Console.ReadLine();
        _cts.Cancel();
    }

    private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update,
        CancellationToken token)
    {
        var settings = new Settings
        (
            bot: bot,
            cancellationToken: token,
            update: update
        );

        switch (update.Type)
        {
            case UpdateType.Message:

                if (update.Message is not { Text: { } } msg) return;

                if (msg.Chat.Username != $"{Secrets.MyUsername}")
                {
                    Urls.TargetFolder = "PublicPhotos";
                    _diskService.DeleteAllImagesFromCache();
                    await _diskService.LoadImagesAsync();
                }

                var msgText = msg.Text;
                var chatId = msg.Chat.Id;

                settings.ChatId = chatId;
                settings.Cmd = msgText.Split(" ")[0];

                if (msgText.Split(" ").Length > 1)
                {
                    settings.Query = msgText[(msgText.IndexOf(" ", StringComparison.Ordinal) + 1)..];
                }

                switch (settings.Cmd.ToLower())
                {
                    case "/start":
                        await StartAsync(settings);
                        await HelpAsync(settings);
                        var task = _diskService.GetLikesAsync(settings.ChatId);
                        return;
                    case "/help":
                        await HelpAsync(settings);
                        return;
                    case "/find":
                        await FindAsync(settings);
                        return;
                    case "сменить":
                        if (msg.Chat.Username != $"{Secrets.MyUsername}")
                        {
                            await NoAccessAsync(settings);
                            return;
                        }

                        await ChangeFolderAsync(settings);
                        return;
                    case "/like":
                        await LikeAsync(settings);
                        return;
                    case "/likes" or "избранные" or "🖤":
                        await GetLikesAsync(settings);
                        return;
                    default:
                        await FindAsync(settings);
                        return;
                }

            case UpdateType.CallbackQuery:
                if (update.CallbackQuery!.From.Username != $"{Secrets.MyUsername}")
                {
                    Urls.TargetFolder = "PublicPhotos";
                    _diskService.DeleteAllImagesFromCache();
                    await _diskService.LoadImagesAsync();
                }

                var data = update.CallbackQuery!.Data;
                settings.Query = data!.Split(" ")[1];
                settings.Cmd = data.Split(" ")[0];
                settings.ChatId = update.CallbackQuery!.From.Id;
                if (data.Split(" ").Length > 1)
                {
                    settings.Query = data[(data.IndexOf(" ", StringComparison.Ordinal) + 1)..];
                }

                var username = update.CallbackQuery.From.Username;
                switch (settings.Cmd)
                {
                    case "/find":
                        await FindAsync(settings);
                        break;
                    case "/like":
                        await LikeAsync(settings);
                        break;
                    case "/download":
                        var task = DownloadImageAsync(settings);
                        break;
                    case "/changedir":
                        if (username != $"{Secrets.MyUsername}")
                        {
                            await NoAccessAsync(settings);
                            return;
                        }

                        await ConfirmChangeFolderAsync(settings);
                        break;
                    case "/openlikes":
                        await GetLikesAsync(settings);

                        break;
                    case "/delete":
                        if (username != $"{Secrets.MyUsername}")
                        {
                            await NoAccessAsync(settings);
                            return;
                        }

                        await DeleteAsync(settings);
                        break;
                    case "/confirmDelete":
                        if (username != $"{Secrets.MyUsername}")
                        {
                            await NoAccessAsync(settings);
                            return;
                        }

                        await ConfirmDeleteAsync(settings);
                        break;
                }

                await settings.Bot.AnswerCallbackQueryAsync(
                    callbackQueryId: settings.Update.CallbackQuery!.Id,
                    cancellationToken: settings.CancellationToken);
                return;
        }
    }

    private async Task ChangeFolderAsync(Settings settings)
    {
        var folders = await _diskService.GetFoldersAsync();
        await settings.Bot.SendTextMessageAsync(
            chatId: settings.ChatId,
            text: "Выбери из списка",
            replyMarkup: new InlineKeyboardMarkup(
                folders.Select(f => new[]
                {
                    InlineKeyboardButton.WithCallbackData(f.Name!, $"/changedir {f.Name}")
                })
            ),
            cancellationToken: settings.CancellationToken);
    }

    private async Task ConfirmDeleteAsync(Settings settings)
    {
        var imgName = settings.Query!;
        _diskService.DeleteImage(imgName);
        await settings.Bot.SendTextMessageAsync(
            chatId: settings.ChatId,
            text: $"{imgName} удалено.",
            cancellationToken: settings.CancellationToken);
        await settings.Bot.DeleteMessageAsync(
            chatId: settings.ChatId,
            messageId: settings.Update.CallbackQuery!.Message!.MessageId,
            cancellationToken: settings.CancellationToken);
    }

    private async Task DownloadImageAsync(Settings settings)
    {
        var imgName = settings.Query!;
        settings.Image = _diskService.FindImageByName(imgName);
        await settings.Bot.SendTextMessageAsync(
            chatId: settings.ChatId,
            text: "Фото скоро будет доступно в чате, можешь продолжать использовать бота.",
            cancellationToken: settings.CancellationToken
        );

        var img = settings.Image;
        if (img is null)
        {
            await ImgNotFoundAsync(settings, imgName);
            return;
        }

        var imgBytes = await _diskService.LoadOriginalImageAsync(img);

        await settings.Bot.SendDocumentAsync(
            chatId: settings.ChatId,
            document: new InputMedia(new MemoryStream(imgBytes), img.Name),
            cancellationToken: settings.CancellationToken);
    }

    private async Task DeleteAsync(Settings settings)
    {
        var imgName = settings.Query!;
        settings.Image = _diskService.FindImageByName(imgName);
        var img = settings.Image;
        if (img is null)
        {
            await ImgNotFoundAsync(settings, imgName);
            return;
        }

        await settings.Bot.SendPhotoAsync(
            chatId: settings.ChatId,
            photo: new MemoryStream(await _diskService.LoadThumbnailImageAsync(img))!,
            caption: $"Точно удалить {img.Name}?",
            parseMode: ParseMode.Html,
            replyMarkup: new InlineKeyboardMarkup(
                InlineKeyboardButton.WithCallbackData("да", $"/confirmDelete {img.Name}")
            )
        );
    }

    private async Task ImgNotFoundAsync(Settings settings, string imgName)
    {
        await settings.Bot.SendTextMessageAsync(
            chatId: settings.ChatId,
            text: $"Фотография {imgName} не найдена.",
            cancellationToken: settings.CancellationToken);
        return;
    }

    private async Task GetLikesAsync(Settings settings)
    {
        var likes = await _diskService.GetLikesAsync(settings.ChatId);

        if (!likes.Any())
        {
            await settings.Bot.SendTextMessageAsync(
                chatId: settings.ChatId,
                text: "Список избранных пуст",
                cancellationToken: settings.CancellationToken);
            return;
        }

        var mediaPhotos = likes
            .Select(pair => new InputMediaPhoto(new InputMedia(new MemoryStream(pair.Value), pair.Key)));

        await settings.Bot.SendMediaGroupAsync(
            chatId: settings.ChatId,
            media: mediaPhotos.Take(10),
            cancellationToken: settings.CancellationToken,
            disableNotification: true
        );

        var urlToLikedImages = _diskService.GetPublicFolderUrlByChatIdAsync(chatId: settings.ChatId);

        await settings.Bot.SendTextMessageAsync(
            chatId: settings.ChatId,
            text: $"<a href=\"{await urlToLikedImages}\">Папка на диске</a>",
            parseMode: ParseMode.Html,
            cancellationToken: settings.CancellationToken,
            disableNotification: true
        );

        var task = _diskService.LoadLikesAsync(settings.ChatId);
    }

    private async Task StartAsync(Settings settings)
    {
        Console.WriteLine($"{DateTime.Now} | Бот запущен для {settings.Update.Message!.Chat.Username}");
        await settings.Bot.SendTextMessageAsync(
            text: "Этот бот умеет присылать фотки с яндекс диска, " +
                  "искать по названию/дате, " +
                  "добавлять в избранное " +
                  "и формировать папку с оригиналами на яндекс диске.",
            chatId: settings.ChatId,
            replyMarkup: defaultReplyKeyboardMarkup,
            cancellationToken: settings.CancellationToken,
            disableNotification: true);
    }

    private async Task LikeAsync(Settings settings)
    {
        var imgName = settings.Query!;
        settings.Image = _diskService.FindImageByName(imgName);

        if (settings.Image is null)
        {
            await ImgNotFoundAsync(settings, imgName);
            return;
        }

        var url = await _diskService.GetPublicFolderUrlByChatIdAsync(settings.ChatId);

        var task = _diskService.AddToLikes(settings.ChatId, settings.Image);

        await settings.Bot.SendTextMessageAsync(
            chatId: settings.ChatId,
            text: $"{settings.Image!.Name} добавлено в <a href=\"{url}\">избранное</a>",
            disableWebPagePreview: true,
            cancellationToken: settings.CancellationToken,
            parseMode: ParseMode.Html,
            disableNotification: true);
    }

    private async Task ConfirmChangeFolderAsync(Settings settings)
    {
        var parentFolder = settings.Query!;
        if (Urls.TargetFolder != parentFolder)
        {
            Urls.TargetFolder = parentFolder;
            await _diskService.LoadImagesAsync();
        }

        await settings.Bot.SendTextMessageAsync(
            chatId: settings.ChatId,
            text: $"Папка изменена на {Urls.TargetFolder}",
            replyMarkup: defaultReplyKeyboardMarkup,
            cancellationToken: settings.CancellationToken,
            disableNotification: true);
    }

    private async Task NoAccessAsync(Settings settings)
    {
        await settings.Bot.SendTextMessageAsync(
            chatId: settings.ChatId,
            text: "Нет доступа",
            cancellationToken: settings.CancellationToken,
            disableNotification: true);
    }

    private async Task HelpAsync(Settings settings)
    {
        Console.WriteLine($"{DateTime.Now} | Отправлен список помощи");
        await settings.Bot.SendTextMessageAsync(
            chatId: settings.ChatId,
            text: "Доступные команды:\n" +
                  "/find <дата, имя> - найти фото по названию.\n" +
                  "/changedir <имя> - сменить папку.\n" +
                  "/like <имя> - добавить фотку в избранные.\n" +
                  "/likes - избранные фотки.\n" +
                  "/openlikes - получить ссылку на лайкнутые фотки на диске.\n" +
                  "/delete - удалить фотку с диска.\n" +
                  "/help - доступные команды.\n" +
                  "/start - начало работы бота.\n",
            cancellationToken: settings.CancellationToken,
            disableNotification: true
        );
    }

    private async Task FindAsync(Settings settings)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("de-DE");

        settings.Image = _diskService.GetRandomImage();

        if (settings.Query == null)
        {
            await SendImageAsync(settings);
            return;
        }

        var dateString = settings.Query.Split(" ")[0];
        if (DateTime.TryParseExact(
                dateString,
                "dd.MM.yyyy",
                CultureInfo.GetCultureInfo("de-DE"),
                DateTimeStyles.None,
                out var date))
        {
            var img = _diskService.GetRandomImage(date);
            if (img is null)
            {
                var dateBefore = _diskService.FindClosestDateBefore(date);
                var dateAfter = _diskService.FindClosestDateAfter(date);

                await settings.Bot.SendTextMessageAsync(
                    chatId: settings.ChatId,
                    text: $"На дату {date.ToShortDateString()} фотографий нет. " +
                          $"Можешь посмотреть фотки на ближайшие даты в прошлом и в будущем.",
                    replyMarkup: new InlineKeyboardMarkup(new[]
                    {
                        InlineKeyboardButton.WithCallbackData(dateBefore.ToShortDateString(),
                            $"/find {dateBefore}"),
                        InlineKeyboardButton.WithCallbackData(dateAfter.ToShortDateString(),
                            $"/find {dateAfter}"),
                    }),
                    cancellationToken: settings.CancellationToken);
            }
            else
            {
                settings.Image = img;
                await SendImageAsync(settings);
            }

            return;
        }

        settings.Image = _diskService.FindImageByName(settings.Query);
        if (settings.Image is null)
        {
            await ImgNotFoundAsync(settings, settings.Query);
            return;
        }

        await SendImageAsync(settings);
    }

    async Task SendImageAsync(Settings settings)
    {
        var img = settings.Image!;
        await settings.Bot.SendPhotoAsync(
            chatId: settings.ChatId,
            caption: $"<a href=\"{Urls.OpenInBrowserUrl + img.Name}\">{img.Name}</a><b> {img.DateTime}</b>",
            parseMode: ParseMode.Html,
            photo: new MemoryStream(await _diskService.LoadThumbnailImageAsync(img))!,
            replyMarkup: new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("Удалить", $"/delete {img.Name}"),
                    InlineKeyboardButton.WithCallbackData("🖤", $"/like {img.Name}"),
                    /*InlineKeyboardButton.WithCallbackData("Скачать", $"/download {img.Name}"),*/
                },
                new[]
                {
                    InlineKeyboardButton
                        .WithCallbackData("Предыдущий день", $"/find {img.DateTime.Date.AddDays(-1)}"),
                    InlineKeyboardButton
                        .WithCallbackData("Ещё за эту дату", $"/find {img.DateTime.Date}"),
                    InlineKeyboardButton
                        .WithCallbackData("Следующий день", $"/find {img.DateTime.Date.AddDays(1)}"),
                }
            }),
            cancellationToken: settings.CancellationToken,
            disableNotification: true
        );

        var username = (settings.Update.Message is not null
            ? settings.Update.Message.Chat.Username
            : settings.Update.CallbackQuery!.From.Username)!;

        Console.WriteLine(
            $"{DateTime.Now} | Отправлено фото {img} пользователю {username}");
    }

    private Task HandlePollingErrorAsync(Exception exception)
    {
        var ErrorMessage = exception switch
        {
            ApiRequestException apiRequestException
                => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
            _ => exception.ToString()
        };

        Console.WriteLine(ErrorMessage);

        return Task.CompletedTask;
    }

    private Func<ITelegramBotClient, Exception, CancellationToken, Task> PollingErrorHandler()
    {
        return async (_, exception, _) =>
        {
            try
            {
                await HandlePollingErrorAsync(exception);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error occurred in HandlePollingErrorAsync: " + ex.Message);
            }
        };
    }

    private Func<ITelegramBotClient, Update, CancellationToken, Task> UpdateHandler()
    {
        return async (botClient, update, cancellationToken) =>
        {
            try
            {
                await HandleUpdateAsync(botClient, update, cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error occurred in HandleUpdateAsync: " + ex.Message);
            }
        };
    }
}

class Settings
{
    public Settings(ITelegramBotClient bot, Update update, CancellationToken cancellationToken)
    {
        Bot = bot;
        Update = update;
        CancellationToken = cancellationToken;
    }

    public ITelegramBotClient Bot { get; }
    public Update Update { get; }
    public long ChatId { get; set; }
    public CancellationToken CancellationToken { get; }
    public Image? Image { get; set; }
    public string? Cmd { get; set; }
    public string? Query { get; set; }
}