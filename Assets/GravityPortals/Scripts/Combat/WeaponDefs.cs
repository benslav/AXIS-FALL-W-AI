using UnityEngine;

namespace GravityPortals
{
    public enum WeaponKind { Hitscan, Projectile }

    /// <summary>Tuning for one weapon. Shared by client (feel) and server (validation, damage).</summary>
    public sealed class WeaponDef
    {
        public string Name;
        public WeaponKind Kind;
        public int MagazineSize;
        public float FireInterval;     // seconds between shots
        public bool Automatic;         // hold to fire
        public float ReloadTime;
        public float Range;            // hitscan
        public int Damage;             // hitscan body damage / projectile max splash damage
        public float HeadshotMultiplier;
        public float Spread;           // degrees, standing still on the ground
        public float MovingSpread;     // degrees, moving or airborne
        public float RecoilUp;         // degrees per shot
        public float RecoilSide;       // max random degrees per shot
        public float ProjectileSpeed;
        public float SplashRadius;
        public int MinSplashDamage;
        public float SelfDamageScale;
        public float Knockback;        // m/s at the center of an explosion
        public float ScopeFov;         // aiming (right mouse) zooms to this field of view
        public bool Scope;             // a real scope: black scope overlay and the gun hidden (else iron sights)
        public float ScopedSpread;     // degrees while scoped and steady
        public float ScopedMovingSpread; // degrees while scoped and moving or airborne
        public float ShotPitch = 1f;   // pitch of the shot sound
        public Color Accent;

        public float ShotsPerSecond => 1f / FireInterval;
    }

    public static class WeaponDefs
    {
        public const int Rifle = 0;
        public const int Launcher = 1;
        public const int Sniper = 2;

        public static readonly WeaponDef[] All =
        {
            new WeaponDef
            {
                Name = "Pulse Rifle", Kind = WeaponKind.Hitscan, MagazineSize = 30, FireInterval = 0.11f,
                Automatic = true, ReloadTime = 1.9f, Range = 250f, Damage = 14, HeadshotMultiplier = 2f,
                Spread = 0.35f, MovingSpread = 1.4f, RecoilUp = 0.75f, RecoilSide = 0.35f,
                ScopeFov = 60f, ScopedSpread = 0.12f, ScopedMovingSpread = 0.9f,
                Accent = new Color(0.2f, 0.9f, 1f),
            },
            new WeaponDef
            {
                Name = "Plasma Launcher", Kind = WeaponKind.Projectile, MagazineSize = 4, FireInterval = 0.85f,
                Automatic = false, ReloadTime = 2.4f, Damage = 90, HeadshotMultiplier = 1f,
                Spread = 0f, MovingSpread = 0f, RecoilUp = 4.5f, RecoilSide = 0.8f, ScopeFov = 65f,
                ProjectileSpeed = 30f, SplashRadius = 4f, MinSplashDamage = 15, SelfDamageScale = 0.4f,
                Knockback = 15f, Accent = new Color(1f, 0.45f, 0.1f),
            },
            new WeaponDef
            {
                // Two body shots kill (2 x 50 = 100 health); a headshot (125) always kills.
                // Inaccurate from the hip; dead on when scoped and steady.
                Name = "Rail Sniper", Kind = WeaponKind.Hitscan, MagazineSize = 4, FireInterval = 1.1f,
                Automatic = false, ReloadTime = 2.6f, Range = 500f, Damage = 50, HeadshotMultiplier = 2.5f,
                Spread = 2.5f, MovingSpread = 5f, ScopedSpread = 0f, ScopedMovingSpread = 0.8f,
                RecoilUp = 5f, RecoilSide = 0.8f, ScopeFov = 22f, Scope = true, ShotPitch = 0.6f,
                Accent = new Color(0.75f, 0.45f, 1f),
            },
        };

        public static WeaponDef Get(int index) => All[Mathf.Clamp(index, 0, All.Length - 1)];
    }
}
