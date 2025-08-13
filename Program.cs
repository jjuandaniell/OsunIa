using GroqApiLibrary;
using Newtonsoft.Json.Linq;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;
using System.Collections.Concurrent;

class Program
{
    static ConcurrentDictionary<long, List<JObject>> userHistories = new();

    static async Task Main(string[] args)
    {
        string groqAiKey = Environment.GetEnvironmentVariable("GROQ_API_KEY");
        string telegramToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");

        if (string.IsNullOrEmpty(groqAiKey) || string.IsNullOrEmpty(telegramToken))
        {
            Console.WriteLine("Error: Missing GROQ_API_KEY or TELEGRAM_BOT_TOKEN environment variables.");
            return;
        }

        var groqApi = new GroqAPI(groqAiKey);
        var botClient = new TelegramBotClient(telegramToken);

        using var cts = new CancellationTokenSource();

        botClient.StartReceiving(
            HandleUpdateAsync,
            HandleErrorAsync,
            new ReceiverOptions { AllowedUpdates = { } },
            cancellationToken: cts.Token
        );

        Console.WriteLine("Bot is running...");
        await Task.Delay(-1); // Mantener vivo el bot en servidor
    }

    static async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        try
        {
            if (update.Type != UpdateType.Message || update.Message!.Type != MessageType.Text)
                return;

            var chatId = update.Message.Chat.Id;
            var userInput = update.Message.Text.Trim();

            var history = userHistories.GetOrAdd(chatId, _ => new List<JObject>());
            history.Add(new JObject { ["role"] = "user", ["content"] = userInput });

            int maxMessagesSize = 8;
            if (history.Count > maxMessagesSize)
                history.RemoveRange(0, history.Count - maxMessagesSize);

            JObject response = await GenerateAIResponse(new GroqAPI(Environment.GetEnvironmentVariable("GROQ_API_KEY")), history);
            string aiResponse = response?["choices"]?[0]?["message"]?["content"]?.ToString() ?? "No response from AI.";

            history.Add(new JObject { ["role"] = "assistant", ["content"] = aiResponse });

            const int telegramMessageLimit = 4096;
            if (aiResponse.Length > telegramMessageLimit)
            {
                for (int i = 0; i < aiResponse.Length; i += telegramMessageLimit)
                {
                    string part = aiResponse.Substring(i, Math.Min(telegramMessageLimit, aiResponse.Length - i));
                    await bot.SendTextMessageAsync(new ChatId(chatId), part, cancellationToken: ct);
                }
            }
            else
            {
                await bot.SendTextMessageAsync(new ChatId(chatId), aiResponse, cancellationToken: ct);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception in HandleUpdateAsync: {ex}");
        }
    }

    static Task HandleErrorAsync(ITelegramBotClient bot, Exception exception, CancellationToken ct)
    {
        Console.WriteLine($"Telegram error: {exception}");
        return Task.CompletedTask;
    }

    static async Task<JObject> GenerateAIResponse(GroqAPI anApi, List<JObject> history)
    {
        JObject request = new JObject
        {
            ["model"] = "llama-3.1-8b-instant",
            ["messages"] = new JArray(history)
        };

        return await anApi.CreateChatCompletionAsync(request);
    }
}

