using System.Text;
using OpenAI.GPT3;
using OpenAI.GPT3.Managers;
using OpenAI.GPT3.ObjectModels;
using OpenAI.GPT3.ObjectModels.RequestModels;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace TelegramBots.ChatGptTelegramBot;

public class Program
{
    private static readonly TelegramBotClient _botClient = new(Secrets.BotToken);

    private static readonly OpenAIService _aiService = new(new OpenAiOptions
    {
        ApiKey = Secrets.OpenAiToken,
        DefaultModelId = Models.ChatGpt3_5Turbo
    });

    private static readonly IMessagesRepo _messagesRepo = new MessagesRepo();

    public static void Run(string[] args)
    {
        _aiService.SetHttpClientTimeout(TimeSpan.FromMinutes(5));

        using CancellationTokenSource cts = new();

        async void UpdateHandler(ITelegramBotClient bot, Update update, CancellationToken token)
        {
            switch (update.Type)
            {
                case UpdateType.Message:
                    if (update.Message is null) return;

                    var msg = update.Message;
                    var chatId = msg.Chat.Id;

                    var name = msg.Chat.FirstName + msg.Chat.LastName;
                    var username = msg.Chat.Username;

                    if (msg.Type == MessageType.Document)
                    {
                        var document = msg.Document!;
                        if (document.MimeType is not "text/plain") return;

                        using var stream = new MemoryStream();

                        await bot.GetInfoAndDownloadFileAsync(
                            fileId: document.FileId,
                            destination: stream,
                            cancellationToken: token);

                        var fileText = Encoding.Default.GetString(stream.ToArray());
                        if (fileText.Length == 0)
                        {
                            await bot.SendTextMessageAsync(
                                chatId: chatId,
                                text: "Текста в файле нет",
                                cancellationToken: token);
                            return;
                        }

                        Console.WriteLine($"{DateTime.Now}| Отправлен файл пользователем {name} - {username}");

                        await AskAsync(bot, chatId, token, fileText);
                        return;
                    }

                    if (msg.Text is null) return;

                    Console.WriteLine(
                        $"{DateTime.Now}| Отправлено сообщение '{msg.Text}' пользователем {name} - {username}");

                    var parts = msg.Text.Split(' ', 2);
                    var cmd = parts[0];
                    var query = parts.Length > 1 ? parts[1] : string.Empty;
                    var text = cmd + " " + query;

                    switch (cmd)
                    {
                        case "/start":
                            await bot.SendTextMessageAsync(
                                chatId: chatId,
                                text: "API chatGPT. " +
                                      "Преимущества по сравнению с сайтом: \n" +
                                      "1. Не надо регистрироваться, включать впн, регать иностранную симку и тд\n" +
                                      "2. Ответ приходит практически сразу, а на сайте нужно ждать\n" +
                                      "3. Возможность заливать длинный текст в файле .txt. На сайте он бы отказался отвечать.\n" +
                                      "4. Во время большой нагрузки сайт ложится и лагает, а мой бот продолжает работать как часы",
                                cancellationToken: token);
                            await bot.SendTextMessageAsync(
                                chatId: chatId,
                                text: "Пиши текстом или прикрепляй сюда текстовый файл, " +
                                      "бот из него извлечёт текст и отправит chatGTP. " +
                                      "Ответ ты получишь здесь, в чате.",
                                cancellationToken: token);
                            return;
                        case "/newchat":
                            await _messagesRepo.RemoveAll(chatId);
                            await bot.SendTextMessageAsync(
                                chatId: chatId,
                                text: "Контекст переписки удалён. Можешь задавать новые вопросы.",
                                cancellationToken: token);
                            return;
                        default:
                            if (text.Length <= 0)
                            {
                                await bot.SendTextMessageAsync(
                                    chatId: chatId,
                                    text: "Вопрос не задан - пустой запрос.",
                                    cancellationToken: token
                                );
                                return;
                            }

                            await AskAsync(bot, chatId, token, text);
                            return;
                    }
                case UpdateType.CallbackQuery:
                    if (update.CallbackQuery?.Message?.Text is null) return;

                    var callbackMsg = update.CallbackQuery.Message;
                    var callbackCmd = update.CallbackQuery.Data!.Split(" ")[0];
                    var callbackChatId = callbackMsg.Chat.Id;

                    switch (callbackCmd)
                    {
                        case "/regen":
                            var chatMessage = await _messagesRepo.RemoveLast(callbackChatId);

                            await AskAsync(bot, callbackChatId, token, chatMessage.Content);

                            await bot.AnswerCallbackQueryAsync(
                                callbackQueryId: update.CallbackQuery.Id,
                                cancellationToken: token);

                            return;
                    }

                    break;
            }
        }

        void PollingErrorHandler(ITelegramBotClient bot, Exception exception, CancellationToken token)
        {
        }

        _botClient.StartReceiving(
            updateHandler: UpdateHandler,
            pollingErrorHandler: PollingErrorHandler,
            receiverOptions: new ReceiverOptions { AllowedUpdates = Array.Empty<UpdateType>() },
            cancellationToken: cts.Token
        );

        Console.ReadLine();
        cts.Cancel();
    }

    private static async Task AskAsync(ITelegramBotClient bot, long chatId, CancellationToken token, string query)
    {
        var answer = AnswerAsync(query, token, chatId);
        await bot.SendChatActionAsync(
            chatId: chatId,
            chatAction: ChatAction.Typing,
            cancellationToken: token);
        var msg = await bot.SendTextMessageAsync(
            chatId: chatId,
            text: await answer,
            cancellationToken: token
        );
        await bot.EditMessageReplyMarkupAsync(
            chatId: chatId,
            messageId: msg.MessageId,
            replyMarkup: new InlineKeyboardMarkup(
                InlineKeyboardButton.WithCallbackData("🔄", $"/regen {msg.MessageId}")),
            cancellationToken: token);

        static async Task<string> AnswerAsync(string question, CancellationToken token, long chatId)
        {
            await _messagesRepo.Save(ChatMessage.FromUser(question), chatId);
            var response = await _aiService.ChatCompletion.CreateCompletion(new ChatCompletionCreateRequest
                {
                    Messages = await _messagesRepo.GetHistory(chatId)
                },
                cancellationToken: token
            );
            var answer = response.Choices[0].Message.Content;
            return answer;
        }
    }
}