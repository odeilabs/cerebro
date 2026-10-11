"""Traer cambios del cerebro a la copia del paquete comun de un juego (y subir los arreglos de la copia).

Cada juego lleva su propia copia de com.odeilabs.guardianes en <Proyecto>/Packages/. Junto a ella,
`.sincronizacion.json` (Unity no importa los ficheros que empiezan por punto) guarda la foto de la
ultima sincronizacion: el hash de cada fichero tal como quedo. Comparando esa foto con el cerebro y
con la copia se sabe, fichero a fichero, que ha cambiado en el cerebro (se puede traer), que ha
cambiado en la copia (se deberia subir al cerebro) y que ha cambiado en los dos (conflicto: se mira
a mano). Los .meta van siempre con su fichero (los GUID los usan las escenas).

Uso (desde la carpeta del juego o con --copia):
  python traer_cerebro.py estado                       que hay de nuevo en cada lado
  python traer_cerebro.py diferencias Runtime/X.cs     el diff de un fichero (cerebro frente a copia)
  python traer_cerebro.py traer Runtime/Sensacion      trae del cerebro esos ficheros o carpetas
  python traer_cerebro.py traer --todo                 trae todo lo que solo ha cambiado en el cerebro
  python traer_cerebro.py subir Runtime/Humano.cs      sube al cerebro un arreglo hecho en la copia
  python traer_cerebro.py iniciar --commit aa9c189     crea la foto de una copia que no la tiene

Opciones: --copia <Proyecto>/Packages/com.odeilabs.guardianes (por defecto la busca bajo la carpeta
actual), --cerebro <cerebro>/Guardianes (por defecto el cerebro hermano del juego), --confirmado
(compara con lo commiteado en el cerebro, HEAD, en vez de con lo que hay en disco), --simular (dice
lo que haria sin tocar nada), --forzar (pisa tambien los conflictos). Nada hace commits: despues se
prueba el juego en batch y se commitea en su repo (y en el cerebro, lo que se suba).
"""
import argparse, datetime, difflib, hashlib, json, os, shutil, subprocess, sys

FOTO = '.sincronizacion.json'
PROPIOS = {FOTO, 'ORIGEN.md', 'ORIGEN.md.meta'}      # de cada copia: nunca se traen ni se suben
IGNORAR_DIRS = {'__pycache__', '_revision', '.git'}
BINARIOS = {'.png', '.jpg', '.jpeg', '.tga', '.exr', '.psd', '.wav', '.ogg', '.mp3', '.fbx', '.obj',
            '.bytes', '.asset', '.dll', '.so', '.ttf', '.otf'}


# ---------------------------------------------------------------------------- utilidades
def hash_de(datos, ruta):
    """SHA-1 del contenido, con los finales de linea normalizados en los ficheros de texto (git en
    Windows los cambia al sacar los ficheros y no queremos falsos cambios)."""
    if os.path.splitext(ruta)[1].lower() not in BINARIOS:
        datos = datos.replace(b'\r\n', b'\n')
    return hashlib.sha1(datos).hexdigest()


def git(cerebro_repo, *args, binario=False):
    r = subprocess.run(['git', '-C', cerebro_repo] + list(args), capture_output=True)
    if r.returncode != 0:
        raise SystemExit('git ' + ' '.join(args) + ': ' + r.stderr.decode('utf-8', 'replace').strip())
    return r.stdout if binario else r.stdout.decode('utf-8', 'replace')


def listar_disco(raiz):
    """{ruta relativa con '/': bytes} de una carpeta (sin los ficheros propios de la copia)."""
    fich = {}
    for d, dirs, files in os.walk(raiz):
        dirs[:] = [x for x in dirs if x not in IGNORAR_DIRS]
        for f in files:
            rel = os.path.relpath(os.path.join(d, f), raiz).replace(os.sep, '/')
            if rel in PROPIOS or f.endswith('.pyc'):
                continue
            fich[rel] = os.path.join(d, f)
    return fich


class Cerebro:
    """El cerebro como fuente: lo que hay en disco o lo commiteado (--confirmado)."""

    def __init__(self, guardianes, confirmado):
        self.raiz = os.path.abspath(guardianes)
        self.repo = os.path.dirname(self.raiz)
        self.confirmado = confirmado
        self.commit = git(self.repo, 'rev-parse', '--short', 'HEAD').strip()
        if confirmado:
            nombres = git(self.repo, 'ls-tree', '-r', '--name-only', 'HEAD', 'Guardianes').splitlines()
            self.ficheros = {n[len('Guardianes/'):]: n for n in nombres
                             if n[len('Guardianes/'):] not in PROPIOS
                             and not any(p in IGNORAR_DIRS for p in n.split('/'))}
            self.sucio = False
        else:
            self.ficheros = listar_disco(self.raiz)
            self.sucio = bool(git(self.repo, 'status', '--porcelain', '--', 'Guardianes').strip())

    def leer(self, rel):
        if self.confirmado:
            return git(self.repo, 'show', 'HEAD:' + self.ficheros[rel], binario=True)
        with open(self.ficheros[rel], 'rb') as f:
            return f.read()

    def version(self):
        return self.commit + (' + cambios sin commitear' if self.sucio else '')


class Copia:
    def __init__(self, raiz):
        self.raiz = os.path.abspath(raiz)
        self.ficheros = listar_disco(self.raiz)

    def leer(self, rel):
        with open(self.ficheros[rel], 'rb') as f:
            return f.read()


def buscar_copia(desde):
    candidatos = []
    for d, dirs, files in os.walk(desde):
        dirs[:] = [x for x in dirs if x not in IGNORAR_DIRS | {'Library', 'Logs', 'Temp', 'node_modules', 'Assets'}]
        if os.path.basename(d) == 'com.odeilabs.guardianes' and os.path.basename(os.path.dirname(d)) == 'Packages':
            candidatos.append(d)
            dirs[:] = []
        if d.count(os.sep) - desde.count(os.sep) > 3:
            dirs[:] = []
    if len(candidatos) != 1:
        raise SystemExit('No encuentro una sola copia del paquete bajo ' + desde +
                         (' (hay ' + ', '.join(candidatos) + ')' if candidatos else '') + ': usa --copia')
    return candidatos[0]


def buscar_cerebro(copia):
    d = copia
    while True:
        padre = os.path.dirname(d)
        if padre == d:
            raise SystemExit('No encuentro el cerebro (una carpeta cerebro/Guardianes por encima de la copia): usa --cerebro')
        d = padre
        candidato = os.path.join(d, 'cerebro', 'Guardianes')
        if os.path.isfile(os.path.join(candidato, 'package.json')):
            return candidato


# ---------------------------------------------------------------------------- la foto
def leer_foto(copia):
    ruta = os.path.join(copia.raiz, FOTO)
    if not os.path.isfile(ruta):
        raise SystemExit('Esta copia no tiene ' + FOTO + ': crea la foto con "iniciar --commit <commit del cerebro del que sale>"')
    with open(ruta, encoding='utf-8') as f:
        datos = json.load(f)
    datos['mapa'] = {e['ruta']: e['hash'] for e in datos.get('ficheros', [])}
    return datos


def guardar_foto(copia, foto, simular):
    foto = dict(foto)
    mapa = foto.pop('mapa')
    foto['ficheros'] = [{'ruta': r, 'hash': h} for r, h in sorted(mapa.items())]
    if not simular:
        with open(os.path.join(copia.raiz, FOTO), 'w', encoding='utf-8', newline='\n') as f:
            json.dump(foto, f, ensure_ascii=False, indent=1)
            f.write('\n')


# ---------------------------------------------------------------------------- comparar
CLASES = [
    ('cerebro', 'Cambios en el cerebro (se pueden traer)'),
    ('copia', 'Cambios en la copia (subirlos al cerebro para que no se pierdan)'),
    ('conflicto', 'Cambiado en los dos (mirar a mano; --forzar para pisar)'),
]


def comparar(cerebro, copia, foto):
    """{ruta: (clase, que)}: que = nuevo / cambiado / borrado, visto desde el lado que cambia."""
    base = foto['mapa']
    hc = {r: hash_de(cerebro.leer(r), r) for r in cerebro.ficheros}
    hk = {r: hash_de(copia.leer(r), r) for r in copia.ficheros}
    res = {}
    for r in sorted(set(base) | set(hc) | set(hk)):
        b, c, k = base.get(r), hc.get(r), hk.get(r)
        if c == k:
            continue                                   # iguales (o borrados en los dos)
        cambia_c, cambia_k = c != b, k != b
        if cambia_c and not cambia_k:
            res[r] = ('cerebro', 'nuevo' if b is None else 'borrado' if c is None else 'cambiado')
        elif cambia_k and not cambia_c:
            res[r] = ('copia', 'nuevo' if b is None else 'borrado' if k is None else 'cambiado')
        else:
            res[r] = ('conflicto', 'nuevo en los dos' if b is None else 'cambiado en los dos')
    return res, hc, hk


def seleccionar(rutas, todas):
    """Las rutas pedidas (ficheros o carpetas, relativas a la copia) con sus .meta y los de sus carpetas."""
    if not rutas:
        return set(todas)
    elegidas = set()
    for p in rutas:
        p = p.replace('\\', '/').strip('/')
        for r in todas:
            if r == p or r.startswith(p + '/') or r == p + '.meta':
                elegidas.add(r)
    # el .meta de cada fichero elegido, y el de cada carpeta que lo contiene
    for r in list(elegidas):
        if not r.endswith('.meta') and r + '.meta' in todas:
            elegidas.add(r + '.meta')
        partes = r.split('/')
        for i in range(1, len(partes)):
            m = '/'.join(partes[:i]) + '.meta'
            if m in todas:
                elegidas.add(m)
    return elegidas


def avisar_meta_sueltos(elegidas, cambios):
    sueltos = sorted(r for r in elegidas if r.endswith('.meta') and r[:-5] in cambios and r[:-5] not in elegidas)
    for r in sueltos:
        print('  aviso: ' + r + ' va sin su fichero')


# ---------------------------------------------------------------------------- ORIGEN.md
def apuntar_origen(copia, linea, commit_completo, simular):
    ruta = os.path.join(copia.raiz, 'ORIGEN.md')
    if simular or not os.path.isfile(ruta):
        return
    with open(ruta, encoding='utf-8') as f:
        texto = f.read()
    if '## Sincronizaciones' in texto:
        texto = texto.rstrip('\n') + '\n' + linea + '\n'
    else:
        texto = texto.rstrip('\n') + '\n\n## Sincronizaciones\n\n' + linea + '\n'
    if commit_completo:
        lineas = texto.split('\n')
        for i, l in enumerate(lineas):
            if l.startswith('- **Commit del cerebro**:'):
                lineas[i] = '- **Commit del cerebro**: `' + commit_completo + '` (' + hoy() + ')'
                # lo que seguia en el mismo punto (notas del commit anterior) ya no vale
                j = i + 1
                while j < len(lineas) and lineas[j].startswith('  '):
                    j += 1
                del lineas[i + 1:j]
                break
        texto = '\n'.join(lineas)
    with open(ruta, 'w', encoding='utf-8', newline='\n') as f:
        f.write(texto)


def hoy():
    return datetime.date.today().strftime('%d/%m/%Y')


def resumen(rutas, n=6):
    vis = [r for r in sorted(rutas) if not r.endswith('.meta')]
    return ', '.join('`' + r + '`' for r in vis[:n]) + (' y %d más' % (len(vis) - n) if len(vis) > n else '')


# ---------------------------------------------------------------------------- ordenes
def orden_estado(cerebro, copia, foto, args):
    cambios, _, _ = comparar(cerebro, copia, foto)
    print('Copia:   ' + copia.raiz)
    print('Cerebro: ' + cerebro.raiz + ' (' + ('commiteado, ' if cerebro.confirmado else 'en disco, ') + cerebro.version() + ')')
    print('Foto:    ' + foto.get('fecha', '?') + ', cerebro ' + foto.get('commit', '?'))
    base = foto.get('commit', '').split(' ')[0]
    if base:
        try:
            log = git(cerebro.repo, 'log', '--oneline', base + '..HEAD', '--', 'Guardianes').strip()
            if log:
                print('\nCommits del cerebro en Guardianes desde la foto:\n  ' + log.replace('\n', '\n  '))
        except SystemExit:
            pass
    if not cambios:
        print('\nLa copia está al día con el cerebro.')
        return 0
    for clase, titulo in CLASES:
        lista = [(r, q) for r, (c, q) in cambios.items() if c == clase]
        visibles = [(r, q) for r, q in lista if not r.endswith('.meta') or r[:-5] not in cambios]
        if not visibles:
            continue
        print('\n' + titulo + ':')
        for r, q in visibles:
            print('  %-10s %s' % (q, r))
    print('\nTraer: traer <ruta>... | traer --todo   Subir: subir <ruta>...   Ver: diferencias <ruta>')
    return 0


def orden_diferencias(cerebro, copia, foto, args):
    for r in args.rutas:
        r = r.replace('\\', '/').strip('/')
        a = cerebro.leer(r).decode('utf-8', 'replace').replace('\r\n', '\n').splitlines(True) if r in cerebro.ficheros else []
        b = copia.leer(r).decode('utf-8', 'replace').replace('\r\n', '\n').splitlines(True) if r in copia.ficheros else []
        sys.stdout.writelines(difflib.unified_diff(b, a, 'copia/' + r, 'cerebro/' + r))
    return 0


def copiar(origen_leer, origen_fich, destino_raiz, rutas, simular):
    for r in sorted(rutas):
        dst = os.path.join(destino_raiz, *r.split('/'))
        if r in origen_fich:
            print('  copiar  ' + r)
            if not simular:
                os.makedirs(os.path.dirname(dst), exist_ok=True)
                with open(dst, 'wb') as f:
                    f.write(origen_leer(r))
        else:
            print('  borrar  ' + r)
            if not simular and os.path.exists(dst):
                os.remove(dst)


def orden_mover(cerebro, copia, foto, args, hacia_copia):
    cambios, hc, hk = comparar(cerebro, copia, foto)
    lado = 'cerebro' if hacia_copia else 'copia'
    if not args.todo and not args.rutas:
        raise SystemExit('Di qué rutas (ficheros o carpetas) o --todo')
    elegidas = seleccionar(None if args.todo else args.rutas, set(cambios))
    validas = {r for r in elegidas if cambios[r][0] == lado or (args.forzar and cambios[r][0] == 'conflicto')}
    for r in sorted(elegidas - validas):
        clase = cambios[r][0]
        motivo = ('conflicto: cambiado en los dos (--forzar para pisar)' if clase == 'conflicto'
                  else 'el cambio está en el otro lado (' + clase + ')')
        if not r.endswith('.meta') or r[:-5] not in elegidas:
            print('  se queda  ' + r + ': ' + motivo)
    if not validas:
        print('Nada que ' + ('traer.' if hacia_copia else 'subir.'))
        return 1 if elegidas else 0
    avisar_meta_sueltos(validas, cambios)
    if hacia_copia:
        print('Traer del cerebro a la copia (' + cerebro.version() + '):')
        copiar(cerebro.leer, cerebro.ficheros, copia.raiz, validas, args.simular)
    else:
        if cerebro.confirmado:
            raise SystemExit('subir escribe en el cerebro en disco: no se puede con --confirmado')
        print('Subir de la copia al cerebro:')
        copiar(copia.leer, copia.ficheros, cerebro.raiz, validas, args.simular)
    # la foto: esos ficheros quedan iguales en los dos lados
    nuevo = hc if hacia_copia else hk
    for r in validas:
        if r in nuevo:
            foto['mapa'][r] = nuevo[r]
        else:
            foto['mapa'].pop(r, None)
    pendientes = {r for r, (c, _) in cambios.items() if r not in validas}
    al_dia = hacia_copia and not pendientes
    if hacia_copia:
        foto['commit'] = cerebro.version() if al_dia else foto.get('commit', '?') + ' (con partes de ' + cerebro.version() + ')'
        foto['fecha'] = hoy()
    guardar_foto(copia, foto, args.simular)
    if hacia_copia:
        linea = '- ' + hoy() + ' — traído del cerebro (' + cerebro.version() + '): ' + resumen(validas) + '.'
    else:
        linea = '- ' + hoy() + ' — subido al cerebro desde esta copia: ' + resumen(validas) + '.'
    apuntar_origen(copia, linea, cerebro.version() if al_dia else None, args.simular)
    if args.simular:
        print('(simulado: no se ha tocado nada)')
    elif hacia_copia:
        print('Hecho. Ahora: compilar el juego en batch y sus pruebas, y commitear en su repo.')
    else:
        print('Hecho. Ahora: commitear en el cerebro (y probarlo en los otros juegos cuando lo traigan).')
    return 0


def orden_iniciar(cerebro, copia, args):
    ruta = os.path.join(copia.raiz, FOTO)
    if os.path.exists(ruta) and not args.forzar:
        raise SystemExit(FOTO + ' ya existe (--forzar para rehacerla)')
    if not args.commit:
        raise SystemExit('Di de qué commit del cerebro sale la copia: --commit <commit> (lo pone su ORIGEN.md)')
    foto = {'commit': args.commit, 'fecha': hoy(),
            'mapa': {r: hash_de(copia.leer(r), r) for r in copia.ficheros}}
    guardar_foto(copia, foto, args.simular)
    print('Foto creada con %d ficheros de la copia tal como está (cerebro %s).' % (len(foto['mapa']), args.commit))
    return 0


def main():
    if hasattr(sys.stdout, 'reconfigure'):
        sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('orden', choices=['estado', 'diferencias', 'traer', 'subir', 'iniciar'], nargs='?', default='estado')
    ap.add_argument('rutas', nargs='*')
    ap.add_argument('--copia')
    ap.add_argument('--cerebro')
    ap.add_argument('--todo', action='store_true')
    ap.add_argument('--confirmado', action='store_true')
    ap.add_argument('--simular', action='store_true')
    ap.add_argument('--forzar', action='store_true')
    ap.add_argument('--commit')
    args = ap.parse_args()

    copia = Copia(args.copia or buscar_copia(os.getcwd()))
    cerebro = Cerebro(args.cerebro or buscar_cerebro(copia.raiz), args.confirmado)
    if os.path.samefile(copia.raiz, cerebro.raiz):
        raise SystemExit('La copia y el cerebro son la misma carpeta')
    if args.orden == 'iniciar':
        return orden_iniciar(cerebro, copia, args)
    foto = leer_foto(copia)
    if args.orden == 'estado':
        return orden_estado(cerebro, copia, foto, args)
    if args.orden == 'diferencias':
        return orden_diferencias(cerebro, copia, foto, args)
    return orden_mover(cerebro, copia, foto, args, args.orden == 'traer')


if __name__ == '__main__':
    sys.exit(main())
