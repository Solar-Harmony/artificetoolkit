using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;

namespace ArtificeToolkit.Editor
{
    /// <summary>
    /// Extension point that lets external editor assemblies replace the default <c>DropdownField</c> selector used
    /// for <c>[SerializeReference]</c> fields (e.g. with a searchable type picker). The delegates are set at editor
    /// load by extension assemblies (typically via <c>[InitializeOnLoad]</c>). When unset, Artifice falls back to its
    /// default dropdown behavior.
    /// </summary>
    public static class Artifice_SerializeReferenceSelector
    {
        /// <summary>
        /// Returns true when the property must be rendered through the serialize-reference field even when its managed
        /// reference is null (i.e. a custom selector is wanted). Set by extension assemblies.
        /// </summary>
        public static Func<SerializedProperty, bool> RequiresSelector;

        /// <summary>
        /// Builds a custom selector element for the property. <paramref name="typeMap"/> maps display name to type.
        /// <paramref name="onSelect"/> swaps/instantiates the managed reference (already wired to Undo and validator
        /// refresh). Returns null to fall back to the default dropdown. The returned element is responsible for
        /// tracking the property and updating its own label.
        /// </summary>
        public static Func<SerializedProperty, IReadOnlyDictionary<string, Type>, Action<Type>, VisualElement> TryCreate;
    }
}
