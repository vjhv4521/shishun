using UnityEngine;

namespace Haven.Camp
{
    public sealed class CampQuestNameplate : MonoBehaviour
    {
        private void LateUpdate()
        {
            var camera = Camera.main;
            if (camera) transform.rotation = camera.transform.rotation;
        }
    }
}
