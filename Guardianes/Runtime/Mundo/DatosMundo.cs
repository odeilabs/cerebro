using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Unity.Collections;
using Unity.Mathematics;

namespace Guardianes.Mundo
{
    /// <summary>
    /// El mundo en formato compacto (un .bytes en GZip): alturas a la resolucion del heightmap en 16
    /// bits (codificadas por diferencias para que comprima), mascaras a la mitad en 8 bits, la cota
    /// del agua, los rios, los lagos y los rasgos sueltos (mogotes, tepuyes). Con esto se levantan los
    /// terrenos al cargar la escena, se pinta el suelo, se reparte la vegetacion y se construye el
    /// agua; nada pesado va al repo.
    ///   Version 1 (la isla de Kong): sin medidas ni agua ni lagos; las medidas las da quien lee.
    ///   Version 2: medidas en la cabecera, mapa de agua, lagos y mascaras extra con nombre.
    /// </summary>
    public sealed class DatosMundo : IDisposable
    {
        const int Firma = 0x4B4B4953; // "SIKK" (de la isla de Kong, donde nacio el formato)
        const int Version = 2;

        public MedidasMundo medidas;
        public int resAlturas;            // medidas.ResolucionGlobal
        public int resMascaras;           // (resAlturas - 1) / 2 + 1
        public NativeArray<float> alturas; // metros
        public NativeArray<byte> cauce, ribera, humedad, contaminacion, claro;
        /// <summary>Cota del agua a la resolucion de las mascaras: 0 = seco; si no, 1 + cota en 16 bits.</summary>
        public NativeArray<ushort> agua;
        public Rios.Punto[][] rios = new Rios.Punto[0][];
        public float4[] torres = new float4[0];
        public Lago[] lagos = new Lago[0];
        /// <summary>Mascaras propias de cada juego (varzea, playa, camino...), a la resolucion de las demas.</summary>
        public readonly Dictionary<string, NativeArray<byte>> extras = new Dictionary<string, NativeArray<byte>>();
        public int VersionLeida { get; private set; } = Version;

        public static int ResMascaras(int resAlturas) => (resAlturas - 1) / 2 + 1;

        public DatosMundo(MedidasMundo medidas)
        {
            this.medidas = medidas;
            resAlturas = medidas.ResolucionGlobal;
            resMascaras = ResMascaras(resAlturas);
            alturas = new NativeArray<float>(resAlturas * resAlturas, Allocator.Persistent);
            int m = resMascaras * resMascaras;
            cauce = new NativeArray<byte>(m, Allocator.Persistent);
            ribera = new NativeArray<byte>(m, Allocator.Persistent);
            humedad = new NativeArray<byte>(m, Allocator.Persistent);
            contaminacion = new NativeArray<byte>(m, Allocator.Persistent);
            claro = new NativeArray<byte>(m, Allocator.Persistent);
        }

        public void Dispose()
        {
            if (alturas.IsCreated) alturas.Dispose();
            if (cauce.IsCreated) cauce.Dispose();
            if (ribera.IsCreated) ribera.Dispose();
            if (humedad.IsCreated) humedad.Dispose();
            if (contaminacion.IsCreated) contaminacion.Dispose();
            if (claro.IsCreated) claro.Dispose();
            if (agua.IsCreated) agua.Dispose();
            foreach (var e in extras.Values) if (e.IsCreated) e.Dispose();
            extras.Clear();
        }

        /// <summary>Crea (o devuelve) una mascara extra con nombre.</summary>
        public NativeArray<byte> Extra(string nombre)
        {
            if (!extras.TryGetValue(nombre, out var m) || !m.IsCreated)
            {
                m = new NativeArray<byte>(resMascaras * resMascaras, Allocator.Persistent);
                extras[nombre] = m;
            }
            return m;
        }

        public bool TieneExtra(string nombre) => extras.TryGetValue(nombre, out var m) && m.IsCreated;

        public void CrearAgua()
        {
            if (!agua.IsCreated) agua = new NativeArray<ushort>(resMascaras * resMascaras, Allocator.Persistent);
        }

        public ushort CodificarAgua(float nivel) =>
            float.IsNaN(nivel) ? (ushort)0 : (ushort)(1 + math.clamp(math.round((nivel - medidas.alturaMinima) / medidas.rangoAlturas * 65534f), 0f, 65534f));

        public float DecodificarAgua(ushort v) =>
            v == 0 ? float.NaN : medidas.alturaMinima + (v - 1) / 65534f * medidas.rangoAlturas;

        // ------------------------------------------------------------------ Muestreo
        /// <summary>Altura del terreno (m) en x, z de mundo, bilineal.</summary>
        public float Altura(float2 xz)
        {
            float2 g = math.clamp((xz - medidas.Origen) / medidas.tamano * (resAlturas - 1), 0f, resAlturas - 1.001f);
            int2 c = (int2)g;
            float2 f = g - c;
            int k = c.y * resAlturas + c.x;
            return math.lerp(math.lerp(alturas[k], alturas[k + 1], f.x), math.lerp(alturas[k + resAlturas], alturas[k + resAlturas + 1], f.x), f.y);
        }

        /// <summary>Mascara (0..1) en x, z de mundo, bilineal.</summary>
        public float Mascara(NativeArray<byte> m, float2 xz)
        {
            if (!m.IsCreated) return 0f;
            float2 g = math.clamp((xz - medidas.Origen) / medidas.tamano * (resMascaras - 1), 0f, resMascaras - 1.001f);
            int2 c = (int2)g;
            float2 f = g - c;
            int k = c.y * resMascaras + c.x;
            float a = m[k], b = m[k + 1], d = m[k + resMascaras], e = m[k + resMascaras + 1];
            return math.lerp(math.lerp(a, b, f.x), math.lerp(d, e, f.x), f.y) / 255f;
        }

        public float MascaraExtra(string nombre, float2 xz) => extras.TryGetValue(nombre, out var m) ? Mascara(m, xz) : 0f;

        /// <summary>
        /// Cota del agua en x, z (rio, lago o mar) o NaN si alli no hay agua. Con mapa de agua (v2)
        /// sale de las cuatro muestras vecinas; sin el (v1), del cauce, los lagos y el punto de rio
        /// mas cercano.
        /// </summary>
        public float NivelAgua(float2 xz)
        {
            float nivel = float.NaN;
            if (agua.IsCreated)
            {
                // El maximo de las cuatro vecinas: la orilla no se come el agua.
                float2 g = math.clamp((xz - medidas.Origen) / medidas.tamano * (resMascaras - 1), 0f, resMascaras - 1.001f);
                int2 c = (int2)g;
                int k = c.y * resMascaras + c.x;
                ushort v = (ushort)math.max(math.max((int)agua[k], (int)agua[k + 1]), math.max((int)agua[k + resMascaras], (int)agua[k + resMascaras + 1]));
                nivel = DecodificarAgua(v);
            }
            else if (Mascara(cauce, xz) > 0.2f)
            {
                foreach (var l in lagos)
                    if (l.Contiene(xz, 1.02f)) { nivel = l.nivel; break; }
                if (float.IsNaN(nivel)) nivel = NivelRioCercano(xz, 160f);
            }
            if (medidas.hayMar && (float.IsNaN(nivel) || nivel < medidas.nivelMar) && Altura(xz) < medidas.nivelMar)
                nivel = medidas.nivelMar;
            return nivel;
        }

        // Rejilla de puntos de rio para la consulta de la version 1 (se construye la primera vez).
        Dictionary<long, List<int2>> _rejillaRios;
        const float CeldaRios = 64f;

        float NivelRioCercano(float2 xz, float alcance)
        {
            if (_rejillaRios == null)
            {
                _rejillaRios = new Dictionary<long, List<int2>>();
                for (int r = 0; r < rios.Length; r++)
                    for (int i = 0; i < rios[r].Length; i++)
                    {
                        int2 c = (int2)math.floor(rios[r][i].xz / CeldaRios);
                        long clave = ((long)c.x << 32) ^ (uint)c.y;
                        if (!_rejillaRios.TryGetValue(clave, out var l)) _rejillaRios[clave] = l = new List<int2>();
                        l.Add(new int2(r, i));
                    }
            }
            int2 c0 = (int2)math.floor((xz - alcance) / CeldaRios), c1 = (int2)math.floor((xz + alcance) / CeldaRios);
            float mejor = alcance * alcance, nivel = float.NaN;
            for (int y = c0.y; y <= c1.y; y++)
            for (int x = c0.x; x <= c1.x; x++)
            {
                if (!_rejillaRios.TryGetValue(((long)x << 32) ^ (uint)y, out var l)) continue;
                foreach (var ri in l)
                {
                    var p = rios[ri.x][ri.y];
                    float d = math.distancesq(p.xz, xz);
                    if (d < mejor) { mejor = d; nivel = p.nivel; }
                }
            }
            return nivel;
        }

        // ------------------------------------------------------------------ Serializacion
        public byte[] Escribir()
        {
            using var memoria = new MemoryStream();
            using (var gz = new GZipStream(memoria, CompressionLevel.Optimal, true))
            using (var w = new BinaryWriter(gz))
            {
                w.Write(Firma);
                w.Write(Version);
                w.Write(medidas.tamano); w.Write(medidas.teselas); w.Write(medidas.resolucionAlturas); w.Write(medidas.resolucionCapas);
                w.Write(medidas.alturaMinima); w.Write(medidas.rangoAlturas); w.Write(medidas.nivelMar); w.Write(medidas.hayMar);
                w.Write(resAlturas);

                // Alturas en 16 bits sobre [alturaMinima, alturaMinima + rango], fila a fila por diferencias.
                var fila = new byte[resAlturas * 2];
                for (int j = 0; j < resAlturas; j++)
                {
                    ushort previo = 0;
                    for (int i = 0; i < resAlturas; i++)
                    {
                        float h = alturas[j * resAlturas + i];
                        ushort q = (ushort)math.clamp(math.round((h - medidas.alturaMinima) / medidas.rangoAlturas * 65535f), 0f, 65535f);
                        ushort d = unchecked((ushort)(q - previo));
                        previo = q;
                        fila[i * 2] = (byte)(d & 0xFF);
                        fila[i * 2 + 1] = (byte)(d >> 8);
                    }
                    w.Write(fila);
                }

                foreach (var m in new[] { cauce, ribera, humedad, contaminacion, claro })
                    w.Write(m.ToArray());

                w.Write(agua.IsCreated);
                if (agua.IsCreated)
                {
                    var filaAgua = new byte[resMascaras * 2];
                    for (int j = 0; j < resMascaras; j++)
                    {
                        ushort previo = 0;
                        for (int i = 0; i < resMascaras; i++)
                        {
                            ushort q = agua[j * resMascaras + i];
                            ushort d = unchecked((ushort)(q - previo));
                            previo = q;
                            filaAgua[i * 2] = (byte)(d & 0xFF);
                            filaAgua[i * 2 + 1] = (byte)(d >> 8);
                        }
                        w.Write(filaAgua);
                    }
                }

                w.Write(rios.Length);
                foreach (var rio in rios)
                {
                    w.Write(rio.Length);
                    foreach (var p in rio)
                    {
                        w.Write(p.xz.x); w.Write(p.xz.y); w.Write(p.nivel); w.Write(p.semiancho);
                        w.Write(p.profundidad); w.Write(p.s); w.Write(p.avance); w.Write(p.cascada);
                    }
                }

                w.Write(torres.Length);
                foreach (var t in torres) { w.Write(t.x); w.Write(t.y); w.Write(t.z); w.Write(t.w); }

                w.Write(lagos.Length);
                foreach (var l in lagos)
                {
                    w.Write(l.nombre ?? "");
                    w.Write(l.centro.x); w.Write(l.centro.y); w.Write(l.nivel); w.Write(l.profundidad);
                    int n = l.radios != null ? l.radios.Length : 0;
                    w.Write(n);
                    for (int i = 0; i < n; i++) w.Write(l.radios[i]);
                }

                w.Write(extras.Count);
                foreach (var kv in extras)
                {
                    w.Write(kv.Key);
                    w.Write(kv.Value.ToArray());
                }
            }
            return memoria.ToArray();
        }

        /// <summary>Lee un mundo. medidasV1: las medidas de un archivo de version 1 (que no las lleva).</summary>
        public static DatosMundo Leer(byte[] bytes, MedidasMundo medidasV1)
        {
            using var memoria = new MemoryStream(bytes);
            using var gz = new GZipStream(memoria, CompressionMode.Decompress);
            using var r = new BinaryReader(gz);
            if (r.ReadInt32() != Firma) throw new InvalidDataException("Datos del mundo: firma incorrecta");
            int version = r.ReadInt32();
            if (version < 1 || version > Version) throw new InvalidDataException("Datos del mundo: version " + version);

            var medidas = medidasV1;
            if (version >= 2)
            {
                medidas = new MedidasMundo
                {
                    tamano = r.ReadSingle(), teselas = r.ReadInt32(), resolucionAlturas = r.ReadInt32(), resolucionCapas = r.ReadInt32(),
                    alturaMinima = r.ReadSingle(), rangoAlturas = r.ReadSingle(), nivelMar = r.ReadSingle(), hayMar = r.ReadBoolean(),
                };
            }
            int res = r.ReadInt32();
            if (res != medidas.ResolucionGlobal)
                throw new InvalidDataException($"Datos del mundo: {res} muestras y las medidas piden {medidas.ResolucionGlobal}");
            var d = new DatosMundo(medidas) { VersionLeida = version };

            var alturas = new float[res * res];
            for (int j = 0; j < res; j++)
            {
                var fila = r.ReadBytes(res * 2);
                ushort q = 0;
                for (int i = 0; i < res; i++)
                {
                    ushort dif = (ushort)(fila[i * 2] | (fila[i * 2 + 1] << 8));
                    q = unchecked((ushort)(q + dif));
                    alturas[j * res + i] = medidas.alturaMinima + q / 65535f * medidas.rangoAlturas;
                }
            }
            d.alturas.CopyFrom(alturas);

            int m = d.resMascaras * d.resMascaras;
            foreach (var mascara in new[] { d.cauce, d.ribera, d.humedad, d.contaminacion, d.claro })
                mascara.CopyFrom(r.ReadBytes(m));

            if (version >= 2 && r.ReadBoolean())
            {
                d.CrearAgua();
                var aguaTmp = new ushort[m];
                for (int j = 0; j < d.resMascaras; j++)
                {
                    var fila = r.ReadBytes(d.resMascaras * 2);
                    ushort q = 0;
                    for (int i = 0; i < d.resMascaras; i++)
                    {
                        ushort dif = (ushort)(fila[i * 2] | (fila[i * 2 + 1] << 8));
                        q = unchecked((ushort)(q + dif));
                        aguaTmp[j * d.resMascaras + i] = q;
                    }
                }
                d.agua.CopyFrom(aguaTmp);
            }

            d.rios = new Rios.Punto[r.ReadInt32()][];
            for (int k = 0; k < d.rios.Length; k++)
            {
                var rio = new Rios.Punto[r.ReadInt32()];
                for (int i = 0; i < rio.Length; i++)
                {
                    rio[i] = new Rios.Punto
                    {
                        xz = new float2(r.ReadSingle(), r.ReadSingle()),
                        nivel = r.ReadSingle(),
                        semiancho = r.ReadSingle(),
                        profundidad = r.ReadSingle(),
                        s = r.ReadSingle(),
                        avance = r.ReadSingle(),
                        cascada = r.ReadBoolean(),
                    };
                }
                d.rios[k] = rio;
            }

            d.torres = new float4[r.ReadInt32()];
            for (int k = 0; k < d.torres.Length; k++)
                d.torres[k] = new float4(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

            if (version >= 2)
            {
                d.lagos = new Lago[r.ReadInt32()];
                for (int k = 0; k < d.lagos.Length; k++)
                {
                    var l = new Lago { nombre = r.ReadString(), centro = new float2(r.ReadSingle(), r.ReadSingle()), nivel = r.ReadSingle(), profundidad = r.ReadSingle() };
                    l.radios = new float[r.ReadInt32()];
                    for (int i = 0; i < l.radios.Length; i++) l.radios[i] = r.ReadSingle();
                    d.lagos[k] = l;
                }
                int extras = r.ReadInt32();
                for (int k = 0; k < extras; k++)
                {
                    string nombre = r.ReadString();
                    d.Extra(nombre).CopyFrom(r.ReadBytes(m));
                }
            }
            return d;
        }
    }
}
