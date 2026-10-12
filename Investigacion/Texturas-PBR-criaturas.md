# Texturas PBR para las criaturas de la saga

Qué texturas necesita cada criatura de *Gaia's Response*, siguiendo la lógica PBR de AssetsCreator
(lo que ya genera, cómo lo nombra y cómo llega a Unity HDRP) y lo que hay que añadir para las
criaturas que AssetsCreator no cubre. Octubre de 2026. Las rutas de AssetsCreator son relativas a
`Odei Labs/AssetsCreator/AssetsCreatorWin/`; recuerda que **no se modifica AssetsCreator**: lo que
falte se hace en el cerebro (shaders, generadores de `Guardianes/Tools~`) o en el juego.

## 1. El juego base: todas las criaturas

AssetsCreator escribe cada material con `ultra/pbr.py::exportar_material()`:

| AssetsCreator | En Unity HDRP (Lit) | Espacio | Notas |
|---|---|---|---|
| `<p>_basecolor.png` | Base Map (`_diff` si lo exporta `creador.py unity`) | sRGB | albedo entre ~30 y ~240 sRGB (`pbr.informe_calibracion()`) |
| `<p>_normal.png` | Normal Map (`_nor`) | lineal | tangente, **OpenGL (Y+)**; 16 bits si la rugosidad mediana es < 0,20 |
| `<p>_roughness.png` + `<p>_ao.png` (+ `<p>_metallic.png`) | **Mask Map** (`_mask`): R metal, G AO, B máscara de detalle, A suavidad = 1 − rugosidad | lineal, **sin comprimir** (BC1 se come el alfa) | `exportar_unity.py::empaquetar_mask()`; sin rugosidad, A = 128 |
| `<p>_height.png` | Height Map (POM o desplazamiento) | lineal, 16 bits, sin comprimir | la escala física sale de `RELIEVE_MM` (piel 0,5 mm, pelo 2,5, paquidermo 4,0) |
| `<p>_detail.png` | Detail Map en mosaico | lineal | neutro 0,5 (`exportar_detalle()`) |
| `<p>_material.json` | lo que no es mapa: IOR, transmisión, SSS y sus radios, brillo (*sheen*), barniz (*coat*), anisotropía, intensidad emisiva | — | se aplica a mano o en el constructor del juego |

- **Metal = 0** en casi todas las criaturas (solo armaduras de Ferrox, el oro del Grifo).
- **El Detail Map es obligatorio en las gigantes.** Una criatura de 18–120 m con una textura de 4K
  da menos de un píxel por centímetro: el detalle cercano (poros, escamas pequeñas, pelo) lo da un
  mapa en mosaico encima (como la piel humana: `_detail` de poros de 32 mm, tiling 25,6).
- **Resoluciones** (`configuracion.RESOLUCIONES`: 256–4096): 4096 + Detail para las gigantes;
  1024–2048 para las pequeñas (la cría de Basiliskos) y las partes (ojos, dientes).
- Dentro de un juego, los mapas se renombran a los sufijos que entiende `ImportadorTexturas` del
  cerebro: `_albedo` (sRGB), `_normal`, `_mascara` (Mask Map, lineal), `_grosor` (lineal, un canal) y
  `_altura` (16 bits sin comprimir). `_emision` se importa por defecto en sRGB, que es lo correcto.

## 2. Los extras según de qué está hecha la criatura

| Material | Mapas y datos extra | Estado en AssetsCreator | Criaturas de la saga |
|---|---|---|---|
| **Pelo corto** | `_packed` (R raíz→punta, G ID de mechón, B AO de raíz, A profundidad), UV girada por el campo de flujo (`generador/animales/pelaje.py::flujo`), brillo y anisotropía en `_pelaje.json`; albedo 0,012–0,80 lineal; IOR 1,55 | ✅ comando `animal` (`generador/crear_animal.py`) | Cerbero, Fenrir, Smilodon, Noctarion, León de Nemea, Kitsune, Nian, Ciervo blanco, Hielofauce; el león del Grifo y de la Mantícora |
| **Pelo largo por capas** (shells) | `_mechones` (A altura, RGB variación, lineal); color de vértice r largo / g plata / b piel; shader `Guardianes/Pelaje` | ✅ comando `gorila` | Koba (Gorilla Revenge) y pelajes densos |
| **Melenas, colas, crines** | tarjetas con alfa (`generador/animales/cards.py`, `_cards.json`) | ✅ | las nueve colas de la Kitsune, la melena de la Mantícora, la crin del Qilin |
| **Escamas** | atlas por tiras `_diff/_nor/_mask/_height`; F0 0,044 (n 1,53); **sin SSS en el dorso**; `_HeightAmplitude` = relieve en mm / 10 cm | ✅ `serpiente`, `basiliskos`; shader `Guardianes/Escamas` del cerebro | Basiliskos, Jörmungandr, Hidra, Titanoboa, Amaru, Qilin, las serpientes de Tifón |
| **Queratina, hueso, dientes** (cuernos, garras, colmillos, pezuñas, caparazón) | el juego base con valores físicos: queratina F0 0,047 (IOR 1,55), rugosidad 0,25–0,45; esmalte F0 0,057 (IOR 1,63) | ⚠️ en los animales, color plano (`generador/animales/cuerpo.py::_COLOR`); los valores están en `Investigacion/13-animales-cuadrupedos.md` §9.15; `forma --forma cuerno\|colmillo` da la malla | colmillos del Smilodon, cuernos y garras de todos, caparazón del Escorpión Colosal |
| **Piel desnuda y mucosas** (hocico, lengua, boca, almohadillas, orejas) | `_thickness` / `_grosor` (blanco = grueso, convenio de HDRP) + Diffusion Profile de HDRP; radios de Jensen R 3,5–4,8 / G 1,4–1,7 / B 0,7–1,1 mm; IOR 1,38–1,41; trufa con rugosidad 0,15–0,30, almohadillas 0,62–0,78 | ⚠️ solo hay perfil para la piel humana («Piel AssetsCreator», 3,67/1,37/0,68 mm, `unity/AssetsCreatorPiel.cs`); **no hay perfil de animal** | todas las bocas y lenguas; Mokele-Mbembe, Behemot (piel), la cara de Koba |
| **Membranas de ala** | `_thickness` + transmisión (la luz pasa a contraluz), venas en el albedo y la altura | ❌ | alas de murciélago (Tifón, la Mantícora si se decide así) |
| **Plumas** | tarjetas con alfa por pluma, anisotropía (el raquis), iridiscencia (`_iridescence` y `_iridescence_thickness`, que `pbr.py` ya sabe escribir), plumón por capas | ❌ no hay pipeline de plumas (solo la malla `forma --forma ala_plumas`) | Fénix, Grifo, Pájaro del Trueno, Roc, Simurgh |
| **Ojos** | iris y esclera (albedo con alfa + `_altura` de 16 bits) y la córnea como cáscara aparte (IOR 1,376, F0 0,025, rugosidad 0,03–0,08); `_emissive` si brillan | ✅ humano (`ultra/ojo_humano.py`) y serpiente (`generador/serpientes/ojo.py`); ❌ cuadrúpedo con córnea | todas; brillan los de Basiliskos y Noctarion |
| **Roca y lava** | juego base de roca + `_emissive` con una **máscara de enfriamiento** (lava viva → costra), altura fuerte; intensidad en nits | ⚠️ emisivos en `pbr.PARAMS` (brasa 10, llama 5), pero ninguna criatura de lava | Behemot, Magmákor, Pantagon, Escorpión Colosal (roca) |
| **Madera, raíces, musgo** | cortezas de la selva + máscara de musgo (como el color de vértice de `gorila --estatua`); `_emissive` si el veneno brilla | ✅ en parte (cortezas de `selva`, musgo de la estatua) | Raizmuerta, Antífero |
| **Piel mojada y bioluminiscencia** | rugosidad baja (película de agua: F0 0,020, rugosidad 0,05–0,10), barniz (*coat*); `_emissive` con su longitud de onda (como los hongos de Basiliskos, 520–530 nm); `_thickness` en lo gelatinoso | ⚠️ el emisivo de `basiliskos` sí; el resto no | Leviatán, Kraken, Kraken abisal, Megalodón; las estrellas del Noctarion |
| **Hielo y jade** (translúcidos) | `_thickness` + SSS o transmisión; en el hielo, IOR 1,31 y normales nítidas | ❌ | Hielofauce, la escarcha de Fenrir, el jade del Qilin |
| **Fuego, rayo, viento** | no es PBR: emisión en nits y partículas (`Guardianes/Particula`), no texturas de superficie | ✅ comandos `fuego`, `efectos` | Fénix, Pájaro del Trueno, Tifón |

## 3. Estados comunes de la saga

AssetsCreator tiene los estados del pelaje (`pbr.ESTADOS`: canas, muda, polvo, barro, sangre, mojado,
nieve; `--estado "mojado=0.8,barro=0.3"`), pero los **hornea en el albedo**. En la saga conviene que
vayan como **máscaras encima, en el shader** (como las globales `_GD_` de `Guardianes/Escamas`): una
misma textura sirve para la criatura limpia, herida, mojada o manchada, y cambia en juego. Estados que
piden las mecánicas de la saga, además de los de AssetsCreator:

- **Mojado**: queratina/agua F0 0,0057, película de agua F0 0,020 con rugosidad 0,05–0,10, la croma
  sube un 6 % (`Investigacion/14-pelaje-animales-y-videojuegos.md`).
- **Petrificación** (la mirada de Basiliskos y lo que venga): piedra del templo por máscara.
- **Quemado** (Fénix, Behemot) y **escarcha** (Fenrir, Hielofauce).
- **Contaminado**: el mercurio y el petróleo de Ferrox, una película iridiscente.
- **Sangre y daño**, por zonas.

## 4. Lo que falta, por cuántos juegos lo piden

Se hace en el cerebro (sin tocar AssetsCreator), en este orden:

1. **Plumas** (5 juegos y DLC): tarjetas con alfa, anisotropía e iridiscencia; una variante del Lit
   en `Tools~/generar_shaders_lit.py` si hace falta.
2. **Perfil de difusión y `_grosor` para animales**, que vale también para las membranas: casi todas.
3. **Emisivo de lava con máscara de enfriamiento** (4 juegos).
4. **Material translúcido** para hielo y jade.
5. **Ojo de cuadrúpedo con córnea.**
6. **Estados como máscaras en el shader** (sección 3) para todos.

Fuentes en AssetsCreator: `ultra/pbr.py` (exportación, `PARAMS`, `ESTADOS`, calibración),
`exportar_unity.py` (renombrado y Mask Map), `ultra/shaders_hdrp.py`, `generador/animales/`,
`generador/gorilas/mechones.py`, `generador/serpientes/`, `generador/basiliskos/texturas.py`,
`ultra/mpfb_blender.py` (piel), y la investigación `Investigacion/01`, `11`, `13`, `14` y `19`.
