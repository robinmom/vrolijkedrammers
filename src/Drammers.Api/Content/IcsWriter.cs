using System.Globalization;
using System.Text;

namespace Drammers.Api.Content;

/// <summary>Minimale iCalendar-export (RFC 5545) van één event, voor "Zet in agenda".</summary>
public static class IcsWriter
{
    public static string Write(Guid id, string title, string? description, string? location, DateTime startUtc, DateTime? endUtc, bool allDay, DateTime stampUtc)
    {
        var sb = new StringBuilder();
        Line(sb, "BEGIN:VCALENDAR");
        Line(sb, "VERSION:2.0");
        Line(sb, "PRODID:-//De Vrolijke Drammers//App//NL");
        Line(sb, "CALSCALE:GREGORIAN");
        Line(sb, "BEGIN:VEVENT");
        Line(sb, $"UID:{id}@vrolijkedrammers.nl");
        Line(sb, $"DTSTAMP:{Utc(stampUtc)}");
        if (allDay)
        {
            var start = DateOnly.FromDateTime(startUtc);
            var end = endUtc is { } e ? DateOnly.FromDateTime(e).AddDays(1) : start.AddDays(1);
            Line(sb, $"DTSTART;VALUE=DATE:{start:yyyyMMdd}");
            Line(sb, $"DTEND;VALUE=DATE:{end:yyyyMMdd}");
        }
        else
        {
            Line(sb, $"DTSTART:{Utc(startUtc)}");
            Line(sb, $"DTEND:{Utc(endUtc ?? startUtc.AddHours(2))}");
        }

        Line(sb, $"SUMMARY:{Escape(title)}");
        if (!string.IsNullOrWhiteSpace(description))
        {
            Line(sb, $"DESCRIPTION:{Escape(description)}");
        }

        if (!string.IsNullOrWhiteSpace(location))
        {
            Line(sb, $"LOCATION:{Escape(location)}");
        }

        Line(sb, "END:VEVENT");
        Line(sb, "END:VCALENDAR");
        return sb.ToString();
    }

    private static string Utc(DateTime value) => value.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace(";", "\\;", StringComparison.Ordinal)
            .Replace(",", "\\,", StringComparison.Ordinal).Replace("\r\n", "\\n", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);

    /// <summary>Regels afsluiten met CRLF en vouwen na 75 octetten (RFC 5545 §3.1).</summary>
    private static void Line(StringBuilder sb, string line)
    {
        var bytes = Encoding.UTF8.GetBytes(line);
        var start = 0;
        var first = true;
        while (start < bytes.Length)
        {
            var max = first ? 75 : 74;
            var length = Math.Min(max, bytes.Length - start);
            while (start + length < bytes.Length && (bytes[start + length] & 0xC0) == 0x80)
            {
                length--;
            }

            if (!first)
            {
                sb.Append(' ');
            }

            sb.Append(Encoding.UTF8.GetString(bytes, start, length)).Append("\r\n");
            start += length;
            first = false;
        }
    }
}
