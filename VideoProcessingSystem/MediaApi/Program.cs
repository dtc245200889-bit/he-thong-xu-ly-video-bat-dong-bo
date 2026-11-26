using MediaApi.Data;
using MediaApi.Messaging;
using MediaApi.Models;
using MediaApi.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = VideoService.MaximumUploadBytes;
});
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = VideoService.MaximumUploadBytes;
});

var storageRoot = Path.GetFullPath(
    builder.Configuration["Storage:RootPath"] ?? "../storage",
    builder.Environment.ContentRootPath);
var frontendRoot = Path.GetFullPath(
    builder.Configuration["Frontend:RootPath"] ?? "../frontend",
    builder.Environment.ContentRootPath);
Directory.CreateDirectory(Path.Combine(storageRoot, "uploads"));
Directory.CreateDirectory(Path.Combine(storageRoot, "outputs"));

var databasePath = Path.Combine(storageRoot, "media.db");
var connectionString = new SqliteConnectionStringBuilder
{
    DataSource = databasePath,
    ForeignKeys = true
}.ToString();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(connectionString));
builder.Services.AddScoped<VideoService>();
builder.Services.AddScoped<JobService>();
builder.Services.AddSingleton<RabbitMqPublisher>();
builder.Services
    .AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var message = string.Join(
                " ",
                context.ModelState.Values
                    .SelectMany(value => value.Errors)
                    .Select(error => error.ErrorMessage)
                    .Where(error => !string.IsNullOrWhiteSpace(error))
                    .Distinct());

            if (string.IsNullOrWhiteSpace(message))
            {
                message = "Dữ liệu yêu cầu không hợp lệ.";
            }

            return new BadRequestObjectResult(new ApiError("INVALID_REQUEST", message));
        };
    });

var app = builder.Build();
var frontendFileProvider = new PhysicalFileProvider(frontendRoot);

await using (var scope = app.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await database.Database.EnsureCreatedAsync();
}

app.UseDefaultFiles(new DefaultFilesOptions
{
    FileProvider = frontendFileProvider
});
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = frontendFileProvider
});
app.UseStatusCodePages(async statusContext =>
{
    var response = statusContext.HttpContext.Response;
    var error = response.StatusCode switch
    {
        StatusCodes.Status404NotFound =>
            new ApiError("ROUTE_NOT_FOUND", "Không tìm thấy tài nguyên hoặc endpoint."),
        StatusCodes.Status405MethodNotAllowed =>
            new ApiError("METHOD_NOT_ALLOWED", "Phương thức HTTP không được hỗ trợ cho endpoint này."),
        StatusCodes.Status413PayloadTooLarge =>
            new ApiError("PAYLOAD_TOO_LARGE", "Nội dung yêu cầu vượt quá kích thước cho phép."),
        StatusCodes.Status415UnsupportedMediaType =>
            new ApiError("UNSUPPORTED_MEDIA_TYPE", "Kiểu nội dung của yêu cầu không được hỗ trợ."),
        _ => new ApiError("HTTP_ERROR", $"Yêu cầu thất bại với mã HTTP {response.StatusCode}.")
    };

    await response.WriteAsJsonAsync(error);
});
app.MapControllers();

app.Run();
