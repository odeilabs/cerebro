using UnityEngine;

namespace Guardianes.Enemigos
{
    /// <summary>
    /// Las manos de un humano en un arma que no lleva en la mano: las dos cachas de una ametralladora montada (la M2 en la
    /// torreta de un blindado), que los clips de AssetsCreator (los del fusil) no cogen. Despues del Animator (LateUpdate),
    /// cada brazo se dobla (dos huesos: upperarm y lowerarm del esqueleto de AssetsCreator, el codo en el plano en el que lo
    /// tenia el clip) para que el puno —el centro de las falanges de los cuatro dedos, donde cierra la mano— quede en su
    /// cacha, y la mano se gira para cogerla como una empunadura de pie: los nudillos hacia el canon y del menique al indice
    /// a lo largo de la cacha (hacia arriba del arma). Lo que no alcanza se estira hacia ella. Muerto, no hace nada (cae con
    /// su clip). 'derecha' e 'izquierda' son las cachas (null: esa mano, con su clip); los ejes, los del arma ('arma': el
    /// canon a su -X y arriba a su +Y, como los modelos de AssetsCreator y del kit de armas ya importados). Lo pone cada
    /// juego (Poner) con lo que diga la ficha del arma (MontajeArma, el agarre y el guardamanos).
    /// </summary>
    [DisallowMultipleComponent]
    public class ManosEnArma : MonoBehaviour
    {
        public Transform arma, derecha, izquierda;

        /// <summary>Las falanges cuyo centro es el puno (las mismas con las que se mide en las pruebas).</summary>
        static readonly string[] Falanges = { "index_01_", "index_02_", "middle_01_", "middle_02_", "ring_01_", "ring_02_", "pinky_01_", "pinky_02_" };

        class Brazo
        {
            public Transform hombro, codo, mano, indice, menique, corazon;
            public Transform[] dedos;
        }

        Brazo _derecho, _izquierdo;
        Humano _humano;
        bool _buscado;

        /// <summary>Las manos de 'h' en las cachas de 'arma' (las de un brazo sin cacha, con su clip).</summary>
        public static ManosEnArma Poner(Humano h, Transform arma, Transform derecha, Transform izquierda)
        {
            if (h == null) return null;
            var m = h.GetComponent<ManosEnArma>();
            if (m == null) m = h.gameObject.AddComponent<ManosEnArma>();
            m.arma = arma;
            m.derecha = derecha;
            m.izquierda = izquierda;
            m._buscado = false;
            return m;
        }

        void LateUpdate() => Aplicar();

        /// <summary>
        /// Lo de cada LateUpdate, a mano (una prueba que para el Animator en una postura y mide en el mismo fotograma). Dice
        /// si ha podido: sin los huesos de los brazos (otro esqueleto), nada.
        /// </summary>
        public bool Aplicar()
        {
            if (!_buscado)
            {
                _buscado = true;
                _humano = GetComponent<Humano>();
                _derecho = BuscarBrazo("r");
                _izquierdo = BuscarBrazo("l");
                if (_derecho == null || _izquierdo == null) Debug.LogWarning($"[GD] {name}: sin los huesos de los brazos, las manos no van al arma");
            }
            if (_humano != null && _humano.Muerto) return false;
            var ejes = arma != null ? arma : derecha != null ? derecha : izquierda;
            if (ejes == null) return false;
            Vector3 canon = -ejes.right, arriba = ejes.up;
            bool hecho = false;
            if (_derecho != null && derecha != null) { Coger(_derecho, derecha.position, canon, arriba); hecho = true; }
            if (_izquierdo != null && izquierda != null) { Coger(_izquierdo, izquierda.position, canon, arriba); hecho = true; }
            return hecho;
        }

        /// <summary>Donde cierra el puno de un brazo: el centro de sus falanges.</summary>
        static Vector3 Puno(Brazo b)
        {
            Vector3 p = Vector3.zero;
            foreach (var d in b.dedos) p += d.position;
            return p / b.dedos.Length;
        }

        /// <summary>El puno de un brazo en 'cacha', con la mano girada a los ejes del arma.</summary>
        static void Coger(Brazo b, Vector3 cacha, Vector3 canon, Vector3 arriba)
        {
            // Con la mano ya girada, el puno queda donde esta ahora respecto a la muneca, girado igual: la muneca tiene que ir
            // ahi detras de la cacha.
            var giro = Giro(b, canon, arriba);
            Vector3 meta = cacha - giro * (Puno(b) - b.mano.position);
            DosHuesos(b.hombro, b.codo, b.mano, meta);
            // (el brazo ha girado la mano con el: el giro, otra vez)
            b.mano.rotation = Giro(b, canon, arriba) * b.mano.rotation;
        }

        /// <summary>
        /// Lo que hay que girar la mano para coger una cacha de pie: los nudillos (de la muneca al corazon) hacia el canon y
        /// del menique al indice hacia arriba del arma.
        /// </summary>
        static Quaternion Giro(Brazo b, Vector3 canon, Vector3 arriba)
        {
            Vector3 alto = b.indice.position - b.menique.position;
            Vector3 nudillos = Vector3.ProjectOnPlane(b.corazon.position - b.mano.position, alto);
            Vector3 hacia = Vector3.ProjectOnPlane(canon, arriba);
            if (alto.sqrMagnitude < 1e-8f || nudillos.sqrMagnitude < 1e-8f || hacia.sqrMagnitude < 1e-8f) return Quaternion.identity;
            return Quaternion.LookRotation(hacia, arriba) * Quaternion.Inverse(Quaternion.LookRotation(nudillos, alto));
        }

        /// <summary>
        /// La cadena hombro-codo-mano con la mano en 'meta' (la ley de los cosenos): primero el hombro y el codo en el plano
        /// del brazo (el que tiene el clip: el codo no cambia de lado), para que la mano quede a la distancia de la meta sobre
        /// la linea de antes, y despues el brazo entero hacia la meta. Lo que no alcanza, estirado hacia ella.
        /// </summary>
        static void DosHuesos(Transform hombro, Transform codo, Transform mano, Vector3 meta)
        {
            Vector3 a = hombro.position, b = codo.position, c = mano.position;
            float lab = (b - a).magnitude, lcb = (c - b).magnitude;
            if (lab < 1e-4f || lcb < 1e-4f) return;
            float lat = Mathf.Clamp((meta - a).magnitude, Mathf.Abs(lab - lcb) + 1e-3f, lab + lcb - 1e-3f);
            float acab0 = Angulo(c - a, b - a), babc0 = Angulo(a - b, c - b);
            float acab1 = Mathf.Acos(Mathf.Clamp((lcb * lcb - lab * lab - lat * lat) / (-2f * lab * lat), -1f, 1f));
            float babc1 = Mathf.Acos(Mathf.Clamp((lat * lat - lab * lab - lcb * lcb) / (-2f * lab * lcb), -1f, 1f));
            // (el brazo recto no tiene plano: el codo, hacia abajo)
            Vector3 eje = Vector3.Cross(c - a, b - a);
            if (eje.sqrMagnitude < 1e-10f) eje = Vector3.Cross(c - a, Vector3.down);
            if (eje.sqrMagnitude < 1e-10f) eje = Vector3.Cross(c - a, Vector3.forward);
            eje.Normalize();
            hombro.rotation = Quaternion.AngleAxis((acab1 - acab0) * Mathf.Rad2Deg, eje) * hombro.rotation;
            codo.rotation = Quaternion.AngleAxis((babc1 - babc0) * Mathf.Rad2Deg, eje) * codo.rotation;
            hombro.rotation = Quaternion.FromToRotation(mano.position - a, meta - a) * hombro.rotation;
        }

        static float Angulo(Vector3 u, Vector3 v) => Mathf.Acos(Mathf.Clamp(Vector3.Dot(u.normalized, v.normalized), -1f, 1f));

        /// <summary>Los huesos de un brazo ("r" o "l") del esqueleto de AssetsCreator; null si falta alguno.</summary>
        Brazo BuscarBrazo(string lado)
        {
            var b = new Brazo
            {
                hombro = Buscar(transform, "upperarm_" + lado), codo = Buscar(transform, "lowerarm_" + lado), mano = Buscar(transform, "hand_" + lado),
                indice = Buscar(transform, "index_01_" + lado), menique = Buscar(transform, "pinky_01_" + lado), corazon = Buscar(transform, "middle_01_" + lado),
                dedos = new Transform[Falanges.Length],
            };
            for (int i = 0; i < Falanges.Length; i++)
                if ((b.dedos[i] = Buscar(transform, Falanges[i] + lado)) == null) return null;
            return b.hombro != null && b.codo != null && b.mano != null && b.indice != null && b.menique != null && b.corazon != null ? b : null;
        }

        static Transform Buscar(Transform t, string nombre)
        {
            if (t.name == nombre) return t;
            foreach (Transform h in t)
            {
                var r = Buscar(h, nombre);
                if (r != null) return r;
            }
            return null;
        }
    }
}
