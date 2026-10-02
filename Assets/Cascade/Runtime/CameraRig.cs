using UnityEngine;

namespace Cascade.Runtime
{
    /// <summary>Angled top-down camera. Follows a target; Tab toggles free mode (WASD pans). Right-drag orbits, wheel zooms.</summary>
    public sealed class CameraRig : MonoBehaviour
    {
        public Transform Follow;
        public Vector3 Focus = new Vector3(20f, 0f, 10f);
        public float Distance = 38f;
        public float Yaw = 0f;
        public float Pitch = 60f;
        public bool FreeMode;
        public float PanSpeed = 20f;

        public Camera Camera { get; private set; }

        public static CameraRig Create()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                cam = go.AddComponent<Camera>();
            }
            var rig = cam.gameObject.GetComponent<CameraRig>();
            if (rig == null) rig = cam.gameObject.AddComponent<CameraRig>();
            rig.Camera = cam;
            return rig;
        }

        private void LateUpdate()
        {
            if (CascadeInput.Pressed(InputKey.Tab)) FreeMode = !FreeMode;
            if (CascadeInput.RightHeld)
            {
                var d = CascadeInput.MouseDelta;
                Yaw += d.x * 0.25f;
                Pitch = Mathf.Clamp(Pitch - d.y * 0.25f, 25f, 89f);
            }
            Distance = Mathf.Clamp(Distance - CascadeInput.Scroll * 3f, 8f, 90f);

            if (!FreeMode && Follow != null) Focus = Vector3.Lerp(Focus, Follow.position, 8f * Time.unscaledDeltaTime);
            else
            {
                var move = Vector3.zero;
                if (CascadeInput.Held(InputKey.W)) move.z += 1f;
                if (CascadeInput.Held(InputKey.S)) move.z -= 1f;
                if (CascadeInput.Held(InputKey.D)) move.x += 1f;
                if (CascadeInput.Held(InputKey.A)) move.x -= 1f;
                Focus += Quaternion.Euler(0f, Yaw, 0f) * move * PanSpeed * Time.unscaledDeltaTime;
            }

            var rot = Quaternion.Euler(Pitch, Yaw, 0f);
            transform.position = Focus - rot * Vector3.forward * Distance;
            transform.rotation = rot;
        }
    }
}
