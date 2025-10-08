using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.IO;

public class GeneticAlgorithmManager : MonoBehaviour
{
    [Header("Genetic Algorithm Settings")]
    public bool enableGeneticAlgorithm = true;
    public float generationDuration = 10f;
    public float successThreshold = 0.1f;
    public int populationSize = 8;
    public float mutationRate = 0.1f;
    public float elitePercentage = 0.2f;
    
    [Header("Auto Save Settings")]
    public bool enableAutoSave = true;
    public float autoSaveInterval = 3600f;
    public string saveDirectory = "SavedModels";
    
    [Header("References")]
    public Envirovment environment;
    public Agent[] agents;
    
    private float generationTimer = 0f;
    private float autoSaveTimer = 0f;
    private int currentGeneration = 0;
    private List<AgentFitness> agentFitnessScores;
    private bool isEvolving = false;
    
    [System.Serializable]
    public class AgentFitness
    {
        public int agentIndex;
        public float fitness;
        public float totalReward;
        public int kills;
        public float survivalTime;
        public bool isElite;
        
        public AgentFitness(int index)
        {
            agentIndex = index;
            fitness = 0f;
            totalReward = 0f;
            kills = 0;
            survivalTime = 0f;
            isElite = false;
        }
    }
    
    void Start()
    {
        if (environment == null)
            environment = FindFirstObjectByType<Envirovment>();
            
        if (agents == null || agents.Length == 0)
        {
            agents = FindObjectsByType<Agent>(FindObjectsSortMode.None);
        }
        
        if (!Directory.Exists(saveDirectory))
        {
            Directory.CreateDirectory(saveDirectory);
        }
        
        InitializeFitnessTracking();
        
        Debug.Log($"Genetic Algorithm Manager initialized - Population: {agents.Length}");
    }
    
    void Update()
    {
        if (!enableGeneticAlgorithm && !enableAutoSave) return;
        
        generationTimer += Time.deltaTime;
        autoSaveTimer += Time.deltaTime;
        
        // Check for timeout-based evolution trigger
        bool timeoutTriggered = false;
        if (environment != null && environment.timeSinceLastReward >= environment.noRewardTimeoutDuration)
        {
            timeoutTriggered = true;
        }
        
        if (enableGeneticAlgorithm && !isEvolving && (generationTimer >= generationDuration || timeoutTriggered))
        {
            if (timeoutTriggered)
            {
                Debug.Log("=== TIMEOUT TRIGGERED EVOLUTION ===");
                Debug.Log($"No meaningful rewards for {environment.timeSinceLastReward:F1}s, forcing generation evolution!");
            }
            EvaluateGeneration();
        }
        
        if (enableAutoSave && autoSaveTimer >= autoSaveInterval)
        {
            AutoSaveAllModels();
            autoSaveTimer = 0f;
        }
    }
    
    private void InitializeFitnessTracking()
    {
        if (agents == null || agents.Length == 0)
        {
            Debug.LogError("Cannot initialize fitness tracking - no agents found!");
            return;
        }
        
        agentFitnessScores = new List<AgentFitness>();
        
        for (int i = 0; i < agents.Length; i++)
        {
            agentFitnessScores.Add(new AgentFitness(i));
        }
        
        Debug.Log($"Fitness tracking initialized for {agents.Length} agents");
    }
    
    private void EvaluateGeneration()
    {
        isEvolving = true;
        
        Debug.Log($"=== GENERATION {currentGeneration} EVALUATION (Duration: {generationTimer:F1}s) ===");
        
        // Calculate fitness for each agent
        CalculateFitnessScores();
        
        // Calculate average fitness
        float averageFitness = agentFitnessScores.Average(a => a.fitness);
        
        Debug.Log($"Generation {currentGeneration} completed - Average Fitness: {averageFitness:F3}");
        
        if (averageFitness < successThreshold)
        {
            Debug.Log($"Poor performance detected (avg: {averageFitness:F3} < threshold: {successThreshold}). Evolving generation...");
            EvolvePopulation();
        }
        else
        {
            Debug.Log($"Generation performing well (avg: {averageFitness:F3} >= threshold: {successThreshold}). Continuing without evolution.");
        }
        
        // Reset for next generation
        ResetGenerationTracking();
        
        // Reset the full environment state (positions, deaths, rewards, timeout)
        if (environment != null)
        {
            Debug.Log("🔄 RESETTING WORLD STATE: Reviving dead agents and resetting positions");
            environment.Reset(); // This calls the full reset including positions and agent revival
        }
        
        currentGeneration++;
        generationTimer = 0f;
        isEvolving = false;
    }
    
    private void CalculateFitnessScores()
    {
        Debug.Log("=== Calculating Fitness Scores ===");
        
        for (int i = 0; i < agentFitnessScores.Count; i++)
        {
            if (i < agents.Length && environment != null)
            {
                AgentFitness fitness = agentFitnessScores[i];
                
                // Get cumulative reward for this agent
                fitness.totalReward = environment.GetReward(i);
                
                // Get kill count
                fitness.kills = environment.GetKillCount(i);
                
                // Update survival time
                fitness.survivalTime = environment.IsAgentAlive(i) ? generationTimer : fitness.survivalTime;
                
                // Calculate composite fitness
                fitness.fitness = CalculateCompositeFitness(fitness);
                
                Debug.Log($"Agent_{i}: Reward={fitness.totalReward:F1}, Kills={fitness.kills}, Survival={fitness.survivalTime:F1}s, Fitness={fitness.fitness:F3}, Alive={environment.IsAgentAlive(i)}");
            }
        }
        
        // Sort by fitness (best first)
        agentFitnessScores = agentFitnessScores.OrderByDescending(a => a.fitness).ToList();
        
        Debug.Log($"Best Agent: {agentFitnessScores[0].agentIndex} with fitness {agentFitnessScores[0].fitness:F3}");
        Debug.Log($"Worst Agent: {agentFitnessScores[agentFitnessScores.Count-1].agentIndex} with fitness {agentFitnessScores[agentFitnessScores.Count-1].fitness:F3}");
    }
    
    private float CalculateCompositeFitness(AgentFitness fitness)
    {
        float rewardWeight = 0.8f;  // Even higher weight for rewards since kills are now 200 points
        float survivalWeight = 0.2f;
        
        // Normalize rewards (updated for new reward scale: kills are +200, spotting +5)
        // New range: roughly -200 to +600 (multiple kills possible at 200 each)
        float normalizedReward = fitness.totalReward / 400f; // Updated normalization factor for 200 point kills
        
        // Give slight penalty for excessive timeout punishment
        if (fitness.totalReward < -100f) // Likely accumulated timeout punishments
        {
            normalizedReward = Mathf.Max(normalizedReward, -0.8f); // Cap penalty
            Debug.Log($"Agent_{fitness.agentIndex} has excessive timeout punishment, capping fitness penalty");
        }
        
        // Normalize survival time
        float normalizedSurvival = Mathf.Clamp01(fitness.survivalTime / generationDuration);
        
        float compositeFitness = (normalizedReward * rewardWeight) + (normalizedSurvival * survivalWeight);
        
        // Ensure minimum fitness is not too negative
        return Mathf.Max(compositeFitness, -1f);
    }
    
    private void EvolvePopulation()
    {
        int eliteCount = Mathf.RoundToInt(agents.Length * elitePercentage);
        for (int i = 0; i < eliteCount; i++)
        {
            agentFitnessScores[i].isElite = true;
        }
        
        Debug.Log($"Evolving population - Elite agents: {eliteCount}");
        
        for (int i = 0; i < eliteCount; i++)
        {
            int agentIndex = agentFitnessScores[i].agentIndex;
            string elitePath = Path.Combine(saveDirectory, $"Elite_Agent_{agentIndex}_Gen_{currentGeneration}.json");
            agents[agentIndex].SaveModel(elitePath);
        }
        
        for (int i = eliteCount; i < agents.Length; i++)
        {
            int agentIndex = agentFitnessScores[i].agentIndex;
            int parentIndex = agentFitnessScores[Random.Range(0, eliteCount)].agentIndex;
            
            CloneAndMutateAgent(parentIndex, agentIndex);
        }
        
        Debug.Log($"Generation {currentGeneration} evolution completed!");
    }
    
    private void CloneAndMutateAgent(int parentIndex, int childIndex)
    {
        if (parentIndex >= 0 && parentIndex < agents.Length && 
            childIndex >= 0 && childIndex < agents.Length)
        {
            if (agents[parentIndex].policyNetwork != null && agents[childIndex].policyNetwork != null)
            {
                agents[childIndex].policyNetwork = new SimpleNeuralNetwork();
                agents[childIndex].policyNetwork.Initialize();
                
                Debug.Log($"Agent_{childIndex} evolved from Agent_{parentIndex}");
            }
        }
    }
    
    private void ResetGenerationTracking()
    {
        for (int i = 0; i < agentFitnessScores.Count; i++)
        {
            agentFitnessScores[i].fitness = 0f;
            agentFitnessScores[i].totalReward = 0f;
            agentFitnessScores[i].kills = 0;
            agentFitnessScores[i].survivalTime = 0f;
            agentFitnessScores[i].isElite = false;
        }
    }
    
    private void AutoSaveAllModels()
    {
        string timestamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string autoSaveFolder = Path.Combine(saveDirectory, $"AutoSave_{timestamp}");
        
        if (!Directory.Exists(autoSaveFolder))
        {
            Directory.CreateDirectory(autoSaveFolder);
        }
        
        int savedCount = 0;
        for (int i = 0; i < agents.Length; i++)
        {
            try
            {
                string savePath = Path.Combine(autoSaveFolder, $"Agent_{i}.json");
                agents[i].SaveModel(savePath);
                savedCount++;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to auto-save Agent_{i}: {e.Message}");
            }
        }
        
        Debug.Log($"Auto-saved {savedCount}/{agents.Length} agent models to {autoSaveFolder}");
        
        SaveGenerationStats(autoSaveFolder);
    }
    
    private void SaveGenerationStats(string folder)
    {
        try
        {
            string statsPath = Path.Combine(folder, "generation_stats.txt");
            List<string> stats = new List<string>
            {
                $"Generation: {currentGeneration}",
                $"Timestamp: {System.DateTime.Now}",
                $"Population Size: {agents.Length}",
                $"Generation Duration: {generationDuration}s",
                "",
                "Agent Fitness Scores:"
            };
            
            if (agentFitnessScores != null)
            {
                foreach (var fitness in agentFitnessScores)
                {
                    stats.Add($"Agent_{fitness.agentIndex}: Fitness={fitness.fitness:F3}, Reward={fitness.totalReward:F1}, Elite={fitness.isElite}");
                }
            }
            
            File.WriteAllLines(statsPath, stats);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to save generation stats: {e.Message}");
        }
    }
    
    public void ForceEvolution()
    {
        if (!isEvolving)
        {
            Debug.Log("Forcing evolution...");
            EvaluateGeneration();
        }
    }
    
    public void ForceSave()
    {
        Debug.Log("Forcing auto-save...");
        AutoSaveAllModels();
    }
    
    public float GetAverageFitness()
    {
        if (agentFitnessScores == null || agentFitnessScores.Count == 0) return 0f;
        return agentFitnessScores.Average(a => a.fitness);
    }
    
    public int GetCurrentGeneration()
    {
        return currentGeneration;
    }
    
    public float GetGenerationProgress()
    {
        return generationTimer / generationDuration;
    }
    
    // Debug method to test fitness calculation
    [ContextMenu("Test Fitness Calculation")]
    public void TestFitnessCalculation()
    {
        if (environment == null || agents == null)
        {
            Debug.LogError("Environment or agents not set!");
            return;
        }
        
        Debug.Log("=== MANUAL FITNESS TEST ===");
        for (int i = 0; i < agents.Length; i++)
        {
            float cumulativeReward = environment.GetReward(i);
            bool isAlive = environment.IsAgentAlive(i);
            Debug.Log($"Agent_{i}: Cumulative Reward = {cumulativeReward:F1}, Alive = {isAlive}, Timer = {generationTimer:F1}s");
        }
    }
    
    // Debug method to force generation evaluation
    [ContextMenu("Force Generation Evaluation")]
    public void TestGenerationEvaluation()
    {
        if (!isEvolving)
        {
            Debug.Log("=== FORCED GENERATION EVALUATION ===");
            EvaluateGeneration();
        }
        else
        {
            Debug.Log("Already evaluating generation!");
        }
    }
    
    // Debug method to check timeout status
    [ContextMenu("Check Timeout Status")]
    public void CheckTimeoutStatus()
    {
        if (environment != null)
        {
            Debug.Log($"=== TIMEOUT STATUS ===");
            Debug.Log($"Time since last reward: {environment.timeSinceLastReward:F1}s / {environment.noRewardTimeoutDuration}s");
            Debug.Log($"Has earned reward: {environment.hasEarnedRewardThisEpisode}");
            Debug.Log($"Has been punished: {environment.hasBeenPunishedForTimeout}");
            Debug.Log($"Generation timer: {generationTimer:F1}s / {generationDuration}s");
            
            bool willTimeout = environment.timeSinceLastReward >= environment.noRewardTimeoutDuration;
            Debug.Log($"Will trigger timeout evolution: {willTimeout}");
        }
    }
}
