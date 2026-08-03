using System;

namespace ArtificeToolkit.Attributes
{
    /// <summary>
    /// Customizes the entry name shown for a class in a <c>[TypePicker]</c> search window.
    /// When absent, the type name is nicified instead.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class TypePickerNameAttribute : Attribute
    {
        public string Name { get; }

        public TypePickerNameAttribute(string name)
        {
            Name = name;
        }
    }
}
