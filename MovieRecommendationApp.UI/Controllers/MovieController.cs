using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MovieRecommendationApp.BLL.Models;
using System.Collections.Generic;

namespace MovieRecommendationApp.UI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MovieController : ControllerBase
    {
        private readonly ILogger<MovieController> logger;

        public MovieController(ILogger<MovieController> logger)
        {
            this.logger = logger;
        }

        [HttpGet]
        public ActionResult<IEnumerable<MovieModel>> Get()
        {
            return Ok(new List<MovieModel>());
        }
    }
}
