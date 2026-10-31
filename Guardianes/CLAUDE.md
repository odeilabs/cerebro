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
  Burst; `Buscar`, `Derribar`, `Restaurar`), `Derribos` (el resto tumbado se quita cuando lleva un rato sin verse y
  lejos de la cámara; el hueco queda —`Huecos`— y la planta no vuelve sola: `Restaurar(centro, radio)` la hace
  rebrotar, con `AlRestaurar`; cada juego decide con qué —Gorilla Revenge, con la Ira de Koba—), `DatosVegetacion`.
- `Runtime/Criaturas/Criatura` — **la criatura del jugador** (Koba, el basilisco...). Los humanos,
  los helicópteros, la cámara, los efectos, el sonido y el HUD la buscan aquí (`Criatura.Jugador`)
  y escalan con `Tamano` (17,5 m = Koba = escala 1) y `AlturaBlanco`. Eventos para los demás:
  `Intimidar` (los humanos huyen, los helicópteros cercanos pierden el control), `OndaCono`,
  `Purificar`, `Pisar`. `Premiar` (cada juego decide qué da: furia, crecimiento, afinidad) y
  `Celebrar`. `IEntradaCamara` para que la cámara lea los controles de cada juego.
  `PuntoDisparo(desde, out punto)`: a dónde apunta un tirador (cada humano por disparo, el cañón por ráfaga,
  cada cohete); de serie, falso y sin gastar `Random`: apuntan como siempre (a `AlturaBlanco` con su reparto).
- `Runtime/Combate/` — `Salud` + `DatosDanio`/`TipoDanio`, `Impacto` (daño en área, empuje,
  derribo de vegetación; también a los blancos de `Balas`, una vez por `Salud`, con la distancia y la
  fracción que da cada uno), `Lanzable`. **Balas** (`Balas.cs`): todos los tiros (humanos, cañón y cohetes)
  pasan por `Balas.Rayo`/`Impactar`/`Disparar`: el rayo de física de siempre, que se salta lo del propio
  tirador, y después los **blancos sin colisionadores** (`IBlancoBalas`, `Registrar`/`Quitar`: un cuerpo que
  un collider no sigue, como la cadena de cápsulas de una serpiente) solo hasta donde llegó ese rayo, así que
  lo que tiene colisionador delante los tapa; `GolpeBlanco.para` (el agua: el tiro se queda sin dañar).
  **Calibres** (`Calibres`: perdigón 0,45, pistola y subfusil 1, arcabuz 1,3, fusil de asalto 8, fusil de
  caza 12, ametralladora 25, cañón 60) frente al **blindaje** de cada `Salud` (0 de serie: nada rebota):
  `Salud.Penetracion(calibre)` = smoothstep de calibre/blindaje entre 0,6 y 1,4; los dados del rebote los tira
  `Balas` con un `System.Random` propio (semilla 1542, no mueve el `UnityEngine.Random` de cada juego) solo si
  puede salir una cosa u otra, y un rebote (`DatosDanio.rebote`) hace el `restoRebote` (0,06) y avisa con
  `AlDanio`. `DatosDanio.calibre` (0 = sin calibre: entra siempre). `Balas.AlImpactar(Tiro)`, `Ultimo`,
  `Impactos`, `Rebotes`, `Tiradas` y el `ProfilerMarker` «Balas» para las pruebas. Con lo de serie (sin blancos,
  sin blindaje) todo da lo mismo que antes, también la secuencia de `Random`. Comprobación sin escena:
  `Guardianes.EditorTools.PruebaBalas.ComprobarDesdeBatch`.
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
  inocente (un villano desarmado); `termica`: visión de calor, la oscuridad le quita poca vista, pero es un visor que solo ve de frente;
  **el arma** —`arma` (`ClaseArma`), `calibre`, `proyectiles` y `abanico`: en `Awake` reconoce el arma que lleva en la
  mano (`Arma.DeHumano`) y de su ficha toma solo lo físico; el daño, la cadencia y la dispersión siguen siendo los del
  prefab y los de las misiones; `Armar(clase)` copia la ficha entera; cada disparo pasa por `Balas` (los perdigones
  en su abanico, el del centro el último: es el que se ve y el que se avisa por `AlDisparar`, que acaba en la piel)
  y apunta a donde diga `Criatura.PuntoDisparo`, también al `Enfrentar` a la propia criatura—), `Arma`
  (`ClaseArma`: `FusilAsalto` —el 0, el de los prefabs de antes—, `Pistola`, `Subfusil`, `Escopeta`, `Arcabuz`,
  `FusilCaza`, `Ametralladora`, `Canon`, los nuevos al final; `DatosArma` con calibre, daño, cadencia, dispersión,
  abanico, alcance, fogonazo, tono, proyectiles, ráfaga y pausa; `Ficha`/`Ajustar` —cada juego retoca la tabla al
  arrancar—; `Prefijos`/`ClaseDe`/`Reconocer`: la clase por el nombre del modelo —`kk_ak47` y los de AssetsCreator, y
  las ocho del kit de armas de AssetsCreator, `creador.py arma`: `gd_glock17` y `gd_colt_saa` pistola, `gd_mp5` subfusil, `gd_ak47` fusil de
  asalto, `gd_escopeta` (las dos) escopeta, `gd_remington700` fusil de caza y `gd_m2_browning` ametralladora; gana el
  prefijo más largo—; `DeHumano`: un `Arma` en los hijos
  o la malla bajo `AC_arma_r`; `MontajeArma`: el pivote, el eje de giro y el radio de la columna de un arma montada (la M2
  en su cuna; el poste lo pone el juego), de su ficha; `ManosEnArma` (`Poner(humano, arma, cachaDerecha,
  cachaIzquierda)`): las manos de quien maneja un arma que no lleva en la mano —las dos cachas de la M2—, después del
  `Animator` (`LateUpdate`, o `Aplicar` a mano), con los dos huesos de cada brazo del esqueleto de AssetsCreator: el puño
  —el centro de las falanges— en su cacha, el codo en el plano que tenía en el clip y la mano girada a la cacha (los
  nudillos hacia el cañón, el índice arriba); muerto, nada; el fusil de asalto, con
  fogonazo y tono 1, es el de siempre; la escopeta, fogonazo ancho y estampido grave, y el fusil de caza, fogonazo
  estrecho y chasquido seco, para que se distingan), `Ruidos` (el oído del mundo: `Emitir(donde, radio, TipoRuido, deCriatura)` y `AlOir`; cada juego dice
  qué ruido hace su criatura), `ZonaHumana` (con `AlActivar` al despertar su guardia; `Helicopteros`, los que ha mandado; `ElegirGuardia`:
  el prefab de cada uno de la guardia —cada juego reparte sus armas; sin él, el del azar, que se tira igual—; `Reocupar`: la compañía vuelve a una zona liberada con
  instalaciones nuevas, `AlReocupar`, `Reocupaciones`), `ConstructorInstalaciones` + `Piezas` (cajas, cilindros,
  prismas y `Tubo`/`Catenaria`: un tubo a lo largo de unos puntos o colgado entre dos —ramas, lianas, cables—,
  `PuntosCatenaria`; la malla del tubo es suya) +
  `ConstructorPiezasCampana`, `Estructura` (`engullida`: se la traga una criatura, desaparece sin piezas ni explosión), `Helicoptero` (rotor del BEMT de Lur Aerospace; ve a una criatura pequeña (menos de 8 m)
  con la misma `Humano.Visibilidad` que los humanos: la selva y el relieve la tapan; el cañón y los cohetes pasan por
  `Balas` —`calibreCanon`, 60 de serie— y apuntan a `Criatura.PuntoDisparo` si da algo: la ráfaga, al punto que dio
  al empezarla, guardado respecto a la criatura; sin él, a la altura de siempre, que sigue gastando su `Random`;
  `Estorbos`: lo que cada juego levanta y no se puede atravesar —un muro, una presa: el aparato es cinemático y no choca
  con los colisionadores—, lo más alto en un punto, que cuenta como las copas en su techo y en la altura a la que va; sin
  ninguno, como siempre), `Cohete`,
  `MaterialesInstalaciones`. **Miedo** (solo si el juego pone `Humano.ConMiedo = true`; Gorilla Revenge, en sus misiones de noche):
  `miedo` de 0 a 100 que sube con la oscuridad (sin linterna), lo que ve de reojo, los ojos de la
  criatura cuando les da una luz de noche, siseos y rugidos (se oyen a 4 veces su radio), mordiscos,
  los gritos y las muertes de los de alrededor, las luces que se apagan y las `MarcaLeyenda` (camisas,
  estatuas, huellas: una vez cada una); se contagia y se calma con luz. Umbrales `Nervioso` (35: anda
  más despacio), `Aterrado` (70: los armados disparan a las sombras, los demás se quedan helados, y
  ante una criatura pequeña huyen en vez de atacar; `AlAterrarse`) y `Roto` (95: tira el arma y huye
  para no volver, `Ahuyentar`/`AlAhuyentar`). `AlSerAtacado` cuando la criatura le hace daño.
- `Runtime/Misiones/` — **el director de misiones común**: `Mision` (número, `rotulo` —lo que se ve en vez de
  «Misión N»: un encargo, una misión fuera de la cuenta; vacío, el número—, acto, título,
  etiquetas, hora, introducción y desenlace, objetivos, reglas, `preparar`), `DirectorMisiones`
  (monta, pone la hora, cuenta la introducción con la criatura quieta, lleva los objetivos con sus
  diálogos, vigila las reglas, desenlace, balance con la pantalla de misión y, si falla, la pantalla
  de caída y la repite; cada juego hereda `Preparar`, `Bloquear`, `AlTerminar`, `FilasBalance`,
  `Valoracion` y los textos; para retomar una misión a medias, `PrimerObjetivo` —los de antes se saltan con
  `Objetivo.Saltar` y no se cuenta la introducción— y `AlEmpezarObjetivo` —el autoguardado—; `AntesDelBalance`,
  `TituloCompletada`, `PieFallida` y `FallarDesdeFuera`), `Objetivo` (`Narracion`, `Esperar`, `Sobrevivir`, `Llegar`,
  `Condicion` con funciones, `Descubrir`; `Dice`/`AlAcabar`/`ConPista`; `Saltar`) y `Regla` (`SinSerVisto`,
  `SinMatarCiviles`, `CriaturaViva`, `ReglaCondicion`). Los objetivos de cada criatura heredan de
  `Objetivo` en su juego. Lo usan Basiliskos y, desde el 31/10/2026, la campaña de Gorilla Revenge.
- `Runtime/Sensacion/` — **la sensación de los golpes y del control** (opcional: cada juego la
  trae a su copia y la llama con su perfil; desde el 30/10/2026 la usa Gorilla Revenge, con el `Gigante` y
  paradas globales porque su animación es a mano). `PerfilSensacion` (asset; `Crear(ArquetipoSensacion)`: `Gigante`, `Agil`,
  `Creciente`, `Volador`, `Acuatico`, `Arcade`), `Sensaciones` (`Perfil`, `Impacto(Golpe)` con
  `ModoParada` local/global/ninguna, `Golpe.Desde(DatosDanio, Salud)`, `Temblor`, `Vibrar`,
  `EstaParado` para lo animado a mano, `EscalaTiempoBase`, `Opciones` de accesibilidad, eventos
  `AlImpacto`/`AlParar`), `TraumaCamara` (en cualquier cámara, después de su controlador),
  `BufferEntrada` y `MargenApoyo` (coyote time), `AsistenciaPunteria` (`Elegir`, `Corregir`,
  `BuscarSalud`) y `Medidor` (métricas para las pruebas, `Informe()`; una petición que lleva más de
  `PeticionPerdida`, 1 s, sin respuesta no cuenta: la siguiente se mide desde la suya). Comprobación sin escena:
  `Guardianes.EditorTools.PruebaSensacion.ComprobarDesdeBatch`.
- `Runtime/Efectos/EfectosCombate` (`Gotas`: una rociada de un color —la sangre o el veneno de un mordisco—;
  `Rebote(punto, normal, dir, escala)`: la bala que rebota —chispas en un cono alrededor del reflejo, a veces una
  trazadora desviada y un destello—, que pinta solo con `Balas.AlImpactar`; un tiro que da en un blanco sin
  colisionadores o rebota no deja polvillo ni chispas —la sangre la pone el juego—; la trazadora se apaga en lo que
  toca —a menos de 3 m no hay, sin cambiar el `Random`— y su raya (420 m/s por el `velocityScale` de 0,005: unos 2,2 m) cabe
  en los 2,5 m a los que sale de la boca: no la cruza ni atraviesa al tirador; el fogonazo de un humano, del
  tamaño de su arma; `Rebotes`, `Polvillos`, `SinPolvillo` y `AlPolvillo` para las pruebas), `Runtime/Audio/SonidoJuego` (la voz de cada criatura la pone su
  juego con `Sonar` y los bancos `voces`/`percusion`/`pisadas`...; el disparo de un humano, con el tono de su arma;
  los rebotes con el banco `rebotes` —`armas/rebote_*`, el «piiu» de `generar_sonidos.py --solo rebote`, que pone
  `ConstructorAudio`; mientras no estén, una
  chapa de `metales` muy aguda— y su propio tope, `rebotesPorSegundo`; los perdigones de un tiro que rebotan suenan
  una vez —mismo tirador y fotograma—, algo más fuerte cuantos más rebotan; `RebotesSonados`), `Runtime/Camara/CamaraCriatura`
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
  AssetsCreator —`Def.familia`: el prefijo de los clips de quieto, andar, apuntar y disparar, `fusil_ak` de serie, o
  `pistola`, `escopeta`, `subfusil` de `animar --arma`; `Def.carpeta` y `Def.materialesDe`: una variante con otra arma
  va en la carpeta del humano de serie y con sus materiales, sin copiar dos veces las texturas; con `danioBala` 0 un
  armado lleva la ficha entera de su arma; el objeto «Arma» lleva un `Arma` con la clase del proyecto; **las armas del
  kit de armas** (`gd_*`, de AssetsCreator: `generador\armas`, `creador.py arma`) traen su **ficha**, `<n>_agarre.json` (`LeerFicha(ruta)` →
  `FichaArma`: la clase, el `Agarre` ya en el espacio del modelo importado —la x del OBJ cambia de signo: empuñadura
  `(-agarre.x, agarre.y, agarre.z)` más `DesfaseAgarre(clase)` —de serie ninguno: en los clips el puño cierra en el hueso
  de la mano, y con el del AK de la regla, (0,06; −0,04; 0), quedaba debajo de la empuñadura—; el cañón a `Vector3.left`,
  arriba `Vector3.up`, la boca, el guardamanos y, en la M2, el montaje—, la receta de cada material y la rugosidad que
  pida; `RutaFicha(proyecto)`), y van en la mano con
  ella y no con la regla: `PonerArma` / `InstanciarArma` (el arma fuera de la mano, para una torreta) le ponen la
  `Bocacha`, un hijo «Guardamanos» (la mano izquierda) y, si va montada, un `MontajeArma`; **`TraerArma(proyecto,
  retocar)`** copia de AssetsCreator a `Art/Humanos/Armas/<n>` el OBJ, la ficha, el `.mtl` sin sus `map_`/`norm`/`bump`/
  `disp` (sin él, el OBJ sale con un solo material, blanco) y, de los mapas, solo el color base de cada material (el que
  ya está en el arte del juego igual byte a byte no se copia, salvo si está en la carpeta de otra arma: el `TraerArma` de
  esa la pisaría al cambiar y quitar su carpeta la borraría; esa se copia otra vez, y LFS guarda un solo objeto), y le pone
  un HDRP/Lit por material del OBJ, remapeado por
  nombre en su importador (`AcabadoDe`: metal si su receta lo es —el pavonado brillante y la pintura del kit son
  `pintura_coche`/`pintura_mate`, no metal— o, sin receta, si tiene mapa metallic; suavidad = 1 − la rugosidad de la
  ficha o, si no la lleva, la `Pr` de su `.mtl`; sin ficha, por el nombre; `retocar`, el tinte de cada juego);
  `ConstruirTodos` trae así las del kit; `Agarres`: el agarre a mano de las armas en las que ni la regla del 28 % de
  `EjesFusil` ni una ficha valen (hoy ninguna); `kk_ak47` y lo demás
  sin ficha, con la regla y su copia de siempre; lo que ya está igual no se vuelve a copiar;
  `ImportadorArmas`: la malla de los OBJ de `Art/Humanos/Armas` se llama como el modelo —un OBJ de un solo objeto la
  deja en «default»—, que es por lo que `Arma.DeHumano` reconoce el calibre, también en los prefabs de antes—,
  `ConstructorHelicoptero`, `ConfiguradorZonas`).
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
  en Python: numpy/scipy/Pillow; todos con `--proyecto <carpeta del proyecto Unity>` o `--salida`;
  `arte/armas` solo dice dónde está el kit de armas, que desde el 28/10/2026 vive en AssetsCreator),
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
sus pruebas. Comprobaciones del paquete sin escena (en batch, `-Metodo`):
`Guardianes.EditorTools.PruebaSensacion.ComprobarDesdeBatch` y
`Guardianes.EditorTools.PruebaBalas.ComprobarDesdeBatch` (las balas: lo mismo que `Physics.Raycast` sin blancos,
lo del tirador, los blancos, los rebotes y sus dados, `Impacto.Golpear` con un blanco, la tabla de las armas —también
las del kit, y que con dos prefijos que se solapan gana el más largo— y su ficha, con los `_agarre.json` que escribe en
una carpeta temporal: un fusil de caza, una pistola, la M2 montada y tres que no valen del todo).
