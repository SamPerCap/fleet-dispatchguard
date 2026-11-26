using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;

namespace Core.Tests.ValueObjects;

public class VehicleEligibilityCheckTests
{
    [Fact]
    public void Eligible_ProducesIsEligibleTrue_WithNoViolations()
    {
        var check = VehicleEligibilityCheck.Eligible();

        Assert.True(check.IsEligible);
        Assert.Empty(check.Violations);
    }

    [Fact]
    public void Ineligible_WithViolations_ProducesIsEligibleFalse_AndSameViolations()
    {
        var violations = new[]
        {
            ComplianceViolation.Critical("VehicleEligibility.NotActive", Texts.AsWritten("Vehicle is not active")),
            ComplianceViolation.Critical(
                "VehicleEligibility.InsufficientCapacity",
                Texts.AsWritten("Insufficient capacity: 30 seats available, 40 required")),
        };

        var check = VehicleEligibilityCheck.Ineligible(violations);

        Assert.False(check.IsEligible);
        Assert.Equal(2, check.Violations.Count);
        Assert.Same(violations[0], check.Violations.ElementAt(0));
        Assert.Same(violations[1], check.Violations.ElementAt(1));
    }

    [Fact]
    public void Ineligible_WithSingleViolation_ContainsThatViolation()
    {
        var violation = ComplianceViolation.Critical(
            "VehicleEligibility.Unavailable.Maintenance",
            Texts.AsWritten("Vehicle under maintenance until 2026-07-05"));

        var check = VehicleEligibilityCheck.Ineligible(new[] { violation });

        Assert.False(check.IsEligible);
        var single = Assert.Single(check.Violations);
        Assert.Equal("VehicleEligibility.Unavailable.Maintenance", single.RuleId);
        Assert.Equal(ViolationSeverity.Critical, single.Severity);
    }

    [Fact]
    public void Ineligible_WithEmptyCollection_ThrowsArgumentException()
    {
        var emptyViolations = Array.Empty<ComplianceViolation>();

        Assert.ThrowsAny<ArgumentException>(() => VehicleEligibilityCheck.Ineligible(emptyViolations));
    }

    [Fact]
    public void Ineligible_WithNullCollection_ThrowsArgumentException()
    {
        // A null collection carries no violations either: it must be rejected the same way, not with a NullReferenceException.
        Assert.ThrowsAny<ArgumentException>(() => VehicleEligibilityCheck.Ineligible(null!));
    }

    [Fact]
    public void Eligible_CheckedAt_IsStampedNearUtcNow()
    {
        var before = DateTime.UtcNow;

        var check = VehicleEligibilityCheck.Eligible();
        var after = DateTime.UtcNow;

        Assert.InRange(check.CheckedAt, before.AddSeconds(-1), after.AddSeconds(1));
    }

    [Fact]
    public void Ineligible_CheckedAt_IsStampedNearUtcNow()
    {
        var before = DateTime.UtcNow;
        var violations = new[]
        {
            ComplianceViolation.Critical("VehicleEligibility.NotActive", Texts.AsWritten("Vehicle is not active")),
        };

        var check = VehicleEligibilityCheck.Ineligible(violations);
        var after = DateTime.UtcNow;

        Assert.InRange(check.CheckedAt, before.AddSeconds(-1), after.AddSeconds(1));
    }
}
