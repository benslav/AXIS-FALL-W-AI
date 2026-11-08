using System.Collections.Generic;
using System.IO;
using UnityEngine.SceneManagement;

namespace GravityPortals
{
    /// <summary>The playable maps: every scene in the build except the menu and Unity's template.
    /// Same order on every machine running the same build, so map numbers can be sent over the network.</summary>
    public static class MapList
    {
        public const string MenuScene = "MainMenu";
        static List<(string name, string scene, string path)> maps;

        public static IReadOnlyList<(string name, string scene, string path)> All
        {
            get
            {
                if (maps != null) return maps;
                maps = new List<(string, string, string)>();
                for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
                {
                    string path = SceneUtility.GetScenePathByBuildIndex(i);
                    string scene = Path.GetFileNameWithoutExtension(path);
                    if (scene == MenuScene || scene == "SampleScene") continue;
                    maps.Add((Pretty(scene), scene, path));
                }
                return maps;
            }
        }

        public static string Pretty(string sceneName)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < sceneName.Length; i++)
            {
                char c = sceneName[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(sceneName[i - 1])) sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString().Replace("Tri Gravity", "Tri-Gravity");
        }

        public static bool InMenu => SceneManager.GetActiveScene().name == MenuScene;
    }
}
