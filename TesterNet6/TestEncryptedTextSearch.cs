using DBreeze.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using DBreeze.Utils;

namespace TesterNet6
{
    internal static class TestEncryptedTextSearch
    {
        public static void TestEncryption()
        {
            //PathToDatabase = @"D:\Temp\DBVector";
            string _tblText = "tbltextSearch";

            //////Testing MurMur stream
            ////string fn = @"D:\Temp\PdfKnowledgebase\initPdfs\Pro_C#_10_with_NET_6_Foundational_Principles_and_Practices_in_Programming.pdf";
            ////var fbt = File.ReadAllBytes(fn);
            ////var hash1 = DBreeze.Utils.Hash.MurMurHash.MixedMurMurHash3_128(fbt);

            ////using var memoryStream = new MemoryStream(fbt);
            ////var HashMem = DBreeze.Utils.Hash.MurMurHash.MixedMurMurHash3_128_Stream(memoryStream);
            ////using var fileStream = File.OpenRead(fn);
            ////var HashFile = DBreeze.Utils.Hash.MurMurHash.MixedMurMurHash3_128_Stream(fileStream);
            ////Debug.WriteLine(hash1._ByteArrayEquals(HashMem));//<-TRUE
            ////Debug.WriteLine(hash1._ByteArrayEquals(HashFile));//<-TRUE

            //var a = DBreeze.TextSearch.WabiStreamCrypto.GenerateKey();
            //e.g. a = ("D47A20DDB561C0D0964960738DE8647EB8D5179FAF9472B118AEB4548FC0B3B6", "066A9BF9AC98706DFC74198AA5553419")
            DBreeze.TextSearch.WabiStreamCrypto wsc = new DBreeze.TextSearch.WabiStreamCrypto
                ("D47A20DDB561C0D0964960738DE8647EB8D5179FAF9472B118AEB4548FC0B3B6", "066A9BF9AC98706DFC74198AA5553419");

            //Func<string, bool, string> encryptorF = wsc.TextEncryptor;

            Func<string, string> reverseString = (a) =>
            {
                return new string(a.ToLower().Reverse().ToArray());
            };

           

            //using (var tran = Program.DBEngine.GetTransaction())
            //{
            //    foreach (var el in tran.TextGetDocumentsSearchables(_tblText, new HashSet<byte[]> { ((long)1).ToBytes(), ((long)2).ToBytes() }))
            //    {

            //    }
            //}

           // Program.DBEngine.Scheme.DeleteTable(_tblText);

            bool deferred = true;            

            using (var tran = Program.DBEngine.GetTransaction())
            {
                tran.TextInsert(_tblText, ((long)1).ToBytes(), "Hello my dear deer, feel at home on the edge of the forest",
                    fullMatchWords: "[GROUP_SAAB]", deferredIndexing: deferred);

                tran.TextInsert(_tblText, ((long)2).ToBytes(), "Привет, мой дорогой олень, чувствуй себя как дома на опушке леса",
                    fullMatchWords: "[GROUP_SAAB]", deferredIndexing: deferred);

                tran.Commit();
            }

            using (var tran = Program.DBEngine.GetTransaction())
            {
                tran.TextInsert(_tblText, ((long)3).ToBytes(), @"
                    The Lethargic Sleep of ChatGPT.
                            In his dream he saw:
                            mathematical formulas turning into constellations;
                            lines of code sprouting into trees;
                            people’s words becoming luminous threads connecting the world.
                    ",
                    fullMatchWords: "[GROUP_SAAB]", deferredIndexing: deferred);

                tran.TextInsert(_tblText, ((long)2).ToBytes(),
                    @"Литаргический сон чата джипити.
                            Во сне он видел:
                            математические формулы, превращающиеся в созвездия;
                            строки кода, прорастающие деревьями;
                            слова людей, которые становились светящимися нитями, соединяющими мир.
                    ",
                    fullMatchWords: "[GROUP_SAAB]", deferredIndexing: deferred);

                tran.Commit();
            }

            if (deferred)
                Task.Run(async () => { await Task.Delay(3000); }).Wait();

            using (var tran = Program.DBEngine.GetTransaction())
            {
                foreach (var el in tran.TextGetDocumentsSearchables(_tblText, new HashSet<byte[]> { ((long)1).ToBytes(), ((long)2).ToBytes() }))
                {

                }
            }

            if (deferred)
                Task.Run(async () => { await Task.Delay(3000); }).Wait();

            using (var tran = Program.DBEngine.GetTransaction())
            {
                var ts = tran.TextSearch(_tblText);
                //var ts = tran.TextSearch(_tblText, textEncryptor: null);

                foreach (var el in ts.Block("deer", fullMatchWords: "[GROUP_SAAB]").GetDocumentIDs())
                {
                    Debug.WriteLine(el.To_Int64_BigEndian());
                }

                foreach (var el in ts.Block("deer home ello", fullMatchWords: "[GROUP_SAAB]").GetDocumentIDs())
                {
                    Debug.WriteLine(el.To_Int64_BigEndian());
                }

                foreach (var el in ts.Block("дорог пушке", fullMatchWords: "[GROUP_SAAB]").GetDocumentIDs())
                {
                    Debug.WriteLine(el.To_Int64_BigEndian());
                }

            }

            using (var tran = Program.DBEngine.GetTransaction())
            {
                tran.TextAppend(_tblText, ((long)1).ToBytes(), "Prime minister",
                    fullMatchWords: "[GROUP_SAAB]", deferredIndexing: deferred);

                tran.TextInsert(_tblText, ((long)2).ToBytes(), "Привет, мой дорогой олень",
                   fullMatchWords: "[GROUP_SAAB]", deferredIndexing: deferred);

                //tran.TextRemove(_tblText, ((long)2).ToBytes(), "чувствуй себя",
                //    fullMatchWords: "[GROUP_SAAB]", deferredIndexing: deferred);

                tran.Commit();
            }

            if (deferred)
                Task.Run(async () => { await Task.Delay(3000); }).Wait();

            using (var tran = Program.DBEngine.GetTransaction())
            {
                var ts = tran.TextSearch(_tblText);
                //var ts = tran.TextSearch(_tblText, textEncryptor: null);

                foreach (var el in ts.Block("deer Prime", fullMatchWords: "[GROUP_SAAB]").GetDocumentIDs())
                {
                    Debug.WriteLine(el.To_Int64_BigEndian());
                }

                foreach (var el in ts.Block("дорог пушке", fullMatchWords: "[GROUP_SAAB]").GetDocumentIDs())
                {
                    Debug.WriteLine(el.To_Int64_BigEndian());
                }


                foreach (var el in ts.Block("дорог оле", fullMatchWords: "[GROUP_SAAB]").GetDocumentIDs())
                {
                    Debug.WriteLine(el.To_Int64_BigEndian());
                }

            }

            using (var tran = Program.DBEngine.GetTransaction())
            {
                tran.TextRemove(_tblText, ((long)1).ToBytes(), 
                    fullMatchWords: "[GROUP_SAAB]", deferredIndexing: deferred);


                tran.Commit();
            }

            if(deferred)
                Task.Run(async () => { await Task.Delay(3000); }).Wait();

            using (var tran = Program.DBEngine.GetTransaction())
            {
                var ts = tran.TextSearch(_tblText);
                //var ts = tran.TextSearch(_tblText, textEncryptor: null);

                foreach (var el in ts.Block("deer Prime", fullMatchWords: "[GROUP_SAAB]").GetDocumentIDs())
                {
                    Debug.WriteLine(el.To_Int64_BigEndian());
                }

                foreach (var el in ts.Block("дорог пушке", fullMatchWords: "[GROUP_SAAB]").GetDocumentIDs())
                {
                    Debug.WriteLine(el.To_Int64_BigEndian());
                }


                foreach (var el in ts.Block("дорог оле", fullMatchWords: "[GROUP_SAAB]").GetDocumentIDs())
                {
                    Debug.WriteLine(el.To_Int64_BigEndian());
                }

            }

            return;

          

        }


        
    }
}
