# Cerebro de Gaia's Response — Roadmap

Lo que falta en lo común de la saga (paquete `com.odeilabs.guardianes` e instrucciones). Lo de cada
juego está en el `ROADMAP.md` de su carpeta; el mapa de la saga, en `../README.md`.

## Hecho (octubre 2026)

- Paquete `com.odeilabs.guardianes` extraído de King Kong y usado por King Kong y Basiliskos:
  mundo (relieve, agua, ríos, lagos, vegetación instanciada con viento, derribos), `Criatura`,
  combate, humanos de Ferrox (detección con luz y escondites, criaturas pequeñas y gigantes),
  zonas e instalaciones, helicópteros (BEMT) y cohetes, efectos, sonido, cámara, calidad,
  descubrimientos, HUD base, herramientas de editor (HDRP ultra, terreno, reparto de selva, humanos
  de AssetsCreator, zonas, efectos, audio, capturas, diagnóstico) y generadores de arte en `Tools~`.
- Shaders Lit generados: Vegetación, Pelaje, Río, Escamas; Partícula, Cascada, Calor.
- Mudanza a `Gaia's Response/cerebro` (repo propio) y documentación común: este roadmap,
  [CLAUDE.md](CLAUDE.md) y [README.md](README.md) con la guía de AssetsCreator.
- `ConstructorHumanos` encuentra AssetsCreator subiendo desde el proyecto (o con `%ASSETSCREATOR%`),
  sin depender de la profundidad de la carpeta del juego.

## Siguiente

1. **Día y noche**: un ciclo común (sol, luna, exposición, la luz que ven los humanos) y fuentes de
   luz (linternas, focos, hogueras, generadores) que cualquier juego pueda usar. Lo pide el Acto I
   de Basiliskos (misiones de noche).
2. **Miedo de los humanos**: el valor de miedo (0-100) en `Humano`, lo que lo sube (gritos,
   siseos, cuerpos, oscuridad, ver a la criatura de reojo, marcas de leyenda), el contagio y sus
   efectos (disparar a las sombras, quedarse quieto, tirar el arma, huir para no volver). Activable
   por juego.
3. **Director de misiones común**: objetivos, reglas, diálogos y narración, cinemáticas, éxito,
   fallo y balance, con la parte de cada criatura en su juego. Basiliskos lo estrena; King Kong
   puede pasar su campaña después.
4. **Pruebas comunes**: subir al paquete la base del `ConductorPrueba` (pasos, fotos a RenderTexture,
   recuento de eventos, piloto que sigue el marcador del objetivo) para que cada juego solo añada sus
   acciones.

## Después

- Lluvia y tormenta (tapan ruido y olor, oscurecen, suben los ríos) y viento que cambia.
- Vibración (las pisadas y los motores como ondas en el borde de la pantalla), útil para criaturas
  que sienten el suelo (Basiliskos, Jörmungandr, Behemot, Escorpión Colosal).
- Más humanos de Ferrox desde AssetsCreator (madereros, mineros, petroleros, pilotos, cazadores de
  criaturas, pueblos protegidos) con un `Def` por tipo en `ConstructorHumanos`, y armas (machete,
  escopeta, lanzarredes, escudo de espejo).
- Vehículos terrestres y acuáticos (todoterreno, excavadora, lancha, draga, barco) y un avión.
- Criaturas grandes que vuelan (Fénix, Grifo, Pájaro del Trueno, Roc) y que nadan (Leviatán,
  Kraken, Megalodón, Jörmungandr): cámara y control de vuelo/buceo en `CamaraCriatura`, agua
  abierta (océano) en `AguaMundo`, y biomas nuevos (desierto, tundra, volcán, ciudad) en el reparto
  de vegetación y las capas de terreno.
- Partida guardada común (hoy cada juego guarda la suya) y menú de inicio común.
- *Primal Legends* (el crossover): que varias `Criatura` convivan en una escena (cámara, HUD y
  humanos con varias criaturas, no solo `Criatura.Jugador`).
