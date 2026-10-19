using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kumo.Core.Settings;

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(KumoSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
