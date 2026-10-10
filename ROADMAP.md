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
- **Día y noche** (`CicloDia`, `FuenteLuz`): sol por latitud y declinación, luna, exposición de
  noche, la luz de cada punto para la IA (cielo más hogueras, faroles y linternas).
- **Miedo de los humanos** (activable por juego con `Humano.ConMiedo`; Basiliskos lo usa, King Kong
  no): miedo de 0 a 100, contagio, disparar a las sombras, quedarse helado, huir para no volver;
  `MarcaLeyenda`. `Humano` gana `quieto` (centinelas) y `ruta` (rondas), y los protegidos ya no
  acosan a una criatura pequeña.
- **Director de misiones común** (`Guardianes.Misiones`): misiones con introducción, objetivos,
  diálogos, reglas, desenlace, balance, fallo y repetición. Lo estrena el Acto I de Basiliskos.

- **Sensación común, la base** (`Guardianes.Sensacion`, ver
  [Investigacion/Fluidez-y-sensacion.md](Investigacion/Fluidez-y-sensacion.md)): perfiles por
  arquetipo (gigante, ágil, creciente, volador, acuático, arcade), `Sensaciones.Impacto` (parada,
  trauma de cámara, temblor del golpeado, destello, vibración), `TraumaCamara`, `BufferEntrada`,
  `MargenApoyo`, `AsistenciaPunteria` y `Medidor`, con su comprobación en batch. Opcional: ningún
  juego lo usa todavía.

## Siguiente

1. **Aplicar la sensación común** (cuando lo diga el autor: Basiliskos está en desarrollo): King
   Kong con un perfil `Gigante` (combo, palmada, lanzar, remates a helicópteros) y Basiliskos con
   `Creciente` (mordisco con buffer, emboscada, estrangular, sprint que rompe), `TraumaCamara` en sus
   cámaras y `Medidor.Informe()` en sus pruebas. Después, el sonido en capas sobre `AlImpacto` y la
   destrucción en cadena en `Estructura` y `Humano`.
2. **Pasar la campaña de King Kong al director común** (sus objetivos propios como subclases) y
   probar en su modo libre el día y la noche y el miedo.
3. **Pruebas comunes**: subir al paquete la base del `ConductorPrueba` (pasos, fotos a RenderTexture,
   recuento de eventos) y del piloto que juega las misiones siguiendo el objetivo, para que cada juego
   solo añada sus acciones.
4. **El HUD en las fotos de las pruebas**: en batch el lienzo va en *Screen Space - Camera* y pasa por
   el posproceso de HDRP (exposición, grano, bloom): el texto sale tenue. Pintarlo después del
   posproceso.
5. Estrellas y luna con textura en el cielo físico de noche; nubes que se oscurecen.

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
  abierta (océano) en `AguaMundo`, y biomas nuevos (desierto, tundra, volcán, bosque templado) en el
  reparto de vegetación y las capas de terreno.
- Varias cabezas (Cerbero, Hidra, Tifón: la espina procedural de Basiliskos sirve para los cuellos),
  fuego que se propaga por la vegetación, lava y terreno que cambia en juego, raíces que crecen.
- Partida guardada común (hoy cada juego guarda la suya) y menú de inicio común.
- *Primal Legends* (el crossover): que varias `Criatura` convivan en una escena (cámara, HUD y
  humanos con varias criaturas, no solo `Criatura.Jugador`).
