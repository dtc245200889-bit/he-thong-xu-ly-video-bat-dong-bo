using MediaApi.Data;
using MediaApi.Messaging;
using MediaApi.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http.Features;
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
builder.Services.AddControllers();

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
app.MapControllers();

app.Run();
