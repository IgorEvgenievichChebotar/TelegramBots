using System.Reflection;
using OpenAI.GPT3.Interfaces;

namespace TelegramBots.ChatGptTelegramBot;

public static class OpenAiServiceExtensions
{
    public static void SetHttpClientTimeout(this IOpenAIService openAiService, TimeSpan timeout)
    {
        var httpClientField = openAiService.GetType().GetField("_httpClient",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var httpClient = (HttpClient)httpClientField.GetValue(openAiService)!;

        httpClient.Timeout = timeout;
    }
}