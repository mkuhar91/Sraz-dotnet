using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sraz.Infrastructure.Persistence;

public sealed class LeaderboardStore
{
    private readonly string _filePath;

    public LeaderboardStore(string filePath)
    {
        _filePath = filePath;
        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? ".");
    }

    public List<LeaderboardEntry> Load()
    {
        if (!File.Exists(_filePath))
        {
            return new List<LeaderboardEntry>();
        }

        var json = File.ReadAllText(_filePath);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<LeaderboardEntry>();
        }

        var entries = JsonSerializer.Deserialize<List<LeaderboardEntry>>(json, JsonOptions.Default);
        return entries ?? new List<LeaderboardEntry>();
    }

    public void Save(IEnumerable<LeaderboardEntry> entries)
    {
        var ordered = entries
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.PlayerName)
            .ToList();

        var json = JsonSerializer.Serialize(ordered, JsonOptions.Default);
        File.WriteAllText(_filePath, json);
    }

    public static class JsonOptions
    {
        public static readonly JsonSerializerOptions Default = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }
}
