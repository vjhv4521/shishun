using System.Collections;
using System.Collections.Generic;
using Haven.Framework.Scenes;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SurvivalEngine
{

    //Script to manage transitions between scenes
    public class SceneNav
    {
        public static void RestartLevel()
        {
            SceneTransitionService.LoadScene(SceneManager.GetActiveScene().name);
        }

        public static void GoTo(string scene)
        {
            SceneTransitionService.LoadScene(scene);
        }

        public static string GetCurrentScene()
        {
            return SceneManager.GetActiveScene().name;
        }

        public static bool DoSceneExist(string scene)
        {
            return Application.CanStreamedLevelBeLoaded(scene);
        }
    }

}
