using UnityEngine;

public class Timer
{
    private enum Mode { None, Stopwatch, Countdown }
    private Mode mode = Mode.None;
    private float startTime;
    private float duration;

    // --- START / SETUP ---

    public void StartStopwatch()
    {
        mode = Mode.Stopwatch;
        startTime = Time.time;
        duration = 0f;
    }

    public void StartCountdown(float seconds)
    {
        mode = Mode.Countdown;
        startTime = Time.time;
        duration = Mathf.Max(0f, seconds);
    }

    // --- READ STATE ---

    /// <summary> Current seconds: elapsed (stopwatch) or remaining (countdown). </summary>
    public float Seconds
    {
        get
        {
            if (mode == Mode.None) return 0f;
            float elapsed = Time.time - startTime;
            if (mode == Mode.Stopwatch) return elapsed;
            return Mathf.Max(0f, duration - elapsed);
        }
    }

    /// <summary> Is the timer currently running? (true after Start…, false after Stop/Reset) </summary>
    public bool IsRunning => mode != Mode.None;

    /// <summary> Has the countdown reached zero? (Only meaningful for countdowns) </summary>
    public bool IsFinished => mode == Mode.Countdown && Seconds <= 0f;

    /// <summary> For stopwatches: returns true if the elapsed time is >= the given seconds. </summary>
    public bool HasElapsed(float seconds)
    {
        return mode == Mode.Stopwatch && Seconds >= seconds;
    }

    // --- CONTROL ---

    public void Stop()
    {
        // freeze the current value, then deactivate
        if (mode == Mode.Stopwatch)
            duration = Seconds;
        else if (mode == Mode.Countdown)
            duration = Seconds;

        mode = Mode.None;
    }

    public void Reset()
    {
        mode = Mode.None;
        startTime = 0f;
        duration = 0f;
    }
}