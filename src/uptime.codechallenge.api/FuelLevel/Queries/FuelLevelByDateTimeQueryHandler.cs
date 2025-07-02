using ClickHouse.Client.ADO;
using Dapper;
using System.Data;
using System.Globalization;

namespace uptime.codechallenge.api.FuelLevel.Queries
{
    public class FuelLevelByDateTimeQueryHandler
    {
        private readonly IDbConnection _clickHouseConnection;

        public readonly string FuelLevelByDateTimeQueryString = @"
-- Use a CTE to get and interpolate all data for the target day
WITH InterpolatedAndFilteredData AS (
    SELECT
        ServerDateTime,
        
        -- This CASE statement performs the piece-wise linear interpolation.
        CASE
            WHEN AnalogIN1 >= 5836 THEN 0
            WHEN AnalogIN1 > 4464 THEN 0 + (AnalogIN1 - 5836) * (20 - 0) / (4464 - 5836)
            WHEN AnalogIN1 > 2561 THEN 20 + (AnalogIN1 - 4464) * (40 - 20) / (2561 - 4464)
            WHEN AnalogIN1 > 477 THEN 40 + (AnalogIN1 - 2561) * (70 - 40) / (477 - 2561)
            ELSE 70
        END AS InterpolatedLiters
        
    FROM `default`.kafka_data_mv
    -- KEY CHANGE 1: Select all data for the entire day of your target timestamp
    -- This provides the full context for the exponential average calculation.
    WHERE toDate(ServerDateTime) = toDate(@TargetDateTime)
),

-- Use a second CTE to calculate the exponential average over the entire day's data
SmoothedData AS (
    SELECT           
        ServerDateTime,
        -- KEY CHANGE 2: The exponential average is now calculated over all points in the day,
        -- giving a progressively smoothed value at each timestamp.
        round(
            exponentialTimeDecayedAvg(3600)(InterpolatedLiters, ServerDateTime) OVER (ORDER BY ServerDateTime), 
            2
        ) AS FuelLevel
    FROM InterpolatedAndFilteredData
)

-- Final step: Select the single, closest datapoint you need from the smoothed results
SELECT 
  
    FuelLevel
FROM SmoothedData
-- KEY CHANGE 3: Filter for the first available datapoint AT or AFTER your target time
WHERE ServerDateTime >= @TargetDateTime
ORDER BY ServerDateTime ASC
LIMIT 1;
";


        public FuelLevelByDateTimeQueryHandler(IDbConnection clickHouseConnection)
        {
            _clickHouseConnection = clickHouseConnection;
        }
        public async Task<FuelLevelByDateTimeQueryResponse> HandleAsync(FuelLevelByDateTimeQueryRequest queryRequest)
        {
            var fuelLevelByDateTimeQueryResponse = await _clickHouseConnection.QueryAsync<FuelLevelByDateTimeQueryResponse>(FuelLevelByDateTimeQueryString, new { TargetDateTime = queryRequest.TargetDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) });

            if(fuelLevelByDateTimeQueryResponse.Any())
            return fuelLevelByDateTimeQueryResponse.First();
            return new FuelLevelByDateTimeQueryResponse() { Message = "No Valid Data Available" };
        }
    }
}
