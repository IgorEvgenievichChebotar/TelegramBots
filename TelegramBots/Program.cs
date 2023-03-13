namespace TelegramBots;

public static class Program
{
    private static async Task Main(string[] args)
    {
        var chatGptBot = new ChatGptTelegramBot.Program();
        var photosBot = new PhotosTelegramBot.Program();

        var options = TaskCreationOptions.LongRunning;

        await Task.WhenAll(
            Task.Factory.StartNew(() => chatGptBot.Run(Array.Empty<string>()), options),
            Task.Factory.StartNew(() => photosBot.Run(Array.Empty<string>()), options)
        );
    }
}