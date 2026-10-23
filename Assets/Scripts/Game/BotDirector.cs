using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GoingBatty
{
    /// <summary>
    /// Runs the AI bots. Host only — bots live on the host and look like normal players to everyone.
    /// Bots play by the same rules as people: they only know where you are if you were revealed
    /// (by any sonar pulse, by firing, by carrying the Box) or if you're very close. They "speak"
    /// sonar pulses on a timer, roam the map on a grid path, and fire with a reaction delay and
    /// imperfect aim. Difficulty numbers live in Tuning (Bot*).
    /// </summary>
    public class BotDirector : MonoBehaviour
    {
        public static BotDirector I { get; private set; }

        class Brain
        {
            public byte Slot;
            public bool Spawned;
            public Vector3 Pos, Vel;
            public float Yaw;
            public List<Vector3> Path = new List<Vector3>();
            public int PathIdx;
            public Vector3 Goal;
            public float NextRepath, NextSonar, NextFire;
            public byte Target = 255;
            public float SeenSince = -1;
            public float StuckCheckAt;
            public Vector3 StuckCheckPos;
            public float StrafeSign = 1, NextStrafeFlip;
        }

        readonly Dictionary<byte, Brain> _brains = new Dictionary<byte, Brain>();
        // Where each player was last revealed, and when (shared knowledge among bots).
        readonly Dictionary<byte, (Vector3 pos, float time)> _revealed = new Dictionary<byte, (Vector3, float)>();
        float _sendTimer;

        void Awake() { I = this; }

        GameSession GS => GameSession.I;
        MapBuilder Map => GS.Map;

        // ------------------------------------------------------------------ perception events

        /// <summary>Any sonar pulse (human or bot) reveals players inside its cone with line of sight.</summary>
        public void NoteSonar(Vector3 origin, Vector3 dir, float strength, byte owner)
        {
            if (GS == null || !Net.IsHost) return;
            bool scream = strength >= Tuning.ScreamStrength;
            float range = scream
                ? Mathf.Lerp(Tuning.ScreamRangeMin, Tuning.ScreamRangeMax, Mathf.InverseLerp(Tuning.ScreamStrength, 1f, strength))
                : Mathf.Lerp(Tuning.TalkRangeMin, Tuning.TalkRangeMax, strength / Tuning.ScreamStrength);
            float cone = scream ? Tuning.ScreamConeDeg : Tuning.TalkConeDeg;
            // The speaker gives themselves away too.
            if (GS.Players.TryGetValue(owner, out var speaker)) Reveal(speaker);
            foreach (var p in GS.Players.Values)
            {
                if (!p.Alive || p.Slot == owner) continue;
                Vector3 chest = p.Pos + Vector3.up * 1.1f;
                Vector3 to = chest - origin;
                if (to.magnitude > range || Vector3.Angle(dir, to) > cone) continue;
                if (HasLineOfSight(origin, chest, p.Slot)) Reveal(p);
            }
        }

        public void NoteShot(byte shooter)
        {
            if (GS != null && GS.Players.TryGetValue(shooter, out var p)) Reveal(p);
        }

        void Reveal(PlayerInfo p) => _revealed[p.Slot] = (p.Pos, Time.time);

        bool HasLineOfSight(Vector3 from, Vector3 to, byte targetSlot)
        {
            if (!Physics.Linecast(from, to, out var hit, ~0, QueryTriggerInteraction.Ignore)) return true;
            var rp = hit.collider.GetComponentInParent<RemotePlayer>();
            if (rp != null) return rp.Slot == targetSlot;
            var local = GS.Local;
            return local != null && targetSlot == GS.LocalSlot && hit.collider.transform.IsChildOf(local.transform);
        }

        // ------------------------------------------------------------------ tick

        void Update()
        {
            if (GS == null || !Net.IsHost || !GS.InSession || GS.Phase != Phase.Playing || Map == null)
            {
                if (_brains.Count > 0) _brains.Clear();
                if (_revealed.Count > 0) _revealed.Clear();
                return;
            }

            // Keep a brain per bot
            foreach (var slot in _brains.Keys.ToList())
                if (!GS.Players.TryGetValue(slot, out var bp) || !bp.IsBot) _brains.Remove(slot);
            foreach (var p in GS.Players.Values)
                if (p.IsBot && !_brains.ContainsKey(p.Slot)) _brains[p.Slot] = new Brain { Slot = p.Slot };

            // Box carriers are always visible
            for (int t = 0; t < 2; t++)
            {
                var b = GS.Boxes[t];
                if (b.Status == BoxStatus.Carried && GS.Players.TryGetValue(b.CarrierSlot, out var c)) Reveal(c);
            }

            _sendTimer -= Time.deltaTime;
            bool send = _sendTimer <= 0;
            if (send) _sendTimer = 1f / Tuning.StateSendRate;

            foreach (var brain in _brains.Values)
            {
                if (!GS.Players.TryGetValue(brain.Slot, out var me)) continue;
                if (!me.Alive) { brain.Spawned = false; continue; }
                if (!brain.Spawned) OnSpawned(brain, me);
                Think(brain, me);
                if (send) GS.HostBotState(me, brain.Pos, brain.Yaw, brain.Vel);
            }
        }

        void OnSpawned(Brain b, PlayerInfo me)
        {
            b.Spawned = true;
            b.Pos = me.Pos;
            b.Yaw = me.Yaw;
            b.Vel = Vector3.zero;
            b.Path.Clear();
            b.Target = 255;
            b.SeenSince = -1;
            b.NextSonar = Time.time + Random.Range(0.4f, 1.5f);
            b.NextFire = Time.time + Tuning.SpawnProtection;
            b.StuckCheckAt = Time.time + 2f;
            b.StuckCheckPos = b.Pos;
        }

        void Think(Brain b, PlayerInfo me)
        {
            float now = Time.time;
            Vector3 eye = b.Pos + Vector3.up * Tuning.EyeHeight;

            // Close enemies can be heard/felt even without sonar
            foreach (var p in GS.Players.Values)
                if (p.Alive && IsEnemy(me, p) && (p.Pos - b.Pos).sqrMagnitude < Tuning.BotHearingRange * Tuning.BotHearingRange
                    && HasLineOfSight(eye, p.Pos + Vector3.up * 1.1f, p.Slot))
                    Reveal(p);

            // Pick the nearest enemy revealed recently
            PlayerInfo target = null;
            float bestD = float.MaxValue;
            foreach (var p in GS.Players.Values)
            {
                if (!p.Alive || !IsEnemy(me, p)) continue;
                if (!_revealed.TryGetValue(p.Slot, out var rv) || now - rv.time > Tuning.BotMemory) continue;
                float d = (rv.pos - b.Pos).sqrMagnitude;
                if (d < bestD) { bestD = d; target = p; }
            }

            Vector3 lookDir = b.Vel.sqrMagnitude > 0.1f ? new Vector3(b.Vel.x, 0, b.Vel.z) : Quaternion.Euler(0, b.Yaw, 0) * Vector3.forward;
            bool engaging = false;

            if (target != null)
            {
                if (b.Target != target.Slot) { b.Target = target.Slot; b.SeenSince = -1; }
                Vector3 aimPoint = target.Pos + Vector3.up * 1.1f;
                bool los = (aimPoint - eye).magnitude < Tuning.WeaponRange && HasLineOfSight(eye, aimPoint, target.Slot);
                if (los)
                {
                    if (b.SeenSince < 0) b.SeenSince = now;
                    engaging = true;
                    lookDir = aimPoint - eye;
                    // Track the target's real position while we can see it
                    Reveal(target);
                    if (now - b.SeenSince >= Tuning.BotReaction && now >= b.NextFire)
                    {
                        Vector3 aim = Quaternion.Euler(Random.Range(-1f, 1f) * Tuning.BotAimErrorDeg, Random.Range(-1f, 1f) * Tuning.BotAimErrorDeg, 0) * (aimPoint - eye).normalized;
                        GS.HostBotShoot(me, eye, aim);
                        b.NextFire = now + Tuning.FireCooldown + Random.Range(0.1f, 0.5f);
                    }
                }
                else
                {
                    b.SeenSince = -1;
                    SetGoal(b, _revealed[target.Slot].pos);
                }
            }
            else
            {
                b.Target = 255;
                b.SeenSince = -1;
                SetGoal(b, ObjectiveGoal(b, me));
            }

            // Movement
            Vector3 move = Vector3.zero;
            if (engaging)
            {
                // Strafe while fighting; close distance if far away
                if (now >= b.NextStrafeFlip) { b.StrafeSign = Random.value < 0.5f ? -1 : 1; b.NextStrafeFlip = now + Random.Range(0.6f, 1.4f); }
                Vector3 flat = new Vector3(lookDir.x, 0, lookDir.z).normalized;
                Vector3 side = Vector3.Cross(Vector3.up, flat) * b.StrafeSign;
                move = side * 0.8f + (bestD > 100f ? flat * 0.7f : Vector3.zero);
                Vector3 probe = b.Pos + move.normalized * 1.0f;
                if (!Map.IsOpen(Map.CellOf(probe).x, Map.CellOf(probe).y)) { b.StrafeSign *= -1; move = -side * 0.8f; }
                move = Vector3.ClampMagnitude(move, 1f) * Tuning.BotSpeed * 0.8f;
            }
            else move = FollowPath(b) * Tuning.BotSpeed;

            Vector3 next = b.Pos + move * Time.deltaTime;
            var cell = Map.CellOf(next);
            if (Map.IsOpen(cell.x, cell.y))
            {
                float floor = Map.FloorHeight(next);
                // Don't walk off ledges / up walls when strafing: only accept small height changes
                if (Mathf.Abs(floor - b.Pos.y) < 0.6f) { next.y = floor; b.Pos = next; }
            }
            b.Vel = move;

            if (lookDir.sqrMagnitude > 0.001f)
            {
                float targetYaw = Mathf.Atan2(lookDir.x, lookDir.z) * Mathf.Rad2Deg;
                b.Yaw = Mathf.MoveTowardsAngle(b.Yaw, targetYaw, 540f * Time.deltaTime);
            }

            // Speak a sonar pulse now and then (screaming sometimes, and when they've lost you)
            if (now >= b.NextSonar)
            {
                bool scream = Random.value < 0.22f;
                float strength = scream ? Random.Range(0.8f, 1f) : Random.Range(0.3f, 0.6f);
                Vector3 dir = Quaternion.Euler(0, b.Yaw, 0) * Vector3.forward;
                GS.HostBotSonar(me, eye, dir, strength);
                b.NextSonar = now + (engaging ? Random.Range(2.0f, 3.5f) : Random.Range(1.0f, 2.6f));
            }

            // Unstick
            if (now >= b.StuckCheckAt)
            {
                if (!engaging && b.Path.Count > 0 && (b.Pos - b.StuckCheckPos).sqrMagnitude < 0.25f)
                {
                    b.Path.Clear();
                    b.Goal = Map.RandomOpenPoint();
                    b.NextRepath = 0;
                }
                b.StuckCheckAt = now + 2f;
                b.StuckCheckPos = b.Pos;
            }
        }

        bool IsEnemy(PlayerInfo me, PlayerInfo other)
            => other.Slot != me.Slot && (!GS.IsTeamMode || other.Team != me.Team);

        Vector3 ObjectiveGoal(Brain b, PlayerInfo me)
        {
            if (!GS.IsTeamMode)
            {
                // Deathmatch: roam
                if (b.Path.Count == 0 || b.PathIdx >= b.Path.Count) return Map.RandomOpenPoint();
                return b.Goal;
            }

            int mine = me.Team, theirs = 1 - me.Team;
            var enemyBox = GS.Boxes[theirs];
            var myBox = GS.Boxes[mine];

            if (enemyBox.Status == BoxStatus.Carried && enemyBox.CarrierSlot == me.Slot) return Map.BoxHome[mine];   // run it home
            if (myBox.Status == BoxStatus.Dropped) return myBox.Pos;                                                // return our box
            if (myBox.Status == BoxStatus.Carried && GS.Players.TryGetValue(myBox.CarrierSlot, out var thief)) return thief.Pos; // chase thief
            bool attacker = me.Slot % 3 != 0; // about two thirds attack, one third defend
            if (attacker && enemyBox.Status != BoxStatus.Carried) return enemyBox.Pos;                              // go grab theirs
            if (enemyBox.Status == BoxStatus.Carried && GS.Players.TryGetValue(enemyBox.CarrierSlot, out var mate)) return mate.Pos; // escort
            // Defend: wander near our base
            if (b.Path.Count == 0 || b.PathIdx >= b.Path.Count)
            {
                for (int i = 0; i < 8; i++)
                {
                    var p = Map.RandomOpenPoint();
                    if ((p - Map.BoxHome[mine]).sqrMagnitude < 14f * 14f) return p;
                }
                return Map.BoxHome[mine];
            }
            return b.Goal;
        }

        void SetGoal(Brain b, Vector3 goal)
        {
            bool far = (goal - b.Goal).sqrMagnitude > 4f;
            if (!far && b.Path.Count > 0 && b.PathIdx < b.Path.Count && Time.time < b.NextRepath) return;
            if (!far && Time.time < b.NextRepath) return;
            b.Goal = goal;
            b.Path = Map.FindPath(b.Pos, goal);
            b.PathIdx = 0;
            b.NextRepath = Time.time + 1.5f;
        }

        Vector3 FollowPath(Brain b)
        {
            while (b.PathIdx < b.Path.Count)
            {
                Vector3 wp = b.Path[b.PathIdx];
                Vector3 d = new Vector3(wp.x - b.Pos.x, 0, wp.z - b.Pos.z);
                if (d.magnitude < 0.35f) { b.PathIdx++; continue; }
                return d.normalized;
            }
            return Vector3.zero;
        }
    }
}
