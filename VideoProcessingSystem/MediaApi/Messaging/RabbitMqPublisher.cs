using System.Net.Sockets;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace MediaApi.Messaging;

public sealed class RabbitMqPublisher : IDisposable
{
    private readonly ConnectionFactory _factory;
    private readonly string _queueName;
    private readonly object _sync = new();
    private IConnection? _connection;
    private IModel? _channel;

    public RabbitMqPublisher(IConfiguration configuration)
    {
        var settings = configuration.GetSection("RabbitMq");
        _queueName = settings["QueueName"] ?? "video-processing";
        _factory = new ConnectionFactory
        {
            HostName = settings["HostName"] ?? "localhost",
            Port = settings.GetValue("Port", 5672),
            UserName = settings["UserName"] ?? "video_demo",
            Password = settings["Password"] ?? "video_demo_password",
            AutomaticRecoveryEnabled = true
        };
    }

    public void Publish(VideoJobMessage message)
    {
        lock (_sync)
        {
            try
            {
                EnsureChannel();

                var properties = _channel!.CreateBasicProperties();
                properties.Persistent = true;
                var body = JsonSerializer.SerializeToUtf8Bytes(message);

                _channel.BasicPublish(
                    exchange: string.Empty,
                    routingKey: _queueName,
                    mandatory: true,
                    basicProperties: properties,
                    body: body);
                _channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex) when (
                ex is BrokerUnreachableException
                    or AlreadyClosedException
                    or OperationInterruptedException
                    or TimeoutException
                    or IOException
                    or SocketException)
            {
                ResetChannel();
                throw new RabbitMqPublishException(
                    "RabbitMQ did not confirm the job message.",
                    ex);
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            ResetChannel();
        }
    }

    private void EnsureChannel()
    {
        if (_connection?.IsOpen == true && _channel?.IsOpen == true)
        {
            return;
        }

        ResetChannel();
        _connection = _factory.CreateConnection();
        _channel = _connection.CreateModel();
        _channel.QueueDeclare(
            queue: _queueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);
        _channel.ConfirmSelect();
    }

    private void ResetChannel()
    {
        _channel?.Dispose();
        _connection?.Dispose();
        _channel = null;
        _connection = null;
    }
}

public sealed record VideoJobMessage(Guid JobId, Guid VideoId, string TaskType);

public sealed class RabbitMqPublishException(string message, Exception innerException)
    : Exception(message, innerException);
