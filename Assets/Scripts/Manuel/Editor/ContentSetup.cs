using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProjectLEA.Manuel.Managers;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectLEA.Manuel.EditorTools
{
    /// <summary>
    /// Places the supplied art into the Manuel scene: the arena map, the bean characters,
    /// and the baked weapon viewmodels.
    ///
    /// The map and the characters both come from authors who used their own units, so
    /// neither can simply be dropped in:
    ///
    /// <list type="bullet">
    /// <item>The map is already in metres - its stair treads are 0.34 deep with 0.12 risers and
    /// its perimeter walls are 3.08 tall, which is only sane at 1 unit = 1 metre. It goes in at
    /// scale 1. Its 289 mesh pieces arrive without colliders though, so every one of them gets a
    /// non-convex MeshCollider or the player would fall straight through the floor.</item>
    /// <item>The beans arrive standing on their own rig origin and roughly 1.8 m tall, so each is
    /// rescaled to the exact capsule height and left on its origin - the rig root is between the
    /// feet, which is where a character's pivot belongs, and re-pivoting to the mesh's centre
    /// would push the body backwards off its own feet.</item>
    /// </list>
    ///
    /// Safe to run again: every step checks for what it is about to create and leaves existing
    /// work alone.
    ///
    /// Menu: Tools > Manuel > Setup Content (map, character, weapons)
    /// </summary>
    public static class ContentSetup
    {
        private const string ScenePath = "Assets/Scenes/Manuel.unity";
        private const string MarkerPath = "ProjectSettings/ContentSetup.done";

        private const string MapModelPath = "Assets/Models/Manuel/Maps/FpsTpsMap.glb";
        private const string MapRootName = "MapRoot";

        private const string CharacterPrefabDir = "Assets/Prefabs/Manuel/Characters";
        private const string AnimatorDir = "Assets/Animations/Manuel";

        // The bean's own height in metres. The map is built at 1:1 and the player capsule is
        // 1.8 m, so a bean taller than this looks like a giant next to the walls - which is
        // what "way too big" was: the bake was scaling the FBX's DEPTH to the capsule height,
        // so every bean came out roughly twice as tall as it should be.
        // The bean's finished height. 1.2 m is deliberately small - a bean is a toy-sized
        // fighter, not a human stand-in, and the camera is set to its eye level in the scene.
        private const float CharacterHeight = 1.2f;

        // The beans replace the human character entirely. Each entry is one exported FBX and
        // becomes its own prefab with its own animator, so the customisation tab can swap
        // them later without any retargeting.
        private class CharacterSpec
        {
            public string Model;
            public string Prefab;
            public float Height;
        }

        private static readonly CharacterSpec[] BeanModels =
        {
            new CharacterSpec { Model = "Assets/Models/Manuel/Characters/Bean.fbx", Prefab = "Bean",
                                Height = CharacterHeight },
            new CharacterSpec { Model = "Assets/Models/Manuel/Characters/GentlemanBean.fbx",
                                Prefab = "GentlemanBean", Height = CharacterHeight },
        };

        /// <summary>The bean everybody wears by default; the customisation tab changes it.</summary>
        private const string DefaultBean = "Bean";

        private const string SpeedParam = "Speed";
        private const string CrouchParam = "Crouch";

        // Spawn points, picked from a floor-level obstacle map of the imported arena (see
        // Tools/inspect_scene_assets.py): open cells, far apart, clear of the four quadrant
        // blocks and the central room. Named so SpawnManager's ordinal sort gives slot One the
        // first and slot Two the second - the order is what keeps a client's own body off the
        // host's spawn.
        private static readonly Vector3 SpawnOne = new Vector3(-8f, 0f, -4f);
        private static readonly Vector3 SpawnTwo = new Vector3(8f, 0f, 4f);

        // The imported arena ships its own floor - a 24.8 x 16.4 m plane at y = 0 - so no
        // ground slab is created here. See RelayGround: a leftover placeholder cube from the
        // test scene is deleted, not re-laid, because a second floor at the wrong height is
        // worse than none.
        // The imported arena's walkable level is y = 0; the bean's rig origin is between the
        // feet, so the model goes straight onto the spawn point's y.

        [MenuItem("Tools/Manuel/Setup Content (map, character, weapons)")]
        public static void RunMenu()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            ApplyAll(scene);
        }

        /// <summary>Prompt-free variant used by the automatic run.</summary>
        public static void RunAuto()
        {
            EditorSceneManager.SaveOpenScenes();
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            ApplyAll(scene);
        }

        private static void ApplyAll(Scene scene)
        {
            // The models have to be IMPORTED before they can be loaded. Unity imports them on the
            // same load that compiles this file, and the auto-run waits for isUpdating to clear
            // before ticking, but a large model can still be mid-import at that point - and
            // LoadAssetAtPath silently returns null for an unimported asset. Refresh first, then
            // probe the models: if either is still missing, the run below would quietly do
            // nothing, so the marker is held back and the next load tries again.
            AssetDatabase.Refresh();
            bool importsReady = AssetDatabase.LoadAssetAtPath<GameObject>(MapModelPath) != null &&
                                BeanModels.All(b => AssetDatabase.LoadAssetAtPath<GameObject>(b.Model) != null);

            // The dummy the character is assigned to is created by ManagersSetup. If that pass
            // has not happened yet (its marker is missing too) do it now, otherwise PlayerTwo
            // does not exist and this silently assigns nothing.
            EnsureManagers();

            BakeBeans();
            PlaceMap();
            SwapPlayerBody();
            AssignDummyBody();
            RetireHuman();
            AlignScene();

            // The viewmodels are the same idea applied to the weapons: measured, rescaled,
            // re-pivoted and handed to their WeaponDefinition.
            WeaponModelBake.Bake();

            // ManagersSetup may have re-opened the scene while ensuring the manager stack was
            // present, so the scene this was called with can be stale. Save what is open now.
            var current = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(current);
            EditorSceneManager.SaveScene(current, ScenePath);
            AssetDatabase.SaveAssets();

            if (!importsReady)
            {
                // Everything above is idempotent, so a partial run costs nothing - but the marker
                // means "the content is in", and it is not.
                Debug.LogWarning("[ContentSetup] The map or a bean model was not imported " +
                                 "yet, so nothing was placed. This will retry automatically on " +
                                 "the next reload; select the model and let Unity finish " +
                                 "importing if it does not.");
                return;
            }

            File.WriteAllText(MarkerPath,
                "Content set up " + System.DateTime.Now.ToString("u") + System.Environment.NewLine);

            Debug.Log("[ContentSetup] Done. Map, beans, spawn points and weapon viewmodels " +
                      "are in the Manuel scene. Rebuild the player to test it.");
        }

        /// <summary>The manager stack has to be in the scene before anything can be wired to it.</summary>
        private static void EnsureManagers()
        {
            if (GameObject.Find("[Managers]") != null) return;

            Debug.Log("[ContentSetup] The manager stack is not in the scene yet - running the " +
                      "manager setup first.");
            ManagersSetup.RunAuto();
        }

        // ---------------------------------------------------------------- Character

        /// <summary>
        /// Turns each exported bean into a prefab standing on its own origin, scaled to the
        /// capsule convention, with an Animator wired to the clips the FBX carries.
        /// </summary>
        private static void BakeBeans()
        {
            Directory.CreateDirectory(CharacterPrefabDir);

            foreach (var spec in BeanModels)
            {
                BakeBean(spec);
            }
        }

        private static void BakeBean(CharacterSpec spec)
        {
            // A model imported with no rig has no Avatar, and an Animator without one animates
            // nothing. Unity defaults a new FBX import to no rig, so the first import of a bean
            // has to be flipped to Generic before its avatar exists. Reimporting is
            // synchronous, so the source is reloaded afterwards.
            EnsureGenericRig(spec.Model);

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(spec.Model);
            if (source == null)
            {
                Debug.LogWarning($"[ContentSetup] {spec.Model} is not imported as a model " +
                                 "yet. Select it and let Unity re-import, then run again.");
                return;
            }

            var temp = new GameObject("__bake_" + spec.Prefab);
            var modelGo = new GameObject("Model");
            modelGo.transform.SetParent(temp.transform, false);
            var instance = UnityEngine.Object.Instantiate(source, modelGo.transform, false);
            instance.name = spec.Prefab;

            if (!WorldBounds(instance, out var min, out var max))
            {
                Debug.LogWarning($"[ContentSetup] {spec.Model} has no measurable mesh - the " +
                                 "bean keeps whatever body it had.");
                UnityEngine.Object.DestroyImmediate(temp);
                return;
            }

            var size = max - min;

            // WorldBounds returns the RENDERED bounds: the bean as it will actually appear,
            // already rotated upright by the FBX's axis conversion. So Y is up, size.y is the
            // bean's height and min.y is where its feet land. Measuring anything else here has
            // cost three rounds of this bug:
            //   - size.y from localBounds was the bean's Blender DEPTH (0.90 m), so a 1.8 m
            //     target over the depth gave scale 2.0 and a 3.6 m bean ("waaay too big").
            //   - scaling on size.z instead fixed the size but min.z is not the rendered floor,
            //     so the lift put every bean 1.8 m in the air ("the beans are flying").
            // Renderer.bounds is the world-space box the GPU draws, which is the only quantity
            // that is self-consistent with the placement math below it.
            if (size.y < size.x || size.y < size.z)
            {
                Debug.LogWarning($"[ContentSetup] {spec.Prefab}: Y ({size.y:F2}) is not the " +
                                 $"tallest dimension of the rendered bounds ({size}). The bean " +
                                 "may not be standing up in the file; check the bake result.");
            }

            float scale = spec.Height / size.y;

            // X and Z stay centred on the rig's root: it already sits between the feet, and
            // centring the mesh on the capsule's axis instead would slide the body off its own
            // pivot. Only the feet are lifted, by the rendered floor, so the bean stands on the
            // prefab's origin whatever the FBX's own coordinate quirks.
            modelGo.transform.SetPositionAndRotation(
                new Vector3(0f, -scale * min.y, 0f), Quaternion.identity);
            modelGo.transform.localScale = Vector3.one * scale;

            // The local player already has a CharacterController; a collider here would be a
            // second one next to it, which is what flings a player across the arena.
            StripColliders(temp);

            var animator = temp.AddComponent<Animator>();
            animator.runtimeAnimatorController = BuildBeanAnimator(spec);
            animator.avatar = FindSubAsset<Avatar>(spec.Model);
            animator.applyRootMotion = false;

            if (animator.avatar == null)
            {
                // The clips still import, but an Animator with no avatar plays none of them -
                // the bean would stand frozen in its bind pose with no error to point at it.
                Debug.LogWarning($"[ContentSetup] {spec.Prefab} imported with no Avatar. Its " +
                                 "animation type is " + (AssetImporter.GetAtPath(spec.Model) as ModelImporter)?.animationType +
                                 "; set the FBX's Rig to Generic and re-run.");
            }

            // Drives the animator from the bean's own movement, so the same prefab works on the
            // local player, the network proxy and the stand-in dummy without any wiring.
            temp.AddComponent<BeanAnimation>();

            temp.name = spec.Prefab;
            string prefabPath = AssetPath(CharacterPrefabDir, spec.Prefab + ".prefab");
            var prefab = PrefabUtility.SaveAsPrefabAsset(temp, prefabPath);
            UnityEngine.Object.DestroyImmediate(temp);

            if (prefab == null)
            {
                Debug.LogError($"[ContentSetup] Could not write {prefabPath}.");
                return;
            }

            Debug.Log($"[ContentSetup] Bean baked: {spec.Prefab} renders {size.y:F2} m tall " +
                      $"(width {size.x:F2}, depth {size.z:F2}), floor at {min.y:F2} " +
                      $"-> {spec.Height} m (scale {scale:F4}), feet on the origin, " +
                      "animated by its own clips.");
        }

        /// <summary>
        /// A model imported with no rig has no Avatar, and an Animator without one animates
        /// nothing but the checkbox next to it. Unity defaults a new FBX import to no rig, so
        /// the first import of a bean has to be flipped to Generic before its avatar exists -
        /// Humanoid is not an option for a five-bone bean, and Generic is what the clip-driven
        /// blend trees need.
        /// </summary>
        private static void EnsureGenericRig(string fbxPath)
        {
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null) return;

            // Both must hold: animationType Generic alone leaves avatarSetup at None, and a
            // Generic rig with no Avatar imports its clips but an Animator can't play them -
            // the bean would stand frozen in its bind pose with no error to point at it.
            bool rigOk = importer.animationType == ModelImporterAnimationType.Generic
                         && importer.avatarSetup == ModelImporterAvatarSetup.CreateFromThisModel;
            if (rigOk) return;

            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.SaveAndReimport();

            Debug.Log($"[ContentSetup] {Path.GetFileName(fbxPath)}: rig -> Generic + avatar " +
                      "created from this model, so an Animator can actually drive the bean.");
        }

        /// <summary>
        /// The combined RENDERED bounds of everything under <paramref name="root"/>: the
        /// world-space box the GPU actually draws, in Unity axes with Y up.
        ///
        /// Deliberately uses <see cref="Renderer.bounds"/> rather than
        /// <see cref="SkinnedMeshRenderer.localBounds"/>. localBounds live in the mesh's own
        /// import space, which for a Blender FBX is still the file's native axes (height on Z,
        /// depth on Y) - the axis conversion happens at render time. Every placement decision in
        /// BakeBean (scale by height, lift by the floor) is a statement about the rendered bean,
        /// so mixing localBounds into it produced a bean twice too tall, then one floating a
        /// metre and a half off the floor. Renderer.bounds needs no axis guessing at all.
        ///
        /// Measured with the model at the origin and identity rotation, so world == local for
        /// the purposes of the caller, and before the Animator is added - the bind pose is what
        /// a freshly spawned prefab starts in.
        /// </summary>
        private static bool WorldBounds(GameObject root, out Vector3 min, out Vector3 max)
        {
            min = Vector3.one * float.PositiveInfinity;
            max = Vector3.one * float.NegativeInfinity;
            bool any = false;

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;

                // An inactive renderer still reports its bounds, and a disabled one is still
                // part of the body the player sees as soon as it is enabled.
                AbsorbPoint(renderer.bounds.min, ref min, ref max, ref any);
                AbsorbPoint(renderer.bounds.max, ref min, ref max, ref any);
            }

            return any;
        }

        private static void AbsorbPoint(Vector3 p, ref Vector3 min, ref Vector3 max, ref bool any)
        {
            any = true;
            for (int a = 0; a < 3; a++)
            {
                if (p[a] < min[a]) min[a] = p[a];
                if (p[a] > max[a]) max[a] = p[a];
            }
        }

        /// <summary>Loads a sub-asset of an imported model by type: the Avatar on a Generic rig.</summary>
        private static T FindSubAsset<T>(string assetPath) where T : UnityEngine.Object
        {
            return AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<T>().FirstOrDefault();
        }

        /// <summary>
        /// Locates an imported clip by name. Unity strips the "BeanRig|" take prefix, but an
        /// older export keeps it, so both spellings are tried before giving up.
        /// </summary>
        private static AnimationClip FindClip(string assetPath, string name)
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<AnimationClip>().ToList();
            if (clips.Count == 0) return null;

            var exact = clips.FirstOrDefault(c => string.Equals(c.name, name,
                StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;

            return clips.FirstOrDefault(c => c.name.EndsWith("|" + name,
                StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Builds the bean's animator controller from the clips its FBX imports as: two 1D
        /// blend trees on horizontal speed, one standing and one crouching, switched by a bool.
        ///
        /// One controller per bean, rather than a shared one, because Mecanim matches a clip to
        /// an avatar by bone path and a mismatched clip plays as nothing - with no warning. Both
        /// beans share a skeleton today, but keeping the controllers separate means a third bean
        /// with a different rig cannot silently break the first two.
        /// </summary>
        private static RuntimeAnimatorController BuildBeanAnimator(CharacterSpec spec)
        {
            Directory.CreateDirectory(AnimatorDir);
            string path = AssetPath(AnimatorDir, spec.Prefab + "Animator.controller");

            // Rebuilt from scratch every run: clearing a controller's state machine in place
            // leaves orphaned BlendTree sub-assets behind, and the beans are cheap to rebuild.
            if (File.Exists(path)) AssetDatabase.DeleteAsset(path);

            var controller = new AnimatorController { name = spec.Prefab + "Animator" };
            AssetDatabase.CreateAsset(controller, path);

            controller.AddParameter(SpeedParam, AnimatorControllerParameterType.Float);
            controller.AddParameter(CrouchParam, AnimatorControllerParameterType.Bool);

            // A controller built with new AnimatorController() may ship without a layer at
            // all, so make one rather than indexing into an empty array.
            AnimatorControllerLayer layer;
            if (controller.layers.Length > 0)
            {
                layer = controller.layers[0];
            }
            else
            {
                var baseStateMachine = new AnimatorStateMachine
                {
                    name = "Base Layer",
                    hideFlags = HideFlags.HideInHierarchy
                };
                AssetDatabase.AddObjectToAsset(baseStateMachine, controller);
                layer = new AnimatorControllerLayer
                {
                    name = "Base Layer",
                    defaultWeight = 1f,
                    stateMachine = baseStateMachine
                };
                controller.layers = new[] { layer };
            }

            var stateMachine = layer.stateMachine;

            // AddState already adds the state as a sub-asset of the controller in Unity 6, so
            // it must not be AddObjectToAsset'd as well - that throws "Adding asset to object
            // failed" and aborts the bake before any motion is assigned. The BlendTrees below
            // are plain new objects and do still need it.
            var stand = stateMachine.AddState("Standing");
            stand.motion = BlendTree(controller, spec, "Standing",
                ("Idle", 0f), ("Walk", 1f));

            var crouch = stateMachine.AddState("Crouching");
            crouch.motion = BlendTree(controller, spec, "Crouching",
                ("Crouch_Idle", 0f), ("Crouch_Walk", 1f));

            // No exit times: the crouch has to take over the moment the key goes down, not at
            // the next loop boundary, or a bean drops into a crouch a full stride late.
            var down = stand.AddTransition(crouch);
            down.AddCondition(AnimatorConditionMode.If, 0f, CrouchParam);
            down.hasExitTime = false;
            down.duration = 0.15f;

            var up = crouch.AddTransition(stand);
            up.AddCondition(AnimatorConditionMode.IfNot, 0f, CrouchParam);
            up.hasExitTime = false;
            up.duration = 0.15f;

            EditorUtility.SetDirty(controller);
            return controller;
        }

        /// <summary>
        /// A 0..1 blend between two clips. A missing clip falls back to the first one the FBX
        /// imported, so a bean whose export is missing a take still has a working tree instead
        /// of an empty motion.
        /// </summary>
        private static Motion BlendTree(AnimatorController controller, CharacterSpec spec,
                                        string name, params (string clip, float threshold)[] children)
        {
            var tree = new BlendTree
            {
                name = name,
                blendType = BlendTreeType.Simple1D,
                blendParameter = SpeedParam,
                hideFlags = HideFlags.HideInHierarchy
            };
            AssetDatabase.AddObjectToAsset(tree, controller);

            // BlendTree has no AddMotion; its children are a ChildMotion[] that is also a copy,
            // so the entries are collected here and assigned once below - mutating
            // tree.children[i] directly compiles and writes to a temporary.
            var nodes = new List<ChildMotion>();
            foreach (var child in children)
            {
                var clip = FindClip(spec.Model, child.clip);
                if (clip == null)
                {
                    var fallback = AssetDatabase.LoadAllAssetsAtPath(spec.Model)
                                                .OfType<AnimationClip>()
                                                .FirstOrDefault();
                    if (fallback == null)
                    {
                        Debug.LogWarning($"[ContentSetup] {spec.Prefab} has no imported clips " +
                                         "at all - its '{name}' blend tree is empty.");
                        continue;
                    }

                    Debug.LogWarning($"[ContentSetup] {spec.Prefab}: no clip matching " +
                                     $"'{child.clip}' - using '{fallback.name}' instead. " +
                                     "Re-export the bean with the full set if this matters.");
                    clip = fallback;
                }

                nodes.Add(new ChildMotion
                {
                    motion = clip,
                    threshold = child.threshold,
                    timeScale = 1f
                });
            }

            tree.children = nodes.ToArray();

            return tree;
        }

        /// <summary>
        /// Replaces the player's old body with the default bean and re-points the renderer
        /// reference PlayerController uses to keep the body out of the first-person view.
        /// </summary>
        private static void SwapPlayerBody()
        {
            var player = GameObject.Find("Player");
            if (player == null)
            {
                Debug.LogWarning("[ContentSetup] No object named 'Player' - nothing to swap.");
                return;
            }

            var prefab = LoadBeanPrefab(DefaultBean);
            if (prefab == null) return;

            var oldBody = player.transform.Find("BodyPlaceholder");
            if (oldBody != null)
            {
                // Nothing references the placeholder but PlayerController.bodyRenderer, and
                // that is re-pointed below; the old object can go. Running this again simply
                // replaces the bean with a fresh copy of the same prefab.
                UnityEngine.Object.DestroyImmediate(oldBody.gameObject);
            }

            var body = UnityEngine.Object.Instantiate(prefab, player.transform, false);
            body.name = "BodyPlaceholder";

            // The bean stands on its own origin while the capsule is centred on the player's,
            // so the body goes at the capsule's bottom or the bean floats a metre off the
            // floor. Read it from the controller rather than assuming an authored centre, so a
            // different capsule still lands the feet on the ground.
            var controller = player.GetComponent<CharacterController>();
            float floorY = controller != null
                ? -(controller.center.y + (controller.height * 0.5f))
                : 0f;
            body.transform.SetPositionAndRotation(new Vector3(0f, floorY, 0f), Quaternion.identity);
            body.transform.localScale = Vector3.one;

            RePointBodyRenderer(player, body);

            Debug.Log($"[ContentSetup] Player body -> {DefaultBean} prefab ({CharacterHeight} m tall, " +
                      $"feet at the capsule bottom, y = {floorY}).");
        }

        private static void RePointBodyRenderer(GameObject player, GameObject body)
        {
            var controller = player.GetComponent<PlayerController>();
            if (controller == null) return;

            var bodyRenderer = body.GetComponentInChildren<Renderer>();
            if (bodyRenderer == null)
            {
                Debug.LogWarning("[ContentSetup] The baked bean has no renderer - the local " +
                                 "player's shadow setup gets nothing to hide.");
                return;
            }

            var serialized = new SerializedObject(controller);
            var prop = serialized.FindProperty("bodyRenderer");
            if (prop == null)
            {
                Debug.LogWarning("[ContentSetup] PlayerController has no bodyRenderer field - " +
                                 "recompile and run again.");
                return;
            }

            prop.objectReferenceValue = bodyRenderer;
            serialized.ApplyModifiedProperties();
        }

        /// <summary>
        /// Hands the same prefab to the second player. PlayerTwoDummy.BuildBody instantiates it
        /// at runtime and adds the CapsuleCollider that makes the body damageable - that collider
        /// lives in the scene object, not the prefab, because the local player wears the prefab
        /// too and must stay collider-free.
        /// </summary>
        private static void AssignDummyBody()
        {
            var playerTwo = GameObject.Find("PlayerTwo");
            if (playerTwo == null)
            {
                Debug.LogWarning("[ContentSetup] No object named 'PlayerTwo' - the second " +
                                 "player keeps its capsule.");
                return;
            }

            var dummy = playerTwo.GetComponent<PlayerTwoDummy>();
            if (dummy == null)
            {
                Debug.LogWarning("[ContentSetup] PlayerTwo has no PlayerTwoDummy.");
                return;
            }

            var prefab = LoadBeanPrefab(DefaultBean);
            if (prefab == null) return;

            var serialized = new SerializedObject(dummy);
            var prop = serialized.FindProperty("bodyPrefab");
            if (prop == null)
            {
                Debug.LogWarning("[ContentSetup] PlayerTwoDummy has no bodyPrefab field - " +
                                 "recompile and run again.");
                return;
            }

            if (prop.objectReferenceValue == prefab)
            {
                Debug.Log($"[ContentSetup] PlayerTwo already wears the {DefaultBean} prefab.");
                return;
            }

            prop.objectReferenceValue = prefab;
            serialized.ApplyModifiedProperties();

            Debug.Log($"[ContentSetup] PlayerTwo body -> {DefaultBean} prefab.");
        }

        /// <summary>
        /// Removes the human character the game shipped with, once nothing references it.
        ///
        /// Both consumers - the player's body and the second player's - have been re-pointed by
        /// the time this runs, so the assets are safe to delete. Done here rather than by hand
        /// because a leftover reference to a deleted prefab leaves a whole hierarchy of missing
        /// objects in the scene, and this is the only place that knows the swap finished.
        /// </summary>
        private static void RetireHuman()
        {
            string[] retired =
            {
                AssetPath(CharacterPrefabDir, "PlayerGuy.prefab"),
                "Assets/Models/Manuel/Characters/PlayerGuy.glb"
            };

            foreach (var path in retired)
            {
                if (!File.Exists(path)) continue;

                AssetDatabase.DeleteAsset(path);
                Debug.Log($"[ContentSetup] Retired {path} - the beans replace the human.");
            }
        }

        private static GameObject LoadBeanPrefab(string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                AssetPath(CharacterPrefabDir, name + ".prefab"));
            if (prefab == null)
            {
                Debug.LogWarning($"[ContentSetup] {AssetPath(CharacterPrefabDir, name + ".prefab")} " +
                                 "is missing - the body keeps whatever it had. Bake the beans first.");
            }
            return prefab;
        }

        // --------------------------------------------------------------------- Map

        /// <summary>
        /// Places the arena at the origin at 1:1 and gives every mesh piece a collider, since
        /// glTFast imports geometry without any.
        /// </summary>
        private static void PlaceMap()
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name == MapRootName)
                {
                    Debug.Log($"[ContentSetup] '{MapRootName}' is already in the scene.");
                    return;
                }
            }

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(MapModelPath);
            if (source == null)
            {
                Debug.LogWarning($"[ContentSetup] {MapModelPath} is not imported as a model yet. " +
                                 "Select it and let Unity re-import, then run again.");
                return;
            }

            // InstantiatePrefab keeps the link to the imported asset, so a re-import of the GLB
            // propagates into the scene. Not every importer produces something the call accepts
            // as a prefab, so fall back to a plain clone rather than losing the whole step.
            GameObject map;
            try
            {
                map = (GameObject)PrefabUtility.InstantiatePrefab(source);
            }
            catch (Exception)
            {
                Debug.LogWarning("[ContentSetup] InstantiatePrefab refused the imported asset; " +
                                 "placing an unlinked copy instead.");
                map = (GameObject)UnityEngine.Object.Instantiate(source);
            }

            map.name = MapRootName;
            map.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            map.transform.localScale = Vector3.one;

            int colliders = 0;
            foreach (var filter in map.GetComponentsInChildren<MeshFilter>(true))
            {
                var go = filter.gameObject;
                if (go.GetComponent<Collider>() != null) continue;
                if (filter.sharedMesh == null) continue;

                var meshCollider = go.AddComponent<MeshCollider>();
                meshCollider.convex = false;
                colliders++;
            }

            // Static batching collapses the 289 separate pieces into one draw batch. ContributedGI
            // is deliberately left off: the model has no lightmap UVs and baking 666k vertices
            // would take forever for no benefit.
            GameObjectUtility.SetStaticEditorFlags(map,
                StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic |
                StaticEditorFlags.BatchingStatic);

            Debug.Log($"[ContentSetup] Map placed at the origin (25.0 x 4.2 x 16.6 m, scale 1:1) " +
                      $"with {colliders} mesh collider(s).");
        }

        // ------------------------------------------------------------- Scene layout

        /// <summary>
        /// Lays the placeholder ground under the imported arena, authors the spawn points and
        /// moves the pre-existing scene objects that the imported geometry disagrees with.
        ///
        /// The imported map ships WITHOUT a floor mesh - its author built walls, buildings,
        /// stairs and cover but no ground - so the floor comes from the placeholder the test
        /// scene already had, re-sized to the imported arena's footprint.
        /// </summary>
        private static void AlignScene()
        {
            RelayGround();
            AddSpawnPoints();
            MoveTo("Player", SpawnOne, 1.02f);
            MoveTo("PlayerTwo", SpawnTwo, 1f);
        }

        /// <summary>
        /// The arena ships its own floor (a 24.8 x 16.4 m plane at y = 0), so no ground is
        /// added here. The scene did carry a placeholder cube from the test scene and it has
        /// been removed deliberately - keeping it would double the floor and, at a different
        /// height, make the whole arena look like it floats.
        /// </summary>
        private static void RelayGround()
        {
            GameObject ground = null;
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (go.name == "Ground" || go.name == "Cube")
                {
                    ground = go;
                    break;
                }
            }

            if (ground == null)
            {
                Debug.Log("[ContentSetup] No leftover placeholder ground - the arena's own " +
                          "floor at y = 0 is the only one.");
                return;
            }

            // A leftover placeholder from the test scene: delete it rather than re-laying it,
            // because the imported arena already has a floor and a second slab at the wrong
            // height is worse than none.
            UnityEngine.Object.DestroyImmediate(ground, true);
            Debug.Log("[ContentSetup] Removed the leftover placeholder ground - the arena's " +
                      "own floor at y = 0 is used now.");
        }

        private static void MoveTo(string name, Vector3 horizontal, float y)
        {
            var found = GameObject.Find(name);
            if (found == null)
            {
                Debug.Log($"[ContentSetup] No object named '{name}'; nothing to move.");
                return;
            }

            var target = new Vector3(horizontal.x, y, horizontal.z);
            if (found.transform.position == target)
            {
                Debug.Log($"[ContentSetup] '{name}' is already at {target}.");
                return;
            }

            found.transform.position = target;
            Debug.Log($"[ContentSetup] '{name}' moved to {target} - the imported arena's " +
                      "geometry does not agree with the position the test scene used.");
        }

        private static void AddSpawnPoints()
        {
            int created = 0;

            created += CreateSpawnPoint("SpawnPoint1", SpawnOne);
            created += CreateSpawnPoint("SpawnPoint2", SpawnTwo);

            if (created > 0)
            {
                Debug.Log($"[ContentSetup] Created {created} spawn point(s). SpawnManager " +
                          "prefers these over its generated fallbacks, and they are sorted by " +
                          "name so slot One always gets SpawnPoint1.");
            }
        }

        private static int CreateSpawnPoint(string name, Vector3 position)
        {
            var existing = GameObject.Find(name);
            if (existing != null)
            {
                existing.transform.position = position;
                return 0;
            }

            var point = new GameObject(name);
            point.transform.SetPositionAndRotation(position, Quaternion.identity);

            return 1;
        }

        // ------------------------------------------------------------------ Shared

        /// <summary>AssetDatabase paths want forward slashes; Path.Combine writes backslashes.</summary>
        private static string AssetPath(string directory, string file)
        {
            return Path.Combine(directory, file).Replace('\\', '/');
        }

        private static void StripColliders(GameObject root)
        {
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
            {
                UnityEngine.Object.DestroyImmediate(collider, false);
            }
        }
    }

    /// <summary>Runs the content setup once, automatically, the first time this file is compiled.</summary>
    [InitializeOnLoad]
    public static class ContentSetupAuto
    {
        private static bool _busy;

        static ContentSetupAuto()
        {
            if (File.Exists(MarkerPath)) return;
            EditorApplication.update += Tick;
        }

        private const string MarkerPath = "ProjectSettings/ContentSetup.done";

        private static void Tick()
        {
            if (_busy) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            EditorApplication.update -= Tick;
            _busy = true;

            EditorApplication.delayCall += () =>
            {
                try
                {
                    ContentSetup.RunAuto();
                }
                catch (Exception e)
                {
                    Debug.LogError("[ContentSetup] Automatic setup failed. Use Tools > Manuel > " +
                                   "Setup Content instead. " + e);
                }
            };
        }
    }
}
