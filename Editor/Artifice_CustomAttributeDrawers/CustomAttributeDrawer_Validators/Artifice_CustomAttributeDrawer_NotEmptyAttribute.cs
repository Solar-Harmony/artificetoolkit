using ArtificeToolkit.Attributes;
using ArtificeToolkit.Editor.Resources;
using UnityEditor;
using UnityEngine;

namespace ArtificeToolkit.Editor.Artifice_CustomAttributeDrawers.CustomAttributeDrawer_Validators
{
    [Artifice_CustomAttributeDrawer(typeof(NotEmptyAttribute))]
    public class Artifice_CustomAttributeDrawer_NotEmptyAttribute : Artifice_CustomAttributeDrawer_Validator_BASE
    {
        public override string LogMessage => _logMessage;
        public override Sprite LogSprite { get; } = Artifice_SCR_CommonResourcesHolder.instance.ErrorIcon;
        public override LogType LogType { get; } = LogType.Error;

        private string _logMessage = "";
        
        protected override bool IsApplicableToProperty(SerializedProperty property)
        {
            return property.isArray || property.propertyType == SerializedPropertyType.String;
        }

        public override bool IsValid(SerializedProperty property)
        {
            _logMessage = property.propertyType == SerializedPropertyType.String ? "String must not be empty or whitespace" : "Array cannot be empty";
            
            if (property.isArray)
                return property.arraySize > 0;
                
            if (property.propertyType == SerializedPropertyType.String)
                return !string.IsNullOrWhiteSpace(property.stringValue);
                
            return false;
        }
    }
}