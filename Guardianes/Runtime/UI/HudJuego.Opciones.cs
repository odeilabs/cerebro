using System.Collections.Generic;
using Guardianes.Juego;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Guardianes.UI
{
    /// <summary>
    /// El panel de OPCIONES (desde el menu de inicio y el de pausa; se maneja con raton, teclado o mando): calidad,
    /// pantalla, resolucion, VSync, volumen, idioma, sensibilidad, invertir el eje Y, el tamano y el fondo de los
    /// subtitulos, daltonismo y dificultad (OpcionesJuego), y el de CONTROLES: cada accion que el juego ha registrado
    /// con sus teclas y las del mando; al pulsar una, la siguiente tecla que se pulse la sustituye (Esc cancela).
    /// Los subtitulos (dialogos y narracion) se agrandan y llevan un fondo oscuro segun las opciones.
    /// </summary>
    public partial class HudJuego
    {
        GameObject _opciones, _controles, _volverA;
        readonly List<(Text texto, System.Func<string> valor)> _filasOpciones = new List<(Text, System.Func<string>)>();
        readonly List<(Text teclado, Text mando, InputAction accion)> _filasControles = new List<(Text, Text, InputAction)>();
        Transform _listaControles;
        InputActionRebindingExtensions.RebindingOperation _reasignando;
        Image _fondoDialogo, _fondoNarracion;
        int _tamDialogo, _tamNarracion, _tamSubtitulo;

        /// <summary>Hay un panel de opciones o de controles abierto.</summary>
        public bool OpcionesAbiertas => (_opciones != null && _opciones.activeSelf) || (_controles != null && _controles.activeSelf);

        /// <summary>Para que cada juego cuelgue sus botones de los menus.</summary>
        protected Transform RaizInicio => _inicio != null ? _inicio.transform : null;
        protected Transform RaizPausa => _menu != null ? _menu.transform : null;

        static string SiNo(bool b) => Textos.T(b ? "sí" : "no");

        /// <summary>Los textos fijos (titulos, botones): el espanol y lo que se ensena.</summary>
        readonly List<(Text texto, string es, string mostrado)> _traducibles = new List<(Text, string, string)>();

        /// <summary>Al cambiar de idioma: los textos fijos que nadie ha cambiado, otra vez traducidos.</summary>
        void Retraducir()
        {
            for (int i = 0; i < _traducibles.Count; i++)
            {
                var (t, es, mostrado) = _traducibles[i];
                if (t == null || t.text != mostrado) continue;
                t.text = Textos.T(es);
                _traducibles[i] = (t, es, t.text);
            }
            RefrescarOpciones();
        }

        void OnDestroy()
        {
            OpcionesJuego.AlCambiar -= AplicarSubtitulos;
            OpcionesJuego.AlCambiar -= Retraducir;
        }

        void ConstruirOpciones(Transform r)
        {
            _opciones = Panel(r, "MenuOpciones", "OPCIONES");
            var m = _opciones.transform;
            float y0 = 220f, paso = 66f;
            void Fila(int col, int fila, string nombre, System.Func<string> valor, System.Action cambiar)
            {
                var t = Boton(m, nombre, new Vector2(col == 0 ? -330f : 330f, y0 - fila * paso), () => { cambiar(); RefrescarOpciones(); }, 620f);
                _filasOpciones.Add((t, () => $"{Textos.T(nombre)}: {Textos.T(valor())}"));
            }
            Fila(0, 0, "Calidad", () => CalidadJuego.Instancia != null ? CalidadJuego.Nombre(CalidadJuego.Instancia.Actual) : "-", () =>
            {
                var c = CalidadJuego.Instancia;
                if (c != null) c.Aplicar((CalidadJuego.Nivel)(((int)c.Actual + 1) % 4), true);
            });
            Fila(0, 1, "Pantalla completa", () => SiNo(OpcionesJuego.PantallaCompleta), () => { OpcionesJuego.PantallaCompleta = !OpcionesJuego.PantallaCompleta; OpcionesJuego.Aplicar(); });
            Fila(0, 2, "Resolución", () => OpcionesJuego.NombreResolucion, () =>
            {
                int n = OpcionesJuego.Resoluciones.Length;
                OpcionesJuego.Resolucion = OpcionesJuego.Resolucion + 1 >= n ? -1 : OpcionesJuego.Resolucion + 1;
                OpcionesJuego.Aplicar();
            });
            Fila(0, 3, "Sincronía vertical", () => SiNo(OpcionesJuego.VSync), () => { OpcionesJuego.VSync = !OpcionesJuego.VSync; OpcionesJuego.Aplicar(); });
            Fila(0, 4, "Volumen", () => $"{Mathf.RoundToInt(OpcionesJuego.Volumen * 100f)} %", () =>
            {
                OpcionesJuego.Volumen = OpcionesJuego.Volumen >= 0.99f ? 0f : Mathf.Min(1f, OpcionesJuego.Volumen + 0.25f);
                OpcionesJuego.Aplicar();
            });
            Fila(0, 5, "Idioma", () => OpcionesJuego.Idioma == "en" ? "English" : "Español", () =>
            {
                OpcionesJuego.Idioma = OpcionesJuego.Idioma == "en" ? "es" : "en";
                OpcionesJuego.Aplicar();
            });
            Fila(1, 0, "Sensibilidad de la cámara", () => OpcionesJuego.Sensibilidad.ToString("0.00"), () =>
            {
                float[] pasos = { 0.5f, 0.75f, 1f, 1.25f, 1.5f, 2f };
                int i = System.Array.FindIndex(pasos, p => Mathf.Abs(p - OpcionesJuego.Sensibilidad) < 0.01f);
                OpcionesJuego.Sensibilidad = pasos[(i + 1) % pasos.Length];
                OpcionesJuego.Aplicar();
            });
            Fila(1, 1, "Invertir el eje vertical", () => SiNo(OpcionesJuego.InvertirY), () => { OpcionesJuego.InvertirY = !OpcionesJuego.InvertirY; OpcionesJuego.Aplicar(); });
            Fila(1, 2, "Subtítulos", () => OpcionesJuego.Subtitulos >= 1.45f ? "muy grandes" : OpcionesJuego.Subtitulos >= 1.2f ? "grandes" : "normales", () =>
            {
                OpcionesJuego.Subtitulos = OpcionesJuego.Subtitulos >= 1.45f ? 1f : OpcionesJuego.Subtitulos >= 1.2f ? 1.5f : 1.25f;
                OpcionesJuego.Aplicar();
            });
            Fila(1, 3, "Fondo de los subtítulos", () => SiNo(OpcionesJuego.FondoSubtitulos), () => { OpcionesJuego.FondoSubtitulos = !OpcionesJuego.FondoSubtitulos; OpcionesJuego.Aplicar(); });
            Fila(1, 4, "Daltonismo", () => OpcionesJuego.Paleta switch
            {
                OpcionesJuego.Daltonismo.Deuteranopia => "deuteranopía",
                OpcionesJuego.Daltonismo.Protanopia => "protanopía",
                OpcionesJuego.Daltonismo.Tritanopia => "tritanopía",
                _ => "no",
            }, () => { OpcionesJuego.Paleta = (OpcionesJuego.Daltonismo)(((int)OpcionesJuego.Paleta + 1) % 4); OpcionesJuego.Aplicar(); });
            Fila(1, 5, "Dificultad", () => OpcionesJuego.Nivel switch
            {
                OpcionesJuego.Dificultad.Facil => "fácil",
                OpcionesJuego.Dificultad.Dificil => "difícil",
                _ => "normal",
            }, () => { OpcionesJuego.Nivel = (OpcionesJuego.Dificultad)(((int)OpcionesJuego.Nivel + 1) % 3); OpcionesJuego.Aplicar(); });
            Fila(0, 6, "Consejos de la primera vez", () => SiNo(OpcionesJuego.Consejos), () => { OpcionesJuego.Consejos = !OpcionesJuego.Consejos; OpcionesJuego.Aplicar(); });
            Boton(m, "Controles", new Vector2(-330f, -260f), AbrirControles, 620f);
            Boton(m, "Volver", new Vector2(330f, -260f), CerrarOpciones, 620f);
            var nota = Etiqueta(m, "Fácil: recibes la mitad de daño y tardan más en reconocerte. Difícil: al revés.", new Vector2(0f, -330f), 20, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f));
            nota.color = new Color(0.8f, 0.78f, 0.7f);
            _opciones.SetActive(false);

            _controles = Panel(r, "MenuControles", "CONTROLES");
            var c2 = _controles.transform;
            var cab = Etiqueta(c2, "Acción                                  Teclado y ratón                               Mando", new Vector2(0f, 300f), 22, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f));
            cab.color = new Color(0.85f, 0.8f, 0.65f);
            _listaControles = new GameObject("Lista", typeof(RectTransform)).transform;
            _listaControles.SetParent(c2, false);
            Boton(c2, "Restablecer", new Vector2(-330f, -420f), () => { CancelarReasignar(); OpcionesJuego.RestablecerControles(); RefrescarControles(); }, 620f);
            Boton(c2, "Volver", new Vector2(330f, -420f), () => { CancelarReasignar(); _controles.SetActive(false); _opciones.SetActive(true); Seleccionar(_opciones); }, 620f);
            _controles.SetActive(false);
        }

        /// <summary>Un panel a pantalla completa, oscuro, con su titulo.</summary>
        GameObject Panel(Transform r, string nombre, string titulo)
        {
            var go = new GameObject(nombre, typeof(Image));
            go.transform.SetParent(r, false);
            var fondo = go.GetComponent<Image>();
            fondo.color = new Color(0f, 0f, 0f, 0.82f);
            Estirar(fondo.rectTransform);
            Etiqueta(go.transform, titulo, new Vector2(0f, 400f), 60, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f));
            return go;
        }

        /// <summary>Abre las opciones desde el menu que este abierto (al volver, vuelve a el).</summary>
        public void AbrirOpciones()
        {
            _volverA = _menu != null && _menu.activeSelf ? _menu : _inicio != null && _inicio.activeSelf ? _inicio : null;
            if (_volverA != null) _volverA.SetActive(false);
            _opciones.SetActive(true);
            RefrescarOpciones();
            Seleccionar(_opciones);
        }

        void CerrarOpciones()
        {
            _opciones.SetActive(false);
            if (_volverA != null) { _volverA.SetActive(true); Seleccionar(_volverA); }
            _volverA = null;
        }

        void RefrescarOpciones()
        {
            foreach (var f in _filasOpciones) f.texto.text = f.valor();
        }

        static void Seleccionar(GameObject panel)
        {
            var b = panel.GetComponentInChildren<Button>();
            if (b != null && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(b.gameObject);
        }

        // ------------------------------------------------------------------ Controles
        public void AbrirControles()
        {
            _opciones.SetActive(false);
            _controles.SetActive(true);
            // Las filas, con las acciones que haya registrado el juego.
            foreach (Transform h in _listaControles) Destroy(h.gameObject);
            _filasControles.Clear();
            var acciones = OpcionesJuego.Acciones;
            float alto = Mathf.Min(34f, 680f / Mathf.Max(1, acciones.Count));
            for (int i = 0; i < acciones.Count; i++)
            {
                var a = acciones[i];
                float y = 255f - i * alto;
                var n = Etiqueta(_listaControles, a.nombre, new Vector2(-560f, y), 20, TextAnchor.MiddleLeft, new Vector2(0.5f, 0.5f));
                n.rectTransform.sizeDelta = new Vector2(420f, alto);
                var accion = a.accion;
                Text tTeclado = null, tMando = null;
                tTeclado = BotonPequeno(_listaControles, new Vector2(80f, y), alto - 4f, () => Reasignar(accion, false, tTeclado));
                tMando = BotonPequeno(_listaControles, new Vector2(520f, y), alto - 4f, () => Reasignar(accion, true, tMando));
                _filasControles.Add((tTeclado, tMando, accion));
            }
            RefrescarControles();
            Seleccionar(_controles);
        }

        Text BotonPequeno(Transform padre, Vector2 pos, float alto, UnityEngine.Events.UnityAction accion)
        {
            var t = Boton(padre, "", pos, accion, 400f);
            var rt = (RectTransform)t.transform.parent;
            rt.sizeDelta = new Vector2(400f, alto);
            t.rectTransform.sizeDelta = rt.sizeDelta;
            t.fontSize = Mathf.Clamp(Mathf.RoundToInt(alto * 0.62f), 14, 22);
            return t;
        }

        void RefrescarControles()
        {
            foreach (var f in _filasControles)
            {
                f.teclado.text = OpcionesJuego.Teclas(f.accion, false);
                f.mando.text = OpcionesJuego.Teclas(f.accion, true);
            }
        }

        void Reasignar(InputAction a, bool mando, Text etiqueta)
        {
            CancelarReasignar();
            int indice = -1;
            for (int i = 0; i < a.bindings.Count; i++)
            {
                var b = a.bindings[i];
                if (b.isComposite || b.isPartOfComposite) continue;
                string ruta = b.effectivePath ?? "";
                bool esMando = ruta.StartsWith("<Gamepad>");
                if (esMando == mando) { indice = i; break; }
            }
            if (indice < 0) { etiqueta.text = Textos.T("(no se puede cambiar)"); return; }
            etiqueta.text = Textos.T(mando ? "Pulsa un botón del mando…" : "Pulsa una tecla…");
            bool estaba = a.enabled;
            a.Disable();
            var op = a.PerformInteractiveRebinding(indice)
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .WithControlsExcluding("<Mouse>/scroll")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnMatchWaitForAnother(0.1f);
            if (mando) op.WithControlsHavingToMatchPath("<Gamepad>");
            else op.WithControlsHavingToMatchPath("<Keyboard>").WithControlsHavingToMatchPath("<Mouse>");
            op.OnComplete(o =>
            {
                o.Dispose();
                _reasignando = null;
                if (estaba) a.Enable();
                OpcionesJuego.GuardarControl(a);
                RefrescarControles();
            });
            op.OnCancel(o =>
            {
                o.Dispose();
                _reasignando = null;
                if (estaba) a.Enable();
                RefrescarControles();
            });
            _reasignando = op.Start();
        }

        void CancelarReasignar()
        {
            if (_reasignando == null) return;
            _reasignando.Cancel();
            _reasignando = null;
        }

        // ------------------------------------------------------------------ Subtitulos
        void PrepararSubtitulos()
        {
            _tamDialogo = _dialogo.fontSize;
            _tamNarracion = _narracion.fontSize;
            _tamSubtitulo = _subtitulo.fontSize;
            _fondoDialogo = FondoDe(_dialogo);
            _fondoNarracion = FondoDe(_narracion);
            OpcionesJuego.AlCambiar += AplicarSubtitulos;
            OpcionesJuego.AlCambiar += Retraducir;
            AplicarSubtitulos();
        }

        Image FondoDe(Text t)
        {
            var f = new GameObject("Fondo_" + t.name, typeof(Image)).GetComponent<Image>();
            f.transform.SetParent(t.transform.parent, false);
            f.transform.SetSiblingIndex(t.transform.GetSiblingIndex());
            f.color = new Color(0f, 0f, 0f, 0.6f);
            f.raycastTarget = false;
            var rt = f.rectTransform;
            rt.anchorMin = t.rectTransform.anchorMin;
            rt.anchorMax = t.rectTransform.anchorMax;
            rt.pivot = t.rectTransform.pivot;
            f.enabled = false;
            return f;
        }

        void AplicarSubtitulos()
        {
            if (_dialogo == null) return;
            float k = Mathf.Clamp(OpcionesJuego.Subtitulos, 1f, 1.6f);
            _dialogo.fontSize = Mathf.RoundToInt(_tamDialogo * k);
            _narracion.fontSize = Mathf.RoundToInt(_tamNarracion * k);
            _subtitulo.fontSize = Mathf.RoundToInt(_tamSubtitulo * k);
            _dialogo.rectTransform.sizeDelta = new Vector2(_dialogo.rectTransform.sizeDelta.x, 120f * k);
        }

        /// <summary>El fondo oscuro detras del dialogo y de la narracion, del tamano de lo que dicen (si se pide).</summary>
        void ActualizarSubtitulos()
        {
            if (_fondoDialogo == null) return;
            Ajustar(_fondoDialogo, _dialogo);
            Ajustar(_fondoNarracion, _narracion);
        }

        static void Ajustar(Image fondo, Text t)
        {
            bool ver = OpcionesJuego.FondoSubtitulos && !string.IsNullOrEmpty(t.text) && t.color.a > 0.05f && t.gameObject.activeInHierarchy;
            fondo.enabled = ver;
            if (!ver) return;
            float ancho = Mathf.Min(t.preferredWidth, t.rectTransform.sizeDelta.x) + 40f;
            float alto = Mathf.Min(t.preferredHeight, 400f) + 16f;
            fondo.rectTransform.sizeDelta = new Vector2(ancho, alto);
            // Con el mismo pivote que el texto: el del dialogo crece hacia arriba desde su base; el de la narracion, desde el centro.
            fondo.rectTransform.pivot = t.rectTransform.pivot;
            fondo.rectTransform.anchoredPosition = t.rectTransform.anchoredPosition + (t.alignment == TextAnchor.LowerCenter ? new Vector2(0f, -8f) : Vector2.zero);
            fondo.color = new Color(0f, 0f, 0f, 0.6f * t.color.a);
        }
    }
}
