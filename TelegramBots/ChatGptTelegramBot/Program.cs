using System.Text;
using OpenAI.GPT3.ObjectModels.RequestModels;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace TelegramBots.ChatGptTelegramBot;

public class Program
{
    private readonly ITelegramBotClient _bot;
    private readonly IMessagesRepo _messagesRepo;
    private readonly IChatGPTService _chatGptService;
    private readonly CancellationTokenSource _cts;

    public Program()
    {
        _bot = new TelegramBotClient(Secrets.BotToken);
        _messagesRepo = new MessagesRepo("Data Source=messages;Version=3;");
        _chatGptService = new ChatGPTService();
        _cts = new CancellationTokenSource();
    }

    public void Run(string[] args)
    {
        _bot.StartReceiving(
            updateHandler: UpdateHandler(),
            pollingErrorHandler: PollingErrorHandler(),
            cancellationToken: _cts.Token
        );

        Console.ReadLine();
        _cts.Cancel();
    }

    async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken token)
    {
        if (update.Type == UpdateType.Message)
        {
            if (update.Message is null) return;

            var msg = update.Message;
            var chatId = msg.Chat.Id;

            var name = msg.Chat.FirstName + msg.Chat.LastName;
            var username = msg.Chat.Username;

            if (msg.Text is null) return;

            Console.WriteLine(
                $"{DateTime.Now} | Отправлено сообщение '{msg.Text}' пользователем {name} - {username}");

            var parts = msg.Text.Split(' ', 2);
            var cmd = parts[0];
            var query = parts.Length > 1 ? parts[1] : string.Empty;
            var text = cmd + " " + query;

            if (cmd == "/start")
            {
                await bot.SendTextMessageAsync(
                    chatId: chatId,
                    text: "API chatGPT. " +
                          "Преимущества по сравнению с сайтом: \n" +
                          "1. Не надо регистрироваться, включать впн, регать иностранную симку и тд\n" +
                          "2. Ответ приходит практически сразу, а на сайте нужно ждать\n" +
                          "3. Возможность заливать длинный текст в файле .txt. На сайте он бы отказался отвечать.\n" +
                          "4. Во время большой нагрузки сайт ложится и лагает, а бот продолжает работать как часы.\n" +
                          "5. Возможность редактирования сообщений.",
                    cancellationToken: token);
                await bot.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Пиши текстом или прикрепляй сюда текстовый файл, " +
                          "бот из него извлечёт текст и отправит chatGTP. " +
                          "Ответ ты получишь здесь, в чате.",
                    cancellationToken: token);
                return;
            }

            if (cmd == "/newchat")
            {
                await _messagesRepo.RemoveAllAsync(chatId);
                await bot.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Контекст переписки удалён. Можешь задавать новые вопросы.",
                    cancellationToken: token);
                return;
            }

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

                Console.WriteLine($"{DateTime.Now} | Отправлен файл пользователем {name} - {username}");

                await _messagesRepo.SaveAsync(chatId: chatId, msgId: msg.MessageId, msgText: fileText);
            }

            if (text.Length > 0)
            {
                await _messagesRepo.SaveAsync(chatId: chatId, msgId: msg.MessageId, msgText: text);
            }

            var questions = await _messagesRepo.GetQuestionsAsync(chatId);
            await AskAsync(chatId: chatId, questionMsgId: msg.MessageId, questions: questions);
            return;
        }

        if (update.Type == UpdateType.CallbackQuery)
        {
            if (update.CallbackQuery?.Message?.Text is null) return;

            var msg = update.CallbackQuery.Message;
            var query = update.CallbackQuery.Data!.Split(" ")[1];
            var cmd = update.CallbackQuery.Data!.Split(" ")[0];
            var chatId = msg.Chat.Id;

            if (msg.Text is null) return;

            if (cmd == "/regen")
            {
                var questions = await _messagesRepo.GetQuestionsBeforeInclusiveAsync(
                    chatId: chatId,
                    msgId: Convert.ToInt64(query));

                await AskAsync(chatId: chatId, questionMsgId: Convert.ToInt64(query), questions: questions);

                await bot.AnswerCallbackQueryAsync(
                    callbackQueryId: update.CallbackQuery.Id,
                    cancellationToken: token);
            }
        }

        if (update.Type == UpdateType.EditedMessage)
        {
            var editedMsg = update.EditedMessage!;
            var chatId = editedMsg.Chat.Id;
            var msgId = editedMsg.MessageId;

            if (editedMsg.Text is null) return;

            await _messagesRepo.UpdateAsync(msgId, editedMsg.Text);
            var questions = await _messagesRepo.GetQuestionsBeforeInclusiveAsync(
                chatId: chatId, 
                msgId: msgId);

            await AskAsync(chatId: chatId, questionMsgId: msgId, questions: questions);
        }
    }

    async Task AskAsync(long chatId, long questionMsgId, IList<ChatMessage> questions)
    {
        await _bot.SendChatActionAsync(
            chatId: chatId,
            chatAction: ChatAction.Typing,
            cancellationToken: _cts.Token);

        var answer = await _chatGptService.AskAsync(questions);

        await _bot.SendTextMessageAsync(
            chatId: chatId,
            text: answer,
            replyMarkup: new InlineKeyboardMarkup(
                InlineKeyboardButton.WithCallbackData("🔄", $"/regen {questionMsgId}")),
            cancellationToken: _cts.Token);
    }

    Func<ITelegramBotClient, Exception, CancellationToken, Task> PollingErrorHandler()
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

    Task HandlePollingErrorAsync(Exception exception)
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

    Func<ITelegramBotClient, Update, CancellationToken, Task> UpdateHandler()
    {
        return async (bot, update, cancellationToken) =>
        {
            try
            {
                await HandleUpdateAsync(bot, update, cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error occurred in HandleUpdateAsync: " + ex.Message);
            }
        };
    }
}