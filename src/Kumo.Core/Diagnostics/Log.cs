using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Kumo.Core.Diagnostics;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}

/// <summary>
/// Minimal structured logger writing one JSON object per line to a daily file.
/// Files are opened per write so nothing stays locked while Kumo is idle, and retention is bounded by age and size.
/// </summary>
public sealed class Log
{
    private const long MaxFileBytes = 1024 * 1024;
    private const int RetainedFiles = 7;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly object _gate = new();
    private readonly string _directory;
    private readonly TimeProvider _clock;
    private bool _pruned;

    public Log(string directory, TimeProvider? clock = null)
    {
        _directory = directory;
        _clock = clock ?? TimeProvider.System;
    }

    public static Log Null { get; } = new(string.Empty) { MinimumLevel = (LogLevel)int.MaxValue };

    public string Directory => _directory;

    public LogLevel MinimumLevel { get; set; } = LogLevel.Info;

    public void Debug(string message, params (string Key, object? Value)[] fields) => Write(LogLevel.Debug, message, null, fields);

    public void Info(string message, params (string Key, object? Value)[] fields) => Write(LogLevel.Info, message, null, fields);

    public void Warning(string message, params (string Key, object? Value)[] fields) => Write(LogLevel.Warning, message, null, fields);

    public void Error(string message, Exception? exception = null, params (string Key, object? Value)[] fields) =>
        Write(LogLevel.Error, message, exception, fields);

    public void Write(LogLevel level, string message, Exception? exception, params (string Key, object? Value)[] fields)
    {
        if (level < MinimumLevel || _directory.Length == 0)
            return;

        var now = _clock.GetLocalNow();
        string line = Format(now, level, message, exception, fields);
        lock (_gate)
        {
            try
            {
                System.IO.Directory.CreateDirectory(_directory);
                if (!_pruned)
                {
                    Prune();
                    _pruned = true;
                }
                string path = CurrentFile(now);
                if (File.Exists(path) && new FileInfo(path).Length > MaxFileBytes)
                    return;
                File.AppendAllText(path, line + Environment.NewLine, Utf8NoBom);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Logging must never take the application down; a full or locked disk simply drops the entry.
            }
        }
    }

    internal static string Format(DateTimeOffset time, LogLevel level, string message, Exception? exception, (string Key, object? Value)[] fields)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("t", time.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteString("level", level.ToString().ToLowerInvariant());
            writer.WriteString("msg", message);
            foreach (var (key, value) in fields)
                WriteField(writer, key, value);
            if (exception != null)
            {
                writer.WriteString("error", exception.GetType().FullName);
                writer.WriteString("detail", exception.Message);
                if (exception.StackTrace != null)
                    writer.WriteString("stack", exception.StackTrace);
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteField(Utf8JsonWriter writer, string key, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNull(key);
                break;
            case bool b:
                writer.WriteBoolean(key, b);
                break;
            case int or long or double or float or decimal:
                writer.WriteNumber(key, Convert.ToDouble(value, CultureInfo.InvariantCulture));
                break;
            default:
                writer.WriteString(key, Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
    }

    private string CurrentFile(DateTimeOffset now) =>
        Path.Combine(_directory, $"kumo-{now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.log");

    private void Prune()
    {
        var files = new DirectoryInfo(_directory).GetFiles("kumo-*.log")
            .OrderByDescending(f => f.Name, StringComparer.Ordinal)
            .Skip(RetainedFiles);
        foreach (var file in files)
        {
            try
            {
                file.Delete();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // An old log that cannot be removed now will be retried on the next start.
            }
        }
    }
}
