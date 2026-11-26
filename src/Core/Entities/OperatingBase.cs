using DispatchGuard.Core.Entities.Base;

namespace DispatchGuard.Core.Entities;

/// <summary>Not named <c>Base</c> because it would collide with the <c>Entities.Base</c> namespace.</summary>
public class OperatingBase : Entity
{
    /// <summary>Created by the 0050 migration, for the data that existed before bases.</summary>
    public const int DefaultId = 1;
    public const int NameMaxLength = 100;

    public string Name { get; private set; } = string.Empty;

    private OperatingBase() { }

    public OperatingBase(string name)
    {
        Rename(name);
    }

    public void Rename(string name)
    {
        EntityValidator.ValidateRequiredString(name, nameof(name));
        if (name.Trim().Length > NameMaxLength)
            throw new InvalidRequestException(Texts.TooLong(Texts.NameCannot(), NameMaxLength));
        Name = name.Trim();
        MarkAsUpdated();
    }
}
