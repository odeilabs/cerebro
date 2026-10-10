# Guardianes

Paquete común de la saga **Gaia's Response** de Odei Labs: los juegos en los que controlas a una
criatura mítica que la tierra despierta cuando la envenenan (*King Kong*, *Basiliskos*...) y el
crossover que las reunirá contra Ferrox.

Lleva el mundo (relieve, agua, vegetación instanciada con viento), el combate, los humanos y
helicópteros de Ferrox, los efectos, el sonido, la cámara de criatura, la calidad, el HUD y las
herramientas del editor y de arte. Detalles en [CLAUDE.md](CLAUDE.md); cómo se usa desde los juegos
y la guía de AssetsCreator, en el [README del cerebro](../README.md).

Es **la fuente** de la que cada juego copia lo que usa: el paquete va incrustado en cada proyecto
(`<Proyecto>/Packages/com.odeilabs.guardianes/`, con sus `.meta` y un `ORIGEN.md` con el commit del
cerebro), de modo que el build del juego no necesita el cerebro y los cambios posteriores aquí no
afectan a una versión concreta. Durante el desarrollo, el juego mira el cerebro y trae lo que le
interese.
