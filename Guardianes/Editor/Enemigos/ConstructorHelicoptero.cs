using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Guardianes.Combate;
using Guardianes.Enemigos;
using UnityEditor;
using UnityEngine;

namespace Guardianes.EditorTools.Enemigos
{
    /// <summary>
    /// Monta el prefab del helicoptero de ataque y el de su cohete. El fuselaje, el metal, las ruedas,
    /// los cristales y la torreta salen de Tools~/vehiculos/sdf_helicoptero.py (OBJ por material, sin UV:
    /// materiales triplanares en espacio de objeto, que el aparato se mueve). Los rotores se hacen
    /// aqui (palas con perfil NACA 0012, torsion y conicidad, buje, rotor de cola en tijera y discos
    /// semitransparentes que hacen de estela de las palas a vueltas) y se guardan en un .asset.
    /// </summary>
    public static class ConstructorHelicoptero
    {
        public const string Carpeta = "Assets/_Game/Art/Vehiculos/Helicoptero";
        const string CarpetaMateriales = Carpeta + "/Materiales";
        const string RutaMallas = Carpeta + "/Rotores.asset";
        const string RutaDisco = Carpeta + "/disco_rotor.png";
        const string Instalaciones = "Assets/_Game/Art/Instalaciones/Materiales";
        public const string RutaPrefab = ConstructorHumanos.CarpetaPrefabs + "/Helicoptero.prefab";
        public const string RutaCohete = ConstructorHumanos.CarpetaPrefabs + "/Cohete.prefab";

        // Puntos del modelo en Unity (ver la cabecera de sdf_helicoptero.py).
        static readonly Vector3 Mastil = new Vector3(0f, 3.56f, -0.3f);
        static readonly Vector3 Cola = new Vector3(-0.36f, 3.85f, -9.3f);
        static readonly Vector3 Torreta = new Vector3(0f, 0.6f, 3.55f);
        const float RadioRotor = 7.3f, RadioCola = 1.4f;

        [MenuItem("Guardianes/Construir helicoptero")]
        public static GameObject Construir()
        {
            var mats = Materiales();
            var mallas = MallasRotores();
            var cohete = Cohete(mats["Clara"]);

            var raiz = new GameObject("Helicoptero");
            Mesh Obj(string nombre)
            {
                var m = AssetDatabase.LoadAllAssetsAtPath($"{Carpeta}/{nombre}.obj").OfType<Mesh>().FirstOrDefault();
                if (m == null) throw new FileNotFoundException($"Falta {Carpeta}/{nombre}.obj (python <Guardianes>/Tools~/vehiculos/sdf_helicoptero.py --salida <proyecto>/Assets/_Game/Art/Vehiculos/Helicoptero)");
                return m;
            }
            MeshRenderer Pieza(Transform padre, string nombre, Mesh malla, Material mat, Vector3 pos, Quaternion? rot = null)
            {
                var go = new GameObject(nombre);
                go.transform.SetParent(padre, false);
                go.transform.localPosition = pos;
                go.transform.localRotation = rot ?? Quaternion.identity;
                go.AddComponent<MeshFilter>().sharedMesh = malla;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                return r;
            }
            var t = raiz.transform;
            var fuselaje = Pieza(t, "Fuselaje", Obj("fuselaje"), mats["Pintura"], Vector3.zero);
            var metal = Pieza(t, "Metal", Obj("metal"), mats["Metal"], Vector3.zero);
            var ruedas = Pieza(t, "Ruedas", Obj("ruedas"), mats["Goma"], Vector3.zero);
            var cabina = Pieza(t, "Cabina", Obj("cabina"), mats["Cristal"], Vector3.zero);

            var torreta = new GameObject("Torreta").transform;
            torreta.SetParent(t, false);
            torreta.localPosition = Torreta;
            var canon = Pieza(torreta, "Canon", Obj("canon"), mats["Metal"], Vector3.zero);
            var bocacha = new GameObject("Bocacha").transform;
            bocacha.SetParent(canon.transform, false);
            bocacha.localPosition = new Vector3(0f, 0f, 1.65f);

            // Rotor principal: buje, cuatro palas y el disco de estela.
            var rotor = new GameObject("RotorPrincipal").transform;
            rotor.SetParent(t, false);
            rotor.localPosition = Mastil;
            var buje = Pieza(rotor, "Buje", mallas["Buje"], mats["Metal"], Vector3.zero);
            for (int i = 0; i < 4; i++)
                Pieza(rotor, "Pala" + i, mallas["Pala"], mats["Palas"], new Vector3(0f, 0.18f, 0f), Quaternion.Euler(0f, i * 90f, 0f));
            var disco = Pieza(rotor, "Disco", mallas["Disco"], mats["Disco"], new Vector3(0f, 0.3f, 0f));
            disco.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Rotor de cola a la izquierda de la deriva: cuatro palas en tijera (55 grados).
            var rotorCola = new GameObject("RotorCola").transform;
            rotorCola.SetParent(t, false);
            rotorCola.localPosition = Cola;
            var palasCola = Pieza(rotorCola, "PalasCola", mallas["PalasCola"], mats["Palas"], Vector3.zero);
            var discoCola = Pieza(rotorCola, "DiscoCola", mallas["DiscoCola"], mats["DiscoCola"], Vector3.zero);
            discoCola.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            Transform Punto(string nombre, Vector3 pos)
            {
                var p = new GameObject(nombre).transform;
                p.SetParent(t, false);
                p.localPosition = pos;
                return p;
            }
            var lanzacohetes = new[] { Punto("Lanzacohetes_I", new Vector3(-2.35f, 1.08f, 0.95f)), Punto("Lanzacohetes_D", new Vector3(2.35f, 1.08f, 0.95f)) };
            var toberas = new[] { Punto("Tobera_I", new Vector3(-1.32f, 2.5f, -2.7f)), Punto("Tobera_D", new Vector3(1.32f, 2.5f, -2.7f)) };

            // Colision: capsulas y cajas que envuelven el aparato (los rayos y las ondas lo buscan).
            var colisiones = new GameObject("Colisiones").transform;
            colisiones.SetParent(t, false);
            void Capsula(string n, Vector3 c, float radio, float alto)
            {
                var go = new GameObject(n);
                go.transform.SetParent(colisiones, false);
                var cc = go.AddComponent<CapsuleCollider>();
                cc.direction = 2;
                cc.center = c;
                cc.radius = radio;
                cc.height = alto;
            }
            void Caja(string n, Vector3 c, Vector3 tam)
            {
                var go = new GameObject(n);
                go.transform.SetParent(colisiones, false);
                var bc = go.AddComponent<BoxCollider>();
                bc.center = c;
                bc.size = tam;
            }
            Capsula("Cuerpo", new Vector3(0f, 1.55f, 0.7f), 0.95f, 8.6f);
            Capsula("Botalon", new Vector3(0f, 2.1f, -5.9f), 0.42f, 7.8f);
            Caja("Deriva", new Vector3(0f, 3.15f, -9.2f), new Vector3(0.35f, 2.3f, 1.7f));
            Caja("Alas", new Vector3(0f, 1.3f, 0.45f), new Vector3(5.6f, 1.0f, 2.2f));
            Caja("Motores", new Vector3(0f, 2.4f, -0.6f), new Vector3(2.8f, 0.9f, 3.2f));
            Caja("Tren", new Vector3(0f, 0.4f, 1.85f), new Vector3(2.7f, 0.8f, 0.8f));

            // Fisica, vida, agarre y comportamiento.
            var rb = raiz.AddComponent<Rigidbody>();
            rb.mass = 8000f;
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            var salud = raiz.AddComponent<Salud>();
            salud.maxima = salud.actual = 700f;
            salud.resistenciaBalas = 0f; // las balas de la compania no lo tocan
            var lanzable = raiz.AddComponent<Lanzable>();
            lanzable.danio = 260f;
            lanzable.radioImpacto = 14f;
            lanzable.explota = true;
            lanzable.volador = true;
            var heli = raiz.AddComponent<Helicoptero>();
            lanzable.puntoAgarre = heli.puntoAgarre;
            heli.rotorPrincipal = rotor;
            heli.rotorCola = rotorCola;
            heli.torreta = torreta;
            heli.canon = canon.transform;
            heli.bocacha = bocacha;
            heli.lanzacohetes = lanzacohetes;
            heli.toberas = toberas;
            heli.discos = new Renderer[] { disco, discoCola };
            heli.cuerpo = new Renderer[] { fuselaje, metal, ruedas, canon, buje, palasCola };
            heli.cristales = cabina;
            heli.materialQuemado = mats["Quemado"];
            heli.prefabCohete = cohete;

            var audio = raiz.AddComponent<AudioSource>();
            audio.clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{Audio.ConstructorAudio.Carpeta}/vehiculos/rotor_0.wav");
            if (audio.clip == null) Debug.LogWarning("[GD] Sin el sonido del rotor (generar_sonidos.py --solo rotor)");
            audio.loop = true;
            audio.playOnAwake = false;
            audio.spatialBlend = 1f;
            audio.rolloffMode = AudioRolloffMode.Logarithmic;
            audio.minDistance = 40f;
            audio.maxDistance = 2500f;
            audio.dopplerLevel = 0.6f;
            audio.spread = 60f;
            audio.priority = 64;
            audio.volume = 0f;
            heli.sonidoRotor = audio;

            foreach (var r in raiz.GetComponentsInChildren<MeshRenderer>())
                if (r != disco && r != discoCola) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            int triangulos = 0;
            foreach (var mf in raiz.GetComponentsInChildren<MeshFilter>()) triangulos += (int)mf.sharedMesh.GetIndexCount(0) / 3;
            Directory.CreateDirectory(ConstructorHumanos.CarpetaPrefabs);
            var prefab = PrefabUtility.SaveAsPrefabAsset(raiz, RutaPrefab);
            UnityEngine.Object.DestroyImmediate(raiz);
            AssetDatabase.SaveAssets();
            Debug.Log($"[GD] Prefab Helicoptero listo ({triangulos} triangulos)");
            return prefab;
        }

        public static void ConstruirDesdeBatch()
        {
            int codigo = 0;
            try { Construir(); }
            catch (Exception e) { Debug.LogException(e); codigo = 1; }
            EditorApplication.Exit(codigo);
        }

        // ------------------------------------------------------------------ Materiales
        static Dictionary<string, Material> Materiales()
        {
            Texture2D T(string set, string mapa) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{Instalaciones}/{set}/{set}_{mapa}.png");
            Material Triplanar(string nombre, string set, Color color, float metal, float suavidadMax, float metrosPorTesela, bool albedo = false)
            {
                return MaterialesLit.Crear($"{CarpetaMateriales}/{nombre}.mat", "HDRP/Lit", m =>
                {
                    m.SetTexture("_BaseColorMap", albedo ? T(set, "albedo") : null);
                    m.SetTexture("_NormalMap", T(set, "normal"));
                    m.SetTexture("_MaskMap", T(set, "mascara"));
                    m.SetColor("_BaseColor", color);
                    m.SetFloat("_Metallic", metal);
                    m.SetFloat("_MetallicRemapMin", 0f);
                    m.SetFloat("_MetallicRemapMax", metal);
                    m.SetFloat("_SmoothnessRemapMin", 0f);
                    m.SetFloat("_SmoothnessRemapMax", suavidadMax);
                    m.SetFloat("_NormalScale", 1f);
                    m.SetFloat("_UVBase", 5f);                   // triplanar...
                    m.SetFloat("_ObjectSpaceUVMapping", 1f);     // ...en espacio de objeto: viaja con el aparato
                    m.SetFloat("_TexWorldScale", 1f / metrosPorTesela);
                    m.SetFloat("_MaterialID", 1f);
                });
            }
            var d = new Dictionary<string, Material>
            {
                // Verde oliva mate de la compania (~8 % de albedo lineal, como la pintura militar real);
                // palas gris oscuro; chapa pavonada; goma; restos quemados.
                ["Pintura"] = Triplanar("Pintura", "pintura_mate", new Color(0.33f, 0.345f, 0.28f), 0f, 0.55f, 1.5f),
                ["Palas"] = Triplanar("Palas", "pintura_mate", new Color(0.13f, 0.13f, 0.125f), 0f, 0.5f, 1f),
                ["Clara"] = Triplanar("PinturaClara", "pintura_mate", new Color(0.62f, 0.62f, 0.58f), 0f, 0.5f, 0.5f),
                ["Metal"] = Triplanar("Metal", "metal", new Color(0.16f, 0.16f, 0.16f), 0.85f, 0.65f, 0.8f),
                ["Goma"] = Triplanar("Goma", "goma", new Color(0.05f, 0.05f, 0.05f), 0f, 0.35f, 0.6f),
                ["Quemado"] = Triplanar("Quemado", "oxido", new Color(0.035f, 0.032f, 0.03f), 0f, 0.15f, 1.2f),
            };
            d["Cristal"] = MaterialesLit.Crear($"{CarpetaMateriales}/Cristal.mat", "HDRP/Lit", m =>
            {
                m.SetColor("_BaseColor", new Color(0.03f, 0.045f, 0.05f));
                m.SetFloat("_Metallic", 0.2f);
                m.SetFloat("_Smoothness", 0.96f);
                m.SetFloat("_MaterialID", 1f);
            });
            var disco = TexturaDisco();
            Material Disco(string nombre, float alfa) => MaterialesLit.Crear($"{CarpetaMateriales}/{nombre}.mat", "HDRP/Lit", m =>
            {
                m.SetTexture("_BaseColorMap", disco);
                m.SetColor("_BaseColor", new Color(0.1f, 0.1f, 0.095f, alfa));
                m.SetFloat("_Metallic", 0f);
                m.SetFloat("_Smoothness", 0.3f);
                m.SetFloat("_SurfaceType", 1f);          // transparente
                m.SetFloat("_BlendMode", 0f);            // alfa
                m.SetFloat("_AlphaCutoffEnable", 0f);
                m.SetFloat("_DoubleSidedEnable", 1f);
                m.SetFloat("_EnableFogOnTransparent", 1f);
                m.SetFloat("_TransparentWritingMotionVec", 0f);
                m.SetFloat("_MaterialID", 1f);
            });
            d["Disco"] = Disco("DiscoRotor", 0.8f);
            // El de cola gira mas rapido y es pequeno: casi no se ve.
            d["DiscoCola"] = Disco("DiscoRotorCola", 0.4f);
            return d;
        }

        /// <summary>
        /// Estela de las palas a vueltas: un disco casi transparente, mas denso hacia la punta (donde
        /// la pala barre mas rapido) con cuatro bandas suaves; sin nada en el buje.
        /// </summary>
        static Texture2D TexturaDisco()
        {
            const int N = 256;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true);
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float th = Mathf.Atan2(v, u);
                    float radial = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.08f, 0.2f, r)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.9f, 1f, r)));
                    float bandas = 0.75f + 0.25f * Mathf.Pow(0.5f + 0.5f * Mathf.Cos(4f * th), 3f);
                    float punta = 1f + 0.6f * Mathf.Exp(-Mathf.Pow((r - 0.93f) / 0.04f, 2f)); // la punta de las palas se ve mas
                    float a = 0.17f * radial * bandas * (0.7f + 0.3f * r) * punta;
                    px[y * N + x] = new Color32(255, 255, 255, (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            Directory.CreateDirectory(Carpeta);
            File.WriteAllBytes(RutaDisco, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(RutaDisco, ImportAssetOptions.ForceSynchronousImport);
            var ti = (TextureImporter)AssetImporter.GetAtPath(RutaDisco);
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(RutaDisco);
        }

        // ------------------------------------------------------------------ Rotores
        static Dictionary<string, Mesh> MallasRotores()
        {
            var mallas = new Dictionary<string, Mesh>();
            // Palas: R 7,3 m, cuerda 0,53 m (BEMT), torsion de -9 grados, 8 grados a 0,75 R y 3 de conicidad.
            var pala = new Geo();
            pala.Pala(0.55f, RadioRotor, 0.53f, 0.42f, 8f, -9f, 3f, Vector3.forward, Vector3.right, Vector3.up);
            mallas["Pala"] = pala.Malla("Pala");

            var buje = new Geo();
            buje.Cilindro(new Vector3(0f, -0.05f, 0f), new Vector3(0f, 0.38f, 0f), 0.32f, 0.3f, 16, true);
            buje.Cilindro(new Vector3(0f, 0.38f, 0f), new Vector3(0f, 0.55f, 0f), 0.2f, 0.12f, 12, true);
            for (int i = 0; i < 4; i++)
            {
                var g = Quaternion.Euler(0f, i * 90f, 0f);
                buje.Caja(g * new Vector3(0f, 0.18f, 0.55f), new Vector3(0.13f, 0.1f, 0.32f), g);          // horquillas
                buje.Cilindro(g * new Vector3(0.12f, 0.3f, 0.25f), g * new Vector3(0.12f, 0.02f, 0.6f), 0.035f, 0.035f, 6, true); // bieletas
            }
            mallas["Buje"] = buje.Malla("Buje");

            // Cola: cuatro palas de 1,4 m en tijera (0, 55, 180, 235 grados) que giran sobre el eje X.
            var cola = new Geo();
            foreach (float a in new[] { 0f, 55f, 180f, 235f })
            {
                var g = Quaternion.Euler(a, 0f, 0f);
                cola.Pala(0.18f, RadioCola, 0.25f, 0.22f, 10f, -6f, 0f, g * Vector3.up, g * Vector3.back, g * Vector3.left);
            }
            cola.Cilindro(new Vector3(-0.08f, 0f, 0f), new Vector3(0.2f, 0f, 0f), 0.13f, 0.1f, 12, true);
            mallas["PalasCola"] = cola.Malla("PalasCola");

            var disco = new Geo();
            disco.Disco(0.5f, RadioRotor + 0.05f, 64, Vector3.up, Vector3.right, Vector3.forward);
            mallas["Disco"] = disco.Malla("Disco");
            var discoCola = new Geo();
            discoCola.Disco(0.12f, RadioCola + 0.03f, 32, Vector3.left, Vector3.up, Vector3.forward);
            mallas["DiscoCola"] = discoCola.Malla("DiscoCola");

            // Un .asset con todas las mallas (la primera es el asset y las demas, sub-assets).
            AssetDatabase.DeleteAsset(RutaMallas);
            Mesh principal = null;
            foreach (var m in mallas.Values)
            {
                if (principal == null) AssetDatabase.CreateAsset(principal = m, RutaMallas);
                else AssetDatabase.AddObjectToAsset(m, principal);
            }
            return mallas;
        }

        // ------------------------------------------------------------------ Cohete
        static GameObject Cohete(Material pintura)
        {
            var g = new Geo();
            g.Cilindro(new Vector3(0f, 0f, -0.8f), new Vector3(0f, 0f, 0.5f), 0.085f, 0.085f, 12, true);
            g.Cilindro(new Vector3(0f, 0f, 0.5f), new Vector3(0f, 0f, 0.85f), 0.085f, 0.01f, 12, true); // ojiva
            for (int i = 0; i < 4; i++)
            {
                var q = Quaternion.Euler(0f, 0f, i * 90f + 45f);
                g.Caja(q * new Vector3(0f, 0.14f, -0.68f), new Vector3(0.008f, 0.07f, 0.12f), q);         // aletas
            }
            var malla = g.Malla("Cohete");
            var ruta = Carpeta + "/Cohete.asset";
            AssetDatabase.DeleteAsset(ruta);
            AssetDatabase.CreateAsset(malla, ruta);

            var go = new GameObject("Cohete");
            go.AddComponent<MeshFilter>().sharedMesh = malla;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = pintura;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.AddComponent<Guardianes.Enemigos.Cohete>();
            Directory.CreateDirectory(ConstructorHumanos.CarpetaPrefabs);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, RutaCohete);
            UnityEngine.Object.DestroyImmediate(go);
            return prefab;
        }

        // ------------------------------------------------------------------ Geometria
        /// <summary>Acumula vertices y triangulos de piezas sencillas y da una malla con las caras hacia fuera.</summary>
        class Geo
        {
            readonly List<Vector3> _v = new List<Vector3>();
            readonly List<Vector2> _uv = new List<Vector2>();
            readonly List<int> _t = new List<int>();

            /// <summary>
            /// Pala con perfil NACA 0012 de la raiz a la punta: cuerda constante hasta 0,9 R y luego
            /// afilada con la punta en flecha; calado lineal (torsion) con 'calado75' a 0,75 R; conicidad
            /// hacia arriba. El borde de ataque va hacia -cuerda y el giro de paso sube el borde de ataque.
            /// </summary>
            public void Pala(float r0, float r1, float cuerda, float cuerdaPunta, float calado75, float torsion, float conicidad,
                             Vector3 envergadura, Vector3 ejeCuerda, Vector3 ejeEspesor)
            {
                const int Tramos = 12, Perfil = 14;
                int inicio = _v.Count;
                for (int i = 0; i <= Tramos; i++)
                {
                    float s = (float)i / Tramos;
                    float r = Mathf.Lerp(r0, r1, s);
                    float rr = r / r1;
                    float c = rr < 0.9f ? cuerda : Mathf.Lerp(cuerda, cuerdaPunta, (rr - 0.9f) / 0.1f);
                    float flecha = rr < 0.93f ? 0f : (rr - 0.93f) / 0.07f * 0.12f;      // la punta se va hacia atras
                    float calado = (calado75 + torsion * (rr - 0.75f)) * Mathf.Deg2Rad;
                    float subida = r * Mathf.Sin(conicidad * Mathf.Deg2Rad);
                    for (int j = 0; j < Perfil; j++)
                    {
                        // Recorre el extrados del borde de salida al de ataque y vuelve por el intrados.
                        float a = (float)j / Perfil * Mathf.PI * 2f;
                        float x = 0.5f + 0.5f * Mathf.Cos(a);                            // 1 = salida, 0 = ataque
                        float e = Espesor(x) * c * (j <= Perfil / 2 ? 1f : -1f);
                        float xc = (x - 0.25f) * c + flecha;                             // eje de paso a 1/4 de cuerda
                        // Paso: gira el perfil para subir el borde de ataque (que esta hacia -cuerda).
                        float xr = xc * Mathf.Cos(calado) + e * Mathf.Sin(calado);
                        float yr = -xc * Mathf.Sin(calado) + e * Mathf.Cos(calado);
                        _v.Add(envergadura * r + ejeCuerda * xr + ejeEspesor * (yr + subida));
                        _uv.Add(new Vector2((float)j / Perfil, s));
                    }
                }
                // El perfil gira en sentido antihorario visto desde la punta: (anillo, siguiente, tramo
                // de fuera) deja la cara hacia fuera.
                for (int i = 0; i < Tramos; i++)
                    for (int j = 0; j < Perfil; j++)
                    {
                        int a = inicio + i * Perfil + j, b = inicio + i * Perfil + (j + 1) % Perfil;
                        int c2 = a + Perfil, d = b + Perfil;
                        _t.AddRange(new[] { a, b, c2, b, d, c2 });
                    }
                // Tapas de raiz y punta (abanicos).
                for (int extremo = 0; extremo < 2; extremo++)
                {
                    int baseAnillo = inicio + extremo * Tramos * Perfil;
                    int centro = _v.Count;
                    Vector3 m = Vector3.zero;
                    for (int j = 0; j < Perfil; j++) m += _v[baseAnillo + j];
                    _v.Add(m / Perfil);
                    _uv.Add(new Vector2(0.5f, extremo));
                    for (int j = 0; j < Perfil; j++)
                    {
                        int a = baseAnillo + j, b = baseAnillo + (j + 1) % Perfil;
                        if (extremo == 0) _t.AddRange(new[] { centro, b, a });
                        else _t.AddRange(new[] { centro, a, b });
                    }
                }
            }

            static float Espesor(float x)
            {
                x = Mathf.Clamp01(x);
                return 0.6f * (0.2969f * Mathf.Sqrt(x) - 0.126f * x - 0.3516f * x * x + 0.2843f * x * x * x - 0.1036f * x * x * x * x);
            }

            /// <summary>Tronco de cono (cilindro si r0 = r1) de a a b, con tapas.</summary>
            public void Cilindro(Vector3 a, Vector3 b, float r0, float r1, int lados, bool tapas)
            {
                Vector3 eje = (b - a).normalized;
                Vector3 u = Vector3.Cross(eje, Mathf.Abs(eje.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                Vector3 w = Vector3.Cross(eje, u);
                int inicio = _v.Count;
                for (int k = 0; k < 2; k++)
                    for (int i = 0; i < lados; i++)
                    {
                        float ang = (float)i / lados * Mathf.PI * 2f;
                        Vector3 radial = u * Mathf.Cos(ang) + w * Mathf.Sin(ang);
                        _v.Add((k == 0 ? a : b) + radial * (k == 0 ? r0 : r1));
                        _uv.Add(new Vector2((float)i / lados, k));
                    }
                for (int i = 0; i < lados; i++)
                {
                    int p = inicio + i, q = inicio + (i + 1) % lados;
                    _t.AddRange(new[] { p, q, p + lados, q, q + lados, p + lados });
                }
                if (!tapas) return;
                for (int k = 0; k < 2; k++)
                {
                    int centro = _v.Count;
                    _v.Add(k == 0 ? a : b);
                    _uv.Add(new Vector2(0.5f, k));
                    for (int i = 0; i < lados; i++)
                    {
                        int p = inicio + k * lados + i, q = inicio + k * lados + (i + 1) % lados;
                        if (k == 0) _t.AddRange(new[] { centro, q, p });
                        else _t.AddRange(new[] { centro, p, q });
                    }
                }
            }

            /// <summary>Caja con caras propias (aristas vivas).</summary>
            public void Caja(Vector3 centro, Vector3 medias, Quaternion giro)
            {
                Vector3[] ejes = { Vector3.right, Vector3.up, Vector3.forward };
                for (int eje = 0; eje < 3; eje++)
                    for (int signo = -1; signo <= 1; signo += 2)
                    {
                        Vector3 n = ejes[eje] * signo;
                        Vector3 a = ejes[(eje + 1) % 3], b = ejes[(eje + 2) % 3];
                        int i0 = _v.Count;
                        for (int k = 0; k < 4; k++)
                        {
                            float sa = (k == 1 || k == 2) ? 1f : -1f, sb = k >= 2 ? 1f : -1f;
                            Vector3 p = Vector3.Scale(n + a * sa + b * sb, medias);
                            _v.Add(centro + giro * p);
                            _uv.Add(new Vector2(sa * 0.5f + 0.5f, sb * 0.5f + 0.5f));
                        }
                        if (signo > 0) _t.AddRange(new[] { i0, i0 + 1, i0 + 2, i0, i0 + 2, i0 + 3 });
                        else _t.AddRange(new[] { i0, i0 + 2, i0 + 1, i0, i0 + 3, i0 + 2 });
                    }
            }

            /// <summary>Corona plana (de r0 a r1) en el plano (u, w) con UV planas; se ve por las dos caras.</summary>
            public void Disco(float r0, float r1, int lados, Vector3 normal, Vector3 u, Vector3 w)
            {
                int inicio = _v.Count;
                for (int k = 0; k < 2; k++)
                    for (int i = 0; i < lados; i++)
                    {
                        float ang = (float)i / lados * Mathf.PI * 2f;
                        float r = k == 0 ? r0 : r1;
                        float cu = Mathf.Cos(ang) * r, sw = Mathf.Sin(ang) * r;
                        _v.Add(u * cu + w * sw);
                        _uv.Add(new Vector2(cu / (2f * r1) + 0.5f, sw / (2f * r1) + 0.5f));
                    }
                for (int i = 0; i < lados; i++)
                {
                    int p = inicio + i, q = inicio + (i + 1) % lados;
                    _t.AddRange(new[] { p, p + lados, q, q, p + lados, q + lados });
                }
                _orientarHacia = normal;
            }

            Vector3? _orientarHacia;

            public Mesh Malla(string nombre)
            {
                var m = new Mesh { name = nombre };
                m.SetVertices(_v);
                m.SetUVs(0, _uv);
                var t = _t.ToArray();
                // Caras hacia fuera: en Unity la cara visible tiene normal cross(b - a, c - a). Una malla
                // cerrada con las normales hacia fuera tiene volumen positivo; un disco mira a 'normal'.
                double vol = 0;
                Vector3 suma = Vector3.zero;
                for (int i = 0; i < t.Length; i += 3)
                {
                    Vector3 a = _v[t[i]], b = _v[t[i + 1]], c = _v[t[i + 2]];
                    vol += Vector3.Dot(a, Vector3.Cross(b, c));
                    suma += Vector3.Cross(b - a, c - a);
                }
                bool girar = _orientarHacia.HasValue ? Vector3.Dot(suma, _orientarHacia.Value) < 0f : vol < 0;
                if (girar)
                    for (int i = 0; i < t.Length; i += 3) (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]);
                m.SetTriangles(t, 0);
                m.RecalculateNormals();
                m.RecalculateTangents();
                m.RecalculateBounds();
                return m;
            }
        }
    }

    /// <summary>Importacion de los OBJ de vehiculos: normales calculadas y suaves, sin materiales.</summary>
    public class ImportadorVehiculos : AssetPostprocessor
    {
        public override uint GetVersion() => 1;

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith("Assets/_Game/Art/Vehiculos")) return;
            var mi = (ModelImporter)assetImporter;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.importNormals = ModelImporterNormals.Calculate;
            mi.normalSmoothingAngle = 75f;
            mi.importTangents = ModelImporterTangents.None;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.isReadable = false;
            mi.meshCompression = ModelImporterMeshCompression.Off;
        }
    }
}
