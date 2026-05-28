using System;
using System.Collections.Generic;

namespace LocalAutomation.Application;

/// <summary>
/// Validates generated persisted setting keys from already-discovered descriptor sets without owning any settings category policy.
/// </summary>
internal static class PersistedSettingKeyValidator
{
    /// <summary>
    /// Fails startup validation when two descriptor sets define the same generated key for different owners.
    /// </summary>
    public static void Validate(IEnumerable<(string OwnerLabel, IReadOnlyList<PersistedSettingDescriptor> Descriptors)> descriptorSets)
    {
        if (descriptorSets == null)
        {
            throw new ArgumentNullException(nameof(descriptorSets));
        }

        Dictionary<string, string> seenKeys = new(StringComparer.Ordinal);
        foreach ((string ownerLabel, IReadOnlyList<PersistedSettingDescriptor> descriptors) in descriptorSets)
        {
            string resolvedOwnerLabel = string.IsNullOrWhiteSpace(ownerLabel) ? "<unknown>" : ownerLabel;
            foreach (PersistedSettingDescriptor descriptor in descriptors)
            {
                if (seenKeys.TryGetValue(descriptor.Key, out string? existingOwner) && !string.Equals(existingOwner, resolvedOwnerLabel, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Persisted setting key '{descriptor.Key}' is defined by both '{existingOwner}' and '{resolvedOwnerLabel}'.");
                }

                seenKeys[descriptor.Key] = resolvedOwnerLabel;
            }
        }
    }
}
