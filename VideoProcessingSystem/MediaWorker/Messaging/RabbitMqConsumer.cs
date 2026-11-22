using System.Text.Json;
using MediaWorker.Models;
using MediaWorker.Services;
using Microsoft.Data.Sqlite;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace MediaWorker.Messaging;

public sealed class RabbitMqConsumer(
    IConfiguration configuration,
    VideoProcessingService processingService,
    ILogger<RabbitMqConsumer> logger)
{
    public async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        var settings = configuration.GetSection("RabbitMq");
        var queueName = settings["QueueName"] ?? "video-processing";
        var factory = new ConnectionFactory
        {
            HostName = settings["HostName"] ?? "localhost",
            Port = settings.GetValue("Port", 5672),
            UserName = settings["UserName"] ?? "video_demo",
            Password = settings["Password"] ?? "video_demo_password",
            DispatchConsumersAsync = true,
            AutomaticRecoveryEnabled = true
        };

        while (!stoppingToken.IsCancellationRequested)
        {
            IConnection? connection = null;
            IModel? channel = null;

            try
            {
                connection = factory.CreateConnection();
                channel = connection.CreateModel();
                channel.QueueDeclare(
                    queue: queueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: null);
                channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.Received += (_, eventArgs) =>
                    HandleDeliveryAsync(channel, eventArgs, stoppingToken);
                channel.BasicConsume(queue: queueName, autoAck: false, consumer: consumer);

                logger.LogInformation("Consuming RabbitMQ queue {QueueName}", queueName);
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (BrokerUnreachableException ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "RabbitMQ is unavailable; retrying in five seconds");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            finally
            {
                channel?.Dispose();
                connection?.Dispose();
            }
        }
    }

    private async Task HandleDeliveryAsync(
        IModel channel,
        BasicDeliverEventArgs eventArgs,
        CancellationToken stoppingToken)
    {
        VideoJobMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<VideoJobMessage>(
                eventArgs.Body.Span,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Discarding malformed message from the video queue");
            channel.BasicNack(eventArgs.DeliveryTag, multiple: false, requeue: false);
            return;
        }

        if (message is null)
        {
            logger.LogError("Discarding empty message from the video queue");
            channel.BasicNack(eventArgs.DeliveryTag, multiple: false, requeue: false);
            return;
        }

        try
        {
            await processingService.ProcessAsync(message, stoppingToken);
            channel.BasicAck(eventArgs.DeliveryTag, multiple: false);
        }
        catch (SqliteException ex)
        {
            logger.LogError(ex, "Database failed for job {JobId}; requeueing", message.JobId);
            channel.BasicNack(eventArgs.DeliveryTag, multiple: false, requeue: true);
        }
        catch (IOException ex)
        {
            await FailAndAcknowledgeAsync(channel, message.JobId, ex.Message, eventArgs.DeliveryTag);
        }
        catch (UnauthorizedAccessException ex)
        {
            await FailAndAcknowledgeAsync(channel, message.JobId, ex.Message, eventArgs.DeliveryTag);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            channel.BasicNack(eventArgs.DeliveryTag, multiple: false, requeue: true);
        }
    }

    private async Task FailAndAcknowledgeAsync(
        IModel channel,
        Guid jobId,
        string errorMessage,
        ulong deliveryTag)
    {
        try
        {
            await processingService.MarkFailedAsync(jobId, errorMessage, CancellationToken.None);
            logger.LogError("Job {JobId} failed: {ErrorMessage}", jobId, errorMessage);
            channel.BasicAck(deliveryTag, multiple: false);
        }
        catch (SqliteException ex)
        {
            logger.LogError(ex, "Could not persist failure for job {JobId}; requeueing", jobId);
            channel.BasicNack(deliveryTag, multiple: false, requeue: true);
        }
    }
}
