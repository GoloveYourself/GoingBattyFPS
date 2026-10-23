using System.Collections.Generic;
using UnityEngine;

namespace GoingBatty
{
    /// <summary>
    /// The player you control: Quake-style movement (bunny hop, air strafe), the one-shot
    /// blunderbuss (left click), blast jump (right click), and voice-driven sonar.
    /// </summary>
    public class LocalPlayer : MonoBehaviour
    {
        public byte Slot;
        public bool Alive { get; private set; }
        public string KilledBy { get; private set; } = "";
        public float DiedAt { get; private set; }
        public float FireReady01 => Mathf.Clamp01(1f - (_nextFire - Time.time) / Tuning.FireCooldown);
        public float BlastReady01 => Mathf.Clamp01(1f - (_nextBlast - Time.time) / Tuning.BlastJumpCooldown);
        public Vector3 Velocity => _vel;
        public float LastSonarStrength { get; private set; }
        public float LastSonarTime { get; private set; } = -10;

        CharacterController _cc;
        Transform _head;
        Camera _cam;
        Transform _gun;
        Vector3 _gunRest;
        Vector3 _vel;
        float _yaw, _pitch;
        float _nextFire, _nextBlast, _nextSonar, _sendTimer;
        bool _frozen, _jumpQueued, _grounded;
        float _recoil;

        void Awake()
        {
            _cc = gameObject.AddComponent<CharacterController>();
            _cc.height = Tuning.PlayerHeight;
            _cc.radius = Tuning.PlayerRadius;
            _cc.center = new Vector3(0, Tuning.PlayerHeight * 0.5f, 0);
            _cc.stepOffset = 0.35f;
            _cc.slopeLimit = 50f;
            _cc.skinWidth = 0.04f;
            _cc.minMoveDistance = 0f;

            _head = new GameObject("Head").transform;
            _head.SetParent(transform, false);
            _head.localPosition = new Vector3(0, Tuning.EyeHeight, 0);

            _cam = Bootstrap.MainCamera;
            _cam.transform.SetParent(_head, false);
            _cam.transform.localPosition = Vector3.zero;
            _cam.transform.localRotation = Quaternion.identity;
            _cam.nearClipPlane = 0.03f;

            BuildViewModel();
            SetAlive(false);
        }

        void BuildViewModel()
        {
            // A blunderbuss drawn only as outlines, like the world.
            _gun = new GameObject("Blunderbuss").transform;
            _gun.SetParent(_cam.transform, false);
            _gunRest = new Vector3(0.22f, -0.2f, 0.42f);
            _gun.localPosition = _gunRest;
            var mat = Look.Line(new Color(0.75f, 0.62f, 0.45f));
            void Part(string n, Vector3 size, Vector3 pos, Vector3 euler)
            {
                var go = new GameObject(n);
                go.transform.SetParent(_gun, false);
                go.transform.localPosition = pos;
                go.transform.localEulerAngles = euler;
                go.AddComponent<MeshFilter>().sharedMesh = Look.BoxEdges(size);
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            }
            Part("Barrel", new Vector3(0.06f, 0.06f, 0.55f), new Vector3(0, 0, 0.1f), Vector3.zero);
            Part("Bell", new Vector3(0.15f, 0.15f, 0.08f), new Vector3(0, 0, 0.4f), new Vector3(0, 0, 45f));
            Part("Bell2", new Vector3(0.12f, 0.12f, 0.06f), new Vector3(0, 0, 0.35f), Vector3.zero);
            Part("Stock", new Vector3(0.07f, 0.12f, 0.3f), new Vector3(0, -0.05f, -0.28f), new Vector3(12f, 0, 0));
            Part("Hammer", new Vector3(0.02f, 0.05f, 0.04f), new Vector3(0, 0.05f, -0.12f), Vector3.zero);
        }

        Vector3 MuzzleWorld => _gun.TransformPoint(new Vector3(0, 0, 0.45f));

        // ------------------------------------------------------------- spawn / death

        public void Spawn(Vector3 pos, float yaw)
        {
            _cc.enabled = false;
            transform.position = pos;
            _cc.enabled = true;
            _vel = Vector3.zero;
            _yaw = yaw; _pitch = 0;
            _frozen = false;
            SetAlive(true);
            Sfx.Ui(Sfx.Respawn, 0.6f);
        }

        public void Die(string killer)
        {
            KilledBy = killer;
            DiedAt = Time.time;
            SetAlive(false);
            Sfx.Ui(Sfx.Death, 0.8f);
        }

        public void Freeze() { _frozen = true; _vel = Vector3.zero; }

        void SetAlive(bool alive)
        {
            Alive = alive;
            if (_gun) _gun.gameObject.SetActive(alive);
        }

        // ------------------------------------------------------------- update

        void Update()
        {
            bool canControl = UIRoot.I == null || UIRoot.I.GameplayInputAllowed;

            if (canControl)
            {
                var look = InputWrap.Look() * Settings.MouseSensitivity * 2.2f;
                _yaw += look.x;
                _pitch = Mathf.Clamp(_pitch - look.y, -89f, 89f);
            }
            transform.rotation = Quaternion.Euler(0, _yaw, 0);
            _head.localRotation = Quaternion.Euler(_pitch, 0, 0);
            _cam.fieldOfView = Settings.Fov;

            if (Alive && !_frozen)
            {
                Move(canControl);
                if (canControl) Weapon();
                Sonar(canControl);
            }

            // Viewmodel recoil
            _recoil = Mathf.MoveTowards(_recoil, 0, Time.deltaTime * 2.5f);
            if (_gun)
            {
                float bob = _grounded ? Mathf.Sin(Time.time * 9f) * Mathf.Min(1, new Vector2(_vel.x, _vel.z).magnitude / Tuning.GroundSpeed) * 0.01f : 0;
                _gun.localPosition = _gunRest + new Vector3(0, bob, -_recoil * 0.12f);
                _gun.localRotation = Quaternion.Euler(-_recoil * 18f, 0, 0);
            }

            _sendTimer -= Time.deltaTime;
            if (_sendTimer <= 0 && GameSession.I != null)
            {
                _sendTimer = 1f / Tuning.StateSendRate;
                GameSession.I.SendState(transform.position, _yaw, _pitch, _vel);
            }
        }

        // ------------------------------------------------------------- movement (Quake-style)

        void Move(bool canControl)
        {
            float dt = Time.deltaTime;
            Vector2 input = canControl ? InputWrap.Move() : Vector2.zero;
            bool jumpHeld = canControl && InputWrap.Key(InputWrap.KeyName.Space);
            if (canControl && InputWrap.KeyDown(InputWrap.KeyName.Space)) _jumpQueued = true;

            Vector3 wish = transform.right * input.x + transform.forward * input.y;
            float wishSpeed = wish.magnitude * Tuning.GroundSpeed;
            if (wish.sqrMagnitude > 0) wish.Normalize();

            bool wantJump = _jumpQueued || (Settings.AutoHop && jumpHeld);

            if (_grounded)
            {
                if (wantJump)
                {
                    // No friction on the jump frame: this is what makes bunny hopping keep speed.
                    _vel.y = Tuning.JumpSpeed;
                    _grounded = false;
                    _jumpQueued = false;
                    Accelerate(wish, wishSpeed, Tuning.AirAccel, dt, Tuning.AirSpeedCap);
                }
                else
                {
                    ApplyFriction(dt);
                    Accelerate(wish, wishSpeed, Tuning.GroundAccel, dt, float.MaxValue);
                    _vel.y = -2f; // stick to slopes/ramps
                }
            }
            else
            {
                Accelerate(wish, wishSpeed, Tuning.AirAccel, dt, Tuning.AirSpeedCap);
                _vel.y -= Tuning.Gravity * dt;
            }

            var h = new Vector3(_vel.x, 0, _vel.z);
            if (h.magnitude > Tuning.MaxHorizontalSpeed)
            {
                h = h.normalized * Tuning.MaxHorizontalSpeed;
                _vel.x = h.x; _vel.z = h.z;
            }

            var flags = _cc.Move(_vel * dt);
            if ((flags & CollisionFlags.Above) != 0 && _vel.y > 0) _vel.y = 0;
            bool wasGrounded = _grounded;
            _grounded = _cc.isGrounded && _vel.y <= 0.1f;
            if (_grounded && !wasGrounded && _vel.y < -2f) _vel.y = -2f;
            if (!jumpHeld && _grounded) _jumpQueued = false;

            if (transform.position.y < -20f) transform.position = Vector3.up * 2f; // safety net
        }

        void ApplyFriction(float dt)
        {
            var h = new Vector3(_vel.x, 0, _vel.z);
            float speed = h.magnitude;
            if (speed < 0.01f) { _vel.x = _vel.z = 0; return; }
            float control = Mathf.Max(speed, Tuning.StopSpeed);
            float newSpeed = Mathf.Max(0, speed - control * Tuning.Friction * dt);
            float k = newSpeed / speed;
            _vel.x *= k; _vel.z *= k;
        }

        void Accelerate(Vector3 wishDir, float wishSpeed, float accel, float dt, float cap)
        {
            if (wishSpeed <= 0) return;
            float capped = Mathf.Min(wishSpeed, cap);
            float current = Vector3.Dot(new Vector3(_vel.x, 0, _vel.z), wishDir);
            float add = capped - current;
            if (add <= 0) return;
            float acc = Mathf.Min(accel * wishSpeed * dt, add);
            _vel.x += wishDir.x * acc;
            _vel.z += wishDir.z * acc;
        }

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            // Clip velocity against walls so you slide instead of sticking (Quake's ClipVelocity).
            float into = Vector3.Dot(_vel, hit.normal);
            if (into < 0 && hit.normal.y < 0.7f) _vel -= hit.normal * into;
        }

        // ------------------------------------------------------------- weapon

        void Weapon()
        {
            if (InputWrap.FireDown() && Time.time >= _nextFire) Fire();
            if (InputWrap.AltFireDown() && Time.time >= _nextBlast) BlastJump();
        }

        void Fire()
        {
            _nextFire = Time.time + Tuning.FireCooldown;
            _recoil = 1f;
            Vector3 origin = _cam.transform.position;
            Vector3 dir = _cam.transform.forward;
            int seed = Random.Range(int.MinValue, int.MaxValue);
            var hits = new List<byte>();
            var ends = new List<Vector3>();
            foreach (var pd in PelletDirections(dir, seed))
            {
                Vector3 end = origin + pd * Tuning.WeaponRange;
                if (Physics.Raycast(origin, pd, out var hit, Tuning.WeaponRange, ~0, QueryTriggerInteraction.Ignore))
                {
                    end = hit.point;
                    var rp = hit.collider.GetComponentInParent<RemotePlayer>();
                    if (rp != null && rp.Alive && !hits.Contains(rp.Slot)) hits.Add(rp.Slot);
                }
                ends.Add(end);
            }
            // Friendly fire is off in team mode (the host also enforces it)
            if (GameSession.I.IsTeamMode)
            {
                var me = GameSession.I.LocalInfo;
                hits.RemoveAll(s => GameSession.I.Players.TryGetValue(s, out var p) && me != null && p.Team == me.Team);
            }

            var color = GameSession.I.SonarColorFor(Slot);
            SonarSystem.I.ShotFx(MuzzleWorld, ends, color);
            SonarSystem.I.Pulse(origin, dir, 0.18f, color, Slot, isLocal: true);
            Sfx.Ui(Sfx.Gunshot, 0.9f);
            GameSession.I.SendShot(origin, dir, seed, hits);
            foreach (var s in hits) GameSession.I.Remote(s)?.FlashHit();
        }

        /// <summary>Deterministic pellet spread, so other players draw the same pattern.</summary>
        public static IEnumerable<Vector3> PelletDirections(Vector3 forward, int seed)
        {
            var rng = new System.Random(seed);
            var rot = Quaternion.LookRotation(forward);
            yield return forward;
            for (int i = 1; i < Tuning.Pellets; i++)
            {
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                float rad = Mathf.Sqrt((float)rng.NextDouble()) * Tuning.PelletSpreadDeg;
                var local = Quaternion.Euler(Mathf.Sin(ang) * rad, Mathf.Cos(ang) * rad, 0) * Vector3.forward;
                yield return rot * local;
            }
        }

        void BlastJump()
        {
            _nextBlast = Time.time + Tuning.BlastJumpCooldown;
            _recoil = 0.6f;
            Vector3 dir = _cam.transform.forward;
            // Push away from where you aim; aiming down launches you up.
            _vel += -dir * Tuning.BlastJumpForce;
            if (_grounded && _vel.y < Tuning.JumpSpeed * 0.6f) _vel.y = Mathf.Max(_vel.y, Tuning.JumpSpeed * 0.6f);
            _grounded = false;
            var pos = MuzzleWorld;
            var color = GameSession.I.SonarColorFor(Slot);
            SonarSystem.I.BlastFx(pos, dir, color);
            SonarSystem.I.Pulse(_cam.transform.position, dir, 0.3f, color, Slot, isLocal: true);
            Sfx.Ui(Sfx.BlastJump, 0.9f);
            GameSession.I.SendBlast(pos, dir);
        }

        // ------------------------------------------------------------- sonar from your voice

        void Sonar(bool canControl)
        {
            float level = MicInput.I != null ? MicInput.I.Level : 0f;
            if (Settings.KeyboardSonar && canControl && InputWrap.Key(InputWrap.KeyName.Q)) level = Mathf.Max(level, 0.45f);

            if (level < Tuning.TalkThreshold || Time.time < _nextSonar) return;

            bool scream = level >= Tuning.ScreamThreshold;
            float strength = scream
                ? Mathf.Lerp(Tuning.ScreamStrength, 1f, Mathf.InverseLerp(Tuning.ScreamThreshold, 1f, level))
                : Mathf.Lerp(0.25f, Tuning.ScreamStrength - 0.01f, Mathf.InverseLerp(Tuning.TalkThreshold, Tuning.ScreamThreshold, level));
            _nextSonar = Time.time + (scream ? Tuning.ScreamCooldown : Tuning.TalkCooldown);

            Vector3 origin = _cam.transform.position;
            Vector3 dir = _cam.transform.forward;
            SonarSystem.I.Pulse(origin, dir, strength, GameSession.I.SonarColorFor(Slot), Slot, isLocal: true);
            GameSession.I.SendSonar(origin, dir, strength);
            LastSonarStrength = strength;
            LastSonarTime = Time.time;
        }

        void OnDestroy()
        {
            if (_cam != null && _cam.transform.parent == _head) Bootstrap.ReturnCameraToMenu();
        }
    }
}
