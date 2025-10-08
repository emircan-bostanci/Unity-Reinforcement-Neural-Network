public interface IEnvironment
{
    void Step();
    void Reset();
    bool IsEpisodeFinished();
    float GetReward();
    int GetStateSize();
    int GetActionSize();
}