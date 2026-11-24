using UnityEngine;

namespace FlameBlame
{
    /// <summary>Keyboard + mouse control. WASD move, mouse aim, LMB/Space spray, E use breaker.</summary>
    public class PlayerController : MonoBehaviour
    {
        public Firefighter Actor;
        public Camera Cam;

        void Update()
        {
            if (Actor == null || Actor.Game == null) return;
            if (!Actor.Game.IsPlaying)
            {
                Actor.MoveInput = Vector2.zero;
                Actor.Spraying = false;
                return;
            }

            Actor.MoveInput = InputCompat.Move();

            Ray ray = Cam.ScreenPointToRay(InputCompat.MousePosition());
            var ground = new Plane(Vector3.up, Vector3.zero);
            if (ground.Raycast(ray, out float enter)) Actor.AimPoint = ray.GetPoint(enter);

            Actor.Spraying = InputCompat.SprayHeld();
            if (InputCompat.InteractPressed()) Actor.Game.TryInteract(Actor);
        }
    }
}
