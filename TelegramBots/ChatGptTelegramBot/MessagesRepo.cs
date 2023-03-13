using System.Data.SQLite;
using OpenAI.GPT3.ObjectModels.RequestModels;

namespace TelegramBots.ChatGptTelegramBot;

public interface IMessagesRepo
{
    Task<List<ChatMessage>> GetQuestionsAsync(long chatId);
    Task SaveAsync(string msgText, long msgId, long chatId, string role = "user");
    Task RemoveAllAsync(long chatId);
    Task UpdateAsync(long msgId, string editedQuestion);
    Task RemoveAllAfterAsync(long msgId);
    Task<IList<ChatMessage>> GetQuestionsBeforeInclusiveAsync(long chatId, long msgId);
}

public class MessagesRepo : IMessagesRepo
{
    private readonly string connectionString;

    public MessagesRepo(string connectionString)
    {
        this.connectionString = connectionString;
        using var connection = new SQLiteConnection(connectionString);
        connection.Open();

        using var sqlCommand = connection.CreateCommand();
        sqlCommand.CommandText = @"create table if not exists messages
        (
            msgId  integer not null ,
            chatId integer not null,
            text   text,
            time   datetime DEFAULT current_timestamp,
            role   text default 'user'
        );";
        sqlCommand.ExecuteNonQuery();
    }

    public async Task<List<ChatMessage>> GetQuestionsAsync(long chatId)
    {
        await using var connection = new SQLiteConnection(connectionString);
        await connection.OpenAsync();

        await using var sqlCommand = connection.CreateCommand();
        sqlCommand.CommandText = "select * from messages where chatId = @chatId";
        sqlCommand.Parameters.AddWithValue("@chatId", chatId);

        await using var reader = await sqlCommand.ExecuteReaderAsync();

        var messages = new List<ChatMessage>();
        while (await reader.ReadAsync())
        {
            var text = reader.GetString(2);
            var role = reader.GetString(4);
            messages.Add(new ChatMessage(role, text));
        }

        return messages;
    }


    public async Task SaveAsync(string msgText, long msgId, long chatId, string role = "user")
    {
        await using var connection = new SQLiteConnection(connectionString);
        await connection.OpenAsync();

        await using var sqlCommand = connection.CreateCommand();
        sqlCommand.CommandText =
            "insert into messages (msgId, chatId, text, role) values (@msgId, @chatId, @text, @role)";
        sqlCommand.Parameters.AddWithValue("@chatId", chatId);
        sqlCommand.Parameters.AddWithValue("@msgId", msgId);
        sqlCommand.Parameters.AddWithValue("@text", msgText);
        sqlCommand.Parameters.AddWithValue("@role", role);

        await sqlCommand.ExecuteNonQueryAsync();
    }

    public async Task RemoveAllAsync(long chatId)
    {
        await using var connection = new SQLiteConnection(connectionString);
        await connection.OpenAsync();

        await using var sqlCommand = connection.CreateCommand();
        sqlCommand.CommandText = "delete from messages where chatId = @chatId";
        sqlCommand.Parameters.AddWithValue("@chatId", chatId);

        await sqlCommand.ExecuteNonQueryAsync();
    }

    public async Task UpdateAsync(long msgId, string editedQuestion)
    {
        await using var connection = new SQLiteConnection(connectionString);
        await connection.OpenAsync();

        await using var sqlCommand = connection.CreateCommand();
        sqlCommand.CommandText = "UPDATE messages SET text = @text WHERE msgId = @msgId;";
        sqlCommand.Parameters.AddWithValue("@text", editedQuestion);
        sqlCommand.Parameters.AddWithValue("@msgId", msgId);

        await sqlCommand.ExecuteNonQueryAsync();
    }
    
    public async Task RemoveAllAfterAsync(long msgId)
    {
        await using var connection = new SQLiteConnection(connectionString);
        await connection.OpenAsync();

        await using var sqlCommand = connection.CreateCommand();
        sqlCommand.CommandText = "DELETE FROM messages " +
                                 "WHERE chatId = 34043403 " +
                                 "AND time > (SELECT time FROM messages WHERE msgId = 867);";
        sqlCommand.Parameters.AddWithValue("@msgId", msgId);

        await sqlCommand.ExecuteNonQueryAsync();
    }

    public async Task<IList<ChatMessage>> GetQuestionsBeforeInclusiveAsync(long chatId, long msgId)
    {
        await using var connection = new SQLiteConnection(connectionString);
        await connection.OpenAsync();

        await using var sqlCommand = connection.CreateCommand();
        sqlCommand.CommandText = "select * " +
                                 "from messages " +
                                 "where chatId = @chatId " +
                                 "and (select time from messages where msgId = @msgId and chatId = @chatId) >= time;";
        sqlCommand.Parameters.AddWithValue("@chatId", chatId);
        sqlCommand.Parameters.AddWithValue("@msgId", msgId);

        await using var reader = await sqlCommand.ExecuteReaderAsync();

        var messages = new List<ChatMessage>();
        while (await reader.ReadAsync())
        {
            var text = reader.GetString(2);
            var role = reader.GetString(4);
            messages.Add(new ChatMessage(role, text));
        }

        return messages;
    }
}