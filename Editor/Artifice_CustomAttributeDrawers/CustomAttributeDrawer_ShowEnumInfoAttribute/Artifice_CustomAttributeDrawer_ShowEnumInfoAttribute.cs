using System.ComponentModel;
using System.Reflection;
using ArtificeToolkit.Attributes;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace ArtificeToolkit.Editor.Artifice_CustomAttributeDrawers.CustomAttributeDrawer_ShowEnumInfoAttribute
{
    [Artifice_CustomAttributeDrawer(typeof(ShowEnumInfoAttribute))]
    public class Artifice_CustomAttributeDrawer_ShowEnumInfoAttribute : Artifice_CustomAttributeDrawer
    {
        public override VisualElement OnPostPropertyGUI(SerializedProperty property)
        {
            property.serializedObject.Update();

            var target = property.GetTarget<object>();
            if (target == null || !target.GetType().IsEnum)
                return null;

            var container = new VisualElement();
            container.AddToClassList("enum-info-container");

            var label = new Label();
            label.AddToClassList("enum-info-label");
            label.style.unityTextAlign = new StyleEnum<TextAnchor>(TextAnchor.MiddleRight);
            label.style.unityFontStyleAndWeight = new StyleEnum<FontStyle>(FontStyle.BoldAndItalic);
            container.Add(label);

            UpdateLabel(property, label);

            label.TrackPropertyValue(property, _ => UpdateLabel(property, label));

            return container;
        }

        private static void UpdateLabel(SerializedProperty property, Label label)
        {
            property.serializedObject.Update();

            var target = property.GetTarget<object>();
            if (target == null || !target.GetType().IsEnum)
            {
                label.text = "";
                return;
            }

            var enumValue = target as System.Enum;
            var fieldInfo = target.GetType().GetField(enumValue?.ToString());
            var descriptionAttr = fieldInfo?.GetCustomAttribute<DescriptionAttribute>();

            label.text = descriptionAttr?.Description ?? "";
        }
    }
}
