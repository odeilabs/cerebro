using System;
using System.Collections.Generic;
using System.IO;
using Guardianes.Combate;
using Guardianes.EditorTools.Enemigos;
using Guardianes.Enemigos;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Guardianes.EditorTools
{
    /// <summary>
    /// Comprueba las balas sin escena de juego (lo monta todo lejos de lo que haya abierto, a 5 km de altura, y lo
    /// quita al acabar): sin blancos, Balas.Rayo da lo mismo que Physics.Raycast y el mismo dano; se salta lo del
    /// tirador; un blanco sin colisionadores mas cerca gana y uno detras de un colisionador no; el agua de un blanco
    /// para sin danar; sin blindaje no se tiran dados y nunca se toca UnityEngine.Random; un rebote hace el
    /// restoRebote y avisa con AlDanio; Impacto.Golpear dana al blanco una vez, con la caida y la fraccion; y la
    /// tabla de las armas (Arma.Ficha, ClaseDe con las del kit de armas, DeHumano, Humano.Armar, los agarres de
    /// ConstructorHumanos y la ficha de las armas del kit, su _agarre.json: LeerFicha y AcabadoDe).
    /// Desde batch: -executeMethod Guardianes.EditorTools.PruebaBalas.ComprobarDesdeBatch.
    /// </summary>
    public static class PruebaBalas
    {
        static int _fallos;
        static readonly Vector3 Base = new Vector3(0f, 5000f, 0f);

        /// <summary>Un blanco falso: una esfera sin colisionador (para: es agua, el tiro se queda sin danar).</summary>
        class BlancoEsfera : IBlancoBalas
        {
            public Salud salud;
            public Transform raiz;
            public Vector3 centro;
            public float radio = 1f, fraccion = 1f;
            public bool para;
            public int rayos, esferas;
            public float ultimoHasta;

            public Salud Salud => salud;
            public Transform Raiz => raiz;

            public bool Rayo(Vector3 origen, Vector3 dir, float hasta, out GolpeBlanco g)
            {
                rayos++;
                ultimoHasta = hasta;
                g = default;
                Vector3 oc = origen - centro;
                float b = Vector3.Dot(oc, dir), c = oc.sqrMagnitude - radio * radio;
                float disc = b * b - c;
                if (disc < 0f) return false;
                float t = -b - Mathf.Sqrt(disc);
                // Desde dentro no se ve (como la fisica).
                if (t < 0f || t > hasta) return false;
                g.distancia = t;
                g.punto = origen + dir * t;
                g.normal = (g.punto - centro).normalized;
                g.para = para;
                return true;
            }

            public bool Esfera(Vector3 c, float r, float danio, TipoDanio tipo, GameObject ignorar, out GolpeBlanco g)
            {
                esferas++;
                g = default;
                float d = Vector3.Distance(c, centro) - radio;
                if (d > r) return false;
                Vector3 hacia = c - centro;
                g.normal = hacia.sqrMagnitude > 1e-6f ? hacia.normalized : Vector3.up;
                g.punto = centro + g.normal * radio;
                g.distancia = Mathf.Max(0f, d);
                g.fraccion = fraccion;
                return true;
            }
        }

        public static void ComprobarDesdeBatch()
        {
            int fallos;
            try { fallos = Comprobar(); }
            catch (Exception e) { Debug.LogException(e); fallos = 1; }
            EditorApplication.Exit(fallos == 0 ? 0 : 1);
        }

        [MenuItem("Guardianes/Diagnostico/Comprobar balas")]
        public static int Comprobar()
        {
            _fallos = 0;
            var objetos = new List<GameObject>();
            var blancos = new List<BlancoEsfera>();
            var fichaPistola = Arma.Ficha(ClaseArma.Pistola);
            int impactosAlImpactar = 0;
            Tiro ultimoAvisado = default;
            Action<Tiro> contar = t => { impactosAlImpactar++; ultimoAvisado = t; };
            Balas.AlImpactar += contar;
            try
            {
                if (Balas.Registrados != 0)
                {
                    Esperar(false, $"hay {Balas.Registrados} blancos registrados antes de empezar: la prueba los necesita a 0");
                    return _fallos;
                }
                Balas.ReiniciarContadores();
                var azar = new System.Random(7);
                float U(float desde, float hasta) => desde + (float)azar.NextDouble() * (hasta - desde);
                Vector3 Direccion()
                {
                    Vector3 v;
                    do v = new Vector3(U(-1f, 1f), U(-1f, 1f), U(-1f, 1f)); while (v.sqrMagnitude < 0.01f || v.sqrMagnitude > 1f);
                    return v.normalized;
                }

                // ---------------------------------------------------------------- Penetracion
                var sp = Nuevo("Penetracion", Base, objetos).AddComponent<Salud>();
                Esperar(sp.Penetracion(Calibres.Pistola) == 1f && sp.Penetracion(0f) == 1f, "sin blindaje todo entra");
                sp.blindaje = 1f;
                Esperar(sp.Penetracion(0f) == 1f, "sin calibre entra siempre");
                Esperar(sp.Penetracion(0.6f) == 0f && sp.Penetracion(0.3f) == 0f, "por debajo de 0,6 del blindaje rebota siempre");
                Esperar(sp.Penetracion(1.4f) == 1f && sp.Penetracion(3f) == 1f, "por encima de 1,4 del blindaje entra siempre");
                Esperar(Mathf.Abs(sp.Penetracion(1f) - 0.5f) < 1e-4f, "con el calibre igual al blindaje, la mitad");
                // Los numeros del plan de Basiliskos (escama = (L / 18 m)^0,9): el fusil de asalto entra en el Rey de 120 m.
                float Escama(float largo) => Mathf.Pow(largo / 18f, 0.9f);
                sp.blindaje = Escama(120f);
                float pFusil = sp.Penetracion(Calibres.FusilAsalto);
                sp.blindaje = Escama(6.6f);
                float pPerdigon = sp.Penetracion(Calibres.Perdigon);
                sp.blindaje = Escama(15f);
                float pPistola15 = sp.Penetracion(Calibres.Pistola);
                sp.blindaje = Escama(30f);
                float pPistola30 = sp.Penetracion(Calibres.Pistola);
                Debug.Log($"[GD] BALAS penetracion: fusil de asalto en el Rey de 120 m {pFusil:0.000}, perdigon a 6,6 m {pPerdigon:0.000}, pistola a 15 m {pPistola15:0.000} y a 30 m {pPistola30:0.000}");
                Esperar(pFusil == 1f, "el fusil de asalto (8) entra siempre en el Rey de 120 m");
                Esperar(pPerdigon > 0.65f && pPerdigon < 0.75f, "los perdigones entran el 70 % en la constrictora de 6,6 m");
                Esperar(pPistola15 > 0.75f && pPistola15 < 0.87f, "la pistola entra el 81 % en la coronada de 15 m");
                Esperar(pPistola30 < 0.01f, "la pistola rebota en el basilisco de 30 m");

                // ---------------------------------------------------------------- Sin blancos: lo mismo que Physics.Raycast
                for (int i = 0; i < 120; i++)
                {
                    Vector3 p = Base + new Vector3(U(-20f, 20f), U(-20f, 20f), U(-20f, 20f));
                    var raiz = Nuevo("Nube" + i, p, objetos);
                    raiz.transform.rotation = Quaternion.Euler(U(0f, 360f), U(0f, 360f), U(0f, 360f));
                    // Un cuarto con la Salud en el padre y el colisionador en un hijo; otro cuarto, sin Salud.
                    var conCol = i % 4 == 0 ? Nuevo("Pieza", p, objetos, raiz.transform) : raiz;
                    if (i % 4 != 3)
                    {
                        var s = raiz.AddComponent<Salud>();
                        s.maxima = s.actual = 100000f;
                    }
                    switch (i % 3)
                    {
                        case 0: conCol.AddComponent<BoxCollider>().size = new Vector3(U(1f, 6f), U(1f, 6f), U(1f, 6f)); break;
                        case 1: conCol.AddComponent<SphereCollider>().radius = U(0.5f, 3f); break;
                        default:
                            var cc = conCol.AddComponent<CapsuleCollider>();
                            cc.radius = U(0.4f, 1.5f);
                            cc.height = U(2f, 7f);
                            break;
                    }
                }
                var ajeno = Nuevo("Ajeno", Base + new Vector3(0f, 300f, 0f), objetos);
                Physics.SyncTransforms();
                var estado = UnityEngine.Random.state;
                int distintos = 0, tocan = 0, conSalud = 0, danioDistinto = 0;
                for (int i = 0; i < 1000; i++)
                {
                    Vector3 o = Base + new Vector3(U(-25f, 25f), U(-25f, 25f), U(-25f, 25f));
                    Vector3 dir = Direccion();
                    float alcance = U(10f, 120f);
                    var tirador = i % 5 == 0 ? null : ajeno;
                    bool fis = Physics.Raycast(o, dir, out var hit, alcance, ~0, QueryTriggerInteraction.Ignore);
                    bool bal = Balas.Rayo(o, dir, alcance, tirador, out var t);
                    if (fis != bal) distintos++;
                    else if (fis)
                    {
                        tocan++;
                        if (t.collider != hit.collider || (t.punto - hit.point).sqrMagnitude > 1e-8f || t.salud != hit.collider.GetComponentInParent<Salud>()) distintos++;
                    }
                    else if ((t.punto - (o + dir * alcance)).sqrMagnitude > 1e-8f) distintos++;
                    if (t.salud == null) continue;
                    conSalud++;
                    // El mismo dano con calibre 0 o con el de un fusil (sin blindaje no hay rebote).
                    float antes = t.salud.actual;
                    Balas.Impactar(ref t, new DatosDanio { cantidad = 1f, direccion = dir, tipo = TipoDanio.Bala, calibre = i % 2 == 0 ? 0f : Calibres.FusilAsalto });
                    if (antes - t.salud.actual != 1f || t.rebote) danioDistinto++;
                }
                Debug.Log($"[GD] BALAS sin blancos: 1000 rayos, {tocan} tocan ({conSalud} una Salud): {distintos} distintos de Physics.Raycast, {danioDistinto} con otro dano, {Balas.Tiradas} dados");
                Esperar(tocan >= 150, $"hay con que comparar (tocan {tocan} de 1000)");
                Esperar(distintos == 0, $"sin blancos, Balas.Rayo es Physics.Raycast ({distintos} distintos)");
                Esperar(danioDistinto == 0 && conSalud > 50, $"sin blindaje, el dano de siempre ({danioDistinto} distintos de {conSalud})");
                Esperar(Balas.Tiradas == 0 && Balas.Rebotes == 0, "sin blindaje no se tiran dados ni rebota nada");
                Esperar(Balas.Impactos == conSalud, "cada tiro que da a una Salud cuenta como impacto");
                Esperar(estado.Equals(UnityEngine.Random.state), "Balas no toca UnityEngine.Random");

                // ---------------------------------------------------------------- Lo del tirador
                Vector3 p2 = Base + new Vector3(500f, 0f, 0f);
                var tir = Nuevo("Tirador", p2, objetos);
                var saludTirador = tir.AddComponent<Salud>();
                var escudo = Nuevo("Escudo", p2 + Vector3.forward * 2f, objetos, tir.transform);
                var colEscudo = escudo.AddComponent<BoxCollider>();
                colEscudo.size = new Vector3(1f, 1f, 0.2f);
                var pared = Nuevo("Pared", p2 + Vector3.forward * 10f, objetos);
                var colPared = pared.AddComponent<BoxCollider>();
                colPared.size = new Vector3(4f, 4f, 0.5f);
                var saludPared = pared.AddComponent<Salud>();
                Physics.SyncTransforms();
                Physics.Raycast(p2, Vector3.forward, out var hEscudo, 50f, ~0, QueryTriggerInteraction.Ignore);
                Balas.Rayo(p2, Vector3.forward, 50f, null, out var tSin);
                Balas.Rayo(p2, Vector3.forward, 50f, tir, out var tCon);
                Esperar(hEscudo.collider == colEscudo && tSin.collider == colEscudo && tSin.salud == saludTirador, "sin tirador, el rayo para en el escudo (como la fisica)");
                Esperar(tCon.collider == colPared && tCon.salud == saludPared, "con tirador, se salta su escudo y da en la pared");
                float vidaPared = saludPared.actual;
                Vector3 fin = Balas.Disparar(p2, Vector3.forward, 50f, tir, new DatosDanio { cantidad = 5f, direccion = Vector3.forward, tipo = TipoDanio.Bala, calibre = Calibres.Pistola });
                Esperar(Mathf.Abs(vidaPared - saludPared.actual - 5f) < 1e-4f && Mathf.Abs(fin.z - (p2.z + 9.75f)) < 1e-3f, "Disparar dana la pared y acaba en ella");
                float vidaTirador = saludTirador.actual;
                tSin.tirador = tir;
                Balas.Impactar(ref tSin, new DatosDanio { cantidad = 5f, tipo = TipoDanio.Bala });
                Esperar(saludTirador.actual == vidaTirador, "el tirador no se dana a si mismo");

                // ---------------------------------------------------------------- Blancos sin colisionadores
                Vector3 p3 = Base + new Vector3(1000f, 0f, 0f);
                var paredB = Nuevo("ParedBlancos", p3 + Vector3.forward * 20f, objetos);
                var colParedB = paredB.AddComponent<BoxCollider>();
                colParedB.size = new Vector3(6f, 6f, 0.5f);
                paredB.AddComponent<Salud>();
                var a = Blanco("A", p3 + Vector3.forward * 10f, 1f, objetos, blancos);
                Physics.SyncTransforms();
                Balas.Registrar(a);
                Balas.Registrar(a);
                Esperar(Balas.Registrados == 1, "un blanco se registra una vez");
                Balas.Rayo(p3, Vector3.forward, 50f, ajeno, out var tA);
                Esperar(tA.blanco == a && tA.collider == null && tA.salud == a.salud && Mathf.Abs(tA.distancia - 9f) < 1e-3f,
                        $"el blanco mas cerca que la pared gana (a {tA.distancia:0.000} m)");
                Esperar(Mathf.Abs(a.ultimoHasta - 19.75f) < 1e-3f, $"al blanco solo se le pregunta hasta la pared ({a.ultimoHasta:0.000} m)");
                a.centro = p3 + Vector3.forward * 30f;
                Balas.Rayo(p3, Vector3.forward, 50f, ajeno, out var tDetras);
                Esperar(tDetras.blanco == null && tDetras.collider == colParedB, "un blanco detras de la pared no recibe el tiro");
                a.centro = p3 + Vector3.forward * 10f;
                var b = Blanco("B", p3 + Vector3.forward * 6f, 1f, objetos, blancos);
                Balas.Registrar(b);
                Balas.Rayo(p3, Vector3.forward, 50f, ajeno, out var tB);
                Esperar(tB.blanco == b && Mathf.Abs(tB.distancia - 5f) < 1e-3f, "de dos blancos, gana la entrada mas cercana");
                Balas.Quitar(b);
                // Un blanco que cuelga del tirador no le da.
                var tirB = Nuevo("TiradorBlancos", p3 + Vector3.left * 3f, objetos);
                var c = Blanco("C", p3 + Vector3.forward * 4f, 1f, objetos, blancos, tirB.transform);
                Balas.Registrar(c);
                Balas.Rayo(p3, Vector3.forward, 50f, tirB, out var tC1);
                Balas.Rayo(p3, Vector3.forward, 50f, ajeno, out var tC2);
                Esperar(tC1.blanco == a && tC2.blanco == c, "el tirador se salta su propio blanco");
                Balas.Quitar(c);
                // El agua de un blanco: para el tiro sin danar.
                var dAgua = Blanco("Agua", p3 + Vector3.forward * 3f, 1f, objetos, blancos);
                dAgua.para = true;
                Balas.Registrar(dAgua);
                int impactosAntes = Balas.Impactos, avisosAntes = impactosAlImpactar;
                float vidaAgua = dAgua.salud.actual, vidaA = a.salud.actual;
                Vector3 finAgua = Balas.Disparar(p3, Vector3.forward, 50f, ajeno, new DatosDanio { cantidad = 5f, direccion = Vector3.forward, tipo = TipoDanio.Bala, calibre = Calibres.FusilAsalto });
                Esperar(Balas.Ultimo.para && Balas.Ultimo.salud == null && Balas.Impactos == impactosAntes && dAgua.salud.actual == vidaAgua && a.salud.actual == vidaA
                        && impactosAlImpactar == avisosAntes + 1 && Mathf.Abs(finAgua.z - (p3.z + 2f)) < 1e-3f, "el agua de un blanco para el tiro sin danar (y avisa)");
                Balas.Quitar(dAgua);
                // Disparar a un blanco: dana, avisa una vez y queda como el ultimo de este fotograma.
                avisosAntes = impactosAlImpactar;
                impactosAntes = Balas.Impactos;
                vidaA = a.salud.actual;
                Vector3 finA = Balas.Disparar(p3, Vector3.forward, 50f, ajeno, new DatosDanio { cantidad = 5f, direccion = Vector3.forward, tipo = TipoDanio.Bala, calibre = Calibres.FusilAsalto });
                Esperar(Mathf.Abs(vidaA - a.salud.actual - 5f) < 1e-4f && impactosAlImpactar == avisosAntes + 1 && Balas.Impactos == impactosAntes + 1
                        && ultimoAvisado.blanco == a && Balas.Ultimo.fotograma == Time.frameCount && (finA - tA.punto).sqrMagnitude < 1e-6f,
                        "Disparar a un blanco: dana, avisa una vez y acaba en su piel");
                avisosAntes = impactosAlImpactar;
                Vector3 finNada = Balas.Disparar(p3, Vector3.back, 50f, ajeno, new DatosDanio { cantidad = 5f, tipo = TipoDanio.Bala });
                Esperar(impactosAlImpactar == avisosAntes && (finNada - (p3 + Vector3.back * 50f)).sqrMagnitude < 1e-6f, "un tiro que no toca nada no avisa y acaba en su alcance");

                // ---------------------------------------------------------------- Rebotes
                a.salud.maxima = a.salud.actual = 1000f;
                a.salud.blindaje = 10f;
                int avisosDanio = 0;
                DatosDanio ultimoDanio = default;
                Action<DatosDanio> alDanio = d => { avisosDanio++; ultimoDanio = d; };
                a.salud.AlDanio += alDanio;
                estado = UnityEngine.Random.state;
                int tiradas = Balas.Tiradas, rebotes = Balas.Rebotes;
                float vida = a.salud.actual;
                Balas.Disparar(p3, Vector3.forward, 50f, ajeno, new DatosDanio { cantidad = 10f, direccion = Vector3.forward, tipo = TipoDanio.Bala, calibre = 1f });
                Esperar(Balas.Ultimo.rebote && Balas.Rebotes == rebotes + 1 && Balas.Tiradas == tiradas, "una pistola contra un blindaje de 10 rebota siempre, sin dados");
                Esperar(avisosDanio == 1 && ultimoDanio.rebote && Mathf.Abs(vida - a.salud.actual - 10f * a.salud.restoRebote) < 1e-3f,
                        $"un rebote hace el restoRebote ({vida - a.salud.actual:0.000}) y avisa con AlDanio");
                vida = a.salud.actual;
                Balas.Disparar(p3, Vector3.forward, 50f, ajeno, new DatosDanio { cantidad = 10f, direccion = Vector3.forward, tipo = TipoDanio.Bala, calibre = 100f });
                Esperar(!Balas.Ultimo.rebote && Balas.Tiradas == tiradas && Mathf.Abs(vida - a.salud.actual - 10f) < 1e-3f, "un calibre grande entra entero, sin dados");
                vida = a.salud.actual;
                Balas.Disparar(p3, Vector3.forward, 50f, ajeno, new DatosDanio { cantidad = 10f, direccion = Vector3.forward, tipo = TipoDanio.Bala, calibre = 0f });
                Esperar(!Balas.Ultimo.rebote && Balas.Tiradas == tiradas && Mathf.Abs(vida - a.salud.actual - 10f) < 1e-3f, "sin calibre entra entero");
                // Calibre igual al blindaje: la mitad rebota, con dados deterministas.
                var secuencia = new bool[64];
                Balas.ReiniciarContadores();
                for (int i = 0; i < 2000; i++)
                {
                    Balas.Disparar(p3, Vector3.forward, 50f, ajeno, new DatosDanio { cantidad = 0.001f, direccion = Vector3.forward, tipo = TipoDanio.Bala, calibre = 10f });
                    if (i < secuencia.Length) secuencia[i] = Balas.Ultimo.rebote;
                }
                int rebotesMitad = Balas.Rebotes;
                Esperar(Balas.Tiradas == 2000 && rebotesMitad > 900 && rebotesMitad < 1100, $"con el calibre igual al blindaje rebota la mitad ({rebotesMitad} de 2000, {Balas.Tiradas} dados)");
                Balas.ReiniciarContadores();
                bool igual = true;
                for (int i = 0; i < secuencia.Length; i++)
                {
                    Balas.Disparar(p3, Vector3.forward, 50f, ajeno, new DatosDanio { cantidad = 0.001f, direccion = Vector3.forward, tipo = TipoDanio.Bala, calibre = 10f });
                    igual &= Balas.Ultimo.rebote == secuencia[i];
                }
                Esperar(igual, "los dados repiten la secuencia con la misma semilla");
                Esperar(estado.Equals(UnityEngine.Random.state), "los dados del rebote no tocan UnityEngine.Random");
                a.salud.AlDanio -= alDanio;
                a.salud.blindaje = 0f;
                Balas.Quitar(a);

                // ---------------------------------------------------------------- Impacto.Golpear con un blanco
                Vector3 p4 = Base + new Vector3(1500f, 0f, 0f);
                var e = Blanco("E", p4, 2f, objetos, blancos);
                e.fraccion = 0.5f;
                e.salud.maxima = e.salud.actual = 1000f;
                Balas.Registrar(e);
                int golpes = 0;
                Action<DatosDanio> alGolpe = d => golpes++;
                e.salud.AlDanio += alGolpe;
                vida = e.salud.actual;
                int tocados = Impacto.Golpear(p4 + Vector3.right * 4f, 8f, 40f, 10f, TipoDanio.Explosion, null, 0.6f, false);
                float esperado = 40f * (1f - 0.6f * 2f / 8f) * 0.5f;
                Esperar(tocados == 1 && golpes == 1 && Mathf.Abs(vida - e.salud.actual - esperado) < 1e-3f,
                        $"Golpear dana al blanco una vez con la caida y la fraccion ({vida - e.salud.actual:0.000}, esperado {esperado:0.000})");
                golpes = 0;
                Impacto.Golpear(p4 + Vector3.right * 4f, 8f, 40f, 10f, TipoDanio.Explosion, e.raiz.gameObject, 0.6f, false);
                Impacto.Golpear(p4 + Vector3.right * 20f, 8f, 40f, 10f, TipoDanio.Explosion, null, 0.6f, false);
                Impacto.Golpear(p4 + Vector3.right * 4f, 8f, 40f, 10f, TipoDanio.Explosion, null, 0.6f, false, s => s == e.salud);
                Esperar(golpes == 0, "ni a lo ignorado, ni fuera del radio, ni a lo perdonado");
                // Con colisionador y blanco a la vez: una sola vez.
                e.salud.gameObject.AddComponent<SphereCollider>().radius = 2f;
                Physics.SyncTransforms();
                Impacto.Golpear(p4 + Vector3.right * 4f, 8f, 40f, 10f, TipoDanio.Explosion, null, 0.6f, false);
                Esperar(golpes == 1, $"con colisionador y blanco, una sola vez ({golpes})");
                e.salud.AlDanio -= alGolpe;
                Balas.Quitar(e);

                // ---------------------------------------------------------------- Armas
                Esperar(Arma.ClaseDe("kk_ak47_pbr") == ClaseArma.FusilAsalto && Arma.ClaseDe("arma_subfusil_pbr") == ClaseArma.Subfusil
                        && Arma.ClaseDe("bk_arcabuz_pbr") == ClaseArma.Arcabuz && Arma.ClaseDe("ww2_mg42") == ClaseArma.Ametralladora,
                        "las clases por el nombre del modelo");
                Esperar(Arma.ClaseDe("cosa_rara") == ClaseArma.FusilAsalto && !Arma.Reconocer("cosa_rara", out _) && !Arma.Reconocer(null, out _),
                        "lo que no conoce, fusil de asalto (y dice que no lo conoce)");
                // Las del kit de armas de AssetsCreator, por su nombre y por el de su malla ('<n>_pbr', sin mirar mayusculas): las dos
                // escopetas, por gd_escopeta.
                var kit = new (string nombre, ClaseArma clase)[]
                {
                    ("gd_glock17", ClaseArma.Pistola), ("gd_colt_saa", ClaseArma.Pistola), ("gd_mp5", ClaseArma.Subfusil),
                    ("gd_ak47", ClaseArma.FusilAsalto), ("gd_escopeta_corredera", ClaseArma.Escopeta), ("gd_escopeta_yuxtapuesta", ClaseArma.Escopeta),
                    ("gd_remington700", ClaseArma.FusilCaza), ("gd_m2_browning", ClaseArma.Ametralladora),
                };
                var malKit = new List<string>();
                foreach (var (n, cl) in kit)
                    if (Arma.ClaseDe(n) != cl || Arma.ClaseDe(n + "_pbr") != cl || Arma.ClaseDe(n.ToUpperInvariant() + "_PBR") != cl || !Arma.Reconocer(n + "_pbr", out _))
                        malKit.Add($"{n}: {Arma.ClaseDe(n + "_pbr")}");
                Esperar(malKit.Count == 0, $"las armas del kit por su nombre y por su malla ({string.Join(", ", malKit)})");
                // Dos prefijos que se solapan, puestos al final de la tabla: gana el mas largo, no el primero ni el ultimo que
                // encaje (con el primero, la yuxtapuesta saldria escopeta por gd_escopeta; con el ultimo, la de corredera y el
                // MP5 saldrian pistola por gd_).
                int nPrefijos = Arma.Prefijos.Count;
                Arma.Prefijos.Add(("gd_escopeta_yux", ClaseArma.FusilCaza));
                Arma.Prefijos.Add(("gd_", ClaseArma.Pistola));
                try
                {
                    Esperar(Arma.ClaseDe("gd_escopeta_yuxtapuesta_pbr") == ClaseArma.FusilCaza && Arma.ClaseDe("gd_escopeta_corredera") == ClaseArma.Escopeta
                            && Arma.ClaseDe("gd_mp5") == ClaseArma.Subfusil && Arma.ClaseDe("gd_otra") == ClaseArma.Pistola,
                            $"gana el prefijo mas largo que encaje (yuxtapuesta {Arma.ClaseDe("gd_escopeta_yuxtapuesta_pbr")}, corredera " +
                            $"{Arma.ClaseDe("gd_escopeta_corredera")}, MP5 {Arma.ClaseDe("gd_mp5")}, otra {Arma.ClaseDe("gd_otra")})");
                }
                finally { Arma.Prefijos.RemoveRange(nPrefijos, Arma.Prefijos.Count - nPrefijos); }
                var hgo = Nuevo("Humano", Base + new Vector3(2000f, 0f, 0f), objetos);
                var h = hgo.AddComponent<Humano>();
                var fa = Arma.Ficha(ClaseArma.FusilAsalto);
                Esperar(fa.danio == h.danioBala && fa.cadencia == h.cadencia && fa.dispersion == h.dispersion && fa.rafaga == h.duracionRafaga
                        && fa.pausa == h.pausaRafagas && Mathf.Abs(fa.alcance - h.distanciaDisparo * 1.5f) < 1e-3f && fa.calibre == h.calibre
                        && fa.calibre == Calibres.FusilAsalto && fa.proyectiles == 1 && h.proyectiles == 1 && fa.fogonazo == 1f && fa.tono == 1f && h.arma == ClaseArma.FusilAsalto,
                        "la ficha del fusil de asalto es la de serie de Humano (fogonazo y tono 1)");
                h.Armar(ClaseArma.Escopeta);
                Esperar(h.arma == ClaseArma.Escopeta && h.proyectiles == 9 && h.calibre == Calibres.Perdigon && Mathf.Abs(h.danioBala - 7.2f) < 1e-4f
                        && Mathf.Abs(h.distanciaDisparo - 30f) < 1e-3f && h.abanico > 0f, "Armar copia la ficha entera");
                var nueva = fichaPistola;
                nueva.danio = 99f;
                Arma.Ajustar(ClaseArma.Pistola, nueva);
                Esperar(Arma.Ficha(ClaseArma.Pistola).danio == 99f, "Ajustar cambia la ficha");
                Arma.Ajustar(ClaseArma.Pistola, fichaPistola);
                Esperar(Arma.Ficha(ClaseArma.Canon).calibre == Calibres.Canon && Arma.Ficha((ClaseArma)99).calibre == Calibres.FusilAsalto, "el canon y una clase fuera de la tabla");
                var escopeta = Arma.Ficha(ClaseArma.Escopeta);
                var caza = Arma.Ficha(ClaseArma.FusilCaza);
                Esperar(Mathf.Abs(escopeta.fogonazo - caza.fogonazo) >= 0.3f && Mathf.Abs(escopeta.tono - caza.tono) >= 0.15f,
                        $"la escopeta y el fusil de caza no se ven ni se oyen igual (fogonazo {escopeta.fogonazo}/{caza.fogonazo}, tono {escopeta.tono}/{caza.tono})");
                // El montaje de las armas: el AK con la regla de siempre (ni esta en Agarres ni tiene ficha).
                var agarres = ConstructorHumanos.Agarres;
                Esperar(!agarres.ContainsKey("kk_ak47") && !File.Exists(ConstructorHumanos.RutaFicha("kk_ak47")), "los agarres: el AK con la regla");
                Fichas();
                // El arma de un humano por la malla de la mano (y un Arma en sus hijos manda).
                var mano = Nuevo("AC_arma_r", hgo.transform.position, objetos, hgo.transform);
                mano.name = "AC_arma_r";
                var modelo = Nuevo("Arma", hgo.transform.position, objetos, mano.transform);
                var pieza = Nuevo("Pieza", hgo.transform.position, objetos, modelo.transform);
                pieza.name = "gd_escopeta_corredera_pbr";
                var malla = new Mesh { name = "gd_escopeta_corredera_pbr" };
                pieza.AddComponent<MeshFilter>().sharedMesh = malla;
                var porMalla = Arma.DeHumano(hgo.transform, ClaseArma.Pistola);
                pieza.name = "otra";
                var porNombre = Arma.DeHumano(hgo.transform, ClaseArma.Pistola);
                malla.name = "otra";
                pieza.name = "gd_escopeta_corredera_pbr";
                var porObjeto = Arma.DeHumano(hgo.transform, ClaseArma.Pistola);
                pieza.name = "otra";
                var porDefecto = Arma.DeHumano(hgo.transform, ClaseArma.Pistola);
                modelo.AddComponent<Arma>().clase = ClaseArma.FusilCaza;
                var porComponente = Arma.DeHumano(hgo.transform, ClaseArma.Pistola);
                Object.DestroyImmediate(malla);
                Esperar(porMalla == ClaseArma.Escopeta && porDefecto == ClaseArma.Pistola && porComponente == ClaseArma.FusilCaza,
                        $"DeHumano: por la malla ({porMalla}), sin nada conocido lo de su prefab ({porDefecto}), con un Arma lo que diga ({porComponente})");
                Esperar(porNombre == ClaseArma.Escopeta && porObjeto == ClaseArma.Escopeta,
                        $"DeHumano: por la malla aunque el objeto se llame de otra forma ({porNombre}) y por el objeto si la malla no dice nada ({porObjeto})");
            }
            finally
            {
                Balas.AlImpactar -= contar;
                foreach (var bl in blancos) Balas.Quitar(bl);
                Arma.Ajustar(ClaseArma.Pistola, fichaPistola);
                for (int i = objetos.Count - 1; i >= 0; i--)
                    if (objetos[i] != null) Object.DestroyImmediate(objetos[i]);
                Balas.ReiniciarContadores();
            }

            Debug.Log($"[GD] BALAS: {(_fallos == 0 ? "OK" : _fallos + " fallos")}");
            return _fallos;
        }

        /// <summary>
        /// La ficha de las armas del kit de armas de AssetsCreator (ConstructorHumanos.LeerFicha y AcabadoDe), con tres _agarre.json
        /// como los del kit escritos aqui, en una carpeta temporal (sin AssetsCreator ni las armas traidas): un fusil de caza con
        /// guardamanos, una pistola sin el y sin clase (la de su nombre) y la M2, montada y con su rugosidad; y tres que no
        /// valen del todo: sin la boca, mal escrita y con una clase que no existe (la de su nombre).
        /// </summary>
        static void Fichas()
        {
            string carpeta = Path.Combine(Path.GetTempPath(), "gd_pruebabalas_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(carpeta);
            try
            {
                string Escribir(string nombre, string json)
                {
                    string ruta = Path.Combine(carpeta, nombre + "_agarre.json");
                    File.WriteAllText(ruta, json);
                    return ruta;
                }
                string rifle = Escribir("gd_rifle_prueba",
                    "{\n \"nombre\": \"gd_rifle_prueba\",\n \"clase\": \"FusilCaza\",\n \"unidades\": \"m\",\n" +
                    " \"agarre\": [0.01, 0.02, 0.003],\n \"boca\": [0.83286, 0.07634, 0.0],\n \"guardamanos\": [0.38298, 0.05464, 0.0],\n" +
                    " \"ejes\": {\n  \"canon\": \"+X\",\n  \"arriba\": \"+Y\"\n },\n" +
                    " \"caja\": {\n  \"min\": [-0.2, -0.1, -0.03],\n  \"max\": [0.83, 0.1, 0.03]\n },\n" +
                    " \"materiales\": {\n  \"acero_cerrojo\": \"metal\",\n  \"madera_nogal\": \"madera\",\n  \"pintura_coche_pavonado\": \"pintura_coche\",\n" +
                    "  \"plastico_filete\": \"plastico (por defecto)\"\n },\n \"nota_unity\": \"en Unity la x cambia de signo\"\n}\n");
                string pistola = Escribir("gd_glock17",
                    "{\n \"agarre\": [0.004, -0.002, 0.001],\n \"boca\": [0.15761, 0.07394, 0.0],\n \"guardamanos\": null,\n" +
                    " \"ejes\": {\n  \"canon\": \"+X\",\n  \"arriba\": \"+Y\"\n },\n \"materiales\": {\n  \"acero_canon\": \"metal\"\n }\n}\n");
                string m2 = Escribir("gd_m2_prueba",
                    "{\n \"nombre\": \"gd_m2_prueba\",\n \"clase\": \"Ametralladora\",\n \"agarre\": [0.0, 0.0, 0.0],\n \"boca\": [1.63543, 0.01699, 0.0],\n" +
                    " \"guardamanos\": [0.0, 0.0, -0.095],\n \"ejes\": {\n  \"canon\": \"+X\",\n  \"arriba\": \"+Y\"\n },\n" +
                    " \"materiales\": {\n  \"goma_cachas\": \"goma\",\n  \"pintura_canon\": \"pintura_mate\"\n },\n" +
                    " \"montaje\": {\n  \"pivote\": [0.62904, -0.18699, 0.0],\n  \"eje_giro\": \"+Y\",\n  \"radio_columna\": 0.0407,\n  \"base\": false,\n" +
                    "  \"nota\": \"el poste lo pone el juego\"\n },\n \"rugosidad\": {\n  \"goma_cachas\": 0.85,\n  \"pintura_canon\": 0.5\n }\n}\n");
                string sinBoca = Escribir("gd_rota", "{\n \"agarre\": [0.0, 0.0, 0.0]\n}\n");
                string malFormada = Escribir("gd_mal", "{ \"agarre\": [0.0, 0.0, ");
                string claseRara = Escribir("gd_mp5_prueba", "{\n \"clase\": \"Lanzallamas\",\n \"agarre\": [0.0, 0.0, 0.0],\n \"boca\": [0.40103, 0.1097, 0.0]\n}\n");

                var fr = ConstructorHumanos.LeerFicha(rifle);
                Esperar(fr != null && fr.nombre == "gd_rifle_prueba" && fr.clase == ClaseArma.FusilCaza && fr.agarre.ejes
                        && fr.agarre.canon == Vector3.left && fr.agarre.arriba == Vector3.up
                        && Cerca(fr.agarre.empunadura, new Vector3(-0.01f, 0.02f, 0.003f) + ConstructorHumanos.DesfaseAgarre(ClaseArma.FusilCaza))
                        && Cerca(fr.agarre.boca, new Vector3(-0.83286f, 0.07634f, 0f))
                        && fr.agarre.guardamanos.HasValue && Cerca(fr.agarre.guardamanos.Value, new Vector3(-0.38298f, 0.05464f, 0f)) && !fr.agarre.montada,
                        $"la ficha de un fusil de caza: la x cambiada de signo, el desfase de su clase y el guardamanos (empunadura {(fr != null ? fr.agarre.empunadura.ToString("F4") : "-")})");
                Esperar(fr != null && fr.recetas.Count == 4 && fr.recetas["acero_cerrojo"] == "metal" && fr.recetas["pintura_coche_pavonado"] == "pintura_coche"
                        && fr.recetas["plastico_filete"] == "plastico (por defecto)" && fr.rugosidad.Count == 0,
                        $"las recetas de sus materiales ({(fr != null ? string.Join(", ", fr.recetas) : "-")})");
                var fp = ConstructorHumanos.LeerFicha(pistola);
                Esperar(fp != null && fp.nombre == "gd_glock17" && fp.clase == ClaseArma.Pistola && Cerca(fp.agarre.empunadura, new Vector3(-0.004f, -0.002f, 0.001f))
                        && Cerca(fp.agarre.boca, new Vector3(-0.15761f, 0.07394f, 0f)) && !fp.agarre.guardamanos.HasValue && !fp.agarre.montada && fp.recetas.Count == 1,
                        "la ficha de una pistola: sin clase, la de su nombre; sin desfase y sin guardamanos");
                var fm = ConstructorHumanos.LeerFicha(m2);
                Esperar(fm != null && fm.clase == ClaseArma.Ametralladora && Cerca(fm.agarre.empunadura, Vector3.zero) && fm.agarre.montada
                        && Cerca(fm.agarre.pivote, new Vector3(-0.62904f, -0.18699f, 0f)) && fm.agarre.ejeGiro == Vector3.up && Mathf.Abs(fm.agarre.radioColumna - 0.0407f) < 1e-6f
                        && fm.agarre.guardamanos.HasValue && Cerca(fm.agarre.guardamanos.Value, new Vector3(0f, 0f, -0.095f))
                        && fm.rugosidad.Count == 2 && Mathf.Abs(fm.rugosidad["pintura_canon"] - 0.5f) < 1e-6f,
                        "la ficha de la M2: montada, con el pivote cambiado de signo, el radio de la columna y su rugosidad");
                Esperar(ConstructorHumanos.LeerFicha(Path.Combine(carpeta, "no_esta_agarre.json")) == null && ConstructorHumanos.LeerFicha(sinBoca) == null
                        && ConstructorHumanos.LeerFicha(malFormada) == null,
                        "sin ficha, sin la boca o mal escrita, nada (va con la regla)");
                var fc = ConstructorHumanos.LeerFicha(claseRara);
                Esperar(fc != null && fc.clase == ClaseArma.Subfusil && fc.nombre == "gd_mp5_prueba",
                        $"una clase que no existe: la de su nombre ({(fc != null ? fc.clase.ToString() : "-")})");
                // El desfase de su clase se suma a la empunadura; de serie, ninguno (en los clips el puno cierra en el hueso).
                bool sinDesfase = true;
                foreach (ClaseArma c in Enum.GetValues(typeof(ClaseArma))) sinDesfase &= ConstructorHumanos.DesfaseAgarre(c) == Vector3.zero;
                var desfases = ConstructorHumanos.DesfasesAgarre;
                bool habia = desfases.TryGetValue(ClaseArma.FusilCaza, out var previo);
                ConstructorHumanos.FichaArma conDesfase;
                desfases[ClaseArma.FusilCaza] = new Vector3(0.1f, -0.05f, 0f);
                try { conDesfase = ConstructorHumanos.LeerFicha(rifle); }
                finally
                {
                    if (habia) desfases[ClaseArma.FusilCaza] = previo;
                    else desfases.Remove(ClaseArma.FusilCaza);
                }
                Esperar(sinDesfase && conDesfase != null && Cerca(conDesfase.agarre.empunadura, new Vector3(0.09f, -0.03f, 0.003f)),
                        "el desfase de su clase se suma a la empunadura (de serie, ninguno)");

                // Los materiales: metal lo que su receta dice (la pintura con laca lleva un mapa metallic de escamas y no lo es; sin
                // receta, lo que tenga mapa metallic) y la suavidad = 1 - la rugosidad de la ficha o, si no, la de su .mtl.
                var acero = ConstructorHumanos.AcabadoDe(fr, "acero_cerrojo", true, 0.25f);
                var laca = ConstructorHumanos.AcabadoDe(fr, "pintura_coche_pavonado", true, 0.35f);
                var sinReceta = ConstructorHumanos.AcabadoDe(fr, "otro_material", true, null);
                var canon = ConstructorHumanos.AcabadoDe(fm, "pintura_canon", false, 0.73f);
                var sinFicha = ConstructorHumanos.AcabadoDe(null, "pavonado_fusil_g98", true, 0.2f);
                Esperar(acero.metal == ConstructorHumanos.MetalizadoMetal && Mathf.Abs(acero.suavidad - 0.75f) < 1e-5f
                        && laca.metal == 0f && Mathf.Abs(laca.suavidad - 0.65f) < 1e-5f && sinReceta.metal == ConstructorHumanos.MetalizadoMetal
                        && canon.metal == 0f && Mathf.Abs(canon.suavidad - 0.5f) < 1e-5f
                        && Mathf.Abs(sinFicha.metal - 0.85f) < 1e-5f && Mathf.Abs(sinFicha.suavidad - 0.55f) < 1e-5f
                        && acero.tinte == Color.white && sinFicha.tinte == Color.white,
                        $"el acabado de los materiales (acero {acero.metal}/{acero.suavidad}, laca {laca.metal}/{laca.suavidad}, canon de la M2 {canon.suavidad}, sin ficha {sinFicha.metal}/{sinFicha.suavidad})");
                Debug.Log($"[GD] BALAS fichas: fusil de caza empunadura {fr?.agarre.empunadura.ToString("F3")}, boca {fr?.agarre.boca.ToString("F3")}; " +
                          $"M2 pivote {fm?.agarre.pivote.ToString("F3")}, columna {fm?.agarre.radioColumna:0.0000} m");
            }
            finally
            {
                try { Directory.Delete(carpeta, true); }
                catch (Exception e) { Debug.LogWarning($"[GD] No se borra {carpeta}: {e.Message}"); }
            }
        }

        static bool Cerca(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 1e-10f;

        static GameObject Nuevo(string nombre, Vector3 pos, List<GameObject> objetos, Transform padre = null)
        {
            var go = new GameObject("PruebaBalas_" + nombre);
            if (padre != null) go.transform.SetParent(padre, false);
            go.transform.position = pos;
            if (padre == null) objetos.Add(go);
            return go;
        }

        static BlancoEsfera Blanco(string nombre, Vector3 centro, float radio, List<GameObject> objetos, List<BlancoEsfera> blancos, Transform padre = null)
        {
            var go = Nuevo("Blanco" + nombre, centro, objetos, padre);
            var s = go.AddComponent<Salud>();
            s.maxima = s.actual = 100000f;
            var b = new BlancoEsfera { salud = s, raiz = go.transform, centro = centro, radio = radio };
            blancos.Add(b);
            return b;
        }

        static void Esperar(bool cumple, string que)
        {
            if (cumple) return;
            _fallos++;
            Debug.LogError("[GD] BALAS FALLA: " + que);
        }
    }
}
