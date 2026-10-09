using Guardianes.Criaturas;
using Guardianes.Mundo;
using UnityEngine;

namespace Guardianes.Camara
{
    /// <summary>
    /// Camara en tercera persona de la criatura: orbita con raton o stick, distancia con la rueda,
    /// nunca atraviesa el suelo ni el agua (se acerca si una loma tapa a la criatura), muelle critico
    /// para seguirla, algo mas de angulo al correr y temblores (pisadas, golpes, explosiones).
    /// Las medidas (altura del foco, distancia, limites, plano cercano, linea de vision) estan
    /// pensadas para una criatura de 'tamanoReferencia' metros y crecen con Criatura.Tamano: la misma
    /// camara sigue a una serpiente de 40 cm y a un basilisco de 120 m.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class CamaraCriatura : MonoBehaviour
    {
        public Transform objetivo;
        [Tooltip("Tamano (m) de la criatura para el que valen las medidas de abajo")]
        public float tamanoReferencia = Criatura.TamanoReferencia;
        public float alturaObjetivo = 11f;
        public float distancia = 44f;
        public Vector2 limitesDistancia = new Vector2(18f, 110f);
        public Vector2 limitesInclinacion = new Vector2(-12f, 70f);
        public float sensibilidadRaton = 0.12f;   // grados por pixel
        public float sensibilidadMando = 140f;    // grados por segundo
        public float suavizado = 6f;
        public float fov = 55f;
        public float fovCorriendo = 63f;
        [Tooltip("Radio del cilindro de vision donde se disuelve el follaje (m)")]
        public float radioVision = 9f;
        [Tooltip("La camara nunca baja de esto sobre el suelo o el agua (m)")]
        public float alturaMinima = 2.5f;
        [Tooltip("Plano cercano (m)")]
        public float planoCercano = 0.3f;
        [Tooltip("Inclinacion inicial (grados)")]
        public float inclinacion = 22f;

        static readonly int IdVision = Shader.PropertyToID("_GD_Vision");
        static readonly int IdVisionFin = Shader.PropertyToID("_GD_VisionFin");

        float _yaw, _pitch;
        Vector3 _foco, _velFoco;
        float _distanciaActual;
        float _zoom = 1f;
        float _temblor;
        Camera _cam;
        IEntradaCamara _entrada;
        Criatura _criatura;

        /// <summary>Escala de la camara: tamano de la criatura / tamano de referencia.</summary>
        public float Escala => _criatura != null && tamanoReferencia > 0f ? Mathf.Max(0.005f, _criatura.Tamano / tamanoReferencia) : 1f;
        float Distancia => Mathf.Clamp(distancia * _zoom, limitesDistancia.x, limitesDistancia.y) * Escala;

        void Awake()
        {
            _cam = GetComponent<Camera>();
            _pitch = inclinacion;
            Enlazar();
            if (objetivo != null)
            {
                _yaw = objetivo.eulerAngles.y;
                _foco = Foco();
            }
            _distanciaActual = Distancia;
        }

        void Enlazar()
        {
            if (objetivo == null) return;
            _entrada = objetivo.GetComponent<IEntradaCamara>();
            _criatura = objetivo.GetComponent<Criatura>();
        }

        void OnDisable() => Shader.SetGlobalVector(IdVisionFin, Vector4.zero);

        void Start()
        {
            if (Application.isPlaying)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        /// <summary>Temblor de camara (0..1+), se suma y se apaga solo.</summary>
        public void Sacudir(float fuerza) => _temblor = Mathf.Min(_temblor + fuerza, 2.5f);

        /// <summary>Cambia de criatura (o la misma ha cambiado de cuerpo) y recoloca la camara.</summary>
        public void Seguir(Transform nuevo)
        {
            objetivo = nuevo;
            Enlazar();
            Recolocar();
        }

        /// <summary>Coloca la camara detras de la criatura sin transicion (teletransportes, cinematicas).</summary>
        public void Recolocar()
        {
            if (objetivo == null) return;
            _yaw = objetivo.eulerAngles.y;
            _foco = Foco();
            _velFoco = Vector3.zero;
            _distanciaActual = Distancia;
        }

        Vector3 Foco() => objetivo.position + Vector3.up * alturaObjetivo * Escala;

        void LateUpdate()
        {
            if (objetivo == null) return;
            if (_criatura == null && _entrada == null) Enlazar();
            float dt = Time.deltaTime;
            float k = Escala;
            // En pausa (escala de tiempo 0) el raton mueve el menu, no la camara.
            if (_entrada != null && Time.timeScale > 0f)
            {
                Vector2 m = _entrada.Mirar(sensibilidadRaton, sensibilidadMando);
                _yaw += m.x;
                _pitch = Mathf.Clamp(_pitch - m.y, limitesInclinacion.x, limitesInclinacion.y);
                float z = _entrada.Zoom;
                if (Mathf.Abs(z) > 0.01f)
                {
                    float min = limitesDistancia.x / Mathf.Max(distancia, 0.001f), max = limitesDistancia.y / Mathf.Max(distancia, 0.001f);
                    _zoom = Mathf.Clamp(_zoom * (1f - Mathf.Sign(z) * 0.08f), min, max);
                }
            }

            // Foco con muelle critico (la criatura pesa: la camara la sigue con algo de retraso).
            Vector3 focoDeseado = Foco();
            _foco = Vector3.SmoothDamp(_foco, focoDeseado, ref _velFoco, 1f / suavizado);

            var rot = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 dir = rot * Vector3.back;
            float dist = Distancia;
            float sobreSuelo = alturaMinima * k;
            float d = dist;
            // Si el terreno tapa a la criatura, la camara se acerca.
            for (int i = 1; i <= 12; i++)
            {
                float t = i / 12f;
                Vector3 p = _foco + dir * (dist * t);
                if (p.y < Suelo(p) + sobreSuelo) { d = Mathf.Max(limitesDistancia.x * k * 0.6f, dist * (t - 1f / 12f)); break; }
            }
            _distanciaActual = Mathf.Lerp(_distanciaActual, d, 1f - Mathf.Exp(-dt * (d < _distanciaActual ? 12f : 3f)));
            Vector3 pos = _foco + dir * _distanciaActual;
            pos.y = Mathf.Max(pos.y, Suelo(pos) + sobreSuelo);

            if (_temblor > 0.001f)
            {
                float t = Time.time * 32f;
                pos += new Vector3(Mathf.PerlinNoise(t, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, t) - 0.5f, Mathf.PerlinNoise(t, t) - 0.5f) * _temblor * 1.6f * k;
                _temblor = Mathf.MoveTowards(_temblor, 0f, dt * 2.2f);
            }
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(_foco - pos, Vector3.up));
            // Linea de vision para el shader de la vegetacion (disuelve el follaje que tapa a la criatura).
            Shader.SetGlobalVector(IdVision, new Vector4(pos.x, pos.y, pos.z, radioVision * k));
            // El final del cilindro, algo por encima del foco: 2 m sobre Kong (en las medidas de la
            // referencia de la saga, para que valga igual con otra referencia, como la serpiente de 1 m).
            Vector3 cabeza = objetivo.position + Vector3.up * (alturaObjetivo + 2f * tamanoReferencia / Criatura.TamanoReferencia) * k;
            Shader.SetGlobalVector(IdVisionFin, new Vector4(cabeza.x, cabeza.y, cabeza.z, 1f));

            if (_cam != null)
            {
                bool corre = _criatura != null && _criatura.Corriendo;
                _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, corre ? fovCorriendo : fov, 1f - Mathf.Exp(-dt * 2f));
                _cam.nearClipPlane = Mathf.Clamp(planoCercano * k, 0.01f, 1f);
            }
        }

        static float Suelo(Vector3 p) => TerrenoMundo.Instancia != null ? Mapa.Superficie(p.x, p.z) : 0f;
    }
}
