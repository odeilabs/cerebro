using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
    /// Genera los humanos antes con creador.py de AssetsCreator. Las variantes de un humano con otra
    /// arma (pistola, escopeta...: los clips de 'animar --arma' de su familia) van en la carpeta del de
    /// serie y con sus materiales: las texturas no se copian dos veces. Las armas del kit de armas de
    /// AssetsCreator (gd_glock17, gd_ak47...: generador/armas, creador.py arma) llegan con su ficha (el _agarre.json: agarre,
    /// boca, guardamanos, clase y materiales) y sus materiales (TraerArma) y van en la mano como dice su
    /// ficha; las demas (kk_ak47), con la regla del fusil.
    /// </summary>
    public static class ConstructorHumanos
    {
        public const string CarpetaArte = "Assets/_Game/Art/Humanos";
        public const string CarpetaPrefabs = "Assets/_Game/Prefabs/Enemigos";
        /// <summary>El arte del juego, donde se busca una textura igual antes de copiar otra.</summary>
        const string CarpetaArteJuego = "Assets/_Game/Art";
        const string SubcarpetaProyectos = @"AssetsCreator\AssetsCreatorWin\proyectos";

        public struct Def
        {
            public string nombre;     // proyecto de AssetsCreator (kk_mercenario...)
            public string prefab;     // nombre del prefab
            public float vida;
            /// <summary>Dano por bala. Con 0 o menos, un armado lleva la ficha entera de su arma (Arma.Ficha: dano, cadencia, dispersion, rafagas y alcance).</summary>
            public float danioBala;
            /// <summary>Sufijo del FBX animado (por defecto el de los clips de fusil).</summary>
            public string animado;
            /// <summary>Desarmado: no lleva arma ni dispara.</summary>
            public bool civil;
            /// <summary>
            /// Prefijo de los clips de quieto, andar, apuntar y disparar del FBX animado (fusil_ak, pistola, escopeta,
            /// subfusil: los de 'animar --arma' de AssetsCreator); vacio, fusil_ak. Lo que no tenga, del fusil.
            /// </summary>
            public string familia;
            /// <summary>
            /// Carpeta del arte bajo CarpetaArte; vacio, la del prefab. Una variante de un humano (otra arma, otros clips)
            /// va en la del de serie: sus texturas son las mismas y no se copian dos veces.
            /// </summary>
            public string carpeta;
            /// <summary>
            /// Sufijo del FBX animado de la misma carpeta cuyos materiales lleva (el del de serie, _animado_fusil_ak): la
            /// variante usa esos, no unos suyos iguales. Vacio, los de su FBX.
            /// </summary>
            public string materialesDe;
        }

        /// <summary>
        /// Como va en la mano un modelo de arma, en el espacio del modelo importado (Unity cambia el signo de la x del OBJ: el
        /// canon de AssetsCreator, +X en el OBJ, queda en -X): el punto que va en el hueco del puno (AC_arma_r) y, con 'ejes',
        /// el canon, arriba y la boca. De la ficha de un arma (LeerFicha), ademas: donde apoya la mano izquierda (guardamanos;
        /// null si es de una mano) y, si va montada (la M2 en su cuna), el pivote sobre el que gira, su eje y el radio de la
        /// columna. Sale de Agarres, de la ficha del arma o, si no tiene ninguna de las dos (kk_ak47, bk_arcabuz), de la regla
        /// del fusil, como siempre.
        /// </summary>
        public struct Agarre
        {
            public Vector3 empunadura;
            public bool ejes;
            public Vector3 canon, arriba, boca;
            public Vector3? guardamanos;
            public bool montada;
            public Vector3 pivote, ejeGiro;
            public float radioColumna;
        }

        /// <summary>
        /// El agarre a mano de las armas en las que no valen ni la regla del 28 % de EjesFusil ni una ficha: manda sobre las
        /// dos. Hoy no hay ninguna (las del kit de armas traen su ficha). Cada juego puede anadir los suyos (con Fusil).
        /// </summary>
        public static readonly Dictionary<string, Agarre> Agarres = new Dictionary<string, Agarre>();

        /// <summary>Un agarre con los ejes de las armas de AssetsCreator (el canon a -X y arriba +Y, ya importado).</summary>
        public static Agarre Fusil(Vector3 empunadura, Vector3 boca) =>
            new Agarre { empunadura = empunadura, ejes = true, canon = Vector3.left, arriba = Vector3.up, boca = boca };

        /// <summary>
        /// Lo que se mueve el punto de la mano (el hueso AC_arma_r) respecto al agarre de la ficha de un arma (el centro de la
        /// empunadura, o del cuello de la culata, donde cierra el puno), por clase, en el espacio del modelo importado (+X hacia
        /// la culata). De serie, ninguno: en los clips del mercenario (fusil_ak, escopeta, subfusil y pistola) el puno cierra
        /// en el hueso (el centro de los dedos queda a 1 cm), asi que la empunadura de la ficha va en el. Con el desfase del AK
        /// de la regla (6 cm hacia la culata y 4 cm por debajo: asi cuelga el kk_ak47, con el puno debajo y detras del
        /// pistolete) el puno quedaba debajo de la empunadura, sin cogerla (las fotos de octubre de 2026). Cada juego puede
        /// poner los suyos (para unos clips que cierren el puno en otro sitio).
        /// </summary>
        public static readonly Dictionary<ClaseArma, Vector3> DesfasesAgarre = new Dictionary<ClaseArma, Vector3>();

        /// <summary>El desfase del agarre de una clase (DesfasesAgarre); lo que no esta, cero.</summary>
        public static Vector3 DesfaseAgarre(ClaseArma clase) => DesfasesAgarre.TryGetValue(clase, out var d) ? d : Vector3.zero;

        // ------------------------------------------------------------------ La ficha de un arma (_agarre.json)
        /// <summary>
        /// La ficha de un arma del kit de armas de AssetsCreator (generador/armas, creador.py arma), su &lt;n&gt;_agarre.json leido con
        /// LeerFicha: su clase (la de 'clase' o, si no la dice, la de su nombre), el agarre ya en el espacio del modelo
        /// importado y con el desfase de su clase, la receta del texturizador de cada material ('metal', 'pintura_coche',
        /// 'madera'...) y la rugosidad que pide cada uno, si la lleva (la M2).
        /// </summary>
        public class FichaArma
        {
            public string nombre;
            public ClaseArma clase;
            public Agarre agarre;
            public Dictionary<string, string> recetas = new Dictionary<string, string>();
            public Dictionary<string, float> rugosidad = new Dictionary<string, float>();
        }

        [Serializable] class FichaJson { public string nombre, clase; public float[] agarre, boca, guardamanos; public EjesJson ejes; public MontajeJson montaje; }
        [Serializable] class EjesJson { public string canon, arriba; }
        [Serializable] class MontajeJson { public float[] pivote; public string eje_giro; public float radio_columna; }

        /// <summary>Donde queda la ficha de un arma traida: Art/Humanos/Armas/&lt;p&gt;/&lt;p&gt;_agarre.json.</summary>
        public static string RutaFicha(string proyecto) => $"{CarpetaArte}/Armas/{proyecto}/{proyecto}_agarre.json";

        /// <summary>
        /// Lee la ficha de un arma (su _agarre.json, en cualquier ruta); null si no esta o no dice el agarre y la boca. Del OBJ
        /// al modelo importado la x cambia de signo: la empunadura es (-agarre.x, agarre.y, agarre.z) mas el desfase de su
        /// clase (DesfaseAgarre), la boca (-boca.x, boca.y, boca.z), y lo mismo el guardamanos, el pivote del montaje y los
        /// ejes (el canon '+X' queda en Vector3.left; arriba, '+Y', en Vector3.up). El gd_mp5.py de AssetsCreator busca su
        /// firma en este fichero (Tools~/arte/armas/README.md): si se renombra o se mueve, hay que cambiarlo alli.
        /// </summary>
        public static FichaArma LeerFicha(string ruta)
        {
            if (string.IsNullOrEmpty(ruta) || !File.Exists(ruta)) return null;
            string texto = File.ReadAllText(ruta);
            FichaJson j;
            try { j = JsonUtility.FromJson<FichaJson>(texto); }
            catch (Exception e)
            {
                Debug.LogWarning($"[GD] La ficha {ruta} no se lee: {e.Message}");
                return null;
            }
            if (j == null || !EsPunto(j.agarre) || !EsPunto(j.boca))
            {
                Debug.LogWarning($"[GD] La ficha {ruta} no dice el agarre y la boca: el arma va con la regla");
                return null;
            }
            string nombre = Path.GetFileNameWithoutExtension(ruta);
            if (nombre.EndsWith("_agarre")) nombre = nombre.Substring(0, nombre.Length - "_agarre".Length);
            if (!string.IsNullOrEmpty(j.nombre)) nombre = j.nombre;
            var clase = !string.IsNullOrEmpty(j.clase) && Enum.TryParse(j.clase, true, out ClaseArma c) && Enum.IsDefined(typeof(ClaseArma), c)
                ? c : Arma.ClaseDe(nombre);
            var f = new FichaArma { nombre = nombre, clase = clase };
            f.agarre = new Agarre
            {
                empunadura = AlModelo(j.agarre) + DesfaseAgarre(clase),
                ejes = true,
                canon = EjeKit(j.ejes?.canon, Vector3.left),
                arriba = EjeKit(j.ejes?.arriba, Vector3.up),
                boca = AlModelo(j.boca),
                guardamanos = EsPunto(j.guardamanos) ? AlModelo(j.guardamanos) : (Vector3?)null,
            };
            // (JsonUtility crea el montaje aunque no este: lo que dice que esta es su pivote)
            if (j.montaje != null && EsPunto(j.montaje.pivote))
            {
                f.agarre.montada = true;
                f.agarre.pivote = AlModelo(j.montaje.pivote);
                f.agarre.ejeGiro = EjeKit(j.montaje.eje_giro, Vector3.up);
                f.agarre.radioColumna = j.montaje.radio_columna;
            }
            foreach (var kv in ObjetoPlano(texto, "materiales")) f.recetas[kv.Key] = kv.Value;
            foreach (var kv in ObjetoPlano(texto, "rugosidad"))
                if (float.TryParse(kv.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float r)) f.rugosidad[kv.Key] = r;
            return f;
        }

        static bool EsPunto(float[] p) => p != null && p.Length >= 3;

        /// <summary>Un punto del OBJ en el modelo importado: la x cambia de signo.</summary>
        static Vector3 AlModelo(float[] p) => new Vector3(-p[0], p[1], p[2]);

        /// <summary>Un eje de la ficha ('+X', '-Y'...) en el modelo importado (la x cambia de signo); si no se entiende, 'porDefecto'.</summary>
        static Vector3 EjeKit(string eje, Vector3 porDefecto)
        {
            if (string.IsNullOrEmpty(eje)) return porDefecto;
            string e = eje.Trim().ToUpperInvariant();
            float s = e.StartsWith("-") ? -1f : 1f;
            switch (e.TrimStart('+', '-'))
            {
                case "X": return Vector3.left * s;
                case "Y": return Vector3.up * s;
                case "Z": return Vector3.forward * s;
                default: return porDefecto;
            }
        }

        /// <summary>
        /// Los pares de un objeto plano del json ("materiales": {"acero_canon": "metal", ...}), los valores como texto (sin
        /// comillas): JsonUtility no lee diccionarios. Vacio si no esta.
        /// </summary>
        static Dictionary<string, string> ObjetoPlano(string json, string clave)
        {
            var pares = new Dictionary<string, string>();
            var m = Regex.Match(json, "\"" + Regex.Escape(clave) + "\"\\s*:\\s*\\{([^{}]*)\\}");
            if (!m.Success) return pares;
            foreach (Match p in Regex.Matches(m.Groups[1].Value, "\"([^\"]+)\"\\s*:\\s*(?:\"([^\"]*)\"|([-+0-9.eE]+))"))
                pares[p.Groups[1].Value] = p.Groups[2].Success ? p.Groups[2].Value : p.Groups[3].Value;
            return pares;
        }

        /// <summary>El mercenario de Ferrox de King Kong (el de toda la saga).</summary>
        public static readonly Def Mercenario = new Def { nombre = "kk_mercenario", prefab = "Mercenario", vida = 30f, danioBala = 3.5f };

        public static void ConstruirTodos(IEnumerable<Def> humanos, string arma = "kk_ak47")
        {
            if (!string.IsNullOrEmpty(arma)) CopiarArma(arma);
            foreach (var d in humanos) Construir(d, arma);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// La carpeta proyectos de AssetsCreator: la variable de entorno ASSETSCREATOR (la carpeta
        /// AssetsCreator) o, si no, subiendo desde el proyecto Unity hasta encontrar
        /// AssetsCreator\AssetsCreatorWin\proyectos (Odei Labs\AssetsCreator, junto a Gaia's Response).
        /// </summary>
        public static string ProyectosAssetsCreator()
        {
            var env = Environment.GetEnvironmentVariable("ASSETSCREATOR");
            if (!string.IsNullOrEmpty(env))
            {
                var p = Path.Combine(env, "AssetsCreatorWin", "proyectos");
                if (Directory.Exists(p)) return Path.GetFullPath(p);
            }
            var dir = new DirectoryInfo(Path.GetFullPath(Path.Combine(Application.dataPath, "..")));
            for (; dir != null; dir = dir.Parent)
            {
                var p = Path.Combine(dir.FullName, SubcarpetaProyectos);
                if (Directory.Exists(p)) return p;
            }
            throw new DirectoryNotFoundException("No encuentro " + SubcarpetaProyectos + " subiendo desde el proyecto (o define ASSETSCREATOR)");
        }

        static string Origen(string proyecto) => Path.Combine(ProyectosAssetsCreator(), proyecto, "salida");

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
                CopiarSiCambia(f, Path.Combine(carpetaDestino, n));
            }
            string fbm = Path.Combine(origen, proyecto + ".fbm");
            if (Directory.Exists(fbm))
                foreach (var f in Directory.GetFiles(fbm))
                    CopiarSiCambia(f, Path.Combine(carpetaDestino, Path.GetFileName(f)));
            return carpetaDestino;
        }

        /// <summary>Copia si el destino no existe o es distinto (lo igual no se toca: Unity no lo vuelve a importar).</summary>
        static void CopiarSiCambia(string origen, string destino)
        {
            if (File.Exists(destino) && Iguales(origen, destino)) return;
            File.Copy(origen, destino, true);
        }

        static bool Iguales(string a, string b)
        {
            var fa = new FileInfo(a);
            var fb = new FileInfo(b);
            if (fa.Length != fb.Length) return false;
            const int Trozo = 1 << 16;
            var ba = new byte[Trozo];
            var bb = new byte[Trozo];
            using (var sa = fa.OpenRead())
            using (var sb = fb.OpenRead())
            {
                int n;
                while ((n = sa.Read(ba, 0, Trozo)) > 0)
                {
                    int m = 0;
                    while (m < n)
                    {
                        int k = sb.Read(bb, m, n - m);
                        if (k <= 0) return false;
                        m += k;
                    }
                    for (int i = 0; i < n; i++) if (ba[i] != bb[i]) return false;
                }
            }
            return true;
        }

        /// <summary>
        /// El arma de ConstruirTodos: una del kit de armas (con su _agarre.json), con su ficha y sus materiales (TraerArma);
        /// las demas (kk_ak47), como siempre: su OBJ y todos sus mapas, sin .mtl (un solo material).
        /// </summary>
        static void CopiarArma(string proyecto)
        {
            if (File.Exists(Path.Combine(Origen(proyecto), proyecto + "_agarre.json")))
            {
                TraerArma(proyecto);
                return;
            }
            string dest = $"{CarpetaArte}/Armas/{proyecto}";
            Copiar(proyecto, dest, n => n.StartsWith(proyecto + "_pbr") && (n.EndsWith(".obj") || n.EndsWith(".png")));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        // ------------------------------------------------------------------ Las armas con sus materiales
        /// <summary>El acabado de un material de un arma traida: metalizado, suavidad y tinte del color base.</summary>
        public struct Acabado
        {
            public float metal, suavidad;
            public Color tinte;
        }

        /// <summary>
        /// Las recetas del texturizador que son metal. La pintura con laca ('pintura_coche', el pavonado brillante de las
        /// escopetas, el Remington y el Colt) lleva un mapa metallic de escamas muy finas (0,015 de media) y no lo es.
        /// </summary>
        static readonly HashSet<string> RecetasMetal = new HashSet<string> { "metal", "cascarilla", "laton", "oro", "cobre" };

        /// <summary>El metalizado de lo que es metal: el de los mapas de la receta metal del texturizador (0,84 de media).</summary>
        public const float MetalizadoMetal = 0.84f;

        /// <summary>
        /// El acabado de un material de un arma (el tinte, blanco). Con ficha (las del kit): metal si su receta lo es (si no la
        /// dice, si tiene mapa metallic en su _material.json: 'mapaMetal'), y la suavidad = 1 - su rugosidad: la de la ficha o,
        /// si no la lleva, la que pide su .mtl de AssetsCreator ('rugosidadMtl', su Pr: la del kit, que el texturizador no
        /// lleva a los mapas), o la de su nombre. Sin ficha (las de creador.py), por el nombre: el metal y el pavonado brillan;
        /// la madera, el plastico y el cuero no.
        /// </summary>
        public static Acabado AcabadoDe(FichaArma ficha, string material, bool mapaMetal, float? rugosidadMtl)
        {
            var a = new Acabado { tinte = Color.white };
            if (ficha != null)
            {
                bool metal = ficha.recetas.TryGetValue(material, out var receta)
                    ? RecetasMetal.Contains(receta.Split(' ')[0])
                    : mapaMetal;
                float rug = ficha.rugosidad.TryGetValue(material, out var r) ? r : rugosidadMtl ?? 1f - SuavidadPorNombre(material).suavidad;
                a.metal = metal ? MetalizadoMetal : 0f;
                a.suavidad = Mathf.Clamp01(1f - rug);
                return a;
            }
            (a.metal, a.suavidad) = SuavidadPorNombre(material);
            return a;
        }

        static (float metal, float suavidad) SuavidadPorNombre(string material)
        {
            if (material.Contains("pavonado")) return (0.85f, 0.55f);
            if (material.Contains("metal") || material.Contains("acero")) return (0.8f, 0.5f);
            if (material.Contains("madera") || material.Contains("nogal") || material.Contains("culata")) return (0f, 0.32f);
            if (material.Contains("cuero")) return (0f, 0.22f);
            return (0f, 0.4f);
        }

        /// <summary>
        /// Trae de AssetsCreator un arma a Art/Humanos/Armas/&lt;proyecto&gt;, donde la buscan PonerArma e InstanciarArma: su OBJ
        /// con PBR, su ficha si la tiene (el _agarre.json de las del kit de armas), su .mtl sin las texturas (solo los nombres
        /// y colores de los materiales: sin el, el OBJ se importa con un solo material, blanco) y, de sus mapas, solo el color
        /// base de cada material (la normal, la rugosidad, la oclusion y la altura no las usa el juego); un color base que ya
        /// este en el arte del juego igual byte a byte no se copia: el material usa el de alli (salvo si esta en la carpeta de
        /// otra arma: ver YaEnElArte). Cada material del OBJ lleva un
        /// HDRP/Lit (&lt;proyecto&gt;_&lt;material&gt;.mat junto al OBJ) con su color base y su acabado (AcabadoDe), y el OBJ lo usa
        /// en lugar del suyo (un remapeo en su importador). 'retocar' deja a cada juego cambiar el acabado de un material (un
        /// tinte). Lo que ya esta igual no se vuelve a copiar. Devuelve la ruta del OBJ, o null si el arma no esta generada.
        /// </summary>
        public static string TraerArma(string proyecto, Func<string, Acabado, Acabado> retocar = null)
        {
            string origen = Origen(proyecto);
            string obj = Path.Combine(origen, proyecto + "_pbr.obj");
            if (!File.Exists(obj)) { Debug.LogWarning($"[GD] Falta {obj} (genera el arma en AssetsCreator: creador.py arma <n> las del kit de armas, creador.py objeto arma las demas)"); return null; }
            string carpeta = $"{CarpetaArte}/Armas/{proyecto}";
            Directory.CreateDirectory(carpeta);
            string rutaObj = $"{carpeta}/{proyecto}_pbr.obj";
            CopiarSiCambia(obj, rutaObj);
            string json = Path.Combine(origen, proyecto + "_agarre.json");
            if (File.Exists(json)) CopiarSiCambia(json, RutaFicha(proyecto));
            // Los materiales del OBJ, en el orden de su .mtl (que se copia sin las texturas).
            var materiales = new List<string>();
            string mtl = Path.Combine(origen, proyecto + "_pbr.mtl");
            if (File.Exists(mtl))
            {
                var lineas = new List<string>();
                foreach (var l in File.ReadAllLines(mtl))
                {
                    string t = l.TrimStart();
                    if (t.StartsWith("map_") || t.StartsWith("norm") || t.StartsWith("bump") || t.StartsWith("disp")) continue;
                    if (t.StartsWith("newmtl ")) materiales.Add(t.Substring(7).Trim());
                    lineas.Add(l);
                }
                string rutaMtl = $"{carpeta}/{proyecto}_pbr.mtl";
                string texto = string.Join("\n", lineas) + "\n";
                if (!File.Exists(rutaMtl) || File.ReadAllText(rutaMtl) != texto) File.WriteAllText(rutaMtl, texto);
            }
            // El color base de cada material: el que ya este en el arte del juego o, si no, una copia aqui.
            var bases = new Dictionary<string, string>();
            string prefijo = proyecto + "_pbr_", sufijo = "_basecolor.png";
            var pngs = new List<string>(Directory.Exists(CarpetaArteJuego) ? Directory.GetFiles(CarpetaArteJuego, "*.png", SearchOption.AllDirectories) : new string[0]);
            foreach (var f in Directory.GetFiles(origen, prefijo + "*" + sufijo))
            {
                string n = Path.GetFileName(f);
                string material = n.Substring(prefijo.Length, n.Length - prefijo.Length - sufijo.Length);
                if (!materiales.Contains(material)) materiales.Add(material);
                string propia = $"{carpeta}/{n}";
                string ruta = YaEnElArte(f, propia, pngs);
                if (ruta == null)
                {
                    ruta = propia;
                    CopiarSiCambia(f, ruta);
                    // (otro material de esta misma arma con el mismo color base usara este)
                    if (!pngs.Contains(ruta)) pngs.Add(ruta);
                }
                bases[material] = ruta;
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            var importador = (ModelImporter)AssetImporter.GetAtPath(rutaObj);
            if (importador == null) { Debug.LogWarning($"[GD] No se importa {rutaObj}"); return null; }
            var ficha = LeerFicha(RutaFicha(proyecto));
            var rugosidadMtl = ficha != null ? RugosidadesMtl(Path.Combine(origen, proyecto + ".mtl")) : new Dictionary<string, float>();
            foreach (var material in materiales)
            {
                var tex = bases.TryGetValue(material, out var rutaTex) ? AssetDatabase.LoadAssetAtPath<Texture2D>(rutaTex) : null;
                var a = AcabadoDe(ficha, material, MapaMetal(origen, prefijo + material),
                                  rugosidadMtl.TryGetValue(material, out float pr) ? pr : (float?)null);
                if (retocar != null) a = retocar(material, a);
                var mat = MaterialesLit.Crear($"{carpeta}/{proyecto}_{material}.mat", "HDRP/Lit", m =>
                {
                    m.SetTexture("_BaseColorMap", tex);
                    m.SetColor("_BaseColor", a.tinte);
                    m.SetFloat("_Metallic", a.metal);
                    m.SetFloat("_Smoothness", a.suavidad);
                });
                importador.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), material), mat);
                string receta = ficha != null && ficha.recetas.TryGetValue(material, out var rc) ? rc : "-";
                Debug.Log($"[GD] Arma {proyecto}: material {material} (receta {receta}; color base {(rutaTex ?? "ninguno")}; " +
                          $"metal {a.metal:0.##}, suavidad {a.suavidad:0.##}, tinte {a.tinte})");
            }
            AssetDatabase.SaveAssets();
            importador.SaveAndReimport();
            // (el .mtl no es una dependencia del OBJ para Unity: se reimporta siempre)
            AssetDatabase.ImportAsset(rutaObj, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            // Lo que lleva de verdad el modelo importado (si el remapeo no encaja con sus nombres, saldria con los suyos).
            var modelo = AssetDatabase.LoadAssetAtPath<GameObject>(rutaObj);
            var mr = modelo != null ? modelo.GetComponentInChildren<MeshRenderer>() : null;
            var lleva = new List<string>();
            if (mr != null) foreach (var m in mr.sharedMaterials) lleva.Add(m == null ? "nada" : $"{m.name} ({(AssetDatabase.IsSubAsset(m) ? "suyo" : "remapeado")})");
            var mf = modelo != null ? modelo.GetComponentInChildren<MeshFilter>() : null;
            Debug.Log($"[GD] Arma {proyecto} traida ({(ficha != null ? $"ficha {ficha.clase}" : "sin ficha")}; malla " +
                      $"{(mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "-")}): {materiales.Count} materiales en {rutaObj}; " +
                      $"el modelo lleva {string.Join(", ", lleva)}");
            return rutaObj;
        }

        /// <summary>La rugosidad (Pr) de cada material de un .mtl de AssetsCreator; vacio si no esta.</summary>
        static Dictionary<string, float> RugosidadesMtl(string mtl)
        {
            var r = new Dictionary<string, float>();
            if (!File.Exists(mtl)) return r;
            string actual = null;
            foreach (var l in File.ReadAllLines(mtl))
            {
                string t = l.Trim();
                if (t.StartsWith("newmtl ")) actual = t.Substring(7).Trim();
                else if (actual != null && t.StartsWith("Pr ") &&
                         float.TryParse(t.Substring(3).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) r[actual] = v;
            }
            return r;
        }

        /// <summary>Si el material lleva mapa metallic (su &lt;n&gt;_pbr_&lt;material&gt;_material.json lo dice).</summary>
        static bool MapaMetal(string origen, string material)
        {
            string ruta = Path.Combine(origen, material + "_material.json");
            return File.Exists(ruta) && Regex.IsMatch(File.ReadAllText(ruta), "\"metallic\"\\s*:");
        }

        /// <summary>
        /// La ruta de un PNG del arte del juego ('pngs') igual byte a byte que 'f', sin contar 'propia' (la copia que haria
        /// esta arma): asi no entra dos veces la misma textura en el repo. Null si no hay. Los de la carpeta de otra arma
        /// (Art/Humanos/Armas/&lt;otra&gt;) no valen: el TraerArma de esa arma los pisa si cambia (y quitar su carpeta los
        /// borra), y esta cambiaria sin que nadie la tocara; esos se copian otra vez (git LFS guarda un solo objeto por
        /// contenido: el repo no crece).
        /// </summary>
        static string YaEnElArte(string f, string propia, List<string> pngs)
        {
            long largo = new FileInfo(f).Length;
            string armas = CarpetaArte + "/Armas/", suya = Path.GetDirectoryName(propia).Replace('\\', '/') + "/";
            foreach (var r in pngs)
            {
                string ruta = r.Replace('\\', '/');
                if (ruta == propia || !File.Exists(r) || new FileInfo(r).Length != largo) continue;
                if (ruta.StartsWith(armas) && !ruta.StartsWith(suya)) continue;
                if (Iguales(f, r)) return ruta;
            }
            return null;
        }

        // ------------------------------------------------------------------ Humano
        public static GameObject Construir(Def d, string arma = "kk_ak47")
        {
            string dest = $"{CarpetaArte}/{(string.IsNullOrEmpty(d.carpeta) ? d.prefab : d.carpeta)}";
            string sufijo = string.IsNullOrEmpty(d.animado) ? "_animado_fusil_ak" : d.animado;
            string fbx = d.nombre + sufijo + ".fbx";
            string json = d.nombre + sufijo + ".clips.json";
            Copiar(d.nombre, dest, n => n == fbx || n == json || n.EndsWith(".png"));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            string rutaFbx = $"{dest}/{fbx}";
            ConfigurarModelo(rutaFbx, $"{dest}/{json}");

            var clips = AssetDatabase.LoadAllAssetsAtPath(rutaFbx).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToDictionary(c => c.name);
            Debug.Log($"[GD] {d.prefab}: clips {string.Join(", ", clips.Keys)}");
            var controlador = CrearControlador($"{dest}/{d.prefab}.controller", clips, d.familia);

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
            if (!string.IsNullOrEmpty(d.materialesDe) && d.materialesDe != sufijo) MaterialesDe(inst, $"{dest}/{d.nombre}{d.materialesDe}.fbx", d.prefab);

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
            // La clase del arma que lleva en la mano (la de su Arma: la de la ficha del arma o la de su nombre; Humano.Awake la
            // vuelve a leer del componente); sin dano propio, un armado lleva la ficha entera de esa arma.
            if (punta != null && !d.civil)
            {
                var componente = punta.GetComponentInParent<Arma>();
                h.arma = componente != null ? componente.clase : Arma.ClaseDe(arma);
                if (d.danioBala <= 0f) h.Armar(h.arma);
            }

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

        /// <summary>
        /// Los materiales de otro FBX del mismo humano (el de serie) en lugar de los suyos, por el nombre: una variante
        /// con otros clips lleva los mismos materiales que el humano de serie, no unos suyos iguales.
        /// </summary>
        static void MaterialesDe(GameObject inst, string rutaFbx, string prefab)
        {
            var deSerie = new Dictionary<string, Material>();
            foreach (var m in AssetDatabase.LoadAllAssetsAtPath(rutaFbx).OfType<Material>())
                if (!deSerie.ContainsKey(m.name)) deSerie[m.name] = m;
            if (deSerie.Count == 0) { Debug.LogWarning($"[GD] {prefab}: {rutaFbx} no tiene materiales; lleva los suyos"); return; }
            int puestos = 0, suyos = 0;
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    if (deSerie.TryGetValue(mats[i].name, out var m)) { mats[i] = m; puestos++; }
                    else suyos++;
                }
                r.sharedMaterials = mats;
            }
            Debug.Log($"[GD] {prefab}: {puestos} materiales de {Path.GetFileName(rutaFbx)}{(suyos > 0 ? $" ({suyos} no estan alli: los suyos)" : "")}");
        }

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
        /// El modelo de un arma traida (Art/Humanos/Armas/&lt;p&gt;/&lt;p&gt;_pbr.obj) listo para montar, en el origen y sin girar
        /// (fuera de una mano: una torreta, un expositor): sin colisionadores, con su Arma (la clase de su ficha o, si no tiene,
        /// la de su nombre), su Bocacha, su Guardamanos si lo tiene y, si va montada (la M2), su MontajeArma. 'agarre' es como
        /// va en la mano: de Agarres, de su ficha (RutaFicha) o, si no tiene ninguna de las dos, de la regla del fusil; 'como'
        /// dice de donde. Null si no esta traida.
        /// </summary>
        public static GameObject InstanciarArma(string proyecto, out Agarre agarre, out string como)
        {
            agarre = default;
            como = "-";
            string ruta = $"{CarpetaArte}/Armas/{proyecto}/{proyecto}_pbr.obj";
            var modeloArma = AssetDatabase.LoadAssetAtPath<GameObject>(ruta);
            if (modeloArma == null) { Debug.LogWarning("[GD] Falta el arma: " + ruta); return null; }
            var arma = (GameObject)PrefabUtility.InstantiatePrefab(modeloArma);
            arma.name = "Arma";
            foreach (var c in arma.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(c);
            EjesFusil(arma, out var canonLocal, out var arribaLocal, out var empunadura, out var bocaLocal);
            agarre = new Agarre { empunadura = empunadura, ejes = true, canon = canonLocal, arriba = arribaLocal, boca = bocaLocal };
            var clase = Arma.ClaseDe(proyecto);
            como = "regla del fusil";
            if (Agarres.TryGetValue(proyecto, out var aMano))
            {
                como = "agarre de Agarres";
                var regla = agarre;
                agarre = aMano;
                if (!aMano.ejes) { agarre.ejes = true; agarre.canon = regla.canon; agarre.arriba = regla.arriba; agarre.boca = regla.boca; }
            }
            else
            {
                var ficha = LeerFicha(RutaFicha(proyecto));
                if (ficha != null)
                {
                    como = "su ficha";
                    agarre = ficha.agarre;
                    clase = ficha.clase;
                }
            }
            arma.AddComponent<Arma>().clase = clase;
            var boca = new GameObject("Bocacha").transform;
            boca.SetParent(arma.transform, false);
            boca.localPosition = agarre.boca;
            // Donde apoya la mano izquierda (el centro del guardamanos, la cacha izquierda de la M2), para quien lo quiera.
            if (agarre.guardamanos.HasValue)
            {
                var g = new GameObject("Guardamanos").transform;
                g.SetParent(arma.transform, false);
                g.localPosition = agarre.guardamanos.Value;
            }
            if (agarre.montada)
            {
                var m = arma.AddComponent<MontajeArma>();
                m.pivote = agarre.pivote;
                m.ejeGiro = agarre.ejeGiro;
                m.radioColumna = agarre.radioColumna;
            }
            return arma;
        }

        /// <summary>
        /// Cuelga un arma traida del hueso del puno (AC_arma_r) con la empunadura en el hueco de la mano: la
        /// empunadura va por la linea de nudillos (del menique al indice, "arriba" del fusil) y el canon hacia
        /// donde apuntan los nudillos desde la muneca. Los ejes y la empunadura salen de Agarres, de la ficha
        /// del arma o de la regla del fusil (InstanciarArma). Lleva un Arma con su clase (el calibre). Devuelve
        /// la bocacha (o el hueso, si el arma no esta traida).
        /// </summary>
        public static Transform PonerArma(Transform raiz, Transform hueso, string proyecto)
        {
            var arma = InstanciarArma(proyecto, out var agarre, out string como);
            if (arma == null) return hueso;
            Vector3 canonLocal = agarre.canon, arribaLocal = agarre.arriba, empunadura = agarre.empunadura;

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

            Debug.Log($"[GD] Arma {proyecto} ({arma.GetComponent<Arma>().clase}, {como}): canon {canonLocal}, arriba {arribaLocal}, empunadura {empunadura}, " +
                      $"boca {agarre.boca}{(agarre.guardamanos.HasValue ? $", guardamanos {agarre.guardamanos.Value}" : "")}" +
                      $"{(agarre.montada ? $", montada en {agarre.pivote} (columna de {agarre.radioColumna:0.###} m)" : "")}");
            return arma.transform.Find("Bocacha");
        }

        // ------------------------------------------------------------------ Animator
        /// <summary>
        /// El AnimatorController de un humano: quieto, andar y correr por la velocidad, apuntar, rafaga, huir, golpe y
        /// muerte. Los clips de quieto, andar, apuntar y disparar son los de su familia (fusil_ak de serie) o, si le faltan,
        /// los del fusil.
        /// </summary>
        static AnimatorController CrearControlador(string ruta, Dictionary<string, AnimationClip> c, string familia = null)
        {
            string f = string.IsNullOrEmpty(familia) ? "fusil_ak" : familia;
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
            arbol.AddChild(Clip(f + "_idle", "fusil_ak_idle", "idle"), 0f);
            arbol.AddChild(Clip(f + "_andar", "fusil_ak_andar", "walk"), 1.6f);
            arbol.AddChild(Clip("run"), 5.5f);
            var loco = sm.AddState("Locomocion");
            loco.motion = arbol;
            sm.defaultState = loco;

            var apuntar = sm.AddState("Apuntar"); apuntar.motion = Clip(f + "_apuntar_idle", "fusil_ak_apuntar_idle");
            var disparar = sm.AddState("Disparar"); disparar.motion = Clip(f + "_rafaga", f + "_disparar", "fusil_ak_rafaga", "fusil_ak_disparar");
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

    /// <summary>
    /// Las armas de AssetsCreator (los OBJ de Art/Humanos/Armas): su malla se llama como el modelo (kk_ak47_pbr,
    /// gd_glock17_pbr...). Un OBJ de un solo objeto la deja en 'default', y Arma.DeHumano reconoce el calibre del arma
    /// que lleva un humano en la mano por el nombre de su malla (tambien en los prefabs de antes, sin el componente Arma).
    /// </summary>
    public class ImportadorArmas : AssetPostprocessor
    {
        public override uint GetVersion() => 1;

        void OnPostprocessModel(GameObject modelo)
        {
            if (!assetPath.StartsWith(ConstructorHumanos.CarpetaArte + "/Armas/")) return;
            string nombre = Path.GetFileNameWithoutExtension(assetPath);
            var mallas = modelo.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < mallas.Length; i++)
            {
                var malla = mallas[i].sharedMesh;
                if (malla == null || Arma.Reconocer(malla.name, out _)) continue;
                malla.name = mallas.Length == 1 ? nombre : $"{nombre}_{i}";
            }
        }
    }
}
