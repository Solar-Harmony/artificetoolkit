using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ArtificeToolkit.Attributes;
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
        private Action<Type> _onSelect;

        public static Artifice_ManagedReferenceSearchProvider Create(List<Type> candidateTypes, Action<Type> onSelect)
        {
            var provider = CreateInstance<Artifice_ManagedReferenceSearchProvider>();
            provider._candidateTypes = candidateTypes ?? new List<Type>();
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
            foreach (var type in _candidateTypes.OrderBy(GetDisplayName, StringComparer.Ordinal))
            {
                // Guard against same display name from different namespaces.
                var displayName = GetDisplayName(type);
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

        public static string GetDisplayName(Type type)
        {
            var pickerName = type.GetCustomAttribute<TypePickerNameAttribute>();
            if (pickerName != null && !string.IsNullOrEmpty(pickerName.Name))
                return pickerName.Name;

            return ObjectNames.NicifyVariableName(type.Name);
        }
    }
}
