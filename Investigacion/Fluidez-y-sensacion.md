# Fluidez y sensación: moverse y atacar tiene que ser divertido

Informe de investigación del cerebro de *Gaia's Response* (octubre de 2026). Recoge las mecánicas
que mejor funcionan para que **moverse sea fluido** y **atacar se sienta bien** (lo que genera
dopamina), y cómo aplicarlas a criaturas gigantes y a criaturas que crecen. Es la referencia común:
cada juego la adapta a su criatura. Las reglas cortas están en `../CLAUDE.md` (*Fluidez y sensación*).

## 1. De dónde sale la dopamina en un juego de acción

La dopamina no premia el placer en sí, sino **la sorpresa positiva**: que pase algo un poco mejor de
lo esperado (*error de predicción de la recompensa*). En la práctica:

- **Respuesta inmediata a cada acción.** El jugador pulsa y *algo* pasa ya (menos de 100 ms). Lo
  inmediato es lo que el cerebro asocia a la acción; lo que llega tarde no cuenta.
- **Confirmación rica del acierto.** Un golpe que acierta suena, se ve, se nota en la cámara y en el
  enemigo. Varias respuestas pequeñas apiladas en los primeros ~100 ms se leen como *impacto*.
- **Bucles de recompensa a tres escalas**: cada pocos segundos (golpe, presa, rotura), cada pocos
  minutos (objetivo, botín, nivel) y cada sesión (muda, forma nueva, zona liberada). Siempre tiene
  que haber una recompensa cerca a la vista.
- **Variabilidad.** Si todo da lo mismo, aburre. Variaciones en el resultado (un golpe crítico, una
  reacción en cadena, una rotura espectacular, un enemigo que sale volando distinto) mantienen la
  sorpresa. Recompensas variables sí, **cajas de botín y pagos no** (la saga es de pago único).
- **Crecer y notarlo.** La fantasía de poder funciona cuando el jugador *ve* que es más fuerte que
  antes: lo que antes costaba ahora se rompe de un golpe. Hay que enseñar el antes y el después.
- **Maestría.** El jugador hábil debe sentir que lo es: ventanas de tiempo, combos, cancelaciones y
  emboscadas que premian hacerlo bien con algo más que daño (estilo, velocidad, botín, afinidad).

## 2. Moverse con fluidez

La regla de oro: **el control lo tiene el jugador, la física decora**. La inercia y el peso se
*sienten*, pero nunca le quitan la respuesta.

| Mecánica | Qué es | Valores de partida |
| --- | --- | --- |
| Respuesta al pulsar | El primer fotograma de movimiento o de animación sale el mismo fotograma (o el siguiente) | < 50 ms del input a la reacción visible |
| Aceleración corta | Llegar a la velocidad máxima pronto; frenar todavía más pronto | 0,1–0,3 s a velocidad máxima; frenar en la mitad |
| Giro rápido | Girar en el sitio o con radio pequeño; las criaturas grandes giran el **cuerpo** despacio pero la **cabeza y la cámara** al momento | Cabeza/apuntado instantáneos; cuerpo con retraso |
| *Buffer* de entrada | Si se pulsa poco antes de poder hacerlo, la acción se guarda y sale en cuanto se puede | 100–200 ms |
| *Coyote time* | Saltar o lanzarse justo después de perder el apoyo todavía vale | 80–150 ms |
| Ayudas invisibles | Corregir un poco hacia lo que el jugador claramente quería: bordes, troncos, rampas, presas | Imán suave, nunca robar el control |
| Encadenar movimientos | Cada movimiento puede salir de otro sin parar: correr → saltar → trepar → caer en ataque | Ningún "estado muerto" sin salida |
| Movimiento con recompensa | Moverse bien da algo: velocidad que se acumula, atajos, emboscadas, romper la selva al correr | El sprint que rompe la selva de Basiliskos es un ejemplo |
| Cámara que acompaña | Se adelanta hacia donde va el jugador, abre el FOV al correr, se aparta de los obstáculos sin saltos | FOV +5–10° al correr; suavizado crítico, nunca lineal |
| Animación al servicio del control | La animación se adapta al input (mezclas cortas, IK de pies y manos, inclinación en los giros); el input nunca espera a la animación | Mezclas de 0,1–0,2 s |

**Peso sin lentitud (criaturas gigantes).** Koba, el basilisco o Behemot tienen que *parecer*
enormes sin *sentirse* torpes. Funciona:

- **Peso en la presentación, no en el control**: pisadas que hacen temblar la cámara y levantan polvo,
  árboles que se doblan al pasar, humanos diminutos que huyen, sonido grave, cámara baja y lejana.
- **Anticipación corta y recuperación cancelable**: un golpe enorme puede tardar en llegar (eso es
  peso), pero el jugador puede cancelar la recuperación con otro movimiento.
- **Escala en todo**: velocidades con la raíz del tamaño (como la ley de Froude que ya usa
  Basiliskos), cámara y efectos con `Criatura.Escala`.
- **Mover el mundo, no solo la criatura**: lo que se rompe al pasar es la mejor prueba de tamaño.

**Pequeño y vulnerable (la cría, sigilo).** Lo divertido es la tensión: moverse rápido y bajo,
esconderse al instante (una tecla, sin animaciones largas), notar claramente si te ven (el `?` y el
`!` del HUD de Basiliskos) y tener siempre una salida.

## 3. Atacar y que se sienta bien (*game feel* del golpe)

Un golpe tiene tres partes: **anticipación** (avisa de lo que viene), **contacto** (el instante del
impacto) y **recuperación** (la vuelta). La sensación se construye sobre todo en el contacto.

### En el instante del impacto (los primeros 100 ms)

1. **Hitstop (congelar).** Atacante y golpeado se paran unos fotogramas al tocar; cuanto más fuerte el
   golpe, más tiempo. Es la técnica más eficaz y la más barata (Sakurai abre su serie de vídeos con
   ella: *Stop for Big Moments!*). El golpeado tiembla durante la parada y el temblor se va apagando.
   Valores: 0,03–0,08 s golpes normales, 0,1–0,2 s golpes fuertes, más en el golpe final de un jefe
   (*boss stop*). Solo una vez por impacto, nunca en golpes continuos.
2. **Sacudida de cámara por trauma.** Cada impacto suma "trauma"; la cámara tiembla con trauma² (ruido
   Perlin, no aleatorio puro) y el trauma baja solo. Así los golpes pequeños apenas se notan y los
   grandes se sienten mucho, y varios seguidos se acumulan sin volverse locos. Opción en el menú para
   reducirla.
3. **Reacción del golpeado.** Retroceso en la dirección del golpe, tambaleo, salir volando con física
   si el golpe es grande (Gorilla Revenge ya lo hace), estatua, huida. **Nunca un golpe sin reacción.**
4. **Sonido en capas.** Un golpe = chasquido agudo (el contacto) + cuerpo grave (el peso) + cola
   (eco, escombros). Variar tono y muestra en cada golpe. El sonido es la mitad de la sensación.
5. **Destello y partículas.** Un fotograma de destello en el golpeado, chispas, polvo, astillas,
   escombros en la dirección del golpe. Más grandes cuanto más fuerte.
6. **Vibración del mando** proporcional al golpe.
7. **Números o marcas** solo si ayudan (los jefes con barra de vida: que la barra baje con un retardo
   visible, como la vida de los HUD de la saga).

### Alrededor del golpe

- **Ventanas de cancelación**: después del contacto, el ataque se puede cancelar en otro ataque, en
  esquiva o en movimiento. Encadenar es lo que da fluidez al combate.
- **Combos simples y legibles**: 3–4 golpes con un remate; variaciones con la dirección o el momento,
  no con secuencias largas de botones.
- **Asistencia de puntería suave**: el golpe se orienta un poco hacia el enemigo más cercano dentro
  de un cono (*soft lock*); los ataques en salto buscan al blanco. El jugador apunta "más o menos" y
  acierta.
- **El golpe tiene que cambiar el mundo**: romper, tumbar, lanzar, petrificar. La destrucción en cadena
  (un depósito que explota y tumba un barracón) es la recompensa más espectacular para una criatura
  gigante.
- **Agarrar y lanzar** (Koba), **constreñir** (la serpiente): ataques que convierten a un enemigo en
  arma o en espectáculo.
- **Ritmo de tensión y descarga**: momentos de amenaza (helicópteros, espejos, cazadores) seguidos de
  momentos de poder absoluto (la Ira de la selva de Koba, la furia de la serpiente). La barra que se
  llena al luchar y se gasta en un ataque enorme es un bucle de dopamina probado.
- **Recompensa por el estilo**: emboscadas que hacen el doble (Basiliskos), golpes desde el aire,
  remates especiales (una animación corta de remate que da vida o furia), multiplicadores por
  encadenar sin recibir daño. El combate empuja a atacar, no a esconderse.
- **Enemigos que comunican**: aviso claro antes de que disparen o ataquen (destello, sonido, pausa),
  para que esquivar sea habilidad y no suerte.

## 4. Progresión que engancha (sin trampas)

- **Crecer se ve y se siente**: tamaño, sonido, cámara y lo que se rompe cambian con cada etapa; un
  enemigo que antes era peligroso ahora sale volando. Volver a una zona vieja tiene que ser una
  fiesta de poder.
- **Mudas, formas y habilidades como hitos**: cada una con su momento (pantalla de etapa, cámara,
  sonido, la habilidad nueva enseñada al instante en una situación que la pide).
- **Siempre algo que conseguir cerca**: presas, roturas, lugares, zonas. Ver el siguiente objetivo
  desde donde estás.
- **Curva de dificultad que acompaña al poder**: lo que critican de *Maneater* es que a mitad del
  juego todo se vuelve trivial. Los enemigos nuevos (helicópteros, espejos, cañones de canto) tienen
  que llegar a la vez que el poder nuevo.
- **Fallar barato**: reintentar rápido desde cerca, sin pantallas largas (el director de misiones ya
  repite desde el principio de la misión: mejor aún, desde el último objetivo).

## 5. Cómo aplicarlo en la saga

**En el cerebro (para todos los juegos)**, lo que conviene construir:

1. Un componente de **impacto común** (`Guardianes.Combate`): hitstop por fuerza del golpe (con
   `Time.timeScale` local o congelando las animaciones del atacante y del golpeado), trauma de cámara
   en `CamaraCriatura`, destello, vibración del mando y el sonido en capas de `SonidoJuego`, todo en
   una llamada desde `Impacto`/`Salud` con la fuerza del golpe y la escala de la criatura.
2. **Buffer de entrada y coyote time** en un ayudante común para las entradas de cada criatura.
3. **Asistencia de puntería** (cono hacia el blanco más cercano) para los ataques de cuerpo a cuerpo.
4. **Destrucción en cadena** y reacciones físicas en `Estructura` y `Humano`.
5. **Medir la sensación en las pruebas en batch**: latencia del input a la reacción, tiempo entre
   recompensas (presas, roturas, avisos) y tiempo muerto sin control, escritos en el log de cada
   prueba como un indicador más.

**Hecho (octubre 2026): `Guardianes.Sensacion`**, la base de los puntos 1, 2, 3 y 5, **sin aplicar
todavía a ningún juego**. Es para **implementaciones locales en cada proyecto**: como todo el cerebro,
es la fuente de la que cada juego copia lo que usa a su propia copia del paquete (su build no
necesita el cerebro y los cambios posteriores aquí no tocan una versión concreta), y lo llama desde su
código. No se engancha sola a `Impacto`, `Salud` ni `CamaraCriatura`: cada juego decide qué es un
golpe y cuánto pesa según sus mecánicas.

- `PerfilSensacion` (ScriptableObject): parada, trauma, sacudida, destello y vibración según la
  fuerza del golpe (0 roce, 1 fuerte, más de 1 remate). `Crear(arquetipo)` da un punto de partida.
- `Sensaciones.Impacto(Golpe)`: parada local (Animator y `EstaParado` del atacante y el golpeado) o
  global (`timeScale`, respetando la pausa y `EscalaTiempoBase`), temblor del golpeado, trauma a las
  cámaras por distancia en tamaños de la criatura, destello y vibración; `AlImpacto` para los sonidos
  y partículas de cada juego. `Temblor(punto, trauma)` para pisadas y explosiones. `Opciones` de
  accesibilidad (sacudida, paradas, vibración, destellos).
- `TraumaCamara`: se pone en cualquier cámara (seguir, vuelo, buceo) y tiembla con trauma² y Perlin.
- `BufferEntrada` y `MargenApoyo` (coyote time para cualquier apoyo: suelo, rama, pared, agua).
- `AsistenciaPunteria`: el mejor blanco en un cono (2D o 3D) y la corrección de la dirección.
- `Medidor`: latencia, tiempo entre recompensas, tiempo sin control e impactos (`Informe()`).
- Comprobación: `Guardianes.EditorTools.PruebaSensacion.ComprobarDesdeBatch`.

| Arquetipo | Para quién | Carácter |
|---|---|---|
| `Gigante` | Gorilla Revenge (Koba), Behemot, Cerbero, Escorpión Colosal, Smilodon (adulto) | paradas largas, sacudida grande y grave |
| `Agil` | Noctarion, Fenrir, Manticora, la cría de Basiliskos | paradas cortas y nítidas, sacudida rápida y pequeña |
| `Creciente` | Basiliskos, Raizmuerta (lo que crece en la partida) | intermedio: lo que cambia lo pone `Criatura.Escala` |
| `Volador` | Fénix, Grifo, Pájaro del Trueno, Tifón | poca sacudida (la cámara ya se mueve mucho) |
| `Acuatico` | Leviatán, Jörmungandr, Hidra en el agua | sacudida lenta y amortiguada, vibración grave |
| `Arcade` | Quimera Flip, Qilin | lo mínimo para no tapar la lectura del juego |

Son puntos de partida: cada juego crea su perfil (un asset) y lo ajusta, y puede cambiarlo en juego
(Basiliskos, por etapas; una hidra, por cabeza; un ave, al picar). Lo propio de cada criatura (qué
golpe es un remate, el "apretar" del estrangular, el picado, la embestida desde el agua) queda en su
juego. Cuando se aplique: Gorilla Revenge con `Gigante` y Basiliskos con `Creciente`, con `TraumaCamara` en
`CamaraKoba`/la cámara de la serpiente y `Medidor.Informe()` al final de sus pruebas.

**Gorilla Revenge**: hitstop y trauma en el combo y la palmada; agarrar y lanzar con cámara lenta corta al
soltar; remates contra helicópteros; cancelar la recuperación de golpes grandes en salto o embestida.

**Basiliskos**: la emboscada como gran momento (hitstop largo, cámara que se acerca, sonido seco); el
mordisco con buffer (que salga aunque se pulse un poco antes de acabar el anterior); el estrangular
con un "apretar" rítmico que se nota; el sprint que rompe la selva como recompensa del movimiento; la
muda como momento estrella; los humanos que huyen como confirmación del miedo.

**Juegos futuros**: vuelo (Fénix, Grifo, Pájaro del Trueno) con picados que aceleran y salidas de
picado en ataque; nado (Leviatán, Jörmungandr) con saltos fuera del agua como gran impacto; varias
cabezas (Hidra, Cerbero) con un golpe por cabeza en ritmo.

## 6. Lista de comprobación de cada mecánica

- [ ] ¿Responde en menos de 100 ms al pulsar?
- [ ] ¿Se puede encadenar con otra cosa sin parar? ¿Tiene salida (cancelar, esquivar)?
- [ ] ¿Se guarda el input si se pulsa un poco antes?
- [ ] ¿El acierto tiene al menos cuatro respuestas (sonido, parada, cámara, reacción, partículas)?
- [ ] ¿La fuerza de las respuestas escala con la fuerza de la acción y el tamaño de la criatura?
- [ ] ¿Cambia algo en el mundo?
- [ ] ¿Hay una recompensa a la vista en los próximos 30 segundos?
- [ ] ¿El jugador hábil lo hace mejor y lo nota?
- [ ] ¿Fallar es barato?
- [ ] ¿Se ha probado en batch con fotos y, sobre todo, jugando con mando y teclado en el editor?

## Fuentes

- Masahiro Sakurai, *Masahiro Sakurai on Creating Games*: «Stop for Big Moments!» (hitstop, *boss stop*)
  y «Eight Hit Stop Techniques». Cobertura: [Automaton](https://automaton-media.com/en/news/20220824-15208/),
  [EventHubs](https://eventhubs.com/news/2022/aug/24/masahiro-sakurai-youtube-channel-games),
  [Nintendo Wire](https://nintendowire.com/?p=180395).
- Steve Swink, *Game Feel: A Game Designer's Guide to Virtual Sensation* (2008): respuesta en tiempo
  real, simulación y pulido.
- Jan Willem Nijman (Vlambeer), *The Art of Screenshake* (2013), y Martin Jonasson y Petri Purho,
  *Juice it or lose it* (GDC Europe 2012): capas de respuesta sobre una mecánica sencilla.
- Squirrel Eiserloh, *Math for Game Programmers: Juicing Your Cameras With Math* (GDC 2016): sacudida
  por trauma² con ruido Perlin.
- Maddy Thorson y el equipo de *Celeste*: coyote time, buffer de salto y ayudas invisibles al control.
- *Doom Eternal* (id Software): combate que empuja hacia delante (los remates dan recursos).
- Coyote time y buffer: [devlog de Tooster](https://tooster.itch.io/chapter-01/devlog/560384/input-buffering-coyote-time).
- Capas de respuesta en los primeros 100 ms y límites del hitstop y la sacudida:
  [Cursa, *Game Feel*](https://cursa.app/hi/article/game-feel-how-to-make-your-game-controls-feedback-and-movement-instantly-satisfying).
- Crecimiento por comida en *Maneater* y su problema de ritmo: [CGM](https://www.cgmagonline.com/reviews/maneater-review/),
  [Gameluster](https://gameluster.com/review-maneater-a-fintastic-voyage/).
- Destrucción y crecimiento atado a objetivos repetitivos en *Godzilla: Destroy All Monsters Melee*:
  [Wikipedia](https://en.wikipedia.org/wiki/Godzilla:_Destroy_All_Monsters_Melee), [GameSpot](https://www.gamespot.com/videos/godzilla-video-review/2300-6426075).

Las cifras de las tablas son valores de partida habituales en el oficio, no medidas de un juego
concreto: hay que ajustarlas jugando cada criatura.
