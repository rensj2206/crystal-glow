using System.Text.Json;

namespace CrystalGlow.Dmx;

/// <summary>
/// Loads FixtureProfile definitions from a folder of .json files.
/// </summary>
public static class FixtureProfileLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static List<FixtureProfile> LoadFromFolder(string folderPath)
    {
        var profiles = new List<FixtureProfile>();

        if (!Directory.Exists(folderPath))
            return profiles;

        foreach (var file in Directory.GetFiles(folderPath, "*.json"))
        {
            var json = File.ReadAllText(file);
            var profile = JsonSerializer.Deserialize<FixtureProfile>(json, Options);
            if (profile != null)
                profiles.Add(profile);
        }

        return profiles;
    }
}
