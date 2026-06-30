using System.Linq;
using ArtificeToolkit.Attributes;
using ArtificeToolkit.Editor.Resources;
using UnityEditor;
using UnityEngine;

namespace ArtificeToolkit.Editor.Artifice_CustomAttributeDrawers.CustomAttributeDrawer_Validators
{
    [Artifice_CustomAttributeDrawer(typeof(MaxLengthAttribute))]
    public class Artifice_CustomAttributeDrawer_MaxLengthAttribute : Artifice_CustomAttributeDrawer_Validator_BASE
    {
        public override string LogMessage => _logMessage;
        public override Sprite LogSprite => Artifice_SCR_CommonResourcesHolder.instance.ErrorIcon;
        public override LogType LogType => LogType.Error;

        private string _logMessage = "";
        
        protected override bool IsApplicableToProperty(SerializedProperty property)
        {
            return property.propertyType == SerializedPropertyType.String;
        }

        public override bool IsValid(SerializedProperty property)
        {
            var attribute = (MaxLengthAttribute)property.GetCustomAttributes().FirstOrDefault(attribute => attribute is MaxLengthAttribute);
            if (attribute == null)
            {
                attribute = (MaxLengthAttribute)property.FindParentProperty().GetCustomAttributes().FirstOrDefault(parentAttribute => parentAttribute is MaxLengthAttribute);
                Debug.Assert(attribute != null , "Cannot find where the property was injected from.");
            }
            
            _logMessage = $"String cannot exceed {attribute.Length} characters.";

            return property.stringValue.Length <= attribute.Length;
        }
    }
}