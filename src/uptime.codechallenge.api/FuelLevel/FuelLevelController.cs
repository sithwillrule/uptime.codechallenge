using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks.Dataflow;
using uptime.codechallenge.api.FuelLevel.Queries;

namespace uptime.codechallenge.api.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class FuelLevelController : ControllerBase
    {

        private readonly ILogger<FuelLevelController> _logger;
        private readonly FuelLevelByDateTimeQueryHandler  _fuelLevelByDateTimeQueryHandler;

        public FuelLevelController(ILogger<FuelLevelController> logger, FuelLevelByDateTimeQueryHandler fuelLevelByDateTimeQueryHandler)
        {
            _logger = logger;
            _fuelLevelByDateTimeQueryHandler = fuelLevelByDateTimeQueryHandler;
        }

        [HttpGet(Name = "ByDateTime")]
        public async Task<FuelLevelByDateTimeQueryResponse> GetByDateTimeAsync([FromQuery]FuelLevelByDateTimeQueryRequest queryRequest)
        {
            return await _fuelLevelByDateTimeQueryHandler.HandleAsync(queryRequest);
        }
    }
}
