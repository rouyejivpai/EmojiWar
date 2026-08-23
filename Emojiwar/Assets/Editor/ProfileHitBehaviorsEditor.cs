using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using System;

[CustomEditor(typeof(profile))]
public class ProfileHitBehaviorsEditor : Editor
{
    private ReorderableList _list;
    private SerializedProperty _behaviorsProp;

    private void OnEnable()
    {
        _behaviorsProp = serializedObject.FindProperty("hitBehaviors");
        _list = new ReorderableList(serializedObject, _behaviorsProp, true, true, true, true);

        _list.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Hit Behaviors");

        _list.drawElementCallback = (rect, index, isActive, isFocused) =>
        {
            var element = _behaviorsProp.GetArrayElementAtIndex(index);
            EditorGUI.PropertyField(rect, element, GUIContent.none, true);
        };

        _list.elementHeightCallback = index =>
        {
            var element = _behaviorsProp.GetArrayElementAtIndex(index);
            return EditorGUI.GetPropertyHeight(element, true) + 4f;
        };

        _list.onAddDropdownCallback = (rect, list) =>
        {
            var menu = new GenericMenu();
            AddTypeMenuItem(menu, typeof(DestroyOnHitBehavior), "Destroy On Hit");
            AddTypeMenuItem(menu, typeof(PierceBehavior), "Pierce");
            AddTypeMenuItem(menu, typeof(BounceBehavior), "Bounce");
            menu.DropDown(rect);
        };

        _list.onRemoveCallback = list =>
        {
            if (list.index >= 0 && list.index < _behaviorsProp.arraySize)
            {
                _behaviorsProp.DeleteArrayElementAtIndex(list.index);
                serializedObject.ApplyModifiedProperties();
            }
        };
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspectorExceptBehaviors();
        _list.DoLayoutList();
        serializedObject.ApplyModifiedProperties();
    }

    private void DrawDefaultInspectorExceptBehaviors()
    {
        var prop = serializedObject.GetIterator();
        bool enterChildren = true;
        while (prop.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (prop.propertyPath == _behaviorsProp.propertyPath) continue;
            EditorGUI.BeginDisabledGroup(prop.name == "m_Script");
            EditorGUILayout.PropertyField(prop, true);
            EditorGUI.EndDisabledGroup();
        }
    }

    private void AddTypeMenuItem(GenericMenu menu, Type t, string label)
    {
        menu.AddItem(new GUIContent(label), false, () =>
        {
            serializedObject.Update();
            int i = _behaviorsProp.arraySize;
            _behaviorsProp.InsertArrayElementAtIndex(i);
            var element = _behaviorsProp.GetArrayElementAtIndex(i);
            element.managedReferenceValue = Activator.CreateInstance(t);
            serializedObject.ApplyModifiedProperties();
        });
    }
}