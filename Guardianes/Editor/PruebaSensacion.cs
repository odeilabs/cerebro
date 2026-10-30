using System;
using System.Collections.Generic;
using Guardianes.Sensacion;
using UnityEditor;
using UnityEngine;

namespace Guardianes.EditorTools
{
    /// <summary>
    /// Comprueba la lógica de la sensación común sin escena (curva de parada de cada arquetipo,
    /// buffer de entrada, margen de apoyo, asistencia de puntería, medidor, caída del trauma).
    /// Desde batch: -executeMethod Guardianes.EditorTools.PruebaSensacion.ComprobarDesdeBatch.
    /// </summary>
    public static class PruebaSensacion
    {
        static int _fallos;

        public static void ComprobarDesdeBatch()
        {
            int fallos;
            try { fallos = Comprobar(); }
            catch (Exception e) { Debug.LogException(e); fallos = 1; }
            EditorApplication.Exit(fallos == 0 ? 0 : 1);
        }

        [MenuItem("Guardianes/Diagnostico/Comprobar sensacion")]
        public static int Comprobar()
        {
            _fallos = 0;

            // Parada: nada bajo el umbral, crece con la fuerza, con tope; el gigante para más que el ágil.
            foreach (ArquetipoSensacion a in Enum.GetValues(typeof(ArquetipoSensacion)))
            {
                var p = PerfilSensacion.Crear(a);
                float roce = p.Parada(0.05f), medio = p.Parada(0.5f), fuerte = p.Parada(1f), remate = p.Parada(5f);
                Debug.Log($"[GD] SENSACION {a}: parada roce {roce * 1000f:0} medio {medio * 1000f:0} fuerte {fuerte * 1000f:0} remate {remate * 1000f:0} ms, trauma {p.traumaPorFuerza:0.00}, sacudida {p.sacudidaGrados} a {p.sacudidaFrecuencia} Hz");
                Esperar(roce == 0f, $"{a}: un roce no para");
                Esperar(medio > 0f && medio <= fuerte && fuerte <= remate, $"{a}: la parada crece con la fuerza");
                Esperar(remate <= p.paradaMaxima + 1e-5f && fuerte >= 0.05f && fuerte <= 0.2f, $"{a}: parada fuerte entre 50 y 200 ms y con tope");
                UnityEngine.Object.DestroyImmediate(p);
            }
            var gigante = PerfilSensacion.Crear(ArquetipoSensacion.Gigante);
            var agil = PerfilSensacion.Crear(ArquetipoSensacion.Agil);
            Esperar(gigante.Parada(1f) > agil.Parada(1f) && gigante.traumaPorFuerza > agil.traumaPorFuerza, "el gigante pesa más que el ágil");
            UnityEngine.Object.DestroyImmediate(gigante);
            UnityEngine.Object.DestroyImmediate(agil);

            // Buffer de entrada con un reloj de prueba.
            float ahora = 0f;
            Medidor.reloj = () => ahora;
            Medidor.Reiniciar();
            var buffer = new BufferEntrada { ventana = 0.15f, reloj = () => ahora };
            buffer.Pulsar(1);
            ahora = 0.1f;
            Esperar(buffer.Consumir(1), "el buffer guarda una pulsación 100 ms");
            Esperar(!buffer.Consumir(1), "una pulsación solo se gasta una vez");
            buffer.Pulsar(2);
            ahora = 0.3f;
            Esperar(!buffer.Consumir(2), "el buffer olvida pasada la ventana");
            Esperar(buffer.Consumir(2, 0.25f), "con una ventana propia más larga, sí");

            // Margen de apoyo (coyote time).
            var margen = new MargenApoyo { ventana = 0.12f, reloj = () => ahora };
            margen.Actualizar(true);
            ahora += 0.1f; margen.Actualizar(false);
            Esperar(margen.Puede(), "a los 100 ms sin apoyo aún se puede");
            margen.Gastar();
            Esperar(!margen.Puede(), "el margen se gasta");
            margen.Actualizar(true);
            ahora += 0.2f; margen.Actualizar(false);
            Esperar(!margen.Puede(), "a los 200 ms ya no");

            // Asistencia de puntería.
            var blancos = new List<Vector3> { new Vector3(0f, 0f, 10f), new Vector3(3f, 0f, 5f), new Vector3(-8f, 0f, 2f), new Vector3(0f, 0f, -5f), new Vector3(0f, 0f, 40f) };
            int elegido = AsistenciaPunteria.Elegir(Vector3.zero, Vector3.forward, 20f, 60f, new[] { 0, 1, 2, 3, 4 }, i => blancos[i], null) ;
            Esperar(elegido == 0 || elegido == 1, $"la asistencia elige uno de delante (eligió {elegido})");
            int ninguno = AsistenciaPunteria.Elegir(Vector3.zero, Vector3.forward, 20f, 30f, new[] { 2, 3, 4 }, i => blancos[i], null);
            Esperar(ninguno == default(int), "ni detrás, ni al lado fuera del cono, ni fuera de alcance");
            Vector3 corregida = AsistenciaPunteria.Corregir(Vector3.forward * 2f, new Vector3(1f, 0f, 1f), 10f);
            Esperar(Mathf.Abs(Vector3.Angle(Vector3.forward, corregida) - 10f) < 0.1f && Mathf.Abs(corregida.magnitude - 2f) < 1e-3f, "la corrección gira 10° y conserva el módulo");
            Vector3 cerca = AsistenciaPunteria.Corregir(Vector3.forward, new Vector3(0.05f, 0f, 1f), 10f);
            Esperar(Vector3.Angle(cerca, new Vector3(0.05f, 0f, 1f)) < 0.1f, "si el blanco está a menos del máximo, apunta justo");

            // Medidor.
            Medidor.Reiniciar();
            ahora = 10f; Medidor.Pedir(7); ahora = 10.08f; Medidor.Responder(7);
            // Una peticion que caduco sin hacerse no cuenta: la siguiente se mide desde la suya, y una
            // respuesta a una peticion perdida (mas de un segundo) tampoco.
            ahora = 30f; Medidor.Pedir(8); ahora = 40f; Medidor.Pedir(8); ahora = 40.12f; Medidor.Responder(8);
            ahora = 50f; Medidor.Pedir(9); ahora = 52f; Medidor.Responder(9);
            ahora = 10.08f; Medidor.Recompensa(); ahora = 13f; Medidor.Recompensa(); ahora = 20f; Medidor.Recompensa();
            Medidor.Control(false); ahora = 21.5f; Medidor.Control(true);
            Debug.Log(Medidor.Informe());
            Esperar(Mathf.Abs(Medidor.LatenciaMedia - 0.10f) < 1e-3f && Mathf.Abs(Medidor.LatenciaMaxima - 0.12f) < 1e-3f, "latencia medida (sin las peticiones perdidas)");
            Esperar(Mathf.Abs(Medidor.EntreRecompensasMedio - 4.96f) < 1e-3f && Mathf.Abs(Medidor.EntreRecompensasMaximo - 7f) < 1e-3f, "tiempo entre recompensas");
            Esperar(Mathf.Abs(Medidor.PeorSinControl - 1.5f) < 1e-3f, "tiempo sin control");
            Medidor.reloj = () => Time.unscaledTime;
            Medidor.Reiniciar();

            // Trauma: suma con tope, sacudida al cuadrado, y las opciones la apagan.
            var go = new GameObject("PruebaTrauma") { hideFlags = HideFlags.HideAndDontSave };
            var tc = go.AddComponent<TraumaCamara>();
            tc.Sumar(0.5f);
            Esperar(Mathf.Abs(tc.Sacudida - 0.25f) < 1e-4f, "sacudida = trauma²");
            tc.Sumar(2f);
            Esperar(tc.trauma == 1f, "el trauma tiene tope 1");
            Sensaciones.Opciones.sacudida = 0f;
            Esperar(tc.Sacudida == 0f, "la opción de accesibilidad apaga la sacudida");
            Sensaciones.Opciones.sacudida = 1f;
            UnityEngine.Object.DestroyImmediate(go);

            Debug.Log($"[GD] SENSACION: {(_fallos == 0 ? "OK" : _fallos + " fallos")}");
            return _fallos;
        }

        static void Esperar(bool cumple, string que)
        {
            if (cumple) return;
            _fallos++;
            Debug.LogError("[GD] SENSACION FALLA: " + que);
        }
    }
}
