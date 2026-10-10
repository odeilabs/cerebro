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
  `Ruido` (fBm, crestas, hash, para Burst). **Día y noche**: `CicloDia` mueve el sol según la hora
  con la latitud y la declinación del sitio (sigue encendido hasta ~6° bajo el horizonte: el cielo
  físico hace el crepúsculo), crea la luna (direccional; HDRP solo da sombra a una: la luna cuando no
  hay sol) y un relleno de cielo nocturno desde arriba sin sombras (sin él, bajo el dosel la noche es
  negra), cambia los límites y la compensación de la exposición de noche con un Volume propio (de
  noche puede oscurecer mucho para que una hoguera de cerca no queme la imagen) y da
  a los humanos la luz de cada punto (`Humano.Luz = CicloDia.Luz`: el cielo más las fuentes);
  `FijarHora`, `avanzar`, `minutosPorDia`, `EsDeNoche`, `Dia`, `Reloj`. Sin un `CicloDia` en la
  escena todo sigue siendo de día. `FuenteLuz`: luces que cuentan para la IA (hogueras, faroles,
  linternas con cono, focos): `Aporte`/`Total`, `Apagar` (asusta a los de alrededor), parpadeo de llama.
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
  patrulla, dispara y huye; a una pequeña la buscan a machetazos; detección con luz y escondites;
  `quieto` para centinelas y gente sentada, `ruta` para rondas (con `pausaRuta` en los extremos:
  trabajan un rato y vuelven); los `protegido` miran a la criatura
  pequeña y no le hacen nada), `ZonaHumana`, `ConstructorInstalaciones` + `Piezas` +
  `ConstructorPiezasCampana`, `Estructura`, `Helicoptero` (rotor del BEMT de Lur Aerospace), `Cohete`,
  `MaterialesInstalaciones`. **Miedo** (solo si el juego pone `Humano.ConMiedo = true`; King Kong no):
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
  `Objetivo` en su juego. King Kong aún usa su campaña propia.
- `Runtime/Sensacion/` — **la sensación de los golpes y del control** (opcional: cada juego la
  llama; hoy ninguno). `PerfilSensacion` (asset; `Crear(ArquetipoSensacion)`: `Gigante`, `Agil`,
  `Creciente`, `Volador`, `Acuatico`, `Arcade`), `Sensaciones` (`Perfil`, `Impacto(Golpe)` con
  `ModoParada` local/global/ninguna, `Golpe.Desde(DatosDanio, Salud)`, `Temblor`, `Vibrar`,
  `EstaParado` para lo animado a mano, `EscalaTiempoBase`, `Opciones` de accesibilidad, eventos
  `AlImpacto`/`AlParar`), `TraumaCamara` (en cualquier cámara, después de su controlador),
  `BufferEntrada` y `MargenApoyo` (coyote time), `AsistenciaPunteria` (`Elegir`, `Corregir`,
  `BuscarSalud`) y `Medidor` (métricas para las pruebas, `Informe()`). Comprobación sin escena:
  `Guardianes.EditorTools.PruebaSensacion.ComprobarDesdeBatch`.
- `Runtime/Efectos/EfectosCombate`, `Runtime/Audio/SonidoJuego` (la voz de cada criatura la pone su
  juego con `Sonar` y los bancos `voces`/`percusion`/`pisadas`...), `Runtime/Camara/CamaraCriatura`
  (las medidas valen para `tamanoReferencia` y crecen con la criatura), `Runtime/Juego/`
  (`CalidadJuego` con `AlAplicar`, `RecursosJuego`, `Descubrimiento` con `tipo` entero),
  `Runtime/UI/HudJuego` (con `Jefe(nombre, vida)`: la barra de un jefe arriba, en el centro; + `PantallasHud`: cada juego hereda y pinta su estado en
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
