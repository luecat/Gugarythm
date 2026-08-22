using UnityEngine.SceneManagement;

namespace Gugarhythm
{
    /// <summary>
    /// Centralises the three player-facing scene routes so UI code does not
    /// scatter scene names or depend on build indices.
    /// </summary>
    public static class GugarhythmSceneRouter
    {
        public const string LibraryScene = "LibraryScene";
        public const string SettingsScene = "SettingsScene";
        public const string ChartEditorScene = "ChartEditorScene";
        public const string GameplayScene = "RhythmPrototype";

        public static bool IsLibrary => SceneManager.GetActiveScene().name == LibraryScene;
        public static bool IsSettings => SceneManager.GetActiveScene().name == SettingsScene;
        public static bool IsChartEditor => SceneManager.GetActiveScene().name == ChartEditorScene;
        public static bool IsGameplay => SceneManager.GetActiveScene().name == GameplayScene;

        public static void OpenLibrary() => SceneManager.LoadScene(LibraryScene, LoadSceneMode.Single);
        public static void OpenSettings() => SceneManager.LoadScene(SettingsScene, LoadSceneMode.Single);
        public static void OpenChartEditor() => SceneManager.LoadScene(ChartEditorScene, LoadSceneMode.Single);
        public static void OpenGameplay() => SceneManager.LoadScene(GameplayScene, LoadSceneMode.Single);
    }
}
