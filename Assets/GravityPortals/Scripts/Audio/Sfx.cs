using System.Collections.Generic;
using UnityEngine;

namespace GravityPortals
{
    /// <summary>Fire-and-forget sound playback from a small pool of audio sources.</summary>
    public static class Sfx
    {
        const int PoolSize = 24;
        static readonly List<AudioSource> pool = new List<AudioSource>();
        static int next;
        static Transform root;

        public static float MasterVolume = 0.8f;

        /// <summary>Non-positional (your own gun, hit markers, UI).</summary>
        public static void Play2D(AudioClip clip, float volume = 1f, float pitch = 1f)
        {
            var s = Get();
            if (s == null || clip == null) return;
            s.transform.position = Vector3.zero;
            s.spatialBlend = 0f;
            Start(s, clip, volume, pitch);
        }

        /// <summary>Positional, fades with distance.</summary>
        public static void Play3D(AudioClip clip, Vector3 position, float volume = 1f, float pitch = 1f, float maxDistance = 45f)
        {
            var s = Get();
            if (s == null || clip == null) return;
            s.transform.position = position;
            s.spatialBlend = 1f;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = 1.5f;
            s.maxDistance = maxDistance;
            Start(s, clip, volume, pitch);
        }

        static void Start(AudioSource s, AudioClip clip, float volume, float pitch)
        {
            s.clip = clip;
            s.volume = volume * MasterVolume;
            s.pitch = pitch;
            s.Play();
        }

        static AudioSource Get()
        {
            if (Application.isBatchMode) return null;
            if (root == null)
            {
                pool.Clear();
                var go = new GameObject("Sfx Pool");
                Object.DontDestroyOnLoad(go);
                root = go.transform;
                for (int i = 0; i < PoolSize; i++)
                {
                    var child = new GameObject($"Voice {i}");
                    child.transform.SetParent(root, false);
                    var src = child.AddComponent<AudioSource>();
                    src.playOnAwake = false;
                    src.dopplerLevel = 0f;
                    pool.Add(src);
                }
            }

            // Prefer an idle voice; otherwise steal the oldest one.
            for (int i = 0; i < pool.Count; i++)
            {
                var s = pool[(next + i) % pool.Count];
                if (!s.isPlaying) { next = (next + i + 1) % pool.Count; return s; }
            }
            var steal = pool[next];
            next = (next + 1) % pool.Count;
            return steal;
        }
    }
}
