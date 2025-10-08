# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a Unity project implementing a deep reinforcement learning environment where multiple agents compete in a battle arena. Agents use neural networks to learn combat behaviors through reinforcement learning with genetic algorithm optimization.

## Build and Development Commands

**Unity Development:**
- Open project in Unity 6000.2.2f1 or later
- Open main scene: `Assets/Scenes/SampleScene.unity`
- Press Play to start training simulation
- Use Unity Console to monitor training progress

**Testing Commands:**
- Right-click Environment script → "Test Agent Death"
- Right-click Environment script → "Test Agent Shooting" 
- Right-click Environment script → "Force All Agents Shoot"
- Right-click GeneticAlgorithmManager → "Force Generation Evaluation"

## High-Level Architecture

### Core Systems

**Environment System (`Envirovment.cs`)**
- Multi-agent battle arena with raycast-based perception
- 32-ray vision system for enemy detection and obstacle avoidance
- Reward system: +200 for kills, +5 for enemy spotting, penalties for inaction
- Timeout system triggers genetic evolution when agents are inactive

**Agent System (`Agent.cs`)**
- Policy gradient reinforcement learning with Actor-Critic architecture
- Continuous action space: look angle, shoot, movement (forward/left/right)
- Experience buffer for batch training with GAE (Generalized Advantage Estimation)
- Random action fallback for initial exploration

**Neural Network (`SimpleNeuralNetwork.cs`)**
- Two-layer feedforward network (128→64→5 neurons)
- Separate Actor (policy) and Critic (value) networks
- Xavier weight initialization, ReLU hidden layers, tanh output
- Save/load functionality for model persistence

**Genetic Algorithm (`GeneticAlgorithmManager.cs`)**
- Population-based evolution with elite selection (20% elites)
- Fitness based on cumulative rewards (80%) and survival time (20%)
- Auto-save system for model checkpoints
- Timeout-triggered evolution when training stagnates

### State Representation

**Input State (35 dimensions):**
- 32 normalized ray distances (0-1, 1.0 = max range)
- Agent position X, Y (normalized to map bounds)
- Agent rotation (0-1)

**Action Space (5 dimensions):**
- `lookAngle`: Rotation speed (-1 to 1)
- `shoot`: Fire weapon (>0.1 threshold)
- `moveForward`: Forward movement (-1 to 1)
- `moveLeft`: Strafe left (-1 to 1) 
- `moveRight`: Strafe right (-1 to 1)

### Training Managers

- `TrainingManager.cs`: Standard RL training loop
- `LightweightTrainingManager.cs`: Optimized for performance
- `PerformanceTrainingManager.cs`: Alternative training approach
- `GeneticAlgorithmManager.cs`: Evolution-based optimization

## Key Implementation Details

**Multi-Agent Setup:**
- All agents compete against each other (free-for-all)
- Ray detection distinguishes self vs enemy agents
- Dead agents are moved to "graveyard" position and made invisible
- Episodes end when ≤1 agent remains alive

**Reward Engineering:**
- Massive kill reward (+200) to encourage combat
- Enemy spotting reward (+5) to encourage seeking behavior
- Penalties for missing shots (-5), hitting walls (-20), inaction (-5)
- Timeout punishment (-30) for excessive passivity

**Performance Optimizations:**
- Fixed timestep training (50Hz)
- Batch experience processing
- Efficient raycast visualization system
- Memory management for large agent populations

## Common Workflows

1. **Setup New Environment:** Follow instructions in `SetupInstructions.cs:10-88`
2. **Monitor Training:** Check Unity Console for reward logs and statistics
3. **Evaluate Models:** Use context menu debug functions on Environment
4. **Save/Load Models:** Automatic saves in `SavedModels/` directory
5. **Genetic Evolution:** Automatic or manual trigger via context menu

## Development Notes

- Default population size: 8 agents
- Generation duration: 10 seconds (configurable)
- Network architecture optimized for real-time training
- Extensive debug logging for troubleshooting training issues
- Visual ray debugging with color coding (red=clear, green=wall, blue=enemy)