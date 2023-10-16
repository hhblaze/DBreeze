using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.ML;
using Microsoft.ML.Transforms.Text;
using static TesterNet6.TextCorpus.Clustering;

namespace TesterNet6
{
    /*
     https://learn.microsoft.com/en-us/dotnet/api/microsoft.ml.textcatalog.applywordembedding?view=ml-dotnet
     */
    public class MsMLEmbedder
    {
        Microsoft.ML.Data.TransformerChain<WordEmbeddingTransformer> textTransformer = null;
        MLContext mlContext = null;
        PredictionEngine<TextData, TransformedTextData> predictionEngine = null;

        public MsMLEmbedder()
        {          
            mlContext = new MLContext();

            var emptySamples = new List<TextData>();

            var emptyDataView = mlContext.Data.LoadFromEnumerable(emptySamples);

            var textPipeline = mlContext.Transforms.Text.NormalizeText("Text")
                .Append(mlContext.Transforms.Text.TokenizeIntoWords("Tokens",
                    "Text"))
                .Append(mlContext.Transforms.Text.ApplyWordEmbedding("Features",
                    "Tokens", WordEmbeddingEstimator.PretrainedModelKind
                    //.SentimentSpecificWordEmbedding
                    .GloVe300D
                    ));
          
            textTransformer = textPipeline.Fit(emptyDataView);

            predictionEngine = mlContext.Model.CreatePredictionEngine<TextData,
                TransformedTextData>(textTransformer);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public TransformedTextData GetEmbedding(string text)
        {
            var data = new TextData()
            {
                Text = text
            };
            var prediction = predictionEngine.Predict(data);

            return prediction;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public double[] GetEmbeddingDoubleArray(string text)
        {
            var data = new TextData()
            {
                Text = text
            };
            var prediction = predictionEngine.Predict(data);

            return prediction.Features.Select(Convert.ToDouble).ToArray();
        }


        /// <summary>
        /// 
        /// </summary>
        public static void GetSomeEmbeddingVectors()
        {
            MsMLEmbedder emb=new MsMLEmbedder();

            var furnitureLst = JsonSerializer.Deserialize<List<FurnitureV1>>(File.ReadAllText(@"..\..\..\TextCorpus\FurnitureV1.json"));

            foreach (var cluster in furnitureLst)
            {
                foreach (var clusterItem in cluster.Items)
                {
                    clusterItem.Embedding = emb.GetEmbeddingDoubleArray(cluster.Cluster + " " + clusterItem.Name + " " + clusterItem.Description);                     

                }
            }

            File.WriteAllText(@"..\..\..\TextCorpus\FurnitureV1withEmbeddings_MSML.json", JsonSerializer.Serialize(furnitureLst));

        }

        /// <summary>
        /// 
        /// </summary>
        public class TextData
        {
            public string Text { get; set; }
        }

        /// <summary>
        /// 
        /// </summary>
        public class TransformedTextData : TextData
        {
            public float[] Features { get; set; }
        }
    }
}
