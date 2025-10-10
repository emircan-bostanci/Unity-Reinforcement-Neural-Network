# Dynamic Agent Spawning System

## Quick Setup Guide

### 1. Add Dynamic Spawn Manager
1. Select your Environment GameObject
2. Add Component → DynamicSpawnManager
3. Add Component → EnvironmentSpawnExtension

### 2. Configure Spawn Settings
In the DynamicSpawnManager component:

```csharp
// Basic Settings
useDynamicSpawning = true
spawnAreaCenter = (0, 0)          // Center of spawn area
spawnAreaSize = (20, 20)          // Width and height of spawn area
minimumSpawnDistance = 2.0        // Minimum distance between agents
generateSpawnPointsForAgents = true
```

### 3. Setup Agents
- The system will automatically detect agents from the Environment component
- Or manually assign agents to the `agents` array in DynamicSpawnManager

### 4. Test the System
Right-click on DynamicSpawnManager and select:
- "Generate Spawn Points" - Creates spawn points automatically
- "Test Spawn" - Tests the spawning system

## Features

### 🎲 **Dynamic Random Spawning**
- Agents spawn at random positions within defined area
- Maintains minimum distance between agents
- Random rotation for each agent

### 🎯 **Automatic Spawn Point Generation**
- Creates spawn points matching agent count
- Visual indicators in Scene view
- Organized under "SpawnPoints" parent object

### 📊 **Visual Feedback**
- Green area shows spawn zone in Scene view
- Blue spheres show spawn point locations
- Red center point shows spawn area center

### ⚙️ **Flexible Configuration**
- Toggle between dynamic and fixed spawning
- Adjustable spawn area size and position
- Configurable minimum distance between agents

## Integration

### Using with Existing Environment:
The system works alongside your existing Environment script:

1. **EnvironmentSpawnExtension** bridges the gap
2. Call `EnhanceEnvironmentReset()` instead of normal reset
3. All existing Environment functionality preserved

### Manual Integration:
```csharp
// In your Environment Reset() method:
if (spawnExtension != null && spawnExtension.useSpawnManagerInstead)
{
    spawnExtension.EnhanceEnvironmentReset();
}
else
{
    // Your existing spawn logic
}
```

## Benefits

✅ **No Agent Count Limits**: System adapts to any number of agents
✅ **Prevents Clustering**: Minimum distance enforcement
✅ **Visual Debugging**: See spawn areas in Scene view  
✅ **Performance Friendly**: Efficient position generation
✅ **Easy Integration**: Works with existing code

## Troubleshooting

**Issue**: Agents not spawning in area
- Check spawn area size is large enough
- Reduce minimum spawn distance
- Increase max attempts in generation

**Issue**: Not enough spawn positions
- Increase spawn area size
- Reduce minimum distance between agents
- Check agent count vs available space

**Issue**: Spawn points not visible
- Enable "Show Spawn Area" in DynamicSpawnManager
- Check Scene view has Gizmos enabled

The system automatically handles scaling from small testing (6 agents) to large populations (20+ agents) with proper spacing and randomization!