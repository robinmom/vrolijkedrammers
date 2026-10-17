namespace Drammers.Infrastructure.Exports;

/// <summary>
/// CSV voor Excel (puntkomma). Elke cel wordt beschermd tegen formule-injectie: een waarde die met <c>=</c>, <c>+</c>,
/// <c>-</c>, <c>@</c>, tab of CR begint, zou Excel als formule kunnen uitvoeren (bijv. een clubnaam
/// <c>=HYPERLINK(...)</c>); daarom krijgt die een <c>'</c> ervoor, zodat Excel hem als tekst toont (OWASP CSV Injection).
/// </summary>
public static class Csv
{
    public const char Separator = ';';

    private static readonly char[] FormulaStarters = ['=', '+', '-', '@', '\t', '\r'];
    private static readonly char[] NeedsQuotes = [Separator, '"', '\n', '\r'];

    public static string Cell(string? value)
    {
        value ??= "";
        if (value.Length > 0 && Array.IndexOf(FormulaStarters, value[0]) >= 0)
        {
            value = "'" + value;
        }

        return value.IndexOfAny(NeedsQuotes) >= 0 ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;
    }

    public static string Row(IEnumerable<string?> cells) => string.Join(Separator, cells.Select(Cell));
}
