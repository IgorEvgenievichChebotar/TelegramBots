namespace TelegramBots;

public static class Program
{
    private static void Main(string[] args)
    {
        var ChatGPTBot = Task.Factory.StartNew(() => ChatGptTelegramBot.Program.Run(Array.Empty<string>()),
            TaskCreationOptions.LongRunning);

        var PhotosBot = Task.Factory.StartNew(() => PhotosTelegramBot.Program.Run(Array.Empty<string>()),
            TaskCreationOptions.LongRunning);

        Task.WaitAll(ChatGPTBot, PhotosBot);
    }
}