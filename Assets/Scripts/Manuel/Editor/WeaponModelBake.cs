using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProjectLEA.Manuel.Managers;
using UnityEditor;
using UnityEngine;

namespace ProjectLEA.Manuel.EditorTools
{
    /// <summary>
    /// Turns the imported weapon GLBs into ready-to-use first-person viewmodels.
    ///
    /// Each imported GLB is authored in its own coordinate frame: the Quaternius guns lie with
    /// their barrel along +X, the one Zsky rifle along -Z, and none of them are a sensible size
    /// (a pistol is 2.45 units long, a sniper 7.3). A raw instantiate would put a seven-metre
    /// rifle sideways across the screen. This tool reads the mesh, finds the barrel by picking
    /// the longest axis, finds the muzzle by picking the thinner end, rotates the model so the
    /// muzzle points at +Z and +Y stays up, scales it to a per-weapon length, and saves the
    /// result as a prefab whose pivot sits where the hands go.
    ///
    /// The baked prefab is then handed to the matching <see cref="WeaponDefinition"/> as its
    /// <c>viewmodelPrefab</c>, and a first-pass hand offset is written to
    /// <c>viewmodelOffset</c>. Both are regular serialized fields, so once the bake is done you
    /// tune the hold in the weapon's inspector - the bake never has to be re-run for a nudge.
    ///
    /// Menu: Tools > Manuel > Bake Weapon Models
    /// </summary>
    public static class WeaponModelBake
    {
        private const string ModelDir = "Assets/Models/Manuel/Weapons";
        private const string OutputDir = "Assets/Prefabs/Manuel/Viewmodels";
        private const string WeaponDir = "Assets/Data/Manuel/Weapons";

        /// <summary>
        /// One entry per weapon that should hold something. The model file is reused for several
        /// weapons where the real arsenal has more guns than the model collection does (there are
        /// two sidearms in the data and only one pistol model, for instance).
        ///
        /// <para>Axis and sign were measured from the actual mesh data, not assumed:</para>
        /// <list type="bullet">
        /// <item>All Quaternius guns: barrel +X, up +Y, muzzle at +X.</item>
        /// <item>AssaultRifleC (the Zsky rifle): barrel -Z, up +Y, muzzle at -Z.</item>
        /// <item>MK14 and Mpsd: barrel -Z, up +Y, muzzle at -Z. Both ship with a 0.01 root
        /// scale, so they come in ~20 cm long and get scaled back UP - their geometry itself
        /// is perfectly coherent, it is just authored in the wrong unit.</item>
        /// </list>
        /// <para>
        /// Only Knife and Odin are left without a model: there is no melee piece in the
        /// collection, and the second heavy LMG has nothing big enough to stand in for it.
        /// </para>
        /// </summary>
        private struct Mapping
        {
            public string Model;
            public string Weapon;
            public Vector3 BarrelAxis; // normalised, signed: the direction the muzzle points
            public Vector3 UpAxis;
            public float TargetLength; // metres, barrel tip to butt
            public Vector3 HoldOffset; // first-pass hand position, relative to the camera
        }

        private static readonly Mapping[] Map =
        {
            // Sidearms: small, held close and low.
            New("Pistol", "Classic", new Vector3(1f, 0f, 0f), 0.28f, new Vector3(0.16f, -0.14f, 0.30f)),
            New("Pistol", "Frenzy", new Vector3(1f, 0f, 0f), 0.28f, new Vector3(0.16f, -0.14f, 0.30f)),
            New("Pistol", "Ghost", new Vector3(1f, 0f, 0f), 0.28f, new Vector3(0.16f, -0.14f, 0.30f)),
            New("Revolver", "Sheriff", new Vector3(1f, 0f, 0f), 0.28f, new Vector3(0.16f, -0.15f, 0.32f)),

            // SMGs and the short shotguns: compact, tucked in.
            New("SMG_B", "Stinger", new Vector3(1f, 0f, 0f), 0.46f, new Vector3(0.18f, -0.18f, 0.34f)),
            New("SMG_A", "Spectre", new Vector3(1f, 0f, 0f), 0.48f, new Vector3(0.18f, -0.18f, 0.35f)),
            New("ShotgunShort", "Judge", new Vector3(1f, 0f, 0f), 0.45f, new Vector3(0.19f, -0.18f, 0.35f)),
            New("ShotgunShort", "Shorty", new Vector3(1f, 0f, 0f), 0.38f, new Vector3(0.18f, -0.17f, 0.32f)),

            // Rifles: bullpup first, it is the shortest of them.
            New("Bullpup", "Bulldog", new Vector3(1f, 0f, 0f), 0.55f, new Vector3(0.20f, -0.20f, 0.36f)),
            New("Shotgun", "Bucky", new Vector3(1f, 0f, 0f), 0.55f, new Vector3(0.20f, -0.19f, 0.38f)),
            New("AssaultRifleA", "Vandal", new Vector3(1f, 0f, 0f), 0.62f, new Vector3(0.20f, -0.20f, 0.40f)),
            New("AssaultRifleB", "Phantom", new Vector3(1f, 0f, 0f), 0.62f, new Vector3(0.20f, -0.20f, 0.40f)),
            New("AssaultRifleC", "Guardian", new Vector3(0f, 0f, -1f), 0.68f, new Vector3(0.20f, -0.20f, 0.42f)),

            // Snipers: longest, pushed out so the scope is what fills the view.
            New("SniperRifleA", "Marshal", new Vector3(1f, 0f, 0f), 0.75f, new Vector3(0.22f, -0.22f, 0.46f)),
            New("SniperRifleC", "Outlaw", new Vector3(1f, 0f, 0f), 0.75f, new Vector3(0.22f, -0.22f, 0.46f)),
            New("SniperRifleB", "Operator", new Vector3(1f, 0f, 0f), 0.80f, new Vector3(0.22f, -0.22f, 0.48f)),

            // The two austincford models. Both point down -Z with the muzzle at the thin end,
            // and both arrive 100x too small because their root carries a 0.01 scale. MK14 is a
            // full-length rifle, so it stands in for the heavy; Mpsd is a suppressed SMG, so it
            // takes the carbine.
            New("MK14", "Ares", new Vector3(0f, 0f, -1f), 0.68f, new Vector3(0.20f, -0.20f, 0.42f)),
            New("Mpsd", "Bandit", new Vector3(0f, 0f, -1f), 0.50f, new Vector3(0.18f, -0.18f, 0.34f)),
        };

        private static Mapping New(string model, string weapon, Vector3 barrel, float length, Vector3 offset)
        {
            return new Mapping
            {
                Model = model,
                Weapon = weapon,
                BarrelAxis = barrel,
                UpAxis = Vector3.up,
                TargetLength = length,
                HoldOffset = offset,
            };
        }

        [MenuItem("Tools/Manuel/Bake Weapon Models")]
        public static void Bake()
        {
            if (!Directory.Exists(ModelDir))
            {
                Debug.LogError($"[WeaponModelBake] {ModelDir} does not exist - nothing to bake.");
                return;
            }

            Directory.CreateDirectory(OutputDir);
            AssetDatabase.Refresh();

            int baked = 0;
            int skipped = 0;

            foreach (var mapping in Map)
            {
                if (!BakeOne(mapping))
                {
                    skipped++;
                    continue;
                }
                baked++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[WeaponModelBake] Done. {baked} viewmodel(s) baked into {OutputDir}, " +
                      $"{skipped} failed. Only Knife and Odin are left without a model - there " +
                      "is nothing in the collection to be a knife or a second heavy LMG.");
        }

        /// <summary>Bakes one model into one weapon's viewmodel. Returns false on any failure.</summary>
        private static bool BakeOne(Mapping mapping)
        {
            var modelPath = $"{ModelDir}/{mapping.Model}.glb";
            var weaponPath = $"{WeaponDir}/{mapping.Weapon}.asset";
            var outputPath = $"{OutputDir}/ViewModel_{mapping.Weapon}.prefab";

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (source == null)
            {
                Debug.LogWarning($"[WeaponModelBake] {modelPath} is not imported as a model yet. " +
                                 "Select it and let Unity re-import, then bake again.");
                return false;
            }

            var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(weaponPath);
            if (weapon == null)
            {
                Debug.LogWarning($"[WeaponModelBake] No WeaponDefinition at {weaponPath} - " +
                                 $"the '{mapping.Weapon}' model has nowhere to go.");
                return false;
            }

            // Build in a temporary scene root so nothing in the open scene is touched.
            var temp = new GameObject($"__bake_{mapping.Weapon}");
            var modelGo = new GameObject("Model");
            modelGo.transform.SetParent(temp.transform, false);
            var instance = Object.Instantiate(source, modelGo.transform, false);
            instance.name = mapping.Model;

            if (!Measure(instance, out var min, out var max, out var detectedMuzzle, out var detectedUp))
            {
                Debug.LogWarning($"[WeaponModelBake] {mapping.Model} has no readable mesh - " +
                                 "skipped.");
                Object.DestroyImmediate(temp);
                return false;
            }

            var size = max - min;
            var center = (min + max) * 0.5f;

            // The barrel is the longest dimension, "up" is the second longest.
            int barrelAxis = LongestAxis(size);
            int upAxis = SecondLongestAxis(size);
            float span = size[barrelAxis];

            if (span <= Mathf.Epsilon)
            {
                Debug.LogWarning($"[WeaponModelBake] {mapping.Model} is degenerate (zero size).");
                Object.DestroyImmediate(temp);
                return false;
            }

            // A gun never sits with its barrel pointing at the floor or the sky in the source
            // file, so the remaining axis is the gun's thin direction.
            Vector3 upWorld = Vector3.zero;
            upWorld[upAxis] = 1f;

            if (detectedMuzzle.sqrMagnitude > Mathf.Epsilon &&
                Vector3.Dot(detectedMuzzle, mapping.BarrelAxis) < 0.9f)
            {
                Debug.LogWarning($"[WeaponModelBake] {mapping.Model}: the mesh's thin end points " +
                                 $"at {detectedMuzzle} but the mapping says {mapping.BarrelAxis}. " +
                                 "The mapping wins - check the baked gun is not backwards.");
            }

            // Rotate so the muzzle points at +Z and the gun's up stays +Y. LookRotation builds the
            // rotation that carries +Z onto the barrel direction, so the one that carries the
            // barrel onto +Z is its inverse.
            var rotation = Quaternion.Inverse(Quaternion.LookRotation(mapping.BarrelAxis, mapping.UpAxis));
            float scale = mapping.TargetLength / span;

            // The pivot goes where the hands are: 30% of the barrel length back from the muzzle
            // end, centred on the other two axes. That keeps the receiver - not the barrel -
            // sitting in the camera's lower right.
            var pivot = center;
            pivot[barrelAxis] = min[barrelAxis] + span * 0.30f;

            // The model's points currently sit in the Model object's local space (it is at the
            // world origin with identity rotation, so local == world here). After applying the
            // Model's transform the pivot must land on the prefab's origin:
            //   worldPoint = localPosition + rotation * (scale * localPoint)
            // so localPosition = -rotation * (scale * pivot).
            modelGo.transform.SetPositionAndRotation(
                -(rotation * (scale * pivot)),
                rotation);
            modelGo.transform.localScale = Vector3.one * scale;

            // A viewmodel must never be able to block its own shot: WeaponUser raycasts from the
            // camera, so any collider on the held gun would eat the bullet.
            StripColliders(temp);

            temp.name = $"ViewModel_{mapping.Weapon}";
            var prefab = PrefabUtility.SaveAsPrefabAsset(temp, outputPath);
            Object.DestroyImmediate(temp);

            if (prefab == null)
            {
                Debug.LogError($"[WeaponModelBake] Could not write {outputPath}.");
                return false;
            }

            ApplyToWeapon(weapon, prefab, mapping, size, span, scale);

            Debug.Log($"[WeaponModelBake] {mapping.Weapon} <- {mapping.Model}.glb: baked to " +
                      $"{mapping.TargetLength:F2} m (source span {span:F2}, scale {scale:F4}), " +
                      $"thin end detected at {detectedMuzzle}, up at {detectedUp}.");
            return true;
        }

        /// <summary>
        /// Walks every mesh in the instance and produces the combined bounds, plus which end of
        /// the longest axis is thin enough to be the muzzle.
        /// </summary>
        private static bool Measure(GameObject instance, out Vector3 min, out Vector3 max,
                                    out Vector3 detectedMuzzle, out Vector3 detectedUp)
        {
            min = Vector3.one * float.PositiveInfinity;
            max = Vector3.one * float.NegativeInfinity;
            detectedMuzzle = Vector3.zero;
            detectedUp = Vector3.zero;

            var filters = instance.GetComponentsInChildren<MeshFilter>(true);
            if (filters.Length == 0) return false;

            var allPoints = new List<Vector3>(1024);
            foreach (var filter in filters)
            {
                if (filter == null || filter.sharedMesh == null) continue;
                var mesh = filter.sharedMesh;
                var vertices = mesh.vertices;
                var localToWorld = filter.transform.localToWorldMatrix;
                for (int i = 0; i < vertices.Length; i++)
                {
                    var p = localToWorld.MultiplyPoint(vertices[i]);
                    allPoints.Add(p);
                    for (int a = 0; a < 3; a++)
                    {
                        if (p[a] < min[a]) min[a] = p[a];
                        if (p[a] > max[a]) max[a] = p[a];
                    }
                }
            }

            if (allPoints.Count == 0) return false;

            var size = max - min;
            int barrelAxis = LongestAxis(size);
            int upAxis = SecondLongestAxis(size);
            detectedUp = Vector3.zero;
            detectedUp[upAxis] = 1f;

            // The thinner 15% band at either end is the muzzle. Median distance from the band's
            // own centroid axis is used rather than a bounding radius, because one stray sight or
            // stock vertex would otherwise make a clean barrel look fat.
            float frontRadius = BandMedian(allPoints, barrelAxis, min[barrelAxis], max[barrelAxis], 0.00f, 0.15f);
            float backRadius = BandMedian(allPoints, barrelAxis, min[barrelAxis], max[barrelAxis], 0.85f, 1.00f);

            detectedMuzzle = Vector3.zero;
            if (frontRadius >= 0f && backRadius >= 0f)
            {
                // The thinner end is the muzzle, so the barrel points that way.
                float sign = backRadius < frontRadius ? 1f : -1f;
                detectedMuzzle[barrelAxis] = sign;
            }

            return true;
        }

        /// <summary>Median distance of a band of vertices from that band's own centroid axis.</summary>
        private static float BandMedian(List<Vector3> points, int axis, float lo, float hi,
                                        float fromFrac, float toFrac)
        {
            float span = hi - lo;
            float a = lo + span * fromFrac;
            float b = lo + span * toFrac;

            int u = (axis + 1) % 3;
            int v = (axis + 2) % 3;

            float cu = 0f;
            float cv = 0f;
            int count = 0;
            foreach (var p in points)
            {
                if (p[axis] < a || p[axis] > b) continue;
                cu += p[u];
                cv += p[v];
                count++;
            }
            if (count == 0) return -1f;
            cu /= count;
            cv /= count;

            var distances = new float[count];
            int index = 0;
            foreach (var p in points)
            {
                if (p[axis] < a || p[axis] > b) continue;
                float du = p[u] - cu;
                float dv = p[v] - cv;
                distances[index++] = Mathf.Sqrt(du * du + dv * dv);
            }

            System.Array.Sort(distances);
            return distances[count / 2];
        }

        private static int LongestAxis(Vector3 size)
        {
            if (size.x >= size.y && size.x >= size.z) return 0;
            if (size.y >= size.z) return 1;
            return 2;
        }

        private static int SecondLongestAxis(Vector3 size)
        {
            if (size.x >= size.y && size.x >= size.z)
            {
                return size.y >= size.z ? 1 : 2;
            }
            if (size.y >= size.x && size.y >= size.z)
            {
                return size.x >= size.z ? 0 : 2;
            }
            return size.x >= size.y ? 0 : 1;
        }

        private static void StripColliders(GameObject root)
        {
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
            {
                Object.DestroyImmediate(collider, false);
            }
        }

        /// <summary>Hands the baked prefab and a first-pass hold offset to the weapon data.</summary>
        private static void ApplyToWeapon(WeaponDefinition weapon, GameObject prefab, Mapping mapping,
                                         Vector3 sourceSize, float span, float scale)
        {
            var serialized = new SerializedObject(weapon);

            var prefabProp = serialized.FindProperty("viewmodelPrefab");
            var offsetProp = serialized.FindProperty("viewmodelOffset");
            if (prefabProp == null || offsetProp == null)
            {
                Debug.LogWarning($"[WeaponModelBake] {mapping.Weapon} is missing the viewmodel " +
                                 "fields - recompile WeaponDefinition and bake again.");
                return;
            }

            prefabProp.objectReferenceValue = prefab;
            offsetProp.vector3Value = mapping.HoldOffset;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(weapon);
        }
    }
}
