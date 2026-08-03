using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace ArtificeToolkit.Editor
{
    /// <summary>
    /// SearchWindow provider used by <see cref="ArtificeDrawer"/> when a <c>[SerializeReference]</c> field is
    /// annotated with <c>TypePickerAttribute</c>. Lists the assignable types in a searchable popup.
    /// </summary>
    public sealed class Artifice_ManagedReferenceSearchProvider : ScriptableObject, ISearchWindowProvider
    {
        private List<Type> _candidateTypes;
        private string _trimSuffix;
        private Action<Type> _onSelect;

        public static Artifice_ManagedReferenceSearchProvider Create(List<Type> candidateTypes, string trimSuffix, Action<Type> onSelect)
        {
            var provider = CreateInstance<Artifice_ManagedReferenceSearchProvider>();
            provider._candidateTypes = candidateTypes ?? new List<Type>();
            provider._trimSuffix = trimSuffix;
            provider._onSelect = onSelect;
            return provider;
        }

        public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
        {
            var entries = new List<SearchTreeEntry>
            {
                new SearchTreeGroupEntry(new GUIContent("Select type"))
            };

            var usedNames = new HashSet<string>();
            foreach (var type in _candidateTypes.OrderBy(t => GetDisplayName(t, _trimSuffix), StringComparer.Ordinal))
            {
                // Guard against same display name from different namespaces.
                var displayName = GetDisplayName(type, _trimSuffix);
                if (!usedNames.Add(displayName))
                    displayName = type.FullName ?? type.Name;
                entries.Add(new SearchTreeEntry(new GUIContent(displayName, type.FullName)) { level = 1, userData = type });
            }

            return entries;
        }

        public bool OnSelectEntry(SearchTreeEntry entry, SearchWindowContext context)
        {
            if (entry.userData is Type type)
                _onSelect?.Invoke(type);
            return true;
        }

        public static string GetDisplayName(Type type, string trimSuffix)
        {
            var name = type.Name;
            if (!string.IsNullOrEmpty(trimSuffix) && name.EndsWith(trimSuffix, StringComparison.Ordinal) && name.Length > trimSuffix.Length)
                name = name.Substring(0, name.Length - trimSuffix.Length);

            return ObjectNames.NicifyVariableName(name);
        }
    }
}
