using UnityEngine;
using System.Collections.Generic;

public class Envirovment : MonoBehaviour, IEnvironment, IRewardCalculator
{
    [Header("Environment Settings")]
    public Transform[] agents; // All agents in the environment
    public LayerMask wallLayerMask = 1;
    public LayerMask agentLayerMask = 1;
    public float maxEpisodeLength = 1000f;
    public float shootRange = 10f;
    public float moveSpeed = 5f;
    public float rotationSpeed = 90f;

    [Header("Spawn Points")]
    public Transform[] spawnPoints; // Spawn points for all agents
    
    [Header("Dynamic Spawn Settings")]
    public bool useDynamicSpawning = true;
    public Vector2 spawnRangeX = new Vector2(-10f, 10f); // X range for random spawns
    public Vector2 spawnRangeY = new Vector2(-10f, 10f); // Y range for random spawns
    public float minSpawnDistance = 2f; // Minimum distance between spawn points
    public bool generateSpawnPointsOnStart = true; // Auto-generate spawn points
    public bool visualizeSpawnRange = true; // Show spawn range in editor

    [Header("Timeout Settings")]
    public float noRewardTimeoutDuration = 5f; // Punish after 5 seconds without reward
    public bool enableTimeout = true;
    public float timeoutPunishment = -30f; // Punishment for timeout
    public bool resetOnTimeout = false; // If true, reset episode; if false, just punish

    // Private variables
    public float currentEpisodeTime; // Made public for timeout checking
    private float lastReward;
    private bool episodeFinished;
    private State currentState;
    private Action lastAction;
    
    // Multi-agent tracking arrays
    private bool[] agentHitWall;
    private bool[] agentShotEnemy;
    private bool[] agentShotNothing;
    private bool[] agentSpottedEnemy;
    private bool[] agentAlreadySpottedThisStep;
    private bool[] agentHitByEnemy; // Track if agent was hit/killed by another agent
    private float[] agentDistanceToNearestEnemy; // Track distance to nearest spotted enemy
    private float[] agentPreviousDistanceToEnemy; // Previous distance for proximity rewards
    private int[] agentKills; // Track kills for each agent
    private bool[] agentIsAlive; // Track if agent is alive
    private float[] agentCumulativeRewards; // Track total rewards earned by each agent
    private float[] agentLastStepRewards; // Track last step reward for each agent
    private bool[] agentHadMeaningfulMovement; // Track if agent had meaningful movement this step
    
    // Enhanced spotting tracking
    private bool[] agentCurrentlySpottingEnemy; // Track if agent is currently seeing an enemy (this frame)
    private bool[] agentWasSpottingEnemyLastFrame; // Track if agent was seeing an enemy last frame
    private bool[] agentJustEnteredSpotting; // Track if agent just started spotting (raycast enter)
    private bool[] agentFacingTowardEnemy; // Track if agent is facing toward spotted enemy
    private bool[] agentAlreadyGotFacingBonus; // Track if agent already got facing bonus this spotting session
    private bool[] agentSpottedByEnemy; // Track if agent is currently spotted by another agent
    
    // Timeout tracking (public for TrainingManager access)
    public float timeSinceLastReward;
    public bool hasEarnedRewardThisEpisode;
    public bool hasBeenPunishedForTimeout;
    private float lastTimeoutPunishmentTime;

    // Ray casting setup
    private const int NUM_RAYS = 32;
    private const float RAY_DISTANCE = 25f;
    private const float SPOTTING_RANGE = RAY_DISTANCE; // Spotting range matches raycast range
    private const float RAY_ANGLE_RANGE = 180f;

    public float CalculateReward(int agentIndex = 0)
    {
        if (agentIndex < 0 || agentIndex >= agents.Length) return 0f;
        
        float reward = 0f;
        string rewardReason = "";
        string agentName = $"Agent_{agentIndex}";

        // NORMALIZED REWARD SYSTEM DESIGN (all rewards in [-1, +1] range):
        // - POSITIVE rewards: Reset timeout timer (meaningful progress)
        // - NEGATIVE rewards: Do NOT reset timeout timer (penalties) 
        // - All rewards normalized for stable learning
        
        // Check for timeout punishment first (normalized)
        float normalizedTimeoutPunishment = -0.3f;
        if (enableTimeout && !resetOnTimeout && timeSinceLastReward >= noRewardTimeoutDuration && 
            !hasEarnedRewardThisEpisode && !hasBeenPunishedForTimeout)
        {
            reward += normalizedTimeoutPunishment;
            rewardReason += $"Timeout punishment ({normalizedTimeoutPunishment:F2}) ";
            hasBeenPunishedForTimeout = true;
            lastTimeoutPunishmentTime = currentEpisodeTime;
            Debug.Log($"{agentName} TIMEOUT PUNISHMENT! Agent idle for {timeSinceLastReward:F1} seconds, punishment: {normalizedTimeoutPunishment:F2}");
        }
        
        // Additional punishment every 5 seconds of continued inactivity
        if (enableTimeout && !resetOnTimeout && hasBeenPunishedForTimeout && 
            (currentEpisodeTime - lastTimeoutPunishmentTime) >= noRewardTimeoutDuration)
        {
            float continuedPunishment = normalizedTimeoutPunishment * 0.5f;
            reward += continuedPunishment;
            rewardReason += $"Continued timeout ({continuedPunishment:F2}) ";
            lastTimeoutPunishmentTime = currentEpisodeTime;
            Debug.Log($"{agentName} CONTINUED TIMEOUT! Additional punishment: {continuedPunishment:F2}");
        }

        // STRICT EVENT-BASED REWARD SYSTEM - NO CONTINUOUS REWARDS ALLOWED
        
        // 1. Raycast enter bonus - ONE-TIME reward when first spotting enemy
        if (agentJustEnteredSpotting[agentIndex])
        {
            reward += 0.05f; // One-time bonus for acquiring target
            rewardReason += "Enemy acquired (+0.05) ";
            Debug.Log($"{agentName} REWARD EVENT: Enemy acquired +0.05");
        }
        
        // 2. Facing bonus - ONE-TIME reward when first facing toward enemy (only once per spotting session)
        if (agentCurrentlySpottingEnemy[agentIndex] && agentFacingTowardEnemy[agentIndex] && !agentAlreadyGotFacingBonus[agentIndex])
        {
            reward += 0.25f; // One-time bonus for proper orientation
            rewardReason += "Facing enemy (+0.25) ";
            agentAlreadyGotFacingBonus[agentIndex] = true; // Mark as received
            Debug.Log($"{agentName} REWARD EVENT: Facing enemy +0.25");
        }
        
        // 3. Spotted by enemy penalty - punishment for being seen
        if (agentSpottedByEnemy[agentIndex])
        {
            reward -= 0.1f; // Penalty for being spotted (encourages stealth)
            rewardReason += "Spotted by enemy (-0.1) ";
            Debug.Log($"{agentName} PENALTY: Spotted by enemy -0.1");
        }
        
        // Proximity rewards removed to prevent continuous accumulation
        // Agents should be rewarded for discrete events, not continuous states
        
        // Shooting rewards/penalties (normalized)
        if (agentShotEnemy[agentIndex])
        {
            reward += 1.0f;  // Maximum positive reward for kills
            rewardReason += "🏆 KILLED ENEMY (+1.0) ";
            Debug.Log($"{agentName} 🏆 MAXIMUM KILL REWARD! +1.0 for elimination!");
        }
        else if (agentShotNothing[agentIndex])
        {
            reward -= 0.005f;  // Small penalty for missing shots
            rewardReason += "Shot nothing (-0.05) ";
        }

        // Wall collision penalty (normalized)
        if (agentHitWall[agentIndex])
        {
            reward -= 0.2f; // Normalized from -20
            rewardReason += "Hit wall (-0.2) ";
            Debug.Log($"{agentName} hit wall, penalty: -0.2");
        }
        
        // Punishment for being hit/killed by another agent
        if (agentHitByEnemy[agentIndex])
        {
            reward -= 0.8f; // Significant penalty for being eliminated
            rewardReason += "Hit by enemy (-0.8) ";
            Debug.Log($"{agentName} was hit by enemy, major penalty: -0.8");
        }

        // Penalty for doing nothing or being inactive
        bool isDoingNothing = (lastAction == null || 
            (lastAction.shoot < 0.1f && 
             Mathf.Abs(lastAction.moveForward) < 0.1f && 
             Mathf.Abs(lastAction.moveLeft) < 0.1f && 
             Mathf.Abs(lastAction.moveRight) < 0.1f && 
             Mathf.Abs(lastAction.lookAngle) < 0.1f));
             
        if (isDoingNothing)
        {
            reward -= 0.05f; // Consistent penalty for inactivity
            rewardReason += "Inactivity (-0.05) ";
        }
    
        // No survival bonus to prevent continuous reward accumulation
        // Agents should be rewarded for actions, not for simply staying alive
        
        // Clamp final reward to [-1, +1] range for stability
        reward = Mathf.Clamp(reward, -1f, 1f);
        
        // Update cumulative rewards tracking
        agentLastStepRewards[agentIndex] = reward;
        agentCumulativeRewards[agentIndex] += reward;
        
        // COMPREHENSIVE REWARD VALIDATION AND LOGGING
        if (reward != 0f)
        {
            Debug.Log($"{agentName} Step Reward: {reward:F3} - {rewardReason} | Total: {agentCumulativeRewards[agentIndex]:F2}");
            
            // STRICT validation - catch ANY unexpected positive rewards
            if (reward > 0f)
            {
                bool isValidPositiveReward = rewardReason.Contains("Enemy acquired") || 
                                           rewardReason.Contains("Facing enemy") || 
                                           rewardReason.Contains("KILLED ENEMY");
                                           
                if (!isValidPositiveReward)
                {
                    Debug.LogError($"{agentName} - INVALID POSITIVE REWARD DETECTED: {reward:F3} - {rewardReason}");
                    Debug.LogError($"This reward should not exist! Check the reward calculation logic.");
                }
            }
            
            // Log specific reward breakdown for debugging
            if (reward > 0.4f) // High rewards that might be combinations
            {
                Debug.LogWarning($"{agentName} - HIGH REWARD DETECTED: {reward:F3} - Please verify this is correct: {rewardReason}");
            }
        }

        return reward;
    }

    public int GetActionSize()
    {
        // Actions: lookAngle(1) + shoot(1) + moveForward(1) + moveLeft(1) + moveRight(1) = 5
        return 5;
    }

    public float GetReward()
    {
        return lastReward;
    }

    public int GetStateSize()
    {
        // State: 32 rays + agent position(x,y) + agent rotation(1) = 35 (removed enemy position)
        return NUM_RAYS + 3;
    }

    public bool IsEpisodeFinished()
    {
        // Check for normal episode end conditions
        if (episodeFinished || currentEpisodeTime >= maxEpisodeLength)
        {
            return true;
        }
        
        // Check if only one agent remains alive (or none)
        int aliveCount = 0;
        for (int i = 0; i < agentIsAlive.Length; i++)
        {
            if (agentIsAlive[i]) aliveCount++;
        }
        
        if (aliveCount <= 1)
        {
            Debug.Log($"Episode finished - Only {aliveCount} agent(s) remaining (Last agent wins or all dead)");
            return true;
        }
        
        // Check for timeout - only reset episode if resetOnTimeout is true
        if (enableTimeout && resetOnTimeout && timeSinceLastReward >= noRewardTimeoutDuration && !hasEarnedRewardThisEpisode)
        {
            Debug.Log($"Episode timeout reset! {noRewardTimeoutDuration} seconds passed without earning reward.");
            return true;
        }
        
        return false;
    }

    // Method for GeneticAlgorithmManager compatibility
    public bool GetEpisodeEnded()
    {
        return IsEpisodeFinished();
    }

    // Method for GeneticAlgorithmManager compatibility
    public void ResetEnvironment()
    {
        Reset();
    }

    public void Reset()
    {
        currentEpisodeTime = 0f;
        lastReward = 0f;
        episodeFinished = false;
        
        if (agents == null) return;
        
        // Reset all agent states
        for (int i = 0; i < agents.Length; i++)
        {
            agentHitWall[i] = false;
            agentShotEnemy[i] = false;
            agentShotNothing[i] = false;
            agentSpottedEnemy[i] = false;
            agentAlreadySpottedThisStep[i] = false;
            agentHitByEnemy[i] = false;
            agentDistanceToNearestEnemy[i] = 0f;
            agentPreviousDistanceToEnemy[i] = 0f;
            agentKills[i] = 0;
            agentIsAlive[i] = true; // All agents start alive
            agentCumulativeRewards[i] = 0f; // Reset cumulative rewards
            agentLastStepRewards[i] = 0f; // Reset last step rewards
            agentHadMeaningfulMovement[i] = false; // Reset movement tracking
            
            // Reset enhanced spotting tracking
            agentCurrentlySpottingEnemy[i] = false;
            agentWasSpottingEnemyLastFrame[i] = false;
            agentJustEnteredSpotting[i] = false;
            agentFacingTowardEnemy[i] = false;
            agentAlreadyGotFacingBonus[i] = false;
            agentSpottedByEnemy[i] = false;
            
            // Revive agent (make visible and enable collider)
            ReviveAgent(i);
        }
        
        // Reset timeout tracking
        timeSinceLastReward = 0f;
        hasEarnedRewardThisEpisode = false;
        hasBeenPunishedForTimeout = false;
        lastTimeoutPunishmentTime = 0f;

        // Reset agent positions using dynamic spawning system
        if (useDynamicSpawning)
        {
            SpawnAgentsRandomly();
        }
        else if (spawnPoints != null && spawnPoints.Length > 0)
        {
            SpawnAgentsAtSpawnPoints();
        }
        else
        {
            Debug.LogWarning("No spawn system configured! Agents will spawn at their current positions.");
            GenerateSpawnPoints(agents.Length);
            SpawnAgentsRandomly();
        }

        UpdateState();
    }
    
    // Dynamic spawn point generation
    private void GenerateSpawnPoints(int agentCount)
    {
        Debug.Log($"🎯 Generating {agentCount} spawn points in range X:[{spawnRangeX.x}, {spawnRangeX.y}] Y:[{spawnRangeY.x}, {spawnRangeY.y}]");
        
        List<Vector3> spawnPositions = new List<Vector3>();
        int maxAttempts = agentCount * 20; // Prevent infinite loops
        int attempts = 0;
        
        // Generate spawn positions with minimum distance enforcement and obstacle avoidance
        while (spawnPositions.Count < agentCount && attempts < maxAttempts)
        {
            attempts++;
            
            Vector3 candidatePosition = new Vector3(
                Random.Range(spawnRangeX.x, spawnRangeX.y),
                Random.Range(spawnRangeY.x, spawnRangeY.y),
                0f
            );
            
            // Check for obstacles at spawn position
            bool hasObstacle = Physics2D.OverlapCircle(candidatePosition, 1f, wallLayerMask);
            if (hasObstacle)
            {
                continue; // Skip this position if there's an obstacle
            }
            
            // Check minimum distance from existing spawn points
            bool validPosition = true;
            foreach (Vector3 existingPos in spawnPositions)
            {
                if (Vector3.Distance(candidatePosition, existingPos) < minSpawnDistance)
                {
                    validPosition = false;
                    break;
                }
            }
            
            if (validPosition)
            {
                spawnPositions.Add(candidatePosition);
                Debug.Log($"✅ Valid spawn point {spawnPositions.Count} at {candidatePosition} (attempt {attempts})");
            }
        }
        
        // If we couldn't generate enough positions, try fallback positions with obstacle checking
        while (spawnPositions.Count < agentCount)
        {
            Vector3 fallbackPosition = new Vector3(
                Random.Range(spawnRangeX.x, spawnRangeX.y),
                Random.Range(spawnRangeY.x, spawnRangeY.y),
                0f
            );
            
            // Even for fallback, try to avoid obstacles
            bool hasObstacle = Physics2D.OverlapCircle(fallbackPosition, 1f, wallLayerMask);
            if (!hasObstacle)
            {
                spawnPositions.Add(fallbackPosition);
                Debug.Log($"🆘 Fallback spawn point {spawnPositions.Count} at {fallbackPosition}");
            }
            else
            {
                // If we really can't find any valid positions, use it anyway but warn
                if (spawnPositions.Count < agentCount && attempts > maxAttempts * 2)
                {
                    spawnPositions.Add(fallbackPosition);
                    Debug.LogWarning($"⚠️ Forced spawn point {spawnPositions.Count} at {fallbackPosition} (obstacle detected!)");
                }
            }
            attempts++;
            
            // Prevent infinite loop in extreme cases
            if (attempts > maxAttempts * 3)
            {
                Debug.LogError("❌ Could not generate enough spawn points without obstacles. Map may be too crowded.");
                break;
            }
        }
        
        // Create spawn point GameObjects
        CreateSpawnPointObjects(spawnPositions);
        
        Debug.Log($"✅ Generated {spawnPositions.Count} spawn points (attempts: {attempts})");
    }
    
    private void CreateSpawnPointObjects(List<Vector3> positions)
    {
        // Clean up existing spawn points if they were auto-generated
        if (spawnPoints != null)
        {
            foreach (Transform spawnPoint in spawnPoints)
            {
                if (spawnPoint != null && spawnPoint.name.StartsWith("AutoSpawn_"))
                {
                    DestroyImmediate(spawnPoint.gameObject);
                }
            }
        }
        
        // Create new spawn point array
        spawnPoints = new Transform[positions.Count];
        
        for (int i = 0; i < positions.Count; i++)
        {
            GameObject spawnObject = new GameObject($"AutoSpawn_{i}");
            spawnObject.transform.position = positions[i];
            spawnObject.transform.rotation = Quaternion.Euler(0, 0, Random.Range(0f, 360f));
            spawnObject.transform.SetParent(this.transform);
            
            // Add visual indicator in editor
            if (visualizeSpawnRange)
            {
                spawnObject.AddComponent<SphereCollider>().isTrigger = true;
                spawnObject.GetComponent<SphereCollider>().radius = 0.5f;
            }
            
            spawnPoints[i] = spawnObject.transform;
        }
    }
    
    private void SpawnAgentsRandomly()
    {
        Debug.Log("🎲 Spawning agents at randomized positions with obstacle avoidance");
        
        for (int i = 0; i < agents.Length; i++)
        {
            Vector3 randomPosition = new Vector3(
                Random.Range(spawnRangeX.x, spawnRangeX.y),
                Random.Range(spawnRangeY.x, spawnRangeY.y),
                agents[i].position.z // Preserve Z coordinate
            );
            
            // Ensure minimum distance from other agents and avoid obstacles
            int attempts = 0;
            while (attempts < 20) // Increased max attempts for obstacle avoidance
            {
                bool tooClose = false;
                bool hasObstacle = false;
                
                // Check for obstacles
                hasObstacle = Physics2D.OverlapCircle(randomPosition, 1f, wallLayerMask);
                
                // Check distance from other agents
                for (int j = 0; j < i; j++)
                {
                    if (Vector3.Distance(randomPosition, agents[j].position) < minSpawnDistance)
                    {
                        tooClose = true;
                        break;
                    }
                }
                
                if (!tooClose && !hasObstacle) break;
                
                randomPosition = new Vector3(
                    Random.Range(spawnRangeX.x, spawnRangeX.y),
                    Random.Range(spawnRangeY.x, spawnRangeY.y),
                    agents[i].position.z
                );
                attempts++;
            }
            
            agents[i].position = randomPosition;
            agents[i].rotation = Quaternion.Euler(0, 0, Random.Range(0f, 360f));
            
            // Log result
            bool finalObstacleCheck = Physics2D.OverlapCircle(randomPosition, 1f, wallLayerMask);
            if (finalObstacleCheck)
            {
                Debug.LogWarning($"⚠️ Agent_{i} spawned at {randomPosition} with obstacle detected (attempts: {attempts})");
            }
            else
            {
                Debug.Log($"✅ Agent_{i} spawned at {randomPosition} with rotation {agents[i].rotation.eulerAngles.z:F0}° (attempts: {attempts})");
            }
        }
    }
    
    private void SpawnAgentsAtSpawnPoints()
    {
        Debug.Log("📍 Spawning agents at predefined spawn points");
        
        // Shuffle spawn points to ensure fair spawning
        System.Collections.Generic.List<int> availableSpawns = new System.Collections.Generic.List<int>();
        for (int i = 0; i < spawnPoints.Length; i++)
        {
            availableSpawns.Add(i);
        }
        
        for (int i = 0; i < agents.Length; i++)
        {
            if (availableSpawns.Count > 0)
            {
                int randomIndex = Random.Range(0, availableSpawns.Count);
                int spawnIndex = availableSpawns[randomIndex];
                availableSpawns.RemoveAt(randomIndex);
                
                agents[i].position = spawnPoints[spawnIndex].position;
                agents[i].rotation = spawnPoints[spawnIndex].rotation;
                
                Debug.Log($"Agent_{i} spawned at spawn point {spawnIndex}: {agents[i].position}");
            }
            else
            {
                // Fallback to random position if not enough spawn points
                Debug.LogWarning($"Not enough spawn points for Agent_{i}, using random position");
                Vector3 randomPos = new Vector3(
                    Random.Range(spawnRangeX.x, spawnRangeX.y),
                    Random.Range(spawnRangeY.x, spawnRangeY.y),
                    agents[i].position.z
                );
                agents[i].position = randomPos;
                agents[i].rotation = Quaternion.Euler(0, 0, Random.Range(0f, 360f));
            }
        }
    }

    public void Step()
    {
        currentEpisodeTime += Time.fixedDeltaTime;
        timeSinceLastReward += Time.fixedDeltaTime;
        
        // Update distance tracking before resetting flags
        for (int i = 0; i < agents.Length; i++)
        {
            if (agentIsAlive[i])
            {
                // Store previous distance for proximity calculation
                agentPreviousDistanceToEnemy[i] = agentDistanceToNearestEnemy[i];
                
                // Find nearest enemy distance (only update if we can see enemies)
                float nearestDistance = float.MaxValue;
                bool foundNearbyEnemy = false;
                
                for (int j = 0; j < agents.Length; j++)
                {
                    if (j != i && agentIsAlive[j])
                    {
                        float distance = Vector3.Distance(agents[i].position, agents[j].position);
                        if (distance < SPOTTING_RANGE && distance < nearestDistance) // Only track if within spotting range
                        {
                            nearestDistance = distance;
                            foundNearbyEnemy = true;
                        }
                    }
                }
                
                // Only update distance if we found a nearby enemy
                if (foundNearbyEnemy)
                {
                    agentDistanceToNearestEnemy[i] = nearestDistance;
                }
                else
                {
                    // Reset distance tracking if no enemies in range
                    agentDistanceToNearestEnemy[i] = 0f;
                    agentPreviousDistanceToEnemy[i] = 0f;
                }
            }
        }
        
        // Update spotting state tracking before resetting flags
        for (int i = 0; i < agents.Length; i++)
        {
            // Store previous frame spotting state
            agentWasSpottingEnemyLastFrame[i] = agentCurrentlySpottingEnemy[i];
            
            // Reset facing bonus flag if agent stops spotting enemy
            if (!agentCurrentlySpottingEnemy[i] && agentWasSpottingEnemyLastFrame[i])
            {
                agentAlreadyGotFacingBonus[i] = false; // Reset for next spotting session
                Debug.Log($"Agent_{i} stopped spotting - reset facing bonus flag");
            }
            
            // Reset current frame flags
            agentCurrentlySpottingEnemy[i] = false;
            agentJustEnteredSpotting[i] = false;
            agentFacingTowardEnemy[i] = false;
            agentSpottedByEnemy[i] = false; // Reset spotted flag each frame
        }
        
        // Reset flags for all agents
        for (int i = 0; i < agents.Length; i++)
        {
            agentHitWall[i] = false;
            agentShotEnemy[i] = false;
            agentShotNothing[i] = false;
            agentSpottedEnemy[i] = false;
            agentAlreadySpottedThisStep[i] = false;
            agentHitByEnemy[i] = false;
            agentHadMeaningfulMovement[i] = false; // Reset movement tracking
        }

        // Apply actions would be called by the training system
        // This method updates the environment state after actions are executed
        
        UpdateState();
        
        // Calculate rewards for all agents and update cumulative tracking
        bool anyMeaningfulReward = false;
        for (int i = 0; i < agents.Length; i++)
        {
            float agentReward = CalculateReward(i);
            
            // Check if any agent earned meaningful POSITIVE reward to reset timeout
            // ONLY positive rewards count as meaningful - penalties should NOT reset timeout
            bool isTimeoutPunishment = (Mathf.Abs(agentReward - (-0.3f)) < 0.01f || Mathf.Abs(agentReward - (-0.15f)) < 0.01f);
            
            if (agentReward > 0.02f && !isTimeoutPunishment) // Meaningful positive reward threshold
            {
                anyMeaningfulReward = true;
            }
        }
        
        // Update timeout tracking based on any agent earning meaningful POSITIVE reward
        if (anyMeaningfulReward)
        {
            timeSinceLastReward = 0f; // Reset timeout timer
            hasEarnedRewardThisEpisode = true;
            hasBeenPunishedForTimeout = false; // Reset punishment flag
        }
        
        // Update lastReward for legacy compatibility (use average or first agent)
        lastReward = agents.Length > 0 ? agentCumulativeRewards[0] : 0f;
    }

    public void ApplyAction(Action action, int agentIndex)
    {
        if (agentIndex < 0 || agentIndex >= agents.Length || agents[agentIndex] == null)
        {
            Debug.LogError($"Invalid agent index {agentIndex} or agent is null!");
            return;
        }
        
        // Dead agents cannot perform actions
        if (!agentIsAlive[agentIndex])
        {
            Debug.Log($"💀 Agent_{agentIndex} is dead and cannot perform actions");
            return;
        }
        
        Transform agent = agents[agentIndex];
        lastAction = action;
        
        // Only log important actions or errors
        bool hasSignificantAction = Mathf.Abs(action.lookAngle) > 0.3f || action.shoot > 0.5f || 
                                   Mathf.Abs(action.moveForward) > 0.3f || Mathf.Abs(action.moveLeft) > 0.3f || Mathf.Abs(action.moveRight) > 0.3f;

        // Apply rotation
        if (Mathf.Abs(action.lookAngle) > 0.1f)
        {
            float rotationAmount = action.lookAngle * rotationSpeed * Time.fixedDeltaTime;
            agent.Rotate(0, 0, rotationAmount);
        }

        // Apply movement with better physics integration
        Vector3 movement = Vector3.zero;
        
        if (Mathf.Abs(action.moveForward) > 0.01f)
        {
            movement += agent.up * action.moveForward * moveSpeed * Time.fixedDeltaTime;
        }
        if (Mathf.Abs(action.moveLeft) > 0.01f)
        {
            movement -= agent.right * action.moveLeft * moveSpeed * Time.fixedDeltaTime;
        }
        if (Mathf.Abs(action.moveRight) > 0.01f)
        {
            movement += agent.right * action.moveRight * moveSpeed * Time.fixedDeltaTime;
        }
        
        // Check if movement is meaningful (threshold for actual position change)
        bool hasMeaningfulMovement = movement.magnitude > 0.05f; // Minimum movement threshold

        // Apply actual movement with physics support
        if (movement != Vector3.zero)
        {
            // Check for wall collision before moving
            RaycastHit2D hit = Physics2D.Raycast(agent.position, movement.normalized, movement.magnitude + 0.5f, wallLayerMask);
            if (hit.collider != null)
            {
                agentHitWall[agentIndex] = true;
                // No meaningful movement if blocked by wall
                agentHadMeaningfulMovement[agentIndex] = false;
            }
            else
            {
                // Try to use Rigidbody2D if available, otherwise move directly
                Rigidbody2D rb = agent.GetComponent<Rigidbody2D>();
                if (rb != null)
                {
                    rb.MovePosition(agent.position + movement);
                }
                else
                {
                    agent.position += movement;
                }
                // Mark as meaningful movement if actually moved
                agentHadMeaningfulMovement[agentIndex] = hasMeaningfulMovement;
            }
        }
        else
        {
            // No movement attempted
            agentHadMeaningfulMovement[agentIndex] = false;
        }

        // Handle shooting
        if (action.shoot > 0.1f)  // Lower threshold for better shooting detection
        {
            PerformShoot(agent, agentIndex);
        }
    }

    private void PerformShoot(Transform shooter, int shooterIndex)
    {
        // Dead agents cannot shoot
        if (!agentIsAlive[shooterIndex])
        {
            return;
        }
        
        // Apply 0.4 unit offset in forward direction for shooting (like ray offset)
        Vector3 shootOrigin = shooter.position + (shooter.up * 0.4f);
        
        // Raycast in the forward direction to check for hits
        RaycastHit2D hit = Physics2D.Raycast(shootOrigin, shooter.up, shootRange, wallLayerMask | agentLayerMask);
        
        // Enhanced visual feedback with proper color coding
        Color shootRayColor = Color.cyan; // Cyan for shooting rays
        if (hit.collider != null)
        {
            // Check if we hit an agent
            bool hitAgent = false;
            for (int i = 0; i < agents.Length; i++)
            {
                if (i != shooterIndex && hit.collider.transform == agents[i])
                {
                    hitAgent = true;
                    break;
                }
            }
            shootRayColor = hitAgent ? Color.magenta : Color.cyan;
        }
        Debug.DrawRay(shootOrigin, shooter.up * shootRange, shootRayColor, 1f);

        if (hit.collider != null)
        {
            // Check if hit any other agent (all agents are enemies of each other)
            bool hitEnemy = false;
            for (int i = 0; i < agents.Length; i++)
            {
                if (i != shooterIndex && hit.collider.transform == agents[i] && agentIsAlive[i])
                {
                    agentShotEnemy[shooterIndex] = true;
                    agentHitByEnemy[i] = true; // Track that this agent was hit
                    agentIsAlive[i] = false; // Target agent dies
                    agentKills[shooterIndex]++;
                    hitEnemy = true;
                    
                    // Visual feedback for dead agent
                    MakeAgentDead(i);
                    
                    Debug.Log($"💀 Agent_{shooterIndex} KILLED Agent_{i}! Agent_{i} is eliminated. (Kill #{agentKills[shooterIndex]})");
                    break;
                }
            }
            
            if (!hitEnemy)
            {
                agentShotNothing[shooterIndex] = true; // Hit wall or other object
            }
        }
        else
        {
            // Shot hit nothing
            agentShotNothing[shooterIndex] = true;
        }
    }
    
    private void MakeAgentDead(int agentIndex)
    {
        if (agentIndex >= 0 && agentIndex < agents.Length && agents[agentIndex] != null)
        {
            // Move dead agent to a "graveyard" position (far away)
            Vector3 graveyardPosition = new Vector3(-1000f, -1000f, 0f);
            agents[agentIndex].position = graveyardPosition;
            
            // Disable the agent's renderer if it exists
            Renderer renderer = agents[agentIndex].GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.enabled = false;
            }
            
            // Disable collider to prevent further interactions
            Collider2D collider = agents[agentIndex].GetComponent<Collider2D>();
            if (collider != null)
            {
                collider.enabled = false;
            }
            
            Debug.Log($"🪦 Agent_{agentIndex} moved to graveyard and made invisible");
        }
    }
    
    private void ReviveAgent(int agentIndex)
    {
        if (agentIndex >= 0 && agentIndex < agents.Length && agents[agentIndex] != null)
        {
            // Re-enable the agent's renderer
            Renderer renderer = agents[agentIndex].GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.enabled = true;
            }
            
            // Re-enable collider
            Collider2D collider = agents[agentIndex].GetComponent<Collider2D>();
            if (collider != null)
            {
                collider.enabled = true;
            }
            
            Debug.Log($"✨ Agent_{agentIndex} revived and made visible");
        }
    }

    private void UpdateState()
    {
        if (currentState == null)
            currentState = new State();

        // Use first agent as primary for state (rays will detect other agents)
        if (agents != null && agents.Length > 0)
        {
            currentState.Position = agents[0];
            currentState.EnemyPosition = null; // No longer needed - rays detect enemies
            
            // Perform raycasting for all agents to detect enemy spotting
            currentState.RaycastHits = PerformRaycasts(agents[0].position, agents[0].up, 0);
            
            // Perform raycasting for other agents (for mutual spotting detection)
            for (int i = 1; i < agents.Length; i++)
            {
                if (agentIsAlive[i])
                {
                    PerformRaycasts(agents[i].position, agents[i].up, i);
                }
            }
        }
    }

    private RaycastHit2D[] PerformRaycasts(Vector3 origin, Vector3 forward, int castingAgentIndex)
    {
        RaycastHit2D[] hits = new RaycastHit2D[NUM_RAYS];
        float angleStep = RAY_ANGLE_RANGE / (NUM_RAYS - 1);
        float startAngle = -RAY_ANGLE_RANGE / 2f;

        // Apply 0.4 unit offset in forward direction for ray detection (consistent with shooting)
        Vector3 rayOrigin = origin + (forward * 0.4f);

        for (int i = 0; i < NUM_RAYS; i++)
        {
            float angle = startAngle + (angleStep * i);
            Vector3 direction = Quaternion.Euler(0, 0, angle) * forward;
            
            hits[i] = Physics2D.Raycast(rayOrigin, direction, RAY_DISTANCE, wallLayerMask | agentLayerMask);
            
            // Check if ray hit any enemy agent - declare once for the whole ray
            bool hitEnemyAgent = false;
            bool hitSelf = false;
            
            if (hits[i].collider != null)
            {
                // Use the passed casting agent index instead of trying to guess from position
                if (castingAgentIndex >= 0 && castingAgentIndex < agents.Length)
                {
                    // First, check if we hit ANY agent (including self)
                    for (int targetIndex = 0; targetIndex < agents.Length; targetIndex++)
                    {
                        if (hits[i].collider.transform == agents[targetIndex] && agentIsAlive[targetIndex])
                        {
                            if (targetIndex == castingAgentIndex)
                            {
                                // Hit self
                                hitSelf = true;
                            }
                            else
                            {
                                // Hit enemy agent within SPOTTING_RANGE
                                hitEnemyAgent = true;
                                
                                // Mark that agent is currently spotting an enemy
                                agentCurrentlySpottingEnemy[castingAgentIndex] = true;
                                
                                // Mark the spotted agent as being seen by enemy
                                agentSpottedByEnemy[targetIndex] = true;
                                
                                // Check if this is first time spotting (raycast enter)
                                if (!agentWasSpottingEnemyLastFrame[castingAgentIndex])
                                {
                                    agentJustEnteredSpotting[castingAgentIndex] = true;
                                    Debug.Log($"Agent_{castingAgentIndex} ENTERED spotting range of Agent_{targetIndex}");
                                }
                                
                                // Check if agent is facing toward the enemy
                                Vector3 directionToEnemy = (agents[targetIndex].position - agents[castingAgentIndex].position).normalized;
                                Vector3 agentForward = agents[castingAgentIndex].up; // Unity 2D uses 'up' as forward
                                float dotProduct = Vector3.Dot(agentForward, directionToEnemy);
                                
                                // Consider "facing toward" if dot product > 0.5 (roughly 60 degree cone)
                                if (dotProduct > 0.5f)
                                {
                                    agentFacingTowardEnemy[castingAgentIndex] = true;
                                }
                                
                                // Legacy spotting flag for backward compatibility
                                if (!agentAlreadySpottedThisStep[castingAgentIndex])
                                {
                                    agentSpottedEnemy[castingAgentIndex] = true;
                                }
                            }
                            break;
                        }
                    }
                    
                    // If didn't hit any agent, it's a wall or other object
                    if (!hitEnemyAgent && !hitSelf)
                    {
                        // Wall hit - no logging needed
                    }
                }
            }
            
            // Visual debug with color coding
            if (hits[i].collider != null)
            {
                Color rayColor = hitEnemyAgent ? Color.blue : Color.green;
                Debug.DrawLine(rayOrigin, hits[i].point, rayColor, 0.2f);
            }
            else
            {
                Debug.DrawRay(rayOrigin, direction * RAY_DISTANCE, Color.red, 0.2f);
            }
        }

        return hits;
    }

    public State GetCurrentState()
    {
        return currentState;
    }

    public float[] GetStateAsArray()
    {
        float[] stateArray = new float[GetStateSize()];
        int index = 0;

        // Add ray distances (normalized)
        if (currentState.RaycastHits != null)
        {
            for (int i = 0; i < NUM_RAYS; i++)
            {
                if (i < currentState.RaycastHits.Length && currentState.RaycastHits[i].collider != null)
                {
                    stateArray[index] = currentState.RaycastHits[i].distance / RAY_DISTANCE;
                }
                else
                {
                    stateArray[index] = 1.0f; // Max distance if no hit
                }
                index++;
            }
        }
        else
        {
            // Fill with max distance if no raycast data
            for (int i = 0; i < NUM_RAYS; i++)
            {
                stateArray[index++] = 1.0f;
            }
        }

        // Add agent position and rotation only (enemies detected through rays)
        if (agents != null && agents.Length > 0)
        {
            stateArray[index++] = agents[0].position.x / 50f; // Assuming map width of 100 units
            stateArray[index++] = agents[0].position.y / 50f; // Assuming map height of 100 units
            stateArray[index++] = agents[0].eulerAngles.z / 360f; // Agent rotation
        }
        else
        {
            // Fill with zeros if no agents
            for (int i = 0; i < 3; i++)
            {
                stateArray[index++] = 0f;
            }
        }

        return stateArray;
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        // Initialize agent arrays
        if (agents == null || agents.Length == 0)
        {
            // Try to find agents automatically
            FindAgentsAutomatically();
        }
        
        int agentCount = agents?.Length ?? 0;
        if (agentCount == 0)
        {
            Debug.LogError("No agents found! Please assign agents in the inspector.");
            return;
        }
        
        // Initialize tracking arrays
        agentHitWall = new bool[agentCount];
        agentShotEnemy = new bool[agentCount];
        agentShotNothing = new bool[agentCount];
        agentSpottedEnemy = new bool[agentCount];
        agentAlreadySpottedThisStep = new bool[agentCount];
        agentHitByEnemy = new bool[agentCount];
        agentDistanceToNearestEnemy = new float[agentCount];
        agentPreviousDistanceToEnemy = new float[agentCount];
        agentKills = new int[agentCount];
        agentIsAlive = new bool[agentCount];
        agentCumulativeRewards = new float[agentCount];
        agentLastStepRewards = new float[agentCount];
        agentHadMeaningfulMovement = new bool[agentCount];
        
        // Initialize enhanced spotting tracking arrays
        agentCurrentlySpottingEnemy = new bool[agentCount];
        agentWasSpottingEnemyLastFrame = new bool[agentCount];
        agentJustEnteredSpotting = new bool[agentCount];
        agentFacingTowardEnemy = new bool[agentCount];
        agentAlreadyGotFacingBonus = new bool[agentCount];
        agentSpottedByEnemy = new bool[agentCount];
        
        // Generate spawn points if needed
        if (generateSpawnPointsOnStart || useDynamicSpawning)
        {
            GenerateSpawnPoints(agentCount);
        }
        
        Debug.Log($"Environment initialized with {agentCount} agents and {(spawnPoints?.Length ?? 0)} spawn points");
        Reset();
    }

    // Update is called once per frame
    void Update()
    {
        // Environment updates are handled in FixedUpdate for consistent physics
    }

    void FixedUpdate()
    {
        // The Step() method should be called by the training system
        // This is just to update state if needed
        if (currentState == null)
        {
            UpdateState();
        }
    }
    
    private void FindAgentsAutomatically()
    {
        // Find all GameObjects with Agent component
        Agent[] foundAgents = FindObjectsByType<Agent>(FindObjectsSortMode.None);
        
        if (foundAgents.Length > 0)
        {
            agents = new Transform[foundAgents.Length];
            for (int i = 0; i < foundAgents.Length; i++)
            {
                agents[i] = foundAgents[i].transform;
            }
            Debug.Log($"Automatically found {agents.Length} agents");
        }
        else
        {
            // Try to find by common names
            System.Collections.Generic.List<Transform> foundTransforms = new System.Collections.Generic.List<Transform>();
            
            string[] commonNames = { "Agent", "Player", "Enemy", "Agent_1", "Agent_2", "Agent_3", "Agent_4" };
            foreach (string name in commonNames)
            {
                GameObject found = GameObject.Find(name);
                if (found != null && found.GetComponent<Agent>() != null)
                {
                    foundTransforms.Add(found.transform);
                }
            }
            
            if (foundTransforms.Count > 0)
            {
                agents = foundTransforms.ToArray();
                Debug.Log($"Found {agents.Length} agents by name search");
            }
        }
    }
    
    // Backwards compatibility method for IRewardCalculator interface
    public float CalculateReward()
    {
        return CalculateReward(0); // Default to first agent
    }
    
    // Method to get reward for specific agent
    public float GetReward(int agentIndex)
    {
        if (agentIndex < 0 || agentIndex >= agentCumulativeRewards.Length) return 0f;
        return agentCumulativeRewards[agentIndex];
    }
    
    // Method to get last step reward for specific agent
    public float GetLastStepReward(int agentIndex)
    {
        if (agentIndex < 0 || agentIndex >= agentLastStepRewards.Length) return 0f;
        return agentLastStepRewards[agentIndex];
    }
    
    // Method to reset cumulative rewards (called when generation resets)
    public void ResetCumulativeRewards()
    {
        for (int i = 0; i < agentCumulativeRewards.Length; i++)
        {
            agentCumulativeRewards[i] = 0f;
            agentLastStepRewards[i] = 0f;
        }
    }
    
    // Method to reset timeout status (called by genetic algorithm)
    public void ResetTimeoutStatus()
    {
        timeSinceLastReward = 0f;
        hasEarnedRewardThisEpisode = false;
        hasBeenPunishedForTimeout = false;
        lastTimeoutPunishmentTime = 0f;
        Debug.Log("Timeout status reset by genetic algorithm");
    }
    
    // Method to get number of agents
    public int GetAgentCount()
    {
        return agents?.Length ?? 0;
    }
    
    // Method to get alive agents count
    public int GetAliveAgentCount()
    {
        if (agentIsAlive == null) return 0;
        
        int count = 0;
        for (int i = 0; i < agentIsAlive.Length; i++)
        {
            if (agentIsAlive[i]) count++;
        }
        return count;
    }
    
    // Method to check if agent is alive
    public bool IsAgentAlive(int agentIndex)
    {
        return agentIndex >= 0 && agentIndex < agentIsAlive.Length && agentIsAlive[agentIndex];
    }
    
    // Method to get kill count for specific agent
    public int GetKillCount(int agentIndex)
    {
        if (agentIndex >= 0 && agentIndex < agentKills.Length)
            return agentKills[agentIndex];
        return 0;
    }

    // Debug method to test spawn system
    [ContextMenu("Regenerate Spawn Points")]
    public void RegenerateSpawnPoints()
    {
        if (agents == null || agents.Length == 0)
        {
            Debug.LogError("No agents found! Cannot generate spawn points.");
            return;
        }
        
        Debug.Log("🔄 Regenerating spawn points...");
        GenerateSpawnPoints(agents.Length);
        SpawnAgentsAtSpawnPoints();
    }
    
    [ContextMenu("Test Random Spawning")]
    public void TestRandomSpawning()
    {
        if (agents == null || agents.Length == 0)
        {
            Debug.LogError("No agents found! Cannot test spawning.");
            return;
        }
        
        Debug.Log("🎲 Testing random spawning...");
        SpawnAgentsRandomly();
    }
    
    [ContextMenu("Test Spawn Point Spawning")]
    public void TestSpawnPointSpawning()
    {
        if (agents == null || agents.Length == 0)
        {
            Debug.LogError("No agents found! Cannot test spawning.");
            return;
        }
        
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.Log("No spawn points found, generating them first...");
            GenerateSpawnPoints(agents.Length);
        }
        
        Debug.Log("📍 Testing spawn point spawning...");
        SpawnAgentsAtSpawnPoints();
    }
    
    [ContextMenu("Show Spawn Info")]
    public void ShowSpawnInfo()
    {
        Debug.Log("📊 SPAWN SYSTEM INFO:");
        Debug.Log($"   Dynamic Spawning: {useDynamicSpawning}");
        Debug.Log($"   Spawn Range X: [{spawnRangeX.x}, {spawnRangeX.y}]");
        Debug.Log($"   Spawn Range Y: [{spawnRangeY.x}, {spawnRangeY.y}]");
        Debug.Log($"   Min Distance: {minSpawnDistance}");
        Debug.Log($"   Auto Generate: {generateSpawnPointsOnStart}");
        Debug.Log($"   Agent Count: {(agents?.Length ?? 0)}");
        Debug.Log($"   Spawn Points: {(spawnPoints?.Length ?? 0)}");
        
        if (agents != null)
        {
            for (int i = 0; i < agents.Length; i++)
            {
                Debug.Log($"   Agent_{i}: {agents[i].position}");
            }
        }
    }
}