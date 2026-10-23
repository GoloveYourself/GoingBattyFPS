namespace GoingBatty
{
    /// <summary>
    /// Every gameplay number in one place, so feel can be tuned quickly in future prompts.
    /// Movement is Quake-style (friction, ground accel, air strafing, bunny hopping) and the
    /// weapon is a one-shot hitscan with a short cooldown plus a separate "blast jump" button,
    /// in the spirit of Ratz Instagib.
    /// </summary>
    public static class Tuning
    {
        // ---- Movement (metres, seconds) ----
        public const float GroundSpeed = 8.5f;     // top running speed
        public const float GroundAccel = 14f;
        public const float Friction = 7f;
        public const float StopSpeed = 2.5f;
        public const float AirAccel = 12f;         // high = strong air strafing
        public const float AirSpeedCap = 0.9f;     // Quake's 30u wish cap, scaled
        public const float MaxHorizontalSpeed = 24f;
        public const float JumpSpeed = 7.0f;
        public const float Gravity = 20f;
        public const float PlayerHeight = 1.8f;
        public const float PlayerRadius = 0.4f;
        public const float EyeHeight = 1.62f;

        // ---- Blunderbuss ----
        public const float FireCooldown = 1.0f;
        public const int Pellets = 9;
        public const float PelletSpreadDeg = 3.5f;  // cone half-angle
        public const float WeaponRange = 40f;
        public const float BlastJumpCooldown = 1.1f;
        public const float BlastJumpForce = 12.5f;

        // ---- Respawn ----
        public const float RespawnDelay = 2.0f;
        public const float SpawnProtection = 1.0f;

        // ---- Sonar ----
        public const float SonarSpeed = 30f;          // wavefront speed m/s
        public const float TalkRangeMin = 9f, TalkRangeMax = 18f;
        public const float ScreamRangeMin = 26f, ScreamRangeMax = 42f;
        public const float TalkConeDeg = 32f, ScreamConeDeg = 58f;
        public const int TalkRays = 170, ScreamRays = 460;
        public const float TalkCooldown = 0.38f, ScreamCooldown = 0.95f;
        public const float EchoLifetime = 1.7f;
        public const float GhostLifetime = 1.5f;
        public const float ScreamStrength = 0.75f;    // strength >= this is a "scream" blast

        // Normalised mic level (0..1, after sensitivity) thresholds
        public const float TalkThreshold = 0.22f;
        public const float ScreamThreshold = 0.72f;

        // ---- Capture the Box ----
        public const float BoxPickupRadius = 1.5f;
        public const float BoxAutoReturn = 20f;

        // ---- Networking ----
        public const float StateSendRate = 20f;
        public const int MaxPlayers = 8;
    }
}
