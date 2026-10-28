using UnityEngine;

namespace Guardianes.Enemigos
{
    /// <summary>
    /// Como va montada un arma pesada que no se lleva en la mano (la M2 en la cuna de la torreta de un blindado), de su
    /// ficha (el bloque 'montaje' de su _agarre.json): el pivote sobre el que gira, en el espacio del modelo importado (la x
    /// del OBJ cambiada de signo), el eje de giro y el radio de la columna. La malla acaba en la espiga de la columna: el
    /// poste hasta el techo o el suelo lo pone cada juego (un cilindro de radioColumna desde el pivote hacia abajo, del alto
    /// que haga falta). Lo pone ConstructorHumanos al montar el arma (InstanciarArma, PonerArma).
    /// </summary>
    [DisallowMultipleComponent]
    public class MontajeArma : MonoBehaviour
    {
        public Vector3 pivote;
        public Vector3 ejeGiro = Vector3.up;
        public float radioColumna;

        /// <summary>El pivote en el mundo.</summary>
        public Vector3 Pivote => transform.TransformPoint(pivote);

        /// <summary>El eje de giro en el mundo.</summary>
        public Vector3 EjeGiro => transform.TransformDirection(ejeGiro).normalized;
    }
}
