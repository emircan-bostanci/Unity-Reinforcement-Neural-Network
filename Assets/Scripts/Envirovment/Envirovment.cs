using UnityEngine;

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
    private int[] agentKills; // Track kills for each agent
    private bool[] agentIsAlive; // Track if agent is alive
    private float[] agentCumulativeRewards; // Track total rewards earned by each agent
    private float[] agentLastStepRewards; // Track last step reward for each agent
    
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

        // REWARD SYSTEM DESIGN:
        // - POSITIVE rewards (+15, +50, +2): Reset timeout timer (meaningful progress)
        // - NEGATIVE rewards (-10, -20, -5): Do NOT reset timeout timer (penalties)
        // - TIMEOUT punishment (-30): Special case, does not reset timer
        
        // Check for timeout punishment first
        if (enableTimeout && !resetOnTimeout && timeSinceLastReward >= noRewardTimeoutDuration && 
            !hasEarnedRewardThisEpisode && !hasBeenPunishedForTimeout)
        {
            reward += timeoutPunishment;
            rewardReason += $"Timeout punishment ({timeoutPunishment}) ";
            hasBeenPunishedForTimeout = true;
            lastTimeoutPunishmentTime = currentEpisodeTime;
            Debug.Log($"{agentName} TIMEOUT PUNISHMENT! Agent idle for {timeSinceLastReward:F1} seconds, punishment: {timeoutPunishment}");
        }
        
        // Additional punishment every 5 seconds of continued inactivity
        if (enableTimeout && !resetOnTimeout && hasBeenPunishedForTimeout && 
            (currentEpisodeTime - lastTimeoutPunishmentTime) >= noRewardTimeoutDuration)
        {
            reward += timeoutPunishment * 0.5f; // Smaller repeated punishment
            rewardReason += $"Continued timeout ({timeoutPunishment * 0.5f}) ";
            lastTimeoutPunishmentTime = currentEpisodeTime;
            Debug.Log($"{agentName} CONTINUED TIMEOUT! Additional punishment: {timeoutPunishment * 0.5f}");
        }

        // Enemy spotting reward (encourage seeking behavior) - ONLY for actual enemy agents, not walls
        if (agentSpottedEnemy[agentIndex] && !agentAlreadySpottedThisStep[agentIndex])
        {
            reward += 5f;
            rewardReason += "Spotted enemy agent (+5) ";
            agentAlreadySpottedThisStep[agentIndex] = true; // Prevent multiple rewards in same step
            Debug.Log($"{agentName} correctly spotted an enemy agent, rewarded +5");
        }
        
        // Shooting rewards/penalties
        if (agentShotEnemy[agentIndex])
        {
            reward += 200f;  // MAJOR REWARD - doubled from 100 to 200!
            rewardReason += "🏆 KILLED ENEMY (+200) ";
            // Don't end episode immediately in multi-agent - let others continue
            Debug.Log($"{agentName} 🏆 MASSIVE KILL REWARD! +200 points for elimination!");
        }
        else if (agentShotNothing[agentIndex])
        {
            reward -= 5f;  // Reduced penalty to encourage more shooting attempts
            rewardReason += "Shot nothing (-5) ";
        }

        // Wall collision penalty
        if (agentHitWall[agentIndex])
        {
            reward -= 20f;
            rewardReason += "Hit wall (-20) ";
        }

        // Small penalty for doing nothing (encourages action)
        if (lastAction == null || (lastAction.shoot < 0.5f && 
            Mathf.Abs(lastAction.moveForward) < 0.1f && 
            Mathf.Abs(lastAction.moveLeft) < 0.1f && 
            Mathf.Abs(lastAction.moveRight) < 0.1f))
        {
            reward -= 5f;
            rewardReason += "Do nothing (-5) ";
        }
        
        // Survival bonus (small reward for staying alive while others die)
        int aliveCount = 0;
        for (int i = 0; i < agentIsAlive.Length; i++)
        {
            if (agentIsAlive[i]) aliveCount++;
        }
        
        if (aliveCount <= agents.Length / 2 && agentIsAlive[agentIndex])
        {
            reward += 2f; // Small survival bonus
            rewardReason += "Survival (+2) ";
        }
        
        // Update cumulative rewards tracking
        agentLastStepRewards[agentIndex] = reward;
        agentCumulativeRewards[agentIndex] += reward;
        
        // Log reward calculation for debugging
        if (reward != 0f)
        {
            Debug.Log($"{agentName} Reward calculated: {reward:F1} - {rewardReason} (Total: {agentCumulativeRewards[agentIndex]:F1})");
            
            // Extra validation: If reward includes enemy spotting, verify it's legitimate
            if (rewardReason.Contains("Spotted enemy"))
            {
                Debug.Log($"{agentName} - VERIFICATION: Enemy spotting reward given. Ensure this was for spotting an actual agent, not a wall!");
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
        
        // Check if only one agent remains alive
        int aliveCount = 0;
        for (int i = 0; i < agentIsAlive.Length; i++)
        {
            if (agentIsAlive[i]) aliveCount++;
        }
        
        if (aliveCount <= 1)
        {
            Debug.Log($"Episode finished - Only {aliveCount} agent(s) remaining");
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
            agentKills[i] = 0;
            agentIsAlive[i] = true; // All agents start alive
            agentCumulativeRewards[i] = 0f; // Reset cumulative rewards
            agentLastStepRewards[i] = 0f; // Reset last step rewards
            
            // Revive agent (make visible and enable collider)
            ReviveAgent(i);
        }
        
        // Reset timeout tracking
        timeSinceLastReward = 0f;
        hasEarnedRewardThisEpisode = false;
        hasBeenPunishedForTimeout = false;
        lastTimeoutPunishmentTime = 0f;

        // Reset agent positions
        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            // Shuffle spawn points to ensure fair spawning
            System.Collections.Generic.List<int> availableSpawns = new System.Collections.Generic.List<int>();
            for (int i = 0; i < spawnPoints.Length; i++)
            {
                availableSpawns.Add(i);
            }
            
            for (int i = 0; i < agents.Length && i < spawnPoints.Length; i++)
            {
                int randomIndex = Random.Range(0, availableSpawns.Count);
                int spawnIndex = availableSpawns[randomIndex];
                availableSpawns.RemoveAt(randomIndex);
                
                agents[i].position = spawnPoints[spawnIndex].position;
                agents[i].rotation = spawnPoints[spawnIndex].rotation;
            }
        }
        else
        {
            Debug.LogWarning("No spawn points assigned! Agents will spawn at their current positions.");
        }

        UpdateState();
    }

    public void Step()
    {
        currentEpisodeTime += Time.fixedDeltaTime;
        timeSinceLastReward += Time.fixedDeltaTime;
        
        // Reset flags for all agents
        for (int i = 0; i < agents.Length; i++)
        {
            agentHitWall[i] = false;
            agentShotEnemy[i] = false;
            agentShotNothing[i] = false;
            agentSpottedEnemy[i] = false;
            agentAlreadySpottedThisStep[i] = false;
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
            bool isTimeoutPunishment = (agentReward == timeoutPunishment || agentReward == timeoutPunishment * 0.5f);
            
            if (agentReward > 1f && !isTimeoutPunishment)
            {
                anyMeaningfulReward = true;
                Debug.Log($"Agent_{i} earned meaningful POSITIVE reward: {agentReward:F1} - timeout timer will reset");
            }
            else if (agentReward < -1f && !isTimeoutPunishment)
            {
                Debug.Log($"Agent_{i} received penalty: {agentReward:F1} - timeout timer will NOT reset");
            }
        }
        
        // Update timeout tracking based on any agent earning meaningful POSITIVE reward
        if (anyMeaningfulReward)
        {
            timeSinceLastReward = 0f; // Reset timeout timer
            hasEarnedRewardThisEpisode = true;
            hasBeenPunishedForTimeout = false; // Reset punishment flag
            Debug.Log($"Meaningful POSITIVE reward earned by agents, timeout timer reset to 0");
        }
        
        // Update lastReward for legacy compatibility (use average or first agent)
        lastReward = agents.Length > 0 ? agentCumulativeRewards[0] : 0f;
        
        // Log timeout status periodically
        if (enableTimeout && timeSinceLastReward > 2f && (int)(timeSinceLastReward * 2) % 2 == 0) // Every 0.5 seconds after 2 seconds
        {
            string status = hasBeenPunishedForTimeout ? " (PUNISHED)" : "";
            Debug.Log($"Time since last reward: {timeSinceLastReward:F1}s / {noRewardTimeoutDuration}s{status}");
        }
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
        
        string agentName = $"Agent_{agentIndex}";
        Debug.Log($"{agentName} Action - Look: {action.lookAngle:F2}, Shoot: {action.shoot}, Forward: {action.moveForward:F2}, Left: {action.moveLeft:F2}, Right: {action.moveRight:F2}");

        // Apply rotation
        if (Mathf.Abs(action.lookAngle) > 0.1f)
        {
            float rotationAmount = action.lookAngle * rotationSpeed * Time.fixedDeltaTime;
            agent.Rotate(0, 0, rotationAmount);
        }

        // Apply movement with better physics integration
        Vector3 movement = Vector3.zero;
        bool hasMovement = false;
        
        if (Mathf.Abs(action.moveForward) > 0.01f)
        {
            movement += agent.up * action.moveForward * moveSpeed * Time.fixedDeltaTime;
            hasMovement = true;
            Debug.Log($"Forward movement: {action.moveForward:F2} -> {movement.magnitude:F3}");
        }
        if (Mathf.Abs(action.moveLeft) > 0.01f)
        {
            movement -= agent.right * action.moveLeft * moveSpeed * Time.fixedDeltaTime;
            hasMovement = true;
            Debug.Log($"Left movement: {action.moveLeft:F2}");
        }
        if (Mathf.Abs(action.moveRight) > 0.01f)
        {
            movement += agent.right * action.moveRight * moveSpeed * Time.fixedDeltaTime;
            hasMovement = true;
            Debug.Log($"Right movement: {action.moveRight:F2}");
        }
        
        if (!hasMovement)
        {
            Debug.Log($"Agent_{agentIndex} - No movement (all movement values too small)");
        }

        // Apply actual movement with physics support
        if (movement != Vector3.zero)
        {
            // Check for wall collision before moving
            RaycastHit2D hit = Physics2D.Raycast(agent.position, movement.normalized, movement.magnitude + 0.5f, wallLayerMask);
            if (hit.collider != null)
            {
                Debug.Log($"Wall collision detected for Agent_{agentIndex} - blocked by {hit.collider.name}");
                agentHitWall[agentIndex] = true;
            }
            else
            {
                Vector3 oldPos = agent.position;
                
                // Try to use Rigidbody2D if available, otherwise move directly
                Rigidbody2D rb = agent.GetComponent<Rigidbody2D>();
                if (rb != null)
                {
                    // Use physics-based movement
                    rb.MovePosition(agent.position + movement);
                    Debug.Log($"Agent_{agentIndex} moved with Rigidbody from {oldPos} to {agent.position} (delta: {movement})");
                }
                else
                {
                    // Direct position change
                    agent.position += movement;
                    Debug.Log($"Agent_{agentIndex} moved directly from {oldPos} to {agent.position} (delta: {movement})");
                }
            }
        }
        else
        {
            Debug.Log($"Agent_{agentIndex} - No movement (movement vector is zero)");
        }

        // Handle shooting
        if (action.shoot > 0.1f)  // Lower threshold for better shooting detection
        {
            Debug.Log($"🔫 Agent_{agentIndex} ATTEMPTING TO SHOOT! (shoot value: {action.shoot:F2})");
            PerformShoot(agent, agentIndex);
        }
        else
        {
            Debug.Log($"Agent_{agentIndex} not shooting (shoot value: {action.shoot:F2} <= 0.1)");
        }
    }

    private void PerformShoot(Transform shooter, int shooterIndex)
    {
        // Dead agents cannot shoot
        if (!agentIsAlive[shooterIndex])
        {
            Debug.Log($"💀 Dead Agent_{shooterIndex} attempted to shoot - ignoring");
            return;
        }
        
        Debug.Log($"🔫 Agent_{shooterIndex} FIRES WEAPON! Position: {shooter.position}, Direction: {shooter.up}");
        
        // Apply 0.4 unit offset in forward direction for shooting (like ray offset)
        Vector3 shootOrigin = shooter.position + (shooter.up * 0.4f);
        
        // Raycast in the forward direction to check for hits
        RaycastHit2D hit = Physics2D.Raycast(shootOrigin, shooter.up, shootRange, wallLayerMask | agentLayerMask);
        
        // Enhanced visual feedback with proper color coding
        Color shootRayColor = Color.cyan; // Cyan for shooting rays to distinguish from detection rays
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
            shootRayColor = hitAgent ? Color.magenta : Color.cyan; // Magenta for enemy hits, cyan for wall hits
        }
        Debug.DrawRay(shootOrigin, shooter.up * shootRange, shootRayColor, 1f);
        
        Debug.Log($"🎯 Agent_{shooterIndex} shoots from offset position! Hit: {(hit.collider != null ? hit.collider.name : "nothing")} at distance {(hit.collider != null ? hit.distance.ToString("F2") : "N/A")}");

        if (hit.collider != null)
        {
            // Check if hit any other agent (all agents are enemies of each other)
            bool hitEnemy = false;
            for (int i = 0; i < agents.Length; i++)
            {
                if (i != shooterIndex && hit.collider.transform == agents[i] && agentIsAlive[i])
                {
                    agentShotEnemy[shooterIndex] = true;
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
                                Debug.Log($"🟢 Ray {i} from Agent_{castingAgentIndex} hit SELF at distance {hits[i].distance:F2}");
                            }
                            else
                            {
                                // Hit enemy agent within SPOTTING_RANGE (same as RAY_DISTANCE)
                                hitEnemyAgent = true;
                                Debug.Log($"🎯 Ray {i} from Agent_{castingAgentIndex} hit ENEMY Agent_{targetIndex} at distance {hits[i].distance:F2} (max range: {SPOTTING_RANGE})");
                                
                                if (!agentAlreadySpottedThisStep[castingAgentIndex])
                                {
                                    agentSpottedEnemy[castingAgentIndex] = true;
                                    Debug.Log($"✅ Agent_{castingAgentIndex} gets SPOTTING REWARD for detecting Agent_{targetIndex} within range {SPOTTING_RANGE}");
                                }
                            }
                            break;
                        }
                    }
                    
                    // If didn't hit any agent, check if it's a wall
                    if (!hitEnemyAgent && !hitSelf)
                    {
                        bool hitWall = ((1 << hits[i].collider.gameObject.layer) & wallLayerMask) != 0;
                        if (hitWall)
                        {
                            Debug.Log($"🟢 Ray {i} from Agent_{castingAgentIndex} hit WALL at distance {hits[i].distance:F2} - NO REWARD");
                        }
                        else
                        {
                            Debug.Log($"🟢 Ray {i} from Agent_{castingAgentIndex} hit UNKNOWN: {hits[i].collider.name} (layer: {hits[i].collider.gameObject.layer})");
                        }
                    }
                }
            }
            
            // Visual debug
            if (hits[i].collider != null)
            {
                
                // Color coding based on what was hit
                Color rayColor;
                if (hitEnemyAgent)
                {
                    rayColor = Color.blue;  // Blue: Hit enemy agent (SPOTTED!)
                    Debug.Log($"🔵 Ray {i} from Agent_{castingAgentIndex} detected ENEMY agent at distance {hits[i].distance:F2}");
                }
                else
                {
                    rayColor = Color.green; //  Green: Hit wall/obstacle or own agent
                    
                    if (hitSelf)
                    {
                        Debug.Log($"🟢 Ray {i} from Agent_{castingAgentIndex} hit SELF (green ray - no reward)");
                    }
                    else
                    {
                        // Check what we actually hit for debugging
                        bool hitWall = ((1 << hits[i].collider.gameObject.layer) & wallLayerMask) != 0;
                        if (hitWall)
                        {
                            Debug.Log($"🟢 Ray {i} from Agent_{castingAgentIndex} hit WALL (green ray - no reward)");
                        }
                        else
                        {
                            Debug.Log($"🟢 Ray {i} from Agent_{castingAgentIndex} hit UNKNOWN: {hits[i].collider.name} (green ray)");
                        }
                    }
                }
                
                Debug.DrawLine(rayOrigin, hits[i].point, rayColor, 0.2f);
            }
            else
            {
                //  Red: No collision (clear path)
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
        agentKills = new int[agentCount];
        agentIsAlive = new bool[agentCount];
        agentCumulativeRewards = new float[agentCount];
        agentLastStepRewards = new float[agentCount];
        
        Debug.Log($"Environment initialized with {agentCount} agents");
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
    
    // Method to get death statistics
    public void LogDeathStatistics()
    {
        int aliveCount = GetAliveAgentCount();
        int deadCount = agents.Length - aliveCount;
        
        Debug.Log($"🏆 DEATH STATISTICS: {aliveCount} alive, {deadCount} dead");
        
        for (int i = 0; i < agents.Length; i++)
        {
            string status = agentIsAlive[i] ? "🟢 ALIVE" : "💀 DEAD";
            Debug.Log($"Agent_{i}: {status}, Kills: {agentKills[i]}");
        }
    }
    
    // Debug method to test death system
    [ContextMenu("Test Agent Death")]
    public void TestAgentDeath()
    {
        if (agents.Length > 1)
        {
            Debug.Log("🧪 TESTING AGENT DEATH SYSTEM");
            LogDeathStatistics();
            
            // Kill the first alive agent we find (except the first one)
            for (int i = 1; i < agents.Length; i++)
            {
                if (agentIsAlive[i])
                {
                    Debug.Log($"💀 Test killing Agent_{i}");
                    agentIsAlive[i] = false;
                    MakeAgentDead(i);
                    break;
                }
            }
            
            LogDeathStatistics();
        }
    }
    
    // Debug method to test shooting
    [ContextMenu("Test Agent Shooting")]
    public void TestAgentShooting()
    {
        Debug.Log("🔫 TESTING AGENT SHOOTING SYSTEM");
        
        // Find first alive agent to test shooting
        for (int i = 0; i < agents.Length; i++)
        {
            if (agentIsAlive[i])
            {
                Debug.Log($"🎯 Testing Agent_{i} shooting capability");
                PerformShoot(agents[i], i);
                break;
            }
        }
        
        // Log current reward structure
        Debug.Log("💰 UPDATED REWARD STRUCTURE:");
        Debug.Log("   🏆 Kill Enemy: +200 points (DOUBLED!)");
        Debug.Log("   👁️ Spot Enemy: +5 points");
        Debug.Log("   🚫 Miss Shot: -5 points (reduced penalty)");
        Debug.Log("   🧱 Hit Wall: -20 points");
        Debug.Log("   ⏰ Timeout: -30 points");
        Debug.Log("   📊 Random shooting: 80% chance");
        Debug.Log("   🎯 Neural threshold: 0.1 (very easy)");
        Debug.Log("   🔧 Shooting threshold: 0.1 (very sensitive)");
    }
    
    // Debug method to force all agents to shoot
    [ContextMenu("Force All Agents Shoot")]
    public void ForceAllAgentsShoot()
    {
        Debug.Log("🔫🔫🔫 FORCING ALL AGENTS TO SHOOT NOW!");
        
        int shootCount = 0;
        for (int i = 0; i < agents.Length; i++)
        {
            if (agentIsAlive[i])
            {
                Debug.Log($"💥 Forcing Agent_{i} to shoot");
                PerformShoot(agents[i], i);
                shootCount++;
            }
        }
        
        Debug.Log($"🎯 Total shots fired: {shootCount}");
        LogDeathStatistics();
    }
    
    // Debug method to test ray visualization
    [ContextMenu("Test Ray Visualization")]
    public void TestRayVisualization()
    {
        Debug.Log("🌈 TESTING RAY VISUALIZATION SYSTEM");
        
        // Force raycast updates for all alive agents
        for (int i = 0; i < agents.Length; i++)
        {
            if (agentIsAlive[i])
            {
                Debug.Log($"🔍 Testing rays for Agent_{i}");
                PerformRaycasts(agents[i].position, agents[i].up, i);
            }
        }
        
        Debug.Log("🎨 RAY COLOR SYSTEM:");
        Debug.Log("   🔴 Red: No collision (clear path)");
        Debug.Log("   🟢 Green: Hit wall/obstacle");
        Debug.Log("   🔵 Blue: Hit enemy agent (SPOTTED!)");
        Debug.Log("   🔵 Cyan: Shooting ray (weapon fire)");
        Debug.Log("   🟣 Magenta: Shooting ray hit enemy");
        Debug.Log("📡 Check Scene view to see colored rays!");
    }
    
    // Debug method to test agent detection specifically
    [ContextMenu("Test Agent Detection")]
    public void TestAgentDetection()
    {
        Debug.Log("🔍 TESTING AGENT-TO-AGENT DETECTION");
        
        if (agents.Length < 2)
        {
            Debug.LogError("Need at least 2 agents to test detection!");
            return;
        }
        
        // Test detection between first two alive agents
        int agent1 = -1, agent2 = -1;
        for (int i = 0; i < agents.Length; i++)
        {
            if (agentIsAlive[i])
            {
                if (agent1 == -1) agent1 = i;
                else if (agent2 == -1) { agent2 = i; break; }
            }
        }
        
        if (agent1 >= 0 && agent2 >= 0)
        {
            Debug.Log($"🎯 Testing detection between Agent_{agent1} and Agent_{agent2}");
            Debug.Log($"Agent_{agent1} position: {agents[agent1].position}");
            Debug.Log($"Agent_{agent2} position: {agents[agent2].position}");
            Debug.Log($"Distance: {Vector3.Distance(agents[agent1].position, agents[agent2].position):F2}");
            
            // Point agent1 towards agent2
            Vector3 direction = (agents[agent2].position - agents[agent1].position).normalized;
            agents[agent1].up = direction;
            
            Debug.Log($"🔄 Pointed Agent_{agent1} towards Agent_{agent2}");
            Debug.Log("🔍 Casting rays to test detection...");
            Debug.Log($"🆔 CASTING AGENT INDEX: {agent1} (should detect Agent_{agent2} as enemy)");
            
            PerformRaycasts(agents[agent1].position, agents[agent1].up, agent1);
        }
        else
        {
            Debug.LogError("Could not find two alive agents for testing!");
        }
    }
    
    // Debug method to verify agent identification
    [ContextMenu("Verify Agent Identification")]
    public void VerifyAgentIdentification()
    {
        Debug.Log("🆔 VERIFYING AGENT IDENTIFICATION SYSTEM");
        
        for (int i = 0; i < agents.Length; i++)
        {
            if (agentIsAlive[i])
            {
                Debug.Log($"🤖 Agent_{i} at position {agents[i].position}");
                Debug.Log($"   - Casting rays as Agent_{i}");
                Debug.Log($"   - Should see other agents as enemies");
                Debug.Log($"   - Should NOT see self as enemy");
                
                // Cast a few rays to test
                for (int ray = 0; ray < 5; ray++) // Test first 5 rays
                {
                    float angle = -90f + (ray * 45f); // Spread rays around
                    Vector3 direction = Quaternion.Euler(0, 0, angle) * agents[i].up;
                    RaycastHit2D hit = Physics2D.Raycast(agents[i].position, direction, 10f, wallLayerMask | agentLayerMask);
                    
                    if (hit.collider != null)
                    {
                        // Check what we hit
                        for (int a = 0; a < agents.Length; a++)
                        {
                            if (hit.collider.transform == agents[a])
                            {
                                if (a == i)
                                {
                                    Debug.Log($"   ⚠️ Ray {ray} hit SELF (Agent_{a}) - should be GREEN");
                                }
                                else if (agentIsAlive[a])
                                {
                                    Debug.Log($"   ✅ Ray {ray} hit ENEMY (Agent_{a}) - should be BLUE");
                                }
                                break;
                            }
                        }
                    }
                }
            }
        }
    }
}
