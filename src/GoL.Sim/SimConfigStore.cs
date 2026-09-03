using System;
using System.IO;
using System.Text.Json;

namespace GoL.Sim;

/// <summary>
/// Loads and saves <see cref="SimConfig"/> as JSON.
/// <para>
/// Lives in the simulation assembly rather than the app because it has no
/// graphics dependency - only <c>System.Text.Json</c> and the config type - which
/// means its round-trip behaviour is testable headlessly. That matters more than
/// usual here: the defaults-for-absent-fields behaviour is the difference between
/// a mistuned run and a silently dead one.
/// </para>
/// <para>
/// Persistence is best-effort. A corrupt, unreadable or unwritable file must
/// never stop the game launching, so every failure path falls back to defaults
/// and reports on stderr.
/// </para>
/// </summary>
public sealed class SimConfigStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Creates a store rooted at the given directory. The parameterless
    /// path is <c>~/.gol</c>; tests pass a temporary directory.</summary>
    public SimConfigStore(string? directory = null)
    {
        Directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gol");
    }

    public string Directory { get; }

    public string FilePath => Path.Combine(Directory, "config.json");

    /// <summary>
    /// Reads the saved config, or defaults if there is none or it cannot be read.
    /// Fields absent from the file keep their property initializers - which is
    /// precisely why <see cref="SimConfig"/> is a non-positional record.
    /// </summary>
    public SimConfig Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new SimConfig();
            return JsonSerializer.Deserialize<SimConfig>(File.ReadAllText(FilePath), Options)
                   ?? new SimConfig();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"[config] could not read {FilePath}: {ex.Message}. Using defaults.");
            return new SimConfig();
        }
    }

    /// <summary>Writes the config. Returns false if it could not be saved.</summary>
    public bool Save(SimConfig config)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(config, Options));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"[config] could not write {FilePath}: {ex.Message}");
            return false;
        }
    }
}
