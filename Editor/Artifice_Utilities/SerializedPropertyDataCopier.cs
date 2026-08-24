using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ArtificeToolkit.Editor
{
    /// <summary> Enables deep copy and pasting of serialized properties. </summary>
    public class SerializedPropertyCopier
    {
        private readonly List<PropertyData> _copiedData = new();

        private class PropertyData
        {
            public string RelativePath;
            public object Value;
            public SerializedPropertyType Type;
        }

        /// <summary> Whether the clipboard contains copied data (used to enable/disable paste actions). </summary>
        public bool HasData => _copiedData.Count > 0;

        public void Copy(SerializedProperty property)
        {
            _copiedData.Clear();

            // When copying a single managed reference element, copy the whole object as a unit.
            if (property.propertyType == SerializedPropertyType.ManagedReference)
            {
                _copiedData.Add(new PropertyData
                {
                    RelativePath = "",
                    Value = Artifice_ManagedReferenceDeepCopy.DeepCopy(property.managedReferenceValue),
                    Type = property.propertyType
                });
                return;
            }

            var copy = property.Copy();
            var end = copy.GetEndProperty();
            var skipChildren = true;

            while (copy.NextVisible(skipChildren) && !SerializedProperty.EqualContents(copy, end))
            {
                skipChildren = true;

                // Managed references are copied as whole objects (deep copy), including their subtree.
                if (copy.propertyType == SerializedPropertyType.ManagedReference)
                {
                    _copiedData.Add(new PropertyData
                    {
                        RelativePath = copy.propertyPath.Substring(property.propertyPath.Length + 1),
                        Value = Artifice_ManagedReferenceDeepCopy.DeepCopy(copy.managedReferenceValue),
                        Type = copy.propertyType
                    });
                    skipChildren = false;
                    continue;
                }

                if (copy.propertyType == SerializedPropertyType.Generic)
                {
                    // Serialized classes are copied whole via boxedValue: walking their leaves mishandles 64-bit
                    // fields (e.g. LocalizedString's KeyId is truncated by intValue) and nested collections.
                    // Arrays cannot be boxed, so they keep the leaf walk below.
                    if (copy.isArray == false)
                    {
                        object value;
                        try
                        {
                            value = Artifice_ManagedReferenceDeepCopy.DeepCopy(copy.boxedValue);
                        }
                        catch
                        {
                            value = copy.boxedValue;
                        }

                        _copiedData.Add(new PropertyData
                        {
                            RelativePath = copy.propertyPath.Substring(property.propertyPath.Length + 1),
                            Value = value,
                            Type = copy.propertyType
                        });
                        skipChildren = false;
                    }
                    continue;
                }

                _copiedData.Add(new PropertyData
                {
                    RelativePath = copy.propertyPath.Substring(property.propertyPath.Length + 1),
                    Value = GetValue(copy),
                    Type = copy.propertyType
                });
            }
        }

        public void Paste(SerializedProperty property)
        {
            foreach (var data in _copiedData)
            {
                SerializedProperty targetProp;
                if (data.RelativePath.Length == 0)
                    targetProp = property;
                else
                    targetProp = property.FindPropertyRelative(data.RelativePath);

                if (targetProp == null)
                    continue;

                if (data.Type == SerializedPropertyType.ManagedReference)
                {
                    if (targetProp.propertyType == SerializedPropertyType.ManagedReference)
                        targetProp.managedReferenceValue = data.Value;
                    continue;
                }

                if (data.Type == SerializedPropertyType.Generic)
                {
                    if (targetProp.propertyType == SerializedPropertyType.Generic)
                        targetProp.boxedValue = data.Value;
                    continue;
                }

                SetValue(targetProp, data.Value, data.Type);
            }

            property.serializedObject.ApplyModifiedProperties();
        }

        private object GetValue(SerializedProperty prop)
        {
            return prop.propertyType switch
            {
                // Integer covers both Int32 and Int64: longValue preserves 64-bit keys (e.g. LocalizedString's KeyId).
                SerializedPropertyType.Integer => prop.longValue,
                SerializedPropertyType.Boolean => prop.boolValue,
                SerializedPropertyType.Float => prop.floatValue,
                SerializedPropertyType.String => prop.stringValue,
                SerializedPropertyType.Color => prop.colorValue,
                SerializedPropertyType.ObjectReference => prop.objectReferenceValue,
                SerializedPropertyType.LayerMask => prop.intValue,
                // Copy by underlying value, not display index: enumValueIndex can be -1 / out of range for values
                // that don't match a declared member, which would crash set_enumValueIndex on paste.
                SerializedPropertyType.Enum => prop.intValue,
                SerializedPropertyType.Vector2 => prop.vector2Value,
                SerializedPropertyType.Vector3 => prop.vector3Value,
                SerializedPropertyType.Vector4 => prop.vector4Value,
                SerializedPropertyType.Rect => prop.rectValue,
                SerializedPropertyType.ArraySize => prop.intValue,
                SerializedPropertyType.Character => prop.intValue,
                SerializedPropertyType.AnimationCurve => prop.animationCurveValue,
                SerializedPropertyType.Bounds => prop.boundsValue,
                SerializedPropertyType.Quaternion => prop.quaternionValue,
                _ => null
            };
        }

        private void SetValue(SerializedProperty prop, object value, SerializedPropertyType type)
        {
            switch (type)
            {
                case SerializedPropertyType.Integer: prop.longValue = (long)value; break;
                case SerializedPropertyType.Boolean: prop.boolValue = (bool)value; break;
                case SerializedPropertyType.Float: prop.floatValue = (float)value; break;
                case SerializedPropertyType.String: prop.stringValue = (string)value; break;
                case SerializedPropertyType.Color: prop.colorValue = (Color)value; break;
                case SerializedPropertyType.ObjectReference: prop.objectReferenceValue = (Object)value; break;
                case SerializedPropertyType.LayerMask: prop.intValue = (int)value; break;
                case SerializedPropertyType.Enum: prop.intValue = (int)value; break;
                case SerializedPropertyType.Vector2: prop.vector2Value = (Vector2)value; break;
                case SerializedPropertyType.Vector3: prop.vector3Value = (Vector3)value; break;
                case SerializedPropertyType.Vector4: prop.vector4Value = (Vector4)value; break;
                case SerializedPropertyType.Rect: prop.rectValue = (Rect)value; break;
                case SerializedPropertyType.ArraySize: prop.intValue = (int)value; break;
                case SerializedPropertyType.Character: prop.intValue = (int)value; break;
                case SerializedPropertyType.AnimationCurve: prop.animationCurveValue = (AnimationCurve)value; break;
                case SerializedPropertyType.Bounds: prop.boundsValue = (Bounds)value; break;
                case SerializedPropertyType.Quaternion: prop.quaternionValue = (Quaternion)value; break;
            }
        }
    }
}