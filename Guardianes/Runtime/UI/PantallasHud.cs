using UnityEngine;
using UnityEngine.UI;

namespace Guardianes.UI
{
    /// <summary>
    /// Las pantallas grandes del HUD, a pantalla completa y con su fondo pintado por codigo:
    ///   Caida (la criatura ha caido / mision fallida): negro con una franja roja de sangre que late.
    ///   Mision (mision completada / fin): verde selva oscuro que se funde en gris casi negro, con el
    ///   balance de la mision en filas y la valoracion del equilibrio.
    /// Aparecen y se van con un fundido (tiempo real: funcionan a camara lenta y en pausa).
    /// </summary>
    public partial class HudJuego
    {
        GameObject _caida, _mision;
        CanvasGroup _grupoCaida, _grupoMision;
        Image _latidoCaida;
        Text _caidaTitulo, _caidaTexto, _caidaPie;
        Text _misionTitulo, _misionSubtitulo, _misionValoracion, _misionPie;
        Text[] _misionEtiquetas, _misionValores;
        float _caidaObjetivo, _misionObjetivo;

        static readonly Color RojoSangre = new Color(0.55f, 0.03f, 0.02f);
        static readonly Color RojoOscuro = new Color(0.22f, 0.01f, 0.01f);
        static readonly Color VerdeSelva = new Color(0.09f, 0.33f, 0.15f);
        static readonly Color VerdeHoja = new Color(0.45f, 0.78f, 0.38f);
        static readonly Color GrisNegro = new Color(0.07f, 0.075f, 0.075f);

        // ------------------------------------------------------------------ Fondos
        /// <summary>Degradado vertical (abajo, centro, arriba) con viñeta: una textura pequena estirada.</summary>
        static Sprite Fondo(Color abajo, Color centro, Color arriba, float vineta, float centroAlto = 0.5f)
        {
            const int An = 64, Al = 128;
            var tex = new Texture2D(An, Al, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[An * Al];
            for (int y = 0; y < Al; y++)
            {
                float v = y / (Al - 1f);
                Color c = v < centroAlto ? Color.Lerp(abajo, centro, Mathf.SmoothStep(0f, 1f, v / centroAlto))
                                         : Color.Lerp(centro, arriba, Mathf.SmoothStep(0f, 1f, (v - centroAlto) / (1f - centroAlto)));
                for (int x = 0; x < An; x++)
                {
                    float u = x / (An - 1f) - 0.5f, w = v - 0.5f;
                    float d = Mathf.Sqrt(u * u * 1.4f + w * w);
                    float k = 1f - vineta * Mathf.SmoothStep(0.25f, 0.75f, d);
                    px[y * An + x] = new Color(c.r * k, c.g * k, c.b * k, c.a);
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, An, Al), new Vector2(0.5f, 0.5f));
        }

        Image Capa(Transform padre, string nombre, Sprite sprite, Color color)
        {
            var i = new GameObject(nombre, typeof(Image)).GetComponent<Image>();
            i.transform.SetParent(padre, false);
            i.sprite = sprite;
            i.color = color;
            i.raycastTarget = false;
            Estirar(i.rectTransform);
            return i;
        }

        Image Linea(Transform padre, Vector2 pos, float ancho, Color color)
        {
            var i = new GameObject("Linea", typeof(Image)).GetComponent<Image>();
            i.transform.SetParent(padre, false);
            i.color = color;
            i.raycastTarget = false;
            var rt = i.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(ancho, 3f);
            return i;
        }

        Text Grande(Transform padre, Vector2 pos, int tam, Color color, float ancho = 1600f)
        {
            var t = Etiqueta(padre, "", pos, tam, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f));
            t.rectTransform.sizeDelta = new Vector2(ancho, tam * 2.4f);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.color = color;
            t.fontStyle = FontStyle.Bold;
            var sombra = t.GetComponent<Shadow>();
            sombra.effectDistance = new Vector2(3f, -3f);
            sombra.effectColor = new Color(0f, 0f, 0f, 0.85f);
            return t;
        }

        void ConstruirPantallas(Transform r)
        {
            // Caida: negro arriba y abajo, rojo sangre en el centro; encima, un rojo que late.
            _caida = new GameObject("PantallaCaida", typeof(RectTransform), typeof(CanvasGroup));
            _caida.transform.SetParent(r, false);
            Estirar((RectTransform)_caida.transform);
            _grupoCaida = _caida.GetComponent<CanvasGroup>();
            _grupoCaida.blocksRaycasts = false;
            var c = _caida.transform;
            Capa(c, "Fondo", Fondo(Color.black, RojoOscuro, Color.black, 0.85f, 0.55f), Color.white);
            _latidoCaida = Capa(c, "Latido", Fondo(new Color(0f, 0f, 0f, 0f), RojoSangre, new Color(0f, 0f, 0f, 0f), 0.6f, 0.55f), new Color(1f, 1f, 1f, 0.5f));
            Linea(c, new Vector2(0f, 150f), 900f, new Color(0.85f, 0.1f, 0.06f, 0.9f));
            _caidaTitulo = Grande(c, new Vector2(0f, 70f), 96, new Color(1f, 0.86f, 0.82f));
            Linea(c, new Vector2(0f, -10f), 900f, new Color(0.85f, 0.1f, 0.06f, 0.9f));
            _caidaTexto = Grande(c, new Vector2(0f, -90f), 30, new Color(0.95f, 0.75f, 0.7f), 1300f);
            _caidaTexto.fontStyle = FontStyle.Normal;
            _caidaPie = Grande(c, new Vector2(0f, -300f), 24, new Color(0.9f, 0.55f, 0.5f));
            _caidaPie.fontStyle = FontStyle.Normal;
            _caida.SetActive(false);

            // Mision: verde selva abajo-centro que se va a gris casi negro arriba, panel gris con el balance.
            _mision = new GameObject("PantallaMision", typeof(RectTransform), typeof(CanvasGroup));
            _mision.transform.SetParent(r, false);
            Estirar((RectTransform)_mision.transform);
            _grupoMision = _mision.GetComponent<CanvasGroup>();
            _grupoMision.blocksRaycasts = false;
            var m = _mision.transform;
            Capa(m, "Fondo", Fondo(GrisNegro, VerdeSelva, new Color(0.045f, 0.05f, 0.05f), 0.7f, 0.42f), Color.white);
            _misionTitulo = Grande(m, new Vector2(0f, 330f), 84, new Color(0.88f, 1f, 0.85f));
            _misionSubtitulo = Grande(m, new Vector2(0f, 255f), 28, new Color(0.75f, 0.9f, 0.72f));
            _misionSubtitulo.fontStyle = FontStyle.Normal;
            Linea(m, new Vector2(0f, 215f), 1000f, VerdeHoja);
            var panel = new GameObject("Balance", typeof(Image)).GetComponent<Image>();
            panel.transform.SetParent(m, false);
            panel.color = new Color(GrisNegro.r, GrisNegro.g, GrisNegro.b, 0.72f);
            panel.raycastTarget = false;
            var prt = panel.rectTransform;
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = new Vector2(0f, 20f);
            prt.sizeDelta = new Vector2(900f, 330f);
            _misionEtiquetas = new Text[7];
            _misionValores = new Text[7];
            for (int i = 0; i < _misionEtiquetas.Length; i++)
            {
                float y = 132f - i * 44f;
                _misionEtiquetas[i] = Etiqueta(panel.transform, "", new Vector2(-400f, y), 26, TextAnchor.MiddleLeft, new Vector2(0.5f, 0.5f));
                _misionEtiquetas[i].rectTransform.sizeDelta = new Vector2(600f, 40f);
                _misionEtiquetas[i].rectTransform.pivot = new Vector2(0f, 0.5f);
                _misionEtiquetas[i].color = new Color(0.78f, 0.84f, 0.78f);
                _misionValores[i] = Etiqueta(panel.transform, "", new Vector2(400f, y), 28, TextAnchor.MiddleRight, new Vector2(0.5f, 0.5f));
                _misionValores[i].rectTransform.sizeDelta = new Vector2(300f, 40f);
                _misionValores[i].rectTransform.pivot = new Vector2(1f, 0.5f);
                _misionValores[i].color = new Color(0.9f, 1f, 0.88f);
                _misionValores[i].fontStyle = FontStyle.Bold;
            }
            Linea(m, new Vector2(0f, -175f), 1000f, VerdeHoja);
            _misionValoracion = Grande(m, new Vector2(0f, -235f), 44, VerdeHoja);
            _misionPie = Grande(m, new Vector2(0f, -330f), 24, new Color(0.7f, 0.8f, 0.7f));
            _misionPie.fontStyle = FontStyle.Normal;
            _mision.SetActive(false);
        }

        // ------------------------------------------------------------------ API
        /// <summary>Kong ha caido o la mision ha fallado: rojo y negro.</summary>
        public void PantallaCaida(bool visible, string titulo = "", string texto = "", string pie = "")
        {
            _caidaObjetivo = visible ? 1f : 0f;
            if (!visible) return;
            _caida.SetActive(true);
            _caida.transform.SetAsLastSibling();
            _caidaTitulo.text = titulo;
            _caidaTexto.text = texto;
            _caidaPie.text = pie;
        }

        /// <summary>Mision completada: titulo, la mision, el balance en filas, la valoracion y que viene despues.</summary>
        public void PantallaMision(bool visible, string titulo = "", string subtitulo = "", (string, string)[] filas = null, string valoracion = "", string pie = "")
        {
            _misionObjetivo = visible ? 1f : 0f;
            if (!visible) return;
            _mision.SetActive(true);
            _mision.transform.SetAsLastSibling();
            _misionTitulo.text = Guardianes.Juego.Textos.T(titulo);
            _misionSubtitulo.text = Guardianes.Juego.Textos.T(subtitulo);
            for (int i = 0; i < _misionEtiquetas.Length; i++)
            {
                bool hay = filas != null && i < filas.Length;
                _misionEtiquetas[i].text = hay ? Guardianes.Juego.Textos.T(filas[i].Item1) : "";
                _misionValores[i].text = hay ? filas[i].Item2 : "";
            }
            _misionValoracion.text = Guardianes.Juego.Textos.T(valoracion);
            _misionPie.text = Guardianes.Juego.Textos.T(pie);
        }

        void ActualizarPantallas()
        {
            float dt = Time.unscaledDeltaTime;
            _grupoCaida.alpha = Mathf.MoveTowards(_grupoCaida.alpha, _caidaObjetivo, dt * 1.2f);
            if (_grupoCaida.alpha <= 0f && _caidaObjetivo <= 0f && _caida.activeSelf) _caida.SetActive(false);
            if (_caida.activeSelf)
                _latidoCaida.color = new Color(1f, 1f, 1f, 0.35f + 0.3f * Mathf.Pow(0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3.4f), 3f));
            _grupoMision.alpha = Mathf.MoveTowards(_grupoMision.alpha, _misionObjetivo, dt * 0.9f);
            if (_grupoMision.alpha <= 0f && _misionObjetivo <= 0f && _mision.activeSelf) _mision.SetActive(false);
        }
    }
}
