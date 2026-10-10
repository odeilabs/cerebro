# Cerebro de Gaia's Response

Lo común de la saga **Gaia's Response** de Odei Labs: el paquete Unity que comparten todos los
juegos, las instrucciones para trabajar en ellos y la guía de **AssetsCreator**. Si vas a tocar un
juego de la saga (o a empezar uno), lee primero [CLAUDE.md](CLAUDE.md) (reglas comunes) y este
README. Lo que falta está en [ROADMAP.md](ROADMAP.md); el mapa de los juegos, en `../README.md`.

- [Qué hay en el cerebro](#qué-hay-en-el-cerebro)
- [Cómo lo enlaza cada juego](#cómo-lo-enlaza-cada-juego)
- [Arrancar un juego nuevo de la saga](#arrancar-un-juego-nuevo-de-la-saga)
- [Herramientas comunes (Tools~)](#herramientas-comunes-tools)
- [Guía de AssetsCreator](#guía-de-assetscreator)
- Investigación: [Fluidez y sensación](Investigacion/Fluidez-y-sensacion.md) (mecánicas de movimiento
  y de ataque divertidas: respuesta, buffer, hitstop, sacudida, recompensas)

## Qué hay en el cerebro

```
cerebro/                      repo git propio (sin remoto todavía)
├── CLAUDE.md                 instrucciones comunes para los agentes de todos los juegos
├── README.md / ROADMAP.md
├── portada.png
└── Guardianes/               paquete Unity com.odeilabs.guardianes (0.1.0)
    ├── Runtime/              ensamblado Guardianes
    ├── Editor/               ensamblado Guardianes.Editor
    ├── Shaders/              variantes de HDRP/Lit generadas + shaders propios
    └── Tools~/               generadores en Python (Unity no importa las carpetas con ~)
```

Hoy hay **un paquete**, `com.odeilabs.guardianes` (Unity 6000.5.2f1, HDRP 17.5, Input System 1.19,
Burst, Collections, Mathematics, uGUI). Su detalle sistema por sistema está en
[Guardianes/CLAUDE.md](Guardianes/CLAUDE.md); en resumen:

| Área | Qué da |
| --- | --- |
| Mundo (`Guardianes.Mundo`) | Medidas del mapa, consultas (`Mapa`: altura, agua, profundidad, contaminación), datos compactos (`DatosMundo` en `.bytes`), terreno de 8 capas con Burst, agua de HDRP (lagos, ríos, cascadas), ríos con meandros, zonas, día y noche (`CicloDia`: sol, luna, exposición y la luz que ven los humanos; `FuenteLuz`: hogueras, faroles, linternas). |
| Vegetación | Especies procedurales (ceiba, dosel, palmera, helechos, bambú...), reparto por reglas, render instanciado por cámara con LOD y culling en Burst, viento, derribos. |
| Criaturas | `Criatura`: la criatura del jugador. Todos (humanos, helicópteros, cámara, efectos, sonido, HUD) la buscan aquí y escalan con su `Tamano` (Kong = 17,5 m = escala 1). Eventos `Intimidar`, `OndaCono`, `Purificar`, `Pisar`; `Premiar` y `Celebrar` los traduce cada juego. |
| Combate | `Salud`, `DatosDanio`, `Impacto` (daño en área, empuje, derribos), `Lanzable`. |
| Ferrox (`Guardianes.Enemigos`) | `Humano` (IA según el tamaño de la criatura, detección con luz y escondites, **miedo** que se contagia, rondas y centinelas), `MarcaLeyenda`, `ZonaHumana`, instalaciones y piezas, `Estructura`, `Helicoptero` (rotor por BEMT), `Cohete`. |
| Misiones (`Guardianes.Misiones`) | `DirectorMisiones` (introducción, objetivos con diálogos, reglas, desenlace, balance, fallo y repetición), `Objetivo` (`Narracion`, `Llegar`, `Condicion`...), `Regla` (`SinSerVisto`, `CriaturaViva`...), `Mision`. Cada juego hereda el director y añade sus objetivos. |
| Presentación | `EfectosCombate`, `SonidoJuego`, `CamaraCriatura`, `CalidadJuego`, `HudJuego` (avisos, objetivo, marcador, diálogos, menús, pantallas de misión y de caída). |
| Editor | `ConfiguradorHDRP` (HDRP "ultra", atmósfera de selva), `MaterialesLit`, `Capturador`, `Diagnostico`, terreno (`Relieve`, `PasadasRelieve`: erosión, ríos, lagos, humedad), reparto de selva, materiales de agua, efectos, audio, `ConstructorHumanos` (humanos de AssetsCreator), `ConstructorHelicoptero`, `ConfiguradorZonas`. |
| Shaders | `Guardianes/Vegetacion`, `Pelaje`, `Rio`, `Escamas` (variantes de Lit, generadas), `Particula`, `Cascada`, `Calor`. |

## Cómo lo enlaza cada juego

Cada proyecto Unity de la saga lo declara en su `Packages/manifest.json` como paquete local:

```json
"com.odeilabs.guardianes": "file:../../../cerebro/Guardianes",
```

La ruta es relativa a `Packages/` del proyecto: `Gaia's Response/<Juego>/<Proyecto>/Packages` →
`Gaia's Response/cerebro/Guardianes`. Unity lo trata como paquete editable (aparece en
`Packages/Guardianes` del editor) y escribe la misma ruta en `packages-lock.json`. Consecuencias:

- Un cambio en el cerebro llega a **todos** los juegos en cuanto se abren: hay que comprobarlo en
  todos (compilar en batch y una prueba de cada uno).
- Los componentes del paquete tienen GUID fijos (sus `.meta` están en el repo): no regeneres los
  `.meta` ni muevas scripts sin su `.meta`, o las escenas de los juegos pierden las referencias.
- Lo que cada juego especializa (`TerrenoIsla : TerrenoMundo`, `HudBasilisco : HudJuego`...) vive
  en el juego.

## Arrancar un juego nuevo de la saga

La plantilla es **Basiliskos** (`../Basiliskos`): cópiale la estructura, no el contenido.

1. **Carpeta**: `Gaia's Response/<Juego>/` con el proyecto Unity dentro (`<Juego>/<Proyecto>/`),
   y junto a él `Tools/`, `Guion/`, `Capturas/` (ignorada), `README.md`, `ROADMAP.md` y `CLAUDE.md`.
   La portada que ya hay en la carpeta se queda.
2. **Proyecto**: Unity 6000.5.2f1 con la plantilla HDRP; en *Player > Active Input Handling*, solo
   el Input System (`activeInputHandler: 1`).
3. **Git con LFS desde el primer commit**: copia `.gitattributes` y `.gitignore` de Basiliskos (el
   de la raíz y el del proyecto). Binarios (texturas, audio, FBX, `.bytes`) por LFS. No se crea el
   remoto sin preguntar.
4. **Paquetes**: añade la línea del cerebro al `manifest.json` y las dependencias que pide el
   paquete (HDRP 17.5.0, Input System 1.19.0, Burst 1.8.29, Collections 6.5.0, Mathematics 1.4.0,
   uGUI 2.5.0).
5. **Ensamblados** en `Assets/_Game` (copia los de Basiliskos cambiando el nombre):
   - `Scripts/<Juego>.asmdef`: `rootNamespace` = `<Juego>`, referencias `Guardianes`,
     `Unity.InputSystem`, `Unity.RenderPipelines.Core.Runtime`,
     `Unity.RenderPipelines.HighDefinition.Runtime`, `Unity.Burst`, `Unity.Collections`,
     `Unity.Mathematics`, `UnityEngine.UI`; `allowUnsafeCode`.
   - `Editor/<Juego>.Editor.asmdef`: solo Editor, `rootNamespace` `<Juego>.EditorTools`, referencias
     las de arriba + `<Juego>`, `Guardianes.Editor` y los `.Editor` de RP Core y HDRP.
   - `Tests/Juego/<Juego>.Pruebas.Juego.asmdef`: `UNITY_INCLUDE_TESTS`, `overrideReferences` con
     `nunit.framework.dll`, referencias `<Juego>`, `Guardianes`, `UnityEngine.TestRunner`,
     `UnityEditor.TestRunner`, Input System y HDRP.
6. **La criatura**: una subclase de `Guardianes.Criaturas.Criatura` (velocidad, tamaño, altura del
   blanco, radio, escondida) con su entrada (`IEntradaCamara` para la cámara), una `CamaraCriatura`
   ajustada a su tamaño, un HUD que herede de `HudJuego` (`ConstruirEstado`/`ActualizarEstado`), su
   voz sobre `SonidoJuego` y su traducción de `Premiar`.
7. **El mundo**: una clase estática con las medidas (`MedidasMundo`) y la geografía apta para Burst,
   un generador de relieve propio que use `Relieve` y `PasadasRelieve`, las reglas de vegetación de
   su bioma, sus zonas de Ferrox (`Zona`) y sus lugares. Un `Constructor<Mundo>` en el editor (menú
   con el nombre del juego) que monte la escena entera con un `ConstruirDesdeBatch`.
8. **Tools**: copia `Tools/Unity-Batch.ps1` y `Tools/Unity-Pruebas.ps1` de Basiliskos y cambia la
   carpeta del proyecto, el prefijo de los logs y el filtro de las pruebas.
9. **Pruebas en batch con fotos**: `Scripts/Pruebas/ConductorPrueba` (pasos con entrada simulada,
   acciones, fotos a una RenderTexture, recuento de eventos), `GuionesPrueba` (los guiones) y
   `Tests/Juego/PruebasEnJuego` (carga la escena, lanza el guion y comprueba el resultado). Las fotos
   se miran siempre.
10. **Documentos**: `CLAUDE.md` (estructura, reglas de diseño, cómo verificar), `README.md`,
    `ROADMAP.md` y `Guion/` (diseño y campaña). Prefijo de logs propio (dos letras).

## Herramientas comunes (Tools~)

Python 3 con numpy, scipy y Pillow (el `.venv` de AssetsCreator los tiene:
`<Odei Labs>\AssetsCreator\AssetsCreatorWin\.venv\Scripts\python.exe`). Todos escriben en el
proyecto que digas con `--proyecto <carpeta del proyecto Unity>` (o en `--salida <carpeta>`) y son
deterministas (semillas fijas). Rutas desde `Guardianes/Tools~/`:

| Script | Qué genera |
| --- | --- |
| `generar_shaders_lit.py --proyecto <p> [--solo Vegetacion,Pelaje,Rio,Escamas]` | Las variantes de HDRP/Lit del paquete a partir del `Lit.shader` del HDRP del proyecto. Solo hace falta al cambiar de versión de HDRP o al tocar un `.hlsl`. |
| `arte/terreno/generar_capas_terreno.py --proyecto <p> [--solo ...] [--res 2048]` | Capas del suelo (hojarasca, barro, musgo, roca, guijarros, arena, hierba, contaminado) en `Art/Terreno/Capas`. |
| `arte/vegetacion/generar_texturas_vegetacion.py --proyecto <p> [--solo ...]` | Cortezas, hojas, frondas, hierba y flores en `Art/Vegetacion/Texturas`. |
| `arte/efectos/generar_texturas_efectos.py --proyecto <p>` | Láminas de partículas (humo, bola de fuego, llamas, chispa, fogonazo, escombro, hoja, anillo, espuma de cascada). |
| `arte/audio/generar_sonidos.py --proyecto <p> [--criatura serpiente] [--solo a,b]` | Sonidos sintetizados: voces de criatura, disparos, explosiones, madera, metal, árboles, ambientes en bucle. |
| `arte/empaquetar_materiales.py <carpeta AssetsCreator> <destino> [receta ...]` | Convierte materiales de `creador.py material` al formato del juego (`_albedo`, `_normal`, `_mascara` de HDRP). |
| `vehiculos/sdf_helicoptero.py --salida <carpeta>` (+ `decimar_obj.py` en Blender) | El helicóptero de ataque por campos de distancias. |
| `hoja_contactos.py <carpeta> [--columnas 5]` | Junta las fotos de una prueba en una hoja. |

`ImportadorTexturas` (editor) ajusta la importación por sufijo: `_albedo`, `_normal`, `_mascara`,
`_grosor`, `_altura`.

---

## Guía de AssetsCreator

**AssetsCreator** (`C:\Users\inigo\Desktop\Odei Labs\AssetsCreator`, repo
`github.com/odeilabs/AssetsCreator`) es el generador procedural (y por IA) de Odei Labs: objetos,
personajes con rig y animación, animales, materiales PBR, audio, cielos, agua, efectos y mundos,
con salida para Unity HDRP. Esta guía dice lo que la saga necesita saber para que **ningún juego
tenga que volver a explorarlo**. Su README completo (`AssetsCreatorWin\README.md`, unas 6400
líneas) está ordenado por secciones; aquí se citan por su título.

> **Regla de la saga: no se modifica AssetsCreator** (ni código, ni `config.json`, ni lo que otros
> proyectos tienen en `proyectos\`). Se usa lo que genera con sus comandos y parámetros. Si algo
> no se puede hacer así, se hace en el cerebro o en el juego.

### Lanzarlo

```powershell
cd "C:\Users\inigo\Desktop\Odei Labs\AssetsCreator\AssetsCreatorWin"
.venv\Scripts\python.exe creador.py --help          # la cabecera de creador.py: todos los subcomandos
.venv\Scripts\python.exe creador.py <subcomando> --help
.venv\Scripts\python.exe creador.py ui              # interfaz web en http://127.0.0.1:8765
```

- Si falta el entorno: `py -3.12 -m venv .venv` y `.venv\Scripts\python.exe -m pip install -r requirements.txt`
  (sección *Instalación desde cero*). La vía de IA por GPU está en `ultra\INSTALAR_WINDOWS.md`.
- **Blender 4.x/5.x** (probado con 5.1.2) para `humano`, `animar`, `animal`, `serpiente`,
  `gorila`, `selva`, `helicoptero`, `huesos`. Lo busca en la clave `blender` de `config.json`, en
  `%BLENDER%`/`%ASSETSCREATOR_BLENDER%`, en el `PATH` y en `Program Files\Blender Foundation`.
- `creador.py` es un despachador: cada subcomando llama a su script con `cwd=AssetsCreatorWin`.
- `Generar-Asset.ps1` y `Generar-AssetLote.ps1` son **fotogrametría** (Docker con COLMAP/OpenMVS),
  no generación procedural.

### Dónde deja las cosas

- Casi todo en `proyectos\<nombre>\salida\` (intermedios en `trabajo\`). Excepciones: `material`
  escribe en `--salida` (convención `materiales\<receta>\`), `cielo` en `cielos\`, `reparto` deja
  la hoja de contactos en `proyectos\_revision\`, y `audio-accion`, `audio-fantasia` y `efectos` en
  `proyectos\audio_accion\`, `proyectos\audio_fantasia\` y `proyectos\efectos\`.
- Nombres: `objeto` → `<n>.obj/.mtl` (+ `_preview.png`); con `--pbr` → `<n>_pbr.obj` y
  `<n>_pbr_<material>_{basecolor,normal,roughness,ao,height[,metallic]}.png` (+ `_material.json`);
  `humano` → `<n>.fbx` + carpeta `<n>.fbm\` (sus texturas) + `mpfb_params.json` (lo que se pidió);
  `animar` → `<n>_animado.fbx` o `<n>_animado_<arma>.fbx` + `<fbx>.clips.json`.
- **`proyectos\`, `cielos\`, `materiales\` y `config.json` no se versionan** en el git de
  AssetsCreator: lo generado solo existe en este disco. Por eso la **receta** (el comando exacto) se
  apunta en el cerebro (ver *Recetas de la saga*) y lo que usa un juego se copia a su `Assets` (LFS).
- `creador.py unity <proyecto> --destino <alias>` exporta a los proyectos Unity que tiene en
  `config.json` (renombra mapas, empaqueta la máscara de HDRP e instala sus scripts en
  `Assets\AssetsCreator`). **La saga no lo usa**: no hay alias de Gaia's Response y sus scripts de
  importación chocan con los nuestros (ver *Trampas*). La saga copia desde `proyectos\` con sus
  constructores de editor.

### Qué se puede coger

| Familia | Comando | Ya generado en `proyectos\` | Sección del README |
| --- | --- | --- | --- |
| Humanos realistas con rig (MPFB2) | `humano`, `reparto` | `kk_mercenario` (el mercenario de Ferrox), `guardia_humano_a/b/c`, `ht_guardia_real` (animado); `civil_*`, `fs_civil_*` (solo OBJ) | Humanos realistas; La cara; El cuerpo; La ropa derivada; La ropa |
| Animación de humanos | `animar --proyecto X [--arma fusil_ak] --clips a,b,c` | el mercenario con 15 clips de fusil | Animación de personajes; Clips de combate procedurales |
| Soldados (uniformes de los años 40) | `humano --vestir ww2_* --equipo ww2_*` | 32 FBX `ww2_{us,de,su}_*` | El soldado de la WW2 |
| Humanos de fondo sin rig | `objeto personaje --profesion lenador\|minero\|obrero\|soldado\|guardia...` (22) | — | Pack de personajes y ropa por profesión |
| Armas de fuego (low-poly, PBR) | `objeto arma --preset ak47\|fusil\|escopeta\|subfusil\|pistola` | `kk_ak47`, `arma_fusil`, `arma_escopeta`, `arma_subfusil`, `pistola`, armas WW2 | cabecera de `generador\crear_objeto.py` |
| Armas cuerpo a cuerpo | `objeto arma --preset espada\|hacha\|lanza\|arco\|escudo\|porra` | — | ídem |
| Cuernos, colmillos, alas, garrotes | `forma --forma cuerno\|colmillo\|ala_plumas\|garrote\|maza_gigante...` | `ws2_cuerno`, `ws2_colmillo`, `ws2_ala_plumas`, `ws2_garrote` | Armas de criatura, cuernos, alas... |
| Cuadrúpedos con esqueleto (57 huesos, 23 clips) | `animal --especie X --pbr 1024 [--estado "mojado=1,barro=0.6,sangre=0.3"]` | `animal_*` (caballo, ciervo, conejo, gato, jabalí, león, lobo; pendientes de regenerar) | Animales cuadrúpedos con esqueleto; Animales esculpidos |
| Serpientes PBR con rig y clips | `serpiente --especie mamba_negra\|vibora_russell\|taipan_interior` | las tres | Serpientes PBR ultra |
| Gorila / King Kong (pelaje por capas) | `gorila --nombre kong --alto 17.5`, `shader-hdrp` | — (portado desde King Kong) | Gorilas y King Kong |
| Huesos de gigante | `huesos` | — | Helicóptero de ataque y huesos de gigante |
| Helicóptero de ataque | `helicoptero` | — | ídem |
| Selva tropical (14 especies, LOD, viento) | `selva [--solo ceiba,palmera,...]` + `shader-hdrp --variantes viento` | — (portada desde King Kong) | Selva tropical |
| Suelo de selva por capas | `capas-terreno [--solo ...\|lugares]` | — | Suelo de selva por capas |
| Palmeras y playa | `objeto planta --preset palmera_*`, `objeto playa`, `forma --forma palmera\|hamaca...` | `arb_palmera*`, `pbr_palmera` | Playa y palmeras |
| Agua para el Water System de HDRP | `agua --nombre X --preset tropical\|lago\|tormenta...` | — | Agua: del mar cristalino al temporal |
| Partículas de combate | `efectos [--solo humo,bola_fuego,llamas,...]` | `ws2_efectos` (solo json) | Hojas de partículas de combate |
| Fuego y humo | `fuego --preset antorcha\|fogata\|hoguera...`, `humo --preset diesel\|neumatico...` | `ht_hoguera`, `ht_antorcha`, `humo_*` | Llamas vivas; Humo reiluminable |
| Sonidos a escala gigante | `audio-accion [--solo rugido,pisada,disparo,rotor,selva,...]` | — | Sonidos de acción a escala gigante |
| Sonidos de fantasía oscura | `audio-fantasia [--solo rugido,gemido,mordisco,aleteo,...]` | — | Sonidos de fantasía oscura y asedio |
| Ambientes y música | `audio` | `audio_ambiente`, `fs_audio` | (Audio) |
| Props de terror (campamentos, culto) | `objeto abandono\|matadero\|clinica\|culto\|bunker\|mano --preset X --gore 0\|1\|2` | — | Terror: 110 props |
| Props cotidianos y de obra | `objeto herramienta\|mobiliario\|cocina\|vialidad...`; `forma --forma contenedor\|palet\|saco\|generador\|foco...` | — | Objetos cotidianos: 300 props |
| Cabañas y chozas | `objeto cabana --preset cabana_troncos...`, `objeto edificio --preset choza_juncos\|casa_larga` | — | Cabañas de troncos jugables |
| Kits modulares | `objeto kit --preset almacen\|bunker\|cerca...` | — | Variación paramétrica y kits modulares |
| Cielos por hora y clima | `cielo --horas todas --climas todas`; `cielo-pintado --tipo noche\|luna_sangre\|aurora\|tormenta` | `cielo_demo`, `ht_cielo`, `ws2_cielos` | Cielos dinámicos; Cielos de juego pintados |
| LUTs por hora y clima | `lut --horas todas --climas todas` | `otra_lut` | LUTs de color grading |
| Materiales PBR sueltos (51 recetas) | `material <receta> --res 2048 --salida materiales\<receta>` | en `materiales\` | La paleta PBR |
| Vehículos de serie | `objeto vehiculo --preset pickup_serie\|suv_serie\|furgoneta_serie` | — | (Coches) |
| Mundos procedurales | `mundo --bioma pradera\|montana\|desierto\|nieve\|isla` | — | Mundos procedurales |

**Lo que no tiene** (hay que hacerlo en el juego o en el cerebro): ranas, roedores, capibara,
caimán, nutria, tapir, monos, peces y aves con rig; anacondas y boas; flora amazónica concreta
(açaí, buriti, paxiúba, *Victoria amazonica*); camiones, excavadoras, motosierras, machetes,
lanchas, dragas, hidroaviones, tiendas de campaña y barracones; arquetipos de maderero o minero
(se componen con `--vestir` y colores). El jaguar no tiene rosetas (sirven tigre, león o guepardo
como base). No hay bioma de selva en `mundo` (la saga tiene el suyo en `PasadasRelieve`).

### Llevarlo a un juego

- **Humanos**: `Guardianes.EditorTools.Enemigos.ConstructorHumanos` copia de
  `proyectos\<proyecto>\salida` el FBX animado, sus PNG, su `.clips.json` y la carpeta `.fbm`, monta
  el `AnimatorController` (locomoción, apuntar, ráfaga, huida, impacto, muerte; parámetros
  `velocidad`, `apuntar`, `disparar`, `miedo`, `golpe`, `muerto` y, si hay clip, `atacar`), pone el
  arma en el hueso `AC_arma_r`, la cápsula, la `Salud` y el `Humano`, y guarda el prefab en
  `Assets/_Game/Prefabs/Enemigos`. Encuentra AssetsCreator subiendo desde el proyecto hasta
  `AssetsCreator\AssetsCreatorWin\proyectos` (o en `%ASSETSCREATOR%`). Un humano nuevo es un `Def`:

  ```csharp
  ConstructorHumanos.ConstruirTodos(new[] {
      ConstructorHumanos.Mercenario,
      new ConstructorHumanos.Def { nombre = "gd_garimpeiro", prefab = "Garimpeiro", vida = 25f, danioBala = 2f, civil = true, animado = "_animado" },
  });
  ```
- **Armas y props** (`objeto ... --pbr`): copia `<n>_pbr.obj` y sus PNG a `Assets/_Game/Art/...`
  (como `CopiarArma`) y crea los materiales con `MaterialesLit.Crear`.
- **Materiales**: `creador.py material <receta> --salida materiales\<receta>` y después
  `Tools~/arte/empaquetar_materiales.py` al formato del juego.
- **Animales y serpientes** (FBX Generic con raíz `root`): impórtalos como Generic con
  `optimizeGameObjects = false` y monta el `Animator` con los nombres del `.clips.json`.
- Lo copiado va al repo del juego por LFS. No instales los scripts de `AssetsCreatorWin\unity\` en
  un juego de la saga salvo que haga falta uno concreto (y entonces una sola copia).

### Crear assets nuevos sin tocar su código

No hay recetas JSON genéricas ni `--params`: todo va por las opciones de cada subcomando. Una
categoría o un preset nuevos exigirían escribir código en AssetsCreator, y eso no se hace. Lo que sí
se puede, solo con parámetros:

- **Humanos**: `humano` con sus mandos (sexo, altura, edad, músculo, peso, raza, `--complexion`,
  `--cara-tipo` y 28 mandos de cara, `--vestir` con conjuntos o prendas sueltas y
  `--vestir-colores prenda=#hex`, `--ropa`, `--pelo`, `--ojos`, `--rig game_engine`); `reparto`
  para un grupo variado y reproducible (`--n 12 --semilla 11 --prefijo X`). Un nombre mal escrito
  en `--vestir`, `--ropa`, `--equipo` o `--pelo` hace fallar el comando (es lo que se quiere).
- **Animación**: `animar --proyecto X --clips ...` (unos 1300 clips procedurales en
  `ultra\clips\`: `fusil_ak_*`, `escopeta_*`, `pistola_*`, `idle`, `walk`, `run`, `sprint`,
  `agachado*`, `manos_arriba`, `encogerse`, `hit_*`, `herido_*`, `death`, `muerte_rodillas`,
  `derribo`, `talar`, `serrar`, `cavar`, `martillear`...) o `animar --personaje <fbx>` con
  cualquier FBX con rig; `--clips-dir` con FBX de Mixamo.
- **Objetos**: `objeto <categoria> --preset X --pbr 1024 [--variantes N --var-forma 0..1
  --var-desgaste 0..1 --var-color 0..1] [--lod] [--colision] [--glb]`; kits modulares
  `objeto kit --preset X --pieza Y`; formas PBR `forma --forma X --material Y --tinte "#hex"`.
- **Texturizar un OBJ propio**: modela la malla en el juego (o en el cerebro) y deja que
  AssetsCreator le ponga PBR: `creador.py hornear --entrada malla.obj --material X` o
  `ultra\texturizar_obj.py malla.obj --res 1024`. La receta se elige por el nombre del `usemtl`
  (`ultra\recetas_nombre.py`): nombra los materiales de tu OBJ con las palabras de sus reglas
  (`madera`, `metal`, `oxido`, `lona`...).
- **Lotes**: `personajes --catalogo mis.json`, `cielo-pintado --lote cielos.json`.

### Añadirlo a AssetsCreator para toda la saga

1. **Genera con un nombre con prefijo**: `--nombre gd_<asset>` si lo van a usar varios juegos,
   `<prefijo del juego>_<asset>` si es de uno (`kk_` King Kong, `bk_` Basiliskos). Queda en
   `proyectos\<nombre>\salida\` y cualquier juego lo encuentra por nombre. No pises proyectos de
   otros juegos de Odei Labs (`ht_`, `fs_`, `ws2_`, `ww2_`, `ts_`...). `kk_mercenario` y `kk_ak47`
   conservan su nombre (los usa `ConstructorHumanos`).
2. **Apunta la receta** (el comando exacto) en la tabla de abajo, en el mismo commit del cerebro:
   como `proyectos\` no se versiona, la receta es lo único que permite regenerarlo en otra máquina.
3. **Si lo usa más de un juego**, el código que lo importa va al cerebro (un `Def` en
   `ConstructorHumanos`, o un constructor nuevo en `Guardianes.Editor`), no a cada juego.
4. **Cópialo al juego** con ese constructor y commitea lo copiado (LFS) en el juego.

### Recetas de la saga

Comandos desde `AssetsCreatorWin` con `.venv\Scripts\python.exe creador.py ...`. Las de
`kk_mercenario` y `kk_ak47` están deducidas de su `mpfb_params.json`, su `.clips.json` y sus
ficheros (los colores pueden variar ±1).

| Proyecto | Para qué | Receta |
| --- | --- | --- |
| `kk_mercenario` | Mercenario de Ferrox con fusil (toda la saga; Basiliskos lo usa también para garimpeiros y civiles) | `humano --nombre kk_mercenario --sexo hombre --altura 1.8 --musculo 0.65 --peso 0.5 --vestir camiseta,calzas,botas,cinturon --vestir-colores "camiseta=#4a5230,calzas=#3d3a2c,botas=#1c1a17,cinturon=#2a2418"` y después `animar --proyecto kk_mercenario --arma fusil_ak --clips fusil_ak_idle,fusil_ak_apuntar_idle,fusil_ak_apuntar,fusil_ak_rafaga,fusil_ak_disparar,fusil_ak_recargar,fusil_ak_andar,run,sprint,agachado_rapido,hit_bala,hit_frontal,death,muerte_rodillas,derribo` |
| `kk_ak47` | El AK del mercenario | `objeto arma --preset ak47 --nombre kk_ak47 --pbr 1024` |
| `bk_yara` | Yara, la pescadora yawari (Basiliskos) | `humano --nombre bk_yara --sexo mujer --altura 1.58 --edad 0.4 --musculo 0.55 --peso 0.42 --raza asian --piel-mhmat young_asian_female --pelo long01 --ojos darkbrown --vestir camiseta_tirantes,pantalon_corto --vestir-colores "camiseta_tirantes=#a8492a,pantalon_corto=#3b3a2a" --expresion seria` y después `animar --proyecto bk_yara --clips idle,walk,run,sprint,hit_frontal,death,encogerse` |
| `bk_iracema` | Avó Iracema, la pajé yawari (Basiliskos) | `humano --nombre bk_iracema --sexo mujer --altura 1.5 --edad 0.85 --musculo 0.3 --peso 0.5 --raza asian --piel-mhmat old_asian_female --pelo long01 --ojos darkbrown --vestir camiseta,falda_larga --vestir-colores "camiseta=#6e2a1c,falda_larga=#2f2418" --expresion seria` y después `animar` con los mismos clips |
| `bk_yawari` | Un hombre de la aldea yawari (Basiliskos) | `humano --nombre bk_yawari --sexo hombre --altura 1.64 --edad 0.45 --musculo 0.65 --peso 0.45 --raza asian --piel-mhmat young_asian_male --pelo short02 --ojos darkbrown --vestir pantalon_corto --vestir-colores "pantalon_corto=#5a3a1e" --barba ninguna` y después `animar` con los mismos clips |
| `bk_maderero` | Maderero de Ferrox, desarmado (Basiliskos) | `humano --nombre bk_maderero --sexo hombre --altura 1.76 --edad 0.55 --musculo 0.6 --peso 0.6 --raza mixta --piel-mhmat middleage_caucasian_male --pelo short03 --vestir camiseta,pantalon,botas,cinturon --vestir-colores "camiseta=#8a2b1f,pantalon=#2c3a4a,botas=#2a1e14,cinturon=#1e1a14" --barba corta` y después `animar` con los mismos clips |
| `bk_varga` | La doctora Elena Varga, bióloga (Basiliskos) | `humano --nombre bk_varga --sexo mujer --altura 1.70 --edad 0.65 --musculo 0.5 --peso 0.45 --raza caucasian --piel-mhmat middleage_caucasian_female --pelo short02 --ojos green --vestir camiseta,pantalon,botas,cinturon --vestir-colores "camiseta=#7a6a48,pantalon=#3a4230,botas=#3a2a1c,cinturon=#2a2016" --expresion seria` y después `animar` con los mismos clips |

### Trampas al llevarlo a Unity HDRP

1. **Humanoid o Generic**: los cuadrúpedos, las serpientes y los FBX con arma reconocida son
   Generic con raíz `root`. `fusil_ak` no se reconoce como arma de impacto: el FBX del mercenario
   sale `humanoid` y sin `AC_punta`; por eso `ConstructorHumanos` fuerza Generic y saca la bocacha
   de la malla del arma. Si un proyecto tiene instalado `AssetsCreatorClips.cs`, forzará Humanoid en
   la primera importación y chocará con nuestro constructor.
2. **Huesos de anclaje sin peso** (`AC_arma_*`, `AC_punta_*`): con `optimizeBones` u *Optimize Game
   Objects* Unity los quita. Hay que apagarlos.
3. **`.clips.json`**: `nombre` tiene que coincidir con la toma del FBX (si se repiten, Blender pone
   `.001` y deja de casar). `JsonUtility` descarta en silencio los campos que no conoce.
4. **La carpeta `.fbm`** viaja junto al FBX; sin ella la piel sale gris.
5. **El MTL apunta a `_basecolor`**: si renombras mapas, reescríbelo. Si un proyecto trae OBJ, GLB y
   FBX a la vez, Unity importa tres modelos: copia solo uno.
6. **El material se elige por el nombre** (subcadena, gana la primera regla): un nombre que no casa
   cae al material por defecto sin error (`hueso` sale adobe, `vela` tela...).
7. **Ejes**: todo en metros con Y arriba, pero el AK low-poly está tumbado a lo largo de X; cuernos y
   colmillos crecen por +Y y se curvan hacia +Z; las puertas tienen el origen en la bisagra; el
   importador de Unity espeja X.
8. **Caras traseras**: Unity no las pinta; las piezas son volúmenes cerrados. Un plano suelto (un
   charco) desaparece.
9. **Texturas especiales**: `_grosor` de las hojas es el Thickness Map de HDRP tal cual; los EXR de
   `cielo` son cubemaps lat-long BC6H; la noche de `cielo` cae en subnormales del half (para una
   noche jugable, `cielo-pintado` o el `CicloDia` del cerebro).
10. **Partículas**: una partícula unlit de color 1 sale negra a mediodía en HDRP: hay que pintarla
    en nits (como `Guardianes/Particula`).
11. **Variantes del Lit**: se generan contra el `Lit.shader` del HDRP de cada proyecto; una copia de
    otra versión no compila.
12. **Errata del README**: `creador.py kit ...` no existe; es `creador.py objeto kit ...`.
