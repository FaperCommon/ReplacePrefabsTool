using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Editor.Utils
{
    public abstract class ExternalReference
    {
        public delegate GameObject GetReplacementFor(int objectID);

        private readonly GetReplacementFor GetInstance; //Returns the replaced object that previously had the given id
        private Component _referencingComponent; //The component that stores a reference to the object being replaced
        private Type _referencingComponentType;

        public GameObject ReferencedObject => GetInstance(ReferencedObjectID);

        public int ReferencedObjectID;
        public int SourceObjectID;

        public Component ReferencingComponentInstance
        {
            set
            {
                _referencingComponent = value;
                _referencingComponentType = _referencingComponent.GetType();
            }
            get
            {
                // Gameobject with referencing component might itself have been replaced
                if (_referencingComponent == null || _referencingComponent.gameObject == null)
                {
                    var sourceObject = GetInstance(SourceObjectID);
                    _referencingComponent =
                        GameObjectHelper.GetComponentInAllChildren(sourceObject, _referencingComponentType);
                }

                return _referencingComponent;
            }
        }

        // Writes a value into a component's field
        protected void SetValueFor<T>(Component source, FieldInfo field, T value, bool isList, int index)
        {
            if (isList)
            {
                var fieldType = field.FieldType;

                if (fieldType.IsArray)
                {
                    var array = field.GetValue(source) as Array;
                    array.SetValue(value, index);
                }
                else
                {
                    var list = field.GetValue(source);
                    var propertyItemInfo = fieldType.GetProperty("Item");

                    propertyItemInfo.SetValue(list, value, new object[] { index });
                }
            }
            else
            {
                try
                {
                    field.SetValue(source, value);
                }
                catch (Exception e)
                {
                    Debug.LogError(e.Message);
                    Debug.LogError($"Failed to set value {value}");
                }
            }

            // Set dirty so the value doesn't get lost on play
            EditorUtility.SetDirty(source);
        }

        public FieldInfo ReferencingFieldInSource;
        public bool IsList;
        public int IndexInList;
        public bool IsActivated = true;
        public abstract void UpdateReference();

        public ExternalReference(GetReplacementFor getReplacementFor)
        {
            GetInstance = getReplacementFor;
        }
    }

    public class ReferencedObject : ExternalReference
    {
        public ReferencedObject(GetReplacementFor getReplacementFor) : base(getReplacementFor)
        {
        }

        public override void UpdateReference()
        {
            // Value will always be the parent object
            SetValueFor(ReferencingComponentInstance, ReferencingFieldInSource, ReferencedObject, IsList, IndexInList);
        }
    }

    public class ReferencingComponent : ExternalReference
    {
        public Type ReferencedComponentType;

        public ReferencingComponent(GetReplacementFor getReplacementFor) : base(getReplacementFor)
        {
        }

        public override void UpdateReference()
        {
            var referencedComponent =
                GameObjectHelper.GetComponentInAllChildren(ReferencedObject, ReferencedComponentType);
            SetValueFor(ReferencingComponentInstance, ReferencingFieldInSource, referencedComponent, IsList,
                IndexInList);
        }
    }
}