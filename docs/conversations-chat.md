# Conversaciones y chat — Paquete 3

## Responsabilidades

`Saylo.Modules.Conversations` contiene `ConversationSession` y `ConversationMessage`.
Solo referencia Translation.Contracts y bibliotecas base de .NET. No conoce Avalonia,
portapapeles, dispositivos, motores concretos, rutas, servidores ni almacenamiento.
El consumidor serializa sus accesos; Desktop lo utiliza desde el contexto de UI.

`MainViewModel` mantiene una colección visual de solo lectura. Cada mensaje conserva
la selección de idiomas al enviarse, el original y su origen: escrito, voz revisada o
mezcla de texto y transcripción. La edición de transcripción y cancelación de captura
del paquete 2 siguen activas. Añadir una transcripción conserva el borrador existente.

## Resultados progresivos

1. Enviar valida texto e idiomas, crea un mensaje y detiene la voz anterior.
2. La primera traducción válida se muestra inmediatamente.
3. Pronunciación y voz se completan independientemente. IPA se identifica como tal;
   la guía de lectura se identifica como aproximada. No se generan lecturas ficticias.
4. Un fallo parcial mantiene el texto y muestra el estado de la capacidad afectada.
5. Las actualizaciones deben pertenecer al OperationId del mensaje. Se ignoran eventos
   duplicados, mensajes borrados, operaciones canceladas y resultados posteriores al cierre.

La sesión tiene estados de mensaje `Translating`, `Enriching`, `Ready`, `Failed` y
`Canceled`. Pronunciación y audio tienen `Pending`, `Ready`, `Failed` y `Unavailable`.
La UI presenta además reproducción en curso, detenida o fallida y el resultado de copiar.

## Copiar y limpiar

Copiar traducción usa `IClipboardService` con exactamente el texto traducido mostrado.
`AvaloniaClipboardService` accede al portapapeles de la ventana y captura errores sin
mostrar mensajes internos del proveedor. No se incluye el original, IPA ni etiquetas.
El botón está habilitado cuando hay traducción y portapapeles disponible, incluso si
otros resultados siguen pendientes.

Limpiar chat muestra confirmación dentro de la interfaz. Conservar chat no cambia los
mensajes ni interrumpe la voz. Confirmar invalida inmediatamente los mensajes anteriores,
cancela traducción/captura y espera la detención del audio. Se borran texto, pronunciación
y referencias a audio incluso en objetos retenidos por consumidores; los resultados
tardíos no pueden reconstruir el chat. El borrador que aún no se envió se conserva.

No se escribe historial, texto ni audio a archivos, base de datos, configuración o logs.
Cerrar destruye la sesión y el borrador después de cancelar las operaciones. Reabrir crea
una sesión independiente vacía. El portapapeles del sistema tiene su propio ciclo de vida
y no se borra al limpiar el chat.

## Reproducción local

`ChatPlaybackController` consume `IAudioPlaybackAdapter` y serializa las voces:

- Cada mensaje puede reclamar autoplay una sola vez al recibir su primer audio válido.
- Se inicia sin esperar la pronunciación ni bloquear la enumeración de resultados.
- Repetir reutiliza el audio de la sesión; no vuelve a traducir ni a sintetizar.
- Repetir otro mensaje detiene el anterior. Detener conserva el audio para repetirlo.
- Una nueva traducción, la limpieza y el cierre cancelan la voz anterior y descartan
  solicitudes de reproducción que hubieran quedado en cola.
- Un error de reproducción conserva texto y audio y permite reintentar con Repetir.
- Mientras suena una voz, la captura de micrófono queda deshabilitada para evitar que
  el audio generado entre en una grabación de Saylo.

La composición de `App.axaml.cs` registra el adaptador Windows. Usa WinMM/waveOut con
PCM16 en memoria y el dispositivo de salida predeterminado, una tarea de trabajo y un
evento nativo de finalización. Al cancelar ejecuta reset, libera el encabezado y cierra
el dispositivo antes de soltar sus búferes. No introduce paquetes ni ejecutables externos.
La selección de un dispositivo de salida distinto queda fuera de este paquete.

macOS y Linux mantienen la interfaz inyectable y muestran reproducción no disponible
hasta integrar y probar su backend. La captura nativa y los motores STT/traducción/TTS
siguen siendo dependencias de integración; este paquete no instala modelos ni los simula
en el ejecutable distribuible.

## Validación

```powershell
dotnet test tests/Saylo.Modules.Conversations.Tests/Saylo.Modules.Conversations.Tests.csproj -c Release
dotnet test tests/Saylo.Desktop.Tests/Saylo.Desktop.Tests.csproj -c Release
dotnet test tests/Saylo.Architecture.Tests/Saylo.Architecture.Tests.csproj -c Release
```

Las pruebas de chat cubren autoplay único, repetición sin volver a traducir, ausencia de
superposición, cancelación, texto conservado ante fallos, copia exacta, confirmación,
limpieza durante operaciones, origen de la transcripción y cierre sin recuperación.
Las pruebas del módulo comprueban estados, correlación, duplicados y borrado de referencias.

Las comprobaciones nativas son optativas, requieren Windows con salida de audio y usan
solo PCM de ceros, sin tonos ni voz:

```powershell
$env:SAYLO_NATIVE_AUDIO_TESTS = '1'
dotnet test tests/Saylo.Desktop.Tests/Saylo.Desktop.Tests.csproj -c Release --filter FullyQualifiedName~NativePlaybackTests
```

Referencias de implementación: [portapapeles de Avalonia](https://docs.avaloniaui.net/docs/services/clipboard),
[eventos de waveform audio](https://learn.microsoft.com/en-us/windows/win32/multimedia/using-an-callback-to-process-driver-messages).
Waveform Audio es una API heredada; Microsoft recomienda WASAPI para desarrollos nuevos.
La integración actual prioriza una salida Windows local sin dependencias adicionales;
un backend WASAPI puede sustituirla mediante la misma interfaz.
