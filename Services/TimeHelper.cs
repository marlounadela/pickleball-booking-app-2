using Picklebook.Domain;

namespace Picklebook.Services;

public static class YardTime
{
    /// <summary>
    /// All booking dates/times are stored and interpreted in the yard's local time zone.
    /// </summary>
    public static TimeZoneInfo GetTz(Yard yard) => GetTz(yard?.TimeZoneId);

    public static TimeZoneInfo GetTz(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId)) return TimeZoneInfo.Local;
        try { return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
        catch { return TimeZoneInfo.Local; }
    }

    public static DateTime NowInZone(string? timeZoneId)
    {
        var utc = DateTime.UtcNow;
        return TimeZoneInfo.ConvertTimeFromUtc(utc, GetTz(timeZoneId));
    }

    public static DateOnly Today(string? timeZoneId) => DateOnly.FromDateTime(NowInZone(timeZoneId));

    public static string FormatTime(TimeSpan t, bool padZero = false)
    {
        var h = padZero ? t.Hours.ToString("00") : t.Hours.ToString();
        return $"{h}:{t.Minutes:00}";
    }

    public static string FormatTimeRange(TimeSpan s, TimeSpan e) => $"{FormatTime(s)} – {FormatTime(e)}";

    public static string FormatPrice(decimal amount, string? currency = "PHP")
    {
        return currency switch
        {
            "PHP" => "₱" + amount.ToString("N2"),
            "USD" => "$" + amount.ToString("N2"),
            "EUR" => "€" + amount.ToString("N2"),
            _ => $"{currency} {amount:N2}"
        };
    }
}

public static class BookingRef
{
    public static string Generate(string slug)
    {
        var prefix = string.Concat(slug.Where(char.IsLetterOrDigit)).ToUpperInvariant();
        if (prefix.Length > 4) prefix = prefix[..4];
        if (prefix.Length == 0) prefix = "PBK";
        var rnd = Random.Shared;
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var suffix = new string(Enumerable.Range(0, 5).Select(_ => alphabet[rnd.Next(alphabet.Length)]).ToArray());
        return $"{prefix}-{suffix}";
    }
}