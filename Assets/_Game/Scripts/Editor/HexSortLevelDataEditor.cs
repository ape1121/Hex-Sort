using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(HexSortLevelData))]
public sealed class HexSortLevelDataEditor : Editor
{
    private const string PrefsKey = "HexSortLevelGenerator.Params";

    private HexSortLevelGenerator.Parameters parameters;

    private void OnEnable()
    {
        LoadParameters();
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Generator", EditorStyles.boldLabel);

        int colorMax = HexSortLevelGenerator.MaxColorCount;
        parameters.colorCount = EditorGUILayout.IntSlider(
            new GUIContent("Color Count", "Number of distinct colors. One full glass per color in the solved state."),
            parameters.colorCount, 2, colorMax);

        parameters.emptyGlassCount = EditorGUILayout.IntSlider(
            new GUIContent("Empty Glasses", "Extra empty glasses on top of the colored ones (workspace for solving)."),
            parameters.emptyGlassCount, 1, 4);

        parameters.capacity = EditorGUILayout.IntSlider(
            new GUIContent("Capacity", "Slots per glass. Total units of each color = this value."),
            parameters.capacity, 2, 8);

        parameters.scrambleMoves = EditorGUILayout.IntSlider(
            new GUIContent("Scramble Moves", "How many random legal moves to apply when scrambling. More = harder."),
            parameters.scrambleMoves, 1, 200);

        EditorGUILayout.BeginHorizontal();
        parameters.randomSeed = EditorGUILayout.IntField(
            new GUIContent("Seed", "Random seed. Same parameters + seed reproduces the same level."),
            parameters.randomSeed);
        if (GUILayout.Button("Random", GUILayout.Width(72)))
        {
            parameters.randomSeed = Random.Range(int.MinValue, int.MaxValue);
        }
        EditorGUILayout.EndHorizontal();

        int totalGlasses = parameters.colorCount + parameters.emptyGlassCount;
        int totalUnits = parameters.colorCount * parameters.capacity;
        EditorGUILayout.HelpBox(
            $"Will produce {totalGlasses} glasses ({parameters.colorCount} colored + {parameters.emptyGlassCount} empty), " +
            $"{totalUnits} units total, capacity {parameters.capacity} each.",
            MessageType.None);

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Generate"))
        {
            HexSortLevelData level = (HexSortLevelData)target;
            Undo.RecordObject(level, "Generate Hex Sort Level");
            HexSortLevelGenerator.Populate(level, parameters);
            EditorUtility.SetDirty(level);
            AssetDatabase.SaveAssetIfDirty(level);
            SaveParameters();
        }

        if (GUILayout.Button("Generate New Seed"))
        {
            parameters.randomSeed = Random.Range(int.MinValue, int.MaxValue);
            HexSortLevelData level = (HexSortLevelData)target;
            Undo.RecordObject(level, "Generate Hex Sort Level");
            HexSortLevelGenerator.Populate(level, parameters);
            EditorUtility.SetDirty(level);
            AssetDatabase.SaveAssetIfDirty(level);
            SaveParameters();
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Presets", EditorStyles.miniBoldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Easy"))
        {
            parameters = new HexSortLevelGenerator.Parameters
            {
                colorCount = 3,
                emptyGlassCount = 2,
                capacity = 3,
                scrambleMoves = 25,
                randomSeed = parameters.randomSeed,
            };
            SaveParameters();
        }
        if (GUILayout.Button("Medium"))
        {
            parameters = new HexSortLevelGenerator.Parameters
            {
                colorCount = 4,
                emptyGlassCount = 2,
                capacity = 4,
                scrambleMoves = 60,
                randomSeed = parameters.randomSeed,
            };
            SaveParameters();
        }
        if (GUILayout.Button("Hard"))
        {
            parameters = new HexSortLevelGenerator.Parameters
            {
                colorCount = 5,
                emptyGlassCount = 2,
                capacity = 5,
                scrambleMoves = 120,
                randomSeed = parameters.randomSeed,
            };
            SaveParameters();
        }
        if (GUILayout.Button("Expert"))
        {
            parameters = new HexSortLevelGenerator.Parameters
            {
                colorCount = 6,
                emptyGlassCount = 2,
                capacity = 6,
                scrambleMoves = 200,
                randomSeed = parameters.randomSeed,
            };
            SaveParameters();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        if (GUILayout.Button("Reset Parameters to Defaults"))
        {
            parameters = HexSortLevelGenerator.Parameters.Default;
            SaveParameters();
        }
    }

    private void LoadParameters()
    {
        parameters = HexSortLevelGenerator.Parameters.Default;
        if (!EditorPrefs.HasKey(PrefsKey))
        {
            return;
        }

        string json = EditorPrefs.GetString(PrefsKey, string.Empty);
        if (string.IsNullOrEmpty(json))
        {
            return;
        }

        try
        {
            parameters = JsonUtility.FromJson<HexSortLevelGenerator.Parameters>(json);
        }
        catch
        {
            parameters = HexSortLevelGenerator.Parameters.Default;
        }
    }

    private void SaveParameters()
    {
        try
        {
            string json = JsonUtility.ToJson(parameters);
            EditorPrefs.SetString(PrefsKey, json);
        }
        catch
        {
            // Persistence is best-effort.
        }
    }
}
