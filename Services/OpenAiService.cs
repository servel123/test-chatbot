// File: Services/OpenAiService.cs
using System;
using System.IO;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ChatbotBackend.Services
{
    public class OpenAiService
    {
        private readonly string _apiKeyFilePath = "apikey.txt";
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private const string BaseUrl = "https://api.openai.com/v1";
        private readonly string _assistantFilePath = "assistant_id.txt";
        private readonly string _threadFilePath = "thread_id.txt";

        public OpenAiService(IConfiguration config)
        {
            _apiKey = config["OpenAI:ApiKey"] ?? throw new ArgumentNullException("API key is missing");
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            _httpClient.DefaultRequestHeaders.Add("OpenAI-Beta", "assistants=v2");
        }
       


        /// <summary>
        /// Kiểm tra API key có hợp lệ không bằng cách gọi thử model đơn giản.
        /// </summary>
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
                    $"{BaseUrl}/chat/completions",
                    new StringContent(JsonSerializer.Serialize(testRequest), Encoding.UTF8, "application/json")
                );

                var result = await response.Content.ReadAsStringAsync();
                return result;
            }
            catch (Exception ex)
            {
                return $"Lỗi khi kiểm tra key: {ex.Message}";
            }
        }

        /// <summary>
        /// Tạo assistant 1 lần duy nhất và lưu vào file. Lần sau dùng lại.
        /// </summary>
        private async Task<string> GetOrCreateAssistantIdAsync()
        {
            if (File.Exists(_assistantFilePath))
                return await File.ReadAllTextAsync(_assistantFilePath);

            var assistantBody = new
            {
                name = "",
                instructions = "Bạn là trợ lý AI hữu ích.",
                model = "gpt-4-turbo"
            };

            var assistantResp = await _httpClient.PostAsync($"{BaseUrl}/assistants",
                new StringContent(JsonSerializer.Serialize(assistantBody), Encoding.UTF8, "application/json"));

            var assistantJson = await assistantResp.Content.ReadAsStringAsync();
            var assistantId = JsonDocument.Parse(assistantJson).RootElement.GetProperty("id").GetString();

            if (string.IsNullOrEmpty(assistantId))
                throw new Exception("Assistant ID không hợp lệ.");

            await File.WriteAllTextAsync(_assistantFilePath, assistantId);
            return assistantId;
        }

        /// <summary>
        /// Tạo thread hội thoại 1 lần, lưu vào file để dùng liên tục.
        /// </summary>
        private async Task<string> GetOrCreateThreadIdAsync()
        {
            if (File.Exists(_threadFilePath))
                return await File.ReadAllTextAsync(_threadFilePath);

            var response = await _httpClient.PostAsync($"{BaseUrl}/threads",
                new StringContent("{}", Encoding.UTF8, "application/json"));
            var json = await response.Content.ReadAsStringAsync();
            var threadId = JsonDocument.Parse(json).RootElement.GetProperty("id").GetString();

            if (string.IsNullOrEmpty(threadId))
                throw new Exception("Thread ID không hợp lệ.");

            await File.WriteAllTextAsync(_threadFilePath, threadId);
            return threadId;
        }

        //đối chiếu apikey sau mỗi đoạn chat
        public async Task EnsureValidAssistantFileAsync()
        {
            if (File.Exists(_apiKeyFilePath))
            {
                var storedKey = await File.ReadAllTextAsync(_apiKeyFilePath);
                if (storedKey.Trim() != _apiKey.Trim() && File.Exists(_assistantFilePath))
                {
                    File.Delete(_assistantFilePath);
                    if (File.Exists(_threadFilePath)) File.Delete(_threadFilePath);
                }
            }
            await File.WriteAllTextAsync(_apiKeyFilePath, _apiKey);
        }

        /// <summary>
        /// Gửi prompt người dùng và lấy phản hồi từ Assistant. Dùng lại assistant và thread hiện có.
        /// Nếu người dùng gõ "tạm biệt", hệ thống sẽ xoá thread và kết thúc hội thoại.
        /// </summary>
        public async Task<string> GetAssistantResponseAsync(string userInput)
        {
            try
            {
                var assistantId = await GetOrCreateAssistantIdAsync();
                var threadId = await GetOrCreateThreadIdAsync();

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
                    return "⚠️ Phản hồi quá chậm, vui lòng thử lại.";

                var msgResp = await _httpClient.GetAsync($"{BaseUrl}/threads/{threadId}/messages");
                var messagesJson = await msgResp.Content.ReadAsStringAsync();

                Console.WriteLine("[DEBUG] Raw messagesJson: ");
                Console.WriteLine(messagesJson);

                try
                {
                    var messages = JsonDocument.Parse(messagesJson).RootElement.GetProperty("data");
                    var content = messages[0].GetProperty("content");

                    if (content.GetArrayLength() > 0 &&
                        content[0].TryGetProperty("text", out var textObj) &&
                        textObj.TryGetProperty("value", out var valueObj))
                    {
                         var rawText = valueObj.GetString();
                         var reply = Regex.Replace(rawText ?? "", @"[【\[][^【\[\]]*?†[^】\[\]]*?[】\]]", "").Trim();

                        if (userInput.Trim().ToLower() == "tạm biệt")
                        {
                            File.Delete(_threadFilePath);
                            reply += "\n👋 Cuộc trò chuyện đã kết thúc.";
                        }

                        return reply ?? "⚠️ Trợ lý không có phản hồi.";
                    }

                    return "⚠️ Không có nội dung phản hồi hợp lệ.";
                }
                catch (Exception jsonEx)
                {
                    return $"❌ Lỗi khi phân tích JSON phản hồi: {jsonEx.Message}";
                }
            }
            catch (Exception ex)
            {
                return $"❌ Lỗi hệ thống: {ex.Message}";
            }
        }
    }
}
