using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ArtificeToolkit.Attributes;
using ArtificeToolkit.Editor.Artifice_CustomAttributeDrawers;
using ArtificeToolkit.Editor.Artifice_CustomAttributeDrawers.CustomAttributeDrawer_ButtonAttribute;
using ArtificeToolkit.Editor.Artifice_CustomAttributeDrawers.CustomAttributeDrawers_Groups;
using ArtificeToolkit.Editor.Resources;
using ArtificeToolkit.Editor.VisualElements;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

// ReSharper disable GCSuppressFinalizeForTypeWithoutDestructor
// ReSharper disable CanSimplifyDictionaryLookupWithTryGetValue
// ReSharper disable MemberCanBeMadeStatic.Local
// ReSharper disable RedundantIfElseBlock

namespace ArtificeToolkit.Editor
{
    [InitializeOnLoad]
    public sealed class ArtificeDrawer : IDisposable
    {
        #region FIELDS

        private readonly Stack<IDisposable> _disposableStack = new();
        private bool _disposed;

        // Cached results for custom attribute usage
        private readonly Dictionary<SerializedProperty, bool> _doesRequireVisualElementsCache = new();
        
        // Type cache for performance
        private static readonly Dictionary<string, Type> TypeCache = new();

        /// <summary> String properties that should be ignored from Artifice. </summary>
        private static readonly HashSet<string> PropertyIgnoreSet;

        private static readonly HashSet<Type> DefaultRenderingTypes;

        // Delegate declaration for serialized property filter method.
        public delegate bool SerializedPropertyFilter(SerializedProperty property);
        private SerializedPropertyFilter _serializedPropertyFilter = property => true;

        // References to children visual elements
        private VisualElement _artificeInspectorIndicator;
        
        #endregion

        /// <summary> Static constructor initializes ArrayAppliedCustomAttributes variable since its reused for all artifice drawer instances. </summary>
        static ArtificeDrawer()
        {
            // Refresh toggle of artifice drawer to secure consistency throughout package updates.
            Artifice_Utilities.ToggleArtificeDrawer(Artifice_Utilities.ArtificeDrawerEnabled);
            
            PropertyIgnoreSet = new HashSet<string>()
            {
                // "Serialized Data Mode Controller",
                "Serialized Data Mode Controller",
            };
            
            DefaultRenderingTypes = new HashSet<Type>
            {
                typeof(Quaternion),
                typeof(Vector2),
                typeof(Vector2Int),
                typeof(Vector3),
                typeof(Vector3Int),
            };
        }
        
        /// <summary> Returns the ArtificeInspector of a SerializedObject. </summary>
        public VisualElement CreateInspectorGUI(SerializedObject serializedObject)
        {
            // Do nothing while compiling.
            if (EditorApplication.isCompiling)
                return new VisualElement();
            
            // Make sure serialized object is updated
            serializedObject.Update(); 
            
            // Create initialized artifice inspector container
            var artificeInspector = CreateArtificeInspectorContainerGUI(serializedObject);
            
            // Check whether target object is missing
            if (serializedObject.targetObject == null)
            {
                artificeInspector.Add(CreateScriptMissingUI(serializedObject));
                return artificeInspector;
            }
            
            // Fully render out its visible children properties
            foreach (var property in serializedObject.GetIterator().GetVisibleChildren().SortProperties())
            {
                if (PropertyIgnoreSet.Contains(property.displayName))
                    continue;
                
                if (Artifice_Utilities.MScriptShouldHide && property.name == "m_Script")
                    continue; 
                
                artificeInspector.Add(CreatePropertyGUI(property.Copy()));
            }

            // Create optional method buttons Foldout Group for serializedObject
            artificeInspector.Add(CreateMethodsGUI(serializedObject));
            
            // Add artifice indicator if artifice has been used.
            var targetObject = serializedObject.targetObject;
            if (targetObject != null && !targetObject.GetType().IsSubclassOf(typeof(EditorWindow)) &&
                _doesRequireVisualElementsCache.Any(pair => pair.Value))
            {
                _artificeInspectorIndicator = CreateArtificeIndicatorGUI(serializedObject);
                artificeInspector.Add(_artificeInspectorIndicator);
            }
            
            // Apply any modified property
            serializedObject.ApplyModifiedProperties();

            return artificeInspector;
        }

        /// <summary> Returns an initialized VisualElement container to be used for the Artifice inspector </summary>
        private VisualElement CreateArtificeInspectorContainerGUI(SerializedObject serializedObject)
        {
            var artificeContainer = new VisualElement
            {
                name = serializedObject.GetHashCode().ToString()
            };

            // If for some reason this occurs, the Inspector would be empty and not easily debuggable.
            // Hopefully, the thrown exception will help pinpoint what went wrong.
            if (Artifice_Utilities.GetGlobalStyle() == null || Artifice_Utilities.GetStyle(GetType()) == null)
                throw new Exception("GlobalStyle or ArtificeStyle not found.");

            artificeContainer.styleSheets.Add(Artifice_Utilities.GetGlobalStyle()); // This propagates to all children.
            artificeContainer.styleSheets.Add(Artifice_Utilities.GetStyle(GetType())); // Supports
            artificeContainer.AddToClassList("artifice-inspector");
            
            return artificeContainer;
        }

        /// <summary> Receives a SerializedProperty as a parameter and returns its Artifice GUI </summary>
        public VisualElement CreatePropertyGUI(SerializedProperty property, bool forceArtificeStyle = false, bool useFoldoutForVisibleChildren = true, List<CustomAttribute> additionalCustomAttributes = null)
        {
            var container = new VisualElement();
            container.AddToClassList("property-container");

            // If filtered, return empty container.
            if (_serializedPropertyFilter.Invoke(property) == false)
                return null;

            // Check if property enforces Artifice in following calls.
            var attributes = property.GetAttributes();
            if (attributes != null)
                forceArtificeStyle = forceArtificeStyle || attributes.Any(attribute => attribute is ForceArtificeAttribute);

            // If artifice rendering is required.
            // A null managed reference annotated with TypePicker has no children/attributes of its own,
            // so it would otherwise skip the searchable selector entirely; force it through Artifice.
            if (forceArtificeStyle || DoesRequireArtificeRendering(property) || (property.IsManagedReference() && GetTypePickerAttribute(property) != null))
            {
                // Arrays need to use custom Artifice List Views (and not a string value!)
                if (property.IsArray())
                {
                    // Discern which properties are to be applied to the list and which to its children.
                    SplitCustomPropertiesForArrays(property, out var arrayCustomAttributes, out var childrenCustomAttributes);
                    
                    // Check whether it should be drawn with table list
                    var isTableList = property.GetCustomAttributes().Any(attribute => attribute.GetType() == typeof(TableListAttribute));
                        
                    // Spawn either ListView or TableView
                    var listView = isTableList ? (Artifice_VisualElement_AbstractListView)new Artifice_VisualElement_TableListView() : new Artifice_VisualElement_ListView();
                    listView.SetSerializedPropertyFilter(_serializedPropertyFilter);
                    listView.SetChildrenInjectedCustomAttributes(childrenCustomAttributes);
                    listView.ShouldForceArtifice = forceArtificeStyle;
                    listView.value = property;
                    container.Add(CreateCustomAttributesGUI(property, listView, arrayCustomAttributes));
                    
                    _disposableStack.Push(listView); // Add to disposable list
                }
                // If property is a serialized reference of an interface, allow to select which type of interface inheritors to spawn
                else if (property.IsManagedReference())
                {
                    container.Add(CreateSerializeReferenceFieldGUI(property, additionalCustomAttributes));
                }
                // If property has visible children, wrap it in a foldout to mimic unity's default behaviour or use any potential implemented custom property drawer.
                else if (property.hasVisibleChildren)
                {
                    var hasCustomPropertyDrawer = Artifice_CustomDrawerUtility.HasCustomDrawer(property);
                    if (hasCustomPropertyDrawer)
                    {
                        var customPropertyField = Artifice_CustomDrawerUtility.CreatePropertyGUI(property);
                        
                        // In case the custom property utility fails, fallback to an IMGUI container wrapping
                        // the drawer's OnGUI. A bound PropertyField is NOT used: fields created during a list
                        // rebuild (not the inspector's initial bind) never populate their children, so the
                        // field would render empty until the inspector is re-selected.
                        if (customPropertyField == null)
                            customPropertyField = Artifice_CustomDrawerUtility.CreateIMGUIPropertyGUI(property);

                        // Last-resort fallback so a null never reaches container.Add.
                        if (customPropertyField == null)
                        {
                            var fallbackField = new PropertyField(property);
                            fallbackField.BindProperty(property);
                            customPropertyField = fallbackField;
                        }
                        
                        customPropertyField = CreateCustomAttributesGUI(property, customPropertyField);
                        container.Add(customPropertyField);
                    }
                    else
                    {
                        VisualElement childrenContainer;
                    
                        if (DefaultRenderingTypes.Contains(property.GetTargetType()))
                        {
                            childrenContainer = new PropertyField(property);
                        }
                        else
                        {
                            // Optionally use foldout for visible children, or have them just placed in order.
                            if (useFoldoutForVisibleChildren)
                            {
                                childrenContainer = new Foldout
                                {
                                    value = property.isExpanded,
                                    text = property.displayName
                                };
                                childrenContainer.AddToClassList("nested-field-property");
                                ((Foldout)childrenContainer).BindProperty(property); // Bind to make foldout state (open-closed) be persistent
                            }
                            else
                                childrenContainer = new VisualElement();
                            
                            // Create property for each child
                            foreach (var child in property.GetVisibleChildren().SortProperties())
                                childrenContainer.Add(CreatePropertyGUI(child, forceArtificeStyle));
                            
                        }

                        // Create methods group
                        childrenContainer.Add(CreateMethodsGUI(property));
                        
                        container.Add(CreateCustomAttributesGUI(property, childrenContainer));
                    }
                }
                else
                {
                    var defaultPropertyField = new PropertyField(property);
                    defaultPropertyField.BindProperty(property);
                    container.Add(CreateCustomAttributesGUI(property, defaultPropertyField));
                }
            }
            else
            {
#if UNITY_2022_2_OR_NEWER
                var defaultPropertyField = new PropertyField(property);
#else
                var defaultPropertyField = CreateIMGUIField(property);
#endif
                defaultPropertyField.BindProperty(property);
                container.Add(CreateCustomAttributesGUI(property, defaultPropertyField));
            }
            
            return container;
        }

        /// <summary> Uses <see cref="CustomAttribute"/> and <see cref="Artifice_CustomAttributeDrawer"/> to change how the parameterized <see cref="VisualElement"/> will look like using the property's custom attributes. </summary>
        public VisualElement CreateCustomAttributesGUI(SerializedProperty property, VisualElement propertyField)
        {
            var customAttributes = property.GetCustomAttributes();
            return CreateCustomAttributesGUI(property, propertyField, customAttributes.ToList());
        }
        
        /// <summary> Uses <see cref="CustomAttribute"/> and <see cref="Artifice_CustomAttributeDrawer"/> to change how the parameterized <see cref="VisualElement"/> will look like with any parameterized custom attributes. </summary>
        public VisualElement CreateCustomAttributesGUI(SerializedProperty property, VisualElement propertyField, List<CustomAttribute> customAttributes)
        {
            var attributeDrawers = new List<Artifice_CustomAttributeDrawer>();
            var drawerMap = Artifice_Utilities.GetDrawerTypesMap();
            foreach (var customAttribute in customAttributes)
            {
                // Skip if drawer does not exist for custom attribute
                if (drawerMap.ContainsKey(customAttribute.GetType()) == false)
                {
                    Artifice_Utilities.LogError($"Could not find drawer type for <b>{customAttribute.GetType().Name}</b>");
                    continue;
                }
                
                // Create instance of drawer
                var attributeDrawer = (Artifice_CustomAttributeDrawer)Activator.CreateInstance(drawerMap[customAttribute.GetType()]);
                attributeDrawer.Attribute = customAttribute;
                attributeDrawers.Add(attributeDrawer);
                _disposableStack.Push(attributeDrawer); // Add to disposable stack
            }

            // Copy property because param is an iterator which will move on.
            var rootVisualElement = new VisualElement
            {
                name = property.propertyPath
            };

            // PRE GUI
            foreach (var eachAttributeDrawer in attributeDrawers)
                rootVisualElement.Add(eachAttributeDrawer.OnPrePropertyGUI(property));

            // ON GUI       (Adds first OnPropertyGUI implementation only)
            var propertyReplacementDrawer = attributeDrawers.FirstOrDefault(drawer => drawer.IsReplacingPropertyField);
            propertyField = propertyReplacementDrawer != null ? propertyReplacementDrawer.OnPropertyGUI(property) : propertyField;
            rootVisualElement.Add(propertyField);

            // POST GUI  
            foreach (var drawer in attributeDrawers)
                rootVisualElement.Add(drawer.OnPostPropertyGUI(property));

            // WRAP GUI     (Order matters a lot!)
            var wrapper = rootVisualElement;
            for (var i = attributeDrawers.Count - 1; i >= 0; i--)
                wrapper = attributeDrawers[i].OnWrapGUI(property, wrapper);
            
            // Always applied Wrap GUI for OPEN GROUPS. Skip if property was the one with the Start attribute.
            wrapper = HandleWrapForOpenGroups(property, wrapper, customAttributes);

            // ON PROPERTY BOUND GUI
            propertyField?.schedule.Execute(() =>
            {
                foreach (var drawer in attributeDrawers)
                    drawer.OnPropertyBoundGUI(property, propertyField);
            });

            return wrapper;
        }

        /// <summary> Uses <see cref="IMGUIContainer"/> to create the default UI implementation Unity would have offered. </summary>
        private VisualElement CreateIMGUIField(SerializedProperty property)
        {
            // Fallback to default IMGUI properties
            var guiContainer = new IMGUIContainer();
            guiContainer.onGUIHandler = () => CreateIMGUIFieldHandler(property);
            
            return guiContainer;
        }

        /// <summary> Used by <see cref="CreateIMGUIField"/> to create IMGUI. </summary>
        private void CreateIMGUIFieldHandler(SerializedProperty property)
        {
            property.serializedObject.Update();
            
            EditorGUI.BeginChangeCheck();
            
            // Create dummy rect of zero height, to get width of current available rect
            var rect = EditorGUILayout.GetControlRect(true, 0f);
            var viewWidth = rect.width;

            // Cache previous label width
            var previousLabelWidth = EditorGUIUtility.labelWidth;

            // Minimum label width is 123. Else, set 33% of the available width as label.
            EditorGUIUtility.labelWidth = Mathf.Max((viewWidth) * 0.33f, 123);

            // IMGUI handler is called every editor frame. This is innately incompatible with UI toolkit which works in a persistent manner.
            // To avoid timing errors, this try catch is needed unfortunately. In the future, further investigation should be done to avoid this.
            try
            {
                EditorGUILayout.PropertyField(property);
            }
            catch (Exception)
            {
                // Noop
            }
            
            // Restore label width for custom IMGUI implementations like lists
            EditorGUIUtility.labelWidth = previousLabelWidth;   
                
            if (EditorGUI.EndChangeCheck())
                property.serializedObject.ApplyModifiedProperties();
        }
        
        /// <summary> Uses property's managed reference type to provide options of what to instantiate and then draws it on the inspector. </summary>
        private VisualElement CreateSerializeReferenceFieldGUI(SerializedProperty property, List<CustomAttribute> additionalCustomAttributes = null)
        {
            var typeName = property.managedReferenceFieldTypename;
            var baseType = Artifice_SerializedPropertyExtensions.GetTypeFromFieldTypename(typeName);

            // Get all derived types and create string map for easy accessing.
            var types = UnityEditor.TypeCache.GetTypesDerivedFrom(baseType).OrderBy(type => type.Name).ToList();
            
            if (baseType.IsInterface == false && baseType.IsAbstract == false)
            {
                types.Add(baseType);
                types = types.OrderBy(type => type.Name).ToList();
            }

            var typeMap = new Dictionary<string, Type>();
            foreach (var type in types)
            {
                // MonoBehaviour types cannot be instantiated in runtime like c# objects.
                if(type == typeof(MonoBehaviour) || type.IsSubclassOf(typeof(MonoBehaviour)) || type.IsAbstract || type.IsInterface)
                    continue;
                
                typeMap.Add(type.Name, type);
            }

            // Create base container for property.
            var container = new VisualElement();
            container.AddToClassList("property-container");
            
            // Create the custom attributes GUI (includes validator infoboxes)
            container = CreateCustomAttributesGUI(property, container);

            // Selector container
            var selectorContainer = new VisualElement();
            selectorContainer.AddToClassList("serialize-reference-selector");
            container.Add(selectorContainer);

            // Don't show a selector for concrete types
            bool isPolymorphicType = baseType.IsInterface || baseType.IsAbstract || typeMap.Count != 1;
            
            // Use the searchable type picker when the field is annotated with TypePicker.
            var typePicker = GetTypePickerAttribute(property);
            bool useSearchTypePicker = isPolymorphicType && typePicker != null;

            // Create container for drawing selected inherited property. This will be cleared and drawn again upon change.
            var referenceContainer = new VisualElement();
            container.Add(referenceContainer);

            void RebuildReferenceContainerGUI()
            {
                property.serializedObject.Update();
             
                if (property == null)
                    return;

                // Re-fetch the property handle: after ApplyModifiedProperties the captured property's cached
                // managed-reference type/children info is stale, so the first rebuild after a type pick would
                // otherwise see no visible children.
                var current = property.serializedObject.FindProperty(property.propertyPath) ?? property;

                // Clear reference container.
                referenceContainer.Clear();

                // Get value from type map, create instance and draw from artifice.
                if (current.managedReferenceValue != null && current.hasVisibleChildren)
                {
                    referenceContainer.RemoveFromClassList("hide");

                    foreach (var childProperty in current.GetVisibleChildren().SortProperties())
                        referenceContainer.Add(CreatePropertyGUI(childProperty));
                }
                else
                    referenceContainer.AddToClassList("hide");
            }

            string GetSelectorLabel()
            {
                if (property.managedReferenceValue != null)
                    return useSearchTypePicker
                        ? Artifice_ManagedReferenceSearchProvider.GetDisplayName(property.managedReferenceValue.GetType())
                        : property.managedReferenceValue.GetType().Name;
                return useSearchTypePicker ? "<None>" : "Null";
            }

            void ApplyTypeSelection(Type selectedType)
            {
                Undo.RecordObject(property.serializedObject.targetObject, "Managed Reference Change");
                
                // Only create a new instance if the current managedReferenceValue is null or the wrong type
                if (selectedType != null && (property.managedReferenceValue == null || property.managedReferenceValue.GetType() != selectedType))
                    property.managedReferenceValue = Activator.CreateInstance(selectedType);
                else if (selectedType == null)
                    property.managedReferenceValue = null;
                
                property.serializedObject.ApplyModifiedProperties();
                property.serializedObject.Update();
                
                // TrackPropertyValue may not fire reliably for managed reference changes (known Unity issue).
                // Manually update validator infoboxes so designers see the validation state immediately.
                // Search locally first, then upward through parent hierarchy (needed for list elements
                // where [Required] is injected via ChildrenInjectedCustomAttributes at a higher level).
                var isValid = property.managedReferenceValue != null;
                var infoBoxes = container.Query<Artifice_VisualElement_InfoBox>().ToList();
                if (infoBoxes.Count == 0)
                {
                    var parent = container.parent;
                    while (parent != null && parent is not Artifice_VisualElement_AbstractListView && !parent.ClassListContains("artifice-inspector"))
                    {
                        infoBoxes.AddRange(parent.Children().OfType<Artifice_VisualElement_InfoBox>());
                        if (infoBoxes.Count > 0)
                            break;
                        parent = parent.parent;
                    }
                }
                foreach (var infoBox in infoBoxes)
                {
                    if (isValid)
                        infoBox.AddToClassList("hide");
                    else
                        infoBox.RemoveFromClassList("hide");
                }

                // Rebuild now and again next frame: the managed reference change may not be fully
                // reflected in the SerializedProperty until Unity processes it, so a single immediate
                // rebuild can render nothing on the first pick.
                RebuildReferenceContainerGUI();
                referenceContainer.schedule.Execute(() =>
                {
                    if (referenceContainer.panel != null)
                        RebuildReferenceContainerGUI();
                });
            }

            // Selector: searchable popup button (TypePicker) or classic dropdown.
            VisualElement selector = null;
            Action<string> setSelectorLabel = null;

            if (useSearchTypePicker)
            {
                // Mirror Unity's DropdownField structure so the selector reads as a dropdown, not a button.
                var searchField = new VisualElement();
                searchField.AddToClassList(BaseField<object>.alignedFieldUssClassName);
                searchField.AddToClassList("unity-base-field");
                searchField.AddToClassList("unity-base-popup-field");
                searchField.AddToClassList("unity-popup-field");
                searchField.style.flexGrow = 1;

                var fieldLabel = new Label(property.displayName);
                fieldLabel.AddToClassList(BaseField<object>.labelUssClassName);
                searchField.Add(fieldLabel);

                var input = new VisualElement();
                input.AddToClassList(BaseField<object>.inputUssClassName);
                input.AddToClassList("unity-base-popup-field__input");
                input.AddToClassList("unity-popup-field__input");
                searchField.Add(input);

                var valueLabel = new Label();
                valueLabel.AddToClassList("unity-base-popup-field__text");
                valueLabel.pickingMode = PickingMode.Ignore;
                input.Add(valueLabel);

                var arrow = new VisualElement();
                arrow.AddToClassList("unity-base-popup-field__arrow");
                arrow.pickingMode = PickingMode.Ignore;
                input.Add(arrow);

                setSelectorLabel = label =>
                {
                    valueLabel.text = label;
                    valueLabel.style.color = label == "<None>" ? Color.red : StyleKeyword.Null;
                };

                input.RegisterCallback<PointerDownEvent>(evt =>
                {
                    // Anchor the popup to the bottom-left of the dropdown input.
                    var rect = input.worldBound;
                    var anchor = GUIUtility.GUIToScreenRect(rect).position + new Vector2(0, rect.height);
                    var context = new SearchWindowContext(anchor + new Vector2(120, 16));
                    var provider = Artifice_ManagedReferenceSearchProvider.Create(typeMap.Values.ToList(), ApplyTypeSelection);
                    SearchWindow.Open(context, provider);
                });

                selector = searchField;
            }
            else
            {
                var dropdown = new DropdownField();
                dropdown.AddToClassList(BaseField<object>.alignedFieldUssClassName);
                dropdown.label = property.displayName;
                dropdown.choices.Add("Null");
                foreach (var pair in typeMap)
                    dropdown.choices.Add(pair.Key);

                setSelectorLabel = label => dropdown.value = label;
                dropdown.RegisterValueChangedCallback(evt =>
                {
                    // Get value from type map, create instance and draw from artifice.
                    typeMap.TryGetValue(evt.newValue, out var type);
                    ApplyTypeSelection(type);
                });
                selector = dropdown;
            }

            if (isPolymorphicType)
                selectorContainer.Add(selector);

            // Initialize selector label based on current value.
            setSelectorLabel(GetSelectorLabel());

            // Render any existing managed reference data immediately.
            RebuildReferenceContainerGUI();
            
            // Auto-instantiate if single concrete type
            if (!isPolymorphicType && typeMap.Values.FirstOrDefault() is { } singleType)
            {
                if (property.managedReferenceValue == null)
                {
                    property.managedReferenceValue = Activator.CreateInstance(singleType);
                    property.serializedObject.ApplyModifiedProperties();
                }
                setSelectorLabel(singleType.Name);
                RebuildReferenceContainerGUI();
            }
            
            // Reference container will constantly track property for value changes (Supports undo and object reset this way) to redraw it self.
            referenceContainer.TrackPropertyValue(property, trackedProperty =>
            {
                RebuildReferenceContainerGUI();
            });
            
            // Selector should also track the property in order to update its label on external updates.
            selector.TrackPropertyValue(property, trackedProperty =>
            {
                trackedProperty.serializedObject.Update();
                setSelectorLabel(trackedProperty.managedReferenceValue != null
                    ? (useSearchTypePicker
                        ? Artifice_ManagedReferenceSearchProvider.GetDisplayName(trackedProperty.managedReferenceValue.GetType())
                        : trackedProperty.managedReferenceValue.GetType().Name)
                    : (useSearchTypePicker ? "<None>" : "Null"));
            });

            return container;
        }

        /// <summary> Returns the <c>TypePickerAttribute</c> on the field (or its array parent), or null. </summary>
        private static TypePickerAttribute GetTypePickerAttribute(SerializedProperty property)
        {
            var attribute = FindTypePicker(property.GetAttributes());
            if (attribute != null)
                return attribute;

            // Array element properties do not resolve to the declaring field; walk up to it.
            var parent = property.FindParentProperty();
            for (var depth = 0; parent != null && depth < 4; depth++)
            {
                attribute = FindTypePicker(parent.GetAttributes());
                if (attribute != null)
                    return attribute;
                parent = parent.FindParentProperty();
            }

            return null;
        }

        private static TypePickerAttribute FindTypePicker(IEnumerable<Attribute> attributes)
        {
            return attributes != null ? attributes.OfType<TypePickerAttribute>().FirstOrDefault() : null;
        }
        
        /// <summary> Returns a <see cref="VisualElement"/> with buttons which invoke the methods marked with the <see cref="ButtonAttribute"/>. </summary>
        /// <remarks> Unfortunately, there is not unified structure for SerializedObject and SerializedProperty. A template is used here to avoid deduplicate method overloads. </remarks>
        public VisualElement CreateMethodsGUI<T>(T serializedData) where T : class
        {
            // Obtain the target type depending on the serializedData type.
            var targetType = serializedData switch
            {
                SerializedObject serializedObject => serializedObject.targetObject.GetType(),
                SerializedProperty serializedProperty => serializedProperty.GetTarget<object>().GetType(),
                _ => throw new ArgumentException("Invalid serialized data type.")
            };

            // Get name to show in sliding group title based on serialized data type.
            var slidingGroupTitle = serializedData switch
            {
                SerializedObject serializedObject => serializedObject.targetObject.GetType().Name,
                SerializedProperty serializedProperty => serializedProperty.displayName,
                _ => throw new ArgumentException("Invalid serialized data type.")
            };
            
            // Create main container to return, containing both a list of buttons and a sliding group.
            var container = new VisualElement();
            container.AddToClassList("property-container");

            // Some methods may be in a sliding group. Optional.
            var slidingGroup = new Artifice_VisualElement_SlidingGroup();
            slidingGroup.SetTitle($"{slidingGroupTitle}: Actions");
            slidingGroup.AddToClassList("method-group-container");
            
            // Create reusable button drawer.
            var buttonCustomDrawer = new Artifice_CustomAttributeDrawer_ButtonAttribute();

            var methods = Artifice_SerializedPropertyExtensions.GetAllUniqueMethods(targetType);
            foreach (var method in methods)
            {
                // Get button attribute
                var buttonAttribute = method.GetCustomAttribute<ButtonAttribute>();
                if (buttonAttribute == null)
                    continue;

                // Create dedicated drawer for it
                buttonCustomDrawer.Attribute = buttonAttribute;

                // Create the method GUI using serializedData
                var button = buttonCustomDrawer.CreateMethodGUI(serializedData, method);
                button.name = method.Name;
                button.AddToClassList("method-button");

                // Check whether a Sort or a Group attribute were used with the button.
                var groupAttribute = method.GetCustomAttribute<GroupAttribute>();
                if (groupAttribute != null)
                {
                    // From drawer map, get the type visual element group type.
                    var drawerMap = Artifice_Utilities.GetDrawerTypesMap();
                    if (drawerMap.TryGetValue(groupAttribute.GetType(), out var drawerType) == false)
                        Debug.Assert(false,
                            $"GroupAttribute {groupAttribute.GetType().Name} does not have a respective drawer.");

                    var groupAttributeDrawer =
                        (Artifice_CustomAttributeDrawer)Activator.CreateInstance(drawerType) as
                        Artifice_CustomAttributeDrawer_GroupAttribute;
                    Debug.Assert(groupAttributeDrawer != null, "GroupAttribute drawer cannot be null here.");
                    
                    groupAttributeDrawer.Attribute = groupAttribute;
                    _disposableStack.Push(groupAttributeDrawer);
                    
                    if (serializedData is SerializedObject serializedObject)
                    {
                        var wrapper = groupAttributeDrawer.OnWrapGUI(serializedObject.GetIterator(), button);
                        container.Add(wrapper);
                    }
                    else if (serializedData is SerializedProperty serializedProperty)
                    {
                        // A method can only be contained in a serialized property. So the SerializedProperty we 
                        // need for the group holder, is any of the first children.
                        var visibleChildren = serializedProperty.GetVisibleChildren();
                        if (visibleChildren.Count == 0)
                        {
                            var infoBox = new Artifice_VisualElement_InfoBox(
                                "Cannot add method to a non-existing group container",
                                Artifice_SCR_CommonResourcesHolder.instance.WarningIcon);
                            container.Add(infoBox);
                        }
                        else
                        {
                            var element = groupAttributeDrawer.OnWrapGUI(visibleChildren.First(), button);
                            container.Add(element);
                        }
                    }

                }
                else if (buttonAttribute.ShouldAddOnSlidingPanel)
                    slidingGroup.Add(button);
                else
                    container.Add(button);
            }

            // If sliding group is not empty, add it to the container last.
            if (slidingGroup.childCount > 0)
                container.Add(slidingGroup);

            return container.childCount > 0 ? container : null;
        }
        
        /// <summary> Returns an interactable visual indicator to determine whether ArtificeDrawer is enabled or not </summary>
        private VisualElement CreateArtificeIndicatorGUI(SerializedObject serializedObject)
        {
            var indicator = new VisualElement();
            indicator.AddToClassList("artifice-indicator");
            indicator.AddToClassList(Artifice_Utilities.ArtificeDrawerEnabled ? "indicator-enabled" : "indicator-disabled");

            indicator.RegisterCallback<ClickEvent>(evt =>
            {
                Artifice_Utilities.ToggleArtificeDrawer(!Artifice_Utilities.ArtificeDrawerEnabled);
                indicator.RemoveFromClassList(!Artifice_Utilities.ArtificeDrawerEnabled ? "indicator-enabled" : "indicator-disabled");
                indicator.AddToClassList(Artifice_Utilities.ArtificeDrawerEnabled ? "indicator-enabled" : "indicator-disabled");
            });
            
            indicator.tooltip = "Green: ArtificeDrawer is enabled.\nRed: ArtificeDrawer is disabled\n\nClick to toggle.\nNote: Inspector redraw is required.";
            
            return indicator;
        }
        
        #region Utility Methods
        
        public void SetSerializedPropertyFilter(SerializedPropertyFilter filter)
        {
            _serializedPropertyFilter = filter;
        }

        public void SetArtificeIndicatorVisibility(bool isVisible)
        {
            if(_artificeInspectorIndicator != null)
                _artificeInspectorIndicator.visible = isVisible;
        }
        
        /// <summary> Checks property and its visible children. If any use custom attributes, this method returns true. False otherwise. </summary>
        private bool DoesRequireArtificeRendering(SerializedProperty property)
        {
            if (_doesRequireVisualElementsCache.TryGetValue(property, out var cachedResult))
                return cachedResult;
         
            // Check Ignore List
            var typeName = property.type;
            if (property.isArray == false && Artifice_Utilities.ShouldIgnoreTypeName(typeName))
            {
                _doesRequireVisualElementsCache[property] = false;
                return false;
            }
            
            // Check self
            if (IsUsingCustomAttributesDirectly(property))
            {
                _doesRequireVisualElementsCache[property] = true;
                return true;   
            }

            // Check children (no reason to skip as this check will be called for children as well).
            foreach (var childProperty in property.GetVisibleChildren())
            {
                if (DoesRequireArtificeRendering(childProperty))
                {
                    _doesRequireVisualElementsCache[property] = true;
                    return true;
                }
            }

            _doesRequireVisualElementsCache[property] = false;
            return false;
        }
        
        /// <summary> Returns true if the property is directly using any <see cref="CustomAttribute"/> </summary>
        private bool IsUsingCustomAttributesDirectly(SerializedProperty property)
        {
            string typeName;
            
            // Check if property directly has a custom attribute
            var customAttributes = property.GetCustomAttributes();
            if (customAttributes is { Length: > 0 })
                return true;
            
            // Check if force artifice is used either way.
            var isUsingForceArtifice = property.GetAttributes().Any(attribute => attribute is ForceArtificeAttribute);
            if (isUsingForceArtifice)
                return true;
            
            if (property.IsArray() && property.arraySize == 0)
            {
                typeName = property.arrayElementType.Replace("PPtr<$", "").Replace(">", "");
                if (Artifice_Utilities.ShouldIgnoreTypeName(typeName))
                    return false;

                // Return cached if found. Otherwise search assemblies.
                if (TypeCache.TryGetValue(typeName, out var arrayElementType) == false) 
                {
                    arrayElementType = AppDomain.CurrentDomain.GetAssemblies()
                        .SelectMany(a => {
                            try { return a.GetTypes(); } catch { return Array.Empty<Type>(); }
                        })
                        .FirstOrDefault(t => t.FullName == typeName || t.Name == typeName);

                    TypeCache[typeName] = arrayElementType;
                }

                return arrayElementType != null &&
                       (
                           arrayElementType.GetCustomAttributes<CustomAttribute>().Any() ||
                           DoChildrenOfTypeUseCustomAttributes(arrayElementType)
                       );
            }
            
            // Otherwise, maybe some method of the object uses custom attributes.
            var obj = property.GetTarget<object>();
            if (obj != null)
            {
                foreach(var method in obj.GetType().GetMethods())
                    if (method.GetCustomAttributes().Any(attribute => attribute is CustomAttribute))
                    {
                        _doesRequireVisualElementsCache[property] = true;
                        return true;
                    }
            }
          
            return false;
        }
        
        /// <summary> Returns true if type or any nested field is using any <see cref="CustomAttribute"/> </summary>
        private bool DoChildrenOfTypeUseCustomAttributes(Type type)
        {
            // Create  queue and already-searched structures for BFS
            var queue = new Queue<FieldInfo>();
            var alreadySearched = new HashSet<FieldInfo>();
            
            // Inject into the queue all the direct children fields of type.
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                queue.Enqueue(field);

            while (queue.Count > 0)
            {
                var currentField = queue.Dequeue();

                // Check if already searched to skip
                if (alreadySearched.Contains(currentField))
                    continue;
                alreadySearched.Add(currentField);
                
                // Check if it uses custom attributes
                var customAttributes = currentField.GetCustomAttributes().ToArray();
                if (customAttributes.Length > 0)
                    return true;
                
                // Add nested fields in queue to search
                foreach(var field in currentField.FieldType.GetFields())
                    queue.Enqueue(field);
            }

            return false;
        }
        
        /// <summary> Some <see cref="CustomAttribute"/> on lists are meant to be passed along its children, instead of the list it self. This method splits them and provides them as out parameters. </summary>
        private void SplitCustomPropertiesForArrays(SerializedProperty property, out List<CustomAttribute> arrayCustomAttributes, out List<CustomAttribute> childrenCustomAttributes)
        {
            // Create new lists
            arrayCustomAttributes = new List<CustomAttribute>();
            childrenCustomAttributes = new List<CustomAttribute>();
            
            // Get property attributes and parse-split them
            var attributes = property.GetCustomAttributes();
            foreach (var attribute in attributes)
                if (attribute is IArtifice_ArrayAppliedAttribute)
                    arrayCustomAttributes.Add(attribute);
                else
                    childrenCustomAttributes.Add(attribute);
        }
        
        /// <summary> Returns a VisualElement notifying for a missing script error </summary>
        private VisualElement CreateScriptMissingUI(SerializedObject serializedObject)
        {
            var container = new VisualElement();

            container.Add(new PropertyField(serializedObject.FindProperty("m_Script")));

            var labelContainer = new VisualElement();
            labelContainer.AddToClassList("label-container");
            container.Add(labelContainer);

            var flavorIconLabel = new Label(":'(");
            // var flavorIconLabel = new Label(":(");
            flavorIconLabel.AddToClassList("flavor-icon");
            labelContainer.Add(flavorIconLabel);

            var textLabel = new Label("The associated script can not be loaded. Please fix any compile errors and assign a valid script.");
            textLabel.AddToClassList("text");
            labelContainer.Add(textLabel);

            return container;
        }

        private VisualElement HandleWrapForOpenGroups(SerializedProperty property, VisualElement propertyField, List<CustomAttribute> customAttributes)
        {
            var hadGroupAttribute = customAttributes.Any(a => a is GroupAttribute);
            if (hadGroupAttribute == false && Artifice_CustomAttributeUtility_GroupsHolder.Instance.HasOpenGroup())
            {
                propertyField.AddToClassList("group-child");
                var wrapper = Artifice_CustomAttributeUtility_GroupsHolder.Instance.Get_OpenGroup();
                wrapper.Add(propertyField);
                return wrapper;
            }
            else
                return propertyField;
        }
        
        #endregion

        #region Dispose Pattern

        ~ArtificeDrawer()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true); // Dispose of unmanaged resources.
            GC.SuppressFinalize(this); // Suppress finalization.
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            // Dispose managed resources
            if (disposing)
            {
                while (_disposableStack.Count > 0)
                    _disposableStack.Pop().Dispose();

                _doesRequireVisualElementsCache.Clear();
            }

            _disposed = true;
        }

        #endregion
    }
}
