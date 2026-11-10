# Manual de uso para empleados

SAYLO · Piloto de escritorio · Paquete 6 · Revisión: 29 de octubre de 2026.

## 1. Para qué sirve y qué está disponible

SAYLO ayuda a preparar mensajes entre español e inglés durante una atención.
Utilízalo junto a la herramienta habitual de llamadas o atención. SAYLO no escucha
la llamada, no la contesta y no introduce audio automáticamente en ella. La voz
de SAYLO, cuando esté disponible, se reproduce en tu equipo.

**Esta versión todavía necesita integración y validación antes de utilizarse como
puesto completo de atención con voz.** TI debe confirmar las funciones habilitadas
en tu equipo:

| Función | Disponibilidad en esta versión |
| --- | --- |
| Escribir, elegir español/inglés y consultar el chat | Implementada. Traducir requiere un motor configurado. |
| Traducción real de texto | Conexión local Argos configurable por TI; modelos y motor no están incluidos ni validados en el entorno de estas pruebas. |
| Modo Demostración | Solo frases de ejemplo precargadas; no usa IA ni genera audio. |
| Grabar y transcribir desde la ventana | Flujo preparado; captura nativa y conexión del reconocedor pendientes. |
| Pronunciación y voz reales | Motores pendientes de conexión en la aplicación. Las guías de demostración son ejemplos. |
| Reproducción de audio | Salida Windows implementada; necesita que el motor entregue audio. macOS y Linux requieren sus adaptadores y validación. |
| Copiar y limpiar el chat | Implementadas. El chat dura solo mientras la aplicación está abierta. |

Si la cabecera dice **Demostración**, úsala para practicar con **Usar ejemplo**;
no esperes traducciones de frases libres ni voz. Si dice **Sin configurar**, pide a
TI la preparación del motor. El aviso **Captura de audio pendiente de configurar**
indica que debes utilizar la entrada escrita.

## 2. Preparar una atención

1. Abre SAYLO desde el acceso facilitado por TI y comprueba el modo de la cabecera.
2. En **IDIOMA DE ORIGEN**, elige el idioma de la frase que vas a escribir o grabar.
   En **IDIOMA DE DESTINO**, comprueba el idioma que necesitas leer o decir.
3. Usa el botón con las dos flechas para intercambiar ambos idiomas. El borrador
   existente se conserva; revisa que siga correspondiendo al nuevo origen.
4. Decide si quieres **Reproducir voz automáticamente**. Está activado al iniciar
   la sesión. Desactivarlo afecta a los audios que lleguen después; no detiene una
   voz que ya está sonando. Para eso utiliza **Detener voz**.
5. Si necesitas hablar o escuchar, verifica con TI que las funciones estén
   habilitadas. Comprueba tu salida de audio con una frase de prueba antes de atender.

La [lista de verificación operativa](verificacion-operativa.md) permite registrar
si el puesto está listo para texto o para el flujo completo con voz.

## 3. Escribir y traducir una respuesta

1. Escribe una frase en **Tu mensaje**. **Enter** añade una línea; no envía el mensaje.
2. Revisa nombres, cantidades y el sentido de la frase antes de traducirla.
3. Pulsa **Traducir** o **Ctrl+Enter**. Un mensaje vacío o formado solo por espacios
   no se puede enviar. El contador muestra el límite del motor, actualmente 1000
   caracteres; si lo superas, reduce el mensaje. SAYLO no recorta el texto por ti.
4. Consulta **ORIGINAL** y **TRADUCCIÓN** dentro de **CONVERSACIÓN**. El resultado
   aparece antes que la pronunciación o la voz. Puedes seguir editando un borrador
   mientras termina la operación.
5. La pronunciación, si está disponible, se identifica como **guía aproximada** o
   **IPA**. Una guía aproximada orienta la lectura; comprueba el significado en la
   traducción. Si falta, puedes seguir usando el texto.
6. Si parte del mensaje queda fuera de la zona visible, desplázate dentro del chat.
   Al abrir el panel **Voz** o reducir la ventana, también puede ser necesario
   desplazar la página para llegar a los controles inferiores.

Para practicar en Demostración, elige una frase, pulsa **Usar ejemplo** y después
**Traducir**. Por ejemplo: «Buenos días.» produce «Good morning.». Intercambia los
idiomas y usa un ejemplo en inglés para practicar el sentido contrario.

**Cancelar** interrumpe la traducción en curso; el texto ya obtenido se conserva.
Los idiomas se bloquean mientras se procesa una operación. Espera a que termine o
cancélala antes de cambiarlos.

## 4. Preparar un mensaje desde el micrófono

Este recorrido se aplica cuando TI haya habilitado captura y transcripción. Si
aparece un aviso de función pendiente o **Grabar** está deshabilitado, escribe el
mensaje y comunica la necesidad a TI.

1. Abre **Voz** y pulsa **Actualizar** para consultar los micrófonos disponibles.
2. Selecciona el micrófono de entrada y comprueba el idioma de origen.
3. Pulsa **Grabar** y habla una frase breve. Solo se graba al pulsar ese botón y
   durante un máximo de 30 segundos.
4. Pulsa **Detener** para terminar y transcribir. **Descartar** cancela la captura
   o transcripción y elimina ese intento. No modifica el borrador escrito.
5. Revisa y corrige **Revisa la transcripción**. Corregirla no inicia la traducción.
6. Pulsa **Añadir al mensaje**: se agrega al borrador, conservando lo que ya tenías.
   Revisa el conjunto y pulsa **Traducir** cuando esté listo.

La captura queda deshabilitada mientras suena o se está deteniendo una voz, para
evitar que SAYLO grabe su propia salida. El selector de idiomas se mantiene
bloqueado hasta aceptar o descartar la transcripción.

## 5. Escuchar, detener, repetir y copiar

Cuando haya síntesis disponible y la opción automática esté activada, la primera
voz empieza una sola vez al estar lista, tanto si escribiste como si revisaste una
grabación. No hace falta pulsar reproducir ni esperar a la guía de pronunciación.

- **Detener voz**, en la cabecera del chat, o **Detener audio**, en el mensaje,
  interrumpe la salida. El audio disponible se conserva para repetirlo.
- **Repetir audio** escucha otra vez el audio de ese mensaje sin volver a traducir.
  También funciona con la reproducción automática desactivada. Repetir otro mensaje
  detiene el anterior.
- **Copiar traducción** copia únicamente el texto traducido. Comprueba el aviso
  **Traducción copiada**. Pégalo tú en el destino correcto cuando lo necesites;
  SAYLO no envía el contenido a otras aplicaciones.

Si no se generó audio, Repetir permanece deshabilitado. Un fallo de síntesis
conserva la traducción, pero esta versión aún no ofrece reintentar únicamente la
generación de voz. Utiliza el texto y solicita asistencia a TI. Ante un fallo de
reproducción de un audio ya disponible, prueba **Repetir audio**.

## 6. Avisos y recuperación durante una atención

| Lo que ves | Qué hacer |
| --- | --- |
| Traducir deshabilitado | Revisa el contenido, su longitud y el modo. Termina o cancela la operación de voz que esté pendiente. Si falta configuración, contacta con TI. |
| Faltan modelos o el motor no pudo iniciarse | El original se conserva. Continúa por el procedimiento alternativo del equipo de atención y solicita revisión de TI. |
| No hay micrófono o se desconectó | Conecta o selecciona uno disponible y pulsa Actualizar. Mientras tanto puedes escribir. |
| Permiso de micrófono denegado | Solicita a TI revisar los permisos del puesto; utiliza texto. |
| No se pudo transcribir | Graba de nuevo cuando la función esté disponible o escribe el mensaje. |
| No hay memoria suficiente | Reduce otras tareas del equipo y reintenta. Si persiste, solicita soporte. |
| Falló la traducción y aparece Reintentar traducción | Ese botón usa el original y los idiomas del mensaje fallido, aunque hayas cambiado el borrador. El nuevo intento aparece como otro mensaje. |
| No se pudo generar la voz o falta la pronunciación | Lee o copia la traducción disponible. Solicita revisión de la función a TI. |
| No se pudo reproducir | Revisa la salida de audio y pulsa Repetir audio en ese mensaje. |
| Reintentar detención | Pulsa ese botón. El micrófono seguirá bloqueado hasta confirmar la detención. Si persiste, usa texto y pide soporte. |
| No se pudo copiar | Reintenta Copiar traducción o selecciona el texto visible para copiarlo. |
| La frase no está entre los ejemplos | En Demostración elige una frase precargada con Usar ejemplo. |

El estado **Revisa el aviso · puedes seguir usando Saylo** indica un problema
recuperable. Lee el aviso del mensaje o del panel Voz; no significa que debas
perder el borrador o reiniciar inmediatamente.

## 7. Terminar y empezar otra atención

1. Pulsa **Limpiar chat**. **Conservar chat** vuelve al chat sin interrumpir el audio.
   **Eliminar conversación** confirma el borrado de mensajes y detiene sus operaciones.
2. **El borrador se conserva al limpiar el chat.** Usa **Borrar texto** en el editor
   si tampoco debe quedar para la siguiente atención.
3. Cierra SAYLO cuando termines: se cancelan las operaciones y se vacían la
   conversación y el borrador. Una apertura nueva comienza sin mensajes anteriores.

El portapapeles del sistema tiene su propio ciclo de vida: limpiar o cerrar SAYLO
no borra lo que copiaste ni lo que pegaste en otras aplicaciones. Sigue el
procedimiento de tu empresa para esos destinos.

## 8. Teclado y ayuda

| Tecla | Acción |
| --- | --- |
| Tab / Mayús+Tab | Avanzar o retroceder por los controles. |
| Ctrl+Mayús+S | Intercambiar idiomas cuando estén habilitados. |
| Ctrl+Enter | Traducir el borrador válido. |
| Enter en un editor | Añadir una línea. |
| Ctrl+L dentro del editor | Borrar el borrador. |

Si necesitas soporte, comunica el modo, la función afectada, el aviso visible y
los pasos con una frase ficticia. La ficha de la
[verificación operativa](verificacion-operativa.md#ficha-de-incidencia) ayuda a
describir el problema sin incluir datos de clientes.
