using UnityEngine;

namespace GoingBatty
{
    /// <summary>
    /// Another player. Invisible (everyone is blind) except when revealed by sonar or gunfire.
    /// Holds their hitbox and their proximity-voice audio source.
    /// </summary>
    public class RemotePlayer : MonoBehaviour
    {
        public byte Slot;
        public bool Alive { get; private set; }
        public VoiceOut Voice { get; private set; }

        CapsuleCollider _col;
        Vector3 _target, _vel;
        float _yaw, _targetYaw;
        float _lastStateTime;
        bool _hasState;

        void Awake()
        {
            _col = gameObject.AddComponent<CapsuleCollider>();
            _col.height = Tuning.PlayerHeight;
            _col.radius = Tuning.PlayerRadius + 0.05f; // slightly generous hitbox
            _col.center = new Vector3(0, Tuning.PlayerHeight * 0.5f, 0);

            var mouth = new GameObject("Voice");
            mouth.transform.SetParent(transform, false);
            mouth.transform.localPosition = new Vector3(0, Tuning.EyeHeight, 0);
            mouth.AddComponent<AudioSource>();
            Voice = mouth.AddComponent<VoiceOut>();
            SetAlive(false);
        }

        public void SetAlive(bool alive)
        {
            Alive = alive;
            if (_col) _col.enabled = alive;
        }

        public void ReceiveState(Vector3 pos, float yaw, float pitch, Vector3 vel)
        {
            _target = pos;
            _targetYaw = yaw;
            _vel = vel;
            _lastStateTime = Time.time;
            if (!_hasState) { transform.position = pos; _yaw = yaw; _hasState = true; }
        }

        public void Teleport(Vector3 pos, float yaw)
        {
            _target = pos; transform.position = pos;
            _yaw = _targetYaw = yaw;
            _vel = Vector3.zero;
            _hasState = true;
        }

        public void FlashHit()
        {
            // Small feedback for the shooter: a bright ghost where the hit landed.
            SonarSystem.I.ShowGhost(transform.position, transform.rotation, Color.white, 0.5f);
        }

        void Update()
        {
            if (!_hasState) return;
            // Extrapolate briefly with the last velocity, then ease toward the target.
            float since = Mathf.Min(Time.time - _lastStateTime, 0.15f);
            Vector3 predicted = _target + new Vector3(_vel.x, 0, _vel.z) * since;
            transform.position = Vector3.Lerp(transform.position, predicted, 1f - Mathf.Exp(-18f * Time.deltaTime));
            if ((transform.position - predicted).sqrMagnitude > 25f) transform.position = predicted;
            _yaw = Mathf.LerpAngle(_yaw, _targetYaw, 1f - Mathf.Exp(-20f * Time.deltaTime));
            transform.rotation = Quaternion.Euler(0, _yaw, 0);
        }
    }
}
