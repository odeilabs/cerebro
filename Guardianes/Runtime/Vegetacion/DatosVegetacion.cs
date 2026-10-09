using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Unity.Mathematics;

namespace Guardianes.Vegetacion
{
    /// <summary>Una planta colocada en la isla.</summary>
    public struct InstanciaVegetal
    {
        public float3 posicion;
        public float giro;    // radianes alrededor de Y
        public float escala;
        public float inclinacion; // radianes (arboles de ladera, palmeras de playa)
        public float rumboInclinacion;
        public byte variante;
    }

    /// <summary>
    /// Vegetacion repartida por la isla (VegetacionIsla.bytes, GZip): por especie (por nombre de su
    /// asset), la lista de instancias. La genera ConstructorSelva; la lee RenderizadorVegetacion.
    /// </summary>
    public static class DatosVegetacion
    {
        const int Firma = 0x4B4B4756; // "VGKK"
        const int Version = 2;

        public static byte[] Escribir(Dictionary<string, List<InstanciaVegetal>> porEspecie)
        {
            using var memoria = new MemoryStream();
            using (var gz = new GZipStream(memoria, CompressionLevel.Optimal, true))
            using (var w = new BinaryWriter(gz))
            {
                w.Write(Firma);
                w.Write(Version);
                w.Write(porEspecie.Count);
                foreach (var kv in porEspecie)
                {
                    w.Write(kv.Key);
                    w.Write(kv.Value.Count);
                    foreach (var i in kv.Value)
                    {
                        w.Write(i.posicion.x); w.Write(i.posicion.y); w.Write(i.posicion.z);
                        w.Write(i.giro); w.Write(i.escala); w.Write(i.inclinacion); w.Write(i.rumboInclinacion);
                        w.Write(i.variante);
                    }
                }
            }
            return memoria.ToArray();
        }

        public static Dictionary<string, InstanciaVegetal[]> Leer(byte[] bytes)
        {
            var r = new Dictionary<string, InstanciaVegetal[]>();
            using var memoria = new MemoryStream(bytes);
            using var gz = new GZipStream(memoria, CompressionMode.Decompress);
            using var lector = new BinaryReader(gz);
            if (lector.ReadInt32() != Firma) throw new InvalidDataException("VegetacionIsla.bytes: firma incorrecta");
            if (lector.ReadInt32() != Version) throw new InvalidDataException("VegetacionIsla.bytes: version distinta");
            int especies = lector.ReadInt32();
            for (int e = 0; e < especies; e++)
            {
                string nombre = lector.ReadString();
                var lista = new InstanciaVegetal[lector.ReadInt32()];
                for (int i = 0; i < lista.Length; i++)
                {
                    lista[i] = new InstanciaVegetal
                    {
                        posicion = new float3(lector.ReadSingle(), lector.ReadSingle(), lector.ReadSingle()),
                        giro = lector.ReadSingle(),
                        escala = lector.ReadSingle(),
                        inclinacion = lector.ReadSingle(),
                        rumboInclinacion = lector.ReadSingle(),
                        variante = lector.ReadByte(),
                    };
                }
                r[nombre] = lista;
            }
            return r;
        }

        public static float4x4 Matriz(in InstanciaVegetal i)
        {
            quaternion giro = quaternion.RotateY(i.giro);
            if (i.inclinacion > 1e-4f)
            {
                float3 eje = new float3(math.cos(i.rumboInclinacion), 0f, math.sin(i.rumboInclinacion));
                giro = math.mul(quaternion.AxisAngle(eje, i.inclinacion), giro);
            }
            return float4x4.TRS(i.posicion, giro, new float3(i.escala));
        }
    }
}
