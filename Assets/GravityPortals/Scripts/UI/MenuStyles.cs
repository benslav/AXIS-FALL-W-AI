using UnityEngine;

namespace GravityPortals
{
    /// <summary>Shared look for the menus (built on first use, inside OnGUI).</summary>
    public static class MenuStyles
    {
        static GUIStyle rich, button, big, title, field, panelLabel;
        static Texture2D buttonTex, buttonHoverTex;

        static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        public static GUIStyle Rich => rich ??= new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true, fontSize = 15 };

        public static GUIStyle Button
        {
            get
            {
                if (button != null) return button;
                buttonTex = Solid(new Color(0.12f, 0.16f, 0.22f, 0.95f));
                buttonHoverTex = Solid(new Color(0.0f, 0.55f, 0.75f, 0.95f));
                button = new GUIStyle(GUI.skin.button) { fontSize = 17, fixedHeight = 40, richText = true };
                button.normal.background = buttonTex;
                button.hover.background = buttonHoverTex;
                button.active.background = buttonHoverTex;
                button.normal.textColor = button.hover.textColor = button.active.textColor = Color.white;
                return button;
            }
        }

        public static GUIStyle Big => big ??= new GUIStyle(Button) { fontSize = 20, fixedHeight = 50 };
        public static GUIStyle Title => title ??= new GUIStyle(GUI.skin.label)
            { fontSize = 64, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
        public static GUIStyle Field => field ??= new GUIStyle(GUI.skin.textField) { fontSize = 17, fixedHeight = 32, alignment = TextAnchor.MiddleLeft };
        public static GUIStyle PanelLabel => panelLabel ??= new GUIStyle(Rich) { fontSize = 18 };

        /// <summary>A dark translucent panel.</summary>
        public static void Panel(Rect r)
        {
            GUI.color = new Color(0.02f, 0.03f, 0.05f, 0.82f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = new Color(0f, 0.8f, 1f, 0.6f);
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, 2), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
