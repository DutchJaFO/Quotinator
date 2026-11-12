namespace Quotinator.Api.Tests.Components;

/// <summary>
/// The English strings a component renders, read from the baseline translation file so a test asserts the
/// text an operator actually sees rather than a copy of it. Shared by the component tests that assert
/// outcome sentences, moved here from <see cref="DatabaseRepairActionsTests"/> when a second one needed it.
/// </summary>
internal static class BaselineUiText
{
    /// <summary>A generated <c>UI</c> table populated from <c>UI.en-GB.json</c>.</summary>
    public static Quotinator.Api.I18nText.UI Load()
    {
        string baseline = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "Quotinator.Api", "i18ntext", "UI.en-GB.json");
        Dictionary<string, string> strings =
            System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(baseline))!;

        Quotinator.Api.I18nText.UI text = new();
        foreach (System.Reflection.FieldInfo field in typeof(Quotinator.Api.I18nText.UI).GetFields())
        {
            if (field.FieldType == typeof(string) && strings.TryGetValue(field.Name, out string? value))
                field.SetValue(text, value);
        }
        return text;
    }
}
