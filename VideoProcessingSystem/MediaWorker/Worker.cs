using MediaWorker.Messaging;

namespace MediaWorker;

public sealed class Worker(
    RabbitMqConsumer consumer,
    ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Media Worker is starting");
        await consumer.ConsumeAsync(stoppingToken);
    }
}
