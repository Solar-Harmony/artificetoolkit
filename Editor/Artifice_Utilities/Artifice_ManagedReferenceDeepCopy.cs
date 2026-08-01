using UnityEditor;
using UnityEngine;

namespace ArtificeToolkit.Editor
{
    /// <summary>
    /// Deep copy utilities for <c>[SerializeReference]</c> managed reference objects.
    /// <para>
    /// Unity's <c>SerializedProperty.arraySize++</c> clone shares <c>[SerializeReference]</c> objects between the
    /// original and the new array element (only the reference ids are copied). These helpers replace such shared
    /// objects with deep, independent copies so edits to one element never leak into another.
    /// </para>
    /// </summary>
    public static class Artifice_ManagedReferenceDeepCopy
    {
        /// <summary>
        /// Returns a deep, independent copy of a serializable object, preserving nested <c>[SerializeReference]</c> data.
        /// </summary>
        public static object DeepCopy(object source)
        {
            if (source == null)
                return null;

            var json = EditorJsonUtility.ToJson(source);
            var copy = System.Activator.CreateInstance(source.GetType());
            EditorJsonUtility.FromJsonOverwrite(json, copy);
            return copy;
        }

        /// <summary>
        /// Replaces every non-null managed reference in the serialized subtree of <paramref name="root"/> with a deep,
        /// independent copy. Nested managed references are handled by the deep copy itself.
        /// </summary>
        public static void DeepCopyManagedReferencesInSubtree(SerializedProperty root)
        {
            if (root == null)
                return;

            if (root.propertyType == SerializedPropertyType.ManagedReference)
            {
                if (root.managedReferenceValue != null)
                    root.managedReferenceValue = DeepCopy(root.managedReferenceValue);
                root.serializedObject.ApplyModifiedProperties();
                return;
            }

            var iterator = root.Copy();
            var end = iterator.GetEndProperty();
            var enterChildren = true;

            while (iterator.Next(enterChildren))
            {
                if (SerializedProperty.EqualContents(iterator, end))
                    break;

                enterChildren = true;
                if (iterator.propertyType != SerializedPropertyType.ManagedReference || iterator.managedReferenceValue == null)
                    continue;

                iterator.managedReferenceValue = DeepCopy(iterator.managedReferenceValue);
                // The deep copy already handled nested managed references, so skip the replaced subtree.
                enterChildren = false;
            }

            root.serializedObject.ApplyModifiedProperties();
        }
    }
}
