namespace Picklebook.Services;

public static class AppRoles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Owner = "Owner";
    public const string Player = "Player";

    public static readonly string[] All = { SuperAdmin, Owner, Player };
}

public static class AppConstants
{
    public const string RootDomain = "picklebook.com";
    public const string AdminSubdomain = "app";
    public const string DefaultCurrency = "PHP";
    public const string DefaultTimeZone = "Asia/Manila";
    public const string MainUrl = "https://picklebook.com";
}