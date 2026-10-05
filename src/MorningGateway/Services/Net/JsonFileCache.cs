using System.Text.Json;

namespace MorningGateway.Services.Net;

/// <summary>
/// Tiny last-known-good store: one JSON file per key under a directory. Reads and
/// writes never throw (a missing, corrupt or unwritable cache just means "no cache"),
/// and writes go through a temp file so a crash mid-write can't corrupt the old copy.
/// </summary>
public class JsonFileCache
{
    readonly string _directory;

    public JsonFileCache(string directory)
    {
        _directory = directory;
    }

    string PathFor(string key) => Path.Combine(_directory, Uri.EscapeDataString(key) + ".json");

    public T? Load<T>(string key) where T : class
    {
        try
        {
            var path = PathFor(key);
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path)) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Save<T>(string key, T value)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            var path = PathFor(key);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(value));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception)
        {
            // Best effort: losing the offline copy must never break a live refresh.
        }
    }

    public void Delete(string key)
    {
        try
        {
            File.Delete(PathFor(key));
        }
        catch (Exception)
        {
        }
    }
}
