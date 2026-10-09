using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Guardianes.Combate;
using Guardianes.Enemigos;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Guardianes.EditorTools.Enemigos
{
    /// <summary>
    /// Trae a los humanos de AssetsCreator (MPFB2 + clips procedurales de fusil) y monta sus
    /// prefabs: FBX animado con el hueso del arma, AnimatorController (locomocion, apuntar,
    /// rafaga, huida, impacto, muerte), el arma en la mano, colision, vida y la IA (Humano).
    /// Genera los humanos antes con creador.py de AssetsCreator.
    /// </summary>
    public static class ConstructorHumanos
    {
        public const string CarpetaArte = "Assets/_Game/Art/Humanos";
        public const string CarpetaPrefabs = "Assets/_Game/Prefabs/Enemigos";
        const string RaizAssetsCreator = @"..\..\AssetsCreator\AssetsCreatorWin\proyectos";

        public struct Def
        {
            public string nombre;     // proyecto de AssetsCreator (kk_mercenario...)
            public string prefab;     // nombre del prefab
            public float vida;
            public float danioBala;
            /// <summary>Sufijo del FBX animado (por defecto el de los clips de fusil).</summary>
            public string animado;
            /// <summary>Desarmado: no lleva arma ni dispara.</summary>
            public bool civil;
        }

        /// <summary>El mercenario de Ferrox de King Kong (el de toda la saga).</summary>
        public static readonly Def Mercenario = new Def { nombre = "kk_mercenario", prefab = "Mercenario", vida = 30f, danioBala = 3.5f };

        public static void ConstruirTodos(IEnumerable<Def> humanos, string arma = "kk_ak47")
        {
            if (!string.IsNullOrEmpty(arma)) CopiarArma(arma);
            foreach (var d in humanos) Construir(d, arma);
            AssetDatabase.SaveAssets();
        }

        static string Origen(string proyecto) =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", RaizAssetsCreator, proyecto, "salida"));

        // ------------------------------------------------------------------ Copia desde AssetsCreator
        static string Copiar(string proyecto, string carpetaDestino, Func<string, bool> filtro)
        {
            string origen = Origen(proyecto);
            if (!Directory.Exists(origen)) throw new DirectoryNotFoundException("No existe " + origen + " (genera el humano con creador.py)");
            Directory.CreateDirectory(carpetaDestino);
            foreach (var f in Directory.GetFiles(origen))
            {
                string n = Path.GetFileName(f);
                if (!filtro(n)) continue;
                File.Copy(f, Path.Combine(carpetaDestino, n), true);
            }
            string fbm = Path.Combine(origen, proyecto + ".fbm");
            if (Directory.Exists(fbm))
                foreach (var f in Directory.GetFiles(fbm))
                    File.Copy(f, Path.Combine(carpetaDestino, Path.GetFileName(f)), true);
            return carpetaDestino;
        }

        static void CopiarArma(string proyecto)
        {
            string dest = $"{CarpetaArte}/Armas/{proyecto}";
            Copiar(proyecto, dest, n => n.StartsWith(proyecto + "_pbr") && (n.EndsWith(".obj") || n.EndsWith(".png")));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        // ------------------------------------------------------------------ Humano
        public static GameObject Construir(Def d, string arma = "kk_ak47")
        {
            string dest = $"{CarpetaArte}/{d.prefab}";
            string sufijo = string.IsNullOrEmpty(d.animado) ? "_animado_fusil_ak" : d.animado;
            string fbx = d.nombre + sufijo + ".fbx";
            Copiar(d.nombre, dest, n => n == fbx || n.EndsWith(".png") || n.EndsWith(".clips.json"));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            string rutaFbx = $"{dest}/{fbx}";
            ConfigurarModelo(rutaFbx, $"{dest}/{d.nombre}{sufijo}.clips.json");

            var clips = AssetDatabase.LoadAllAssetsAtPath(rutaFbx).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToDictionary(c => c.name);
            Debug.Log($"[GD] {d.prefab}: clips {string.Join(", ", clips.Keys)}");
            var controlador = CrearControlador($"{dest}/{d.prefab}.controller", clips);

            var modelo = AssetDatabase.LoadAssetAtPath<GameObject>(rutaFbx);
            var raiz = new GameObject(d.prefab);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(modelo);
            inst.name = "Modelo";
            inst.transform.SetParent(raiz.transform, false);
            // (?? no sirve con objetos de Unity: GetComponent devuelve un "nulo falso").
            var animator = inst.GetComponent<Animator>();
            if (animator == null) animator = inst.AddComponent<Animator>();
            animator.runtimeAnimatorController = controlador;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                smr.updateWhenOffscreen = false;
                smr.skinnedMotionVectors = false;
            }

            // Arma en el hueso del arma (AC_arma_r, el hueco del puno) con su bocacha.
            var huesoArma = Hueso(inst.transform, "AC_arma_r");
            Transform punta = null;
            if (huesoArma != null && !d.civil && !string.IsNullOrEmpty(arma)) punta = PonerArma(inst.transform, huesoArma, arma);
            else if (huesoArma == null) Debug.LogWarning($"[GD] {d.prefab}: el FBX no tiene AC_arma_r");

            var col = raiz.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, 0.9f, 0f);
            col.radius = 0.35f;
            col.height = 1.8f;
            var salud = raiz.AddComponent<Salud>();
            salud.maxima = salud.actual = d.vida;
            var h = raiz.AddComponent<Humano>();
            h.danioBala = d.danioBala;
            h.bocacha = punta != null ? punta : huesoArma;
            h.civil = d.civil;

            Directory.CreateDirectory(CarpetaPrefabs);
            bool conArma = huesoArma != null, conBocacha = punta != null;
            string ruta = $"{CarpetaPrefabs}/{d.prefab}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(raiz, ruta);
            UnityEngine.Object.DestroyImmediate(raiz);
            Debug.Log($"[GD] Prefab {d.prefab} listo (arma={conArma && !d.civil}, bocacha={conBocacha})");
            return prefab;
        }

        static void ConfigurarModelo(string rutaFbx, string rutaClips)
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(rutaFbx);
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.importAnimation = true;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.importBlendShapes = false;
            mi.optimizeGameObjects = false;
            // Los huesos de anclaje (AC_arma_*) no pesan en la piel: sin esto Unity los quita.
            mi.optimizeBones = false;
            var bucles = new HashSet<string>();
            if (File.Exists(rutaClips))
            {
                var json = JsonUtility.FromJson<ClipsJson>(File.ReadAllText(rutaClips));
                foreach (var c in json.clips) if (c.loop) bucles.Add(c.nombre);
            }
            var defs = mi.defaultClipAnimations;
            foreach (var c in defs)
            {
                c.loopTime = bucles.Contains(c.name);
                c.lockRootRotation = true;
                c.lockRootHeightY = true;
                c.lockRootPositionXZ = true;
                c.keepOriginalOrientation = true;
                c.keepOriginalPositionY = true;
                c.keepOriginalPositionXZ = true;
            }
            mi.clipAnimations = defs;
            mi.SaveAndReimport();
        }

        [Serializable] class ClipsJson { public ClipJson[] clips; }
        [Serializable] class ClipJson { public string nombre; public bool loop; }

        static Transform Hueso(Transform raiz, string nombre) =>
            raiz.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == nombre);

        /// <summary>
        /// Ejes de un fusil a partir de su malla (en el espacio del objeto): el canon es el eje mas
        /// largo y la boca el extremo con mas detalle (mira, bocacha) que la cantonera; "abajo" es
        /// hacia donde cuelgan el cargador y la empunadura, que esta a ~28 % del largo desde la culata.
        /// </summary>
        static void EjesFusil(GameObject arma, out Vector3 canon, out Vector3 arriba, out Vector3 empunadura, out Vector3 boca)
        {
            var mf = arma.GetComponentInChildren<MeshFilter>();
            var vs = mf.sharedMesh.vertices;
            var p = new Vector3[vs.Length];
            var b = new Bounds(arma.transform.InverseTransformPoint(mf.transform.TransformPoint(vs[0])), Vector3.zero);
            for (int i = 0; i < vs.Length; i++)
            {
                p[i] = arma.transform.InverseTransformPoint(mf.transform.TransformPoint(vs[i]));
                b.Encapsulate(p[i]);
            }
            Vector3 s = b.size;
            int e = s.x >= s.y && s.x >= s.z ? 0 : s.y >= s.z ? 1 : 2;
            int o1 = (e + 1) % 3, o2 = (e + 2) % 3;
            int v = s[o1] >= s[o2] ? o1 : o2;
            int w = v == o1 ? o2 : o1;
            float lo = b.min[e], hi = b.max[e], largo = hi - lo;
            int nLo = 0, nHi = 0;
            foreach (var q in p)
            {
                if (q[e] < lo + largo * 0.15f) nLo++;
                if (q[e] > hi - largo * 0.15f) nHi++;
            }
            float signo = nHi >= nLo ? 1f : -1f;
            float extremoBoca = signo > 0f ? hi : lo;
            // Altura de la linea del canon: media de los vertices de la boca.
            float linea = 0f;
            int n = 0;
            foreach (var q in p)
                if (Mathf.Abs(q[e] - extremoBoca) < largo * 0.15f) { linea += q[v]; n++; }
            linea /= Mathf.Max(1, n);
            float sentidoArriba = (b.max[v] - linea) < (linea - b.min[v]) ? 1f : -1f;
            // Empunadura: franja a 28 % desde la culata; a media altura entre el canon y lo mas bajo.
            float xg = signo > 0f ? lo + largo * 0.28f : hi - largo * 0.28f;
            float bajo = linea;
            foreach (var q in p)
                if (Mathf.Abs(q[e] - xg) < largo * 0.05f && (q[v] - linea) * sentidoArriba < (bajo - linea) * sentidoArriba) bajo = q[v];
            Vector3 Eje(int i) => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;
            canon = Eje(e) * signo;
            arriba = Eje(v) * sentidoArriba;
            empunadura = Eje(e) * xg + Eje(v) * Mathf.Lerp(linea, bajo, 0.55f) + Eje(w) * b.center[w];
            boca = Eje(e) * extremoBoca + Eje(v) * linea + Eje(w) * b.center[w];
        }

        /// <summary>
        /// Cuelga el arma del hueso del puno con la empunadura en el hueco de la mano: la empunadura
        /// va por la linea de nudillos (del menique al indice, "arriba" del fusil) y el canon hacia
        /// donde apuntan los nudillos desde la muneca. Devuelve la bocacha.
        /// </summary>
        static Transform PonerArma(Transform raiz, Transform hueso, string proyecto)
        {
            string ruta = $"{CarpetaArte}/Armas/{proyecto}/{proyecto}_pbr.obj";
            var modeloArma = AssetDatabase.LoadAssetAtPath<GameObject>(ruta);
            if (modeloArma == null) { Debug.LogWarning("[GD] Falta el arma: " + ruta); return hueso; }
            var arma = (GameObject)PrefabUtility.InstantiatePrefab(modeloArma);
            arma.name = "Arma";
            foreach (var c in arma.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(c);
            EjesFusil(arma, out var canonLocal, out var arribaLocal, out var empunadura, out var bocaLocal);

            Transform mano = Hueso(raiz, "hand_r"), indice = Hueso(raiz, "index_01_r"), menique = Hueso(raiz, "pinky_01_r"), corazon = Hueso(raiz, "middle_01_r");
            Quaternion giro = hueso.rotation;
            if (mano != null && indice != null && menique != null && corazon != null)
            {
                Vector3 arriba = (indice.position - menique.position).normalized;
                Vector3 nudillos = corazon.position - mano.position;
                Vector3 canon = (nudillos - Vector3.Dot(nudillos, arriba) * arriba).normalized;
                // Lleva (canon, arriba) del fusil a (canon, arriba) de la mano.
                giro = Quaternion.LookRotation(Vector3.Cross(canon, arriba), arriba) *
                       Quaternion.Inverse(Quaternion.LookRotation(Vector3.Cross(canonLocal, arribaLocal), arribaLocal));
            }
            else Debug.LogWarning("[GD] Sin huesos de la mano derecha: el arma va con el giro del hueso");
            arma.transform.SetPositionAndRotation(hueso.position - giro * empunadura, giro);
            arma.transform.SetParent(hueso, true);

            var boca = new GameObject("Bocacha").transform;
            boca.SetParent(arma.transform, false);
            boca.localPosition = bocaLocal;
            Debug.Log($"[GD] Arma: canon {canonLocal}, arriba {arribaLocal}, empunadura {empunadura}, boca {bocaLocal}");
            return boca;
        }

        // ------------------------------------------------------------------ Animator
        static AnimatorController CrearControlador(string ruta, Dictionary<string, AnimationClip> c)
        {
            AssetDatabase.DeleteAsset(ruta);
            var ac = AnimatorController.CreateAnimatorControllerAtPath(ruta);
            ac.AddParameter("velocidad", AnimatorControllerParameterType.Float);
            ac.AddParameter("apuntar", AnimatorControllerParameterType.Bool);
            ac.AddParameter("disparar", AnimatorControllerParameterType.Bool);
            ac.AddParameter("miedo", AnimatorControllerParameterType.Bool);
            ac.AddParameter("golpe", AnimatorControllerParameterType.Trigger);
            ac.AddParameter("muerto", AnimatorControllerParameterType.Trigger);
            var sm = ac.layers[0].stateMachine;

            AnimationClip Clip(params string[] nombres)
            {
                foreach (var n in nombres) if (c.TryGetValue(n, out var clip)) return clip;
                return null;
            }

            var arbol = new BlendTree { name = "Locomocion", blendParameter = "velocidad", blendType = BlendTreeType.Simple1D, useAutomaticThresholds = false };
            AssetDatabase.AddObjectToAsset(arbol, ac);
            arbol.AddChild(Clip("fusil_ak_idle", "idle"), 0f);
            arbol.AddChild(Clip("fusil_ak_andar", "walk"), 1.6f);
            arbol.AddChild(Clip("run"), 5.5f);
            var loco = sm.AddState("Locomocion");
            loco.motion = arbol;
            sm.defaultState = loco;

            var apuntar = sm.AddState("Apuntar"); apuntar.motion = Clip("fusil_ak_apuntar_idle");
            var disparar = sm.AddState("Disparar"); disparar.motion = Clip("fusil_ak_rafaga", "fusil_ak_disparar");
            var huir = sm.AddState("Huir"); huir.motion = Clip("sprint", "run");
            var golpe = sm.AddState("Golpe"); golpe.motion = Clip("hit_bala", "hit_frontal");
            var muerte = sm.AddState("Muerte"); muerte.motion = Clip("death", "muerte_rodillas");

            AnimatorStateTransition T(AnimatorState de, AnimatorState a, float mezcla = 0.2f)
            {
                var t = de.AddTransition(a);
                t.hasExitTime = false;
                t.duration = mezcla;
                return t;
            }

            T(loco, apuntar).AddCondition(AnimatorConditionMode.If, 0, "apuntar");
            T(apuntar, loco).AddCondition(AnimatorConditionMode.IfNot, 0, "apuntar");
            T(apuntar, disparar, 0.1f).AddCondition(AnimatorConditionMode.If, 0, "disparar");
            T(disparar, apuntar, 0.15f).AddCondition(AnimatorConditionMode.IfNot, 0, "disparar");
            T(loco, huir).AddCondition(AnimatorConditionMode.If, 0, "miedo");
            T(apuntar, huir).AddCondition(AnimatorConditionMode.If, 0, "miedo");
            T(disparar, huir).AddCondition(AnimatorConditionMode.If, 0, "miedo");
            T(huir, loco, 0.3f).AddCondition(AnimatorConditionMode.IfNot, 0, "miedo");
            var g = sm.AddAnyStateTransition(golpe);
            g.hasExitTime = false; g.duration = 0.08f; g.canTransitionToSelf = false;
            g.AddCondition(AnimatorConditionMode.If, 0, "golpe");
            var vuelta = golpe.AddTransition(loco);
            vuelta.hasExitTime = true; vuelta.exitTime = 0.85f; vuelta.duration = 0.15f;
            var m = sm.AddAnyStateTransition(muerte);
            m.hasExitTime = false; m.duration = 0.1f; m.canTransitionToSelf = false;
            m.AddCondition(AnimatorConditionMode.If, 0, "muerto");
            return ac;
        }
    }
}
