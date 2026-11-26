using DispatchGuard.Core.Entities;

namespace DispatchGuard.Core.Auditing;

/// <summary>A session whose web has said something, once (SPEC 0085): the author is told when it is added.</summary>
public sealed class Visit
{
    public const int SessionIdLength = AllocationAttempt.SessionIdLength;
    public const int ReferenceMaxLength = 32;

    public string SessionId { get; init; } = string.Empty;
    public DateTime StartedAt { get; init; }

    /// <summary>Code of the link it came with; null without one that is known.</summary>
    public string? Reference { get; init; }
}

public enum VisitEventKind
{
    /// <summary>Through the link of an application.</summary>
    Arrived,
    Screen,
    Click,
    FormOpened,
    FormSent,
    FormAbandoned,
    RequestFailed,
    Error
}

/// <summary>Something done in the web. Its name is one of a list in the code of the web: never what is typed or shown.</summary>
public sealed class VisitEvent
{
    public const int NameMaxLength = 80;
    public const int KindMaxLength = 20;

    public long Id { get; init; }
    public string SessionId { get; init; } = string.Empty;
    public DateTime At { get; init; }
    public string? TraceId { get; init; }
    public VisitEventKind Kind { get; init; }
    public string Name { get; init; } = string.Empty;

    /// <summary>Only of a request; 0 if it had no answer.</summary>
    public int? Status { get; init; }

    public int? DurationMs { get; init; }
}

/// <summary>Kinds of line of a timeline that are not a <see cref="VisitEventKind"/> nor one of <see cref="AuditActions"/>.</summary>
public static class TimelineKinds
{
    public const string Request = "Request";
}

/// <summary>What a request did inside (SPEC 0085 R6).</summary>
public sealed class RequestTrace
{
    public const int TraceIdLength = 32;
    public const int MethodMaxLength = 10;
    public const int PathMaxLength = 200;
    public const int DetailMaxLength = 8_000;

    public long Id { get; init; }
    public string SessionId { get; init; } = string.Empty;
    public DateTime At { get; init; }
    public string TraceId { get; init; } = string.Empty;
    public string Method { get; init; } = string.Empty;

    /// <summary>Without the query string, where there may be something typed.</summary>
    public string Path { get; init; } = string.Empty;

    public int Status { get; init; }
    public int DurationMs { get; init; }
    public int Queries { get; init; }
    public int QueriesMs { get; init; }

    /// <summary>JSON: rules evaluated and queries, without their values.</summary>
    public string Detail { get; init; } = "{}";
}
