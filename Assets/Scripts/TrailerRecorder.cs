using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

/// Trailer capture mode, enabled with "-trailer <dir>" (implies the autopilot driver).
/// Renders at a fixed 30 fps game clock, saves every frame as a JPG, logs per-frame car
/// state and gameplay events, exports the game's synthesized audio as WAVs, and drives
/// a few cinematic fixed cameras. tools/make_trailer.py turns this into an MP4.
public class TrailerRecorder : MonoBehaviour
{
    public static bool Active { get; private set; }
    public const int Fps = 30;

    public CarController car;
    public ChaseCamera cam;
    public System.Func<string> stateProvider;

    string dir, frameDir;
    int frame, tick;
    readonly StringBuilder frames = new StringBuilder("frame,z,x,y,gear,speed,grounded,worldScale,wrecked,state,shot\n");
    readonly StringBuilder events = new StringBuilder("frame,type,arg\n");
    string shot = "chase";

    public static string ArgDir()
    {
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++)
            if (a[i] == "-trailer") return a[i + 1];
        return null;
    }

    public static void ActivateIfRequested()
    {
        Active = ArgDir() != null;
        if (Active) Time.captureFramerate = Fps * 2; // simulate at 60, keep every other frame
    }

    void Start()
    {
        dir = ArgDir();
        frameDir = Path.Combine(dir, "frames");
        Directory.CreateDirectory(frameDir);
        ExportAudio();

        car.Shifted += (a, b) => Event(b > a ? "shiftup" : "shiftdown", b);
        car.Wreck += () => Event("wreck", 0);
        car.Landed += air => Event("land", Mathf.RoundToInt(air * 100f));
        Piston.Slammed += OnSlam;
        StartCoroutine(Capture());
    }

    void OnDestroy() { Piston.Slammed -= OnSlam; Flush(); }

    void OnSlam(Vector3 pos)
    {
        float d = Vector3.Distance(pos, car.transform.position);
        if (d < 60f) Event("slam", Mathf.RoundToInt((1f - d / 60f) * 100f));
    }

    void Event(string type, int arg) => events.AppendLine($"{frame},{type},{arg}");

    // ------------------------------------------------------------------ cinematic cameras

    void Update()
    {
        float z = car.transform.position.z;
        string state = stateProvider();
        cam.useFixed = false;
        shot = "chase";
        if (state == "Racing" && !car.Wrecked)
        {
            if (z > 30f && z < 78f) Fixed("flip", new Vector3(4.5f, 2.5f, 84f));
            else if (z > 330f && z < 377f && WorldClock.Scale < 0.6f) Fixed("crusher", new Vector3(7.5f, 10.5f, 384f));
            else if (z > 700f && z < 800f) Fixed("jump1", new Vector3(5f, 1.5f, 724f));
        }
    }

    void Fixed(string name, Vector3 pos)
    {
        cam.useFixed = true;
        cam.fixedPos = pos;
        shot = name;
    }

    // ------------------------------------------------------------------ capture

    IEnumerator Capture()
    {
        var wait = new WaitForEndOfFrame();
        while (true)
        {
            yield return wait;
            tick++;
            if (tick % 2 == 1) continue;
            var tex = ScreenCapture.CaptureScreenshotAsTexture(2);
            File.WriteAllBytes(Path.Combine(frameDir, $"f{frame:00000}.jpg"), tex.EncodeToJPG(92));
            Destroy(tex);

            var p = car.transform.position;
            frames.AppendLine(string.Join(",",
                frame, p.z.ToString("0.00"), p.x.ToString("0.00"), p.y.ToString("0.00"), car.Gear,
                car.ForwardSpeed.ToString("0.00"), car.Grounded ? 1 : 0, WorldClock.Scale.ToString("0.000"),
                car.Wrecked ? 1 : 0, stateProvider(), shot));
            frame++;
            if (frame % 150 == 0) Flush();
        }
    }

    void Flush()
    {
        if (dir == null) return;
        File.WriteAllText(Path.Combine(dir, "frames.csv"), frames.ToString());
        File.WriteAllText(Path.Combine(dir, "events.csv"), events.ToString());
    }

    // ------------------------------------------------------------------ audio export

    void ExportAudio()
    {
        string a = Path.Combine(dir, "audio");
        Directory.CreateDirectory(a);
        Wav(a, "music", Synth.Music());
        Wav(a, "engine", Synth.Engine());
        for (int g = 1; g <= 4; g++) Wav(a, "shift" + g, Synth.Sweep("shift" + g, 300f + g * 120f, 700f + g * 220f, 0.16f, 2, 0.35f));
        Wav(a, "shiftdown", Synth.Sweep("shiftdown", 700f, 260f, 0.16f, 2, 0.3f));
        Wav(a, "crash", Synth.Crash());
        Wav(a, "thump", Synth.Thump());
        Wav(a, "chime", Synth.Chime(659.3f, 1318.5f));
    }

    static void Wav(string dir, string name, AudioClip clip)
    {
        var data = new float[clip.samples * clip.channels];
        clip.GetData(data, 0);
        using var fs = new FileStream(Path.Combine(dir, name + ".wav"), FileMode.Create);
        using var w = new BinaryWriter(fs);
        int bytes = data.Length * 2;
        w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes);
        w.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16); w.Write((short)1); w.Write((short)clip.channels);
        w.Write(clip.frequency); w.Write(clip.frequency * clip.channels * 2); w.Write((short)(clip.channels * 2)); w.Write((short)16);
        w.Write(Encoding.ASCII.GetBytes("data")); w.Write(bytes);
        foreach (float f in data) w.Write((short)Mathf.Clamp(f * 32767f, -32768f, 32767f));
    }
}
