# Guardianes — paquete común de la saga

Paquete de Unity (`com.odeilabs.guardianes`) con lo que comparten los juegos de la saga **Gaia's
Response** de Odei Labs: *Gorilla Revenge — El guardián de la isla* (antes *King Kong*), *Basiliskos* y los que vengan (y el
crossover final, *Primal Legends*). Vive en el cerebro de la saga (`Gaia's Response/cerebro`, ver
`../CLAUDE.md` para las reglas comunes y `../README.md` para la guía de AssetsCreator).

**Esto es la fuente, no una dependencia**: cada juego lleva su propia copia de este paquete en
`<Proyecto>/Packages/com.odeilabs.guardianes/` (con los `.meta`, y un `ORIGEN.md` con el commit del
cerebro del que sale), para que su build no necesite el cerebro y los cambios de aquí no toquen una
versión ya hecha. Durante el desarrollo, cada juego mira aquí si hay cambios y trae a su copia lo que
le interese, probándolo. Por eso todo lo de aquí es opcional y configurable por juego, sin suponer
las mecánicas de ninguna criatura. (Gorilla Revenge y Basiliskos ya llevan la suya.)

Unity 6000.5.2f1, HDRP 17.5, Input System 1.19, Burst + Collections + Mathematics. Código y nombres
en español. Ensamblados `Guardianes` (runtime) y `Guardianes.Editor`.

## Qué hay

- `Runtime/Mundo/` — `MedidasMundo` (lado, teselas, resoluciones, rango de alturas, mar), `Mapa`
  (consultas para todos: altura, agua, profundidad, tierra firme, normal; `Crecida`: lo que suben los ríos y los lagos
  —la lluvia, la pone cada juego—, el mar no), `DatosMundo` (el mundo
  compacto en `.bytes`: alturas, máscaras, **mapa de agua**, ríos, lagos, rasgos y máscaras extra
  con nombre; versión 1 = la de la isla de Koba, sin medidas ni agua), `TerrenoMundo` (levanta los
  Terrain y pinta 8 capas con Burst; `Purificar`), `AguaMundo` (lagos con WaterSurface, cintas de
  río, cascadas; todo sube con `Mapa.Crecida`), `Rios` (trazado denso con meandros a partir de puntos de control), `Lago`,
  `Zona`/`TipoZona` (los valores son bits de las partidas guardadas: los nuevos van al final),
  `Ruido` (fBm, crestas, hash, para Burst). **Día y noche**: `CicloDia` mueve el sol según la hora
  con la latitud y la declinación del sitio (sigue encendido hasta ~6° bajo el horizonte: el cielo
  físico hace el crepúsculo), crea la luna (direccional; HDRP solo da sombra a una: la luna cuando no
  hay sol) y un relleno de cielo nocturno desde arriba sin sombras (sin él, bajo el dosel la noche es
  negra), cambia los límites y la compensación de la exposición de noche con un Volume propio (de
  noche puede oscurecer mucho para que una hoguera de cerca no queme la imagen) y da
  a los humanos la luz de cada punto (`Humano.Luz = CicloDia.Luz`: el cielo más las fuentes);
  `FijarHora`, `avanzar`, `minutosPorDia`, `EsDeNoche`, `Dia`, `Reloj`. Sin un `CicloDia` en la
  escena todo sigue siendo de día. `FuenteLuz`: luces que cuentan para la IA (hogueras, faroles,
  linternas con cono, focos): `Aporte`/`Total`, `Apagar` (asusta a los de alrededor), parpadeo de llama. **Lluvia** (opcional:
  la pone cada juego o cada misión con `Lluvia.Poner(intensidad, segundos)`): gotas alrededor de la
  cámara (bajo el agua no), el cielo cubierto (`CicloDia.nublado`: menos sol, luna, luz para la IA y
  color) y `Lluvia.Intensidad` (0..1) para lo que cada juego quiera hacer con ella (tapar el olor,
  cegar un sentido).
- `Runtime/Vegetacion/` — `Viento` (+ interactores), `EspecieVegetal`, `Generacion/Plantas`
  (ceiba, dosel, palmera, helecho arbóreo, platanera, bambú, arbusto, helecho, hojas grandes,
  hierba, flores, roca, tronco y herbazal —un trozo de hierba alta cerrada: varias matas en un círculo de algo más de
  un metro, que plantadas a 1,7 m no dejan ver el suelo; las formas nuevas van al final del enum, los assets guardan
  el número—), `RenderizadorVegetacion` (instancing por cámara con culling/LOD en
  Burst; `Buscar`, `Derribar`), `Derribos`, `DatosVegetacion`.
- `Runtime/Criaturas/Criatura` — **la criatura del jugador** (Koba, el basilisco...). Los humanos,
  los helicópteros, la cámara, los efectos, el sonido y el HUD la buscan aquí (`Criatura.Jugador`)
  y escalan con `Tamano` (17,5 m = Koba = escala 1) y `AlturaBlanco`. Eventos para los demás:
  `Intimidar` (los humanos huyen, los helicópteros cercanos pierden el control), `OndaCono`,
  `Purificar`, `Pisar`. `Premiar` (cada juego decide qué da: furia, crecimiento, afinidad) y
  `Celebrar`. `IEntradaCamara` para que la cámara lea los controles de cada juego.
- `Runtime/Combate/` — `Salud` + `DatosDanio`/`TipoDanio`, `Impacto` (daño en área, empuje,
  derribo de vegetación), `Lanzable`.
- `Runtime/Enemigos/` — **Ferrox**: `Humano` (IA según el tamaño de la criatura: con una gigante
  patrulla, dispara y huye; a una pequeña la buscan a machetazos; detección con luz y escondites;
  **investiga y busca** —`Investigar(punto, urgente)`, estados `Investigar` y `Buscar`: va a la pista —a una criatura
  grande se acerca solo a un par de largos— y rastrea alrededor `segundosBusqueda`; la pista es donde la vio por última vez al perderla, lo que oye o
  lo que le mandan—; **oye** (`Ruidos`, con `oido`); **avisa por radio** (`radio`: el armado que ve a una
  criatura grande tarda `segundosLlamada` en dar el aviso, `Llamada`; si lo callan antes no llega;
  `AlAvisar`, y cada juego decide qué pasa); **rodea** lo que le corta el paso al andar (agua honda,
  cuestas y lo que esté en `CapasObstaculo`, que pone el juego); lejos de la criatura piensa uno de cada
  cuatro fotogramas; sus ráfagas hacen ruido; **se enfrenta a un animal** —`Enfrentar(transform, segundos)`:
  mientras no vea a la criatura, el armado se vuelve y le dispara a ráfagas, el civil huye; `Enfrentando`;
  cada juego decide cuándo, p. ej. el animal que la criatura le lleva con un cebo—; `AlCambiarEstado`
  (antes, ahora: para las voces); al buscar, en cada punto se para y mira a un lado y a otro; el cuerpo
  dura `SegundosCuerpo` —25 de serie; cada juego lo pone— salvo `conservarCuerpo`, `MuertoDesde`; **duerme**
  —`Dormir`/`Despertar`, `Dormido`, `AlDespertar`: tumbado, quieto, solo ve lo que tiene encima, y despierta
  con un ruido, un golpe, un aviso o si la ve; muerto dormido se queda tumbado; cada juego decide cuándo—;
  **caminos**: si se atasca —no se acerca en 2,5 s o no hay rumbo libre—, pide uno a `Navegacion` (A* en
  rejilla de 2 m: agua honda, cuestas y `CapasObstaculo`; recortado por líneas de visión con 0,65 m de holgura,
  para que al desviarse no acabe con una esquina delante; uno por fotograma; desde donde está pegado a una pared,
  el primer paso no cuenta lo que ya toca; `Ultimo` dice por qué falla y `Depurar`, con qué choca) y lo sigue,
  `ConCamino`; las esquinas del camino las pisa —a 0,6 m— en vez de cortarlas; si no se acerca al punto del
  camino en 2,5 s, pide otro una vez y, si vuelve a atascarse, lo deja: el que va a una pista se rinde y busca
  alrededor, también si en 6 s no se acerca);
  `quieto` para centinelas y gente sentada, `ruta` para rondas (con `pausaRuta` en los extremos:
  trabajan un rato y vuelven); los `protegido` miran a la criatura
  pequeña y no le hacen nada; `inocente` en falso para un civil que, si muere, no cuenta como
  inocente (un villano desarmado); `termica`: visión de calor, la oscuridad le quita poca vista, pero es un visor que solo ve de frente), `Ruidos` (el oído del mundo: `Emitir(donde, radio, TipoRuido, deCriatura)` y `AlOir`; cada juego dice
  qué ruido hace su criatura), `ZonaHumana` (con `AlActivar` al despertar su guardia; `Reocupar`: la compañía vuelve a una zona liberada con
  instalaciones nuevas, `AlReocupar`, `Reocupaciones`), `ConstructorInstalaciones` + `Piezas` +
  `ConstructorPiezasCampana`, `Estructura` (`engullida`: se la traga una criatura, desaparece sin piezas ni explosión), `Helicoptero` (rotor del BEMT de Lur Aerospace; ve a una criatura pequeña (menos de 8 m)
  con la misma `Humano.Visibilidad` que los humanos: la selva y el relieve la tapan), `Cohete`,
  `MaterialesInstalaciones`. **Miedo** (solo si el juego pone `Humano.ConMiedo = true`; Gorilla Revenge no):
  `miedo` de 0 a 100 que sube con la oscuridad (sin linterna), lo que ve de reojo, los ojos de la
  criatura cuando les da una luz de noche, siseos y rugidos (se oyen a 4 veces su radio), mordiscos,
  los gritos y las muertes de los de alrededor, las luces que se apagan y las `MarcaLeyenda` (camisas,
  estatuas, huellas: una vez cada una); se contagia y se calma con luz. Umbrales `Nervioso` (35: anda
  más despacio), `Aterrado` (70: los armados disparan a las sombras, los demás se quedan helados, y
  ante una criatura pequeña huyen en vez de atacar; `AlAterrarse`) y `Roto` (95: tira el arma y huye
  para no volver, `Ahuyentar`/`AlAhuyentar`). `AlSerAtacado` cuando la criatura le hace daño.
- `Runtime/Misiones/` — **el director de misiones común**: `Mision` (número, acto, título,
  etiquetas, hora, introducción y desenlace, objetivos, reglas, `preparar`), `DirectorMisiones`
  (monta, pone la hora, cuenta la introducción con la criatura quieta, lleva los objetivos con sus
  diálogos, vigila las reglas, desenlace, balance con la pantalla de misión y, si falla, la pantalla
  de caída y la repite; cada juego hereda `Preparar`, `Bloquear`, `AlTerminar`, `FilasBalance`,
  `Valoracion` y los textos), `Objetivo` (`Narracion`, `Esperar`, `Sobrevivir`, `Llegar`, `Condicion`
  con funciones, `Descubrir`; `Dice`/`AlAcabar`/`ConPista`) y `Regla` (`SinSerVisto`,
  `SinMatarCiviles`, `CriaturaViva`, `ReglaCondicion`). Los objetivos de cada criatura heredan de
  `Objetivo` en su juego. Gorilla Revenge aún usa su campaña propia.
- `Runtime/Sensacion/` — **la sensación de los golpes y del control** (opcional: cada juego la
  trae a su copia y la llama con su perfil; hoy ninguno). `PerfilSensacion` (asset; `Crear(ArquetipoSensacion)`: `Gigante`, `Agil`,
  `Creciente`, `Volador`, `Acuatico`, `Arcade`), `Sensaciones` (`Perfil`, `Impacto(Golpe)` con
  `ModoParada` local/global/ninguna, `Golpe.Desde(DatosDanio, Salud)`, `Temblor`, `Vibrar`,
  `EstaParado` para lo animado a mano, `EscalaTiempoBase`, `Opciones` de accesibilidad, eventos
  `AlImpacto`/`AlParar`), `TraumaCamara` (en cualquier cámara, después de su controlador),
  `BufferEntrada` y `MargenApoyo` (coyote time), `AsistenciaPunteria` (`Elegir`, `Corregir`,
  `BuscarSalud`) y `Medidor` (métricas para las pruebas, `Informe()`). Comprobación sin escena:
  `Guardianes.EditorTools.PruebaSensacion.ComprobarDesdeBatch`.
- `Runtime/Efectos/EfectosCombate` (`Gotas`: una rociada de un color —la sangre o el veneno de un mordisco—), `Runtime/Audio/SonidoJuego` (la voz de cada criatura la pone su
  juego con `Sonar` y los bancos `voces`/`percusion`/`pisadas`...), `Runtime/Camara/CamaraCriatura`
  (las medidas valen para `tamanoReferencia` y crecen con la criatura), `Runtime/Juego/`
  (`CalidadJuego` con `AlAplicar`, `RecursosJuego`, `Descubrimiento` con `tipo` entero; `OpcionesJuego`: las
  opciones del jugador en PlayerPrefs —pantalla completa, resolución, VSync, volumen, sensibilidad e invertir el eje
  Y de la cámara, tamaño y fondo de los subtítulos, daltonismo con su paleta `Peligro`/`Aviso`/`Bien`, dificultad
  —`DanioRecibido`, que aplica `Salud` a la criatura del jugador, y `Deteccion`, que aplica `Humano`—, idioma y los
  consejos; los controles: cada juego `Registrar` sus acciones y las teclas cambiadas se guardan; en batch, las de
  serie—; `Textos`: los idiomas —`T` traduce un texto con las tablas `Resources/Textos/*.<idioma>.txt` («español»
  TAB «traducción»), `F` una plantilla con huecos; lo que no está sale en español—),
  `Runtime/UI/HudJuego` (con `Jefe(nombre, vida)`: la barra de un jefe arriba, en el centro; + `PantallasHud`: cada juego hereda y pinta su estado en
  `ConstruirEstado`/`ActualizarEstado`; `HudJuego.Opciones`: el panel de opciones y el de controles —reasignar
  teclas y botones con el Input System—, desde el menú de inicio y el de pausa, con mando; los subtítulos y la
  narración con su tamaño y un fondo oscuro; los textos fijos y los avisos, narraciones, diálogos, títulos y la
  pantalla de la misión pasan por `Textos.T` y se vuelven a traducir al cambiar de idioma; `ConstruirExtras`,
  `RaizInicio`/`RaizPausa` y `CerrarPaneles` para los botones y paneles de cada juego), `Runtime/Splash/` (el
  splash de Odei Labs: `AssetsCreatorSplash` de AssetsCreator —sin tocar—, su vídeo `OdeiLabs.mp4` y el póster de
  plan B; cada juego lo pone en su escena de arranque y lo salta con su entrada). `DirectorMisiones.OscuridadCinematica`:
  lo que se oscurece la pantalla en las cinemáticas (un juego con plano de cine, menos).
- `Editor/` — `ConfiguradorHDRP` (ajustes ultra y la atmósfera de selva), `MaterialesLit`,
  `Capturador`, `Diagnostico`, `AvisoCerebro` (al abrir el editor, avisa de los cambios del cerebro
  que la copia no tiene y de los de la copia que no se han subido), `ImportadorTexturas`, `Terreno/` (`Relieve` y `PasadasRelieve`:
  erosión, subida con detalle, niveles de los ríos, tallar ríos y lagos, allanar zonas y lugares,
  humedad; el relieve base es de cada juego), `Vegetacion/` (`EspeciesSelva`, `RepartoVegetacion`
  con reglas por juego; `ReglasSelvaTropical` son las de la isla de Koba), `Agua/MaterialesAgua`,
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
  `vehiculos/sdf_helicoptero.py` (+ `decimar_obj.py` en Blender) y `traer_cerebro.py` (comparar la
  copia de un juego con el cerebro, traer y subir cambios: ver `../CLAUDE.md`).

## Reglas

- Escala: 1 unidad = 1 m. Los mundos van centrados en el origen; los datos compactos llevan sus
  medidas. Nada pesado en los repos: terreno y vegetación se reconstruyen al cargar a partir de
  semillas y datos compactos (LFS).
- Lo que dependa de una criatura concreta no entra aquí: va en su juego, colgado de `Criatura`,
  de los eventos o de los métodos virtuales.
- Los componentes que Gorilla Revenge (entonces King Kong) ya usaba conservan su GUID (sus escenas y prefabs siguen valiendo);
  las clases que cada juego especializa (`TerrenoIsla : TerrenoMundo`, `CamaraKoba :
  CamaraCriatura`, `HudKoba : HudJuego`) se quedan en el juego con su GUID.
- Los logs del paquete van con `[GD]`.

## Verificar

El paquete no tiene proyecto propio: se compila y se prueba dentro de los juegos (sus
`Tools/Unity-Batch.ps1` y `Tools/Unity-Pruebas.ps1`, con el editor cerrado). Un cambio aquí se
prueba trayéndolo a la copia de un juego (ninguno enlaza ya el cerebro) y compilando allí con
sus pruebas.
