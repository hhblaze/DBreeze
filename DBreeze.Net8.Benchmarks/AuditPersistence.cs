using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DBreeze.Net8.Benchmarks;

internal static class AuditPersistence
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    internal static void WriteJson<T>(string path, T value)
    {
        WriteTextAtomic(path, JsonSerializer.Serialize(value, JsonOptions));
    }

    internal static T ReadJson<T>(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Audit result was not found.", path);
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException($"Invalid audit JSON: {path}");
    }

    internal static void WriteTextAtomic(string path, string content)
    {
        string fullPath = Path.GetFullPath(path);
        string parent = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Output path must have a parent directory.");
        Directory.CreateDirectory(parent);
        string temporaryPath = fullPath + "." + Environment.ProcessId + ".tmp";
        File.WriteAllText(temporaryPath, content ?? String.Empty, new UTF8Encoding(false));
        File.Move(temporaryPath, fullPath, overwrite: true);
    }
}
