using System.IO;
using System.Text;
using UnityEngine;

/// Test driver enabled with the "-autopilot <logDir>" command-line flag.
/// Drives the course with scripted gear choices, logs events and takes screenshots,
/// then quits. Used to verify the track is beatable without a human.
public class Autopilot : MonoBehaviour
{
    public CarController car;
    public System.Func<string> stateProvider;
    public System.Func<float> timeProvider;
    string dir;
    readonly StringBuilder log = new StringBuilder();
    float t, maxSpeed, lastLogZ = -999f;
    int shotIndex;
    float[] shotZ = { 30f, 90f, 200f, 380f, 650f, 760f, 1100f, 1220f, 1480f };
    bool wasWrecked, wasGrounded = true, finished;
    float takeoffZ;

    public static string ArgDir()
    {
        var a = System.Environment.GetCommandLineArgs();
        string trailer = TrailerRecorder.ArgDir();
        if (trailer != null) return trailer;
        for (int i = 0; i < a.Length; i++)
            if (a[i] == "-autopilot") return i + 1 < a.Length ? a[i + 1] : Application.persistentDataPath;
        return null;
    }

    void Start()
    {
        dir = ArgDir();
        Directory.CreateDirectory(dir);
        car.ExternalInput = true;
        car.Wreck += () => Log($"WRECK at z={car.transform.position.z:0.0} y={car.transform.position.y:0.0} gear={car.Gear}");
    }

    void Log(string s)
    {
        string line = $"[{t,6:0.00}] {s}";
        log.AppendLine(line);
        File.WriteAllText(Path.Combine(dir, "autopilot.log"), log.ToString());
    }

    int GearFor(float tile)
    {
        if (tile < 2.6f) return 1;
        if (tile < 9.75f) return 2;
        if (tile < 12.75f) return 1;
        if (tile < 15.75f) return 2;
        if (tile < 18.5f) return 1;
        if (tile < 27.6f) return 3;
        if (tile < 46.0f) return 4;
        if (tile < 58.5f) return 1;
        if (tile < 65.1f) return 2;
        if (tile < 70.6f) return 3;
        return 4;
    }

    float TargetX(float tile)
    {
        if (tile > 59.5f && tile < 61.0f) return 5f;
        if (tile > 61.0f && tile < 62.7f) return -5f;
        if (tile > 62.7f && tile < 64.4f) return 5f;
        return 0f;
    }

    void Update()
    {
        t += Time.deltaTime;
        var p = car.transform.position;
        float tile = p.z / TrackBuilder.T;
        string state = stateProvider != null ? stateProvider() : "";

        if (car.Wrecked != wasWrecked && !car.Wrecked) Log($"respawn at z={p.z:0.0}");
        wasWrecked = car.Wrecked;

        if (!car.Wrecked && car.InputEnabled)
        {
            car.SetGear(GearFor(tile));
            Vector3 desired = new Vector3((TargetX(tile) - p.x) * 0.3f, 0f, 1f).normalized;
            Vector3 local = car.transform.InverseTransformDirection(desired);
            car.ExtSteer = Mathf.Clamp(local.x * 4f, -1f, 1f);
            float top = CarController.TopSpeed[car.Gear];
            bool safe = PathClear(p.z, Mathf.Max(0f, car.ForwardSpeed), out float dist);
            car.ExtThrottle = safe ? 1f : 0f;
            float v = car.ForwardSpeed;
            bool hall = (tile > 19f && tile < 27f) || (tile > 65.8f && tile < 70.2f);
            if (hall && v > 22f) { car.ExtThrottle = 0f; }
            car.ExtBrake = ((!safe && dist < v * v / 90f + 3f && v > 1f) || (hall && v > 25f)) ? 1f : 0f;
        }

        maxSpeed = Mathf.Max(maxSpeed, car.ForwardSpeed);
        if (wasGrounded && !car.Grounded) takeoffZ = p.z;
        if (!wasGrounded && car.Grounded && p.z - takeoffZ > 20f)
            Log($"landed: takeoff z={takeoffZ:0.0} -> z={p.z:0.0} (flight {p.z - takeoffZ:0.0} m) speed={car.ForwardSpeed:0.0}");
        wasGrounded = car.Grounded;

        if (p.z - lastLogZ > 90f && !car.Wrecked)
        {
            lastLogZ = p.z;
            Log($"z={p.z:0.0} tile={tile:0.0} x={p.x:0.0} y={p.y:0.0} gear={car.Gear} speed={car.ForwardSpeed:0.0} grounded={car.Grounded} state={state}");
        }

        if (shotIndex < shotZ.Length && p.z > shotZ[shotIndex] && !car.Wrecked)
        {
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"shot{shotIndex}_z{(int)p.z}.png"));
            shotIndex++;
        }

        if (state == "Finished" && !finished)
        {
            Log($"FINISHED  raceTime={timeProvider():0.00}s  maxSpeed={maxSpeed:0.0}  shifts={car.ShiftCount}");
            if (shotIndex < 99) { ScreenCapture.CaptureScreenshot(Path.Combine(dir, "finish.png")); shotIndex = 99; }
            finished = true;
            Invoke(nameof(Quit), TrailerRecorder.Active ? 4.5f : 2f);
        }
        if (t > 240f) { Log("TIMEOUT"); Quit(); }
    }

    Piston[] pistons;
    Spinner[] spinners;

    // Predicts whether the next crusher/blade ahead will be open while we drive through it.
    // Time to cover distance d at full throttle starting from v0 (current gear).
    float TimeTo(float d, float v0)
    {
        if (d <= 0f) return 0f;
        float a = CarController.Accel[car.Gear], top = CarController.TopSpeed[car.Gear];
        float tAcc = Mathf.Max(0f, (top - v0) / a);
        float dAcc = v0 * tAcc + 0.5f * a * tAcc * tAcc;
        if (d <= dAcc) return (-v0 + Mathf.Sqrt(v0 * v0 + 2f * a * d)) / a;
        return tAcc + (d - dAcc) / top;
    }

    bool PathClear(float z, float v, out float dist)
    {
        if (pistons == null) { pistons = FindObjectsByType<Piston>(FindObjectsSortMode.None); spinners = FindObjectsByType<Spinner>(FindObjectsSortMode.None); }
        float front = z + 2.3f, rear = z - 2.3f;
        const float margin = 0.12f, look = 45f;
        dist = 999f;
        bool clear = true;
        float nearest = 999f; bool nearestClear = true;
        foreach (var pi in pistons)
        {
            float pz = pi.closedPos.z;
            float d = pz - 2f - front;
            if (pz + 2f < rear || d > look) continue;
            bool ceiling = pi.openPos.y > pi.closedPos.y;
            float t0 = TimeTo(d, v) - margin, t1 = TimeTo(pz + 2f - rear, v) + margin;
            for (int k = 0; k <= 16; k++)
            {
                float tt = Mathf.Lerp(t0, t1, k / 16f);
                float c = Piston.Closure((WorldClock.Time + tt * WorldClock.Scale) / pi.period + pi.phase);
                if (ceiling ? c > 0.75f : c > 0.005f) { clear = false; break; }
            }
            if (d < nearest) { nearest = d; nearestClear = clear; }
            clear = true;
        }
        foreach (var sp in spinners)
        {
            float sz = sp.transform.position.z;
            float d = sz - 0.6f - front;
            if (sz + 0.6f < rear || d > look) continue;
            float t0 = TimeTo(d, v) - margin, t1 = TimeTo(sz + 0.6f - rear, v) + margin;
            for (int k = 0; k <= 16; k++)
            {
                float tt = Mathf.Lerp(t0, t1, k / 16f);
                float a = Mathf.Repeat(sp.offset + sp.speed * (WorldClock.Time + tt * WorldClock.Scale), 90f);
                if (a < 26f || a > 64f) { clear = false; break; }
            }
            if (d < nearest) { nearest = d; nearestClear = clear; }
            clear = true;
        }
        dist = nearest;
        return nearestClear;
    }

    void Quit() => Application.Quit();
}
