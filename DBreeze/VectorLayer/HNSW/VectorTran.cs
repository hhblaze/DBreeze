#if NET6FUNC || NET472
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static DBreeze.Transactions.Transaction;

namespace DBreeze.HNSW
{
    internal class VectorTran      
    {

        Dictionary<string, SmallWorld<float[], float>.Composer> dF = new Dictionary<string, SmallWorld<float[], float>.Composer>();
        Dictionary<string, SmallWorld<double[], double>.Composer> dD = new Dictionary<string, SmallWorld<double[], double>.Composer>();

        Transactions.Transaction tran;

        public VectorTran(Transactions.Transaction tran)
        {
            this.tran = tran;
        }

        public void BeforeComit()
        {
            foreach (var el in dF)
                el.Value.Flush();
            foreach (var el in dD)
                el.Value.Flush();
        }

        public SmallWorld<float[], float>.Composer InitForTableF<TVector>(string tableName, VectorTableParameters<float[]> vectorTableParameters = null)
        {//executed from the lock
            if (!dF.TryGetValue(tableName, out var graph))
            {
                SmallWorld<float[], float>.SmallWorldStorageF storage = new SmallWorld<float[], float>.SmallWorldStorageF();
                storage.tran = this.tran;
                storage.TableName = tableName;

                var parameters = new SmallWorld<float[], float>.Parameters();
                parameters.M = 15;
                parameters.LevelLambda = 1 / Math.Log(parameters.M);

                parameters.NeighbourHeuristic = SmallWorld<float[], float>.NeighbourSelectionHeuristic.SelectSimple;

                if (vectorTableParameters != null)
                {
                    graph.MaxItemsInBucket = vectorTableParameters.BucketSize;
                    switch (vectorTableParameters.NeighbourSelection)
                    {
                        case VectorTableParameters<float[]>.eNeighbourSelectionHeuristic.NeighbourSelectSimple:
                            graph._parameters.NeighbourHeuristic = SmallWorld<float[], float>.NeighbourSelectionHeuristic.SelectSimple;
                            break;
                        case VectorTableParameters<float[]>.eNeighbourSelectionHeuristic.NeighbourSelectionHeuristic:
                            graph._parameters.NeighbourHeuristic = SmallWorld<float[], float>.NeighbourSelectionHeuristic.SelectHeuristic;
                            break;
                    }
                }
                parameters.Storage = storage;

                graph = new SmallWorld<float[], float>.Composer(parameters, instanceQuantity: vectorTableParameters?.QuantityOfLogicalProcessorToCompute ?? 0,
                    GetVectorbyExternalId: vectorTableParameters?.GetItem ?? null);


                dF[tableName] = graph;
            }


            return graph;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TVector"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="vectorTableParameters"></param>
        public SmallWorld<double[], double>.Composer InitForTableD<TVector>(string tableName, VectorTableParameters<double[]> vectorTableParameters = null)
        {//executed from the lock

            if (!dD.TryGetValue(tableName, out var graph))
            {
                SmallWorld<double[], double>.SmallWorldStorageD storage = new SmallWorld<double[], double>.SmallWorldStorageD();
                storage.tran = this.tran;
                storage.TableName = tableName;

                var parameters = new SmallWorld<double[], double>.Parameters();
                parameters.M = 15;
                parameters.LevelLambda = 1 / Math.Log(parameters.M);
                parameters.NeighbourHeuristic = SmallWorld<double[], double>.NeighbourSelectionHeuristic.SelectSimple;

                if (vectorTableParameters != null)
                {
                    graph.MaxItemsInBucket = vectorTableParameters.BucketSize;
                    switch (vectorTableParameters.NeighbourSelection)
                    {
                        case VectorTableParameters<double[]>.eNeighbourSelectionHeuristic.NeighbourSelectSimple:
                            graph._parameters.NeighbourHeuristic = SmallWorld<double[], double>.NeighbourSelectionHeuristic.SelectSimple;
                            break;
                        case VectorTableParameters<double[]>.eNeighbourSelectionHeuristic.NeighbourSelectionHeuristic:
                            graph._parameters.NeighbourHeuristic = SmallWorld<double[], double>.NeighbourSelectionHeuristic.SelectHeuristic;
                            break;
                    }
                }

                parameters.Storage = storage;

                graph = new SmallWorld<double[], double>.Composer(parameters, instanceQuantity: vectorTableParameters?.QuantityOfLogicalProcessorToCompute ?? 0,
                    GetVectorbyExternalId: vectorTableParameters?.GetItem ?? null);



                dD[tableName] = graph;
            }


            return graph;
        }
    }
}
#endif
