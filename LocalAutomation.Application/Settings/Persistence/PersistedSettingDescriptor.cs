using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using LocalAutomation.Runtime;

namespace LocalAutomation.Application;

/// <summary>
/// Describes one runtime-editable persisted property and the metadata needed to read and write it.
/// </summary>
public sealed class PersistedSettingDescriptor
{
    // Properties without an explicit write scope use the user override layer because user edits should stay above lower-priority layers.
    private const PersistenceScope DefaultWriteScope = PersistenceScope.UserTargetOverride;

    /// <summary>
    /// Creates a descriptor for one persisted property.
    /// </summary>
    public PersistedSettingDescriptor(PropertyInfo property, string key, PersistenceScope writeScope)
    {
        Property = property ?? throw new ArgumentNullException(nameof(property));
        Key = key ?? throw new ArgumentNullException(nameof(key));
        WriteScope = writeScope;
        ValueType = property.PropertyType;
    }

    /// <summary>
    /// Gets the reflected property that owns the value.
    /// </summary>
    public PropertyInfo Property { get; }

    /// <summary>
    /// Gets the stable persistence key for this property.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Gets the storage layer that should receive edits for this property.
    /// </summary>
    public PersistenceScope WriteScope { get; }

    /// <summary>
    /// Gets the underlying value type that should be serialized.
    /// </summary>
    public Type ValueType { get; }

    /// <summary>
    /// Reflects descriptors for one settings owner type using an owning-system supplied key prefix.
    /// </summary>
    internal static IReadOnlyList<PersistedSettingDescriptor> CreateForOwner(Type ownerType, string ownerPrefix)
    {
        if (ownerType == null)
        {
            throw new ArgumentNullException(nameof(ownerType));
        }

        if (string.IsNullOrWhiteSpace(ownerPrefix))
        {
            throw new ArgumentException("Owner prefix must be provided.", nameof(ownerPrefix));
        }

        return ownerType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead)
            .Where(property => property.GetCustomAttribute<BrowsableAttribute>()?.Browsable != false)
            .Select(property =>
            {
                PersistedValueAttribute? attribute = property.GetCustomAttribute<PersistedValueAttribute>();
                string key = !string.IsNullOrWhiteSpace(attribute?.Key)
                    ? attribute!.Key!
                    : ownerPrefix + "." + ToCamelCase(property.Name);
                PersistenceScope writeScope = attribute?.WriteScope ?? DefaultWriteScope;
                return new PersistedSettingDescriptor(property, key, writeScope);
            })
            .ToList();
    }

    /// <summary>
    /// Resolves the stable owner segment used by owning systems when they generate descriptor key prefixes.
    /// </summary>
    internal static string ResolveOwnerSegment(Type ownerType)
    {
        if (ownerType == null)
        {
            throw new ArgumentNullException(nameof(ownerType));
        }

        PersistedSettingsAttribute? attribute = ownerType.GetCustomAttribute<PersistedSettingsAttribute>();
        return !string.IsNullOrWhiteSpace(attribute?.KeyPrefix)
            ? attribute!.KeyPrefix
            : ToCamelCase(ownerType.Name);
    }

    /// <summary>
    /// Converts a PascalCase identifier into the camelCase key segment used by generated setting ids.
    /// </summary>
    private static string ToCamelCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (value.Length == 1)
        {
            return value.ToLowerInvariant();
        }

        return char.ToLowerInvariant(value[0]) + value.Substring(1);
    }
}
