using System;
using UnityEngine;

namespace ArtificeToolkit.Attributes
{
    /// <summary>
    /// Marks a <c>[SerializeReference]</c> field (or a <c>TypeReference</c>-like field) so its type selector
    /// is rendered as a searchable popup instead of a plain dropdown.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class TypePickerAttribute : PropertyAttribute
    {
        public bool IncludeBaseType { get; set; }
        public bool AllowAbstract { get; set; }
        public bool GroupByNamespace { get; set; } = true;
    }
}
