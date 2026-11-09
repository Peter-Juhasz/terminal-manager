using System.Text.Json;
using System.Text.Json.Serialization;

namespace App.Domain;

// Source generated, so serialization keeps working when the app is trimmed.
// Values left unset aren't written, so they are read back as their defaults.
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault)]
[JsonSerializable(typeof(Project))]
[JsonSerializable(typeof(IReadOnlyCollection<Project>))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(IReadOnlyList<HistoryEntry>))]
public sealed partial class AppJsonSerializerContext : JsonSerializerContext;
