# Interfaz de SAYLO — Paquetes 1, 2, 3 y 4

La aplicación usa Avalonia 12, CommunityToolkit.Mvvm, bindings compilados y .NET 8,
como el resto de la solución. La UI depende de Conversations y los contratos públicos de Translation,
sin arrancar Api, Identity ni servicios HTTP.

## Componentes

- `MainViewModel`: compone idiomas, borrador, entrada de audio y conversación temporal.
- `ConversationSession` / `ConversationMessage`: lógica de sesión en el módulo
  Conversations, con estados independientes, origen de entrada e invalidación al limpiar.
- `LanguageSelectionViewModel`: selección manual español/inglés e intercambio. Cambiar
  un idioma al del otro selector intercambia la pareja de forma atómica. Durante una
  operación se bloquean los selectores y el comando, incluido su atajo de teclado.
- `TextInputViewModel`: edición multilínea, contador, borrado y creación de solicitudes
  validadas. Se conserva el original; 160 caracteres es un benchmark, no un límite.
- `AudioInputViewModel` / `AudioInputView`: dispositivos, grabar, detener, descartar,
  duración, progreso, errores y transcripción editable. Se conserva el dispositivo por
  ID al actualizar; al desaparecer se elimina la selección obsoleta.
- `ConversationMessageViewModel` / `ConversationView`: original, traducción, guía
  aproximada o IPA, estados independientes de pronunciación y voz. Los resultados
  parciales se muestran al llegar y un fallo posterior conserva el texto.

Los code-behind inicializan vistas, desplazan nuevos mensajes y gestionan apertura/cierre. El
cierre espera la cancelación de operaciones y la liberación del adaptador antes de
terminar, incluso si se solicita cerrar varias veces.

## Composición de servicios futuros

El constructor inyectable es:

```csharp
new MainViewModel(
    audioInput: nativeAudioAdapter,         // IAudioInputAdapter
    recognizer: localSpeechRecognizer,     // ISpeechRecognitionEngine, opcional
    translation: localTranslationService,  // ITranslationService, opcional
    playback: AudioPlaybackAdapterFactory.CreateDefault(),
    clipboard: new AvaloniaClipboardService(() => window.Clipboard));
```

La composición real se registra en `App.axaml.cs`. Desde el paquete 4,
`DesktopTranslationFactory` conecta Argos cuando se configura y ofrece una demostración
explícita con `--demo`. Nunca sustituye un fallo del motor real por resultados simulados.
La demostración está identificada en la interfaz y solo conoce sus frases precargadas.

`AudioInputAdapterFactory.CreateDefault()` detecta Windows, macOS o Linux y devuelve
un adaptador que declara la captura pendiente de configuración en esa plataforma.
**Este paquete prepara la frontera multiplataforma; no implementa todavía captura
nativa, permisos del SO ni acceso real a dispositivos.** Sin adaptador nativo no se
inventan micrófonos y Grabar permanece deshabilitado. Sin servicio de traducción,
Traducir permanece deshabilitado y la UI explica su disponibilidad.

El flujo escrito, la configuración y la demostración del paquete 4 se documentan en
[Traducción escrita y flujos UI](written-translation-ui.md).

`IAudioInputAdapter` define:

| Operación | Contrato del adaptador nativo |
| --- | --- |
| `GetDevicesAsync` | Enumerar micrófonos sin activar captura ni solicitar permisos. |
| `StartAsync` | Solicitar acceso por acción explícita y capturar solo el micrófono seleccionado. |
| `StopAsync` | Entregar PCM16 en memoria con frecuencia y canales válidos. |
| `CancelAsync` | Liberar el dispositivo y descartar búferes, también después de un inicio fallido. |
| `DisposeAsync` | Limpiar recursos al cerrar la aplicación. |

El adaptador debe respetar cancelación, ser seguro ante limpieza repetida, imponer
el máximo de 30 segundos en su propia capa y mapear permisos, desconexiones y fallos
mediante `AudioInputException`. No debe capturar loopback ni audio de llamadas. No
se exponen mensajes internos del proveedor en la interfaz.

## Flujo de audio

1. El empleado abre Voz y consulta o actualiza micrófonos.
2. Selecciona un dispositivo e inicia voluntariamente la grabación.
3. Los idiomas y el dispositivo quedan bloqueados durante la sesión de voz.
4. Detener, o llegar a 30 segundos, finaliza la captura. La UI también rechaza una
   salida que exceda ese límite. Un `TimeProvider` permite comprobarlo sin esperas reales.
5. El motor STT inyectado recibe el audio y el idioma de origen capturado al iniciar.
6. El empleado revisa y corrige la transcripción. No se traduce automáticamente.
7. Añadir al mensaje agrega el texto revisado al borrador existente sin perderlo.
8. Descartar cancela arranque, grabación o transcripción, elimina la transcripción y
   libera recursos. Un fallo de limpieza bloquea nuevas grabaciones hasta recuperarse
   mediante Actualizar.

El micrófono nunca se inicia al abrir la ventana o al enumerar dispositivos. Sin STT,
se informa de su ausencia y se descarta el audio tras detener. El adaptador debe
hacer que el retorno de Cancel/Dispose implique que sus búferes han sido descartados.

## Traducción y conversación

`TranslateCommand` crea un `TextTranslationRequest` con el idioma y texto vigentes,
consume una vez `ITranslationService.TranslateTextAsync` y aplica las actualizaciones
al mensaje de esa operación. Cancela por token, ignora actualizaciones de otra operación
y conserva el texto confirmado. La interfaz permanece reactiva y el borrador editable.

El paquete 3 añade copia de la traducción, limpieza confirmada, autoplay una vez por
operación, repetición y detención. `ChatPlaybackController` evita superposición de voces
y detiene el audio anterior al iniciar otra traducción. `WindowsAudioPlaybackAdapter`
reproduce PCM16 en memoria en la salida predeterminada de Windows, sin archivos de audio.
Otros sistemas utilizan `IAudioPlaybackAdapter` con diagnóstico de indisponibilidad hasta
integrar su backend nativo. No se transmite audio a aplicaciones externas.

Limpiar invalida las operaciones pendientes, cancela la traducción y la captura, detiene
la voz y elimina los mensajes y sus referencias a audio. El borrador se conserva. Cerrar
la ventana cancela y libera todos los recursos y elimina también el borrador. Una nueva
instancia empieza vacía. El texto copiado queda bajo la gestión del portapapeles del SO.

Detalles de estados, reproducción, pruebas y límites: [Módulo de conversaciones](conversations-chat.md).

El paquete 5 añade un indicador del estado actual, reproducción automática opcional,
detención desde la cabecera del chat y recuperación ante fallos de audio sin perder
la traducción. La captura queda bloqueada hasta confirmar que la salida se detuvo.
Detalles: [Reproducción, estados y errores](playback-states-errors.md).

## Teclado y accesibilidad

- Tab: navegación entre controles; foco visible de Fluent y etiquetas accesibles.
- `Ctrl+Mayús+S`: intercambiar idiomas, cuando el contexto permite hacerlo.
- `Ctrl+Enter`: solicitar traducción, cuando existe servicio y texto válido.
- `Ctrl+L` dentro del editor: borrar el borrador.
- Enter: nueva línea en el borrador o la transcripción.
- Estados de audio y traducción anunciables a tecnologías de asistencia.

La ventana inicial mide 1080 × 860 unidades lógicas; mínimo 760 × 640. El panel Voz
se expande bajo el editor. La zona central se desplaza verticalmente cuando falta
altura; la conversación y los editores también admiten desplazamiento de contenido.

## Validación

```powershell
dotnet build src/Saylo.Desktop/Saylo.Desktop.csproj -c Release
dotnet test tests/Saylo.Desktop.Tests/Saylo.Desktop.Tests.csproj -c Release
dotnet test tests/Saylo.Modules.Conversations.Tests/Saylo.Modules.Conversations.Tests.csproj -c Release
dotnet test tests/Saylo.Architecture.Tests/Saylo.Architecture.Tests.csproj -c Release
dotnet run --project src/Saylo.Desktop/Saylo.Desktop.csproj
```

Las pruebas de escritorio usan adaptadores y motores de prueba, sin acceso a micrófonos,
red o modelos reales. Verifican selección, revisión, cancelación durante arranque y STT,
límite temporal, errores, cierre y entrega progresiva. La revisión visual se realiza en
Windows; macOS y Linux necesitan validación con adaptadores nativos antes de declarar
compatibilidad de captura. La prueba de cancelación de Whisper que fallaba en la suite
general del paquete 1 pertenece al módulo de Translation, que este paquete no modifica.
