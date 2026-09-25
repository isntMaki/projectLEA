#!/usr/bin/env python3
"""Generates the 29 class assets for the Valorant-style roster.

Each class is a ClassDefinition ScriptableObject whose abilities are drawn from the shared
archetype library, so the kits are data rather than bespoke prefabs. Renamed from the real
agents - same role and kit archetype, different name - as requested.

Run with the managed Python; it writes Assets/Data/Manuel/Classes/*.asset plus .meta files,
and rewrites the ClassManager roster in the scene.

Enum ordinals must match the C# declarations:
  ClassRole     Duelist 0, Initiator 1, Controller 2, Sentinel 3
  AbilityArchetype None 0, Dash 1, Updraft 2, Teleport 3, HealSelf 4, ShieldSelf 5,
                BuffSelf 6, Decoy 7, Mine 8, Projectile 9, Reveal 10, Zone 11
  ZoneKind      None 0, Smoke 1, Wall 2, Damage 3, Slow 4, Pull 5, Suppress 6, Flash 7
  Key           A=15 .. Z=40  (verified against the scene's reloadKey: 32 == R)
"""

import os
import uuid

ROOT = r"c:\Dev\projectLEA"
OUT_DIR = os.path.join(ROOT, "Assets", "Data", "Manuel", "Classes")
CLASS_GUID = "3515046cad7f28e4187fc9a43cea57ae"   # ClassDefinition.cs
SCENE = os.path.join(ROOT, "Assets", "Scenes", "Manuel.unity")

DUELIST, INITIATOR, CONTROLLER, SENTINEL = 0, 1, 2, 3
NONE, DASH, UPDRAFT, TELEPORT, HEAL, SHIELD, BUFF, DECOY, MINE, PROJECTILE, REVEAL, ZONE = range(12)
Z_NONE, SMOKE, WALL, DAMAGE, SLOW, PULL, SUPPRESS, FLASH = range(8)
KEY_C, KEY_Q, KEY_E, KEY_X = 17, 31, 19, 38


def ab(display, desc, cooldown, power, kind, zone=Z_NONE, dur=0.0,
       range_=0.0, radius=0.0, ultimate=False, key=KEY_C):
    return dict(display=display, desc=desc, cd=cooldown, dur=dur, power=power,
                kind=kind, zone=zone, range=range_, radius=radius, ult=ultimate, key=key)


CLASSES = [
    # ---------------------------------------------------------------- DUELISTS
    dict(name="Kestrel", role=DUELIST,
         desc="An agile duelist who controls the vertical space. Dash in, strike from above, and vanish behind a smoke.",
         pas="Momentum: dashes and launches cover more distance",
         neg="Featherweight: shields absorb 20% less",
         kit=[
             ab("Cloudburst", "Deploys a smoke sphere that blocks line of sight.", 12, 0, ZONE, SMOKE, dur=4.5, range_=22, radius=4.5),
             ab("Updraft", "Launches you straight up.", 8, 1.4, UPDRAFT, key=KEY_Q),
             ab("Tailwind", "A burst of speed along your look direction.", 9, 1.6, DASH, key=KEY_E),
             ab("Blade Storm", "Speed and fire rate for the duration.", 45, 0.30, BUFF, dur=6, ultimate=True, key=KEY_X),
         ]),
    dict(name="Sangria", role=DUELIST,
         desc="A self-sufficient duelist who sustains on aggression. Every kill feeds her escape and her health.",
         pas="Devour: healing is 25% stronger",
         neg="Selfish: no utility for a teammate",
         kit=[
             ab("Leer", "A blind that lands where you aim.", 10, 0, ZONE, FLASH, dur=2.0, range_=18, radius=5),
             ab("Dismiss", "Vanish and reappear a short distance away.", 8, 0, TELEPORT, range_=6, key=KEY_Q),
             ab("Devour", "Restores health over a short window.", 6, 50, HEAL, dur=2, key=KEY_E),
             ab("Empress", "Fire rate, speed and sustain for the duration.", 50, 0.35, BUFF, dur=8, ultimate=True, key=KEY_X),
         ]),
    dict(name="Pyre", role=DUELIST,
         desc="A duelist who trades in fire. Blocks angles with a wall of flame and heals through his own blaze.",
         pas="Warmth: standing in your own fire heals instead of hurts",
         neg="Reckless: ability damage to self is not fully mitigated",
         kit=[
             ab("Blaze", "A solid wall of flame that blocks movement and sight.", 10, 0, ZONE, WALL, dur=6, range_=14),
             ab("Hot Hands", "Restores health over a short window.", 10, 40, HEAL, dur=2, key=KEY_Q),
             ab("Curveball", "A blind that curves around the corner it is thrown past.", 9, 0, ZONE, FLASH, dur=2.0, range_=20, radius=6, key=KEY_E),
             ab("Second Wind", "A surge of health when you need it most.", 60, 100, HEAL, dur=2, ultimate=True, key=KEY_X),
         ]),
    dict(name="Bombard", role=DUELIST,
         desc="A duelist with too much ordnance. Booms a bot, blasts herself in, and finishes with a rocket.",
         pas="Munitions: explosive damage is 15% higher",
         neg="Loud: every ability announces where you are",
         kit=[
             ab("Boom Bot", "A proximity explosive that chases the ground you point it at.", 12, 60, MINE, range_=20, radius=3.5),
             ab("Blast Pack", "A burst of speed from the concussive charge at your feet.", 8, 1.3, DASH, key=KEY_Q),
             ab("Paint Shells", "Cluster explosives along your aim line.", 10, 55, PROJECTILE, range_=25, radius=3, key=KEY_E),
             ab("Showstopper", "One rocket. Make it count.", 45, 90, PROJECTILE, range_=30, radius=4.5, ultimate=True, key=KEY_X),
         ]),
    dict(name="Kitsune", role=DUELIST,
         desc="A trickster duelist of illusions and misdirection. Never where the enemy thinks you are.",
         pas="Unreadable: your decoy lasts longer",
         neg="Delicate: shields absorb 20% less",
         kit=[
             ab("Blindside", "A blind that lands where you aim.", 9, 0, ZONE, FLASH, dur=2.0, range_=18, radius=5.5),
             ab("Gatecrash", "Step through a rift to the point you aim at.", 12, 0, TELEPORT, range_=12, key=KEY_Q),
             ab("Fakeout", "A decoy body that draws fire and reveals who shoots it.", 20, 0, DECOY, range_=18, key=KEY_E),
             ab("Dimensional Drift", "Slip between dimensions to anywhere you can see.", 50, 0, TELEPORT, range_=25, ultimate=True, key=KEY_X),
         ]),
    dict(name="Surge", role=DUELIST,
         desc="A duelist built for speed. Outpaces every angle and punishes anyone caught in her lane.",
         pas="Fast Lane: movement buffs are 20% stronger",
         neg="Fragile: shields absorb 20% less",
         kit=[
             ab("Fast Lane", "Speed for a few seconds.", 10, 0.40, BUFF, dur=3, key=KEY_C),
             ab("Relay Bolt", "A concussive field that slows anyone inside.", 9, 0.60, ZONE, SLOW, dur=4, range_=20, radius=3.5, key=KEY_Q),
             ab("High Gear", "A shorter, sharper burst of speed.", 12, 0.25, BUFF, dur=5, key=KEY_E),
             ab("Overdrive", "Maximum speed and fire rate.", 45, 0.50, BUFF, dur=6, ultimate=True, key=KEY_X),
         ]),
    dict(name="Aegis", role=DUELIST,
         desc="A duelist who duels on his own terms, walling off sightlines and pressing the advantage.",
         pas="Contingency: shields last 25% longer",
         neg="Methodical: slower ability cooldowns",
         kit=[
             ab("Contingency", "A solid barrier that blocks movement and sight.", 10, 0, ZONE, WALL, dur=5, range_=12),
             ab("Undercut", "A field that slows anyone caught inside.", 12, 0.70, ZONE, SLOW, dur=4, range_=22, radius=3, key=KEY_Q),
             ab("Double Tap", "Speed and fire rate for the duration.", 10, 0.25, BUFF, dur=6, key=KEY_E),
             ab("Kill Contract", "A marked duelist's buff: speed, fire rate and focus.", 50, 0.45, BUFF, dur=8, ultimate=True, key=KEY_X),
         ]),
    dict(name="Predator", role=DUELIST,
         desc="A hunter duelist who pins prey in a killing field before closing the distance.",
         pas="Apex: damage-over-time effects last longer",
         neg="Overconfident: shields absorb 20% less",
         kit=[
             ab("Saturate", "A field that damages anyone standing in it.", 10, 6, ZONE, DAMAGE, dur=5, range_=20, radius=3.5),
             ab("Lightspeed Refract", "A burst of speed along your look direction.", 8, 1.5, DASH, key=KEY_Q),
             ab("Convergent Paths", "A hail of bolts along your aim line.", 10, 50, PROJECTILE, range_=25, radius=3, key=KEY_E),
             ab("Lethal Bloom", "A single devastating strike at the point you aim.", 45, 80, PROJECTILE, range_=30, radius=4, ultimate=True, key=KEY_X),
         ]),

    # ---------------------------------------------------------------- CONTROLLERS
    dict(name="Comet", role=CONTROLLER,
         desc="An orbital controller. Calls smokes down from above and an airstrike on anything left in the open.",
         pas="Commander: your smokes are larger",
         neg="Predictable: abilities telegraph loudly",
         kit=[
             ab("Sky Smoke", "Deploys a smoke sphere that blocks line of sight.", 15, 0, ZONE, SMOKE, dur=12, range_=25, radius=5),
             ab("Stim Beacon", "Speed and fire rate for the duration.", 15, 0.20, BUFF, dur=6, key=KEY_Q),
             ab("Incendiary", "A burning field that damages anyone inside.", 12, 8, ZONE, DAMAGE, dur=6, range_=22, radius=3.5, key=KEY_E),
             ab("Orbital Strike", "A laser-guided munition at the point you aim.", 50, 80, PROJECTILE, range_=30, radius=5, ultimate=True, key=KEY_X),
         ]),
    dict(name="Wraith", role=CONTROLLER,
         desc="A controller of shadows and dread. Smokes a site, then steps through the dark to flank it.",
         pas="Shrouded: teleports make no sound",
         neg="Unstable: lower max shield",
         kit=[
             ab("Dark Cover", "Deploys a smoke sphere that blocks line of sight.", 12, 0, ZONE, SMOKE, dur=12, range_=25, radius=4.5),
             ab("Shrouded Step", "Step through shadow to the point you aim at.", 10, 0, TELEPORT, range_=6, key=KEY_Q),
             ab("Paranoia", "A blind that lands where you aim.", 12, 0, ZONE, FLASH, dur=2.0, range_=20, radius=5, key=KEY_E),
             ab("From the Shadows", "Cross the map to anywhere you can see.", 50, 0, TELEPORT, range_=30, ultimate=True, key=KEY_X),
         ]),
    dict(name="Nebula", role=CONTROLLER,
         desc="A controller who bends the battlefield itself, pulling and slowing enemies inside her cosmic field.",
         pas="Astral: your zones last longer",
         neg="Drifting: slower movement while casting",
         kit=[
             ab("Gravity Well", "Drags enemies towards the centre of the field.", 12, 8, ZONE, PULL, dur=3, range_=22, radius=4),
             ab("Nova Pulse", "A concussive field that slows anyone inside.", 12, 0.60, ZONE, SLOW, dur=4, range_=22, radius=4, key=KEY_Q),
             ab("Cosmos", "Deploys a smoke sphere that blocks line of sight.", 12, 0, ZONE, SMOKE, dur=12, range_=25, radius=5, key=KEY_E),
             ab("Cosmic Divide", "A massive barrier that splits the site.", 50, 0, ZONE, WALL, dur=10, range_=16, ultimate=True, key=KEY_X),
         ]),
    dict(name="Toxin", role=CONTROLLER,
         desc="A controller of poison. Denies ground with a burning screen and a pit that outlasts a firefight.",
         pas="Venom: damage-over-time is 20% stronger",
         neg="Corrosive: your own zones hurt you too",
         kit=[
             ab("Poison Cloud", "A toxic field that damages anyone inside.", 12, 5, ZONE, DAMAGE, dur=8, range_=20, radius=3.5),
             ab("Toxic Screen", "A solid barrier of corrosive vapour.", 12, 0, ZONE, WALL, dur=8, range_=15, key=KEY_Q),
             ab("Snake Bite", "A vial of venom along your aim line.", 10, 40, PROJECTILE, range_=20, radius=2.5, key=KEY_E),
             ab("Viper's Pit", "A vast toxic field. You are immune; they are not.", 50, 8, ZONE, DAMAGE, dur=10, range_=18, radius=5, ultimate=True, key=KEY_X),
         ]),
    dict(name="Maelstrom", role=CONTROLLER,
         desc="A controller of tide and tide-wall. Shields himself, parts the sea, and drags the fight to the deep.",
         pas="Cove: shields are 25% stronger",
         neg="Salt: heals are 15% weaker",
         kit=[
             ab("Cove", "A shield that absorbs damage for a while.", 12, 50, SHIELD, dur=6),
             ab("High Tide", "A solid wall of water that blocks movement and sight.", 12, 0, ZONE, WALL, dur=8, range_=16, key=KEY_Q),
             ab("Cascade", "A concussive wave that slows anyone inside.", 12, 0.65, ZONE, SLOW, dur=4, range_=20, radius=4, key=KEY_E),
             ab("Reckoning", "A slowing deluge across the whole site.", 50, 0.50, ZONE, SLOW, dur=6, range_=22, radius=5, ultimate=True, key=KEY_X),
         ]),
    dict(name="Hemlock", role=CONTROLLER,
         desc="A controller with a deathgrip on the round. Smokes a site and decays anyone holding it.",
         pas="Not Dead Yet: the first lethal hit leaves you at 1 health",
         neg="Withering: healing received is 15% weaker",
         kit=[
             ab("Ruse", "Deploys a smoke sphere that blocks line of sight.", 12, 0, ZONE, SMOKE, dur=10, range_=24, radius=4.5),
             ab("Meddle", "A decaying field that damages anyone inside.", 12, 7, ZONE, DAMAGE, dur=4, range_=20, radius=3, key=KEY_Q),
             ab("Pick-Me-Up", "Restores health over a short window.", 10, 40, HEAL, dur=2, key=KEY_E),
             ab("Not Dead Yet", "A surge of health when you need it most.", 55, 100, HEAL, dur=2, ultimate=True, key=KEY_X),
         ]),
    dict(name="Siren", role=CONTROLLER,
         desc="A controller whose sound is a weapon. Slows, silences and shreds with rhythm.",
         pas="Harmonics: suppression zones are larger",
         neg="Overwhelmed: lower max shield",
         kit=[
             ab("M-Pulse", "A dampening field that slows anyone inside.", 12, 0.60, ZONE, SLOW, dur=4, range_=20, radius=4),
             ab("Harmonize", "Deploys a smoke sphere that blocks line of sight.", 12, 0, ZONE, SMOKE, dur=8, range_=22, radius=4.5, key=KEY_Q),
             ab("Waveform", "A resonant field that damages anyone inside.", 12, 6, ZONE, DAMAGE, dur=5, range_=20, radius=3.5, key=KEY_E),
             ab("Bassquake", "A shockwave that drags enemies to its centre.", 50, 10, ZONE, PULL, dur=4, range_=22, radius=5, ultimate=True, key=KEY_X),
         ]),

    # ---------------------------------------------------------------- INITIATORS
    dict(name="Falcon", role=INITIATOR,
         desc="A recon initiator with a bow and a thousand eyes. Finds the enemy, then puts a bolt through them.",
         pas="Hawkeye: reveals last longer",
         neg="Exposed: revealed enemies see you too",
         kit=[
             ab("Recon Bolt", "Marks the enemy through walls for a few seconds.", 12, 0, REVEAL, dur=3, range_=25),
             ab("Shock Bolt", "An explosive bolt along your aim line.", 10, 45, PROJECTILE, range_=25, radius=2.5, key=KEY_Q),
             ab("Owl Drone", "Marks the enemy through walls for a few seconds.", 15, 0, REVEAL, dur=4, range_=20, key=KEY_E),
             ab("Hunter's Fury", "Three long-range bolts along your aim line.", 45, 70, PROJECTILE, range_=35, radius=3, ultimate=True, key=KEY_X),
         ]),
    dict(name="Tremor", role=INITIATOR,
         desc="A disruption initiator who turns the ground against you. Fault lines, aftershocks, and a rolling thunder.",
         pas="Seismic: slows are 20% stronger",
         neg="Unstable: lower max shield",
         kit=[
             ab("Aftershock", "A field that damages anyone inside.", 12, 9, ZONE, DAMAGE, dur=4, range_=18, radius=3),
             ab("Fault Line", "A concussive rupture that slows anyone inside.", 12, 0.70, ZONE, SLOW, dur=3, range_=20, radius=4, key=KEY_Q),
             ab("Flashpoint", "A blind that lands where you aim.", 10, 0, ZONE, FLASH, dur=2.0, range_=16, radius=5, key=KEY_E),
             ab("Rolling Thunder", "A wave of concussions across the whole site.", 50, 0.50, ZONE, SLOW, dur=5, range_=22, radius=5, ultimate=True, key=KEY_X),
         ]),
    dict(name="Tracker", role=INITIATOR,
         desc="A nature initiator with a hawk, a hound and a healer's hand. Finds you, blinds you, keeps her team alive.",
         pas="Trailblazer: reveals last longer",
         neg="Soft: shields absorb 20% less",
         kit=[
             ab("Guiding Light", "A blinding light that follows your aim.", 10, 0, ZONE, FLASH, dur=2.0, range_=22, radius=6),
             ab("Trailblazer", "Sends a beast out to mark the enemy.", 12, 0, REVEAL, dur=3, range_=20, key=KEY_Q),
             ab("Regrowth", "Restores health over a short window.", 12, 45, HEAL, dur=3, key=KEY_E),
             ab("Seekers", "Finds the enemy wherever they are hiding.", 50, 0, REVEAL, dur=5, range_=0, ultimate=True, key=KEY_X),
         ]),
    dict(name="Automaton", role=INITIATOR,
         desc="A machine built to fight abilities. Flashes, suppresses, and nullifies the enemy's kit.",
         pas="Purpose-built: suppression lasts longer",
         neg="Cold: healing received is 15% weaker",
         kit=[
             ab("FLASH/drive", "A blind that lands where you aim.", 10, 0, ZONE, FLASH, dur=2.0, range_=18, radius=5),
             ab("ZERO/point", "A field that blocks ability use inside it.", 12, 0, ZONE, SUPPRESS, dur=4, range_=20, radius=4, key=KEY_Q),
             ab("FRAG/ment", "An explosive field that damages anyone inside.", 12, 8, ZONE, DAMAGE, dur=4, range_=18, radius=3, key=KEY_E),
             ab("NULL/cmd", "A vast suppression field across the site.", 50, 0, ZONE, SUPPRESS, dur=6, range_=22, radius=5, ultimate=True, key=KEY_X),
         ]),
    dict(name="Bounty", role=INITIATOR,
         desc="A nightmare initiator who hunts with a prowler and a pull. You cannot run, and you cannot hide.",
         pas="Nightfall: reveals last longer",
         neg="Haunted: revealed enemies deal 10% more damage",
         kit=[
             ab("Seize", "Drags enemies towards the centre of the field.", 12, 7, ZONE, PULL, dur=3, range_=20, radius=4),
             ab("Haunt", "Marks the enemy through walls for a few seconds.", 12, 0, REVEAL, dur=3, range_=22, key=KEY_Q),
             ab("Prowler", "Sends a shade out to mark the enemy.", 15, 0, REVEAL, dur=3, range_=18, key=KEY_E),
             ab("Nightfall", "A slowing nightmare across the whole site.", 50, 0.50, ZONE, SLOW, dur=5, range_=24, radius=5, ultimate=True, key=KEY_X),
         ]),
    dict(name="Flock", role=INITIATOR,
         desc="A creature-initiator with reusable pets. Wingman blinds, Mosh burns, and Thrash clears.",
         pas="Pack: your creatures recharge faster",
         neg="Soft: shields absorb 20% less",
         kit=[
             ab("Wingman", "An explosive concierge along your aim line.", 12, 45, PROJECTILE, range_=22, radius=3),
             ab("Dizzy", "A blinding pet that lands where you aim.", 10, 0, ZONE, FLASH, dur=2.0, range_=18, radius=5, key=KEY_Q),
             ab("Mosh Pit", "A field that damages anyone inside.", 12, 8, ZONE, DAMAGE, dur=4, range_=18, radius=3.5, key=KEY_E),
             ab("Thrash", "A concussive beast along your aim line.", 45, 75, PROJECTILE, range_=28, radius=4, ultimate=True, key=KEY_X),
         ]),
    dict(name="Volley", role=INITIATOR,
         desc="A salvo initiator with a stealth drone and a guided rocket for every answer.",
         pas="Guided: projectile damage is 15% higher",
         neg="Predictable: abilities telegraph loudly",
         kit=[
             ab("Stealth Drone", "Marks the enemy through walls for a few seconds.", 12, 0, REVEAL, dur=4, range_=24),
             ab("Guided Salvo", "A guided munition along your aim line.", 12, 50, PROJECTILE, range_=26, radius=3, key=KEY_Q),
             ab("Special Delivery", "A concussive field that slows anyone inside.", 12, 0.70, ZONE, SLOW, dur=3, range_=20, radius=3.5, key=KEY_E),
             ab("Armageddon", "Everything you have, at the point you aim.", 50, 85, PROJECTILE, range_=32, radius=4.5, ultimate=True, key=KEY_X),
         ]),

    # ---------------------------------------------------------------- SENTINELS
    dict(name="Mender", role=SENTINEL,
         desc="The only sentinel who heals. Slows the push, walls the angle, and keeps herself in the round.",
         pas="Soothing: heals are 25% stronger",
         neg="Pacifist: ability damage is 15% lower",
         kit=[
             ab("Slow Orb", "A concussive field that slows anyone inside.", 12, 0.60, ZONE, SLOW, dur=4, range_=20, radius=3.5),
             ab("Barrier Orb", "A solid wall that blocks movement and sight.", 15, 0, ZONE, WALL, dur=8, range_=12, key=KEY_Q),
             ab("Healing Orb", "Restores health over a short window.", 10, 45, HEAL, dur=2, key=KEY_E),
             ab("Resurrection", "A surge of health when you need it most.", 55, 100, HEAL, dur=2, ultimate=True, key=KEY_X),
         ]),
    dict(name="Watcher", role=SENTINEL,
         desc="A spy sentinel. Trapwires, cages and a camera that sees everything you would rather he didn't.",
         pas="Informant: reveals last longer",
         neg="Exposed: revealed enemies see you too",
         kit=[
             ab("Trapwire", "A proximity explosive at the point you aim.", 15, 50, MINE, range_=18, radius=3),
             ab("Cyber Cage", "Deploys a smoke sphere that blocks line of sight.", 12, 0, ZONE, SMOKE, dur=8, range_=20, radius=4, key=KEY_Q),
             ab("Spycam", "Marks the enemy through walls for a few seconds.", 15, 0, REVEAL, dur=5, range_=24, key=KEY_E),
             ab("Neural Theft", "Finds the enemy wherever they are hiding.", 50, 0, REVEAL, dur=6, range_=0, ultimate=True, key=KEY_X),
         ]),
    dict(name="Tinker", role=SENTINEL,
         desc="An engineer sentinel who sets a site and walks away. Bots, nanoswarms and a lockdown for the late round.",
         pas="Set and Forget: mines recharge faster",
         neg="Stationary: lower move speed while a mine is armed",
         kit=[
             ab("Nanoswarm", "A proximity explosive at the point you aim.", 12, 55, MINE, range_=18, radius=3),
             ab("Alarmbot", "A proximity explosive at the point you aim.", 15, 40, MINE, range_=18, radius=3.5, key=KEY_Q),
             ab("Turret", "Marks the enemy through walls for a few seconds.", 15, 0, REVEAL, dur=6, range_=20, key=KEY_E),
             ab("Lockdown", "A slowing field across the whole site.", 50, 0.40, ZONE, SLOW, dur=5, range_=22, radius=5, ultimate=True, key=KEY_X),
         ]),
    dict(name="Deadeye", role=SENTINEL,
         desc="A gentleman sentinel with a trap, a teleport and a licence for the operator.",
         pas="Trademark: mines are cheaper, and larger",
         neg="Predictable: teleports announce where you went",
         kit=[
             ab("Trademark", "A proximity explosive at the point you aim.", 15, 50, MINE, range_=18, radius=3),
             ab("Rendezvous", "Step to the point you have marked.", 12, 0, TELEPORT, range_=8, key=KEY_Q),
             ab("Headhunter", "Speed and handling for the duration.", 12, 0.30, BUFF, dur=6, key=KEY_E),
             ab("Tour De Force", "A duelist's focus: speed, fire rate and composure.", 50, 0.50, BUFF, dur=8, ultimate=True, key=KEY_X),
         ]),
    dict(name="Barricade", role=SENTINEL,
         desc="A sentinel of nets and barriers. Catches the push, pins it, and ends it.",
         pas="Grav-net: pulls are stronger",
         neg="Rigid: lower move speed while a wall is up",
         kit=[
             ab("GravNet", "Drags enemies towards the centre of the field.", 12, 7, ZONE, PULL, dur=3, range_=20, radius=4),
             ab("Sonic Sensor", "Marks the enemy through walls for a few seconds.", 15, 0, REVEAL, dur=4, range_=20, key=KEY_Q),
             ab("Barrier Mesh", "A solid wall that blocks movement and sight.", 15, 0, ZONE, WALL, dur=8, range_=12, key=KEY_E),
             ab("Annihilation", "A crushing pull across the whole site.", 50, 10, ZONE, PULL, dur=4, range_=22, radius=5, ultimate=True, key=KEY_X),
         ]),
    dict(name="Briar", role=SENTINEL,
         desc="A thorned sentinel. Razorvine, arc roses and a garden of steel for anyone who pushes.",
         pas="Razorvine: damage-over-time is 20% stronger",
         neg="Brittle: shields absorb 20% less",
         kit=[
             ab("Razorvine", "A field that damages anyone inside.", 12, 7, ZONE, DAMAGE, dur=5, range_=18, radius=3.5),
             ab("Arc Rose", "A blind that lands where you aim.", 12, 0, ZONE, FLASH, dur=2.0, range_=18, radius=5, key=KEY_Q),
             ab("Shear", "A concussive field that slows anyone inside.", 12, 0.70, ZONE, SLOW, dur=3, range_=20, radius=3.5, key=KEY_E),
             ab("Steel Garden", "A vast barrier of thorns across the site.", 50, 0, ZONE, WALL, dur=10, range_=14, ultimate=True, key=KEY_X),
         ]),
    dict(name="Cordon", role=SENTINEL,
         desc="A sentinel of denial. Chokes the angle, cuts the rotation, intercepts the push.",
         pas="Interceptor: mines recharge faster",
         neg="Bureaucratic: slower ability cooldowns",
         kit=[
             ab("Chokehold", "A concussive field that slows anyone inside.", 12, 0.60, ZONE, SLOW, dur=4, range_=20, radius=4),
             ab("Crosscut", "Step through a seam to the point you aim at.", 12, 0, TELEPORT, range_=7, key=KEY_Q),
             ab("Interceptor", "A proximity explosive at the point you aim.", 15, 45, MINE, range_=18, radius=3, key=KEY_E),
             ab("Evolution", "Adapt: speed, fire rate and focus.", 50, 0.50, BUFF, dur=8, ultimate=True, key=KEY_X),
         ]),
]


def fmt(value):
    """Unity writes whole floats without a decimal point."""
    if isinstance(value, float):
        if value == int(value):
            return str(int(value))
        return repr(value)
    return str(value)


def yaml_string(text):
    return '"' + text.replace("\\", "\\\\").replace('"', '\\"') + '"'


def write_asset(cls, guid):
    lines = [
        "%YAML 1.1",
        "%TAG !u! tag:unity3d.com,2011:",
        "--- !u!114 &11400000",
        "MonoBehaviour:",
        "  m_ObjectHideFlags: 0",
        "  m_CorrespondingSourceObject: {fileID: 0}",
        "  m_PrefabInstance: {fileID: 0}",
        "  m_PrefabAsset: {fileID: 0}",
        "  m_GameObject: {fileID: 0}",
        "  m_Enabled: 1",
        "  m_EditorHideFlags: 0",
        f"  m_Script: {{fileID: 11500000, guid: {CLASS_GUID}, type: 2}}",
        f"  m_Name: {cls['name']}",
        "  m_EditorClassIdentifier: ",
        f"  displayName: {yaml_string(cls['name'])}",
        f"  role: {cls['role']}",
        f"  description: {yaml_string(cls['desc'])}",
        f"  passiveTrait: {yaml_string(cls['pas'])}",
        f"  negativeTrait: {yaml_string(cls['neg'])}",
        "  allowedWeapons: []",
        "  abilities:",
    ]

    for a in cls["kit"]:
        lines.append(f"  - displayName: {yaml_string(a['display'])}")
        lines.append(f"    description: {yaml_string(a['desc'])}")
        lines.append(f"    cooldown: {fmt(a['cd'])}")
        lines.append(f"    duration: {fmt(a['dur'])}")
        lines.append(f"    power: {fmt(a['power'])}")
        lines.append(f"    isUltimate: {1 if a['ult'] else 0}")
        lines.append(f"    archetype: {a['kind']}")
        lines.append(f"    zoneKind: {a['zone']}")
        lines.append(f"    range: {fmt(a['range'])}")
        lines.append(f"    radius: {fmt(a['radius'])}")
        lines.append(f"    activationKey: {a['key']}")

    lines += [
        "  abilityPrefab: {fileID: 0}",
        "  maxHealth: 100",
        "  moveSpeedMultiplier: 1",
        "  maxStamina: 100",
        "",
    ]

    with open(os.path.join(OUT_DIR, cls["name"] + ".asset"), "w", encoding="utf-8") as f:
        f.write("\n".join(lines))


def write_meta(path, guid):
    with open(path, "w", encoding="utf-8") as f:
        f.write(
            "fileFormatVersion: 2\n"
            f"guid: {guid}\n"
            "NativeFormatImporter:\n"
            "  externalObjects: {}\n"
            "  mainObjectFileID: 11400000\n"
            "  userData: \n"
            "  assetBundleName: \n"
            "  assetBundleVariant: \n"
        )


def patch_scene(guids):
    """Replaces the ClassManager's empty roster with the 29 class references."""
    with open(SCENE, "r", encoding="utf-8-sig") as f:
        text = f.read()

    marker = "  classes: []"
    if marker not in text:
        raise SystemExit("Could not find the ClassManager's 'classes: []' line - scene not patched.")

    entries = "\n".join(
        f"  - {{fileID: 11400000, guid: {g}, type: 2}}" for g in guids
    )
    replacement = "  classes:\n" + entries

    with open(SCENE, "w", encoding="utf-8") as f:
        f.write(text.replace(marker, replacement, 1))


def main():
    os.makedirs(OUT_DIR, exist_ok=True)

    guids = []
    for cls in CLASSES:
        guid = uuid.uuid4().hex
        guids.append(guid)
        write_asset(cls, guid)
        write_meta(os.path.join(OUT_DIR, cls["name"] + ".asset.meta"), guid)

    patch_scene(guids)

    # Sanity checks against the enum contract.
    names = [c["name"] for c in CLASSES]
    assert len(names) == 29, f"expected 29 classes, got {len(names)}"
    assert len(set(names)) == 29, "duplicate class names"
    for cls in CLASSES:
        keys = [a["key"] for a in cls["kit"]]
        assert len(set(keys)) == len(keys), f"{cls['name']} has a double-bound key"
        assert all(k in (KEY_C, KEY_Q, KEY_E, KEY_X) for k in keys), f"{cls['name']} uses a key outside C/Q/E/X"
        assert sum(1 for a in cls["kit"] if a["ult"]) == 1, f"{cls['name']} needs exactly one ultimate"
        for a in cls["kit"]:
            if a["kind"] == ZONE:
                assert a["zone"] != Z_NONE, f"{cls['name']}.{a['display']} is a Zone with no zoneKind"

    print(f"Wrote {len(CLASSES)} class assets to {OUT_DIR}")
    print("Patched the ClassManager roster in the scene.")
    print("GUIDs:")
    for name, guid in zip(names, guids):
        print(f"  {name:<12} {guid}")


if __name__ == "__main__":
    main()
