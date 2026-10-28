# Kit de armas de fuego realistas (gd_*): vive en AssetsCreator

Desde el 28/10/2026 el kit de armas de fuego realistas de la saga (las ocho `gd_`: `gd_glock17`,
`gd_colt_saa`, `gd_mp5`, `gd_ak47`, `gd_escopeta_corredera`, `gd_escopeta_yuxtapuesta`,
`gd_remington700` y `gd_m2_browning`) vive en AssetsCreator, en `AssetsCreatorWin\generador\armas`
(commit `ae070e2`), como un generador más. **Aquel es el original**: los cambios a las armas (una
nueva, otra medida, otro material) se hacen allí, no aquí, y la receta de una nueva se apunta también
en *Recetas de la saga* del `README.md` del cerebro. Esta carpeta solo dice dónde está, para que no
haya dos copias que se separen. La convención, la ficha, la API de modelado y cómo se modela
un arma contra sus láminas están en su `README.md` (`generador\armas\README.md`).

## Construir un arma

Desde `AssetsCreatorWin`, con el Python del `.venv`:

```powershell
.venv\Scripts\python.exe creador.py arma --lista                          # las que hay, su lámina y si están hechas
.venv\Scripts\python.exe creador.py arma gd_glock17 --res 1024            # entera: modelo, texturas, ficha y comparación
.venv\Scripts\python.exe creador.py arma gd_m2_browning --solo agarre     # rehacer solo la ficha, sin Blender
.venv\Scripts\python.exe creador.py arma --hoja gd_glock17,gd_mp5,gd_ak47 # hoja de contactos a la misma escala
```

Pasos de `--solo` (por comas): `laminas`, `modelo`, `texturas`, `agarre`, `comparar`. `creador.py
arma` sabe si cada una va con el kit o con el fichero de su modelo (el Colt, con su comparación
aparte): ya no hay que lanzarlas distinto.

## Dónde queda y cómo la usa la saga

En `AssetsCreatorWin\proyectos\gd_<n>\salida\` (`proyectos\` no se versiona): el OBJ, el `_pbr.obj`
con sus mapas y la ficha `<n>_agarre.json` (agarre, boca, guardamanos, ejes, clase, la receta del
texturizador de cada material, el montaje de la M2 y la receta del arma, `creador.py arma <n> --res
1024`). En los juegos la trae `ConstructorHumanos.TraerArma` del paquete a `Art/Humanos/Armas/<n>` y
la monta con su ficha (`LeerFicha`, y `PonerArma` o `InstanciarArma`, en la mano o fuera de ella).

## Lo que AssetsCreator lee de aquí

`modelos\gd_mp5.py` de AssetsCreator consulta este paquete (si lo encuentra: `%GUARDIANES%` o
`..\..\Gaia's Response\cerebro\Guardianes` desde `AssetsCreatorWin`). Busca `"gd_mp5"` en
`Runtime/Enemigos/Arma.cs` (el prefijo de `Arma.PrefijosSerie`) y `FichaArma LeerFicha(` en
`Editor/Enemigos/ConstructorHumanos.cs`, y lo apunta en el bloque `unity` de la ficha del MP5. Si se
renombran aquí, hay que cambiarlo allí: si no, la ficha dice «falta» y `creador.py arma gd_mp5` da un
AVISO. Si se mueve o se renombra uno de esos dos ficheros, también, pero entonces no avisa: la ficha
dice «no encuentro el fichero», como en una máquina sin el paquete, y si es `Arma.cs`, los tests de
AssetsCreator se saltan la ficha del MP5.

## Historial

El kit se hizo aquí: entró en `c45e9a1` y así siguió en `9f50a2a`, del que salió la copia de
AssetsCreator. Si hace falta, está en git (`git show c45e9a1 -- "Guardianes/Tools~/arte/armas"`).
`Tools~` no lo importa Unity: no hay `.meta`.
