using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// Manages loading, browsing, and organizing saved neural network models.
/// Provides comprehensive functionality for model management in the reinforcement learning environment.
/// </summary>
public class ModelManager : MonoBehaviour
{
    [Header("Model Management Settings")]
    [SerializeField] private string savedModelsPath = "SavedModels";
    [SerializeField] private bool autoFindSavedModelsFolder = true;
    [SerializeField] private bool enableModelValidation = true;
    [SerializeField] private bool showDebugLogs = true;

    [Header("Model Selection")]
    [SerializeField] private string selectedModelPath = "";
    [SerializeField] private ModelInfo currentSelectedModel;

    [Header("Auto-Load Settings")]
    [SerializeField] private bool autoLoadBestModel = false;
    [SerializeField] private AutoLoadCriteria autoLoadCriteria = AutoLoadCriteria.LatestGeneration;

    // Private fields
    private List<ModelInfo> availableModels = new List<ModelInfo>();
    private Dictionary<int, List<ModelInfo>> modelsByAgent = new Dictionary<int, List<ModelInfo>>();
    private Dictionary<int, List<ModelInfo>> modelsByGeneration = new Dictionary<int, List<ModelInfo>>();
    
    public enum AutoLoadCriteria
    {
        LatestGeneration,
        HighestGeneration,
        BestPerformance, // Would require additional metadata
        RandomSelection
    }

    [System.Serializable]
    public class ModelInfo
    {
        public string fileName;
        public string fullPath;
        public int agentIndex;
        public int generation;
        public DateTime lastModified;
        public long fileSize;
        public bool isValid;
        public string networkType; // SimpleNN or LSTM
        
        public ModelInfo(string path)
        {
            fullPath = path;
            fileName = Path.GetFileName(path);
            ParseModelInfo();
            
            if (File.Exists(path))
            {
                FileInfo fileInfo = new FileInfo(path);
                lastModified = fileInfo.LastWriteTime;
                fileSize = fileInfo.Length;
            }
        }
        
        private void ParseModelInfo()
        {
            // Parse filename format: Elite_Agent_{agentIndex}_Gen_{generation}.json
            try
            {
                string nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
                string[] parts = nameWithoutExtension.Split('_');
                
                if (parts.Length >= 4 && parts[0] == "Elite" && parts[1] == "Agent")
                {
                    agentIndex = int.Parse(parts[2]);
                    generation = int.Parse(parts[4]);
                    isValid = true;
                }
                else
                {
                    Debug.LogWarning($"⚠️ ModelManager: Unable to parse model info from filename: {fileName}");
                    isValid = false;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"❌ ModelManager: Error parsing model info: {e.Message}");
                isValid = false;
            }
        }
        
        public override string ToString()
        {
            return $"Agent {agentIndex}, Gen {generation} ({fileName})";
        }
    }

    void Start()
    {
        if (autoFindSavedModelsFolder)
        {
            FindSavedModelsFolder();
        }
        
        RefreshModelList();
        
        if (autoLoadBestModel)
        {
            LoadBestModelAutomatically();
        }
    }

    /// <summary>
    /// Automatically find the SavedModels folder relative to the project
    /// </summary>
    private void FindSavedModelsFolder()
    {
        string projectPath = Application.dataPath.Replace("/Assets", "");
        string potentialPath = Path.Combine(projectPath, "SavedModels");
        
        if (Directory.Exists(potentialPath))
        {
            savedModelsPath = potentialPath;
            if (showDebugLogs)
                Debug.Log($"✅ ModelManager: Found SavedModels folder at: {savedModelsPath}");
        }
        else
        {
            Debug.LogWarning($"⚠️ ModelManager: SavedModels folder not found at: {potentialPath}");
        }
    }

    /// <summary>
    /// Refresh the list of available models from the SavedModels directory
    /// </summary>
    [ContextMenu("Refresh Model List")]
    public void RefreshModelList()
    {
        availableModels.Clear();
        modelsByAgent.Clear();
        modelsByGeneration.Clear();
        
        if (!Directory.Exists(savedModelsPath))
        {
            Debug.LogError($"❌ ModelManager: SavedModels directory not found: {savedModelsPath}");
            return;
        }
        
        string[] jsonFiles = Directory.GetFiles(savedModelsPath, "*.json");
        
        foreach (string filePath in jsonFiles)
        {
            ModelInfo modelInfo = new ModelInfo(filePath);
            
            if (modelInfo.isValid)
            {
                availableModels.Add(modelInfo);
                
                // Organize by agent
                if (!modelsByAgent.ContainsKey(modelInfo.agentIndex))
                    modelsByAgent[modelInfo.agentIndex] = new List<ModelInfo>();
                modelsByAgent[modelInfo.agentIndex].Add(modelInfo);
                
                // Organize by generation
                if (!modelsByGeneration.ContainsKey(modelInfo.generation))
                    modelsByGeneration[modelInfo.generation] = new List<ModelInfo>();
                modelsByGeneration[modelInfo.generation].Add(modelInfo);
            }
        }
        
        // Sort models
        availableModels = availableModels.OrderByDescending(m => m.generation)
                                       .ThenBy(m => m.agentIndex)
                                       .ToList();
        
        if (showDebugLogs)
            Debug.Log($"📊 ModelManager: Found {availableModels.Count} valid models from {jsonFiles.Length} files");
    }

    /// <summary>
    /// Load a specific model into an agent
    /// </summary>
    public bool LoadModelIntoAgent(ModelInfo modelInfo, Agent targetAgent)
    {
        if (modelInfo == null || targetAgent == null)
        {
            Debug.LogError("❌ ModelManager: Invalid model info or target agent");
            return false;
        }
        
        if (!File.Exists(modelInfo.fullPath))
        {
            Debug.LogError($"❌ ModelManager: Model file not found: {modelInfo.fullPath}");
            return false;
        }
        
        if (enableModelValidation && !ValidateModelCompatibility(modelInfo, targetAgent))
        {
            Debug.LogError($"❌ ModelManager: Model {modelInfo.fileName} is not compatible with target agent");
            return false;
        }
        
        try
        {
            targetAgent.LoadModel(modelInfo.fullPath);
            
            if (showDebugLogs)
                Debug.Log($"✅ ModelManager: Successfully loaded {modelInfo.fileName} into {targetAgent.name}");
            
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ ModelManager: Failed to load model {modelInfo.fileName}: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Load model by file path
    /// </summary>
    public bool LoadModelByPath(string modelPath, Agent targetAgent)
    {
        ModelInfo modelInfo = availableModels.FirstOrDefault(m => m.fullPath == modelPath);
        
        if (modelInfo == null)
        {
            modelInfo = new ModelInfo(modelPath);
        }
        
        return LoadModelIntoAgent(modelInfo, targetAgent);
    }

    /// <summary>
    /// Get the best model for a specific agent based on generation
    /// </summary>
    public ModelInfo GetBestModelForAgent(int agentIndex)
    {
        if (!modelsByAgent.ContainsKey(agentIndex))
            return null;
        
        return modelsByAgent[agentIndex]
            .OrderByDescending(m => m.generation)
            .FirstOrDefault();
    }

    /// <summary>
    /// Get the latest generation models for all agents
    /// </summary>
    public List<ModelInfo> GetLatestGenerationModels()
    {
        if (availableModels.Count == 0)
            return new List<ModelInfo>();
        
        int latestGeneration = availableModels.Max(m => m.generation);
        return availableModels.Where(m => m.generation == latestGeneration).ToList();
    }

    /// <summary>
    /// Load models into multiple agents
    /// </summary>
    public int LoadModelsIntoAgents(Agent[] agents, AutoLoadCriteria criteria = AutoLoadCriteria.LatestGeneration)
    {
        if (agents == null || agents.Length == 0)
        {
            Debug.LogWarning("⚠️ ModelManager: No agents provided for model loading");
            return 0;
        }
        
        int successCount = 0;
        
        for (int i = 0; i < agents.Length; i++)
        {
            if (agents[i] == null) continue;
            
            ModelInfo modelToLoad = SelectModelForAgent(i, criteria);
            
            if (modelToLoad != null && LoadModelIntoAgent(modelToLoad, agents[i]))
            {
                successCount++;
            }
        }
        
        if (showDebugLogs)
            Debug.Log($"📊 ModelManager: Loaded models into {successCount}/{agents.Length} agents");
        
        return successCount;
    }

    /// <summary>
    /// Select a model for an agent based on criteria
    /// </summary>
    private ModelInfo SelectModelForAgent(int agentIndex, AutoLoadCriteria criteria)
    {
        switch (criteria)
        {
            case AutoLoadCriteria.LatestGeneration:
                return GetBestModelForAgent(agentIndex);
                
            case AutoLoadCriteria.HighestGeneration:
                return GetBestModelForAgent(agentIndex); // Same as latest for now
                
            case AutoLoadCriteria.RandomSelection:
                if (modelsByAgent.ContainsKey(agentIndex) && modelsByAgent[agentIndex].Count > 0)
                {
                    var agentModels = modelsByAgent[agentIndex];
                    return agentModels[UnityEngine.Random.Range(0, agentModels.Count)];
                }
                return null;
                
            case AutoLoadCriteria.BestPerformance:
                // Would need performance metadata - fallback to latest for now
                return GetBestModelForAgent(agentIndex);
                
            default:
                return GetBestModelForAgent(agentIndex);
        }
    }

    /// <summary>
    /// Validate if a model is compatible with an agent
    /// </summary>
    private bool ValidateModelCompatibility(ModelInfo modelInfo, Agent targetAgent)
    {
        // Basic validation - can be extended
        if (!File.Exists(modelInfo.fullPath))
            return false;
        
        if (modelInfo.fileSize < 10) // Very small files are likely corrupt
            return false;
        
        try
        {
            // Try to read the JSON to validate format
            string jsonContent = File.ReadAllText(modelInfo.fullPath);
            
            // Check if it's valid JSON and contains expected neural network data
            if (string.IsNullOrEmpty(jsonContent) || 
                (!jsonContent.Contains("weights") && !jsonContent.Contains("layer")))
            {
                return false;
            }
            
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Automatically load the best model based on criteria
    /// </summary>
    private void LoadBestModelAutomatically()
    {
        Agent[] agents = FindObjectsByType<Agent>(FindObjectsSortMode.None);
        
        if (agents.Length > 0)
        {
            LoadModelsIntoAgents(agents, autoLoadCriteria);
        }
    }

    /// <summary>
    /// Get model statistics
    /// </summary>
    public Dictionary<string, object> GetModelStatistics()
    {
        var stats = new Dictionary<string, object>
        {
            ["TotalModels"] = availableModels.Count,
            ["UniqueAgents"] = modelsByAgent.Keys.Count,
            ["UniqueGenerations"] = modelsByGeneration.Keys.Count,
            ["LatestGeneration"] = availableModels.Count > 0 ? availableModels.Max(m => m.generation) : 0,
            ["OldestGeneration"] = availableModels.Count > 0 ? availableModels.Min(m => m.generation) : 0
        };
        
        return stats;
    }

    // Context menu methods for debugging
    [ContextMenu("Load Best Models Into All Agents")]
    public void LoadBestModelsIntoAllAgents()
    {
        Agent[] agents = FindObjectsByType<Agent>(FindObjectsSortMode.None);
        LoadModelsIntoAgents(agents, AutoLoadCriteria.LatestGeneration);
    }

    [ContextMenu("Print Model Statistics")]
    public void PrintModelStatistics()
    {
        var stats = GetModelStatistics();
        
        Debug.Log("=== MODEL MANAGER STATISTICS ===");
        foreach (var kvp in stats)
        {
            Debug.Log($"{kvp.Key}: {kvp.Value}");
        }
        
        if (modelsByAgent.Count > 0)
        {
            Debug.Log("Models per agent:");
            foreach (var kvp in modelsByAgent)
            {
                Debug.Log($"Agent {kvp.Key}: {kvp.Value.Count} models");
            }
        }
    }

    [ContextMenu("Print Available Models")]
    public void PrintAvailableModels()
    {
        Debug.Log($"=== AVAILABLE MODELS ({availableModels.Count}) ===");
        
        foreach (var model in availableModels.Take(20)) // Show first 20
        {
            Debug.Log($"{model} - {model.lastModified:yyyy-MM-dd HH:mm}");
        }
        
        if (availableModels.Count > 20)
        {
            Debug.Log($"... and {availableModels.Count - 20} more models");
        }
    }

    // Public getters
    public List<ModelInfo> AvailableModels => availableModels;
    public Dictionary<int, List<ModelInfo>> ModelsByAgent => modelsByAgent;
    public Dictionary<int, List<ModelInfo>> ModelsByGeneration => modelsByGeneration;
    public string SavedModelsPath => savedModelsPath;
}