using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace DBreeze.ReleaseAudit.Protocol
{
    [DataContract]
    public sealed class WorkerReport
    {
        [DataMember(Order = 1)] public string ProtocolVersion { get; set; }
        [DataMember(Order = 2)] public string Variant { get; set; }
        [DataMember(Order = 3)] public string Framework { get; set; }
        [DataMember(Order = 4)] public string Runtime { get; set; }
        [DataMember(Order = 5)] public string AssemblyPath { get; set; }
        [DataMember(Order = 6)] public string AssemblySha256 { get; set; }
        [DataMember(Order = 7)] public DateTime StartedUtc { get; set; }
        [DataMember(Order = 8)] public DateTime CompletedUtc { get; set; }
        [DataMember(Order = 9)] public bool Succeeded { get; set; }
        [DataMember(Order = 10)] public string Failure { get; set; }
        [DataMember(Order = 11)] public List<ApiMember> AssemblyApi { get; set; }
        [DataMember(Order = 12)] public List<ApiMember> FocusedApi { get; set; }
        [DataMember(Order = 13)] public List<CoverageEntry> Coverage { get; set; }
        [DataMember(Order = 14)] public List<CaseResult> Cases { get; set; }
        [DataMember(Order = 15)] public List<FileEntry> Files { get; set; }
        [DataMember(Order = 16)] public List<Measurement> Measurements { get; set; }

        public WorkerReport()
        {
            ProtocolVersion = "1";
            AssemblyApi = new List<ApiMember>();
            FocusedApi = new List<ApiMember>();
            Coverage = new List<CoverageEntry>();
            Cases = new List<CaseResult>();
            Files = new List<FileEntry>();
            Measurements = new List<Measurement>();
        }
    }

    [DataContract]
    public sealed class ApiMember
    {
        [DataMember(Order = 1)] public string Id { get; set; }
        [DataMember(Order = 2)] public string DeclaringType { get; set; }
        [DataMember(Order = 3)] public string Kind { get; set; }
    }

    [DataContract]
    public sealed class CoverageEntry
    {
        [DataMember(Order = 1)] public string MemberId { get; set; }
        [DataMember(Order = 2)] public string Mode { get; set; }
        [DataMember(Order = 3)] public int Attempts { get; set; }
        [DataMember(Order = 4)] public int Successes { get; set; }
        [DataMember(Order = 5)] public string Evidence { get; set; }
    }

    [DataContract]
    public sealed class CaseResult
    {
        [DataMember(Order = 1)] public string Id { get; set; }
        [DataMember(Order = 2)] public string Category { get; set; }
        [DataMember(Order = 3)] public string Mode { get; set; }
        [DataMember(Order = 4)] public bool Succeeded { get; set; }
        [DataMember(Order = 5)] public string SemanticValue { get; set; }
        [DataMember(Order = 6)] public string Detail { get; set; }
        [DataMember(Order = 7)] public long ElapsedMilliseconds { get; set; }
    }

    [DataContract]
    public sealed class FileEntry
    {
        [DataMember(Order = 1)] public string RelativePath { get; set; }
        [DataMember(Order = 2)] public long Length { get; set; }
        [DataMember(Order = 3)] public string Sha256 { get; set; }
    }

    [DataContract]
    public sealed class Measurement
    {
        [DataMember(Order = 1)] public string Scenario { get; set; }
        [DataMember(Order = 2)] public string Category { get; set; }
        [DataMember(Order = 3)] public int Workers { get; set; }
        [DataMember(Order = 4)] public int Round { get; set; }
        [DataMember(Order = 5)] public long Operations { get; set; }
        [DataMember(Order = 6)] public double ElapsedMilliseconds { get; set; }
        [DataMember(Order = 7)] public long AllocatedBytes { get; set; }
        [DataMember(Order = 8)] public long ProcessAllocatedBytes { get; set; }
        [DataMember(Order = 9)] public int Gen0Collections { get; set; }
        [DataMember(Order = 10)] public int Gen1Collections { get; set; }
        [DataMember(Order = 11)] public int Gen2Collections { get; set; }
        [DataMember(Order = 12)] public long LiveHeapBytes { get; set; }
        [DataMember(Order = 13)] public long PeakPrivateBytes { get; set; }
        [DataMember(Order = 14)] public long DatabaseBytes { get; set; }
        [DataMember(Order = 15)] public string Checksum { get; set; }
        [DataMember(Order = 16)] public bool BackgroundAllocationCounter { get; set; }
    }

    public static class WireJson
    {
        public static void Write<T>(string path, T value)
        {
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (!String.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            string temporary = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
            var serializer = new DataContractJsonSerializer(typeof(T), new DataContractJsonSerializerSettings
            {
                UseSimpleDictionaryFormat = true
            });
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                serializer.WriteObject(stream, value);
                stream.Flush();
            }
            if (File.Exists(fullPath))
                File.Replace(temporary, fullPath, null);
            else
                File.Move(temporary, fullPath);
        }

        public static T Read<T>(string path)
        {
            var serializer = new DataContractJsonSerializer(typeof(T), new DataContractJsonSerializerSettings
            {
                UseSimpleDictionaryFormat = true
            });
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                return (T)serializer.ReadObject(stream);
        }
    }
}
