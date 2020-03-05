﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MovieRecommendationApp.BLL.Models;
using MovieRecommendationApp.BLL.Services;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MovieRecommendationApp.UI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MovieController : ControllerBase
    {
        private readonly ILogger<MovieController> logger;
        private readonly IMovieService movieService;

        public MovieController(ILogger<MovieController> logger,
                               IMovieService movieService)
        {
            this.logger = logger;
            this.movieService = movieService;
        }

        [HttpGet]
        public async Task<ActionResult<ListData<MovieModel>>> Search([FromQuery] MovieSearchFilter filter)
        {
            var res = await movieService.Search(filter);
            return Ok(res);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<MovieModel>> Get(int id)
        {
            var res = await movieService.Get(id);
            return Ok(res);
        }

        [HttpGet("recommendations/{id}")]
        public async Task<ActionResult<List<MovieModel>>> GetRecommendations(int id)
        {
            var res = await movieService.GetRecommendations(id);
            return Ok(res);
        }
    }
}
