using System.Text.Json.Serialization;

namespace App.Client.WinUI3.Models;

[JsonConverter(typeof(JsonStringEnumConverter<AppTheme>))]
public enum AppTheme
{
    System,
    Dark,
    Light,
}
