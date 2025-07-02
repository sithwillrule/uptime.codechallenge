using ClickHouse.Client.ADO;
using Dapper;
using Microsoft.Extensions.Options;
using System.Data;
using System.Threading.Tasks;


namespace uptime.codechallenge.api.Init
{

    public static class ClickHouseServiceCollectionExtensions
    {
        private static readonly string InitFuelSensorIngestion = @"
-- Step 1: Create the destination table for permanent storage.
-- This MergeTree table is optimized for fast queries and long-term storage.
-- We add a PARTITION BY clause, which is crucial for performance on time-series data.
CREATE TABLE IF NOT EXISTS kafka_data_storage
(
    -- The timestamp of the event, used for ordering and partitioning.
    ServerDateTime DateTime64(3, 'Asia/Tehran'),

    -- Example data column.
    AnalogIN1      UInt16
)
ENGINE = MergeTree()
-- Partitioning by month is a common and effective strategy for time-series data.
-- It dramatically speeds up queries that filter by a time range.
PARTITION BY toYYYYMM(ServerDateTime)
-- Ordering by the timestamp is essential for MergeTree performance.
ORDER BY (ServerDateTime);

";
        private static readonly string InitFuelSensorIngestion2 = @"


-- Step 2: Create the Kafka Engine table to consume from the Kafka topic.
-- This table acts as a live, streaming view into the Kafka topic. It does not store data itself.
CREATE TABLE IF NOT EXISTS kafka_data_queue
(
    -- The schema must match the JSON messages in Kafka AND the destination table.
    ServerDateTime DateTime64(3, 'Asia/Tehran'),
    AnalogIN1      UInt16
)
ENGINE = Kafka
SETTINGS
    -- Connection details for the Kafka broker(s).
    kafka_broker_list = 'kafka:29092',

    -- The Kafka topic to consume from.
    kafka_topic_list = 'fuel-sensor-events',

    -- The consumer group name. All consumers with this name will share the load.
    kafka_group_name = 'fuel-sensor-events-fuel-level-micro-2', 

    -- The data format of the messages in the Kafka topic.
    kafka_format = 'JSONEachRow',

    -- Best Practice: Allows a single ClickHouse node to use multiple threads to consume.
    -- Set to 0 to auto-calculate based on `max_threads`.
    kafka_thread_per_consumer = 0,

    -- The number of consumer processes to create on this node.
    kafka_num_consumers = 1,

    -- Best Practice: Skip messages that cannot be parsed instead of stopping the consumer.
    -- This makes the pipeline more robust to bad data.
    kafka_skip_broken_messages = 10;
";

        private static readonly string InitFuelSensorIngestion3 = @"


-- Step 3: Create a Materialized View to automatically move data.
-- This view acts as a persistent trigger. As soon as data arrives in `kafka_data_queue`,
-- this view selects it and inserts it into the `kafka_data_storage` table.
CREATE MATERIALIZED VIEW IF NOT EXISTS kafka_data_mv TO kafka_data_storage
AS SELECT
    ServerDateTime,
    AnalogIN1
FROM
    kafka_data_queue;
";



        /// <summary>
        /// Registers a transient IDbConnection for ClickHouse and associated services.
        /// </summary>
        /// <param name="services">The IServiceCollection to add the services to.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="configurationSectionName">The name of the configuration section to bind from (default is "ClickHouse").</param>
        /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
        public static async Task<IServiceCollection> AddClickHouse(
            this IServiceCollection services,
            IConfiguration configuration,
            string configurationSectionName = ClickHouseOptions.SectionName)
        {
            // 1. Bind the ClickHouseOptions from the configuration file
            services.Configure<ClickHouseOptions>(
                configuration.GetSection(configurationSectionName)
            );

            // 2. Register the IDbConnection with a transient lifetime.
            // This is the safest lifetime for DB connections. A new connection
            // is created for each request, and the underlying connection pooling
            // of the driver handles efficiency.
            services.AddTransient<IDbConnection>(sp =>
            {
                // Resolve the configured options
                var options = sp.GetRequiredService<IOptions<ClickHouseOptions>>().Value;

                // Build the ClickHouse-specific connection string
                var builder = new ClickHouseConnectionStringBuilder
                {
                    Host = options.Host,
                    Port = options.Port,
                    Database = options.Database,
                    Username = options.Username,
                    Password = options.Password,
                    Compression = options.UseCompression
                };

                return new ClickHouseConnection(builder.ToString());
            });
        
                var connection = services.BuildServiceProvider().GetRequiredService<IDbConnection>();

                //Start Ingestion from Kafka for Fuel Sensor
                connection.Execute(InitFuelSensorIngestion);
                connection.Execute(InitFuelSensorIngestion2);
                connection.Execute(InitFuelSensorIngestion3);

       
            

            return services;
        }
    }
}
