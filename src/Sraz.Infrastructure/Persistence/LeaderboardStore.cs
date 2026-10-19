using System.Text.Json;
using System.Text.Json.Serialization;
using Sraz.Application.Abstractions;
using Sraz.Domain.Models;

namespace Sraz.Infrastructure.Persistence;

public sealed class LeaderboardStore : ILeaderboardStore
{
    private static readonly object Sync = new();

    private readonly string _filePath;

    public LeaderboardStore(string filePath)
    {
        _filePath = filePath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? ".");
    }

    public List<LeaderboardEntry> Load()
    {
        lock (Sync)
        {
            return LoadCore();
        }
    }

    public void Save(IEnumerable<LeaderboardEntry> entries)
    {
        lock (Sync)
        {
            SaveCore(entries);
        }
    }

    private List<LeaderboardEntry> LoadCore()
    {
        if (!File.Exists(_filePath))
        {
            return new List<LeaderboardEntry>();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<LeaderboardEntry>();
            }

            var entries = JsonSerializer.Deserialize<List<LeaderboardEntry>>(json, JsonOptions.Default);
            return entries ?? new List<LeaderboardEntry>();
        }
        catch (JsonException)
        {
            BackupCorruptFile();
            return new List<LeaderboardEntry>();
        }
    }

    private void SaveCore(IEnumerable<LeaderboardEntry> entries)
    {
        var ordered = entries
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.PlayerName)
            .ToList();

        var json = JsonSerializer.Serialize(ordered, JsonOptions.Default);
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _filePath, overwrite: true);
    }

    private void BackupCorruptFile()
    {
        try
        {
            File.Move(_filePath, _filePath + ".corrupt", overwrite: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public static class JsonOptions
    {
        public static readonly JsonSerializerOptions Default = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }
}
