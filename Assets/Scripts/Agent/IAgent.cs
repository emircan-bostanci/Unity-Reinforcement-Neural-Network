public interface IAgent
{
    Action SelectAction(State state);
    void Learn(State state, Action action, float reward, State nextState, bool done);
    void SaveModel(string path);
    void LoadModel(string path);
}