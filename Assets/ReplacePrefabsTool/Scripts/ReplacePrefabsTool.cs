using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Editor.Utils
{
    public class ReplacePrefabsTool : EditorWindow
    {
        private readonly List<ReferencedObject> _referencedObjects = new List<ReferencedObject>();
        private readonly List<ReferencingComponent> _referencingComponents = new List<ReferencingComponent>();
        private readonly Dictionary<int, GameObject> _replacementHistory = new Dictionary<int, GameObject>();

        private delegate List<T> GetHierarchyFor<T>(GameObject target);

        private int _lastStartPosition;
        private int _lastEndPosition;
        private Object _lastOriginalPrefab;
        private Object _lastNewPrefab;

        private Object _originalPrefab;
        private Object _newPrefab;
        private Vector3 _offset;

        [MenuItem("Tools/Replace Prefabs")]
        private static void OpenWindow()
        {
            GetWindow<ReplacePrefabsTool>().Show();
        }

        public void OnGUI()
        {
            GUILayout.BeginHorizontal();
            _offset = EditorGUILayout.Vector3Field("Offset: ", _offset);
            GUILayout.EndHorizontal();

            GUILayout.Space(20);
            GUILayout.Label("Original prefab: ");
            GUILayout.BeginHorizontal();
            _originalPrefab = EditorGUILayout.ObjectField(_originalPrefab, typeof(GameObject), true);
            GUILayout.EndHorizontal();

            GUILayout.Label("New prefab: ");
            GUILayout.BeginHorizontal();
            _newPrefab = EditorGUILayout.ObjectField(_newPrefab, typeof(GameObject), true);
            GUILayout.EndHorizontal();

            GUILayout.Space(20);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Replace", GUILayout.Width(200)))
            {
                ReplacePrefab();
            }

            GUILayout.EndHorizontal();
        }


        private void ReplacePrefab()
        {
            try
            {
                FindExternalReferences();
                ReplacePrefabsInScene();
                RestoreExternalReferences();
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            }
            catch (Exception ex)
            {
                Debug.LogError(ex);
            }

            CleanUp();

            _lastOriginalPrefab = _originalPrefab;
            _lastNewPrefab = _newPrefab;
        }

        private void CleanUp()
        {
            _referencedObjects.Clear();
            _referencingComponents.Clear();
            _replacementHistory.Clear();
        }

        // Updates all references, so that all replaced objects will now be referenced
        private void RestoreExternalReferences()
        {
            _referencedObjects.ForEach(ro => ro.UpdateReference());
            _referencingComponents.ForEach(rc => rc.UpdateReference());
        }

        private bool IsOriginalPrefab(Transform transform)
        {
            if(transform != null)
            {
                return PrefabUtility.GetCorrespondingObjectFromSource(transform.gameObject) == _originalPrefab || IsOriginalPrefab(transform.parent);
            }

            return false;
        }

        private void FindExternalReferences()
        {
            var allObjectsInScene = FindObjectsOfType<Transform>().ToList();

            allObjectsInScene = allObjectsInScene.Where(x => !IsOriginalPrefab(x)).ToList();
            var allComponentsInScene = allObjectsInScene
                .Where(x => PrefabUtility.GetCorrespondingObjectFromSource(x) != _originalPrefab)
                .SelectMany(s => GameObjectHelper.GetComponentsSafe<Component>(s.gameObject))
                .Where(c => c.GetType().Namespace != "UnityEngine").ToList();

            var componentToFieldsMapping = new Dictionary<Type, List<FieldInfo>>();
        
            var targetObjects = FindObjectsOfType<Transform>().Select(x => x.gameObject)
                .Where(x => PrefabUtility.GetCorrespondingObjectFromSource(x) == _originalPrefab).ToList();

            allComponentsInScene.Select(c => c.GetType()).Distinct().ToList()
                .ForEach(t => componentToFieldsMapping.Add(t, GetFieldsForType(t)));

            var objectHierarchyMapping = new Dictionary<GameObject, List<GameObject>>();

            var componentHierarchyMapping = new Dictionary<GameObject, List<Component>>();

            // Iterate through every variable of every component in the scene to check whether they may be referencing one of the replaced objects
            foreach (var potentiallyTargetingComponent in allComponentsInScene)
            {
                foreach (var potentialReference in componentToFieldsMapping[
                    potentiallyTargetingComponent.GetType()])
                {
                    var componentFieldType = potentialReference.FieldType;
                    var baseParameter = new FindReferenceBaseParameter()
                    {
                        ComponentWithReference = potentiallyTargetingComponent,
                        ReferenceOnTarget = potentialReference,
                        TargetObjects = targetObjects
                    };

                    if (componentFieldType.IsAssignableFrom(typeof(GameObject)))
                    {
                        baseParameter.IsList = false;
                        FindReferencesOn(baseParameter, objectHierarchyMapping,
                            GameObjectHelper.ObjectHierarchyFor);
                    }

                    if (componentFieldType.IsSubclassOf(typeof(Component)))
                    {
                        baseParameter.IsList = false;
                        FindReferencesOn(baseParameter, componentHierarchyMapping,
                            GameObjectHelper.ComponentHierarchyFor);
                    }

                    if (IsListOf<GameObject>(componentFieldType))
                    {
                        baseParameter.IsList = true;
                        FindReferencesOn(baseParameter, objectHierarchyMapping,
                            GameObjectHelper.ObjectHierarchyFor);
                    }

                    if (IsListOf<Component>(componentFieldType))
                    {
                        baseParameter.IsList = true;
                        FindReferencesOn(baseParameter, componentHierarchyMapping,
                            GameObjectHelper.ComponentHierarchyFor);
                    }
                }
            }
        }

        // True if type is an enumerable of T
        private bool IsListOf<T>(Type type)
        {
            foreach (var interfaceType in type.GetInterfaces())
            {
                if (interfaceType.IsGenericType && interfaceType.GetGenericTypeDefinition() == typeof(IList<>))
                {
                    Type itemType;

                    itemType = type.IsArray ? type.GetElementType() : type.GetGenericArguments().Single();
                    return itemType == typeof(T) || itemType.IsSubclassOf(typeof(T));
                }
            }

            return false;
        }

        private void FindReferencesOn<T>(FindReferenceBaseParameter param,
            Dictionary<GameObject, List<T>> hierarchyMapping,
            GetHierarchyFor<T> getHierarchyFor)
        {
            List<T> hierarchyAsList;
            // Current value of the component's variable
            // May be a list. For simplicity, a list is always assumed
            var value = GetValueAsListFor<T>(param.ComponentWithReference, param.ReferenceOnTarget, param.IsList);

            foreach (var targetObject in param.TargetObjects)
            {
                if (hierarchyMapping.ContainsKey(targetObject))
                {
                    hierarchyAsList = hierarchyMapping[targetObject];
                }
                else
                {
                    hierarchyAsList = getHierarchyFor(targetObject);
                    hierarchyMapping.Add(targetObject, hierarchyAsList);
                }

                for (var i = 0; i < value.Count; i++)
                {
                    if (hierarchyAsList.Any(o => o.Equals(value[i])))
                    {
                        if (typeof(T) == typeof(Component))
                            StoreReferenceForComponent(targetObject, param.ComponentWithReference,
                                param.ReferenceOnTarget, value[i] as Component, param.IsList, i);
                        if (typeof(T) == typeof(GameObject))
                            StoreReferenceForObject(targetObject, param.ComponentWithReference, param.ReferenceOnTarget,
                                value[i] as GameObject, param.IsList, i);
                    }
                }
            }
        }

        private void StoreReferenceForObject(GameObject referencedObject, Component referencingComponent,
            FieldInfo referencingField, GameObject value, bool isList, int index)
        {
            Undo.RegisterCompleteObjectUndo(referencingComponent, "ObjectReference");

            _referencedObjects.Add(new ReferencedObject((o) => _replacementHistory[o])
            {
                IsList = isList,
                IndexInList = index,
                IsActivated = true,
                ReferencingFieldInSource = referencingField,
                ReferencingComponentInstance = referencingComponent,
                ReferencedObjectID = referencedObject.GetInstanceID(),
                SourceObjectID = referencingComponent.gameObject.GetInstanceID()
            });
        }

        private void StoreReferenceForComponent(GameObject referencedObject, Component referencingComponent,
            FieldInfo referencingField, Component value, bool isList, int index)
        {
            Undo.RegisterCompleteObjectUndo(referencingComponent, "ObjectReference");

            _referencingComponents.Add(new ReferencingComponent((o) => _replacementHistory[o])
            {
                IsList = isList,
                IndexInList = index,
                IsActivated = true,
                ReferencedComponentType = value.GetType(),
                ReferencingFieldInSource = referencingField,
                ReferencingComponentInstance = referencingComponent,
                ReferencedObjectID = referencedObject.GetInstanceID(),
                SourceObjectID = referencingComponent.gameObject.GetInstanceID()
            });
        }

        private void MakeHistory(GameObject oldObject, GameObject newObject)
        {
            var idHierarchy = GameObjectHelper.GetAllChildrenOf(oldObject, true).Select(go => go.GetInstanceID())
                .ToList();
            idHierarchy.ForEach(id => _replacementHistory.Add(id, newObject));
        }

        private List<T> GetValueAsListFor<T>(Component source, FieldInfo field, bool isList)
        {
            var results = new List<T>();
            if (isList)
            {
                var fieldType = field.FieldType;

                if (fieldType.IsArray)
                {
                    var array = field.GetValue(source) as Array;
                    var arrayLength = array.Length;

                    for (var i = 0; i < arrayLength; i++)
                    {
                        results.Add((T)array.GetValue(i));
                    }
                }
                else
                {
                    var list = field.GetValue(source);

                    if (list == null)
                    {
                        return results;
                    }

                    var count = (int)fieldType.GetProperty("Count").GetValue(list);
                    var propertyItemInfo = fieldType.GetProperty("Item");

                    for (var i = 0; i < count; i++)
                    {
                        results.Add((T)propertyItemInfo.GetValue(list, new object[] { i }));
                    }
                }
            }
            else
            {
                results.Add((T)field.GetValue(source));
            }

            return results;
        }
        
        private void ReplacePrefabsInScene()
        {
            var listToDestroy = new List<GameObject>();

            var transforms = FindObjectsOfType<Transform>().Select(x => x.gameObject)
                .Where(x => PrefabUtility.GetCorrespondingObjectFromSource(x) == _originalPrefab);

            foreach(var transform in transforms)
            {
                var newObject = (GameObject)PrefabUtility.InstantiatePrefab(_newPrefab);
                newObject.transform.parent = transform.transform.parent;
                newObject.transform.localPosition = transform.transform.localPosition + _offset;
                newObject.transform.localRotation = transform.transform.localRotation;
                newObject.transform.localScale = transform.transform.localScale;
                newObject.transform.SetSiblingIndex(transform.transform.GetSiblingIndex());

                Undo.RegisterCreatedObjectUndo(newObject, "Spawn prefab");
                MakeHistory(transform, newObject);

                listToDestroy.Add(transform);
            }

            foreach (var gameObject in listToDestroy)
            {
                Undo.DestroyObjectImmediate(gameObject);
            }
        }

        // Find all fields (variables) of a specific type and returns their fieldInfo as a list
        private List<FieldInfo> GetFieldsForType(Type type)
        {
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Default |
                                        BindingFlags.DeclaredOnly | BindingFlags.NonPublic |
                                        BindingFlags.FlattenHierarchy).ToList();

            // GetFields won't actually return all fields of the base types, even with FlattenHierarchy enabled
            // Manually add base type fields
            var baseType = type.BaseType;
            while (baseType != typeof(Component) && baseType != typeof(MonoBehaviour) && baseType != null)
            {
                var baseFields = baseType.GetFields(BindingFlags.Instance | BindingFlags.Default |
                                                    BindingFlags.DeclaredOnly | BindingFlags.NonPublic |
                                                    BindingFlags.FlattenHierarchy | BindingFlags.Public).ToList();
                fields.AddRange(baseFields);
                baseType = baseType.BaseType;
            }

            fields.RemoveAll(f => !f.IsPublic && !Attribute.IsDefined(f, typeof(SerializeField)));
            return fields;
        }

        public struct FindReferenceBaseParameter
        {
            public List<GameObject> TargetObjects;
            public Component ComponentWithReference;
            public FieldInfo ReferenceOnTarget;
            public bool IsList;
        }
    }
}