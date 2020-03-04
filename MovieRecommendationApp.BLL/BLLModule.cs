using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MovieRecommendationApp.BLL.Services;
using MovieRecommendationApp.DAL;
using System;
using System.Collections.Generic;
using System.Text;

namespace MovieRecommendationApp.BLL
{
    public static class BLLModule
    {
        public static void Load(IServiceCollection services, IConfiguration configuration)
        {
            services.AddTransient<IDataParserService, DataParserService>();

            DALModule.Load(services, configuration);
        }
    }
}
