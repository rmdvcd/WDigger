name: winrepair-ux
description: >-
  Guía de UX para una herramienta nativa de diagnóstico y reparación de Windows.
  Úsala al diseñar flujos de análisis, confirmación de acciones destructivas,
  creación de puntos de restauración, gestión de elevación UAC y comunicación
  de resultados técnicos. Cubre reversibilidad, transparencia y reducción de
  riesgo operativo.
---

# UX de la Herramienta de Reparación Windows: Seguridad, Confianza y Reversibilidad

Esta skill define las decisiones de experiencia de usuario de una herramienta de reparación. El usuario confía en la herramienta para tocar su sistema: **cada acción debe ser reversible o explícitamente confirmada, y cada resultado debe ser comprensible sin ser ingeniero de sistemas**.

## 1. Principios rectores

1. **Diagnosticar antes de reparar.** Nunca ofrezcas una reparación sin haber ejecutado el diagnóstico correspondiente. El usuario debe ver qué se va a arreglar y por qué.
2. **Punto de restauración antes de cualquier cambio.** Antes de la primera acción que modifique el sistema, ofrece crear un punto de restauración. Si el usuario lo rechaza, regístralo en el log.
3. **Toda acción destructiva se confirma con consecuencias explícitas.** No basta “¿Estás seguro?”; di qué se va a borrar/cambiar y qué puede dejar de funcionar.
4. **La elevación (UAC) se explica antes de pedirla.** El usuario debe saber por qué la herramienta necesita permisos de administrador antes de que aparezca el diálogo del sistema.
5. **El resultado final es un informe, no un toast.** Al terminar, genera un informe con lo diagnosticado, lo reparado, lo que falló y lo que requiere intervención manual.

## 2. Flujo canónico

```text
1. La herramienta se abre sin elevar. Muestra el estado general del sistema.
2. El usuario selecciona categorías y pulsa “Analizar”.
3. Las comprobaciones de solo lectura se ejecutan sin UAC.
4. Al seleccionar una reparación que requiere elevación, la UI explica por qué y pide UAC.
5. Antes de la primera modificación, se ofrece crear un punto de restauración.
6. La reparación se ejecuta con progreso y log en vivo.
7. Al terminar, se muestra un informe con estado OK / Reparado / Fallido / Manual.
8. El informe se puede exportar (texto o HTML) para soporte.
3. Confirmación de acciones destructivas
Usa un diálogo de confirmación con checkbox de responsabilidad para acciones de alto riesgo:

text
┌─────────────────────────────────────────────────────┐
│  ⚠  Reiniciar el servicio de red                    │
│                                                     │
│  Esto desconectará temporalmente todas las          │
│  conexiones de red activas, incluidas las sesiones  │
│  remotas. Las descargas en curso se interrumpirán.  │
│                                                     │
│  [ ] Entiendo las consecuencias                     │
│                                                     │
│              [Cancelar]  [Reiniciar red]            │
└─────────────────────────────────────────────────────┘
El botón de confirmación permanece deshabilitado hasta que el checkbox se marca. Para acciones de riesgo bajo, basta con un diálogo estándar.

4. Elevación y transparencia
Explica por qué se necesita elevación: “Para reparar archivos del sistema, la herramienta necesita permisos de administrador.”

Si el usuario rechaza UAC, la herramienta debe degradar con gracia: mostrar qué comprobaciones puede hacer sin elevación y qué reparaciones quedan bloqueadas.

Registra en el log si la sesión fue elevada o no, y qué acciones se omitieron por falta de permisos.

5. Comunicación de resultados técnicos
Traduce códigos de error a lenguaje llano. 0x800F081F → “Windows no pudo encontrar los archivos necesarios para la reparación. Comprueba la conexión a Internet o inserta el medio de instalación.”

Cada resultado incluye: qué se encontró, qué se hizo, qué queda pendiente y qué puede hacer el usuario.

Separa claramente “Reparado automáticamente”, “Requiere acción manual” y “No se pudo reparar”.

El informe exportable incluye los comandos ejecutados y su salida completa, pero presenta primero el resumen legible.

6. Checklist de UX
□ ¿El diagnóstico se ejecuta antes de ofrecer cualquier reparación?
□ ¿Se ofrece crear un punto de restauración antes de la primera modificación?
□ ¿Las acciones destructivas tienen confirmación con consecuencias explícitas y checkbox de responsabilidad?
□ ¿La elevación UAC se explica antes de solicitarla y el rechazo degrada con gracia?
□ ¿Los códigos de error se traducen a lenguaje llano con acción recomendada?
□ ¿El resultado final es un informe exportable con resumen legible y detalles técnicos?
□ ¿Se distinguen claramente reparado / manual / fallido?