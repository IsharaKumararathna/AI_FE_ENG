using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Aife.Infrastructure.Persistence;

/// <summary>
/// Helper for reading and writing JSON files with camelCase serialization
/// (Newtonsoft.Json per convention).
/// </summary>
internal static class JsonFileHelper
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        Formatting = Formatting.Indented,
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore
    };

    public static T? Read<T>(string path)
    {
        if (!File.Exists(path))
            return default;

        var json = File.ReadAllText(path);
        return JsonConvert.DeserializeObject<T>(json, Settings);
    }

    public static void Write<T>(string path, T value)
    {
        var dir = Path.GetDirectoryName(path);
        if (dir is not null)
            Directory.CreateDirectory(dir);

        var json = JsonConvert.SerializeObject(value, Settings);
        File.WriteAllText(path, json);
    }
}
