using System.Globalization;
using System.Text.Json;
using Kumo.Core.Diagnostics;

namespace Kumo.Core.Settings;

public sealed class SettingsStore
{
    // Relaxed escaping keeps values such as "Ctrl+Alt+K" readable for people editing the file by hand.
    private static readonly SettingsJsonContext Json = new(new JsonSerializerOptions(SettingsJsonContext.Default.Options)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    private readonly string _path;
    private readonly Log _log;

    public SettingsStore(string path, Log log)
    {
        _path = path;
        _log = log;
    }

    public string FilePath => _path;

    /// <summary>Missing or unreadable settings fall back to defaults; a corrupt file is kept aside for inspection.</summary>
    public KumoSettings Load()
    {
        if (!File.Exists(_path))
            return new KumoSettings();
        try
        {
            using var stream = File.OpenRead(_path);
            var settings = JsonSerializer.Deserialize(stream, Json.KumoSettings);
            return (settings ?? new KumoSettings()).Normalize();
        }
        catch (JsonException ex)
        {
            _log.Warning("Settings file is corrupt; defaults restored", ("detail", ex.Message));
            PreserveCorruptFile();
            return new KumoSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning("Settings file could not be read; using defaults", ("detail", ex.Message));
            return new KumoSettings();
        }
    }

    /// <summary>Writes to a sibling temporary file and swaps it in, so a crash never leaves a half-written file.</summary>
    public void Save(KumoSettings settings)
    {
        string directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, settings.Clone().Normalize(), Json.KumoSettings);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private void PreserveCorruptFile()
    {
        try
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            File.Move(_path, $"{_path}.corrupt-{stamp}", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning("Corrupt settings file could not be set aside", ("detail", ex.Message));
        }
    }
}
