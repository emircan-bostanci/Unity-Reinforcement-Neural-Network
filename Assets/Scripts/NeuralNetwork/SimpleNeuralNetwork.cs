using UnityEngine;
using System;
using System.IO;

[System.Serializable]
public class SimpleNeuralNetwork : INeuralNetwork
{
    [Header("Network Architecture")]
    public int inputSize = 38;  // 32 rays + 6 position/rotation values
    public int hiddenSize1 = 128;
    public int hiddenSize2 = 64;
    public int outputSize = 5;  // 5 action parameters (continuous actions)
    public bool useValueNetwork = true;  // For Actor-Critic methods
    
    // INetworkInterface properties
    public bool UseValueNetwork { get => useValueNetwork; set => useValueNetwork = value; }

    // Actor network weights and biases (for policy)
    private float[,] weightsInputHidden1;
    private float[] biasesHidden1;
    private float[,] weightsHidden1Hidden2;
    private float[] biasesHidden2;
    private float[,] weightsHidden2Output;
    private float[] biasesOutput;
    
    // Critic network weights and biases (for value estimation)
    private float[,] criticWeightsInputHidden1;
    private float[] criticBiasesHidden1;
    private float[,] criticWeightsHidden1Hidden2;
    private float[] criticBiasesHidden2;
    private float[,] criticWeightsHidden2Output;
    private float[] criticBiasesOutput;

    // For backpropagation
    private float[] lastInput;
    private float[] lastHidden1;
    private float[] lastHidden2;
    private float[] lastOutput;

    private bool isInitialized = false;

    public SimpleNeuralNetwork()
    {
        // Don't initialize here - will be done in Initialize() method
    }

    public SimpleNeuralNetwork(int inputSize, int hiddenSize1, int hiddenSize2, int outputSize)
    {
        this.inputSize = inputSize;
        this.hiddenSize1 = hiddenSize1;
        this.hiddenSize2 = hiddenSize2;
        this.outputSize = outputSize;
        // Don't initialize here - will be done in Initialize() method
    }
    
    public void Initialize()
    {
        if (!isInitialized)
        {
            InitializeNetwork();
            isInitialized = true;
        }
    }

    private void InitializeNetwork()
    {
        // Initialize actor network weights with Xavier initialization
        weightsInputHidden1 = InitializeWeights(inputSize, hiddenSize1);
        biasesHidden1 = new float[hiddenSize1];

        weightsHidden1Hidden2 = InitializeWeights(hiddenSize1, hiddenSize2);
        biasesHidden2 = new float[hiddenSize2];

        weightsHidden2Output = InitializeWeights(hiddenSize2, outputSize);
        biasesOutput = new float[outputSize];
        
        // Initialize critic network weights if using value network
        if (useValueNetwork)
        {
            criticWeightsInputHidden1 = InitializeWeights(inputSize, hiddenSize1);
            criticBiasesHidden1 = new float[hiddenSize1];
            
            criticWeightsHidden1Hidden2 = InitializeWeights(hiddenSize1, hiddenSize2);
            criticBiasesHidden2 = new float[hiddenSize2];
            
            criticWeightsHidden2Output = InitializeWeights(hiddenSize2, 1); // Single value output
            criticBiasesOutput = new float[1];
        }

        // Initialize arrays for storing intermediate values
        lastInput = new float[inputSize];
        lastHidden1 = new float[hiddenSize1];
        lastHidden2 = new float[hiddenSize2];
        lastOutput = new float[outputSize];
    }

    private float[,] InitializeWeights(int rows, int cols)
    {
        float[,] weights = new float[rows, cols];
        float std = Mathf.Sqrt(2.0f / rows); // Xavier initialization
        
        // Use System.Random instead of UnityEngine.Random to avoid serialization issues
        System.Random random = new System.Random();

        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < cols; j++)
            {
                weights[i, j] = (float)(random.NextDouble() * 2.0 - 1.0) * std;
            }
        }
        return weights;
    }

    public float[] Forward(float[] input)
    {
        // Ensure network is initialized
        if (!isInitialized)
        {
            Initialize();
        }
        
        if (input.Length != inputSize)
        {
            Debug.LogError($"Input size mismatch. Expected {inputSize}, got {input.Length}");
            return new float[outputSize];
        }

        // Store input for backpropagation
        Array.Copy(input, lastInput, input.Length);

        // Forward pass through first hidden layer
        for (int i = 0; i < hiddenSize1; i++)
        {
            float sum = biasesHidden1[i];
            for (int j = 0; j < inputSize; j++)
            {
                sum += input[j] * weightsInputHidden1[j, i];
            }
            lastHidden1[i] = ReLU(sum);
        }

        // Forward pass through second hidden layer
        for (int i = 0; i < hiddenSize2; i++)
        {
            float sum = biasesHidden2[i];
            for (int j = 0; j < hiddenSize1; j++)
            {
                sum += lastHidden1[j] * weightsHidden1Hidden2[j, i];
            }
            lastHidden2[i] = ReLU(sum);
        }

        // Forward pass through output layer (Actor network - outputs action parameters)
        for (int i = 0; i < outputSize; i++)
        {
            float sum = biasesOutput[i];
            for (int j = 0; j < hiddenSize2; j++)
            {
                sum += lastHidden2[j] * weightsHidden2Output[j, i];
            }
            // Use tanh activation for bounded continuous actions
            lastOutput[i] = Tanh(sum);
        }

        return (float[])lastOutput.Clone();
    }
    
    // Forward pass for value network (Critic)
    public float ForwardValue(float[] input)
    {
        // Ensure network is initialized
        if (!isInitialized)
        {
            Initialize();
        }
        
        if (!useValueNetwork)
        {
            Debug.LogWarning("Value network not enabled");
            return 0f;
        }
        
        if (input.Length != inputSize)
        {
            Debug.LogError($"Input size mismatch for value network. Expected {inputSize}, got {input.Length}");
            return 0f;
        }

        // Forward pass through first hidden layer (critic)
        float[] criticHidden1 = new float[hiddenSize1];
        for (int i = 0; i < hiddenSize1; i++)
        {
            float sum = criticBiasesHidden1[i];
            for (int j = 0; j < inputSize; j++)
            {
                sum += input[j] * criticWeightsInputHidden1[j, i];
            }
            criticHidden1[i] = ReLU(sum);
        }

        // Forward pass through second hidden layer (critic)
        float[] criticHidden2 = new float[hiddenSize2];
        for (int i = 0; i < hiddenSize2; i++)
        {
            float sum = criticBiasesHidden2[i];
            for (int j = 0; j < hiddenSize1; j++)
            {
                sum += criticHidden1[j] * criticWeightsHidden1Hidden2[j, i];
            }
            criticHidden2[i] = ReLU(sum);
        }

        // Forward pass through output layer (critic - single value)
        float valueOutput = criticBiasesOutput[0];
        for (int j = 0; j < hiddenSize2; j++)
        {
            valueOutput += criticHidden2[j] * criticWeightsHidden2Output[j, 0];
        }
        
        return valueOutput; // No activation for value output
    }

    public void Backward(float[] input, float[] targetOutput, float learningRate)
    {
        // Simple gradient descent implementation
        if (targetOutput.Length != outputSize)
        {
            Debug.LogError($"Target output size mismatch. Expected {outputSize}, got {targetOutput.Length}");
            return;
        }

        // Forward pass to get current output
        float[] currentOutput = Forward(input);

        // Calculate output layer gradients
        float[] outputGradients = new float[outputSize];
        for (int i = 0; i < outputSize; i++)
        {
            outputGradients[i] = targetOutput[i] - currentOutput[i];
        }

        // Update output layer weights and biases
        for (int i = 0; i < hiddenSize2; i++)
        {
            for (int j = 0; j < outputSize; j++)
            {
                weightsHidden2Output[i, j] += learningRate * outputGradients[j] * lastHidden2[i];
            }
        }
        for (int i = 0; i < outputSize; i++)
        {
            biasesOutput[i] += learningRate * outputGradients[i];
        }

        // Calculate hidden layer 2 gradients
        float[] hidden2Gradients = new float[hiddenSize2];
        for (int i = 0; i < hiddenSize2; i++)
        {
            float sum = 0f;
            for (int j = 0; j < outputSize; j++)
            {
                sum += outputGradients[j] * weightsHidden2Output[i, j];
            }
            hidden2Gradients[i] = sum * ReLUDerivative(lastHidden2[i]);
        }

        // Update hidden layer 2 weights and biases
        for (int i = 0; i < hiddenSize1; i++)
        {
            for (int j = 0; j < hiddenSize2; j++)
            {
                weightsHidden1Hidden2[i, j] += learningRate * hidden2Gradients[j] * lastHidden1[i];
            }
        }
        for (int i = 0; i < hiddenSize2; i++)
        {
            biasesHidden2[i] += learningRate * hidden2Gradients[i];
        }

        // Calculate hidden layer 1 gradients
        float[] hidden1Gradients = new float[hiddenSize1];
        for (int i = 0; i < hiddenSize1; i++)
        {
            float sum = 0f;
            for (int j = 0; j < hiddenSize2; j++)
            {
                sum += hidden2Gradients[j] * weightsHidden1Hidden2[i, j];
            }
            hidden1Gradients[i] = sum * ReLUDerivative(lastHidden1[i]);
        }

        // Update hidden layer 1 weights and biases
        for (int i = 0; i < inputSize; i++)
        {
            for (int j = 0; j < hiddenSize1; j++)
            {
                weightsInputHidden1[i, j] += learningRate * hidden1Gradients[j] * lastInput[i];
            }
        }
        for (int i = 0; i < hiddenSize1; i++)
        {
            biasesHidden1[i] += learningRate * hidden1Gradients[i];
        }
    }

    public void BackwardGradient(float[] input, float[] targetOutput, float learningRate)
    {
        // For now, use the same implementation as Backward
        Backward(input, targetOutput, learningRate);
    }

    public void LoadWeights(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                NetworkData data = JsonUtility.FromJson<NetworkData>(json);
                
                // Load weights and biases from data
                LoadFromNetworkData(data);
                Debug.Log($"Loaded neural network from: {path}");
            }
            else
            {
                Debug.LogWarning($"File not found: {path}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error loading weights: {e.Message}");
        }
    }

    public void SaveWeights(string path)
    {
        try
        {
            NetworkData data = CreateNetworkData();
            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(path, json);
            Debug.Log($"Saved neural network to: {path}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Error saving weights: {e.Message}");
        }
    }

    public INeuralNetwork Clone()
    {
        SimpleNeuralNetwork clone = new SimpleNeuralNetwork(inputSize, hiddenSize1, hiddenSize2, outputSize);
        
        // Copy weights and biases
        Array.Copy(weightsInputHidden1, clone.weightsInputHidden1, weightsInputHidden1.Length);
        Array.Copy(biasesHidden1, clone.biasesHidden1, biasesHidden1.Length);
        Array.Copy(weightsHidden1Hidden2, clone.weightsHidden1Hidden2, weightsHidden1Hidden2.Length);
        Array.Copy(biasesHidden2, clone.biasesHidden2, biasesHidden2.Length);
        Array.Copy(weightsHidden2Output, clone.weightsHidden2Output, weightsHidden2Output.Length);
        Array.Copy(biasesOutput, clone.biasesOutput, biasesOutput.Length);
        
        return clone;
    }

    // Activation functions
    private float ReLU(float x)
    {
        return Mathf.Max(0f, x);
    }

    private float ReLUDerivative(float x)
    {
        return x > 0f ? 1f : 0f;
    }
    
    private float Tanh(float x)
    {
        return (float)System.Math.Tanh(x);
    }
    
    private float TanhDerivative(float x)
    {
        float tanh = Tanh(x);
        return 1f - tanh * tanh;
    }

    // Data structures for serialization
    [System.Serializable]
    private class NetworkData
    {
        public float[] weightsInputHidden1;
        public float[] biasesHidden1;
        public float[] weightsHidden1Hidden2;
        public float[] biasesHidden2;
        public float[] weightsHidden2Output;
        public float[] biasesOutput;
        public int inputSize;
        public int hiddenSize1;
        public int hiddenSize2;
        public int outputSize;
    }

    private NetworkData CreateNetworkData()
    {
        NetworkData data = new NetworkData();
        data.inputSize = inputSize;
        data.hiddenSize1 = hiddenSize1;
        data.hiddenSize2 = hiddenSize2;
        data.outputSize = outputSize;

        // Flatten 2D arrays to 1D for serialization
        data.weightsInputHidden1 = Flatten2DArray(weightsInputHidden1);
        data.biasesHidden1 = (float[])biasesHidden1.Clone();
        data.weightsHidden1Hidden2 = Flatten2DArray(weightsHidden1Hidden2);
        data.biasesHidden2 = (float[])biasesHidden2.Clone();
        data.weightsHidden2Output = Flatten2DArray(weightsHidden2Output);
        data.biasesOutput = (float[])biasesOutput.Clone();

        return data;
    }

    private void LoadFromNetworkData(NetworkData data)
    {
        // Restore 2D arrays from 1D
        weightsInputHidden1 = Unflatten1DArray(data.weightsInputHidden1, inputSize, hiddenSize1);
        biasesHidden1 = (float[])data.biasesHidden1.Clone();
        weightsHidden1Hidden2 = Unflatten1DArray(data.weightsHidden1Hidden2, hiddenSize1, hiddenSize2);
        biasesHidden2 = (float[])data.biasesHidden2.Clone();
        weightsHidden2Output = Unflatten1DArray(data.weightsHidden2Output, hiddenSize2, outputSize);
        biasesOutput = (float[])data.biasesOutput.Clone();
    }

    private float[] Flatten2DArray(float[,] array)
    {
        int rows = array.GetLength(0);
        int cols = array.GetLength(1);
        float[] flattened = new float[rows * cols];
        
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < cols; j++)
            {
                flattened[i * cols + j] = array[i, j];
            }
        }
        return flattened;
    }

    private float[,] Unflatten1DArray(float[] array, int rows, int cols)
    {
        float[,] result = new float[rows, cols];
        
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < cols; j++)
            {
                result[i, j] = array[i * cols + j];
            }
        }
        return result;
    }
}