using UnityEngine;

/// Procedurally generated audio: no audio files needed (works on WebGL, which has no
/// OnAudioFilterRead). Engine loop, SFX and a short synthwave music loop.
public static class Synth
{
    const int SR = 44100;

    static float Saw(float ph) => 2f * (ph - Mathf.Floor(ph)) - 1f;
    static float Sqr(float ph) => (ph - Mathf.Floor(ph)) < 0.5f ? 1f : -1f;
    static float Tri(float ph) { float p = ph - Mathf.Floor(ph); return 4f * Mathf.Abs(p - 0.5f) - 1f; }
    static float Sin(float ph) => Mathf.Sin(ph * 2f * Mathf.PI);

    static AudioClip Make(string name, float[] data, int sr = SR)
    {
        var c = AudioClip.Create(name, data.Length, 1, sr, false);
        c.SetData(data, 0);
        return c;
    }

    public static AudioClip Engine()
    {
        const int period = 800; // 55.125 Hz fundamental, loops perfectly
        int len = period * 16;
        var d = new float[len];
        for (int i = 0; i < len; i++)
        {
            float ph = i / (float)period;
            float s = Saw(ph) * 0.5f + Sqr(ph * 0.5f) * 0.35f + Sin(ph * 2f) * 0.25f + Saw(ph * 3f) * 0.1f;
            d[i] = (float)System.Math.Tanh(s * 1.4f) * 0.5f;
        }
        return Make("engine", d);
    }

    public static AudioClip Sweep(string name, float f0, float f1, float dur, int wave, float vol = 0.5f)
    {
        int n = (int)(dur * SR);
        var d = new float[n];
        float ph = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            float f = Mathf.Lerp(f0, f1, t);
            ph += f / SR;
            float s = wave == 0 ? Sin(ph) : wave == 1 ? Saw(ph) : wave == 2 ? Sqr(ph) : Tri(ph);
            float env = Mathf.Min(1f, t * 40f) * Mathf.Pow(1f - t, 1.5f);
            d[i] = s * env * vol;
        }
        return Make(name, d);
    }

    public static AudioClip Crash()
    {
        int n = (int)(1.1f * SR);
        var d = new float[n];
        var rng = new System.Random(7);
        float lp = 0f, ph = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            lp += (noise - lp) * 0.25f;
            ph += Mathf.Lerp(90f, 30f, t) / SR;
            float thump = Sin(ph) * Mathf.Exp(-t * 6f);
            d[i] = (lp * Mathf.Exp(-t * 4f) * 0.9f + thump * 0.8f) * 0.7f;
        }
        return Make("crash", d);
    }

    public static AudioClip Thump()
    {
        int n = (int)(0.4f * SR);
        var d = new float[n];
        var rng = new System.Random(3);
        float ph = 0f, lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            ph += Mathf.Lerp(70f, 35f, t / 0.4f) / SR;
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            lp += (noise - lp) * 0.1f;
            d[i] = (Sin(ph) * 0.9f + lp * 0.6f) * Mathf.Exp(-t * 10f) * 0.8f;
        }
        return Make("thump", d);
    }

    public static AudioClip Chime(float f0, float f1)
    {
        int n = (int)(0.6f * SR);
        var d = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float a = Sin(t * f0) * Mathf.Exp(-t * 8f);
            float b = t > 0.09f ? Sin(t * f1) * Mathf.Exp(-(t - 0.09f) * 6f) : 0f;
            d[i] = (a + b) * 0.3f;
        }
        return Make("chime", d);
    }

    // ----------------------------------------------------------------- music

    public static AudioClip Music()
    {
        const int sr = 22050;
        const float bpm = 112f;
        float step = 60f / bpm / 4f;            // 16th note
        int steps = 128;                        // 8 bars
        int len = Mathf.CeilToInt(steps * step * sr);
        var bass = new float[len];
        var lead = new float[len];
        var drums = new float[len];

        // Am - F - C - G  (each chord = 2 bars)
        float[] roots = { 55f, 43.65f, 65.41f, 49f };
        int[][] chord = { new[] { 0, 3, 7, 12 }, new[] { 0, 4, 7, 12 }, new[] { 0, 4, 7, 12 }, new[] { 0, 4, 7, 12 } };
        int[] arp = { 0, 1, 2, 3, 2, 1, 2, 3 };

        var rng = new System.Random(11);
        for (int s = 0; s < steps; s++)
        {
            int c = (s / 32) % 4;
            float root = roots[c];
            float t0 = s * step;

            // bass: driving 8ths with octave bounce
            if (s % 2 == 0)
            {
                float f = (s % 4 == 2) ? root * 2f : root;
                AddTone(bass, sr, t0, step * 1.8f, f, 0.55f, 1, 6f);
            }
            // arpeggio lead, quieter, two octaves up
            int semis = chord[c][arp[s % 8]];
            float lf = root * 4f * Mathf.Pow(2f, semis / 12f);
            AddTone(lead, sr, t0, step * 0.9f, lf, 0.12f, 2, 14f);

            // drums
            if (s % 4 == 0) AddKick(drums, sr, t0);
            if (s % 8 == 4) AddNoise(drums, sr, t0, 0.18f, 0.45f, 18f, rng, 0.35f);
            if (s % 2 == 1) AddNoise(drums, sr, t0, 0.05f, 0.12f, 60f, rng, 0.9f);
        }

        // simple one-pole lowpass on bass for warmth
        float lp = 0f;
        for (int i = 0; i < len; i++) { lp += (bass[i] - lp) * 0.18f; bass[i] = lp; }

        var mix = new float[len];
        for (int i = 0; i < len; i++)
            mix[i] = (float)System.Math.Tanh((bass[i] * 1.2f + lead[i] + drums[i]) * 1.1f) * 0.6f;
        return Make("music", mix, sr);
    }

    static void AddTone(float[] buf, int sr, float start, float dur, float freq, float amp, int wave, float decay)
    {
        int s0 = (int)(start * sr), n = (int)(dur * sr);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)sr;
            float ph = freq * t;
            float w = wave == 0 ? Sin(ph) : wave == 1 ? Saw(ph) : wave == 2 ? Sqr(ph) : Tri(ph);
            float env = Mathf.Min(1f, t * 200f) * Mathf.Exp(-t * decay) * Mathf.Min(1f, (n - i) / (0.01f * sr));
            buf[(s0 + i) % buf.Length] += w * env * amp;
        }
    }

    static void AddKick(float[] buf, int sr, float start)
    {
        int s0 = (int)(start * sr), n = (int)(0.3f * sr);
        float ph = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)sr;
            ph += Mathf.Lerp(150f, 45f, Mathf.Min(1f, t * 12f)) / sr;
            buf[(s0 + i) % buf.Length] += Sin(ph) * Mathf.Exp(-t * 12f) * 0.8f;
        }
    }

    static void AddNoise(float[] buf, int sr, float start, float dur, float amp, float decay, System.Random rng, float hp)
    {
        int s0 = (int)(start * sr), n = (int)(dur * sr);
        float prev = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)sr;
            float x = (float)(rng.NextDouble() * 2.0 - 1.0);
            float y = x - prev * hp; prev = x; // crude highpass
            buf[(s0 + i) % buf.Length] += y * Mathf.Exp(-t * decay) * amp;
        }
    }
}
