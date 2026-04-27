using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ape.Editor
{
    public sealed class EditorSceneSelector : EditorWindow
    {
        private const string ScenesRootPath = "Assets/_Game/Scenes";
        private const string PersistedSceneStatesKey = "EditorSceneSelector.SceneStates";
        private const int RestoreAttemptCount = 10;

    [Serializable]
    private sealed class SceneStateContainer
    {
        public SceneState[] scenes = Array.Empty<SceneState>();
    }

    [Serializable]
    private sealed class SceneState
    {
        public string scenePath;
        public string selectedSiblingPath;
        public string[] expandedObjectGlobalIds = Array.Empty<string>();
    }

    private readonly List<string> scenePaths = new();
    private readonly Dictionary<string, SceneState> statesByScenePath = new(StringComparer.Ordinal);

    private Vector2 sceneListScrollPosition;
    private string pendingRestoreScenePath = string.Empty;
    private int hierarchyRestoreAttemptsRemaining;
    private int selectionRestoreAttemptsRemaining;

    [MenuItem("Tools/VoxelSim/Scenes/Scene Selector")]
    private static void OpenWindow()
    {
        EditorSceneSelector window = GetWindow<EditorSceneSelector>("Scene Selector");
        window.minSize = new Vector2(320f, 260f);
        window.Show();
    }

    private void OnEnable()
    {
        LoadPersistedSceneStates();
        RefreshSceneList();
    }

    private void OnDisable()
    {
        SaveStateForScene(SceneManager.GetActiveScene());
        PersistSceneStates();
        ClearPendingRestore();
    }

    private void OnProjectChange()
    {
        RefreshSceneList();
        Repaint();
    }

    private void OnGUI()
    {
        DrawToolbar();
        DrawActiveSceneInfo();
        DrawSceneButtons();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(74f)))
            RefreshSceneList();

        if (GUILayout.Button("Save State", EditorStyles.toolbarButton, GUILayout.Width(82f)))
        {
            SaveStateForScene(SceneManager.GetActiveScene());
            PersistSceneStates();
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    private static void DrawActiveSceneInfo()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        string activeSceneName = activeScene.IsValid() && !string.IsNullOrWhiteSpace(activeScene.path)
            ? Path.GetFileNameWithoutExtension(activeScene.path)
            : "(Unsaved Scene)";
        EditorGUILayout.HelpBox($"Active Scene: {activeSceneName}", MessageType.None);
    }

    private void DrawSceneButtons()
    {
        if (scenePaths.Count == 0)
        {
            EditorGUILayout.HelpBox("No scene assets were found.", MessageType.Info);
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        string activeScenePath = activeScene.path;

        sceneListScrollPosition = EditorGUILayout.BeginScrollView(sceneListScrollPosition);
        for (int i = 0; i < scenePaths.Count; i++)
        {
            string scenePath = scenePaths[i];
            string sceneName = Path.GetFileNameWithoutExtension(scenePath);
            bool isActive = string.Equals(activeScenePath, scenePath, StringComparison.Ordinal);

            Color previousBackground = GUI.backgroundColor;
            if (isActive)
                GUI.backgroundColor = new Color(0.60f, 0.84f, 0.70f, 1f);

            string buttonLabel = isActive ? $"{sceneName} (Active)" : sceneName;
            if (GUILayout.Button(buttonLabel, GUILayout.Height(28f)))
                SwitchToScene(scenePath);

            GUI.backgroundColor = previousBackground;
            GUILayout.Label(scenePath, EditorStyles.miniLabel);
            GUILayout.Space(4f);
        }

        EditorGUILayout.EndScrollView();
    }

    private void RefreshSceneList()
    {
        scenePaths.Clear();

        List<string> foundScenes = FindScenePathsInFolder(ScenesRootPath);
        if (foundScenes.Count == 0)
            foundScenes = FindScenePathsInProject();

        scenePaths.AddRange(foundScenes
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal));
    }

    private static List<string> FindScenePathsInFolder(string folderPath)
    {
        if (!AssetDatabase.IsValidFolder(folderPath))
            return new List<string>();

        return FindScenePaths(AssetDatabase.FindAssets("t:Scene", new[] { folderPath }));
    }

    private static List<string> FindScenePathsInProject()
    {
        return FindScenePaths(AssetDatabase.FindAssets("t:Scene"));
    }

    private static List<string> FindScenePaths(string[] sceneGuids)
    {
        var paths = new List<string>();
        if (sceneGuids == null || sceneGuids.Length == 0)
            return paths;

        for (int i = 0; i < sceneGuids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(sceneGuids[i]);
            if (!path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(path))
                continue;

            paths.Add(path);
        }

        return paths;
    }

    private void SwitchToScene(string targetScenePath)
    {
        if (string.IsNullOrWhiteSpace(targetScenePath))
            return;

        Scene activeScene = SceneManager.GetActiveScene();
        SaveStateForScene(activeScene);
        PersistSceneStates();

        if (!EditorSceneManager.SaveOpenScenes())
        {
            Debug.LogWarning("EditorSceneSelector: Failed to auto-save open scenes. Scene switch cancelled.");
            return;
        }

        bool alreadySingleActive = activeScene.IsValid() &&
                                   activeScene.isLoaded &&
                                   SceneManager.sceneCount == 1 &&
                                   string.Equals(activeScene.path, targetScenePath, StringComparison.Ordinal);
        if (alreadySingleActive)
        {
            QueueRestoreForScene(targetScenePath);
            return;
        }

        EditorSceneManager.OpenScene(targetScenePath, OpenSceneMode.Single);
        QueueRestoreForScene(targetScenePath);
    }

    private void SaveStateForScene(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrWhiteSpace(scene.path))
            return;

        statesByScenePath.TryGetValue(scene.path, out SceneState previousState);

        string selectedSiblingPath = GetSelectedSiblingPathForScene(scene.path);
        string[] expandedObjectGlobalIds = previousState?.expandedObjectGlobalIds ?? Array.Empty<string>();

        if (TryGetExpandedGlobalObjectIdsForScene(scene.path, out string[] capturedGlobalIds))
            expandedObjectGlobalIds = capturedGlobalIds;

        statesByScenePath[scene.path] = new SceneState
        {
            scenePath = scene.path,
            selectedSiblingPath = selectedSiblingPath,
            expandedObjectGlobalIds = expandedObjectGlobalIds
        };
    }

    private static string GetSelectedSiblingPathForScene(string scenePath)
    {
        GameObject selectedObject = Selection.activeGameObject;
        if (selectedObject == null)
            return string.Empty;

        Scene selectedScene = selectedObject.scene;
        if (!selectedScene.IsValid() || !selectedScene.isLoaded)
            return string.Empty;

        if (!string.Equals(selectedScene.path, scenePath, StringComparison.Ordinal))
            return string.Empty;

        return BuildSiblingPath(selectedObject.transform);
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

    private void QueueRestoreForScene(string scenePath)
    {
        pendingRestoreScenePath = scenePath;
        hierarchyRestoreAttemptsRemaining = RestoreAttemptCount;
        selectionRestoreAttemptsRemaining = RestoreAttemptCount;
        EditorApplication.delayCall -= TryRestorePendingSceneState;
        EditorApplication.delayCall += TryRestorePendingSceneState;
    }

    private void TryRestorePendingSceneState()
    {
        if (string.IsNullOrWhiteSpace(pendingRestoreScenePath))
            return;

        Scene scene = SceneManager.GetSceneByPath(pendingRestoreScenePath);
        if (!scene.IsValid() || !scene.isLoaded)
        {
            QueueDelayedRetryOrFinish();
            return;
        }

        bool hierarchyRestored = TryRestoreHierarchyForScene(scene.path);
        bool selectionRestored = TryRestoreSelectionForScene(scene.path);

        if (!hierarchyRestored && hierarchyRestoreAttemptsRemaining > 0)
            hierarchyRestoreAttemptsRemaining--;

        if (!selectionRestored && selectionRestoreAttemptsRemaining > 0)
            selectionRestoreAttemptsRemaining--;

        bool shouldRetry = !hierarchyRestored && hierarchyRestoreAttemptsRemaining > 0 ||
                           !selectionRestored && selectionRestoreAttemptsRemaining > 0;
        if (shouldRetry)
        {
            EditorApplication.delayCall += TryRestorePendingSceneState;
            return;
        }

        ClearPendingRestore();
        EnsureSceneSelection(scene);
    }

    private void QueueDelayedRetryOrFinish()
    {
        if (hierarchyRestoreAttemptsRemaining > 0)
            hierarchyRestoreAttemptsRemaining--;
        if (selectionRestoreAttemptsRemaining > 0)
            selectionRestoreAttemptsRemaining--;

        if (hierarchyRestoreAttemptsRemaining > 0 || selectionRestoreAttemptsRemaining > 0)
        {
            EditorApplication.delayCall += TryRestorePendingSceneState;
            return;
        }

        ClearPendingRestore();
    }

    private void ClearPendingRestore()
    {
        pendingRestoreScenePath = string.Empty;
        hierarchyRestoreAttemptsRemaining = 0;
        selectionRestoreAttemptsRemaining = 0;
        EditorApplication.delayCall -= TryRestorePendingSceneState;
    }

    private bool TryRestoreHierarchyForScene(string scenePath)
    {
        if (!statesByScenePath.TryGetValue(scenePath, out SceneState sceneState))
            return true;

        string[] globalIds = sceneState.expandedObjectGlobalIds ?? Array.Empty<string>();
        List<int> instanceIds = ResolveExpandedObjectInstanceIds(globalIds, scenePath);
        if (globalIds.Length > 0 && instanceIds.Count == 0)
            return false;

        return TrySetHierarchyExpandedFromInstanceIds(instanceIds);
    }

    private bool TryRestoreSelectionForScene(string scenePath)
    {
        if (!statesByScenePath.TryGetValue(scenePath, out SceneState sceneState))
            return true;

        if (string.IsNullOrWhiteSpace(sceneState.selectedSiblingPath))
            return true;

        Scene scene = SceneManager.GetSceneByPath(scenePath);
        if (!scene.IsValid() || !scene.isLoaded)
            return false;

        if (!TryResolveTransform(scene, sceneState.selectedSiblingPath, out Transform target))
            return false;

        Selection.activeGameObject = target.gameObject;
        EditorGUIUtility.PingObject(target.gameObject);
        return true;
    }

    private static void SelectFirstRootInScene(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return;

        GameObject[] roots = scene.GetRootGameObjects();
        if (roots == null || roots.Length == 0 || roots[0] == null)
            return;

        Selection.activeGameObject = roots[0];
    }

    private static void EnsureSceneSelection(Scene scene)
    {
        if (Selection.activeObject != null)
            return;

        SelectFirstRootInScene(scene);
    }

    private static bool TryResolveTransform(Scene scene, string siblingPath, out Transform target)
    {
        target = null;
        if (string.IsNullOrWhiteSpace(siblingPath))
            return false;

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

    private void LoadPersistedSceneStates()
    {
        statesByScenePath.Clear();

        string json = EditorPrefs.GetString(PersistedSceneStatesKey, string.Empty);
        if (string.IsNullOrWhiteSpace(json))
            return;

        SceneStateContainer container = JsonUtility.FromJson<SceneStateContainer>(json);
        if (container?.scenes == null)
            return;

        for (int i = 0; i < container.scenes.Length; i++)
        {
            SceneState state = container.scenes[i];
            if (state == null || string.IsNullOrWhiteSpace(state.scenePath))
                continue;

            state.expandedObjectGlobalIds ??= Array.Empty<string>();
            state.selectedSiblingPath ??= string.Empty;
            statesByScenePath[state.scenePath] = state;
        }
    }

    private void PersistSceneStates()
    {
        SceneState[] states = statesByScenePath.Values
            .Where(s => s != null && !string.IsNullOrWhiteSpace(s.scenePath))
            .OrderBy(s => s.scenePath, StringComparer.Ordinal)
            .ToArray();

        var container = new SceneStateContainer
        {
            scenes = states
        };

        EditorPrefs.SetString(PersistedSceneStatesKey, JsonUtility.ToJson(container));
    }

    private static bool TryGetExpandedGlobalObjectIdsForScene(string scenePath, out string[] globalIds)
    {
        globalIds = Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(scenePath))
            return false;

        if (!TryGetExpandedEntityIdsFromHierarchy(out List<object> expandedEntityIds))
            return false;

        var ids = new List<string>(expandedEntityIds.Count);
        for (int i = 0; i < expandedEntityIds.Count; i++)
        {
            UnityEngine.Object obj = ConvertEntityIdToObject(expandedEntityIds[i]);
            if (obj is not GameObject gameObject)
                continue;

            Scene objectScene = gameObject.scene;
            if (!objectScene.IsValid() || !objectScene.isLoaded)
                continue;

            if (!string.Equals(objectScene.path, scenePath, StringComparison.Ordinal))
                continue;

            GlobalObjectId globalObjectId = GlobalObjectId.GetGlobalObjectIdSlow(gameObject);
            if (globalObjectId.identifierType == 0)
                continue;

            ids.Add(globalObjectId.ToString());
        }

        globalIds = ids.Distinct(StringComparer.Ordinal).ToArray();
        return true;
    }

    private static List<int> ResolveExpandedObjectInstanceIds(string[] globalObjectIds, string scenePath)
    {
        var instanceIds = new List<int>();
        if (globalObjectIds == null || globalObjectIds.Length == 0)
            return instanceIds;

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

            if (!string.Equals(scene.path, scenePath, StringComparison.Ordinal))
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

        if (!TryBuildAssignableCollectionValue(memberType, elementType, convertedValues, out object assignValue))
            return false;

        if (!SetMemberValue(owner, memberInfo, assignValue))
            return false;

        EditorApplication.RepaintHierarchyWindow();
        return true;
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

        ConstructorInfo ctor = targetType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(int) },
            null);
        if (ctor != null)
        {
            convertedValue = ctor.Invoke(new object[] { instanceId });
            return true;
        }

        ctor = targetType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(long) },
            null);
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
            Type parameterType = entityMethod.GetParameters()[0].ParameterType;

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
    }
}
