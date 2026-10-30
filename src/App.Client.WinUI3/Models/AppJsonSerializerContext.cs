using System.Text.Json;
using System.Text.Json.Serialization;

namespace App.Client.WinUI3.Models;

// Source generated, so serialization keeps working when the app is trimmed.
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(Project))]
[JsonSerializable(typeof(IReadOnlyCollection<Project>))]
public sealed partial class AppJsonSerializerContext : JsonSerializerContext;
