#if KNNSearch
namespace DBreeze.HNSW
{
    public interface IProgressReporter
    {
        void Progress(int current, int total);
    }
}
#endif