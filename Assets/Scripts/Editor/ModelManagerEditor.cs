using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Collections.Generic;

/// <summary>
/// Custom editor for the ModelManager component to provide enhanced UI for model browsing and loading
/// </summary>
[CustomEditor(typeof(ModelManager))]
public class ModelManagerEditor : Editor
{
    private ModelManager modelManager;
    private Vector2 scrollPosition;
    private string searchFilter = "";
    private int selectedAgentFilter = -1; // -1 = all agents
    private int selectedGenerationFilter = -1; // -1 = all generations
    private bool showModelDetails = true;
    private bool autoRefresh = true;
    
    private GUIStyle headerStyle;
    private GUIStyle buttonStyle;
    private GUIStyle boxStyle;
    
    void OnEnable()
    {
        modelManager = (ModelManager)target;
        InitializeStyles();
    }
    
    private void InitializeStyles()
    {
        if (headerStyle == null)
        {
            headerStyle = new GUIStyle(EditorStyles.boldLabel);
            headerStyle.fontSize = 12;
            headerStyle.normal.textColor = Color.white;
        }
        
        if (buttonStyle == null)
        {
            buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.fontStyle = FontStyle.Bold;
        }
        
        if (boxStyle == null)
        {
            boxStyle = new GUIStyle(GUI.skin.box);
            boxStyle.padding = new RectOffset(10, 10, 5, 5);
        }
    }
    
    public override void OnInspectorGUI()
    {
        InitializeStyles();
        
        // Draw default inspector
        DrawDefaultInspector();
        
        EditorGUILayout.Space(10);
        
        // Model Management Section
        EditorGUILayout.BeginVertical(boxStyle);
        
        EditorGUILayout.LabelField("🎛️ Model Management", headerStyle);
        EditorGUILayout.Space(5);
        
        // Control buttons
        EditorGUILayout.BeginHorizontal();
        
        if (GUILayout.Button("🔄 Refresh Models", buttonStyle))
        {
            modelManager.RefreshModelList();
        }
        
        if (GUILayout.Button("📊 Show Statistics", buttonStyle))
        {
            modelManager.PrintModelStatistics();
        }
        
        if (GUILayout.Button("📋 List Models", buttonStyle))
        {
            modelManager.PrintAvailableModels();
        }
        
        EditorGUILayout.EndHorizontal();
        
        // Quick load button
        EditorGUILayout.Space(5);
        if (GUILayout.Button("⚡ Load Best Models Into All Agents", GUILayout.Height(25)))
        {
            modelManager.LoadBestModelsIntoAllAgents();
        }
        
        EditorGUILayout.EndVertical();
        
        EditorGUILayout.Space(10);
        
        // Model Browser Section
        DrawModelBrowser();
        
        // Auto-refresh if enabled
        if (autoRefresh && Application.isPlaying)
        {
            Repaint();
        }
    }
    
    private void DrawModelBrowser()
    {
        EditorGUILayout.BeginVertical(boxStyle);
        
        EditorGUILayout.LabelField("🔍 Model Browser", headerStyle);
        EditorGUILayout.Space(5);
        
        // Filters
        DrawFilters();
        
        EditorGUILayout.Space(5);
        
        // Model list
        DrawModelList();
        
        EditorGUILayout.EndVertical();
    }
    
    private void DrawFilters()
    {
        EditorGUILayout.BeginHorizontal();
        
        // Search filter
        EditorGUILayout.LabelField("Search:", GUILayout.Width(50));
        searchFilter = EditorGUILayout.TextField(searchFilter);
        
        if (GUILayout.Button("Clear", GUILayout.Width(50)))
        {
            searchFilter = "";
        }
        
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.BeginHorizontal();
        
        // Agent filter
        EditorGUILayout.LabelField("Agent:", GUILayout.Width(50));
        List<string> agentOptions = new List<string> { "All" };
        if (modelManager.ModelsByAgent != null)
        {
            agentOptions.AddRange(modelManager.ModelsByAgent.Keys.Select(k => k.ToString()));
        }
        int newAgentFilter = EditorGUILayout.Popup(selectedAgentFilter + 1, agentOptions.ToArray()) - 1;
        if (newAgentFilter != selectedAgentFilter)
        {
            selectedAgentFilter = newAgentFilter;
        }
        
        // Generation filter
        EditorGUILayout.LabelField("Gen:", GUILayout.Width(30));
        List<string> genOptions = new List<string> { "All" };
        if (modelManager.ModelsByGeneration != null && modelManager.ModelsByGeneration.Keys.Count > 0)
        {
            var sortedGens = modelManager.ModelsByGeneration.Keys.OrderByDescending(k => k).Take(20);
            genOptions.AddRange(sortedGens.Select(k => k.ToString()));
        }
        int newGenFilter = EditorGUILayout.Popup(selectedGenerationFilter + 1, genOptions.ToArray()) - 1;
        if (newGenFilter != selectedGenerationFilter)
        {
            selectedGenerationFilter = newGenFilter;
        }
        
        EditorGUILayout.EndHorizontal();
        
        // Options
        EditorGUILayout.BeginHorizontal();
        showModelDetails = EditorGUILayout.Toggle("Show Details", showModelDetails);
        autoRefresh = EditorGUILayout.Toggle("Auto Refresh", autoRefresh);
        EditorGUILayout.EndHorizontal();
    }
    
    private void DrawModelList()
    {
        if (modelManager.AvailableModels == null || modelManager.AvailableModels.Count == 0)
        {
            EditorGUILayout.HelpBox("No models found. Click 'Refresh Models' to scan for available models.", MessageType.Info);
            return;
        }
        
        // Filter models
        var filteredModels = FilterModels();
        
        if (filteredModels.Count == 0)
        {
            EditorGUILayout.HelpBox($"No models match the current filters. Found {modelManager.AvailableModels.Count} total models.", MessageType.Info);
            return;
        }
        
        EditorGUILayout.LabelField($"📁 Models ({filteredModels.Count} of {modelManager.AvailableModels.Count})", EditorStyles.boldLabel);
        
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(200));
        
        foreach (var model in filteredModels)
        {
            DrawModelItem(model);
        }
        
        EditorGUILayout.EndScrollView();
    }
    
    private List<ModelManager.ModelInfo> FilterModels()
    {
        var models = modelManager.AvailableModels.AsEnumerable();
        
        // Apply search filter
        if (!string.IsNullOrEmpty(searchFilter))
        {
            models = models.Where(m => m.fileName.ToLower().Contains(searchFilter.ToLower()));
        }
        
        // Apply agent filter
        if (selectedAgentFilter >= 0 && modelManager.ModelsByAgent != null)
        {
            var agentKeys = modelManager.ModelsByAgent.Keys.ToList();
            if (selectedAgentFilter < agentKeys.Count)
            {
                int targetAgent = agentKeys[selectedAgentFilter];
                models = models.Where(m => m.agentIndex == targetAgent);
            }
        }
        
        // Apply generation filter
        if (selectedGenerationFilter >= 0 && modelManager.ModelsByGeneration != null)
        {
            var genKeys = modelManager.ModelsByGeneration.Keys.OrderByDescending(k => k).Take(20).ToList();
            if (selectedGenerationFilter < genKeys.Count)
            {
                int targetGen = genKeys[selectedGenerationFilter];
                models = models.Where(m => m.generation == targetGen);
            }
        }
        
        return models.ToList();
    }
    
    private void DrawModelItem(ModelManager.ModelInfo model)
    {
        EditorGUILayout.BeginVertical(GUI.skin.box);
        
        EditorGUILayout.BeginHorizontal();
        
        // Model info
        string modelDisplayName = $"Agent {model.agentIndex} - Gen {model.generation}";
        EditorGUILayout.LabelField(modelDisplayName, EditorStyles.boldLabel, GUILayout.Width(120));
        
        // File size
        string sizeText = FormatFileSize(model.fileSize);
        EditorGUILayout.LabelField(sizeText, GUILayout.Width(60));
        
        // Date
        EditorGUILayout.LabelField(model.lastModified.ToString("MM/dd HH:mm"), GUILayout.Width(80));
        
        GUILayout.FlexibleSpace();
        
        // Load button
        if (GUILayout.Button("Load into Agent", GUILayout.Width(100)))
        {
            LoadModelIntoAgent(model);
        }
        
        EditorGUILayout.EndHorizontal();
        
        // Show details if enabled
        if (showModelDetails)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("File:", GUILayout.Width(35));
            EditorGUILayout.SelectableLabel(model.fileName, EditorStyles.miniLabel, GUILayout.Height(16));
            EditorGUILayout.EndHorizontal();
        }
        
        EditorGUILayout.EndVertical();
    }
    
    private void LoadModelIntoAgent(ModelManager.ModelInfo model)
    {
        // Find agents in scene
        Agent[] agents = FindObjectsByType<Agent>(FindObjectsSortMode.None);
        
        if (agents.Length == 0)
        {
            EditorUtility.DisplayDialog("No Agents Found", "No Agent components found in the scene.", "OK");
            return;
        }
        
        if (agents.Length == 1)
        {
            // Only one agent, load directly
            modelManager.LoadModelIntoAgent(model, agents[0]);
            return;
        }
        
        // Multiple agents, show selection dialog
        string[] agentNames = agents.Select((agent, index) => $"{index}: {agent.name}").ToArray();
        
        int selectedAgent = EditorUtility.DisplayDialogComplex(
            "Select Agent",
            $"Load model '{model.fileName}' into which agent?",
            "Agent 0",
            "Cancel",
            agents.Length > 1 ? "Agent 1" : "Cancel"
        );
        
        if (selectedAgent == 0 && agents.Length > 0)
        {
            modelManager.LoadModelIntoAgent(model, agents[0]);
        }
        else if (selectedAgent == 2 && agents.Length > 1)
        {
            modelManager.LoadModelIntoAgent(model, agents[1]);
        }
        // selectedAgent == 1 means cancel
    }
    
    private string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024} KB";
        return $"{bytes / (1024 * 1024)} MB";
    }
}