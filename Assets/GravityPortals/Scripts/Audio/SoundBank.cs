using System;
using UnityEngine;

namespace GravityPortals
{
    /// <summary>
    /// Every sound in the game, synthesized at startup (no audio files needed).
    /// Each clip is built once, the first time it's used.
    /// </summary>
    public static class SoundBank
    {
        const int Rate = 44100;

        static AudioClip rifleShot, launcherShot, explosion, portalWhoosh, gravityShift, hitMarker,
            killConfirm, hurt, footstep, land, jump, click, empty, portalHum, death;

        public static AudioClip RifleShot => rifleShot ??= Make("RifleShot", 0.16f, RifleShotSample);
        public static AudioClip LauncherShot => launcherShot ??= Make("LauncherShot", 0.45f, LauncherSample);
        public static AudioClip Explosion => explosion ??= MakeFiltered("Explosion", 1.4f, ExplosionSample, 0.06f);
        public static AudioClip PortalWhoosh => portalWhoosh ??= MakeFiltered("PortalWhoosh", 0.55f, WhooshSample, 0.0f);
        public static AudioClip GravityShift => gravityShift ??= Make("GravityShift", 0.6f, GravitySample);
        public static AudioClip HitMarker => hitMarker ??= Make("HitMarker", 0.06f, t => Sine(t, 1900f) * Mathf.Exp(-t * 70f) * 0.6f);
        public static AudioClip KillConfirm => killConfirm ??= Make("KillConfirm", 0.3f, KillSample);
        public static AudioClip Hurt => hurt ??= Make("Hurt", 0.22f, HurtSample);
        public static AudioClip Footstep => footstep ??= MakeFiltered("Footstep", 0.09f, t => Noise() * Mathf.Exp(-t * 55f), 0.12f);
        public static AudioClip Land => land ??= MakeFiltered("Land", 0.2f, t => (Noise() * 0.6f + Sine(t, 70f)) * Mathf.Exp(-t * 25f), 0.08f);
        public static AudioClip Jump => jump ??= MakeFiltered("Jump", 0.14f, t => Noise() * Mathf.Sin(Mathf.PI * t / 0.14f) * 0.5f, 0.2f);
        public static AudioClip Click => click ??= Make("Click", 0.035f, t => (Noise() * 0.6f + Sine(t, 3200f) * 0.4f) * Mathf.Exp(-t * 160f));
        public static AudioClip Empty => empty ??= Make("Empty", 0.05f, t => Sine(t, 900f) * Mathf.Exp(-t * 120f) * 0.5f);
        public static AudioClip PortalHum => portalHum ??= Make("PortalHum", 2f, HumSample);
        public static AudioClip Death => death ??= Make("Death", 0.7f, DeathSample);

        // ------------------------------------------------------------------ voices

        static float RifleShotSample(float t)
        {
            float crack = Noise() * Mathf.Exp(-t * 45f);
            float zap = Sine(t, Mathf.Lerp(1400f, 180f, Mathf.Clamp01(t * 9f))) * Mathf.Exp(-t * 28f);
            float thump = Sine(t, 90f) * Mathf.Exp(-t * 35f);
            return Crush(crack * 0.55f + zap * 0.45f + thump * 0.5f, 24f);
        }

        static float LauncherSample(float t)
        {
            float sweep = Sine(t, Mathf.Lerp(320f, 55f, Mathf.Clamp01(t * 3f))) * Mathf.Exp(-t * 7f);
            float hiss = Noise() * Mathf.Exp(-t * 14f) * 0.4f;
            float wobble = Sine(t, 18f) * 0.15f + 1f;
            return (sweep * wobble + hiss) * 0.8f;
        }

        static float ExplosionSample(float t)
        {
            float rumble = Noise() * Mathf.Exp(-t * 3.2f);
            float boom = Sine(t, Mathf.Lerp(80f, 35f, Mathf.Clamp01(t * 2f))) * Mathf.Exp(-t * 4f);
            return rumble * 0.8f + boom * 0.9f;
        }

        static float WhooshSample(float t)
        {
            float env = Mathf.Sin(Mathf.PI * t / 0.55f);
            float tone = Sine(t, Mathf.Lerp(200f, 600f, t / 0.55f)) * 0.25f;
            return (Noise() * 0.7f + tone) * env * env;
        }

        static float GravitySample(float t)
        {
            float f = Mathf.Lerp(160f, 440f, Mathf.SmoothStep(0f, 1f, t / 0.6f));
            float env = Mathf.Sin(Mathf.PI * t / 0.6f);
            return (Sine(t, f) * 0.6f + Sine(t, f * 1.5f) * 0.25f + Sine(t, f * 3f) * 0.1f) * env * 0.6f;
        }

        static float KillSample(float t)
        {
            float a = t < 0.12f ? Sine(t, 880f) : 0f;
            float b = t >= 0.1f ? Sine(t, 1320f) : 0f;
            return (a + b) * Mathf.Exp(-t * 9f) * 0.5f;
        }

        static float HurtSample(float t) =>
            (Sine(t, Mathf.Lerp(150f, 80f, t / 0.22f)) * 0.8f + Noise() * 0.3f) * Mathf.Exp(-t * 14f);

        static float HumSample(float t)
        {
            // Every component completes whole cycles in 2 s, so the loop is seamless.
            float am = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 0.5f * t);
            return (Sine(t, 70f) * 0.6f + Sine(t, 140f) * 0.25f + Sine(t, 211f) * 0.08f) * am * 0.5f;
        }

        static float DeathSample(float t) =>
            (Sine(t, Mathf.Lerp(600f, 60f, t / 0.7f)) * 0.6f + Noise() * 0.25f) * Mathf.Exp(-t * 4f);

        // ------------------------------------------------------------------ building blocks

        static System.Random rng = new System.Random(1234);
        static float Noise() => (float)(rng.NextDouble() * 2.0 - 1.0);

        // Plain sine. With a frequency that changes over time it gives a zappy chirp, which suits sci-fi.
        static float Sine(float t, float frequency) => Mathf.Sin(2f * Mathf.PI * frequency * t);

        static float Crush(float x, float steps) => Mathf.Round(x * steps) / steps;

        static AudioClip Make(string name, float seconds, Func<float, float> sample)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = sample(i / (float)Rate);
            return Finish(name, data);
        }

        /// <summary>Like Make, then a one-pole low-pass (smaller = darker, 0 = sweeping filter).</summary>
        static AudioClip MakeFiltered(string name, float seconds, Func<float, float> sample, float cutoff)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            float y = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                // cutoff 0: sweep the filter open then closed (whoosh).
                float a = cutoff > 0f ? cutoff : Mathf.Lerp(0.02f, 0.35f, Mathf.Sin(Mathf.PI * t / seconds));
                y += a * (sample(t) - y);
                data[i] = y;
            }
            return Finish(name, data);
        }

        static AudioClip Finish(string name, float[] data)
        {
            // Normalize to a safe peak and fade the last few ms to avoid clicks.
            float peak = 0.0001f;
            for (int i = 0; i < data.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            float gain = 0.9f / peak;
            int fade = Mathf.Min(data.Length / 4, 220);
            for (int i = 0; i < data.Length; i++)
            {
                data[i] *= gain;
                int fromEnd = data.Length - 1 - i;
                if (fromEnd < fade) data[i] *= fromEnd / (float)fade;
            }
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
