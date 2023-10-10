#if KNNSearch
namespace DBreeze.HNSW
{
    internal interface IProgressReporter
    {
        void Progress(int current, int total);
    }
}
#endif