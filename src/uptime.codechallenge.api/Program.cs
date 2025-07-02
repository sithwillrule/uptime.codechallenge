
using ClickHouse.Client.ADO;
using ClosedXML.Excel;
using Confluent.Kafka;
using DocumentFormat.OpenXml.ExtendedProperties;
using Newtonsoft.Json;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using uptime.codechallenge.api.FuelLevel.Queries;
using uptime.codechallenge.api.Init;

namespace uptime.codechallenge.api
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var baseDir = AppContext.BaseDirectory;
            var loadedFuel = "loadedFuel";
            string LoadedFuel = Path.Combine(baseDir, loadedFuel);

            //this task will load the excel to kafka 
            if (!File.Exists(LoadedFuel))
            Task.Run( async () => {
            

                string filePath = Path.Combine(baseDir, "fuel.xlsx");             
                
                var stopwatch = Stopwatch.StartNew();

                // Read data from Excel
                Console.WriteLine("Reading data from Excel file...");
                List<SensorData> data = ReadExcelData(filePath);
                stopwatch.Stop();
                Console.WriteLine($"Read {data.Count:N0} records from Excel in {stopwatch.Elapsed.TotalSeconds:F2} seconds.");

                // Send data to Kafka
                stopwatch.Restart();
                await SendToKafkaInBatchesAsync(data, "fuel-sensor-events", 100000);
                File.Create(LoadedFuel);
                stopwatch.Stop();
                Console.WriteLine($"\nDispatch to Kafka completed in {stopwatch.Elapsed.TotalSeconds:F2} seconds.");

            }).Wait();
           


                var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddClickHouse(builder.Configuration);
            builder.Services.AddScoped<FuelLevelByDateTimeQueryHandler>(sp => new FuelLevelByDateTimeQueryHandler(sp.GetRequiredService<IDbConnection>()));
            builder.Services.AddControllers();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            app.MapControllers();

            app.Run();
        }







        public class SensorData
        {
            public string ServerDateTime { get; set; }
            public Int16 AnalogIN1 { get; set; }
        }

        static List<SensorData> ReadExcelData(string filePath)
        { 
            List<SensorData> data = new List<SensorData>();

            using (var workbook = new XLWorkbook(filePath))
            {
                Console.WriteLine("File is loaded to memory and getting proccesed");
                var worksheet = workbook.Worksheet("Result 1");
                if (!worksheet.RowsUsed().Any()) return data;

                var headerRow = worksheet.Row(1);
                int serverDateTimeColIndex = headerRow.Cells().FirstOrDefault(c => c.Value.ToString() == "ServerDateTime")?.Address.ColumnNumber ?? -1;
                int analogIn1ColIndex = headerRow.Cells().FirstOrDefault(c => c.Value.ToString() == "AnalogIN1")?.Address.ColumnNumber ?? -1;

                if (serverDateTimeColIndex == -1 || analogIn1ColIndex == -1)
                    throw new Exception("Required columns 'ServerDateTime' or 'AnalogIN1' not found in the Excel file.");

                var dataRows = worksheet.RowsUsed().Skip(1); // Skip header row

                foreach (var currentRow in dataRows)
                {
                    try
                    {
                        var dateTimeCell = currentRow.Cell(serverDateTimeColIndex);
                        var analogCell = currentRow.Cell(analogIn1ColIndex);

                        if (dateTimeCell.IsEmpty() || analogCell.IsEmpty()) continue;

                        DateTime dateTime;
                        if (dateTimeCell.DataType == XLDataType.DateTime)
                        {
                            dateTime = dateTimeCell.GetDateTime();
                        }
                        else if (!DateTime.TryParse(dateTimeCell.Value.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out dateTime))
                        {
                            Console.WriteLine($"Warning: Could not parse date '{dateTimeCell.Value}' at row {currentRow.RowNumber()}");
                            continue;
                        }

                        if (!Int16.TryParse(analogCell.Value.ToString(), out Int16 analogValue))
                        {
                            Console.WriteLine($"Warning: Could not parse value '{analogCell.Value}' at row {currentRow.RowNumber()}");
                            continue;
                        }

                        data.Add(new SensorData
                        {
                            ServerDateTime = dateTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                            AnalogIN1 = analogValue
                        });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error processing row {currentRow.RowNumber()}: {ex.Message}");
                    }
                }
            }
            return data;
        }


        static async Task
            SendToKafkaInBatchesAsync(List<SensorData> data, string topic, int batchSize)
        {
            var config = new ProducerConfig
            {
                BootstrapServers = "localhost:9092",
                LingerMs = 5,
                CompressionType = CompressionType.Snappy,
                Acks = Acks.Leader
            };

            long totalSentCount = 0;
            long errorCount = 0;

            using (var producer = new ProducerBuilder<Null, string>(config).Build())
            {
                Console.WriteLine($"\nStarting dispatch of {data.Count:N0} records in batches of {batchSize:N0}...");

           
                Action<DeliveryReport<Null, string>> deliveryHandler = r =>
                {
                    if (r.Error.Code != ErrorCode.NoError)
                    {
                        Console.WriteLine($"\n[ERROR] Failed to deliver message: {r.Error.Reason}");
                        Interlocked.Increment(ref errorCount);
                    }
                };

                for (int i = 0; i < data.Count; i += batchSize)
                {
                    var batch = data.Skip(i).Take(batchSize);

                    foreach (var item in batch)
                    {
                        string json = JsonConvert.SerializeObject(item);

               
                        producer.Produce(topic, new Message<Null, string> { Value = json }, deliveryHandler);
                    }

                    totalSentCount += batch.Count();

             
                    Console.Write($"\rDispatched {totalSentCount:N0} / {data.Count:N0} records. Current Errors: {errorCount}");
                    producer.Flush(TimeSpan.FromSeconds(30)); // A generous timeout for the flush operation
                }

                Console.WriteLine("\nFlushing final messages...");
                var remaining = producer.Flush(TimeSpan.FromSeconds(30));
                Console.WriteLine($"{remaining} messages were still in-flight before final flush.");

                Console.WriteLine($"\nDispatch summary: Total Sent: {totalSentCount:N0}, Total Errors: {errorCount}");
            }
        }



    }

}
