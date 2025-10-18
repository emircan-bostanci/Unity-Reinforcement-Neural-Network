using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;

/// <summary>
/// Custom editor for the Agent component to provide enhanced model loading UI
/// </summary>
[CustomEditor(typeof(Agent))]
public class AgentEditor : Editor
{
    private Agent agent;
    private string[] availableModels;
    private int selectedModelIndex = 0;
    private bool showModelSection = true;
    private bool showAdvancedOptions = false;
    
    private GUIStyle headerStyle;
    private GUIStyle warningStyle;
    private GUIStyle successStyle;
    
    void OnEnable()
    {
        agent = (Agent)target;
        RefreshAvailableModels();
        InitializeStyles();
    }
    
    private void InitializeStyles()
    {
        if (headerStyle == null)
        {
            headerStyle = new GUIStyle(EditorStyles.boldLabel);
            headerStyle.fontSize = 11;
        }
        
        if (warningStyle == null)
        {
            warningStyle = new GUIStyle(EditorStyles.label);
            warningStyle.normal.textColor = Color.yellow;
            warningStyle.fontStyle = FontStyle.Italic;
        }
        
        if (successStyle == null)
        {
            successStyle = new GUIStyle(EditorStyles.label);
            successStyle.normal.textColor = Color.green;
            successStyle.fontStyle = FontStyle.Bold;
        }
    }
    
    public override void OnInspectorGUI()
    {
        InitializeStyles();
        
        // Draw default inspector
        DrawDefaultInspector();
        
        EditorGUILayout.Space(10);
        
        // Model Loading Section
        DrawModelLoadingSection();
        
        // Performance Section
        if (Application.isPlaying)
        {
            DrawPerformanceSection();
        }
    }
    
    private void DrawModelLoadingSection()
    {
        EditorGUILayout.BeginVertical(GUI.skin.box);
        
        showModelSection = EditorGUILayout.Foldout(showModelSection, "🤖 Model Loading", true);
        
        if (showModelSection)
        {
            EditorGUILayout.Space(5);
            
            // Current model info
            DrawCurrentModelInfo();
            
            EditorGUILayout.Space(5);
            
            // Model selection
            DrawModelSelection();
            
            EditorGUILayout.Space(5);
            
            // Action buttons
            DrawModelActionButtons();
            
            // Advanced options
            DrawAdvancedOptions();
        }
        
        EditorGUILayout.EndVertical();
    }
    
    private void DrawCurrentModelInfo()
    {
        EditorGUILayout.LabelField("Current Status:", headerStyle);
        
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Network Type:", GUILayout.Width(100));
        EditorGUILayout.LabelField(agent.networkType.ToString(), EditorStyles.boldLabel);
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Loaded Model:", GUILayout.Width(100));
        
        string modelInfo = agent.GetLoadedModelInfo();
        if (modelInfo == "No model loaded")
        {
            EditorGUILayout.LabelField(modelInfo, warningStyle);
        }
        else
        {
            EditorGUILayout.LabelField(modelInfo, successStyle);
        }
        EditorGUILayout.EndHorizontal();
        
        if (agent.HasLoadedModel())
        {
            EditorGUILayout.HelpBox("✅ Model loaded successfully. Agent is ready for inference.", MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox("⚠️ No model loaded. Agent will use random actions or untrained network.", MessageType.Warning);
        }
    }
    
    private void DrawModelSelection()
    {
        EditorGUILayout.LabelField("Model Selection:", headerStyle);
        
        EditorGUILayout.BeginHorizontal();
        
        if (GUILayout.Button("🔄 Refresh", GUILayout.Width(70)))
        {
            RefreshAvailableModels();
        }
        
        EditorGUILayout.LabelField("Model:", GUILayout.Width(50));
        
        if (availableModels != null && availableModels.Length > 0)
        {
            selectedModelIndex = EditorGUILayout.Popup(selectedModelIndex, availableModels);
        }
        else
        {
            EditorGUILayout.LabelField("No models found", warningStyle);
        }
        
        EditorGUILayout.EndHorizontal();
        
        // Show selected model details
        if (availableModels != null && selectedModelIndex >= 0 && selectedModelIndex < availableModels.Length)
        {
            string selectedModel = availableModels[selectedModelIndex];
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Selected:", GUILayout.Width(60));
            EditorGUILayout.SelectableLabel(selectedModel, EditorStyles.miniLabel, GUILayout.Height(16));
            EditorGUILayout.EndHorizontal();
        }
    }
    
    private void DrawModelActionButtons()
    {
        EditorGUILayout.BeginHorizontal();
        
        // Load selected model
        GUI.enabled = availableModels != null && availableModels.Length > 0;
        if (GUILayout.Button("📥 Load Selected Model"))
        {
            LoadSelectedModel();
        }
        GUI.enabled = true;
        
        // Load best model
        if (GUILayout.Button("⭐ Load Best Model"))
        {
            agent.LoadBestAvailableModel();
        }
        
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.BeginHorizontal();
        
        // Clear model
        GUI.enabled = agent.HasLoadedModel();
        if (GUILayout.Button("🗑️ Clear Model"))
        {
            agent.ClearLoadedModel();
        }
        GUI.enabled = true;
        
        // Browse models
        if (GUILayout.Button("📁 Browse Folder"))
        {
            BrowseModelFolder();
        }
        
        EditorGUILayout.EndHorizontal();
    }
    
    private void DrawAdvancedOptions()
    {
        EditorGUILayout.Space(5);
        showAdvancedOptions = EditorGUILayout.Foldout(showAdvancedOptions, "Advanced Options");
        
        if (showAdvancedOptions)
        {
            EditorGUILayout.BeginVertical(GUI.skin.box);
            
            // Network type switching
            EditorGUILayout.LabelField("Network Type Switching:", EditorStyles.boldLabel);
            
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Switch to Simple NN"))
            {
                agent.SwitchToSimpleNN();
            }
            
            if (GUILayout.Button("Switch to LSTM RNN"))
            {
                agent.SwitchToLSTM();
            }
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space(5);
            
            // Validation settings
            EditorGUILayout.LabelField("Validation:", EditorStyles.boldLabel);
            
            SerializedProperty validationProp = serializedObject.FindProperty("validateModelBeforeLoading");
            if (validationProp != null)
            {
                EditorGUILayout.PropertyField(validationProp, new GUIContent("Validate Before Loading"));
                serializedObject.ApplyModifiedProperties();
            }
            
            EditorGUILayout.EndVertical();
        }
    }
    
    private void DrawPerformanceSection()
    {
        EditorGUILayout.Space(5);
        EditorGUILayout.BeginVertical(GUI.skin.box);
        
        EditorGUILayout.LabelField("📊 Runtime Performance", headerStyle);
        
        if (GUILayout.Button("Show Performance Stats"))
        {
            agent.LogPerformanceStats();
        }
        
        EditorGUILayout.EndVertical();
    }
    
    private void RefreshAvailableModels()
    {
        string projectPath = Application.dataPath.Replace("/Assets", "");
        string savedModelsPath = Path.Combine(projectPath, "SavedModels");
        
        if (Directory.Exists(savedModelsPath))
        {
            string[] jsonFiles = Directory.GetFiles(savedModelsPath, "*.json");
            
            // Sort by modification time (most recent first)
            availableModels = jsonFiles
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .Select(f => Path.GetFileName(f))
                .ToArray();
        }
        else
        {
            availableModels = new string[0];
        }
        
        selectedModelIndex = 0;
    }
    
    private void LoadSelectedModel()
    {
        if (availableModels == null || selectedModelIndex < 0 || selectedModelIndex >= availableModels.Length)
        {
            EditorUtility.DisplayDialog("Error", "No valid model selected.", "OK");
            return;
        }
        
        string projectPath = Application.dataPath.Replace("/Assets", "");
        string savedModelsPath = Path.Combine(projectPath, "SavedModels");
        string modelPath = Path.Combine(savedModelsPath, availableModels[selectedModelIndex]);
        
        if (agent.LoadModelSafe(modelPath))
        {
            EditorUtility.DisplayDialog("Success", $"Model '{availableModels[selectedModelIndex]}' loaded successfully!", "OK");
        }
        else
        {
            EditorUtility.DisplayDialog("Error", $"Failed to load model '{availableModels[selectedModelIndex]}'.", "OK");
        }
    }
    
    private void BrowseModelFolder()
    {
        string projectPath = Application.dataPath.Replace("/Assets", "");
        string savedModelsPath = Path.Combine(projectPath, "SavedModels");
        
        if (Directory.Exists(savedModelsPath))
        {
            EditorUtility.RevealInFinder(savedModelsPath);
        }
        else
        {
            EditorUtility.DisplayDialog("Folder Not Found", $"SavedModels folder not found at:{savedModelsPath}", "OK");
        }
    }
}