using System.Text.Json.Serialization;

namespace App.Domain;

/// <param name="WrapOutput">
/// Whether the long lines a command writes are wrapped, rather than scrolled horizontally.
/// Always written, as it is <see langword="true"/> when missing, so <see langword="false"/> wouldn't be read back otherwise.
/// </param>
/// <param name="MaxOutputHeight">How tall the output of a command in the history grows, in pixels, before it is scrolled, following what is written last; <see langword="null"/> for as tall as it is.</param>
/// <param name="ConfirmTemplateScripts">Whether the command of a script template, with its variables filled in, is shown to be confirmed before it is run.</param>
public sealed record AppSettings(AppTheme Theme = AppTheme.System, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] bool WrapOutput = true, int? MaxOutputHeight = null, bool ConfirmTemplateScripts = false);
