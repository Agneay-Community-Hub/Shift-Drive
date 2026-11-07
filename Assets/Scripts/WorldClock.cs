using UnityEngine;

/// World time used by every obstacle. Third gear slows this clock down while the
/// player's car (and the race timer) keep running in real time.
public static class WorldClock
{
    public static float Scale = 1f;
    public static float Time;

    public static void Tick(float realDelta, float targetScale)
    {
        Scale = Mathf.MoveTowards(Scale, targetScale, realDelta * 3.5f);
        Time += realDelta * Scale;
    }
}
