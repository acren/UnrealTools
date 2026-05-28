using System;

namespace LocalAutomation.Extensions.Abstractions;

/// <summary>
/// Converts special runtime setting value types to and from stable serialized representations so hosts can save settings
/// values without serializing live runtime objects.
/// </summary>
public interface ISettingValueConverter
{
    /// <summary>
    /// Gets the stable converter identifier used for diagnostics.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Returns whether this converter can handle the provided runtime setting value type.
    /// </summary>
    bool CanConvert(Type valueType);

    /// <summary>
    /// Converts a live runtime setting value into a stable serialized representation.
    /// </summary>
    object? Serialize(Type valueType, object? value);

    /// <summary>
    /// Rehydrates a serialized value into the runtime type used by the settings property.
    /// </summary>
    object? Deserialize(Type valueType, object? serializedValue);
}
