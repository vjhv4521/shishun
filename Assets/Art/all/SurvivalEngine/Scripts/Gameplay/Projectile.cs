using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivalEngine
{
    /// <summary>
    /// A projectile shot with a ranged weapon
    /// </summary>

    public class Projectile : MonoBehaviour
    {
        public float speed = 10f;
        public float duration = 10f;
        public float gravity = 0.2f;
        public float initial_curve = 0.2f;

        public AudioClip shoot_sound;
		
		[HideInInspector]
        public int damage = 0; //Will be replaced by weapon damage

        [HideInInspector]
        public Vector3 dir;  //Direction the projectile is moving to

        [HideInInspector]
        public float distance = 10f; //Distance the projectile is trying to reach

        [HideInInspector]
        public PlayerCharacter player_shooter;

        [HideInInspector]
        public Destructible shooter;

        private float timer = 0f;

        void Start()
        {
            TheAudio.Get().PlaySFX("projectile", shoot_sound);
            dir += Vector3.up * initial_curve * distance / 100f;
        }

        void Update()
        {
            if (TheGame.Get().IsPaused())
                return;

            //Regular dir
            transform.position += dir * speed * Time.deltaTime;
            dir += gravity * Vector3.down * Time.deltaTime;
            transform.rotation = Quaternion.LookRotation(dir.normalized, Vector2.up);

            timer += Time.deltaTime;
            if (timer > duration)
                Destroy(gameObject);
        }

        private void OnTriggerEnter(Collider collision)
        {
            Destructible destruct = collision.GetComponent<Destructible>();
            if (destruct != null && !destruct.attack_melee_only)
            {
                if(player_shooter != null)
                    destruct.TakeDamage(player_shooter, damage);
                else if (shooter != null)
                    destruct.TakeDamage(shooter, damage);
                else
                    destruct.TakeDamage(damage);
                Destroy(gameObject);
            }

            PlayerCharacterCombat player = collision.GetComponent<PlayerCharacterCombat>();
            if (player != null && (player_shooter == null || player_shooter.Combat != player))
            {
                player.TakeDamage(damage);
                Destroy(gameObject);
            }
        }
    }

}