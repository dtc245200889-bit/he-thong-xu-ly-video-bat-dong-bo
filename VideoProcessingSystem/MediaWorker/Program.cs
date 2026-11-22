using MediaWorker.Messaging;
using MediaWorker.Services;
using MediaWorker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton<VideoProcessingService>();
builder.Services.AddSingleton<RabbitMqConsumer>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
