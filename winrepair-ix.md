
## `winrepair-ix.md`

```markdown
---
name: winrepair-ix
description: >-
  Guía de interacción (IX) para una herramienta nativa de diagnóstico y
  reparación de Windows. Úsala al implementar atajos de teclado, bandeja del
  sistema, progreso en vivo, cancelación de operaciones, notificaciones toast
  y feedback de elevación. Cubre accesibilidad por teclado, anuncios de estado
  y respeto a las preferencias de animación de Windows.
---

# IX de la Herramienta de Reparación Windows: Teclado, Progreso y Notificaciones

Esta skill define cómo responde la herramienta a la interacción del usuario en Windows. Mientras `winrepair-ui` define la apariencia y `winrepair-ux` el flujo, **`winrepair-ix` define el timing, los atajos, las notificaciones y la respuesta a operaciones largas**.

## 1. Teclado y accesibilidad de interacción

- Toda la herramienta debe ser **operable sin ratón**. Atajos mínimos:
  - `F5` — Analizar todo
  - `Ctrl+R` — Reparar seleccionados
  - `Ctrl+L` — Mostrar/ocultar log en vivo
  - `Esc` — Cancelar operación en curso (si es segura)
  - `F1` — Ayuda contextual
- El foco debe ser visible en todo momento (anillo de foco del sistema, no lo sobrescribas sin reemplazo).
- Los lectores de pantalla (Narrator) deben anunciar el cambio de estado de cada comprobación: usa `AutomationProperties.LiveSetting` en WinUI 3 o `AutomationPeer.RaiseNotificationEvent` en WPF.

## 2. Progreso en vivo y cancelación

- Las operaciones largas publican progreso en tiempo real: porcentaje si el comando lo reporta, o un contador de pasos si no.
- La cancelación debe ser **segura y explícita**: si cancelar una reparación a medias puede dejar el sistema en un estado inconsistente, la UI debe indicarlo antes de cancelar y ofrecer “Esperar a que termine” como opción primaria.
- Tras cancelar, la herramienta debe ejecutar las acciones de limpieza necesarias y registrar el estado en el log.
- Bajo **“Efectos de animación” desactivados** en Windows, las barras de progreso indeterminadas deben mostrar un patrón estático en lugar de animación.

## 3. Bandeja del sistema y notificaciones

- La herramienta puede minimizarse a la bandeja del sistema durante una reparación larga. El icono muestra el estado (en ejecución, éxito, error) mediante un cambio de superposición.
- Al terminar una reparación en segundo plano, emite una **notificación toast** con resumen y acción “Ver informe”.
- No abuses de las notificaciones: una por operación completada, no una por cada paso.
- La notificación debe ser anunciada por Narrator y respetar “No molestar” / Asistente de concentración de Windows.

## 4. Feedback de elevación (UAC)

- Antes de que aparezca el diálogo de UAC, la UI muestra un estado “Solicitando permisos de administrador…” con un spinner breve.
- Si el usuario cancela el UAC, la UI vuelve al estado anterior sin error rojo; muestra un aviso informativo: “La reparación requiere permisos de administrador. Puedes reintentar cuando quieras.”
- Si el UAC falla por política de grupo, muestra el motivo y la acción de contacto con el administrador.

## 5. Animaciones y transiciones

- Las transiciones de estado (OK → Reparando → Reparado) duran **150–250 ms**. No uses animaciones de más de 300 ms en un flujo operativo.
- Respeta la configuración **“Mostrar animaciones en Windows”**; si está desactivada, elimina las transiciones no esenciales.
- El log en vivo debe hacer auto-scroll solo si el usuario está al final de la lista; si el usuario ha hecho scroll hacia arriba, no le muevas la vista.

## 6. Checklist de IX

- [ ] ¿Toda la herramienta es operable sin ratón, con atajos documentados?
- [ ] ¿El foco es visible y los cambios de estado se anuncian a Narrator?
- [ ] ¿Las operaciones largas publican progreso en tiempo real y la cancelación es segura o explicitada?
- [ ] ¿Las barras indeterminadas respetan “Efectos de animación” desactivados?
- [ ] ¿La bandeja del sistema refleja el estado y las notificaciones toast son informativas y no abusivas?
- [ ] ¿La cancelación de UAC vuelve al estado anterior sin error rojo?
- [ ] ¿El log en vivo no hace auto-scroll si el usuario ha subido manualmente?