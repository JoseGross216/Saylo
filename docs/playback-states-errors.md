# Reproducción, estados y errores — Paquete 5

La ventana muestra el estado de la operación actual mediante `MainViewModel.State`
y `StateLabel`: consulta de dispositivos, preparación de micrófono, grabación,
liberación, transcripción, revisión, traducción, preparación de resultados,
reproducción, detención, limpieza, error recuperable y cierre.
Los errores se muestran como avisos con acciones comprensibles; no cierran la app
ni exponen excepciones, rutas internas o contenido del proveedor.

## Reproducción

- **Reproducir voz automáticamente** está activado inicialmente y se conserva solo
  durante la sesión. Cambiarlo afecta al audio que llegue después; activarlo no
  reproduce mensajes anteriores y desactivarlo no interrumpe una voz en curso.
- **Repetir audio** usa los mismos datos de audio, sin traducir ni sintetizar otra
  vez. Funciona también cuando se desactiva la reproducción automática.
- **Detener voz** está disponible en la cabecera del chat y en el mensaje activo.
  La cancelación invalida reproducciones en cola y espera al adaptador.
- Mientras hay reproducción o detención pendiente, el micrófono permanece
  deshabilitado. Si el adaptador falla al detenerse, aparece **Reintentar detención**;
  la captura se habilita después de una detención confirmada.
- Los errores al reproducir o detener conservan el texto y el audio del mensaje.
  Repetir permite recuperarse. Una respuesta normal del adaptador después de una
  cancelación se muestra como audio detenido.
- La limpieza y el cierre siguen cancelando las operaciones y borrando las
  referencias de la conversación temporal.

## Fallos parciales

`ConversationMessage` conserva por separado los códigos de fallo de traducción,
pronunciación y síntesis. Los códigos se borran con la sesión; las excepciones
originales no forman parte del mensaje.

Un fallo de síntesis diferencia modelos ausentes o inválidos, motor no disponible,
memoria insuficiente, recursos inaccesibles y otros errores. La traducción continúa
visible y se puede copiar. Un fallo de pronunciación tampoco oculta la traducción.
No se habilita Repetir cuando no existe audio válido.

Los errores de transcripción distinguen esas mismas causas y permiten grabar de
nuevo o escribir. Los fallos de micrófono indican permisos, desconexión, ausencia
de dispositivos o captura sin configurar. Un borrador escrito se conserva en
todos estos casos. Si no hay dispositivos, Actualizar permite recuperarse cuando
se conecta uno; no se inventan micrófonos disponibles.

## Alcance de integración

La salida Windows usa el adaptador WinMM del paquete 3. La captura nativa y los
motores de transcripción/síntesis siguen siendo dependencias configurables; este
paquete no instala modelos. El modo `--demo` sigue sin generar audio. Las pruebas
inyectan adaptadores y motores controlados para comprobar estados y errores.

Se conservan la edición de transcripción, la copia exacta y la confirmación de
limpieza implementadas en los paquetes anteriores (RF-021 a RF-023 del SRS).

## Validación

```powershell
dotnet test tests/Saylo.Desktop.Tests/Saylo.Desktop.Tests.csproj -c Release
dotnet test tests/Saylo.Modules.Conversations.Tests/Saylo.Modules.Conversations.Tests.csproj -c Release
dotnet test tests/Saylo.Architecture.Tests/Saylo.Architecture.Tests.csproj -c Release
```

Si hay una ventana anterior abierta bloqueando las DLL, puede compilarse sin
cerrarla usando `--artifacts-path artifacts/package5` en los comandos anteriores.
La compilación de esa carpeta necesita su propia restauración inicial.

Las pruebas cubren transiciones de captura y traducción, autoplay opcional,
repetición manual, detención pendiente/fallida y recuperación, errores tipados de
síntesis/transcripción, copia y borrado tras fallos, cancelación y cierre.
