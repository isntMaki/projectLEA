using System;
using UnityEngine;

namespace ProjectLEA.Manuel.Abilities
{
    /// <summary>
    /// What a class ability actually DOES, chosen from a shared library so that 29 classes can
    /// have distinct kits without 29 bespoke prefabs.
    ///
    /// The class asset only carries data - which archetype, plus the tuning numbers in
    /// <see cref="ProjectLEA.Manuel.Managers.AbilityDefinition"/>. <see cref="ProjectLEA.Manuel.Managers.ClassAbilityHost"/>
    /// maps the archetype to the script that implements it and adds that script to the player
    /// when the class is picked.
    ///
    /// The archetypes split into three groups by how they reach the other machine:
    ///
    ///   Self        - Dash, Updraft, Teleport, HealSelf, ShieldSelf, BuffSelf, Decoy.
    ///                 Only ever affect the player who cast them, so they are entirely local
    ///                 and never touch the network.
    ///   Caster-side - Mine, Projectile, Reveal. These exist only on the machine that cast
    ///                 them and damage the opponent through the existing damage relay, exactly
    ///                 like a gunshot: the caster computes the amount, the victim applies it.
    ///                 The victim sees no visual for them yet - placeholder behaviour until the
    ///                 models and VFX arrive.
    ///   Mirrored    - Zone. Both machines spawn the same zone, but a zone only ever affects
    ///                 the body its OWN machine is authoritative for, so the damage lands
    ///                 exactly once and both players can see the smoke, the wall and the blast.
    /// </summary>
    public enum AbilityArchetype
    {
        None = 0,

        // --- Self -------------------------------------------------------
        /// <summary>A burst of horizontal speed along the look direction. power = distance.</summary>
        Dash,
        /// <summary>Launches the player straight up. power = height.</summary>
        Updraft,
        /// <summary>Instantly moves the player to the aimed point. power = max range.</summary>
        Teleport,
        /// <summary>Restores the caster's health over time. power = total health restored.</summary>
        HealSelf,
        /// <summary>Temporary damage absorption layered over health. power = shield amount.</summary>
        ShieldSelf,
        /// <summary>Movement and handling buff for a duration. power = bonus move speed.</summary>
        BuffSelf,
        /// <summary>Spawns a standing decoy body at the aimed point that draws fire.</summary>
        Decoy,

        // --- Caster-side (damage relayed like a gunshot) -----------------
        /// <summary>Proximity explosive placed at the aimed point.</summary>
        Mine,
        /// <summary>An explosive round along the aim ray with splash on impact.</summary>
        Projectile,
        /// <summary>Marks the opponent through walls for the duration. Caster-side only.</summary>
        Reveal,

        // --- Mirrored zone ----------------------------------------------
        /// <summary>Deploys a zone at the aimed point. <see cref="ZoneKind"/> picks the effect.</summary>
        Zone
    }

    /// <summary>
    /// What a <see cref="AbilityArchetype.Zone"/> ability deploys. All of these are mirrored:
    /// both machines spawn the zone, and the zone only affects the body its own machine owns,
    /// which keeps the effect single-application across the wire.
    /// </summary>
    [Serializable]
    public enum ZoneKind
    {
        None = 0,
        /// <summary>Blocks line of sight. No game effect.</summary>
        Smoke,
        /// <summary>A solid barrier with a collider.</summary>
        Wall,
        /// <summary>Deals damage over time to bodies inside.</summary>
        Damage,
        /// <summary>Slows bodies inside for as long as they remain.</summary>
        Slow,
        /// <summary>Drags bodies towards the zone's centre.</summary>
        Pull,
        /// <summary>Stops abilities from being cast while inside.</summary>
        Suppress,
        /// <summary>Blinds a player who is inside when it lands.</summary>
        Flash
    }

    /// <summary>
    /// Placeholder visuals for abilities. Everything here is built from primitives because the
    /// real models and effects are not part of this pass - see the class summary on
    /// <see cref="AbilityArchetype"/>.
    /// </summary>
    public static class AbilityVisuals
    {
        /// <summary>
        /// A translucent sphere that marks a zone's volume. Materials are cloned from an
        /// existing renderer in the scene rather than created from a shader name, so this
        /// never depends on a shader being in the build's always-included list.
        /// </summary>
        /// <param name="center">World position of the zone's centre.</param>
        /// <param name="radius">Radius in meters.</param>
        /// <param name="color">Tint, alpha included.</param>
        /// <param name="solidCollider">True to keep the primitive's collider (a wall).</param>
        public static GameObject SpawnSphereMarker(Vector3 center, float radius, Color color,
                                                   bool solidCollider = false)
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            body.name = "AbilityMarker";

            body.transform.position = center;
            body.transform.localScale = Vector3.one * Mathf.Max(0.1f, radius * 2f);

            if (!solidCollider)
            {
                var collider = body.GetComponent<Collider>();
                if (collider != null) UnityEngine.Object.Destroy(collider);
            }

            ApplyTint(body, color);
            return body;
        }

        /// <summary>A solid box barrier, scaled to the given size.</summary>
        public static GameObject SpawnBoxMarker(Vector3 center, Vector3 size, Color color)
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "AbilityBarrier";

            body.transform.position = center;
            body.transform.localScale = Vector3.Max(Vector3.one * 0.1f, size);

            ApplyTint(body, color);
            return body;
        }

        /// <summary>Colours a primitive by cloning whatever material the scene is already using.</summary>
        public static void ApplyTo(Renderer renderer, Color color)
        {
            if (renderer == null) return;

            var source = FindSharedMaterial();
            if (source == null) return;

            // Clone, never mutate the shared asset - the arena floor would change colour too.
            var material = new Material(source);
            material.color = color;

            if (color.a < 0.999f)
            {
                // URP/Lit switches between opaque and transparent on this keyword. If the
                // shader in use does not support it the material simply stays opaque, which
                // still reads as a clear zone boundary.
                try
                {
                    material.SetFloat("_Surface", 1f);
                    material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                }
                catch (Exception)
                {
                    // Stays opaque; harmless.
                }
            }

            renderer.sharedMaterial = material;
        }

        private static void ApplyTint(GameObject body, Color color) => ApplyTo(body.GetComponent<Renderer>(), color);

        /// <summary>Borrows a material from anything already visible in the scene. Cached, since a
        /// full scene scan on every ability cast would be a visible hitch.</summary>
        private static Material _shared;
        private static bool _sharedResolved;

        private static Material FindSharedMaterial()
        {
            if (_sharedResolved) return _shared;
            _sharedResolved = true;

            var renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            if (renderers == null) return null;

            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                if (renderer is SpriteRenderer) continue;

                var material = renderer.sharedMaterial;
                if (material != null && material.shader != null)
                {
                    _shared = material;
                    return _shared;
                }
            }

            return null;
        }
    }
}
