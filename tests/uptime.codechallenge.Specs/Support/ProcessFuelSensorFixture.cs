using Confluent.Kafka;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using System.ComponentModel;
using System.Net;
using System.Net.Http;
using Xunit;
using IContainer = DotNet.Testcontainers.Containers.IContainer;

public sealed class ProcessFuelSensorFixture : IAsyncLifetime
{
    private readonly INetwork _network;
    private readonly IContainer _clickhouseContainer;
    private readonly IContainer _kafkaContainer;

    public ProcessFuelSensorFixture()
    {
        // 1. Create a network corresponding to 'data-network' in the docker-compose file.
        // This allows containers to communicate with each other by their hostname.
        _network = new NetworkBuilder()
            .WithName("data-network")
            .Build();

        // 2. Define the ClickHouse container
        _clickhouseContainer = new DotNet.Testcontainers.Builders.ContainerBuilder()
            .WithImage("clickhouse/clickhouse-server:24.1.5.6-alpine")
            .WithName("clickhouse-test") // Testcontainers adds a random suffix to avoid conflicts
            .WithHostname("clickhouse") // This is the name used for inter-container communication
            .WithNetwork(_network)
            .WithPortBinding(8123, true) // Bind to a random host port
            .WithPortBinding(9000, true) // Native protocol
            .WithEnvironment("CLICKHOUSE_DB", "default")
            .WithEnvironment("CLICKHOUSE_USER", "default")
            .WithEnvironment("CLICKHOUSE_DEFAULT_ACCESS_MANAGEMENT", "1")
            .WithEnvironment("CLICKHOUSE_PASSWORD", "clickhouse123")
            // Note: 'ulimits' is a Docker host configuration and not directly supported
            // by the Docker Engine API that Testcontainers uses. For most test scenarios,
            // default limits are sufficient.
            .WithWaitStrategy(
                // This wait strategy replicates the healthcheck from the docker-compose file.
                Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(r => r.ForPort(8123).ForPath("/ping"))                    
            )
            .Build();

        // 3. Define the Kafka container
        _kafkaContainer = new DotNet.Testcontainers.Builders.ContainerBuilder()
            .WithImage("confluentinc/cp-kafka:7.6.0")
            .WithName("kafka-test")
            .WithHostname("kafka")
            .WithNetwork(_network)
            .WithPortBinding(9092, true) // The main port tests will connect to from the host
            .WithExposedPort(9093)       // Expose internal controller port without binding to host
            .WithEnvironment("KAFKA_NODE_ID", "1")
            .WithEnvironment("KAFKA_PROCESS_ROLES", "broker,controller")
            .WithEnvironment("KAFKA_CONTROLLER_QUORUM_VOTERS", "1@kafka:29093")
            .WithEnvironment("KAFKA_LISTENERS", "PLAINTEXT://kafka:29092,CONTROLLER://kafka:29093,PLAINTEXT_HOST://0.0.0.0:9092")
            .WithEnvironment("KAFKA_ADVERTISED_LISTENERS", "PLAINTEXT://kafka:29092,PLAINTEXT_HOST://localhost:9092") // See note below
            .WithEnvironment("KAFKA_LISTENER_SECURITY_PROTOCOL_MAP", "CONTROLLER:PLAINTEXT,PLAINTEXT:PLAINTEXT,PLAINTEXT_HOST:PLAINTEXT")
            .WithEnvironment("KAFKA_CONTROLLER_LISTENER_NAMES", "CONTROLLER")
            .WithEnvironment("KAFKA_INTER_BROKER_LISTENER_NAME", "PLAINTEXT")
            .WithEnvironment("CLUSTER_ID", "MkU3OEVBNTcwNTJENDM2Qk")
            .WithEnvironment("KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR", "1")
            .WithEnvironment("KAFKA_GROUP_INITIAL_REBALANCE_DELAY_MS", "0")
            .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_MIN_ISR", "1")
            .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR", "1")
            .WithEnvironment("KAFKA_LOG_DIRS", "/var/lib/kafka/data")
            .WithEnvironment("KAFKA_NUM_PARTITIONS", "1")
            .WithCommand(
                "bash",
                "-c",
                // This command initializes Kafka storage if it's the first run, then starts the server.
                "if [ ! -f /var/lib/kafka/data/meta.properties ]; then echo 'Formatting Kafka storage...' && /bin/kafka-storage format -t $${CLUSTER_ID} -c /etc/kafka/kafka.properties --ignore-formatted; fi && /etc/confluent/docker/run"
            )
            .WithWaitStrategy(
                // Replicates the `CMD-SHELL` healthcheck. It waits until the broker is responsive.
                Wait.ForUnixContainer()
                    .UntilCommandIsCompleted(
                        "kafka-broker-api-versions",
                        "--bootstrap-server",
                        "localhost:9092" // Inside the container, it connects to itself via localhost
                    )                    
            )
            .Build();
    }

    // Public properties to access container details in tests
    public string KafkaBootstrapServers => $"{_kafkaContainer.Hostname}:{_kafkaContainer.GetMappedPublicPort(9092)}";
    public string ClickHouseConnectionString => $"Host={_clickhouseContainer.Hostname};Port={_clickhouseContainer.GetMappedPublicPort(9000)};User=default;Password=clickhouse123";
    public string ClickHouseHttpUrl => $"http://{_clickhouseContainer.Hostname}:{_clickhouseContainer.GetMappedPublicPort(8123)}";


    public async Task InitializeAsync()
    {
        // Start all components in parallel for faster test setup

        try
        {
            await _network.CreateAsync();

            await _clickhouseContainer.StartAsync();
            await _kafkaContainer.StartAsync();

        }
        catch (Exception)
        {

            throw;
        }
        
        await Task.WhenAll(
          
        );

        // This is a dynamic update of Kafka's advertised listeners. It tells Kafka how to be
        // reached from the outside world (our test host) using the dynamically assigned port.
        var execResult = await _kafkaContainer.ExecAsync( new List<string>() {
            "kafka-configs",
            "--bootstrap-server", "localhost:9092",
            "--alter",
            "--broker", "1",
            "--add-config", $"advertised.listeners=[PLAINTEXT_HOST://{_kafkaContainer.Hostname}:{_kafkaContainer.GetMappedPublicPort(9092)}]"}
        );
    }

    public async Task DisposeAsync()
    {
        await Task.WhenAll(
            _clickhouseContainer.DisposeAsync().AsTask(),
            _kafkaContainer.DisposeAsync().AsTask()
        );
        await _network.DisposeAsync();
    }
}

