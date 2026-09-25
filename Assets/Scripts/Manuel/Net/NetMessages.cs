using System;
using UnityEngine;

namespace ProjectLEA.Manuel.Net
{
    // ------------------------------------------------------------------
    // Wire format: 4-byte big-endian length, then that many bytes of UTF-8 JSON.
    //
    // JsonUtility has no polymorphism, so every message carries its own type tag in "t".
    // The receiver reads the tag first, then deserialises into the concrete class it names.
    // Nothing here is a MonoBehaviour or a ScriptableObject, so they serialise plainly.
    // ------------------------------------------------------------------

    /// <summary>Only the type tag. Read first, to decide what to deserialise into.</summary>
    [Serializable]
    public sealed class MsgKind
    {
        public string t;
    }

    /// <summary>UDP broadcast from a host. Any client on the same router hears it.</summary>
    [Serializable]
    public sealed class MsgBeacon
    {
        public string t = "beacon";
        public string id;      // process id, so a machine never lists its own lobby
        public string name;    // host's display name
        public string address; // host's LAN address, to connect straight back to
        public int port;
    }

    /// <summary>Sent on connect, so each side knows the other's name.</summary>
    [Serializable]
    public sealed class MsgHello
    {
        public string t = "hello";
        public string name;
    }

    /// <summary>The host's lobby roster, pushed whenever someone joins or leaves.</summary>
    [Serializable]
    public sealed class MsgLobby
    {
        public string t = "lobby";
        public string[] names;
    }

    /// <summary>
    /// Host to client: the match is starting. Carries the whole preset as JSON, so the
    /// client applies the exact same rules without ever having seen the host's Inspector.
    /// </summary>
    [Serializable]
    public sealed class MsgStart
    {
        public string t = "start";
        public string presetJson;
        public string scene;
    }

    /// <summary>One avatar's position and rotation, sent at a fixed rate by its owner.</summary>
    [Serializable]
    public sealed class MsgTransform
    {
        public string t = "tf";
        public float x, y, z;
        public float rx, ry, rz;
    }

    /// <summary>Host to client: the authoritative phase machine moved.</summary>
    [Serializable]
    public sealed class MsgPhase
    {
        public string t = "phase";
        public int state;      // ProjectLEA.Manuel.Managers.MatchState as an int
        public float duration;
        public float remaining;
    }

    /// <summary>Host to client: the whole score and money picture, pushed on any change.</summary>
    [Serializable]
    public sealed class MsgScore
    {
        public string t = "score";
        public int roundsOne, roundsTwo;
        public int killsOne, killsTwo;
        public int deathsOne, deathsTwo;
        public int moneyOne, moneyTwo;
    }

    /// <summary>
    /// Host to client: a round just resolved. Sent alongside the phase change so the other
    /// machine can say who won and how, instead of inferring it from the state alone. The
    /// round counts ride the score message, which is pushed at the same moment.
    /// </summary>
    [Serializable]
    public sealed class MsgRound
    {
        public string t = "round";
        public int winner;      // ProjectLEA.Manuel.Managers.PlayerSlot as an int
        public int reason;      // ProjectLEA.Manuel.Managers.RoundEndReason as an int
    }

    /// <summary>Lobby chat. Flows both ways; each side stamps its own name on what it sends.</summary>
    [Serializable]
    public sealed class MsgChat
    {
        public string t = "chat";
        public string sender;
        public string text;
    }

    /// <summary>
    /// "My local player hit yours for this much." Damage is applied on the victim's own
    /// machine, which is where the authoritative Health for that player lives.
    /// </summary>
    [Serializable]
    public sealed class MsgDamage
    {
        public string t = "damage";
        public float amount;
        public float x, y, z;
    }

    /// <summary>
    /// "My player is looking at / has locked this class." Flows both ways during class select,
    /// so each machine can show the other player hovering and then committing.
    ///
    /// The index is display-only on the receiving end: a class is applied where the body is
    /// owned, which is always the machine that picked it. A browse carries locked = false and
    /// may be superseded at will; a lock is final for the match.
    /// </summary>
    [Serializable]
    public sealed class MsgClassLock
    {
        public string t = "lock";
        public int index;      // -1 while still browsing
        public bool locked;
    }

    /// <summary>"My local player died." Only meaningful from a client; the host's own death
    /// is resolved locally by its own GameManager.</summary>
    [Serializable]
    public sealed class MsgDeath
    {
        public string t = "death";
    }

    /// <summary>
    /// "My player just deployed an ability zone here." Both machines spawn the same zone, so
    /// both players see the smoke and the wall, but each machine's copy only affects the body
    /// that machine owns - which is what keeps the damage from landing twice.
    ///
    /// The effect numbers ride along rather than being looked up from the caster's class, so
    /// the receiving machine does not have to know which class cast it. They were computed on
    /// the caster's machine from the class asset, which both machines have a copy of.
    /// </summary>
    [Serializable]
    public sealed class MsgZone
    {
        public string t = "zone";
        public int kind;        // ProjectLEA.Manuel.Abilities.ZoneKind as an int
        public float x, y, z;   // where the zone was placed
        public float radius;
        public float duration;
        public float tickDamage;
        public float slow;
        public float pull;
    }
}
