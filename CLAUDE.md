# Cerebro de Gaia's Response — instrucciones comunes para todos los juegos

*Gaia's Response* es la saga de juegos de criaturas míticas de Odei Labs: la Tierra responde, y en
cada juego controlas a una criatura que despierta para defender la naturaleza de los humanos que la
destruyen (la corporación **Ferrox** y otros). Este repositorio (`Gaia's Response/cerebro`) es lo
común a todos los juegos. Léelo antes de tocar cualquier juego de la saga; después, el `CLAUDE.md`
del juego.

- [README.md](README.md) — qué hay en el cerebro, cómo arrancar un juego nuevo y la **guía de
  AssetsCreator** (qué se puede coger, cómo crear assets nuevos y cómo dejarlos para toda la saga).
- [ROADMAP.md](ROADMAP.md) — lo que falta en el cerebro.
- [Guardianes/CLAUDE.md](Guardianes/CLAUDE.md) — el paquete Unity `com.odeilabs.guardianes`
  sistema por sistema.
- `../README.md` — el mapa de la saga (todos los juegos y DLC).
- [Investigacion/Fluidez-y-sensacion.md](Investigacion/Fluidez-y-sensacion.md) — cómo hacer que
  moverse y atacar sea fluido y divertido (resumen en *Fluidez y sensación*, abajo).

## Dónde vive cada cosa

```
Odei Labs/
├── AssetsCreator/            generador de assets (NO se modifica su código; se usa lo que genera)
└── Gaia's Response/
    ├── cerebro/              ESTE repo: paquete Guardianes + instrucciones comunes
    ├── Gorilla Revenge/      juego 1, antes King Kong (repo, remoto github.com/odeilabs/KingKong)
    ├── Basiliskos/           juego 2 (repo, remoto github.com/odeilabs/Basiliskos)
    ├── <18 juegos futuros>/  de momento portada + README/ROADMAP con la propuesta
    └── DLCs/<18 DLC>/        expansiones, cada una propone su juego base
```

Cada juego con código: `<Juego>/<Proyecto Unity>/` y, junto al proyecto, `Tools/`, `Guion/`,
`Capturas/` (ignorada), `README.md`, `ROADMAP.md` y `CLAUDE.md`, y **su propia copia del paquete
común** (ver la regla siguiente).

## El cerebro es la fuente; cada juego lleva su copia

Lo que hay en el cerebro (sistemas, métodos, mecánicas, herramientas) es **la fuente de la que
beben los juegos, no algo de lo que dependan**. Cada juego implementa en local lo que coge:

- **Se copia a la carpeta del juego.** El build de un juego no puede necesitar la carpeta `cerebro`:
  todo lo que use del cerebro tiene que estar dentro de su repo. Así una versión concreta del juego
  no se rompe ni cambia por un cambio posterior en el cerebro, y se puede compilar con solo su repo.
- **Cómo**: el paquete va **incrustado** en el proyecto, en
  `<Proyecto>/Packages/com.odeilabs.guardianes/` (copia de `cerebro/Guardianes` **con sus `.meta`**,
  para conservar los GUID que usan las escenas), y en `manifest.json` no hay línea `file:` hacia el
  cerebro (Unity carga solo los paquetes que están en `Packages/`). Junto a la copia, `ORIGEN.md`
  con el commit del cerebro del que sale y lo que se trajo en cada sincronización.
- **Durante el desarrollo, el juego mira el cerebro todo el rato** por si hay cambios: comparar la
  copia con el cerebro (el commit de `ORIGEN.md` frente al actual) al empezar cada sesión de trabajo
  en un juego y antes de cada versión, y **traer a mano lo que interese**, probándolo en ese juego
  (compilar en batch y sus pruebas con fotos). Traer cambios es una decisión del juego, nunca
  automática; lo que no se trae no le afecta.
- **Lo nuevo común se hace en el cerebro** y después se trae a cada juego; si se arregla algo en la
  copia de un juego, se sube también al cerebro para que no se pierda. Lo propio de una criatura no
  se escribe dentro de la copia: va en el código del juego (subclases, eventos, sus assets de
  configuración), para que la copia se pueda refrescar sin pisar nada.
- Los sistemas del cerebro se escriben pensando en eso: **opcionales y configurables por juego**
  (perfiles, flags como `Humano.ConMiedo`, eventos), sin suponer las mecánicas de ninguna criatura,
  y teniendo en cuenta los juegos que aún no han empezado (vuelo, nado, varias cabezas, arcade).

Estado (octubre 2026): **los dos juegos llevan su copia** y ninguno enlaza ya el cerebro:
Gorilla Revenge desde `cca49a1` y Basiliskos desde `aa9c189` (más los cambios de `Humano.cs` que
aún no estaban commiteados); ver el `Packages/com.odeilabs.guardianes/ORIGEN.md` de cada uno. Desde
ahora, lo que se cambie en `cerebro/Guardianes` no llega a ningún juego hasta que se trae a su copia.

**Cómo se trae y se sube** (`Guardianes/Tools~/traer_cerebro.py`, desde la carpeta del juego):

```
python ../cerebro/Guardianes/Tools~/traer_cerebro.py estado            # qué ha cambiado y dónde
python ../cerebro/Guardianes/Tools~/traer_cerebro.py diferencias <ruta>
python ../cerebro/Guardianes/Tools~/traer_cerebro.py traer <rutas>     # o traer --todo
python ../cerebro/Guardianes/Tools~/traer_cerebro.py subir <rutas>     # un arreglo hecho en la copia
```

Compara fichero a fichero el cerebro, la copia y la foto de la última sincronización
(`.sincronizacion.json` junto a la copia; Unity no lo importa): lo que solo ha cambiado en el cerebro
se puede traer, lo que solo ha cambiado en la copia se debería subir, y lo que ha cambiado en los dos
es un conflicto que se mira a mano (`diferencias`; `--forzar` pisa). Las rutas son ficheros o
carpetas del paquete (`Runtime/Sensacion`) y los `.meta` van siempre con su fichero. Compara con lo
que hay en disco en el cerebro (también lo no commiteado); `--confirmado`, solo con lo commiteado.
`--simular` dice lo que haría. Cada sincronización se apunta sola en `ORIGEN.md`. No hace commits:
después se prueba el juego en batch y se commitea en su repo (y en el cerebro lo subido). Al abrir el
juego en el editor, `AvisoCerebro` dice en la consola si hay cambios por traer o por subir (menú
*Guardianes > Cerebro > Comprobar cambios*).

**Los ficheros nuevos del cerebro necesitan su `.meta`** (con un GUID nuevo): ningún proyecto de
Unity abre ya el cerebro, así que nadie lo genera solo. O se escribe a mano (copiando el de un
fichero del mismo tipo con otro GUID), o se crea el fichero en la copia de un juego, se deja que
Unity haga el `.meta` y se sube con `subir`.

## Reglas de trabajo (todas las sesiones, todos los juegos)

- **Español** en código, nombres, logs, comentarios, commits y documentación. Sin tildes en el
  texto del HUD si el juego no las usa (Gorilla Revenge no; Basiliskos sí).
- **Git desde PowerShell** (desde Git Bash falla al escribir objetos en algunos repos). Commits en
  español y **sin `Co-Authored-By`**. Mensajes largos: escríbelos en un fichero (fuera del repo, p.
  ej. en el directorio temporal) y `git commit -F <fichero>`; los here-strings chocan con el filtro
  de comandos.
- **No hacer `push` ni crear repos remotos sin preguntar.**
- **No modificar AssetsCreator** (`Odei Labs/AssetsCreator`): ni código ni configuración. Se usa
  lo que genera; los assets nuevos se generan con sus comandos y parámetros (ver la guía del
  README).
- **Verificar en Unity en batch con el editor cerrado** (si el editor está abierto, el proyecto está
  bloqueado y batch falla): `Tools/Unity-Batch.ps1 -Metodo <clase.Metodo>` y
  `Tools/Unity-Pruebas.ps1 -Guion <guion>` de cada juego. **Mira las fotos** de las pruebas (con
  la herramienta de leer imágenes): que compile y pase no basta.
- Antes de mover, borrar o sobrescribir algo, míralo. Las carpetas `Library/`, `Logs/`,
  `UserSettings/` y `Assets/_Recovery/` de los proyectos no se commitean.

## Qué va al cerebro y qué al juego

- **Lo que vaya a servir a más de un juego va al cerebro**, no a un juego: mundo (relieve, agua,
  vegetación, día y noche), humanos de Ferrox y su IA (detección, miedo), instalaciones, vehículos,
  combate, efectos, sonido, cámara, HUD base, misiones (director, objetivos, reglas), pruebas,
  herramientas de editor y generadores de arte.
- **Lo de una criatura concreta va en su juego**, colgado de `Guardianes.Criaturas.Criatura`, de
  los eventos estáticos o de los métodos virtuales (`Premiar`, `Celebrar`, `HudJuego.ConstruirEstado`...).
  Si un sistema empieza en un juego y luego lo necesita otro, se sube al cerebro con cuidado de
  conservar los GUID de los componentes que ya usan las escenas.
- Un cambio en el cerebro se comprueba compilando en batch en al menos un juego; cada juego lo
  prueba a fondo cuando lo trae a su copia. Como ningún juego enlaza el cerebro, un cambio
  se comprueba trayéndolo a la copia de un juego (o a un proyecto de prueba) y compilando allí. Los comportamientos nuevos que cambian el
  juego (p. ej. el miedo de los humanos) se activan por juego para no alterar a los demás.
- Enumeraciones que se guardan en partidas o datos (`TipoZona`, `MotivoPremio`...): los valores
  nuevos van **al final**.

## Convenciones del paquete y de los juegos

- Escala real: 1 unidad = 1 m. Mundos centrados en el origen (x este, z norte). La referencia de
  tamaño de la saga es Koba (antes Kong) de pie (`Criatura.TamanoReferencia` = 17,5 m): todo lo que depende del
  tamaño escala con `Criatura.Tamano`.
- **Nada pesado en los repos**: terreno y vegetación se guardan compactos (`.bytes` en LFS) y se
  reconstruyen al cargar a partir de semillas; texturas y sonidos se generan con los scripts de
  `Tools~`/`Tools`. Los binarios van por **Git LFS desde el primer commit** (ver `.gitattributes`
  de Basiliskos como plantilla).
- Ensamblados por juego: `<Juego>` (runtime, referencia `Guardianes`), `<Juego>.Editor`
  (referencia además `Guardianes.Editor`) y `<Juego>.Pruebas.Juego` (pruebas PlayMode). Namespace
  raíz = nombre del juego; el editor en `<Juego>.EditorTools`.
- Logs con prefijo: `[GD]` el paquete, `[GR]` Gorilla Revenge (antes `[KK]`), `[BK]` Basiliskos; cada juego nuevo
  elige el suyo (dos letras).
- Shaders: las variantes de HDRP/Lit del paquete se generan con
  `Guardianes/Tools~/generar_shaders_lit.py`; **no se editan** los `.shader` ni los `Data.hlsl`
  generados. Globales de shader con prefijo `_GD_`.
- Materiales sobre Lit con `Guardianes.EditorTools.MaterialesLit.Crear`.
- HDRP "ultra" con `ConfiguradorHDRP`; las capturas fuera del modo juego no tienen viento ni
  skinning: para ver criaturas y humanos moverse, pruebas en modo juego.

## Fluidez y sensación (moverse y atacar tiene que ser divertido)

Todo lo que controla el jugador tiene que sentirse bien al primer toque y dar ganas de repetirlo.
Detalle, valores de partida y fuentes en [Investigacion/Fluidez-y-sensacion.md](Investigacion/Fluidez-y-sensacion.md).

- **Responde ya**: menos de 100 ms del input a una reacción visible. La animación sirve al control,
  nunca al revés; el peso de las criaturas gigantes va en la presentación (cámara, polvo, sonido, lo
  que se rompe), no en un control lento.
- **Nada se pierde ni se atasca**: buffer de entrada (100–200 ms), coyote time (80–150 ms), ayudas
  invisibles hacia lo que el jugador quería, y ninguna acción sin salida (cancelar la recuperación en
  otro ataque, en esquiva o en movimiento).
- **Cada acierto se nota con varias capas a la vez** (los primeros ~100 ms): hitstop proporcional a
  la fuerza (una vez por impacto), sacudida de cámara por trauma², reacción del golpeado, sonido en
  capas, destello y partículas, vibración. Todo escala con la fuerza y con `Criatura.Escala`; lo
  rutinario (pisadas) se nota poco para que lo importante se note mucho.
- **El golpe cambia el mundo**: romper, tumbar, lanzar, petrificar, asustar. Destrucción en cadena.
- **Recompensas a tres escalas** (segundos, minutos, sesión) y siempre una a la vista; variación en
  los resultados (críticos, cadenas, reacciones distintas), nunca cajas de botín.
- **Crecer se nota**: cada etapa o habilidad nueva cambia cámara, sonido y lo que se rompe; los
  enemigos nuevos llegan con el poder nuevo para que nada se vuelva trivial.
- **La maestría se premia** (emboscadas, combos, remates, encadenar sin recibir daño) y **fallar es
  barato** (reintentar rápido y cerca).
- Cada mecánica nueva pasa la lista de comprobación del informe y se juega con mando y teclado,
  además de las pruebas en batch.

## Verificar (cómo son las pruebas de la saga)

- `Tools/Unity-Batch.ps1 -Metodo <Juego>.EditorTools.<Clase>.<Metodo>DesdeBatch [-Extra ...]`:
  construir la escena, capturas, previsualizar el relieve. Para solo abrir y compilar:
  `-Metodo Guardianes.EditorTools.Diagnostico.ComprobarDesdeBatch` (compila los shaders propios).
- `Tools/Unity-Pruebas.ps1 -Guion <guion> [-Capturas <carpeta>]`: PlayMode en batch. Un
  `ConductorPrueba` conduce a la criatura con entradas simuladas según un guion de pasos
  (`GuionesPrueba`), hace fotos con la cámara del juego (a una RenderTexture: en batch no hay
  pantalla), cuenta lo que pasa y la prueba NUnit falla si no pasa lo que el guion pide. Las fotos
  quedan en `-Capturas` (por defecto `%TEMP%\<prefijo>_juego_<guion>`), el log y el XML en `%TEMP%`.
- En batch: `runInBackground`, `WaitForEndOfFrame` no vuelve nunca, audio en silencio, sin
  desenfoque de movimiento. Unity a veces sale con 0xC0000005 tras guardar los resultados: vale lo
  que diga el XML.
- `python Guardianes/Tools~/hoja_contactos.py <carpeta de fotos>` junta las fotos en una hoja.

## Documentación

Cada proyecto tiene `README.md` (qué es, estado, cómo se juega/compila) y `ROADMAP.md` (hecho,
siguiente, después). Los juegos con código, además, `CLAUDE.md` (estructura, reglas de diseño,
cómo verificar). Cuando cambies algo que afecte a esos documentos, actualízalos en el mismo commit.
Los juegos que solo tienen portada llevan una **propuesta** marcada como tal: es un punto de
partida, no una decisión.
