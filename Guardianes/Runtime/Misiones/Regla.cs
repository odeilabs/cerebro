using System;
using System.Collections.Generic;
using Guardianes.Combate;
using Guardianes.Criaturas;
using Guardianes.Enemigos;

namespace Guardianes.Misiones
{
    /// <summary>
    /// Lo que no se puede hacer durante toda la mision (que te vean, matar inocentes, morir...). El
    /// director la vigila con cada objetivo; 'Estado' sale en el HUD bajo el objetivo.
    /// </summary>
    public abstract class Regla
    {
        protected DirectorMisiones D;
        public virtual void Empezar(DirectorMisiones d) => D = d;
        public virtual void Terminar() { }
        /// <summary>Por que se ha perdido la mision (null si va bien).</summary>
        public abstract string Fallo { get; }
        /// <summary>Linea para el HUD (null = nada).</summary>
        public virtual string Estado => null;

        /// <summary>Lo ha hecho la criatura del jugador (el origen del dano es ella o algo suyo).</summary>
        public static bool CulpaDeLaCriatura(DatosDanio d, Criatura c)
        {
            if (c == null || d.origen == null) return false;
            return d.origen == c.gameObject || d.origen.transform.IsChildOf(c.transform);
        }
    }

    /// <summary>Que no te vean mas de 'maximo' humanos (sigilo).</summary>
    public class SinSerVisto : Regla
    {
        readonly int _maximo;
        readonly string _motivo;
        readonly Func<Humano, bool> _cuenta;
        readonly HashSet<Humano> _vistos = new HashSet<Humano>();

        /// <param name="cuenta">Que humanos cuentan (null = todos menos los protegidos).</param>
        public SinSerVisto(int maximo = 0, string motivo = null, Func<Humano, bool> cuenta = null)
        {
            _maximo = maximo;
            _motivo = motivo ?? "Te han visto.";
            _cuenta = cuenta;
        }

        public override void Empezar(DirectorMisiones d)
        {
            base.Empezar(d);
            _vistos.Clear();
            Humano.AlVer += Visto;
        }

        public override void Terminar() => Humano.AlVer -= Visto;

        void Visto(Humano h)
        {
            if (h == null || (_cuenta != null ? !_cuenta(h) : h.protegido)) return;
            _vistos.Add(h);
        }

        public int Vistos => _vistos.Count;
        public override string Fallo => _vistos.Count > _maximo ? _motivo : null;
        public override string Estado => $"Te han visto {_vistos.Count}/{_maximo + 1}";
    }

    /// <summary>No matar a mas de 'maximo' civiles (desarmados) ni a ningun protegido.</summary>
    public class SinMatarCiviles : Regla
    {
        readonly int _maximo;
        readonly string _motivo;
        int _muertos;

        public SinMatarCiviles(int maximo = 0, string motivo = null)
        {
            _maximo = maximo;
            _motivo = motivo ?? "Has matado a gente que no hacía daño a la selva.";
        }

        public override void Empezar(DirectorMisiones d)
        {
            base.Empezar(d);
            _muertos = 0;
            Humano.AlMorir += Muerto;
        }

        public override void Terminar() => Humano.AlMorir -= Muerto;

        void Muerto(Humano h)
        {
            if (h == null || !((h.civil && h.inocente) || h.protegido)) return;
            var s = h.GetComponent<Salud>();
            if (s != null && CulpaDeLaCriatura(s.UltimoDanio, D.Criatura)) _muertos++;
        }

        public override string Fallo => _muertos > _maximo ? _motivo : null;
        public override string Estado => _maximo > 0 ? $"Inocentes muertos {_muertos}/{_maximo}" : null;
    }

    /// <summary>La criatura no puede morir (si muere, se repite la mision).</summary>
    public class CriaturaViva : Regla
    {
        readonly string _motivo;
        bool _muerta;

        public CriaturaViva(string motivo = null) { _motivo = motivo ?? "Has muerto."; }

        public override void Empezar(DirectorMisiones d)
        {
            base.Empezar(d);
            _muerta = false;
            if (d.Criatura != null) d.Criatura.Salud.AlMorir += Muere;
        }

        public override void Terminar()
        {
            if (D != null && D.Criatura != null) D.Criatura.Salud.AlMorir -= Muere;
        }

        void Muere(DatosDanio d) => _muerta = true;
        public override string Fallo => _muerta ? _motivo : null;
    }

    /// <summary>Una regla hecha con una funcion (fallo o null) y, si se quiere, su linea de HUD.</summary>
    public class ReglaCondicion : Regla
    {
        readonly Func<string> _fallo, _estado;
        public ReglaCondicion(Func<string> fallo, Func<string> estado = null) { _fallo = fallo; _estado = estado; }
        public override string Fallo => _fallo();
        public override string Estado => _estado?.Invoke();
    }
}
