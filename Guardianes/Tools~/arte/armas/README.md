# Kit de armas de fuego realistas (gd_*)

Armas de fuego modeladas a medida real contra las láminas de referencia de AssetsCreator y texturizadas
con su texturizador PBR, **sin tocar AssetsCreator**: de él solo se leen las láminas
(`AssetsCreatorWin\referencias\armas\armas de fuego\NNN-<arma>\`, cuatro vistas: `lateral`,
`tres_cuartos`, `detalle_empunadura`, `detalle_boca`) y se usa `ultra\texturizar_obj.py`. Cada arma
es un fichero `modelos/gd_<arma>.py`; el resultado queda en `AssetsCreatorWin\proyectos\gd_<arma>\`
con los mismos nombres que un `objeto --pbr`, más su ficha (`_agarre.json`): en Unity lo trae
`ConstructorHumanos.TraerArma` (el OBJ, la ficha, el `.mtl` sin texturas, el color base de cada material y un
HDRP/Lit por material) y lo monta en la mano con su ficha (`ConstructorHumanos.LeerFicha`), no con la regla del
`kk_ak47`. Sirve a toda la saga (prefijo `gd_`).

| Fichero | Dónde corre | Qué hace |
| --- | --- | --- |
| `construir_arma.py` | Python del `.venv` de AssetsCreator | Lo lanza todo: contornos de la lámina, modelo, texturas, `_agarre.json`, comparación y hoja de contactos |
| `kit_armas.py` | Blender 5.1 | Las funciones de modelado (la API de abajo) y la exportación OBJ |
| `blender_arma.py` | Blender 5.1 | Carga el modelo, lo exporta y hace los renders (lado, tres cuartos, preview, hoja) |
| `laminas.py` | Python del `.venv` | Silueta, caja y contornos de una lámina; rejillas para leer coordenadas |
| `modelos/gd_<arma>.py` | Blender (vía el kit) | Un arma: `FICHA` y `modelar(k)` |

## Lanzarlo

Desde cualquier carpeta, con el Python de AssetsCreator (tiene numpy, scipy, Pillow y scikit-image):

```powershell
$py  = "C:\Users\inigo\Desktop\Odei Labs\AssetsCreator\AssetsCreatorWin\.venv\Scripts\python.exe"
$kit = "C:\Users\inigo\Desktop\Odei Labs\Gaia's Response\cerebro\Guardianes\Tools~\arte\armas"
& $py "$kit\construir_arma.py" gd_glock17                    # todo: ~35 s (3 s el modelo, 30 las texturas)
& $py "$kit\construir_arma.py" gd_glock17 --solo modelo,comparar --revision C:\tmp\armas   # iterar: 5 s
& $py "$kit\construir_arma.py" --hoja gd_glock17,kk_ak47,gd_mp5 --salida C:\tmp\hoja_armas.png
& $py "$kit\laminas.py" rejilla 030-glock-17/lateral C:\tmp\rejilla.png --paso 10 --recorte 500,240,1000,600 --zoom 2
```

- Pasos (`--solo`, separados por comas): `laminas`, `modelo`, `texturas`, `agarre`, `comparar`.
  Sin `--solo`, todos. `--res 512|1024|2048` cambia la resolución de los mapas (la de la FICHA, de serie).
- Blender: `--blender`, `%BLENDER%` o el más nuevo de `C:\Program Files\Blender Foundation`.
  Corre en segundo plano (`-b --factory-startup`); no hace falta cerrar otros Blender.
- `--origen linea` deja el origen en la x del agarre pero con la línea de tiro en y = 0 (ver *Convención*).
- AssetsCreator se encuentra en `%ASSETSCREATOR%` o subiendo desde esta carpeta (como `ConstructorHumanos`).

## Dónde deja cada cosa

En `AssetsCreatorWin\proyectos\gd_<arma>\` (una carpeta nueva, como la de un comando de AssetsCreator;
`proyectos\` no se versiona: la receta es el comando de arriba):

- `salida\` — `gd_<arma>.obj/.mtl` (el modelo con su Kd, UV propias sin solapes y `.veta.json` si hay
  maderas), `gd_<arma>_preview.png`, y lo que deja el texturizador: `gd_<arma>_pbr.obj/.mtl`,
  `gd_<arma>_pbr_<material>_{basecolor,normal,roughness,ao,height[,metallic]}.png` (+ `_material.json`)
  y `gd_<arma>_pbr_preview.png`. Además `gd_<arma>_agarre.json`.
- `trabajo\` — los contornos de la lámina (`_lamina_<vista>.json`), `_medidas.json`, los renders con
  fondo transparente (`_lado.png` ortográfico a píxeles por metro conocidos, `_tres_cuartos.png`), los
  logs de Blender y del texturizador, `kit_armas.txt` (la marca del kit) y, sin `--revision`, el
  `_comparar.png`.

**Seguridad**: el kit solo escribe en `proyectos\gd_*` y solo si la carpeta es nueva o lleva la marca
`trabajo\kit_armas.txt`; nunca pisa un proyecto de otro. Antes de texturizar borra los `gd_<arma>_pbr*`
de la pasada anterior (un material renombrado dejaría mapas viejos en la salida). La hoja
de contactos solo lee los `<n>_pbr.obj` de otros proyectos.

## Convención (la del AK de la saga)

- Metros reales. Cañón hacia **+X**, arriba **+Y**, grosor en **Z**; **+Z es el lado derecho del
  tirador** (la ventana de expulsión de la Glock) y −Z el izquierdo, el que enseñan las láminas laterales.
- **El origen es el agarre de la mano derecha**: el centro de la empuñadura (o del cuello de la
  culata) a la altura donde cierra el puño, en la de una pistola el dedo corazón. La línea de tiro va
  paralela a +X por z = 0, a la altura `boca.y` sobre el agarre (en la Glock, 74 mm). Si un juego
  prefiere la línea de tiro en y = 0, `--origen linea` (y lo apunta en la receta).
- Un solo objeto en el OBJ (`o gd_<arma>`; el texturizador lo llama `gd_<arma>_pbr`), triángulos,
  normales por esquina: aristas de más de 40° duras, el resto suaves y ponderadas por área (las caras
  planas siguen planas junto a los biseles). Todas las aristas vivas biseladas: lee a distancia.
- Presupuesto: de 2.000 a 12.000 triángulos (la Glock, 7.600).
- **En Unity** el importador de OBJ cambia el signo de X: allí el cañón es −X y la x de `boca` y
  `guardamanos` cambia de signo (lo hace `ConstructorHumanos.LeerFicha` al leer la ficha).

### `gd_<arma>_agarre.json`

```json
{ "nombre": "gd_glock17", "clase": "Pistola", "lamina": "030-glock-17", "unidades": "m",
  "largo": 0.19735, "alto": 0.13812, "ancho": 0.03055,
  "agarre": [0.0, 0.0, 0.0], "boca": [0.15761, 0.07394, 0.0], "guardamanos": null,
  "ejes": {"canon": "+X", "arriba": "+Y"}, "linea_tiro": {"y": 0.07394, "z": 0.0},
  "caja": {"min": [...], "max": [...]}, "triangulos": 7600, "vertices": 3834,
  "materiales": {"acero_canon": "metal", "pintura_corredera": "pintura_mate", "plastico_armazon": "plastico"},
  "receta": "\"...\\.venv\\Scripts\\python.exe\" \"...\\construir_arma.py\" gd_glock17 --res 1024" }
```

`clase`: `Pistola`, `Escopeta`, `Subfusil`, `FusilAsalto`, `FusilCaza` o `Ametralladora` (las de
`Arma.ClaseArma`). `guardamanos`: donde apoya la mano izquierda (el centro del guardamanos, el
cargador de un MP 40, la empuñadura delantera de una Thompson, la empuñadura izquierda de una M2);
`null` si es de una mano. `materiales`: la receta que el texturizador ha dado a cada uno.

## Cómo se modela un arma nueva contra sus láminas

1. **Mira las cuatro vistas** (`Read` ve los PNG): qué piezas hay, de qué material, qué grosor
   tiene cada una (el `detalle_boca` y el `tres_cuartos` dan los anchos).
2. **Busca las medidas reales** (largo total o de una pieza conocida, alto, ancho, cañón). Las láminas
   son imágenes generadas, no planos: **pueden venir estiradas**. La de la Glock lo está un 17 % a lo
   largo (a la escala de su largo mide 118 mm de alto; la Glock 17 mide 138 con cargador y miras; los
   128 que se suelen citar son de la Glock 19). Por eso la calibración lleva dos escalas.
3. **Saca una rejilla** de la lateral y recortes ampliados (`laminas.py rejilla ... --recorte x0,y0,x1,y1
   --zoom 2 --paso 10`) y apunta en píxeles de la lámina lo que vayas a dibujar a mano. La rejilla
   pinta en cian el contorno que el kit sacará solo.
4. **Copia `modelos/gd_glock17.py`** como plantilla: `FICHA` arriba (sin importar `bpy`: la lee
   `construir_arma.py`) y `modelar(k)`, que devuelve `{"agarre", "boca", "guardamanos", "calibracion"}`.
5. **Calibra**: `cal = k.Calibracion(px_boca, px_cola, largo, py_arriba, py_abajo, alto, py_eje)`: dos
   referencias a lo largo con sus metros, dos en alto con los suyos, y el píxel de la línea de tiro (el
   cero de y). `cal.p(px, py)`, `cal.puntos([...])`, `cal.x`, `cal.y`, `cal.dx`, `cal.dy`.
6. **Las piezas grandes, de la silueta**: `k.silueta(cal, region=[...píxeles...], ancho=...)` extruye el
   contorno exterior de la lámina recortado por una región: la forma exacta sin dibujarla. Cada zona de
   distinto grosor es una región (en la Glock: armazón 25 mm, guardamonte 14,5, empuñadura 29, base del
   cargador 27). **Haz coincidir los bordes de las regiones con líneas reales del arma** (la pared del
   guardamonte, el borde del abultamiento de la palma): un corte en mitad de una cara deja un escalón
   que se ve. Una pieza estrecha que se meta 10-15 px dentro de la ancha no deja rendija.
7. **Las demás, a mano**: `perfil` (lateral, sección o planta), `perfil(..., plano="zy")` intersecado con
   el lateral para las secciones con chaflanes (la corredera), `torno` para cañones, bocachas, tambores,
   visores y pomos, `cilindro`/`caja`/`esfera`/`barrido` para pasadores, palancas y anillas. Los huecos
   (guardamonte, ventana de expulsión, estrías, ánima) con `restar`; las repeticiones con `repetir`.
8. **Bisela** cada pieza (`bisel=` al crearla o `biselar` después de las booleanas): 0,3-0,6 mm en
   miras y mandos, ~1 mm en correderas y cajones, 1,5 en armazones, 3-5 mm en empuñaduras y culatas.
9. **Itera** con `--solo modelo,comparar` (5 s) y mira el `_comparar.png` hasta que case: la IoU de
   siluetas «ajustada a la caja» por encima de 0,95 (la Glock da 0,992) y el tres cuartos parecido. La
   IoU «solo por el largo» mide cuánto está estirada la lámina, no el modelo.
10. **Texturiza** (la pasada completa) y mira el `_pbr_preview.png` (es del lado derecho). Mira la hoja
    de contactos con otras armas para ver la escala.
11. **Apunta la receta** en la tabla *Recetas de la saga* del `README.md` del cerebro, en el mismo
    commit que el modelo.

## La API del kit (`k` dentro de `modelar`)

Todo en metros (`k.mm(v)` pasa milímetros) y en los ejes de la convención. Cada función que crea
una pieza devuelve un objeto de Blender con la malla en su sitio y un material; las operaciones se
aplican en el acto; al final `terminar` une todas las piezas vivas.

| Función | Para qué |
| --- | --- |
| `material(nombre, color="#hex", rugosidad, metal, veta=None)` | Un material: el nombre elige la receta del texturizador, el color es el Kd con que se tiñe. `veta=(1,0,0)`: la fibra de una madera a lo largo del cañón (va al `.veta.json`) |
| `perfil(puntos, ancho=, centro=0 \| desde=, hasta=, plano="xy"\|"zy"\|"xz", material, bisel, segmentos, angulo)` | Extruye un polígono 2D: `xy` el perfil lateral (en Z), `zy` la sección vista desde la boca (u = z; en X), `xz` la planta (en Y) |
| `silueta(cal, vista="lateral", region=[px], ancho=, ...)` | El contorno de la lámina, recortado por una región en píxeles |
| `torno([(a, r), ...], eje="x", origen, segmentos, cerrado=False, fase)` | Revolución: r = 0 cierra en un polo; extremos fuera del eje, tapados; `cerrado`, un tubo con ánima |
| `cilindro(p0, p1, radio, segmentos, radio1=None)` | Cilindro o tronco de cono entre dos puntos (pasadores, y la herramienta de los agujeros) |
| `caja(min, max)`, `esfera(centro, r)`, `barrido(puntos3d, radio, radios=None)` | Cajas, pomos, palancas y anillas (un tubo por una polilínea) |
| `redondear(puntos, radios, segmentos)`, `arco(...)`, `bezier(...)` | Esquinas redondeadas (un radio, una lista o `{índice: radio}`), arcos y curvas en 2D |
| `Calibracion(...)`, `lamina(vista)` | Píxeles de la lámina a metros; los contornos de una vista (`.exterior`, `.huecos`, `.caja`) |
| `restar(pieza, *herramientas)`, `intersecar(pieza, otra)` | Booleanas EXACT; borran las herramientas; las caras del corte llevan el material de la pieza |
| `unir(*piezas)` | Junta mallas sin booleana (las piezas pueden solaparse) |
| `biselar(pieza, ancho, segmentos=2, angulo=30)` | Bisel de las aristas de más de `angulo` grados (con *clamp*) |
| `deformar(pieza, f)`, `mover`, `girar(pieza, grados, eje, centro)`, `escalar`, `copiar`, `espejo(pieza, "z")`, `repetir(pieza, n, paso)`, `quitar` | Retoques; `espejo` hace la pieza del otro lado; `repetir` devuelve una sola pieza |
| `azar` | `random.Random` sembrado con `FICHA["semilla"]`: todo lo aleatorio, por aquí |

Ejemplo corto (un cañón con ánima, una corredera achaflanada con estrías y un agujero):

```python
FICHA = {"nombre": "gd_ejemplo", "clase": "Pistola", "lamina": "030-glock-17", "semilla": 1,
         "texturas": {"res": 1024, "escala": 1.0}, "vista_34": {"azimut": 28, "elevacion": 12, "focal": 100}}

def modelar(k):
    mm = k.mm
    negro = k.material("pintura_corredera", "#2c2d30", rugosidad=0.7)
    acero = k.material("acero_canon", "#303133", metal=1.0)
    lado = k.redondear([(0, mm(12)), (-mm(186), mm(12)), (-mm(186), -mm(13)), (0, -mm(13))], mm(1.5))
    corredera = k.perfil(lado, ancho=mm(27), material=negro)
    seccion = [(-mm(12.75), -mm(13)), (mm(12.75), -mm(13)), (mm(12.75), mm(7)), (mm(8), mm(12)),
               (-mm(8), mm(12)), (-mm(12.75), mm(7))]
    k.intersecar(corredera, k.perfil(seccion, plano="zy", desde=-mm(190), hasta=mm(2)))
    k.biselar(corredera, mm(0.9))
    estria = k.caja((-mm(160), -mm(14), mm(11.9)), (-mm(157), mm(8), mm(14)))
    estrias = k.repetir(estria, 7, (-mm(4.75), 0, 0))
    k.restar(corredera, k.unir(estrias, k.espejo(estrias)), k.cilindro((mm(1), 0, 0), (-mm(3), 0, 0), mm(7.6)))
    k.torno([(-mm(12), 0), (-mm(12), mm(7)), (mm(2), mm(7)), (mm(2), mm(4.6)), (-mm(8), mm(4.6)), (-mm(8), 0)],
            material=acero)
    return {"agarre": (-mm(155), -mm(74), 0), "boca": (mm(2), 0, 0), "guardamanos": None}
```

### La FICHA

`nombre`, `clase`, `lamina` (la carpeta, `030-glock-17`), `para` (quién la usa), `medidas_reales`,
`texturas` (`res`, `escala`: metros por tesela; **todas las gd_ van a 0,4**, la misma densidad de textura: con 1,0, la
de los `objeto` de AssetsCreator, la veta del nogal y el grano salen 2,5 veces más gordos y borrosos de cerca, y
juntas en la mano no casaban), `vista_34`
(`azimut` desde −Z hacia +X, `elevacion`, `focal`: la cámara que imita la lámina de tres cuartos),
`semilla`, y opcionales `umbral` (22: lo que se aparta del gris de estudio para ser arma; súbelo si
coge sombras, bájalo si pierde maderas claras), `suavizado` (2 px: se lleva los dientes del picado,
deja los huecos de los dedos), `tolerancia` (1,5 px) y `angulo_suave` (40°).

## Las recetas del texturizador (por el nombre del material)

`ultra\recetas_nombre.py` (de AssetsCreator, solo se lee) elige la receta por subcadena y **gana la
primera regla**; `construir_arma.py` la comprueba y la apunta en el `_agarre.json`. Lo medido con
`texturizar_obj.py` (color medio del basecolor con un Kd casi negro):

| Nombre con... | Receta | Qué sale |
| --- | --- | --- |
| `pintura_coche` (antes que `pintura`) | `pintura_coche` | El Kd liso con laca y brillo (rugosidad 0,38), dieléctrico, con escamas metálicas muy finas. **El pavonado brillante**: cañones y cajones de las escopetas y del Remington, el revólver (pavonado y cementado) |
| `pintura`, `pintado`, `esmalte_`, `camuflaje` | `pintura_mate` | El Kd tal cual, mate (rugosidad 0,73), dieléctrico. **El negro mate de una corredera, un pavonado oscuro o un parkerizado** |
| `pavonado`, `parkerizado`, `bronido` | `metal` (tinte 0,5) | Acero gris claro (~135 sRGB) aunque el Kd sea negro: el tinte no baja de la mitad del F0 |
| `acero`, `metal`, `hierro`, `rail`, `chapa` | `metal` (tinte 0,5) | Lo mismo: acero desnudo (corona del cañón, cerrojos pulidos, pasadores) |
| `cascarilla` | `cascarilla` | Hierro forjado casi negro con calvas: armas antiguas (arcabuz, pedernal) |
| `plastico`, `detalle`, `placa`, `banda` | `plastico` | El Kd, satinado (rugosidad 0,3): polímeros, cargadores, cachas sintéticas |
| `goma`, `neumatico` | `goma` | Cantoneras y cachas de goma |
| `madera`, `culata`, `tablero`, `mango` | `madera` (tinte 0,4) | Madera clara con su color; `madera_nogal_*` tiñe al 0,85 (nogal oscuro) |
| `cuero`, `correa`, `funda`, **`empunadura`** | `cuero_curtido` | **Cuidado: «empunadura» va a cuero**; una empuñadura de polímero se llama `plastico_...` |
| `bronce` / `oro`, `dorado` / `bronce_patinado`, `cardenillo` | `laton` / `oro` / `cobre` | Guarniciones de armas antiguas (`laton` y `cobre` a secas **no** casan) |

Lo que no casa con ninguna regla (`polimero`, `armazon`, `laton`...) cae a `plastico` sin avisar (el kit
lo apunta en el `_agarre.json` como «plastico (por defecto)»). Tres materiales por arma suelen bastar
(cada uno son 5-6 mapas).

## Trampas

- **El texturizador rehace las UV**: proyección de caja en unidades de mundo (1 tesela = `escala` m),
  teselable, así que en el `_pbr.obj` las UV se solapan a propósito. Las UV sin solapes (smart project)
  están en el `<n>.obj` base, por si hacen falta (lightmaps, calcas). Conserva las normales y las caras
  una a una.
- **Las láminas pueden estar estiradas** (ver el paso 2). El `_comparar.png` lo dice: «la lámina a la
  escala del largo mide N mm de alto» y la deformación de la calibración.
- **Los contornos automáticos** salen de una silueta contra el gris de estudio: una pieza clara (madera
  rubia, niquelado) o un brillo fuerte pueden comérsela; mira el cian de la rejilla y ajusta `umbral`.
  El gatillo queda dentro del hueco del guardamonte: el hueco se dibuja a mano (completo) y el gatillo
  es una pieza aparte (`perfil` con los puntos que el contorno automático deja dentro del hueco).
- **Las booleanas**: EXACT (aguanta caras coplanarias). Varias herramientas que no se tocan, juntas
  (`unir`) en una sola resta. Biselar antes de restar deja los agujeros con arista viva (bien para
  pasadores y estrías); biselar después redondea también los cortes (bien para el guardamonte).
- **Los renders de revisión** son de Workbench con el Kd aclarado (×2,6) y sin metal, para leer la forma;
  el aspecto real es el del `_pbr_preview.png` (que el texturizador hace desde el lado derecho).
- **AssetsCreator lo usan otras sesiones a la vez**: si el texturizador falla (su código puede estar a
  medio cambiar), `construir_arma.py` espera 90 s y reintenta una vez; si vuelve a fallar, para y lo dice
  (log en `trabajo\texturizar.log`).

## Armas

| Arma | Lámina | Clase | Medidas (largo × alto × ancho) | Triángulos | Para |
| --- | --- | --- | --- | --- | --- |
| `gd_glock17` | 030-glock-17 | Pistola | 197 × 138 × 30,5 mm (corredera 186, cañón 114) | 7.600 | Capataces y mercenarios de Basiliskos; cualquier pistola moderna de la saga |
| `gd_colt_saa` | 008-colt-single-action-army | Pistola | 279 × 128 × 42 mm (cañón de 5½", 139,7) | 11.018 | Ze Dourado, el capataz del garimpo; cualquier revólver de acción simple |
| `gd_mp5` | — (foto del MP5A3 de la Small Arms Survey) | Subfusil | 680 (510 con la culata metida) × 271 × 51 mm (cañón 225) | 11.672 | Traficantes de Basiliskos; cualquier subfusil moderno |
| `gd_ak47` | 021-ak-47 | FusilAsalto | 870 × 275 (191 sin cargador) × 56 mm (cañón 415) | 11.542 | Mercenarios de Ferrox y Comadrejas (sustituye a `kk_ak47`) |
| `gd_escopeta_corredera` | 026-escopeta-corredera | Escopeta | 1.181 × 218 × 46 mm (cañón de 26", 660) | 11.866 | Garimpeiros, cazadores e incendiarios; cualquier escopeta de corredera |
| `gd_escopeta_yuxtapuesta` | 027-escopeta-yuxtapuesta | Escopeta | 1.119 × 210 × 58 mm (cañones de 26", 660) | 11.438 | El Vigilante y el furtivo; cualquier escopeta de dos cañones |
| `gd_remington700` | 028-remington-700 | FusilCaza | 1.079 × 229 × 68 mm (cañón de 22") | 11.990 | Vigías, Comadrejas y Brandão; cualquier fusil de cerrojo con visor |
| `gd_m2_browning` | 029-m2-browning | Ametralladora | 1.654 × 330 × 230 mm (cañón de 45", 1.143; sin el pie del pedestal) | 10.504 | La 12,7 de la torreta del blindado de Brandão (la 34) |

**Cómo se rehace cada una** (con el Python del `.venv` de AssetsCreator; `--revision <carpeta>` deja las comparaciones
fuera del proyecto). La Glock, con `construir_arma.py gd_glock17 --res 1024`. Las demás, con su propio fichero, que
usa los pasos del kit y añade lo que el kit no tiene: `modelos/gd_<arma>.py --res 1024` (las láminas con la boca a la
derecha —todas menos la de la Glock— se voltean para comparar; la del MP5 no está en AssetsCreator y
`construir_arma.py gd_mp5` falla; la M2 escribe su anexo de rugosidad entre el modelo y las texturas, que el paso del
modelo del kit borra). El Colt: `construir_arma.py gd_colt_saa --res 1024 --solo laminas,modelo,texturas,agarre` y
después `modelos/gd_colt_saa.py comparar`. Con `construir_arma.py <arma>` a secas salen el mismo modelo y las mismas
texturas (salvo en la M2 y el MP5), pero la comparación con la lámina sin voltear.
