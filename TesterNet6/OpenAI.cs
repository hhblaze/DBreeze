using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Text.Json;

namespace TesterNet6
{
    internal static class OpenAI
    {
        static HttpClient _HttpClient = null;
        static string OpenAiApiKey="";

        /// <summary>
        /// 
        /// </summary>
        public static void Init(string pathToOpenAiKey)
        {
            var handler = new System.Net.Http.HttpClientHandler();
            if (handler.SupportsAutomaticDecompression)
            {
                handler.AutomaticDecompression = DecompressionMethods.GZip |
                                                 DecompressionMethods.Deflate;
            }

            _HttpClient = new System.Net.Http.HttpClient(handler);
            _HttpClient.Timeout = TimeSpan.FromSeconds(25);
            _HttpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 6.3; rv:36.0) Gecko/20100101 Firefox/36.0");
            _HttpClient.DefaultRequestHeaders.Add("Referer", "http://google.de");
            _HttpClient.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate");
            //we want to hold connection, but DNS change will be fixed by ConnectionLeaseTimeout
            _HttpClient.DefaultRequestHeaders.ConnectionClose = false;

            //Reading OpenAiApiKey            
            if (File.Exists(pathToOpenAiKey))
                OpenAiApiKey = File.ReadAllText(pathToOpenAiKey);
        }

        public async static Task<GptCallStat> GetEmbedding(string userInput)
        {
            //string userInput = "Мама мыла раму";

            var statCall = await OpenAI.GPTCallEmbedding(userInput).ConfigureAwait(false);
            //statCall.GptEmbeddingsFullResponse = NetJSON.NetJSON.Deserialize<OAIEmbeddingsResponse>(statCall.fullResp);
            statCall.GptEmbeddingsFullResponse = JsonSerializer.Deserialize<OAIEmbeddingsResponse>(statCall.fullResp);

            if (statCall.GptEmbeddingsFullResponse.data.Count > 0)
            {
                statCall.EmbeddingAnswer = statCall.GptEmbeddingsFullResponse.data.First().embedding;

            }

            return statCall;
        }

        internal class GptCallStat
        {
            public OAIEmbeddingsResponse GptEmbeddingsFullResponse = null;
            /// <summary>
            /// Full response as string
            /// </summary>
            public string fullResp = String.Empty;
            public bool error = false;
            public string Bearer = "OpenAI";
            public string Model = String.Empty;

            /// <summary>
            ///Default String.Empty, text of the answer from [OpenAI.ChatGPT]=Content.choices[0].message.content;
            /// </summary>
            public string TextAnswer = String.Empty;
            /// <summary>
            /// Can be null. Answer for embeddings
            /// </summary>
            public List<double> EmbeddingAnswer = null;

            /// <summary>
            /// Default String.Empty
            /// </summary>
            public string FinishReason = String.Empty;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="userInput"></param>
        /// <returns></returns>
        public static async Task<GptCallStat> GPTCallEmbedding(string userInput)
        {
            var callStat = new GptCallStat();
            callStat.Bearer = "OpenAI";
            callStat.Model = "text-embedding-ada-002";

            //long requestTokensUsed = 0;
            //long responseTokensUsed = 0;

            try
            {
                string apiKey = OpenAiApiKey; // Replace with your actual API key
                                                                                       //string endpoint = "https://api.openai.com/v1/engines/davinci-codex/completions";
                                                                                       //string endpoint = "https://api.openai.com/v1/completions";
                string endpoint = "https://api.openai.com/v1/embeddings";


                var requestMessage = new HttpRequestMessage();
                //requestMessage.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 6.3; rv:36.0) Gecko/20100101 Firefox/36.0");            
                requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                #region "helper"
                /*
                 using System.Web;
                 string escapedJson = HttpUtility.JavaScriptStringEncode(unescapedJson);
                string unescapedJson = HttpUtility.JavaScriptStringDecode(escapedJson);

                https://platform.openai.com/docs/api-reference/chat
                https://platform.openai.com/docs/guides/gpt/chat-completions-api
                https://www.freeformatter.com/json-escape.html

                Chat views
                //https://gscode.in/chat-box-design/
                //https://codepen.io/masudrana2779/pen/OJbRyRB

                //https://dev.tiesky.com:27750/modules.getfz.GM_GPT/web.zip/chat.html

                https://json2csharp.com/


               string cont = @"{
        ""model"": ""gpt-3.5-turbo"",
        ""messages"": [
          {
            ""role"": ""system"",
            ""content"": ""Your role is to provide me a javascript function main() that answers with JSON object, without surrounding text. Inside of the main() function should be called as many external functions as necessary to provide an answer. Results of those functions must be computed and aggregated when necessary using javascript.\tFor external functions supply DateTime in format 'YYYYMMDD hh:mm:ss', but compute DateTime with javascript where possible. For view formatters use external functions. \r\n\tFor example, if user asks 'How much fuel spent Tino this year' you should answer with: \r\nfunction main() \r\n{ \r\n\r\n\tvar carId = external_GetCarIdByUserName('Tino');\r\n\tstartDate = \/\/compute it with javascript ;\r\n\tstopDate = \/\/compute it with javascript ;\r\n\tvar fuelConsumption = external_GetCarFuelCosumptionById(carId, startDate, stopDate);\r\n\tvar response={ 'FuelConsumption' : fuelConsumption};\r\n\treturn response;\r\n}""
          },
          {
            ""role"": ""user"",
            ""content"": """+userInput+@"""
          }
        ]
      }";

                VS escaping format
                   ""content"": """+userInput+@"""


                 */
                #endregion

                //postman
                //https://platform.openai.com/docs/api-reference/chat

                //userInput = "Show me location of Tino last Thursday";
                userInput = HttpUtility.JavaScriptStringEncode(userInput);

                //Models: https://platform.openai.com/docs/models/gpt-4
                //Pricing calculator https://gptforwork.com/tools/openai-chatgpt-api-pricing-calculator
                //Usage: https://platform.openai.com/account/usage
                //Pricing: https://openai.com/pricing

                //Way to compress prompt in GPT4 with unicode
                //https://gist.github.com/VictorTaelin/d293328f75291b23e203e9d9db9bd136
                //https://www.piratewires.com/p/compression-prompts-gpt-hidden-dialects

                //Options of the request
                //https://platform.openai.com/docs/api-reference/chat/create

                //string model = "gpt-3.5-turbo";
                //model = "gpt-4";

                string cont = $@"{{
    ""model"": ""{callStat.Model}"",
    ""input"": ""{userInput}""
  }}";


                //requestMessage.Content = new StringContent("{\"model\": \"" + "gpt-3.5-turbo" + "\", \"prompt\": \"" + input + "\", \"temperature\": 0.7, \"max_tokens\": 150}", Encoding.UTF8, "application/json");


                //callStat.TokeneizerRequestTokensUsed = GPT3Tokenizer.Encode(cont).Count();

                requestMessage.Content = new StringContent(cont, Encoding.UTF8, "application/json");


                //requestMessage.Content = new StringContent($@"
                //{{
                //   ""model"":""text-davinci-003"",
                //   ""prompt"":""{input}"",
                //   ""temperature"":0.7,
                //   ""max_tokens"":150,
                //}}", Encoding.UTF8, "application/json");


                requestMessage.Method = HttpMethod.Post;
                requestMessage.RequestUri = new Uri(endpoint);


                //var content = new StringContent("{\"prompt\": \"" + input + "\", \"temperature\": 0.7, \"max_tokens\": 150}", Encoding.UTF8, "application/json");
                var response = await _HttpClient.SendAsync(requestMessage);
                response.EnsureSuccessStatusCode();
                callStat.fullResp = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                //callStat.TokeneizerResponseTokensUsed = GPT3Tokenizer.Encode(callStat.fullResp).Count();

                return callStat;

            }
            catch (Exception ex)
            {
                //_M_WDA._Log.LogException("OpenAI.GPTTransaction", "GPTCallEmbeddings", ex, "");
                callStat.fullResp = String.Empty;
                callStat.error = true;
                return callStat;
            }


        }

        /// <summary>
        /// 
        /// </summary>
        public class OAIEmbeddingsResponse
        {
            public class Data
            {
                //public string @object { get; set; }
                public int index { get; set; }
                public List<double> embedding { get; set; }
            }
            /// <summary>
            /// Usually only first element is a response
            /// </summary>
            public List<OAIEmbeddingsResponse.Data> data { get; set; }
            public string model { get; set; }
            public Usage usage { get; set; }
        }
        public class Usage
        {
            public int prompt_tokens { get; set; }
            public int completion_tokens { get; set; }
            public int total_tokens { get; set; }
        }

    }
}
