using UnityEngine;

namespace GravityPortals
{
    /// <summary>
    /// Frames per second and frame time, top centre of the screen, in every scene.
    /// F3 shows or hides it (remembered between sessions). Averaged over half a second so
    /// the number is readable; green at 60+, yellow at 30+, red below.
    /// </summary>
    public class FpsCounter : MonoBehaviour
    {
        const string PrefKey = "GravityPortals.ShowFps";

        bool show;
        int frames;
        float elapsed, fps, ms, worstMs, worstThisWindow;
        GUIStyle style;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (Application.isBatchMode) return; // headless servers have no screen
            var go = new GameObject("FPS Counter");
            DontDestroyOnLoad(go);
            go.AddComponent<FpsCounter>();
        }

        void Awake() => show = PlayerPrefs.GetInt(PrefKey, 1) == 1;

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F3))
            {
                show = !show;
                PlayerPrefs.SetInt(PrefKey, show ? 1 : 0);
            }

            float dt = Time.unscaledDeltaTime;
            frames++;
            elapsed += dt;
            worstThisWindow = Mathf.Max(worstThisWindow, dt * 1000f);
            if (elapsed >= 0.5f)
            {
                fps = frames / elapsed;
                ms = elapsed * 1000f / frames;
                worstMs = worstThisWindow;
                frames = 0;
                elapsed = 0f;
                worstThisWindow = 0f;
            }
        }

        void OnGUI()
        {
            if (!show || fps <= 0f) return;
            if (style == null)
                style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                    wordWrap = false, clipping = TextClipping.Overflow, padding = new RectOffset(0, 0, 0, 0),
                };

            // Worst frame in the last half second shows stutters an average hides.
            var text = new GUIContent($"{fps:0} FPS   {ms:0.0} ms   (worst {worstMs:0} ms)");
            // Size the box to the text (plus padding), so nothing is ever cut off.
            Vector2 size = style.CalcSize(text);
            float w = size.x + 24f, h = size.y + 8f;
            var rect = new Rect(Mathf.Round(Screen.width * 0.5f - w * 0.5f), 6f, w, h);
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = fps >= 60f ? new Color(0.45f, 1f, 0.55f) : fps >= 30f ? new Color(1f, 0.85f, 0.3f) : new Color(1f, 0.4f, 0.35f);
            GUI.Label(rect, text, style);
            GUI.color = Color.white;
        }
    }
}
