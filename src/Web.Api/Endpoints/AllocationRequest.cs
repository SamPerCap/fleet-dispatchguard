using System.ComponentModel.DataAnnotations;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.API.Endpoints;

/// <summary>In the body and not in the URL, which logs and histories keep: it carries a name and a reason (SPEC 0089).</summary>
public sealed record AllocationRequest
{
    public required int DriverId { get; init; }

    public required int VehicleId { get; init; }

    [StringLength(AllocationAttempt.AttemptedByMaxLength, ErrorMessageResourceType = typeof(AnnotationTexts), ErrorMessageResourceName = nameof(AnnotationTexts.NameLength))]
    public string? AssignedBy { get; init; }

    public bool Force { get; init; }

    [StringLength(AllocationOverride.MaxReasonLength, ErrorMessageResourceType = typeof(AnnotationTexts), ErrorMessageResourceName = nameof(AnnotationTexts.ReasonLength))]
    public string? Reason { get; init; }

    public AttemptOrigin Origin { get; init; } = AttemptOrigin.Manual;

    /// <summary>Protects whoever assigned the trip after the recommendation was calculated.</summary>
    public bool OnlyIfUnassigned { get; init; }

    public AllocationOverride? ToOverride() => Force ? AllocationOverride.Create(Reason, AssignedBy) : null;
}
