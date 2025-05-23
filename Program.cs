using ChatbotBackend.Services;

var builder = WebApplication.CreateBuilder(args);

// Đăng ký service
builder.Services.AddSingleton<OpenAiService>();

// Add các thành phần Web API
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Kiểm tra nếu đổi API key thì xóa assistant ID
using (var scope = app.Services.CreateScope())
{
    var openAi = scope.ServiceProvider.GetRequiredService<OpenAiService>();
    await openAi.EnsureValidAssistantFileAsync();
}

// Swagger UI
if (app.Environment.IsDevelopment() || app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();
app.MapControllers();
app.Run();
