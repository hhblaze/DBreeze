#if NET6FUNC
namespace DBreeze.HNSW
{
    internal interface IProgressReporter
    {
        void Progress(int current, int total);
    }
}
#endif