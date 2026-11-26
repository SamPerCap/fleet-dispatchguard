namespace DispatchGuard.Core.Auditing;

/// <summary>One change of the data, written in its same transaction (SPEC 0085 R2). Only added: never changed, and deleted when it expires.</summary>
public sealed class AuditEvent
{
    public const int DeclaredNameMaxLength = 100;
    public const int ActionMaxLength = 20;
    public const int EntityMaxLength = 60;

    public long Id { get; init; }
    public DateTime At { get; init; }

    /// <summary>Null outside a request.</summary>
    public string? SessionId { get; init; }

    public string? TraceId { get; init; }

    /// <summary>Whatever the visitor typed as their name: nobody checks it.</summary>
    public string? DeclaredName { get; init; }

    public string Action { get; init; } = string.Empty;
    public string Entity { get; init; } = string.Empty;
    public int? EntityId { get; init; }

    /// <summary>JSON: the previous and the new value of each field.</summary>
    public string Changes { get; init; } = "{}";
}

public static class AuditActions
{
    public const string Created = "Created";
    public const string Changed = "Changed";
    public const string Deleted = "Deleted";

    /// <summary>The demo of the day: one event for everything it deletes and loads.</summary>
    public const string Reset = "Reset";
}
