using System.Threading.Tasks;

namespace MovieRecommendationApp.BLL.Services
{
    public interface IDataParserService
    {
        Task ParseData();

        Task GenerateCreditsGenresKeywordsCastSimilarityMatrix();

        Task SyncSimilarityMatrix();
        Task ComputeClasters();
        Task LoadClastersFromFile();
    }
}