using System.Text.Json.Serialization;

namespace App.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<AppTheme>))]
public enum AppTheme
{
    System,
    Dark,
    Light,
}
