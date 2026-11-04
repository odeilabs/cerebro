using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Guardianes.Mundo;

namespace Guardianes.Criaturas
{
    /// <summary>
    /// Un animal de AssetsCreator (`creador.py animal`: esqueleto de 57 huesos, Generic, raiz 'root') movido
    /// por sus clips sin Animator Controller (Playables, como el huevo). Mezcla de dos en dos con fundido:
    /// - el BUCLE de fondo: si `automatico`, la marcha que va con la velocidad a la que se mueve el objeto
    ///   (idle, walk, trot, gallop...), acelerada o frenada para que las patas no resbalen (cada marcha
    ///   trae la velocidad a la que pisa: la mide el constructor en el clip); o el que se pida con Bucle();
    /// - una ACCION encima (salto, atacar, hit...): una vez y vuelve al bucle; Final() la deja en su ultimo
    ///   fotograma (la muerte).
    /// La IA que mueve el objeto solo dice que hace; los pasos los pone esto.
    /// </summary>
    public class AnimalAnimado : MonoBehaviour
    {
        [Serializable]
        public struct Clip
        {
            public string nombre;
            public AnimationClip clip;
            public bool bucle;
            [Tooltip("Velocidad a la que pisa (m/s) en las marchas; 0 en las demas")]
            public float velocidad;
        }

        public Animator animador;
        public Clip[] clips = Array.Empty<Clip>();
        [Tooltip("Las marchas por orden de velocidad (nombres de clips)")]
        public string[] marchas = { "walk", "trot", "gallop_rotatorio" };
        public string reposo = "idle";
        [Tooltip("Las marchas en el agua (nadar...): si el agua cubre mas de hondoNadar, se elige entre estas")]
        public string[] marchasAgua = Array.Empty<string>();
        public float hondoNadar = 0.3f;
        [Tooltip("Elige la marcha por la velocidad del objeto")]
        public bool automatico = true;
        [Tooltip("Por debajo de esto, quieto (m/s)")]
        public float umbralQuieto = 0.12f;
        public float fundido = 0.2f;
        [Tooltip("Lo que se puede acelerar una marcha antes de pasar a la siguiente")]
        public float aceleracionMaxima = 2.5f;
        [Tooltip("Escala del modelo frente al de AssetsCreator (las velocidades de las marchas escalan con ella)")]
        public float escala = 1f;

        PlayableGraph _grafo;
        AnimationMixerPlayable _mezcla;
        readonly Dictionary<string, int> _indice = new Dictionary<string, int>();
        float[] _peso = Array.Empty<float>();
        int _bucle = -1, _accion = -1;
        bool _final;
        string _bucleForzado;
        int[] _marchasOrdenadas = Array.Empty<int>(), _marchasAguaOrdenadas = Array.Empty<int>();
        Vector3 _ultima;
        float _velocidad;

        public string Actual => _accion >= 0 ? clips[_accion].nombre : _bucle >= 0 ? clips[_bucle].nombre : "";
        public bool EnAccion => _accion >= 0 && !_final;
        /// <summary>La velocidad horizontal medida (m/s, suavizada).</summary>
        public float Velocidad => _velocidad;

        void OnEnable()
        {
            if (animador == null) animador = GetComponentInChildren<Animator>();
            if (animador == null || clips.Length == 0) { enabled = false; return; }
            animador.applyRootMotion = false;
            _grafo = PlayableGraph.Create("AnimalAnimado " + name);
            _grafo.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var salida = AnimationPlayableOutput.Create(_grafo, "animal", animador);
            _mezcla = AnimationMixerPlayable.Create(_grafo, clips.Length);
            _indice.Clear();
            _peso = new float[clips.Length];
            for (int i = 0; i < clips.Length; i++)
            {
                _indice[clips[i].nombre] = i;
                var p = AnimationClipPlayable.Create(_grafo, clips[i].clip);
                p.SetApplyFootIK(false);
                _grafo.Connect(p, 0, _mezcla, i);
                _mezcla.SetInputWeight(i, 0f);
            }
            salida.SetSourcePlayable(_mezcla);
            var ms = new List<int>();
            foreach (var m in marchas) { int i = Indice(m); if (i >= 0 && clips[i].velocidad > 0f) ms.Add(i); }
            ms.Sort((x, y) => clips[x].velocidad.CompareTo(clips[y].velocidad));
            _marchasOrdenadas = ms.ToArray();
            var ma = new List<int>();
            foreach (var m in marchasAgua) { int i = Indice(m); if (i >= 0 && clips[i].velocidad > 0f) ma.Add(i); }
            ma.Sort((x, y) => clips[x].velocidad.CompareTo(clips[y].velocidad));
            _marchasAguaOrdenadas = ma.ToArray();
            _ultima = transform.position;
            _bucle = Indice(reposo);
            if (_bucle < 0) _bucle = 0;
            _peso[_bucle] = 1f;
            _mezcla.SetInputWeight(_bucle, 1f);
            // Cada uno en su momento del ciclo (no todos los animales a la vez).
            _mezcla.GetInput(_bucle).SetTime(UnityEngine.Random.value * clips[_bucle].clip.length);
            _grafo.Play();
        }

        void OnDisable()
        {
            if (_grafo.IsValid()) _grafo.Destroy();
        }

        int Indice(string nombre) => nombre != null && _indice.TryGetValue(nombre, out int i) ? i : -1;
        public bool Tiene(string nombre) => Indice(nombre) >= 0;

        /// <summary>El bucle de fondo a la fuerza (acecho, oler...); null vuelve a las marchas.</summary>
        public void Bucle(string nombre) => _bucleForzado = Tiene(nombre) ? nombre : null;

        /// <summary>Una accion una vez, encima del bucle. velocidad: multiplica la del clip.</summary>
        public bool Accion(string nombre, float velocidad = 1f)
        {
            int i = Indice(nombre);
            if (i < 0 || _final) return false;
            _accion = i;
            var p = _mezcla.GetInput(i);
            p.SetTime(0);
            p.SetSpeed(velocidad);
            return true;
        }

        /// <summary>Una accion que se queda en su ultimo fotograma (la muerte). Para siempre.</summary>
        public void Final(string nombre)
        {
            if (_final) return;
            if (Accion(nombre)) _final = true;
        }

        /// <summary>Lo que lleva la accion en curso (0..1), o 1 si no hay.</summary>
        public float ProgresoAccion => _accion >= 0 ? Mathf.Clamp01((float)(_mezcla.GetInput(_accion).GetTime() / clips[_accion].clip.length)) : 1f;

        void Update()
        {
            if (!_grafo.IsValid()) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            var d = transform.position - _ultima;
            d.y = 0f;
            _ultima = transform.position;
            _velocidad = Mathf.Lerp(_velocidad, d.magnitude / dt, 1f - Mathf.Exp(-dt * 8f));

            // El bucle: el pedido, o la marcha de la velocidad.
            int quiere = Indice(_bucleForzado);
            float ritmo = 1f;
            // un bucle pedido que tambien anda (el acecho): a su paso, si se mueve; si va mas deprisa de lo
            // que da de si, las marchas
            if (quiere >= 0 && clips[quiere].velocidad > 0f && _velocidad > umbralQuieto)
            {
                float vb = clips[quiere].velocidad * escala;
                if (_velocidad > vb * aceleracionMaxima * 1.3f) quiere = -1;
                else ritmo = Mathf.Clamp(_velocidad / vb, 0.4f, aceleracionMaxima * 1.3f);
            }
            if (quiere < 0 && automatico)
            {
                quiere = Indice(reposo);
                if (_velocidad > umbralQuieto)
                {
                    // la marcha mas lenta que llega acelerada como mucho `aceleracionMaxima` (los ciclos de
                    // AssetsCreator son los de un animal grande: en uno pequeño pisan corto y van mas deprisa)
                    int ultima = -1;
                    quiere = -1;
                    var lista = _marchasAguaOrdenadas.Length > 0 && Mapa.Profundidad(transform.position.x, transform.position.z) > hondoNadar
                        ? _marchasAguaOrdenadas : _marchasOrdenadas;
                    foreach (int i in lista)
                    {
                        ultima = i;
                        if (_velocidad <= clips[i].velocidad * escala * aceleracionMaxima) { quiere = i; break; }
                    }
                    if (quiere < 0) quiere = ultima;
                    if (quiere >= 0)
                        ritmo = Mathf.Clamp(_velocidad / (clips[quiere].velocidad * escala), 0.5f, aceleracionMaxima * 1.3f);
                    else quiere = Indice(reposo);
                }
            }
            if (quiere < 0) quiere = _bucle;
            if (quiere != _bucle)
            {
                // la nueva marcha entra en la misma fase del paso que la que deja
                var viejo = _mezcla.GetInput(_bucle);
                double fase = viejo.GetTime() / Math.Max(1e-3, clips[_bucle].clip.length);
                _mezcla.GetInput(quiere).SetTime((fase - Math.Floor(fase)) * clips[quiere].clip.length);
                _bucle = quiere;
            }
            _mezcla.GetInput(_bucle).SetSpeed(ritmo);

            // La accion: al acabar, vuelve al bucle (salvo la final).
            if (_accion >= 0 && !_final)
            {
                var p = _mezcla.GetInput(_accion);
                if (p.GetTime() >= clips[_accion].clip.length) _accion = -1;
            }
            if (_final)
            {
                var p = _mezcla.GetInput(_accion);
                if (p.GetTime() >= clips[_accion].clip.length) { p.SetTime(clips[_accion].clip.length); p.SetSpeed(0); }
            }

            // Los pesos, hacia el que manda (la accion si la hay).
            int manda = _accion >= 0 ? _accion : _bucle;
            float paso = fundido > 0f ? dt / fundido : 1f;
            float suma = 0f;
            for (int i = 0; i < _peso.Length; i++)
            {
                _peso[i] = Mathf.MoveTowards(_peso[i], i == manda ? 1f : 0f, paso);
                suma += _peso[i];
            }
            for (int i = 0; i < _peso.Length; i++)
            {
                float w = suma > 0f ? _peso[i] / suma : (i == manda ? 1f : 0f);
                _mezcla.SetInputWeight(i, w);
                // los bucles que no suenan, a la espera (no avanzan); los ciclicos se dan la vuelta solos
                if (w <= 0f && i != _bucle && i != _accion) _mezcla.GetInput(i).SetSpeed(0);
                else if (i != _bucle && i != _accion) _mezcla.GetInput(i).SetSpeed(1);
                if (clips[i].bucle && _mezcla.GetInput(i).GetTime() >= clips[i].clip.length)
                    _mezcla.GetInput(i).SetTime(_mezcla.GetInput(i).GetTime() % clips[i].clip.length);
            }
        }
    }
}
