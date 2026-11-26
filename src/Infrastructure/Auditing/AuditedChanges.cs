using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using DispatchGuard.Core.Auditing;
using DispatchGuard.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DispatchGuard.Infrastructure.Auditing;

/// <summary>What is about to be saved, as it will be audited (SPEC 0085 R2). Value objects count as fields of their owner.</summary>
internal sealed class AuditedChanges
{
    /// <summary>In place of a value that must not be kept (SPEC 0085 R2).</summary>
    private const string Hidden = "•••";

    /// <summary>The timestamps of each row: the event has its own.</summary>
    private const string Timestamps = "Audit";

    private static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter() },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>A cache, and the records themselves.</summary>
    private static readonly HashSet<Type> NotAudited =
        [typeof(CachedRoute), typeof(AuditEvent), typeof(Visit), typeof(VisitEvent), typeof(RequestTrace)];

    private static readonly HashSet<string> PersonalData =
    [
        nameof(Driver.IdentificationNumber), nameof(Client.TaxId), nameof(Client.ContactEmail), nameof(Client.ContactPhone),
        nameof(Booking.Notes)
    ];

    /// <summary>Long, and derived from the rest of the attempt.</summary>
    private static readonly HashSet<string> LeftOut = [nameof(AllocationAttempt.Explanation)];

    private readonly Dictionary<(Type Entity, object Id), AuditedRow> _rows = [];

    private AuditedChanges()
    {
    }

    public bool Any => _rows.Count > 0;

    public static AuditedChanges Of(ChangeTracker tracker)
    {
        var changes = new AuditedChanges();
        foreach (var entry in tracker.Entries())
        {
            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            {
                changes.Add(entry);
            }
        }

        foreach (var (key, _) in changes._rows.Where(row => !row.Value.SaysSomething).ToList())
        {
            changes._rows.Remove(key);
        }

        return changes;
    }

    /// <summary>After saving, when the new rows already have their id.</summary>
    public IReadOnlyList<(string Action, string Entity, int? Id, string Changes)> ToEvents() =>
        [.. _rows.Values.Select(row => (row.Action, row.Entity, row.Id, JsonSerializer.Serialize(row.Fields, Json)))];

    private void Add(EntityEntry entry)
    {
        var ownership = entry.Metadata.FindOwnership();
        if (ownership is null)
        {
            if (!NotAudited.Contains(entry.Metadata.ClrType))
            {
                var row = RowOf(entry.Metadata.ClrType, KeyOf(entry, entry.Metadata.FindPrimaryKey()!.Properties));
                row.Entry = entry;
                AddFields(row, entry, string.Empty);
            }
        }
        else if (ownership.PrincipalToDependent is { Name: not Timestamps } navigation)
        {
            var row = RowOf(ownership.PrincipalEntityType.ClrType, KeyOf(entry, ownership.Properties));
            AddFields(row, entry, ownership.IsUnique ? $"{navigation.Name}." : $"{navigation.Name}[{PositionOf(entry)}].");
        }
    }

    private AuditedRow RowOf(Type entity, object id)
    {
        if (!_rows.TryGetValue((entity, id), out var row))
        {
            _rows[(entity, id)] = row = new AuditedRow(entity.Name, id);
        }

        return row;
    }

    private static object KeyOf(EntityEntry entry, IReadOnlyList<IProperty> key) => entry.Property(key[0].Name).CurrentValue!;

    /// <summary>In the list of its owner: the last part of its key.</summary>
    private static object? PositionOf(EntityEntry entry) => entry.Property(entry.Metadata.FindPrimaryKey()!.Properties[^1].Name).CurrentValue;

    private static void AddFields(AuditedRow row, EntityEntry entry, string prefix)
    {
        foreach (var property in entry.Properties)
        {
            var name = property.Metadata.Name;
            if (property.Metadata.IsPrimaryKey() || (prefix.Length > 0 && property.Metadata.IsShadowProperty()) || LeftOut.Contains(name))
            {
                continue;
            }

            if (entry.State != EntityState.Added && (entry.State == EntityState.Deleted || property.IsModified))
            {
                row.Previous(prefix + name, property.OriginalValue);
            }

            if (entry.State != EntityState.Deleted && (entry.State == EntityState.Added || property.IsModified))
            {
                row.New(prefix + name, property.CurrentValue);
            }
        }
    }

    private sealed class AuditedRow(string entity, object id)
    {
        private readonly Dictionary<string, object?> _previous = new(StringComparer.Ordinal);
        private readonly Dictionary<string, object?> _new = new(StringComparer.Ordinal);

        public string Entity => entity;

        /// <summary>Null when only a value object of the row changes.</summary>
        public EntityEntry? Entry { get; set; }

        public string Action => Entry?.State switch
        {
            EntityState.Added => AuditActions.Created,
            EntityState.Deleted => AuditActions.Deleted,
            _ => AuditActions.Changed
        };

        public int? Id => (Entry?.Property(Entry.Metadata.FindPrimaryKey()!.Properties[0].Name).CurrentValue ?? id) as int?;

        public bool SaysSomething => Action != AuditActions.Changed || Fields.Count > 0;

        public SortedDictionary<string, FieldChange> Fields => new(
            _previous.Keys.Union(_new.Keys)
                .Select(name => (Name: name, Old: _previous.GetValueOrDefault(name), New: _new.GetValueOrDefault(name)))
                .Where(change => !AreEqual(change.Old, change.New))
                .ToDictionary(change => change.Name, change => Shown(change.Name, change.Old, change.New)),
            StringComparer.Ordinal);

        public void Previous(string field, object? value) => _previous[field] = value;

        public void New(string field, object? value) => _new[field] = value;

        /// <summary>A list of texts is compared by what it holds.</summary>
        private static bool AreEqual(object? old, object? current) =>
            Equals(old, current) || (old is IEnumerable<string> before && current is IEnumerable<string> after && before.SequenceEqual(after));

        private static FieldChange Shown(string field, object? old, object? current) => PersonalData.Contains(field)
            ? new FieldChange(old is null ? null : Hidden, current is null ? null : Hidden)
            : new FieldChange(old, current);
    }

    internal sealed record FieldChange(
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] object? Old,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] object? New);
}
