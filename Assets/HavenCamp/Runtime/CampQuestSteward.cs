using SurvivalEngine;
using UnityEngine;

namespace Haven.Camp
{
    [RequireComponent(typeof(Selectable))]
    public sealed class CampQuestSteward : MonoBehaviour
    {
        private Selectable _selectable;
        private void Awake()
        {
            _selectable = GetComponent<Selectable>();
            _selectable.onUse += Interact;
        }

        private void Interact(PlayerCharacter player)
        {
            if (player && !player.IsDead() && Vector3.Distance(player.transform.position, transform.position) <= 3f)
                FindAnyObjectByType<CampQuestPanel>()?.Open();
        }

        private void OnDestroy()
        {
            if (_selectable) _selectable.onUse -= Interact;
        }
    }
}
