using OpenAI.GPT3;
using OpenAI.GPT3.Interfaces;
using OpenAI.GPT3.Managers;
using OpenAI.GPT3.ObjectModels;
using OpenAI.GPT3.ObjectModels.RequestModels;

namespace TelegramBots.ChatGptTelegramBot;

public interface IChatGPTService
{
    Task<string> AskAsync(IList<ChatMessage> messages);
}

public class ChatGPTService : IChatGPTService
{
    private readonly IOpenAIService _aiService;

    public ChatGPTService()
    {
        _aiService = new OpenAIService(new OpenAiOptions
        {
            ApiKey = Secrets.OpenAiToken,
            DefaultModelId = Models.ChatGpt3_5Turbo
        });
        _aiService.SetHttpClientTimeout(TimeSpan.FromMinutes(5));
    }


    public async Task<string> AskAsync(IList<ChatMessage> messages)
    {
        var response = await _aiService.ChatCompletion.CreateCompletion(
            new ChatCompletionCreateRequest { Messages = messages }
        );

        if (!response.Successful)
        {
            Console.WriteLine($"{DateTime.Now} | ОШИБКА ОТВЕТА ЧАТГПТ - {response.Error?.Message}");
            return "Произошла ошибка на стороне сервера chatGPT. " +
                   "Скорее всего закончилась квота. \n" +
                   "Обратитесь к @igor_gcnx, чтобы он поменял ключ.";
        }

        var answer = response.Choices[0].Message.Content;
        return answer;
    }
}