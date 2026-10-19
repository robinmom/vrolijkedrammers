using System.Globalization;
using System.Text;

namespace Drammers.Api.Content;

/// <summary>Minimale iCalendar-export (RFC 5545): één activiteit ("Zet in agenda") of de hele agenda als abonnement-feed.</summary>
public static class IcsWriter
{
    /// <summary>Eén activiteit, voor de losse "Zet in agenda"-export.</summary>
    public static string Write(Guid id, string title, string? description, string? location, DateTime startUtc, DateTime? endUtc, bool allDay, DateTime stampUtc)
    {
        var sb = new StringBuilder();
        BeginCalendar(sb);
        WriteEvent(sb, id, title, description, location, url: null, startUtc, endUtc, allDay, stampUtc);
        EndCalendar(sb);
        return sb.ToString();
    }

    /// <summary>
    /// De hele openbare agenda als abonnement-feed: meerdere VEVENTs plus kalender-metadata zodat agenda-apps periodiek
    /// verversen (<c>REFRESH-INTERVAL</c>/<c>X-PUBLISHED-TTL</c>). Tijdzone- en hele-dag-afhandeling gelijk aan de losse export.
    /// </summary>
    public static string WriteCalendar(string calendarName, string timeZone, IEnumerable<IcsEvent> events, DateTime stampUtc)
    {
        var sb = new StringBuilder();
        BeginCalendar(sb);
        Line(sb, $"X-WR-CALNAME:{Escape(calendarName)}");
        Line(sb, $"X-WR-TIMEZONE:{Escape(timeZone)}");
        Line(sb, "REFRESH-INTERVAL;VALUE=DURATION:PT12H");
        Line(sb, "X-PUBLISHED-TTL:PT12H");
        foreach (var e in events)
        {
            WriteEvent(sb, e.Id, e.Title, e.Description, e.Location, e.Url, e.StartUtc, e.EndUtc, e.AllDay, stampUtc);
        }

        EndCalendar(sb);
        return sb.ToString();
    }

    private static void BeginCalendar(StringBuilder sb)
    {
        Line(sb, "BEGIN:VCALENDAR");
        Line(sb, "VERSION:2.0");
        Line(sb, "PRODID:-//De Vrolijke Drammers//App//NL");
        Line(sb, "CALSCALE:GREGORIAN");
    }

    private static void EndCalendar(StringBuilder sb) => Line(sb, "END:VCALENDAR");

    private static void WriteEvent(
        StringBuilder sb, Guid id, string title, string? description, string? location, string? url, DateTime startUtc, DateTime? endUtc, bool allDay, DateTime stampUtc)
    {
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

        if (!string.IsNullOrWhiteSpace(url))
        {
            Line(sb, $"URL:{Escape(url)}");
        }

        Line(sb, "END:VEVENT");
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

/// <summary>Eén activiteit voor de agenda-feed; tijden in UTC, net als de losse export.</summary>
public sealed record IcsEvent(Guid Id, string Title, string? Description, string? Location, string? Url, DateTime StartUtc, DateTime? EndUtc, bool AllDay);
