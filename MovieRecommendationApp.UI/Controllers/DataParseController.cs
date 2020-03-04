using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MovieRecommendationApp.BLL.Services;
using System.Threading.Tasks;

namespace MovieRecommendationApp.UI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DataParseController : ControllerBase
    {
        private readonly ILogger<DataParseController> logger;
        private readonly IDataParserService dataParserService;

        public DataParseController(ILogger<DataParseController> logger, IDataParserService dataParserService)
        {
            this.logger = logger;
            this.dataParserService = dataParserService;
        }

        [HttpGet]
        public async Task<ActionResult> StartParse()
        {
            await dataParserService.ParseData();
            return Ok();
        }
    }
}
