using System.Data.SQLite;
using OpenAI.GPT3.ObjectModels;
using OpenAI.GPT3.ObjectModels.RequestModels;

namespace TelegramBots.ChatGptTelegramBot;

public interface IMessagesRepo
{
    Task<List<ChatMessage>> GetHistory(long chatId);
    Task Save(ChatMessage message, long chatId);
    Task<ChatMessage> RemoveLast(long chatId);
    Task RemoveAll(long chatId);
}

public class MessagesRepo : IMessagesRepo
{
    private static readonly SQLiteConnection _connection = new("Data Source=messages;Version=3;");

    public MessagesRepo()
    {
        var sqLiteCommand = _connection.CreateCommand();
        sqLiteCommand.CommandText = @"create table if not exists messages
        (
            id     integer primary key autoincrement,
            chatId integer,
            text   text
        );";
        _connection.Open();
        sqLiteCommand.ExecuteNonQuery();
        _connection.Close();
    }

    public async Task<List<ChatMessage>> GetHistory(long chatId)
    {
        var messages = new List<ChatMessage>();
        var sqLiteCommand = _connection.CreateCommand();
        sqLiteCommand.CommandText = "select * from messages where chatId = @chatId";
        sqLiteCommand.Parameters.AddWithValue("@chatId", chatId);
        await _connection.OpenAsync();
        var reader = await sqLiteCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var id = reader.GetInt32(0);
            var text = reader.GetString(2);
            messages.Add(new ChatMessage(StaticValues.ChatMessageRoles.User, text));
        }

        await _connection.CloseAsync();

        return messages;
    }


    public async Task Save(ChatMessage message, long chatId)
    {
        await using var connection = new SQLiteConnection("Data Source=messages;Version=3;");
        await connection.OpenAsync();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "insert into messages (chatId, text) values (@chatId, @text)";
            command.Parameters.AddWithValue("@chatId", chatId);
            command.Parameters.AddWithValue("@text", message.Content);

            await command.ExecuteNonQueryAsync();
        }

        await connection.CloseAsync();
    }

    public async Task<ChatMessage> RemoveLast(long chatId)
    {
        var sqLiteCommand = _connection.CreateCommand();
        sqLiteCommand.CommandText = "select * from messages where chatId = @chatId order by id desc limit 1";
        sqLiteCommand.Parameters.AddWithValue("@chatId", chatId);
        await _connection.OpenAsync();
        var reader = await sqLiteCommand.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        var id = reader.GetInt32(0);
        var text = reader.GetString(2);
        var message = new ChatMessage(StaticValues.ChatMessageRoles.User, text);
        var sqLiteCommandDelete = _connection.CreateCommand();
        sqLiteCommandDelete.CommandText = "delete from messages where id = @id";
        sqLiteCommandDelete.Parameters.AddWithValue("@id", id);
        await sqLiteCommandDelete.ExecuteNonQueryAsync();
        await _connection.CloseAsync();
        return message;
    }

    public async Task RemoveAll(long chatId)
    {
        var sqLiteCommand = _connection.CreateCommand();
        sqLiteCommand.CommandText = "delete from messages where chatId = @chatId";
        sqLiteCommand.Parameters.AddWithValue("@chatId", chatId);
        await _connection.OpenAsync();
        await sqLiteCommand.ExecuteNonQueryAsync();
        await _connection.CloseAsync();
    }
}