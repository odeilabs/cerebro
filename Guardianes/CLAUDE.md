# Guardianes — paquete común de la saga

Paquete de Unity (`com.odeilabs.guardianes`) con lo que comparten los juegos de la saga **Gaia's
Response** de Odei Labs: *King Kong — El guardián de la isla*, *Basiliskos* y los que vengan (y el
crossover final, *Primal Legends*). Vive en el cerebro de la saga (`Gaia's Response/cerebro`, ver
`../CLAUDE.md` para las reglas comunes y `../README.md` para la guía de AssetsCreator). Cada juego
lo añade a su `Packages/manifest.json` como paquete local:

```json
"com.odeilabs.guardianes": "file:../../../cerebro/Guardianes"
```

(la ruta es relativa a la carpeta `Packages` del proyecto: `Gaia's Response/<juego>/<proyecto>/Packages`).

Unity 6000.5.2f1, HDRP 17.5, Input System 1.19, Burst + Collections + Mathematics. Código y nombres
en español. Ensamblados `Guardianes` (runtime) y `Guardianes.Editor`.

## Qué hay

- `Runtime/Mundo/` — `MedidasMundo` (lado, teselas, resoluciones, rango de alturas, mar), `Mapa`
  (consultas para todos: altura, agua, profundidad, tierra firme, normal), `DatosMundo` (el mundo
  compacto en `.bytes`: alturas, máscaras, **mapa de agua**, ríos, lagos, rasgos y máscaras extra
  con nombre; versión 1 = la de la isla de Kong, sin medidas ni agua), `TerrenoMundo` (levanta los
  Terrain y pinta 8 capas con Burst; `Purificar`), `AguaMundo` (lagos con WaterSurface, cintas de
  río, cascadas), `Rios` (trazado denso con meandros a partir de puntos de control), `Lago`,
  `Zona`/`TipoZona` (los valores son bits de las partidas guardadas: los nuevos van al final),
  `Ruido` (fBm, crestas, hash, para Burst).
- `Runtime/Vegetacion/` — `Viento` (+ interactores), `EspecieVegetal`, `Generacion/Plantas`
  (ceiba, dosel, palmera, helecho arbóreo, platanera, bambú, arbusto, helecho, hojas grandes,
  hierba, flores, roca, tronco), `RenderizadorVegetacion` (instancing por cámara con culling/LOD en
  Burst; `Buscar`, `Derribar`), `Derribos`, `DatosVegetacion`.
- `Runtime/Criaturas/Criatura` — **la criatura del jugador** (Kong, el basilisco...). Los humanos,
  los helicópteros, la cámara, los efectos, el sonido y el HUD la buscan aquí (`Criatura.Jugador`)
  y escalan con `Tamano` (17,5 m = Kong = escala 1) y `AlturaBlanco`. Eventos para los demás:
  `Intimidar` (los humanos huyen, los helicópteros cercanos pierden el control), `OndaCono`,
  `Purificar`, `Pisar`. `Premiar` (cada juego decide qué da: furia, crecimiento, afinidad) y
  `Celebrar`. `IEntradaCamara` para que la cámara lea los controles de cada juego.
- `Runtime/Combate/` — `Salud` + `DatosDanio`/`TipoDanio`, `Impacto` (daño en área, empuje,
  derribo de vegetación), `Lanzable`.
- `Runtime/Enemigos/` — **Ferrox**: `Humano` (IA según el tamaño de la criatura: con una gigante
  patrulla, dispara y huye; a una pequeña la buscan a machetazos; detección con luz y escondites),
  `ZonaHumana`, `ConstructorInstalaciones` + `Piezas` + `ConstructorPiezasCampana`, `Estructura`,
  `Helicoptero` (rotor del BEMT de Lur Aerospace), `Cohete`, `MaterialesInstalaciones`.
- `Runtime/Efectos/EfectosCombate`, `Runtime/Audio/SonidoJuego` (la voz de cada criatura la pone su
  juego con `Sonar` y los bancos `voces`/`percusion`/`pisadas`...), `Runtime/Camara/CamaraCriatura`
  (las medidas valen para `tamanoReferencia` y crecen con la criatura), `Runtime/Juego/`
  (`CalidadJuego` con `AlAplicar`, `RecursosJuego`, `Descubrimiento` con `tipo` entero),
  `Runtime/UI/HudJuego` (+ `PantallasHud`: cada juego hereda y pinta su estado en
  `ConstruirEstado`/`ActualizarEstado`).
- `Editor/` — `ConfiguradorHDRP` (ajustes ultra y la atmósfera de selva), `MaterialesLit`,
  `Capturador`, `Diagnostico`, `ImportadorTexturas`, `Terreno/` (`Relieve` y `PasadasRelieve`:
  erosión, subida con detalle, niveles de los ríos, tallar ríos y lagos, allanar zonas y lugares,
  humedad; el relieve base es de cada juego), `Vegetacion/` (`EspeciesSelva`, `RepartoVegetacion`
  con reglas por juego; `ReglasSelvaTropical` son las de la isla de Kong), `Agua/MaterialesAgua`,
  `Efectos/ConstructorEfectos`, `Audio/ConstructorAudio`, `Enemigos/` (`ConstructorHumanos` desde
  AssetsCreator, `ConstructorHelicoptero`, `ConfiguradorZonas`).
- `Shaders/` — variantes de HDRP/Lit generadas por `Tools~/generar_shaders_lit.py`:
  `Guardianes/Vegetacion` (+ `Viento.hlsl`), `Guardianes/Pelaje` (+ `Pelaje.hlsl`),
  `Guardianes/Rio` (+ `Rio.hlsl`) y `Guardianes/Escamas` (+ `Escamas.hlsl`: piel de serpiente; la
  textura es una máscara de dibujo y los colores llegan por globales `_GD_Escamas*`).
  **No editar los `.shader` ni los `Data.hlsl` generados**: se cambia el `.hlsl` o el generador.
  Propios: `Guardianes/Particula`, `Guardianes/Cascada` y `Guardianes/Calor` (los cuerpos calientes
  de una visión térmica, como material de sustitución en un pase de HDRP; con variante
  `DOTS_INSTANCING_ON` y propiedades en `UnityPerMaterial`, que el GPU Resident Drawer exige).
  Las globales llevan el prefijo `_GD_`.
- `Tools~/` (Unity no lo importa) — `generar_shaders_lit.py --proyecto <proyecto con HDRP>`,
  `arte/terreno`, `arte/vegetacion`, `arte/efectos`, `arte/audio` (texturas y sonidos procedurales
  en Python: numpy/scipy/Pillow; todos con `--proyecto <carpeta del proyecto Unity>` o `--salida`),
  `vehiculos/sdf_helicoptero.py` (+ `decimar_obj.py` en Blender).

## Reglas

- Escala: 1 unidad = 1 m. Los mundos van centrados en el origen; los datos compactos llevan sus
  medidas. Nada pesado en los repos: terreno y vegetación se reconstruyen al cargar a partir de
  semillas y datos compactos (LFS).
- Lo que dependa de una criatura concreta no entra aquí: va en su juego, colgado de `Criatura`,
  de los eventos o de los métodos virtuales.
- Los componentes que King Kong ya usaba conservan su GUID (sus escenas y prefabs siguen valiendo);
  las clases que cada juego especializa (`TerrenoIsla : TerrenoMundo`, `CamaraKong :
  CamaraCriatura`, `HudKong : HudJuego`) se quedan en el juego con su GUID.
- Los logs del paquete van con `[GD]`.

## Verificar

El paquete no tiene proyecto propio: se compila y se prueba dentro de los juegos (sus
`Tools/Unity-Batch.ps1` y `Tools/Unity-Pruebas.ps1`, con el editor cerrado). Un cambio aquí se
comprueba en **los dos** juegos.
