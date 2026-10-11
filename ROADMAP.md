# Cerebro de Gaia's Response — Roadmap

Lo que falta en lo común de la saga (paquete `com.odeilabs.guardianes` e instrucciones). Lo de cada
juego está en el `ROADMAP.md` de su carpeta; el mapa de la saga, en `../README.md`.

## Hecho (octubre 2026)

- Paquete `com.odeilabs.guardianes` extraído de King Kong (hoy Gorilla Revenge) y usado por Gorilla Revenge y Basiliskos:
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
- **Miedo de los humanos** (activable por juego con `Humano.ConMiedo`; Basiliskos lo usa, Gorilla
  Revenge no): miedo de 0 a 100, contagio, disparar a las sombras, quedarse helado, huir para no volver;
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
- **Gorilla Revenge (antes King Kong) con su copia del paquete** en `Packages/com.odeilabs.guardianes/`
  (desde `cca49a1`, con `ORIGEN.md`): ya no enlaza el cerebro; compila y pasa sus pruebas en batch.
- **Basiliskos con su copia del paquete** (desde `aa9c189` más los cambios de `Humano.cs` sin
  commitear entonces en el cerebro, con `ORIGEN.md`): compila y pasa la prueba `marcha`. Ningún juego
  enlaza ya el cerebro.
- **Herramienta para traer y subir cambios** (`Tools~/traer_cerebro.py`: estado, diferencias, traer,
  subir, con la foto `.sincronizacion.json` de cada copia y los `.meta` siempre con su fichero) y
  **aviso en el editor** de cada juego (`AvisoCerebro`) cuando hay cambios por traer o por subir.
- **Lluvia** (`Lluvia`, con el cielo cubierto en `CicloDia.nublado`): gotas alrededor de la cámara,
  menos luz para la IA y `Lluvia.Intensidad` para cada juego (Basiliskos la estrena en la noche de las
  Comadrejas: tapa el olor a los perros y ciega las fosetas). Y **lo que tapa la vista** a los humanos
  (`Humano.Visibilidad`) con la **visión térmica** de visor (solo de frente).
- **Regla de la copia local** (ver [CLAUDE.md](CLAUDE.md#el-cerebro-es-la-fuente-cada-juego-lleva-su-copia)):
  el cerebro es la fuente y cada juego lleva su propia copia del paquete, para que su build no
  necesite el cerebro y los cambios posteriores no toquen una versión concreta.

## Siguiente

1. **Probar un build de cada juego sin la carpeta `cerebro` al lado.**
2. **Aplicar la sensación común**, en local en cada juego (traída a su copia y llamada desde su
   código, con su perfil; cuando lo diga el autor: Basiliskos está en desarrollo): Gorilla Revenge con un
   perfil `Gigante` (combo, palmada, lanzar, remates a helicópteros) y Basiliskos con `Creciente`
   (mordisco con buffer, emboscada, estrangular, sprint que rompe), `TraumaCamara` en sus cámaras y
   `Medidor.Informe()` en sus pruebas. Después, el sonido en capas sobre `AlImpacto` y la destrucción
   en cadena en `Estructura` y `Humano`.
3. **Pasar la campaña de Gorilla Revenge al director común** (sus objetivos propios como subclases) y
   probar en su modo libre el día y la noche y el miedo.
4. **Pruebas comunes**: subir al paquete la base del `ConductorPrueba` (pasos, fotos a RenderTexture,
   recuento de eventos) y del piloto que juega las misiones siguiendo el objetivo, para que cada juego
   solo añada sus acciones.
5. **El HUD en las fotos de las pruebas**: en batch el lienzo va en *Screen Space - Camera* y pasa por
   el posproceso de HDRP (exposición, grano, bloom): el texto sale tenue. Pintarlo después del
   posproceso.
6. Estrellas y luna con textura en el cielo físico de noche; nubes que se oscurecen.

## Después

- La tormenta completa (ya hay `Lluvia`): que tape el ruido, que suban los ríos, charcos y suelo
  mojado, relámpagos, nubes que se oscurecen; y viento que cambia.
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
