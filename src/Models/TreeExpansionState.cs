using System.Collections.Concurrent;

namespace SourceGit.Models
{
    public class TreeExpansionState
    {
        public ConcurrentDictionary<string, bool> ManualStates { get; set; } = new();

        public bool GetIsExpanded(string path, bool defaultValue)
        {
            return ManualStates.TryGetValue(path, out var isExpanded) ? isExpanded : defaultValue;
        }

        public void SetExpanded(string path, bool isExpanded)
        {
            ManualStates[path] = isExpanded;
        }
    }
}
