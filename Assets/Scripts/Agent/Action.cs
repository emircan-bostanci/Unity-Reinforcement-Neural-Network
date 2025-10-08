public class Action
{
    public float lookAngle;
    public float shoot;
    public float moveForward;
    public float moveRight;
    public float moveLeft;

    public Action()
    {
        lookAngle = 0f;
        shoot = 0f;
        moveForward = 0f;
        moveRight = 0f;
        moveLeft = 0f;
    }

    public Action(float lookAngle, float shoot, float moveForward, float moveLeft, float moveRight)
    {
        this.lookAngle = lookAngle;
        this.shoot = shoot;
        this.moveForward = moveForward;
        this.moveLeft = moveLeft;
        this.moveRight = moveRight;
    }
}