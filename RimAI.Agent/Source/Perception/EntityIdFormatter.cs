using System;

namespace RimAI.Agent.Perception
{
    /// <summary>
    /// Converts RimWorld load-reference IDs to the canonical typed IDs shared
    /// between Perception and Action.
    /// </summary>
    public static class EntityIdFormatter
    {
        public static string FromLoadId(string prefix, string uniqueLoadId)
        {
            if (string.IsNullOrEmpty(uniqueLoadId)) return null;
            if (string.IsNullOrEmpty(prefix)) return uniqueLoadId;

            var separator = uniqueLoadId.LastIndexOf('_');
            var suffix = separator >= 0 && separator < uniqueLoadId.Length - 1
                ? uniqueLoadId.Substring(separator + 1)
                : uniqueLoadId;

            return prefix + suffix;
        }
    }
}
