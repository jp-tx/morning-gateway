using System.Text.Json;
using MorningGateway.Models;

namespace MorningGateway.Services.Calendar;

/// <summary>Persists the list of configured calendar sources (not their secrets - see CalendarSourceConfig.SecureStorageKey).</summary>
public class CalendarSourceStore
{
    const string PreferenceKey = "calendar_sources_json";
    const string DemoSeededKey = "demo_source_seeded";

    /// <summary>
    /// A public demo feed (daily recurring stand-up/lunch/wrap-up/gym/trash-day events)
    /// so a fresh install shows a populated month and day view instead of an empty
    /// dashboard. Seeded once on first run only - removing it in Settings sticks.
    /// </summary>
    const string DemoIcsUrl = "https://gist.githubusercontent.com/jp-tx/ccfb52833900a7e6e2f9faa27a3e2e54/raw/d01732dd2168d56f6743085d8709abbf69bc1852/demo-calendar.ics";

    public event Action? SourcesChanged;

    public List<CalendarSourceConfig> Load()
    {
        var json = Preferences.Default.Get(PreferenceKey, string.Empty);
        if (string.IsNullOrWhiteSpace(json))
        {
            if (!Preferences.Default.Get(DemoSeededKey, false))
            {
                Preferences.Default.Set(DemoSeededKey, true);
                var seeded = new List<CalendarSourceConfig> { CreateDemoSource() };
                Save(seeded);
                return seeded;
            }

            return new List<CalendarSourceConfig>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<CalendarSourceConfig>>(json) ?? new List<CalendarSourceConfig>();
        }
        catch (JsonException)
        {
            return new List<CalendarSourceConfig>();
        }
    }

    static CalendarSourceConfig CreateDemoSource() => new()
    {
        DisplayName = "Demo Calendar",
        Type = CalendarSourceType.IcsUrl,
        IcsUrl = DemoIcsUrl,
        ColorHex = "#4C8BF5",
    };

    public void Save(List<CalendarSourceConfig> sources)
    {
        Preferences.Default.Set(PreferenceKey, JsonSerializer.Serialize(sources));
        SourcesChanged?.Invoke();
    }

    public void Upsert(CalendarSourceConfig source)
    {
        var sources = Load();
        var index = sources.FindIndex(s => s.Id == source.Id);
        if (index >= 0)
        {
            sources[index] = source;
        }
        else
        {
            sources.Add(source);
        }

        Save(sources);
    }

    public async Task RemoveAsync(string sourceId)
    {
        var sources = Load();
        var match = sources.FirstOrDefault(s => s.Id == sourceId);
        if (match is not null)
        {
            sources.Remove(match);
            Save(sources);
            try
            {
                SecureStorage.Default.Remove(match.SecureStorageKey);
            }
            catch (Exception)
            {
                // Best-effort secret cleanup.
            }
        }

        await Task.CompletedTask;
    }
}
