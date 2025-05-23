using Microsoft.AspNetCore.Mvc;
using ChatbotBackend.Services;
using System.Threading.Tasks;

namespace ChatbotBackend.Controller
{
    [ApiController]
    [Route("api/[controller]")]
    public class OpenAiController : ControllerBase
    {
        private readonly OpenAiService _openAi;

        public OpenAiController(OpenAiService openAi)
        {
            _openAi = openAi;
        }

        [HttpPost("ask")]
        public async Task<IActionResult> Ask([FromBody] ChatRequest request)
        {
            var response = await _openAi.GetAssistantResponseAsync(request.Message);
            return Ok(new { reply = response });
        }

        [HttpGet("check")]
        public async Task<IActionResult> CheckKey()
        {
            var result = await _openAi.CheckApiKeyAsync();
            return Ok(new { result });
        }

        public class ChatRequest
        {
            public string Message { get; set; }
        }
    }
}
