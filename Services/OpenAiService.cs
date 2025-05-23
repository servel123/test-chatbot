using System;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ChatbotBackend.Services
{
    public class OpenAiService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private const string BaseUrl = "https://api.openai.com/v1";

        public OpenAiService(IConfiguration config)
        {
            _apiKey = config["OpenAI:ApiKey"];
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            _httpClient.DefaultRequestHeaders.Add("OpenAI-Beta", "assistants=v2");
        }

        public async Task<string> CheckApiKeyAsync()
        {
            try
            {
                var testRequest = new
                {
                    model = "gpt-4-turbo",
                    messages = new[]
                    {
                new { role = "user", content = "Ping?" }
            }
                };

                var response = await _httpClient.PostAsync(
                    "https://api.openai.com/v1/chat/completions",
                    new StringContent(JsonSerializer.Serialize(testRequest), Encoding.UTF8, "application/json")
                );

                var result = await response.Content.ReadAsStringAsync();
                return result;
            }
            catch (Exception ex)
            {
                return $"Lỗi khi gọi API: {ex.Message}";
            }
        }


        public async Task<string> GetAssistantResponseAsync(string userInput)
        {
            try
            {
                var assistantBody = new
                {
                    name = "Demo Assistant",
                    instructions = "Bạn là trợ lý AI hữu ích.",
                    model = "gpt-4-turbo"
                };

                var assistantResp = await _httpClient.PostAsync($"{BaseUrl}/assistants",
                    new StringContent(JsonSerializer.Serialize(assistantBody), Encoding.UTF8, "application/json"));

                var assistantJson = await assistantResp.Content.ReadAsStringAsync();
                var assistantId = JsonDocument.Parse(assistantJson).RootElement.GetProperty("id").GetString();

                var threadResp = await _httpClient.PostAsync($"{BaseUrl}/threads",
                    new StringContent("{}", Encoding.UTF8, "application/json"));
                var threadId = JsonDocument.Parse(await threadResp.Content.ReadAsStringAsync())
                    .RootElement.GetProperty("id").GetString();

                var messageBody = new { role = "user", content = userInput };
                await _httpClient.PostAsync($"{BaseUrl}/threads/{threadId}/messages",
                    new StringContent(JsonSerializer.Serialize(messageBody), Encoding.UTF8, "application/json"));

                var runResp = await _httpClient.PostAsync($"{BaseUrl}/threads/{threadId}/runs",
                    new StringContent(JsonSerializer.Serialize(new { assistant_id = assistantId }), Encoding.UTF8, "application/json"));
                var runId = JsonDocument.Parse(await runResp.Content.ReadAsStringAsync())
                    .RootElement.GetProperty("id").GetString();

                string status = "";
                int retry = 0;
                while (status != "completed" && retry < 20)
                {
                    await Task.Delay(1000);
                    var checkResp = await _httpClient.GetAsync($"{BaseUrl}/threads/{threadId}/runs/{runId}");
                    var checkJson = JsonDocument.Parse(await checkResp.Content.ReadAsStringAsync());
                    status = checkJson.RootElement.GetProperty("status").GetString();
                    retry++;
                }

                if (status != "completed")
                    return "❌ Hệ thống phản hồi quá chậm. Vui lòng thử lại sau.";

                var msgResp = await _httpClient.GetAsync($"{BaseUrl}/threads/{threadId}/messages");
                var messagesJson = await msgResp.Content.ReadAsStringAsync();
                var messages = JsonDocument.Parse(messagesJson).RootElement.GetProperty("data");

                return messages[0].GetProperty("content")[0].GetProperty("text").GetProperty("value").GetString()!;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[OpenAI ERROR] " + ex.Message);
                return "❌ Đã xảy ra lỗi khi gọi trợ lý AI.";
            }
        }
    }
}