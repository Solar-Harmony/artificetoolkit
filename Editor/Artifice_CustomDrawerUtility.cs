using UnityEditor;
using UnityEngine.UIElements;
using System.Reflection;
using System;
using UnityEditor.UIElements;
using UnityEngine;

namespace ArtificeToolkit.Editor
{
    /// <summary> Code written with the help of Unity Support. </summary>
    public static class Artifice_CustomDrawerUtility
    {
        private static Type ScriptAttributeUtility => Type.GetType("UnityEditor.ScriptAttributeUtility, UnityEditor");

        private static MethodInfo _getHandler = null;
        private static MethodInfo GetHandlerMethod
        {
            get
            {
                if (_getHandler == null)
                {
                    _getHandler = ScriptAttributeUtility.GetMethod("GetHandler", BindingFlags.Static | BindingFlags.NonPublic);
                }
                return _getHandler;
            }
        }

        private static object _nullHandler = null;
        private static object NullHandler
        {
            get
            {
                if (_nullHandler == null)
                {
                    var field = ScriptAttributeUtility.GetField("s_SharedNullHandler", BindingFlags.Static | BindingFlags.NonPublic);
                    if (field == null)
                        throw new ArgumentException("Could not find \'s_SharedNullHandler\'.");
                    
                    _nullHandler = field.GetValue(null);
                }
                return _nullHandler;
            }
        }

        /// <summary>
        /// Checks if the SerializedProperty has a custom property drawer.
        /// </summary>
        /// <param name="property">The serialized property.</param>
        /// <returns>True if the property has a custom drawer, false otherwise.</returns>
        public static bool HasCustomDrawer(SerializedProperty property)
        {
            var handler = GetHandlerMethod.Invoke(null, new object[] { property });
            return handler != NullHandler;
        }

        /// <summary>
        /// Creates the property GUI using the custom drawer of the SerializedProperty.
        /// </summary>
        /// <param name="property">The serialized property.</param>
        /// <returns>A VisualElement representing the custom drawer GUI for the property.</returns>
        public static VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            // Assert that the property has a custom drawer
            if (!HasCustomDrawer(property))
                throw new InvalidOperationException($"Property '{property.name}' does not have a custom property drawer.");

            // Retrieve the handler using reflection
            var handler = GetHandlerMethod.Invoke(null, new object[] { property });

            // Check if handler has the method for creating the property GUI in the new UI Toolkit (VisualElement system)
            var handlerType = handler.GetType();
            
            // Get property drawer method and create property GUI if not null.
            var getPropertyDrawerMethod = handlerType.GetMethod("get_propertyDrawer", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (getPropertyDrawerMethod != null)
            {
                var propertyDrawer = (PropertyDrawer)getPropertyDrawerMethod.Invoke(handler, new object[]{});
                if (propertyDrawer == null)
                    return null;
                
                return propertyDrawer.CreatePropertyGUI(property);
            }
            
            // Fallback to a default property field if no CreatePropertyGUI method is found
            return new PropertyField(property);
        }

        /// <summary>
        /// Creates an IMGUI-based property GUI wrapping the custom drawer's OnGUI/GetPropertyHeight.
        /// Used when the drawer only implements IMGUI (no CreatePropertyGUI override): a bound
        /// PropertyField is unreliable in that case, since fields created during a list rebuild
        /// (e.g. Artifice list views) never populate their children until the inspector is re-selected.
        /// </summary>
        public static VisualElement CreateIMGUIPropertyGUI(SerializedProperty property)
        {
            if (!HasCustomDrawer(property))
                return null;

            var handler = GetHandlerMethod.Invoke(null, new object[] { property });

            var getPropertyDrawerMethod = handler.GetType().GetMethod("get_propertyDrawer", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (getPropertyDrawerMethod == null)
                return null;

            var propertyDrawer = (PropertyDrawer)getPropertyDrawerMethod.Invoke(handler, new object[] { });
            if (propertyDrawer == null)
                return null;

            // Use a stable copy of the property handle: the original handle can become stale when the
            // owning serialized object is rebuilt, while a copy refreshes itself on each serializedObject access.
            var propertyCopy = property.Copy();
            var label = new GUIContent(property.displayName, property.tooltip);

            var container = new IMGUIContainer(() =>
            {
                propertyCopy.serializedObject.UpdateIfRequiredOrScript();

                var height = propertyDrawer.GetPropertyHeight(propertyCopy, label);
                var rect = GUILayoutUtility.GetRect(EditorGUIUtility.currentViewWidth, height);

                EditorGUI.BeginChangeCheck();
                propertyDrawer.OnGUI(rect, propertyCopy, label);
                if (EditorGUI.EndChangeCheck())
                    propertyCopy.serializedObject.ApplyModifiedProperties();
            });

            container.AddToClassList(PropertyField.ussClassName);

            return container;
        }
    }
    
}