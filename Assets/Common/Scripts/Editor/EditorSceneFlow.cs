using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ape.Editor
{
    [InitializeOnLoad]
    public static class EditorSceneFlow
    {
        private const string LoaderScenePath = "Assets/_Game/Scenes/Loader.unity";

    private const string RestorePendingKey = "EditorSceneFlow.RestorePending";
    private const string PreviousSetupKey = "EditorSceneFlow.PreviousSetup";
    private const string SelectedScenePathKey = "EditorSceneFlow.SelectedScenePath";
    private const string SelectedSiblingPathKey = "EditorSceneFlow.SelectedSiblingPath";
    private const string ExpandedHierarchyStateKey = "EditorSceneFlow.ExpandedHierarchyState";

    private const int SelectionRestoreAttemptCount = 10;
    private const int HierarchyRestoreAttemptCount = 10;

    private static int selectionRestoreAttemptsRemaining;
    private static int hierarchyRestoreAttemptsRemaining;

    [Serializable]
    private struct SceneSetupState
    {
        public string path;
        public bool isLoaded;
        public bool isActive;
    }

    [Serializable]
    private sealed class SceneSetupContainer
    {
        public SceneSetupState[] scenes = Array.Empty<SceneSetupState>();
    }

    [Serializable]
    private sealed class HierarchyExpandedContainer
    {
        public string[] expandedObjectGlobalIds = Array.Empty<string>();
    }

    static EditorSceneFlow()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode)
        {
            PreparePlayModeStart();
            return;
        }

        if (state == PlayModeStateChange.EnteredEditMode)
            RestorePreviousSceneSetup();
    }

    private static void PreparePlayModeStart()
    {
        if (!EditorSceneManager.SaveOpenScenes())
        {
            Debug.LogWarning("EditorSceneFlow: Failed to auto-save open scenes. Play Mode entry cancelled.");
            EditorApplication.isPlaying = false;
            return;
        }

        if (HasDirtyOpenScenes())
        {
            Debug.LogWarning("EditorSceneFlow: Some scenes remain dirty after auto-save. Play Mode entry cancelled.");
            EditorApplication.isPlaying = false;
            return;
        }

        SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();
        if (setup.Length == 0)
        {
            EditorApplication.isPlaying = false;
            return;
        }

        if (setup.Any(s => string.IsNullOrWhiteSpace(s.path)))
        {
            Debug.LogWarning("EditorSceneFlow: Open scenes must be saved to assets before Play Mode. Entry cancelled.");
            EditorApplication.isPlaying = false;
            return;
        }

        SaveSetupForRestore(setup);
        SaveSelectionForRestore();
        SaveHierarchyExpandedStateForRestore();

        if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(LoaderScenePath))
        {
            Debug.LogError($"EditorSceneFlow: Loader scene not found at '{LoaderScenePath}'.");
            ClearRestoreState();
            EditorApplication.isPlaying = false;
            return;
        }

        EditorSceneManager.OpenScene(LoaderScenePath, OpenSceneMode.Single);
    }

    private static bool HasDirtyOpenScenes()
    {
        int sceneCount = SceneManager.sceneCount;
        for (int i = 0; i < sceneCount; i++)
        {
            if (SceneManager.GetSceneAt(i).isDirty)
                return true;
        }

        return false;
    }

    private static void SaveSetupForRestore(SceneSetup[] setup)
    {
        var container = new SceneSetupContainer
        {
            scenes = setup.Select(s => new SceneSetupState
            {
                path = s.path,
                isLoaded = s.isLoaded,
                isActive = s.isActive
            }).ToArray()
        };

        SessionState.SetString(PreviousSetupKey, JsonUtility.ToJson(container));
        SessionState.SetBool(RestorePendingKey, true);
    }

    private static void RestorePreviousSceneSetup()
    {
        if (!SessionState.GetBool(RestorePendingKey, false))
            return;

        SessionState.SetBool(RestorePendingKey, false);

        string json = SessionState.GetString(PreviousSetupKey, string.Empty);
        SessionState.EraseString(PreviousSetupKey);

        if (string.IsNullOrWhiteSpace(json))
        {
            ClearSelectionRestoreState();
            ClearHierarchyRestoreState();
            return;
        }

        SceneSetupContainer container = JsonUtility.FromJson<SceneSetupContainer>(json);
        if (container?.scenes == null || container.scenes.Length == 0)
        {
            ClearSelectionRestoreState();
            ClearHierarchyRestoreState();
            return;
        }

        SceneSetup[] restoreSetup = container.scenes
            .Where(s => !string.IsNullOrWhiteSpace(s.path) && AssetDatabase.LoadAssetAtPath<SceneAsset>(s.path))
            .Select(s => new SceneSetup
            {
                path = s.path,
                isLoaded = s.isLoaded,
                isActive = s.isActive
            })
            .ToArray();

        if (restoreSetup.Length == 0)
        {
            ClearSelectionRestoreState();
            ClearHierarchyRestoreState();
            return;
        }

        EditorSceneManager.RestoreSceneManagerSetup(restoreSetup);

        bool hierarchyQueued = QueueHierarchyRestore();
        if (!hierarchyQueued)
            QueueSelectionOrFirstHierarchyItem();
    }

    private static void SaveSelectionForRestore()
    {
        ClearSelectionRestoreState();

        GameObject selectedObject = Selection.activeGameObject;
        if (selectedObject == null)
            return;

        Scene selectedScene = selectedObject.scene;
        if (!selectedScene.IsValid() || !selectedScene.isLoaded || string.IsNullOrWhiteSpace(selectedScene.path))
            return;

        string siblingPath = BuildSiblingPath(selectedObject.transform);
        if (string.IsNullOrWhiteSpace(siblingPath))
            return;

        SessionState.SetString(SelectedScenePathKey, selectedScene.path);
        SessionState.SetString(SelectedSiblingPathKey, siblingPath);
    }

    private static bool HasSelectionRestoreData()
    {
        string scenePath = SessionState.GetString(SelectedScenePathKey, string.Empty);
        string siblingPath = SessionState.GetString(SelectedSiblingPathKey, string.Empty);
        return !string.IsNullOrWhiteSpace(scenePath) && !string.IsNullOrWhiteSpace(siblingPath);
    }

    private static string BuildSiblingPath(Transform transform)
    {
        var indices = new List<int>();

        Transform current = transform;
        while (current != null)
        {
            indices.Add(current.GetSiblingIndex());
            current = current.parent;
        }

        indices.Reverse();
        return string.Join("/", indices);
    }

    private static void QueueSelectionRestore()
    {
        if (!HasSelectionRestoreData())
            return;

        selectionRestoreAttemptsRemaining = SelectionRestoreAttemptCount;
        EditorApplication.delayCall -= TryRestoreSelectionDelayed;
        EditorApplication.delayCall += TryRestoreSelectionDelayed;
    }

    private static void TryRestoreSelectionDelayed()
    {
        if (TryRestoreSelectionNow())
        {
            ClearSelectionRestoreState();
            return;
        }

        selectionRestoreAttemptsRemaining--;
        if (selectionRestoreAttemptsRemaining <= 0)
        {
            ClearSelectionRestoreState();
            return;
        }

        EditorApplication.delayCall += TryRestoreSelectionDelayed;
    }

    private static bool TryRestoreSelectionNow()
    {
        string scenePath = SessionState.GetString(SelectedScenePathKey, string.Empty);
        string siblingPath = SessionState.GetString(SelectedSiblingPathKey, string.Empty);
        if (string.IsNullOrWhiteSpace(scenePath) || string.IsNullOrWhiteSpace(siblingPath))
            return false;

        Scene scene = SceneManager.GetSceneByPath(scenePath);
        if (!scene.IsValid() || !scene.isLoaded)
            return false;

        if (!TryResolveTransform(scene, siblingPath, out Transform target))
            return false;

        Selection.activeGameObject = target.gameObject;
        EditorGUIUtility.PingObject(target.gameObject);
        return true;
    }

    private static bool TryResolveTransform(Scene scene, string siblingPath, out Transform target)
    {
        target = null;

        string[] parts = siblingPath.Split('/');
        if (parts.Length == 0 || !TryParseIndex(parts[0], out int rootIndex))
            return false;

        GameObject[] rootObjects = scene.GetRootGameObjects();
        if (rootIndex >= rootObjects.Length)
            return false;

        Transform current = rootObjects[rootIndex].transform;
        for (int i = 1; i < parts.Length; i++)
        {
            if (!TryParseIndex(parts[i], out int childIndex) || childIndex >= current.childCount)
                return false;

            current = current.GetChild(childIndex);
        }

        target = current;
        return true;
    }

    private static bool TryParseIndex(string value, out int index)
    {
        if (!int.TryParse(value, out index))
            return false;

        return index >= 0;
    }

    private static void SaveHierarchyExpandedStateForRestore()
    {
        ClearHierarchyRestoreState();

        if (!TryGetExpandedEntityIdsFromHierarchy(out List<object> expandedEntityIds))
            return;

        var globalIds = new List<string>();
        for (int i = 0; i < expandedEntityIds.Count; i++)
        {
            UnityEngine.Object obj = ConvertEntityIdToObject(expandedEntityIds[i]);
            if (obj is not GameObject gameObject)
                continue;

            Scene scene = gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrWhiteSpace(scene.path))
                continue;

            GlobalObjectId globalId = GlobalObjectId.GetGlobalObjectIdSlow(gameObject);
            if (globalId.identifierType == 0)
                continue;

            globalIds.Add(globalId.ToString());
        }

        var container = new HierarchyExpandedContainer
        {
            expandedObjectGlobalIds = globalIds.ToArray()
        };

        SessionState.SetString(ExpandedHierarchyStateKey, JsonUtility.ToJson(container));
    }

    private static bool QueueHierarchyRestore()
    {
        string json = SessionState.GetString(ExpandedHierarchyStateKey, string.Empty);
        if (string.IsNullOrWhiteSpace(json))
            return false;

        hierarchyRestoreAttemptsRemaining = HierarchyRestoreAttemptCount;
        EditorApplication.delayCall -= TryRestoreHierarchyDelayed;
        EditorApplication.delayCall += TryRestoreHierarchyDelayed;
        return true;
    }

    private static void TryRestoreHierarchyDelayed()
    {
        bool restored = TryRestoreHierarchyNow();
        if (restored)
        {
            ClearHierarchyRestoreState();
            QueueSelectionOrFirstHierarchyItem();
            return;
        }

        hierarchyRestoreAttemptsRemaining--;
        if (hierarchyRestoreAttemptsRemaining <= 0)
        {
            ClearHierarchyRestoreState();
            QueueSelectionOrFirstHierarchyItem();
            return;
        }

        EditorApplication.delayCall += TryRestoreHierarchyDelayed;
    }

    private static bool TryRestoreHierarchyNow()
    {
        string json = SessionState.GetString(ExpandedHierarchyStateKey, string.Empty);
        if (string.IsNullOrWhiteSpace(json))
            return true;

        HierarchyExpandedContainer container = JsonUtility.FromJson<HierarchyExpandedContainer>(json);
        if (container?.expandedObjectGlobalIds == null)
            return true;

        List<int> instanceIds = ResolveExpandedObjectInstanceIds(container.expandedObjectGlobalIds);

        if (container.expandedObjectGlobalIds.Length > 0 && instanceIds.Count == 0)
            return false;

        return TrySetHierarchyExpandedFromInstanceIds(instanceIds);
    }

    private static void QueueSelectionOrFirstHierarchyItem()
    {
        if (HasSelectionRestoreData())
        {
            QueueSelectionRestore();
            return;
        }

        SelectFirstHierarchyItemIfViable();
    }

    private static void SelectFirstHierarchyItemIfViable()
    {
        if (Selection.activeObject != null)
            return;

        Scene activeScene = SceneManager.GetActiveScene();
        if (TrySelectFirstRootInScene(activeScene))
            return;

        int sceneCount = SceneManager.sceneCount;
        for (int i = 0; i < sceneCount; i++)
        {
            if (TrySelectFirstRootInScene(SceneManager.GetSceneAt(i)))
                return;
        }
    }

    private static bool TrySelectFirstRootInScene(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return false;

        GameObject[] roots = scene.GetRootGameObjects();
        if (roots == null || roots.Length == 0 || roots[0] == null)
            return false;

        Selection.activeGameObject = roots[0];
        return true;
    }

    private static List<int> ResolveExpandedObjectInstanceIds(string[] globalObjectIds)
    {
        var instanceIds = new List<int>(globalObjectIds.Length);

        for (int i = 0; i < globalObjectIds.Length; i++)
        {
            if (!GlobalObjectId.TryParse(globalObjectIds[i], out GlobalObjectId globalObjectId))
                continue;

            UnityEngine.Object obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(globalObjectId);
            if (obj is not GameObject gameObject)
                continue;

            Scene scene = gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded)
                continue;

            instanceIds.Add(gameObject.GetInstanceID());
        }

        return instanceIds;
    }

    private static bool TryGetExpandedEntityIdsFromHierarchy(out List<object> expandedEntityIds)
    {
        expandedEntityIds = null;

        if (!TryGetExpandedIdsAccessor(out _, out _, out object expandedIdsValue))
            return false;

        if (expandedIdsValue is not IEnumerable enumerable)
            return false;

        expandedEntityIds = new List<object>();
        foreach (object entityId in enumerable)
        {
            if (entityId == null)
                continue;

            expandedEntityIds.Add(entityId);
        }

        return true;
    }

    private static bool TrySetHierarchyExpandedFromInstanceIds(List<int> instanceIds)
    {
        if (!TryGetExpandedIdsAccessor(out object owner, out MemberInfo memberInfo, out object expandedIdsValue))
            return false;

        Type memberType = GetMemberType(memberInfo);
        Type elementType = GetCollectionElementType(expandedIdsValue?.GetType() ?? memberType) ?? typeof(int);

        List<int> uniqueIds = instanceIds.Distinct().ToList();
        IList convertedValues = CreateTypedList(elementType);
        for (int i = 0; i < uniqueIds.Count; i++)
        {
            if (!TryConvertInstanceIdToEntityId(elementType, uniqueIds[i], out object convertedValue))
                continue;

            convertedValues.Add(convertedValue);
        }

        if (expandedIdsValue is IList existingList && !existingList.IsReadOnly && !existingList.IsFixedSize)
        {
            existingList.Clear();
            foreach (object value in convertedValues)
                existingList.Add(value);

            EditorApplication.RepaintHierarchyWindow();
            return true;
        }

        object assignValue;
        if (!TryBuildAssignableCollectionValue(memberType, elementType, convertedValues, out assignValue))
            return false;

        if (!SetMemberValue(owner, memberInfo, assignValue))
            return false;

        EditorApplication.RepaintHierarchyWindow();
        return true;
    }

    private static bool TryBuildAssignableCollectionValue(Type memberType, Type elementType, IList sourceValues, out object assignValue)
    {
        assignValue = null;

        if (memberType == null)
            return false;

        if (memberType.IsArray)
        {
            Array array = Array.CreateInstance(elementType, sourceValues.Count);
            for (int i = 0; i < sourceValues.Count; i++)
                array.SetValue(sourceValues[i], i);

            assignValue = array;
            return true;
        }

        if (memberType.IsAssignableFrom(sourceValues.GetType()))
        {
            assignValue = sourceValues;
            return true;
        }

        if (typeof(IList).IsAssignableFrom(memberType))
        {
            Type concreteType = memberType;
            if (memberType.IsAbstract || memberType.IsInterface)
                concreteType = typeof(List<>).MakeGenericType(elementType);

            if (Activator.CreateInstance(concreteType) is IList list)
            {
                foreach (object value in sourceValues)
                    list.Add(value);

                assignValue = list;
                return true;
            }
        }

        return false;
    }

    private static Type GetCollectionElementType(Type collectionType)
    {
        if (collectionType == null)
            return null;

        if (collectionType.IsArray)
            return collectionType.GetElementType();

        if (collectionType.IsGenericType)
        {
            Type[] args = collectionType.GetGenericArguments();
            if (args.Length == 1)
                return args[0];
        }

        Type enumerableInterface = collectionType
            .GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

        return enumerableInterface?.GetGenericArguments().FirstOrDefault();
    }

    private static IList CreateTypedList(Type elementType)
    {
        Type listType = typeof(List<>).MakeGenericType(elementType ?? typeof(int));
        return (IList)Activator.CreateInstance(listType);
    }

    private static bool TryConvertInstanceIdToEntityId(Type targetType, int instanceId, out object convertedValue)
    {
        convertedValue = null;

        if (targetType == null || targetType == typeof(object) || targetType == typeof(int))
        {
            convertedValue = instanceId;
            return true;
        }

        if (targetType == typeof(long))
        {
            convertedValue = (long)instanceId;
            return true;
        }

        if (targetType == typeof(short))
        {
            convertedValue = (short)instanceId;
            return true;
        }

        ConstructorInfo ctor = targetType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int) }, null);
        if (ctor != null)
        {
            convertedValue = ctor.Invoke(new object[] { instanceId });
            return true;
        }

        ctor = targetType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(long) }, null);
        if (ctor != null)
        {
            convertedValue = ctor.Invoke(new object[] { (long)instanceId });
            return true;
        }

        MethodInfo factory = targetType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(m =>
                m.ReturnType == targetType &&
                m.GetParameters().Length == 1 &&
                m.GetParameters()[0].ParameterType == typeof(int));
        if (factory != null)
        {
            convertedValue = factory.Invoke(null, new object[] { instanceId });
            return true;
        }

        object boxed = Activator.CreateInstance(targetType);
        if (TrySetIntMemberValue(ref boxed, instanceId))
        {
            convertedValue = boxed;
            return true;
        }

        return false;
    }

    private static bool TrySetIntMemberValue(ref object boxedValue, int value)
    {
        if (boxedValue == null)
            return false;

        string[] memberNames =
        {
            "m_Value",
            "value",
            "m_Id",
            "id",
            "m_InstanceId",
            "instanceId",
            "m_EntityId",
            "entityId"
        };

        Type type = boxedValue.GetType();
        for (int i = 0; i < memberNames.Length; i++)
        {
            if (TryFindMember(type, memberNames[i], out MemberInfo member))
            {
                Type memberType = GetMemberType(member);
                if (memberType == typeof(int))
                {
                    if (SetMemberValue(boxedValue, member, value))
                        return true;
                }
                else if (memberType == typeof(long))
                {
                    if (SetMemberValue(boxedValue, member, (long)value))
                        return true;
                }
            }
        }

        return false;
    }

    private static UnityEngine.Object ConvertEntityIdToObject(object entityId)
    {
        if (entityId == null)
            return null;

        Type editorUtilityType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.EditorUtility");
        if (editorUtilityType == null)
            return null;

        MethodInfo[] methods = editorUtilityType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

        MethodInfo[] entityMethods = methods.Where(m =>
            m.Name == "EntityIdToObject" &&
            m.ReturnType == typeof(UnityEngine.Object) &&
            m.GetParameters().Length == 1).ToArray();

        for (int i = 0; i < entityMethods.Length; i++)
        {
            MethodInfo entityMethod = entityMethods[i];
            ParameterInfo p = entityMethod.GetParameters()[0];
            Type parameterType = p.ParameterType;

            if (parameterType.IsInstanceOfType(entityId))
            {
                UnityEngine.Object direct = entityMethod.Invoke(null, new[] { entityId }) as UnityEngine.Object;
                if (direct != null)
                    return direct;
            }

            if (TryConvertEntityIdObject(entityId, parameterType, out object convertedEntityId))
            {
                UnityEngine.Object converted = entityMethod.Invoke(null, new[] { convertedEntityId }) as UnityEngine.Object;
                if (converted != null)
                    return converted;
            }
        }

        MethodInfo legacyMethod = methods.FirstOrDefault(m =>
            m.Name == "InstanceIDToObject" &&
            m.ReturnType == typeof(UnityEngine.Object) &&
            m.GetParameters().Length == 1 &&
            m.GetParameters()[0].ParameterType == typeof(int));

        if (legacyMethod != null && TryExtractIntFromEntityId(entityId, out int instanceId))
            return legacyMethod.Invoke(null, new object[] { instanceId }) as UnityEngine.Object;

        return null;
    }

    private static bool TryConvertEntityIdObject(object entityId, Type targetType, out object converted)
    {
        converted = null;

        if (entityId == null || targetType == null)
            return false;

        if (targetType.IsInstanceOfType(entityId))
        {
            converted = entityId;
            return true;
        }

        if (TryExtractIntFromEntityId(entityId, out int value))
            return TryConvertInstanceIdToEntityId(targetType, value, out converted);

        return false;
    }

    private static bool TryExtractIntFromEntityId(object entityId, out int value)
    {
        value = 0;

        switch (entityId)
        {
            case int intValue:
                value = intValue;
                return true;
            case long longValue when longValue <= int.MaxValue && longValue >= int.MinValue:
                value = (int)longValue;
                return true;
            case short shortValue:
                value = shortValue;
                return true;
        }

        string[] memberNames =
        {
            "m_Value",
            "value",
            "m_Id",
            "id",
            "m_InstanceId",
            "instanceId",
            "m_EntityId",
            "entityId"
        };

        for (int i = 0; i < memberNames.Length; i++)
        {
            object memberValue = GetMemberValue(entityId, memberNames[i]);
            if (memberValue is int memberInt)
            {
                value = memberInt;
                return true;
            }

            if (memberValue is long memberLong && memberLong <= int.MaxValue && memberLong >= int.MinValue)
            {
                value = (int)memberLong;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetExpandedIdsAccessor(out object owner, out MemberInfo memberInfo, out object expandedIdsValue)
    {
        owner = null;
        memberInfo = null;
        expandedIdsValue = null;

        if (!TryGetHierarchyWindow(out EditorWindow hierarchyWindow))
            return false;

        object sceneHierarchy = GetMemberValue(hierarchyWindow, "m_SceneHierarchy");
        object treeView = GetMemberValue(sceneHierarchy, "m_TreeView") ?? GetMemberValue(hierarchyWindow, "m_TreeView");
        object treeViewState = GetMemberValue(treeView, "m_TreeViewState") ??
                               GetMemberValue(treeView, "treeViewState") ??
                               GetMemberValue(sceneHierarchy, "m_TreeViewState") ??
                               GetMemberValue(sceneHierarchy, "treeViewState") ??
                               GetMemberValue(hierarchyWindow, "m_TreeViewState") ??
                               GetMemberValue(hierarchyWindow, "treeViewState");

        object[] candidates = { treeViewState, treeView, sceneHierarchy, hierarchyWindow };
        for (int i = 0; i < candidates.Length; i++)
        {
            object candidate = candidates[i];
            if (candidate == null)
                continue;

            if (TryFindExpandedIdsMember(candidate, out memberInfo))
            {
                owner = candidate;
                expandedIdsValue = GetMemberValue(owner, memberInfo);
                return true;
            }
        }

        return false;
    }

    private static bool TryGetHierarchyWindow(out EditorWindow hierarchyWindow)
    {
        hierarchyWindow = null;

        EditorWindow[] windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
        if (windows == null || windows.Length == 0)
            return false;

        Type hierarchyInterfaceType = FindTypeInLoadedAssemblies("UnityEditor.IHierarchyWindow");

        for (int i = 0; i < windows.Length; i++)
        {
            EditorWindow window = windows[i];
            if (window == null)
                continue;

            Type windowType = window.GetType();
            string typeName = windowType.FullName ?? windowType.Name;

            bool isHierarchyByInterface = hierarchyInterfaceType != null && hierarchyInterfaceType.IsAssignableFrom(windowType);
            bool isHierarchyByName = typeName.IndexOf("Hierarchy", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                     typeName.IndexOf("Project", StringComparison.OrdinalIgnoreCase) < 0;

            if (!isHierarchyByInterface && !isHierarchyByName)
                continue;

            hierarchyWindow = window;
            return true;
        }

        return false;
    }

    private static Type FindTypeInLoadedAssemblies(string fullTypeName)
    {
        if (string.IsNullOrWhiteSpace(fullTypeName))
            return null;

        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            Type type = assemblies[i].GetType(fullTypeName);
            if (type != null)
                return type;
        }

        return null;
    }

    private static bool TryFindExpandedIdsMember(object source, out MemberInfo memberInfo)
    {
        memberInfo = null;
        if (source == null)
            return false;

        Type type = source.GetType();

        string[] preferredNames =
        {
            "expandedIDs",
            "expandedIds",
            "m_ExpandedIDs",
            "m_ExpandedIds"
        };

        for (int i = 0; i < preferredNames.Length; i++)
        {
            if (TryFindMember(type, preferredNames[i], out memberInfo))
                return true;
        }

        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        MemberInfo[] members = type
            .GetMembers(Flags)
            .Where(m => m.MemberType == MemberTypes.Field || m.MemberType == MemberTypes.Property)
            .ToArray();

        for (int i = 0; i < members.Length; i++)
        {
            MemberInfo member = members[i];
            string name = member.Name ?? string.Empty;
            if (name.IndexOf("expanded", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            Type memberType = GetMemberType(member);
            if (memberType == null)
                continue;

            if (typeof(IEnumerable).IsAssignableFrom(memberType))
            {
                memberInfo = member;
                return true;
            }
        }

        return false;
    }

    private static bool TryFindMember(Type type, string memberName, out MemberInfo memberInfo)
    {
        memberInfo = null;
        if (type == null || string.IsNullOrWhiteSpace(memberName))
            return false;

        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        while (type != null)
        {
            FieldInfo field = type.GetField(memberName, Flags);
            if (field != null)
            {
                memberInfo = field;
                return true;
            }

            PropertyInfo property = type.GetProperty(memberName, Flags);
            if (property != null && property.GetIndexParameters().Length == 0)
            {
                memberInfo = property;
                return true;
            }

            type = type.BaseType;
        }

        return false;
    }

    private static object GetMemberValue(object source, string memberName)
    {
        if (source == null || string.IsNullOrWhiteSpace(memberName))
            return null;

        if (!TryFindMember(source.GetType(), memberName, out MemberInfo memberInfo))
            return null;

        return GetMemberValue(source, memberInfo);
    }

    private static object GetMemberValue(object source, MemberInfo memberInfo)
    {
        if (source == null || memberInfo == null)
            return null;

        if (memberInfo is FieldInfo field)
            return field.GetValue(source);

        if (memberInfo is PropertyInfo property && property.CanRead && property.GetIndexParameters().Length == 0)
            return property.GetValue(source);

        return null;
    }

    private static bool SetMemberValue(object source, MemberInfo memberInfo, object value)
    {
        if (source == null || memberInfo == null)
            return false;

        if (memberInfo is FieldInfo field)
        {
            field.SetValue(source, value);
            return true;
        }

        if (memberInfo is PropertyInfo property && property.CanWrite && property.GetIndexParameters().Length == 0)
        {
            property.SetValue(source, value);
            return true;
        }

        return false;
    }

    private static Type GetMemberType(MemberInfo memberInfo)
    {
        if (memberInfo is FieldInfo field)
            return field.FieldType;

        if (memberInfo is PropertyInfo property)
            return property.PropertyType;

        return null;
    }

    private static void ClearRestoreState()
    {
        SessionState.SetBool(RestorePendingKey, false);
        SessionState.EraseString(PreviousSetupKey);
        ClearSelectionRestoreState();
        ClearHierarchyRestoreState();
    }

    private static void ClearSelectionRestoreState()
    {
        selectionRestoreAttemptsRemaining = 0;
        SessionState.EraseString(SelectedScenePathKey);
        SessionState.EraseString(SelectedSiblingPathKey);
        EditorApplication.delayCall -= TryRestoreSelectionDelayed;
    }

    private static void ClearHierarchyRestoreState()
    {
        hierarchyRestoreAttemptsRemaining = 0;
        SessionState.EraseString(ExpandedHierarchyStateKey);
        EditorApplication.delayCall -= TryRestoreHierarchyDelayed;
    }
    }
}
