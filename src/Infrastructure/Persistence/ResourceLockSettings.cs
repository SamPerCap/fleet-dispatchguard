using System.Globalization;

namespace DispatchGuard.Infrastructure.Persistence;

/// <summary>The wait is for all the resources together, not for each one (SPEC 0065 R5).</summary>
public sealed record ResourceLockSettings(int LockWaitMilliseconds = ResourceLockSettings.DefaultWaitMilliseconds)
{
    public const string Key = "Allocation:LockWaitMilliseconds";

    public const int DefaultWaitMilliseconds = 5_000;

    /// <summary>A value that makes no sense stops the start.</summary>
    public static ResourceLockSettings From(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return new ResourceLockSettings();
        }

        return int.TryParse(configured, NumberStyles.None, CultureInfo.InvariantCulture, out var wait) && wait <= 60_000
            ? new ResourceLockSettings(wait)
            : throw new InvalidOperationException($"{Key} must be a number of milliseconds between 0 and 60000");
    }
}
