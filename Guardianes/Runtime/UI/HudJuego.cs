using System.Collections.Generic;
using Guardianes.Criaturas;
using Guardianes.Enemigos;
using Guardianes.Juego;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Guardianes.UI
{
    /// <summary>
    /// HUD de la saga (uGUI creado por codigo): la vida de la criatura, objetivo y zonas liberadas,
    /// avisos al descubrir y al liberar zonas, mira y los controles al empezar. Tambien las pantallas
    /// de la partida: fundidos, narracion, titulos, subtitulos de dialogo, marcador de destino, el
    /// panel de los eventos rapidos, el menu de inicio y el de pausa.
    /// Cada juego hereda y pinta lo suyo (las barras de furia de Kong, el crecimiento y las afinidades
    /// del basilisco) en ConstruirEstado / ActualizarEstado, y pone sus textos.
    /// </summary>
    public partial class HudJuego : MonoBehaviour
    {
        [Header("Textos del juego")]
        public string tituloJuego = "KING KONG";
        public string subtituloJuego = "El guardian de la isla";
        public string nombreCriatura = "KONG";
        [TextArea] public string textoAyuda = "";
        [TextArea] public string textoControles = "";
        public string textoPurificar = "Ira de la selva";
        public string textoHelicopteros = "Helicopteros de la compania.";
        public string textoModoLibre = "Modo libre";

        protected Criatura _criatura;
        protected Image _vida, _vidaRetardo, _mira, _negro;
        protected Text _aviso, _zonas, _ayuda, _objetivo, _narracion, _titulo, _subtitulo;
        protected float _vidaMostrada = 1f;
        float _avisoHasta;
        float _negroDesde, _negroHasta, _negroInicio, _negroFin;
        float _narracionDesde, _narracionHasta, _tituloDesde, _tituloHasta;
        readonly Queue<string> _cola = new Queue<string>();
        protected Font _fuente;
        protected RectTransform _raiz;
        GameObject _menu, _inicio, _qte;
        Text _textoCalidad, _textoVolumen, _textoCampana, _textoNivel, _textoInicioInfo, _textoContinuarPartida, _textoControlesMenu;
        GameObject _botonContinuarPartida;
        Text _qteTitulo, _qteTexto, _qteResultado, _marcadorTexto, _nivel, _dialogo, _jefeNombre;
        GameObject _jefe;
        Image _jefeVida, _jefeRetardo;
        float _jefeMostrada = 1f;
        float _dialogoDesde, _dialogoHasta;
        Text[] _qteTeclas;
        Image[] _qteCajas;
        Image _qteTiempo, _qteProgreso, _marcador;
        float _qteResultadoHasta;
        Vector3? _puntoMarcador;
        string _etiquetaMarcador;
        bool _avisoHelicopteros;

        /// <summary>Opciones del menu de pausa (las rellena el flujo de la partida).</summary>
        public System.Action alContinuar, alCambiarCalidad, alSalir, alReiniciar, alMenuPrincipal, alGuardar;
        public System.Action<float> alCambiarVolumen;
        /// <summary>Opciones del menu de inicio: campana (nivel elegido), cambiar de nivel y modo libre.</summary>
        public System.Action alCampana, alLibre, alContinuarPartida;
        public System.Action<int> alCambiarNivel;
        /// <summary>En la campana el panel de la derecha lleva la mision, no el recuento de zonas.</summary>
        public bool modoCampana;

        protected virtual void Awake()
        {
            _fuente = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Construir();
        }

        protected virtual void Start()
        {
            _criatura = Criatura.Buscar();
            if (FindAnyObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        protected virtual void OnEnable()
        {
            ZonaHumana.AlDescubrir += Descubierta;
            ZonaHumana.AlLiberar += Liberada;
            Criatura.AlPurificar += Purificada;
            Helicoptero.AlDespegar += Despega;
            Helicoptero.AlExplotar += Derribado;
        }

        protected virtual void OnDisable()
        {
            ZonaHumana.AlDescubrir -= Descubierta;
            ZonaHumana.AlLiberar -= Liberada;
            Criatura.AlPurificar -= Purificada;
            Helicoptero.AlDespegar -= Despega;
            Helicoptero.AlExplotar -= Derribado;
        }

        void Descubierta(ZonaHumana z) => Avisar($"Intrusos en la selva: {z.nombre}");
        void Liberada(ZonaHumana z) => Avisar($"{z.nombre} liberado. La selva respira de nuevo.");
        void Purificada(Vector3 p, float r) { if (!string.IsNullOrEmpty(textoPurificar)) Avisar(textoPurificar); }
        void Despega(Helicoptero h)
        {
            if (_avisoHelicopteros || string.IsNullOrEmpty(textoHelicopteros)) return;
            _avisoHelicopteros = true;
            Avisar(textoHelicopteros);
        }
        void Derribado(Helicoptero h) => Avisar("Helicoptero derribado");

        // ------------------------------------------------------------------ Montaje
        void Construir()
        {
            var lienzo = new GameObject("Lienzo", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)).GetComponent<Canvas>();
            lienzo.transform.SetParent(transform, false);
            lienzo.renderMode = RenderMode.ScreenSpaceOverlay;
            lienzo.sortingOrder = 10;
            var escalador = lienzo.GetComponent<CanvasScaler>();
            escalador.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            escalador.referenceResolution = new Vector2(1920f, 1080f);
            escalador.matchWidthOrHeight = 0.5f;
            var r = lienzo.transform;
            _raiz = (RectTransform)r;

            ConstruirEstado(r);

            _zonas = Etiqueta(r, "", new Vector2(-40f, -34f), 22, TextAnchor.UpperRight, new Vector2(1f, 1f));
            _zonas.verticalOverflow = VerticalWrapMode.Overflow;
            _objetivo = Etiqueta(r, "", new Vector2(-40f, -100f), 20, TextAnchor.UpperRight, new Vector2(1f, 1f));
            // Varias lineas (objetivo, progreso y reglas de la campana), con saltos si no caben.
            _objetivo.rectTransform.sizeDelta = new Vector2(760f, 200f);
            _objetivo.horizontalOverflow = HorizontalWrapMode.Wrap;
            _objetivo.verticalOverflow = VerticalWrapMode.Overflow;
            _aviso = Etiqueta(r, "", new Vector2(0f, 160f), 34, TextAnchor.MiddleCenter, new Vector2(0.5f, 0f));
            _ayuda = Etiqueta(r, textoAyuda, new Vector2(0f, 40f), 18, TextAnchor.MiddleCenter, new Vector2(0.5f, 0f));

            var mira = new GameObject("Mira", typeof(Image)).GetComponent<Image>();
            mira.transform.SetParent(r, false);
            mira.rectTransform.sizeDelta = new Vector2(10f, 10f);
            mira.color = new Color(1f, 1f, 1f, 0.8f);
            mira.enabled = false;
            _mira = mira;

            // Pantallas: negro para los fundidos, narracion y titulos por encima.
            _negro = new GameObject("Negro", typeof(Image)).GetComponent<Image>();
            _negro.transform.SetParent(r, false);
            Estirar(_negro.rectTransform);
            _negro.color = new Color(0f, 0f, 0f, 0f);
            _negro.raycastTarget = false;
            _narracion = Etiqueta(r, "", new Vector2(0f, -40f), 38, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f));
            _narracion.rectTransform.sizeDelta = new Vector2(1500f, 200f);
            _narracion.horizontalOverflow = HorizontalWrapMode.Wrap;
            _titulo = Etiqueta(r, "", new Vector2(0f, 60f), 72, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f));
            _subtitulo = Etiqueta(r, "", new Vector2(0f, -30f), 26, TextAnchor.UpperCenter, new Vector2(0.5f, 0.5f));
            _subtitulo.rectTransform.sizeDelta = new Vector2(1400f, 300f);
            _subtitulo.horizontalOverflow = HorizontalWrapMode.Wrap;
            _nivel = Etiqueta(r, "", new Vector2(-40f, -34f), 24, TextAnchor.UpperRight, new Vector2(1f, 1f));
            // Subtitulos de los personajes (no paran el juego), encima de la ayuda de controles.
            _dialogo = Etiqueta(r, "", new Vector2(0f, 96f), 28, TextAnchor.LowerCenter, new Vector2(0.5f, 0f));
            _dialogo.rectTransform.sizeDelta = new Vector2(1500f, 120f);
            _dialogo.horizontalOverflow = HorizontalWrapMode.Wrap;
            ConstruirMarcador(r);
            ConstruirJefe(r);
            ConstruirQte(r);
            ConstruirPantallas(r);
            ConstruirMenu(r);
            ConstruirInicio(r);
            ConstruirOpciones(r);
            PrepararSubtitulos();
            ConstruirExtras(r);
        }

        /// <summary>Lo que cada juego anade a los menus (sus botones y paneles), al final del montaje.</summary>
        protected virtual void ConstruirExtras(Transform r) { }

        /// <summary>La esquina de la criatura: nombre y vida. Cada juego anade sus barras.</summary>
        protected virtual void ConstruirEstado(Transform r)
        {
            Etiqueta(r, nombreCriatura, new Vector2(40f, -34f), 30, TextAnchor.UpperLeft, new Vector2(0f, 1f));
            Barra(r, new Vector2(40f, -78f), new Vector2(520f, 16f), new Color(0.12f, 0.02f, 0.02f, 0.75f), out _vidaRetardo, new Color(0.9f, 0.85f, 0.75f, 0.9f));
            _vida = Relleno(_vidaRetardo.transform.parent, new Color(0.72f, 0.08f, 0.06f, 1f));
        }

        /// <summary>Cada fotograma, con la criatura viva: la vida (la de antes baja despacio).</summary>
        protected virtual void ActualizarEstado()
        {
            if (_criatura == null || _vida == null) return;
            float v = _criatura.Salud.Fraccion;
            _vidaMostrada = Mathf.MoveTowards(_vidaMostrada, v, Time.deltaTime * 0.25f);
            if (_vidaMostrada < v) _vidaMostrada = v;
            Llenar(_vida, v);
            Llenar(_vidaRetardo, _vidaMostrada);
        }

        /// <summary>Se ve la mira (llevando algo para lanzar, apuntando la mirada...).</summary>
        protected virtual bool MiraVisible => false;

        void ConstruirMarcador(Transform r)
        {
            _marcador = new GameObject("Marcador", typeof(Image)).GetComponent<Image>();
            _marcador.transform.SetParent(r, false);
            _marcador.rectTransform.anchorMin = _marcador.rectTransform.anchorMax = Vector2.zero;
            _marcador.rectTransform.sizeDelta = new Vector2(22f, 22f);
            _marcador.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            _marcador.color = new Color(1f, 0.8f, 0.3f, 0.95f);
            _marcador.raycastTarget = false;
            _marcadorTexto = Etiqueta(r, "", Vector2.zero, 20, TextAnchor.UpperCenter, Vector2.zero);
            _marcadorTexto.rectTransform.sizeDelta = new Vector2(400f, 60f);
            _marcadorTexto.rectTransform.pivot = new Vector2(0.5f, 1f);
            _marcador.enabled = _marcadorTexto.enabled = false;
        }

        /// <summary>La barra de un jefe (arriba, en el centro): su nombre y su vida.</summary>
        void ConstruirJefe(Transform r)
        {
            _jefe = new GameObject("Jefe", typeof(RectTransform));
            _jefe.transform.SetParent(r, false);
            var rt = (RectTransform)_jefe.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -40f);
            rt.sizeDelta = new Vector2(700f, 60f);
            _jefeNombre = Etiqueta(_jefe.transform, "", new Vector2(0f, 0f), 24, TextAnchor.UpperCenter, new Vector2(0.5f, 1f));
            _jefeNombre.rectTransform.sizeDelta = new Vector2(700f, 32f);
            Barra(_jefe.transform, new Vector2(0f, -34f), new Vector2(700f, 12f), new Color(0.1f, 0.03f, 0.02f, 0.8f), out _jefeRetardo, new Color(0.95f, 0.85f, 0.7f, 0.9f));
            _jefeVida = Relleno(_jefeRetardo.transform.parent, new Color(0.85f, 0.35f, 0.1f, 1f));
            _jefe.SetActive(false);
        }

        /// <summary>
        /// Muestra la barra de un jefe con su vida (0..1); con nombre null la quita. La vida perdida
        /// baja despacio (como la de la criatura).
        /// </summary>
        public void Jefe(string nombre, float fraccion = 1f)
        {
            if (_jefe == null) return;
            bool ver = !string.IsNullOrEmpty(nombre);
            if (ver && !_jefe.activeSelf) _jefeMostrada = Mathf.Clamp01(fraccion);
            _jefe.SetActive(ver);
            if (!ver) return;
            _jefeNombre.text = Guardianes.Juego.Textos.T(nombre);
            fraccion = Mathf.Clamp01(fraccion);
            _jefeMostrada = fraccion > _jefeMostrada ? fraccion : Mathf.MoveTowards(_jefeMostrada, fraccion, Time.unscaledDeltaTime * 0.3f);
            Llenar(_jefeVida, fraccion);
            Llenar(_jefeRetardo, _jefeMostrada);
        }

        void ConstruirQte(Transform r)
        {
            _qte = new GameObject("Qte", typeof(Image));
            _qte.transform.SetParent(r, false);
            var fondo = _qte.GetComponent<Image>();
            fondo.color = new Color(0.03f, 0.02f, 0.01f, 0.72f);
            fondo.raycastTarget = false;
            var rt = fondo.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 230f);
            rt.sizeDelta = new Vector2(900f, 250f);
            var q = _qte.transform;
            _qteTitulo = Etiqueta(q, "", new Vector2(0f, -18f), 40, TextAnchor.UpperCenter, new Vector2(0.5f, 1f));
            _qteTitulo.rectTransform.sizeDelta = new Vector2(880f, 60f);
            _qteTexto = Etiqueta(q, "", new Vector2(0f, -70f), 22, TextAnchor.UpperCenter, new Vector2(0.5f, 1f));
            _qteTexto.rectTransform.sizeDelta = new Vector2(860f, 40f);
            _qteCajas = new Image[6];
            _qteTeclas = new Text[6];
            for (int i = 0; i < _qteCajas.Length; i++)
            {
                var caja = new GameObject("Tecla", typeof(Image)).GetComponent<Image>();
                caja.transform.SetParent(q, false);
                caja.raycastTarget = false;
                var c = caja.rectTransform;
                c.anchorMin = c.anchorMax = c.pivot = new Vector2(0.5f, 0f);
                c.sizeDelta = new Vector2(150f, 62f);
                _qteCajas[i] = caja;
                _qteTeclas[i] = Etiqueta(caja.transform, "", Vector2.zero, 22, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f));
                _qteTeclas[i].rectTransform.sizeDelta = c.sizeDelta;
            }
            Barra(q, new Vector2(30f, -222f), new Vector2(840f, 8f), new Color(0.1f, 0.1f, 0.1f, 0.8f), out _qteTiempo, new Color(1f, 0.85f, 0.4f, 1f));
            Barra(q, new Vector2(30f, -236f), new Vector2(840f, 6f), new Color(0.1f, 0.1f, 0.1f, 0.8f), out _qteProgreso, new Color(0.45f, 0.95f, 0.5f, 1f));
            _qteResultado = Etiqueta(r, "", new Vector2(0f, 380f), 56, TextAnchor.MiddleCenter, new Vector2(0.5f, 0f));
            _qte.SetActive(false);
        }

        void ConstruirInicio(Transform r)
        {
            _inicio = new GameObject("MenuInicio", typeof(Image));
            _inicio.transform.SetParent(r, false);
            var fondo = _inicio.GetComponent<Image>();
            fondo.color = new Color(0f, 0f, 0f, 0.55f);
            Estirar(fondo.rectTransform);
            var m = _inicio.transform;
            Etiqueta(m, tituloJuego, new Vector2(0f, 330f), 96, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f));
            Etiqueta(m, subtituloJuego, new Vector2(0f, 260f), 30, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f));
            _textoContinuarPartida = Boton(m, "Continuar partida", new Vector2(0f, 175f), () => alContinuarPartida?.Invoke(), 860f);
            _botonContinuarPartida = _textoContinuarPartida.transform.parent.gameObject;
            _textoCampana = Boton(m, "Campana", new Vector2(0f, 105f), () => alCampana?.Invoke(), 640f);
            _textoNivel = Boton(m, "Nivel", new Vector2(0f, 35f), () => alCambiarNivel?.Invoke(1), 640f);
            Boton(m, textoModoLibre, new Vector2(0f, -40f), () => alLibre?.Invoke(), 640f);
            Boton(m, "Opciones", new Vector2(0f, -115f), AbrirOpciones, 640f);
            Boton(m, "Salir", new Vector2(0f, -190f), () => alSalir?.Invoke(), 640f);
            // Bajo "Salir" (de -220 a -160): la etiqueta mide 200 y el texto empieza arriba.
            _textoInicioInfo = Etiqueta(m, "", new Vector2(0f, -340f), 22, TextAnchor.UpperCenter, new Vector2(0.5f, 0.5f));
            _textoInicioInfo.rectTransform.sizeDelta = new Vector2(1300f, 200f);
            _textoInicioInfo.horizontalOverflow = HorizontalWrapMode.Wrap;
            _inicio.SetActive(false);
        }

        void ConstruirMenu(Transform r)
        {
            _menu = new GameObject("MenuPausa", typeof(Image));
            _menu.transform.SetParent(r, false);
            var fondo = _menu.GetComponent<Image>();
            fondo.color = new Color(0f, 0f, 0f, 0.6f);
            Estirar(fondo.rectTransform);
            var m = _menu.transform;
            Etiqueta(m, "PAUSA", new Vector2(0f, 330f), 64, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f));
            Boton(m, "Continuar", new Vector2(0f, 240f), () => alContinuar?.Invoke());
            Boton(m, "Guardar partida", new Vector2(0f, 170f), () => alGuardar?.Invoke());
            // La calidad y el volumen, en Opciones.
            Boton(m, "Opciones", new Vector2(0f, 100f), AbrirOpciones);
            Boton(m, "Reiniciar nivel", new Vector2(0f, 30f), () => alReiniciar?.Invoke());
            Boton(m, "Menu principal", new Vector2(0f, -40f), () => alMenuPrincipal?.Invoke());
            Boton(m, "Salir", new Vector2(0f, -110f), () => alSalir?.Invoke());
            // Bajo "Salir" (de -140 a -80): la etiqueta mide 120 y el texto empieza arriba.
            _textoControlesMenu = Etiqueta(m, textoControles, new Vector2(0f, -215f), 20, TextAnchor.UpperCenter, new Vector2(0.5f, 0.5f));
            _textoControlesMenu.rectTransform.sizeDelta = new Vector2(1500f, 120f);
            _textoControlesMenu.horizontalOverflow = HorizontalWrapMode.Wrap;
            _menu.SetActive(false);
        }

        // ------------------------------------------------------------------ Piezas de uGUI
        protected Text Boton(Transform padre, string texto, Vector2 pos, UnityEngine.Events.UnityAction accion, float ancho = 420f)
        {
            var go = new GameObject("Boton_" + texto, typeof(Image), typeof(Button));
            go.transform.SetParent(padre, false);
            var img = go.GetComponent<Image>();
            img.color = new Color(0.12f, 0.1f, 0.07f, 0.85f);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(ancho, 60f);
            var b = go.GetComponent<Button>();
            var colores = b.colors;
            colores.highlightedColor = new Color(0.45f, 0.32f, 0.12f, 1f);
            colores.selectedColor = new Color(0.45f, 0.32f, 0.12f, 1f);
            b.colors = colores;
            b.onClick.AddListener(accion);
            var t = Etiqueta(go.transform, texto, Vector2.zero, 28, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f));
            t.rectTransform.sizeDelta = rt.sizeDelta;
            return t;
        }

        protected static void Estirar(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        protected Text Etiqueta(Transform padre, string texto, Vector2 pos, int tam, TextAnchor ancla, Vector2 pivote)
        {
            var t = new GameObject("Texto", typeof(Text), typeof(Shadow)).GetComponent<Text>();
            t.transform.SetParent(padre, false);
            t.font = _fuente;
            t.fontSize = tam;
            t.alignment = ancla;
            t.color = new Color(0.95f, 0.92f, 0.85f);
            // Traducido; los fijos se vuelven a traducir al cambiar de idioma (si nadie los ha cambiado).
            t.text = Guardianes.Juego.Textos.T(texto);
            if (!string.IsNullOrEmpty(texto)) _traducibles.Add((t, texto, t.text));
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = pivote;
            rt.pivot = pivote;
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(1400f, tam * 1.6f);
            t.GetComponent<Shadow>().effectDistance = new Vector2(2f, -2f);
            return t;
        }

        protected void Barra(Transform padre, Vector2 pos, Vector2 tam, Color fondo, out Image relleno, Color colorRelleno)
        {
            var f = new GameObject("Barra", typeof(Image)).GetComponent<Image>();
            f.transform.SetParent(padre, false);
            f.color = fondo;
            var rt = f.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = tam;
            relleno = Relleno(f.transform, colorRelleno);
        }

        protected static Image Relleno(Transform barra, Color color)
        {
            var i = new GameObject("Relleno", typeof(Image)).GetComponent<Image>();
            i.transform.SetParent(barra, false);
            i.color = color;
            var rt = i.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0f, 0.5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return i;
        }

        protected static void Llenar(Image i, float f)
        {
            var rt = i.rectTransform;
            rt.anchorMax = new Vector2(Mathf.Clamp01(f), 1f);
        }

        // ------------------------------------------------------------------ API de pantallas
        public void Avisar(string texto) => _cola.Enqueue(Guardianes.Juego.Textos.T(texto));

        /// <summary>Funde la pantalla a negro (alfa 1) o la aclara (alfa 0) en tiempo real.</summary>
        public void Fundido(float alfa, float segundos)
        {
            _negroInicio = _negro.color.a;
            _negroFin = alfa;
            _negroDesde = Time.unscaledTime;
            _negroHasta = Time.unscaledTime + Mathf.Max(0.001f, segundos);
        }

        /// <summary>Una linea de narracion en el centro (aparece y se desvanece).</summary>
        public void Narrar(string texto, float segundos)
        {
            _narracion.text = Guardianes.Juego.Textos.T(texto);
            _narracionDesde = Time.unscaledTime;
            _narracionHasta = Time.unscaledTime + segundos;
        }

        public void Titulo(string titulo, string subtitulo, float segundos)
        {
            _titulo.text = Guardianes.Juego.Textos.T(titulo);
            _subtitulo.text = Guardianes.Juego.Textos.T(subtitulo);
            _tituloDesde = Time.unscaledTime;
            _tituloHasta = Time.unscaledTime + segundos;
        }

        public void Objetivo(string texto) => _objetivo.text = texto;

        /// <summary>Una frase de un personaje abajo, como subtitulo (el juego sigue).</summary>
        public void Dialogo(string texto, float segundos)
        {
            _dialogo.text = Guardianes.Juego.Textos.T(texto);
            _dialogoDesde = Time.unscaledTime;
            _dialogoHasta = Time.unscaledTime + segundos;
        }

        /// <summary>Titulo del nivel arriba a la derecha (campana).</summary>
        public void Nivel(string texto) => _nivel.text = texto;

        /// <summary>Rombo sobre el punto del mundo al que hay que ir (null lo quita).</summary>
        public void Marcador(Vector3? punto, string etiqueta = "")
        {
            _puntoMarcador = punto;
            _etiquetaMarcador = etiqueta;
        }

        /// <summary>
        /// El panel del evento rapido: teclas (la que toca, resaltada), tiempo que queda y progreso. Las
        /// teclas llegan ya escritas ("ESPACIO · A"): cada juego tiene las suyas.
        /// </summary>
        public void Qte(string titulo, string texto, string[] teclas, int paso, float tiempo, float progreso, bool machacar)
        {
            _qte.SetActive(true);
            _qteTitulo.text = titulo;
            _qteTexto.text = machacar ? texto + "  (pulsa repetidamente)" : texto;
            int n = Mathf.Min(teclas.Length, _qteCajas.Length);
            float ancho = 160f;
            for (int i = 0; i < _qteCajas.Length; i++)
            {
                bool usa = i < n;
                _qteCajas[i].gameObject.SetActive(usa);
                if (!usa) continue;
                _qteCajas[i].rectTransform.anchoredPosition = new Vector2((i - (n - 1) * 0.5f) * ancho, 42f);
                _qteTeclas[i].text = teclas[i];
                bool hecha = i < paso, actual = i == paso;
                float pulso = actual ? 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 14f) : 1f;
                _qteCajas[i].color = hecha ? new Color(0.25f, 0.6f, 0.3f, 0.9f)
                                   : actual ? new Color(0.95f * pulso, 0.7f * pulso, 0.2f * pulso, 0.95f)
                                   : new Color(0.2f, 0.18f, 0.15f, 0.9f);
                _qteCajas[i].rectTransform.localScale = Vector3.one * (actual ? 1.08f : 1f);
            }
            Llenar(_qteTiempo, tiempo);
            Llenar(_qteProgreso, progreso);
        }

        public void FinQte(bool exito)
        {
            _qte.SetActive(false);
            _qteResultado.text = exito ? "BIEN" : "FALLASTE";
            _qteResultado.color = exito ? new Color(0.6f, 1f, 0.6f, 1f) : new Color(1f, 0.45f, 0.35f, 1f);
            _qteResultadoHasta = Time.unscaledTime + 1.4f;
        }

        /// <summary>Menu de inicio: el boton de campana dice desde que nivel empieza; info lo describe.</summary>
        /// <param name="guardada">Lo que dice el boton de continuar la partida guardada (null: no hay).</param>
        public void MenuInicio(bool visible, string campana = "", string nivel = "", string info = "", string guardada = null)
        {
            _inicio.SetActive(visible);
            if (!visible) { CerrarPaneles(); return; }
            _botonContinuarPartida.SetActive(guardada != null);
            if (guardada != null) _textoContinuarPartida.text = "Continuar: " + guardada;
            _textoCampana.text = campana;
            _textoNivel.text = nivel;
            _textoInicioInfo.text = info;
            var b = _inicio.GetComponentInChildren<Button>();
            if (b != null && EventSystem.current != null && EventSystem.current.currentSelectedGameObject == null)
                EventSystem.current.SetSelectedGameObject(b.gameObject);
        }

        public void MenuPausa(bool visible, string calidad, float volumen)
        {
            _menu.SetActive(visible);
            if (_textoCalidad != null) _textoCalidad.text = "Calidad: " + calidad;
            if (_textoVolumen != null) _textoVolumen.text = $"Volumen: {Mathf.RoundToInt(volumen * 100f)} %";
            if (!visible) CerrarPaneles();
            if (visible)
            {
                var b = _menu.GetComponentInChildren<Button>();
                if (b != null && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(b.gameObject);
            }
        }

        public virtual bool MenuAbierto => (_menu != null && _menu.activeSelf) || (_inicio != null && _inicio.activeSelf) || OpcionesAbiertas;

        /// <summary>Cierra las opciones y los controles (al cerrar el menu desde el que se abrieron).</summary>
        protected virtual void CerrarPaneles()
        {
            CancelarReasignar();
            if (_opciones != null) _opciones.SetActive(false);
            if (_controles != null) _controles.SetActive(false);
            _volverA = null;
        }

        static float Aparicion(float desde, float hasta, float entrada)
        {
            float t = Time.unscaledTime;
            if (t >= hasta) return 0f;
            return Mathf.Clamp01((t - desde) / entrada) * Mathf.Clamp01((hasta - t) / entrada);
        }

        void ActualizarMarcador()
        {
            var cam = Camera.main;
            bool ver = _puntoMarcador.HasValue && cam != null && _criatura != null && !_menu.activeSelf && !_inicio.activeSelf;
            _marcador.enabled = _marcadorTexto.enabled = ver;
            if (!ver) return;
            Vector3 p = _puntoMarcador.Value;
            Vector3 s = cam.WorldToScreenPoint(p + Vector3.up * Mathf.Max(2f, _criatura.Tamano * 1.1f));
            // Detras de la camara: al borde de abajo, del lado en que queda.
            if (s.z < 0f) { s.x = Screen.width - s.x; s.y = 0f; }
            float escala = _raiz.rect.width / Mathf.Max(Screen.width, 1);
            Vector2 enLienzo = new Vector2(s.x, s.y) * escala;
            Vector2 tam = _raiz.rect.size;
            enLienzo.x = Mathf.Clamp(enLienzo.x, 40f, tam.x - 40f);
            enLienzo.y = Mathf.Clamp(enLienzo.y, 90f, tam.y - 60f);
            _marcador.rectTransform.anchoredPosition = enLienzo;
            float d = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(_criatura.transform.position.x, _criatura.transform.position.z));
            _marcadorTexto.rectTransform.anchoredPosition = enLienzo - new Vector2(0f, 18f);
            _marcadorTexto.text = (string.IsNullOrEmpty(_etiquetaMarcador) ? "" : _etiquetaMarcador + "\n") + (d < 10f ? $"{d:F1} m" : $"{d:F0} m");
        }

        protected virtual void Update()
        {
            float fn = Mathf.InverseLerp(_negroDesde, _negroHasta, Time.unscaledTime);
            _negro.color = new Color(0f, 0f, 0f, Mathf.Lerp(_negroInicio, _negroFin, fn * fn * (3f - 2f * fn)));
            _narracion.color = new Color(0.97f, 0.94f, 0.86f, Aparicion(_narracionDesde, _narracionHasta, 0.9f));
            float at = Aparicion(_tituloDesde, _tituloHasta, 1.2f);
            _titulo.color = new Color(1f, 0.9f, 0.7f, at);
            _dialogo.color = new Color(1f, 0.95f, 0.82f, Aparicion(_dialogoDesde, _dialogoHasta, 0.4f));
            _subtitulo.color = new Color(0.95f, 0.92f, 0.85f, at);

            ActualizarPantallas();
            ActualizarSubtitulos();
            float ar = Mathf.Clamp01((_qteResultadoHasta - Time.unscaledTime) / 0.5f);
            _qteResultado.color = new Color(_qteResultado.color.r, _qteResultado.color.g, _qteResultado.color.b, ar);
            if (_criatura == null) _criatura = Criatura.Jugador;
            ActualizarMarcador();

            if (_criatura == null) return;
            ActualizarEstado();

            int liberadas = 0;
            foreach (var z in ZonaHumana.Todas) if (z.Liberada) liberadas++;
            string zonas = ZonaHumana.Todas.Count > 0 ? Guardianes.Juego.Textos.F("Zonas liberadas {0}/{1}", liberadas, ZonaHumana.Todas.Count) : "";
            int lugares = Descubrimiento.Todos.Count;
            if (lugares > 0) zonas += "\n" + Guardianes.Juego.Textos.F("Lugares descubiertos {0}/{1}", Descubrimiento.Descubiertos(), lugares);
            _zonas.text = modoCampana ? "" : zonas;

            if (Time.time > _avisoHasta && _cola.Count > 0)
            {
                _aviso.text = _cola.Dequeue();
                _avisoHasta = Time.time + 4.5f;
            }
            float a = Mathf.Clamp01((_avisoHasta - Time.time) / 0.8f);
            _aviso.color = new Color(1f, 0.93f, 0.8f, a);
            _ayuda.color = new Color(1f, 1f, 1f, Mathf.Clamp01(1f - (Time.timeSinceLevelLoad - 40f) / 5f));
            _mira.enabled = MiraVisible;
        }
    }
}
