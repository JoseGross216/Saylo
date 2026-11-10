# Traducción escrita y flujos UI — Paquete 4

## Arranque y servicios

`App.axaml.cs` crea una `DesktopTranslationConnection` mediante
`DesktopTranslationFactory` y la inyecta en `MainViewModel`. Los ViewModels no conocen
Argos, rutas de Python, archivos de modelos ni tipos de motores concretos.

- **Local**: al configurar `SAYLO_ARGOS_PYTHON` y `SAYLO_ARGOS_MODELS` con rutas absolutas,
  se reutiliza `ArgosTextTranslationEngine` a través de `TranslationService`.
- **Sin configurar**: el arranque informa de ello y mantiene Traducir deshabilitado.
  Una configuración incompleta tampoco activa la demostración.
- **Demostración**: solo se activa mediante `--demo`. El encabezado y el selector de
  ejemplos identifican expresamente el modo, sus resultados precargados y la ausencia
  de IA y audio. Incluye cuatro frases en español y cuatro en inglés, con traducciones
  y guías aproximadas correspondientes al idioma de destino.

La demostración usa el mismo coordinador y la misma proyección de conversación que
el servicio real. Rechaza frases desconocidas sin inventar una traducción. Usar ejemplo
coloca la frase en el editor; el empleado aún debe pulsar Traducir o Ctrl+Enter.
Cambiar origen/destino actualiza la selección de ejemplos y la indicación del editor.

```powershell
dotnet run --project src/Saylo.Desktop/Saylo.Desktop.csproj -- --demo
```

En una instalación publicada:

```powershell
.\Saylo.exe --demo
```

## Motor local

Preparar previamente el entorno y los modelos siguiendo
[Traducción local de texto](local-text-translation.md). El arranque de Desktop no
instala paquetes, descarga modelos ni ejecuta pip. Por ejemplo, con los artefactos
de desarrollo ya instalados:

```powershell
$env:SAYLO_ARGOS_PYTHON = (Resolve-Path ./artifacts/argos/runtime/Scripts/python.exe).Path
$env:SAYLO_ARGOS_MODELS = (Resolve-Path ./artifacts/argos/models).Path
dotnet run --project src/Saylo.Desktop/Saylo.Desktop.csproj
```

`LazyArgosTextEngine` pospone la lectura del manifiesto y la creación del motor hasta
la primera solicitud y hace ese trabajo fuera del hilo de UI. Mantiene una instancia
para reutilizar el worker. Si el manifiesto no está disponible, se informa del modelo
faltante y la próxima solicitud permite reintentar la carga. Se conserva la validación
de integridad y el protocolo local del adaptador existente.

La pronunciación y TTS locales siguen dependiendo de sus motores de integración:
esta conexión no los sustituye por la guía o audio de la demostración. Si faltan,
se muestra su indisponibilidad sin ocultar la traducción escrita.

La conexión es dueña del motor. Cerrar cancela la solicitud activa, espera su salida
y después dispone el worker. Los servicios inyectados directamente con el parámetro
`translation` mantienen la propiedad de sus recursos en el llamador; no se deben
proporcionar a la vez `translation` y `connection`.

## Entrada y presentación

- Se rechaza texto vacío o compuesto solo por espacios, incluido el envío por teclado.
- El perfil Argos y la demostración declaran un límite de 1000 unidades UTF-16, el
  mismo que admite el backend existente. El contador y el aviso muestran ese límite.
- Pegar un texto demasiado largo **no lo recorta**: se conserva el borrador completo y
  se deshabilita Traducir hasta corregirlo. Los 160 caracteres del SRS siguen siendo
  una condición de benchmark, no un límite de entrada.
- El original se conserva exactamente, incluidos espacios, acentos y saltos de línea.
- Cada mensaje identifica ORIGINAL y TRADUCCIÓN con sus respectivos idiomas. La guía
  indica si es aproximada o IPA; las guías de demo se marcan como ejemplos.
- El texto aparece antes de completar pronunciación o síntesis. La barra de progreso
  y los estados informan del trabajo; el borrador permanece editable.
- Los idiomas se bloquean durante la solicitud. El resultado mantiene los idiomas que
  tenía al enviarse, aunque se cambie de dirección después.

## Recursos, errores y reintento — RF-015

Los fallos de texto conservan un `FailureCode` en `ConversationMessage`. La capa de
presentación traduce códigos a mensajes accionables sin exponer excepciones internas,
rutas ni contenido del proveedor.

| Código | Respuesta |
| --- | --- |
| ModelUnavailable | Indicar modelos faltantes, conservar el original y permitir reintento. |
| ModelInvalid | Solicitar revisión del modelo o su lectura. |
| RuntimeUnavailable | Indicar que el motor no pudo iniciarse. |
| InsufficientMemory | Indicar memoria insuficiente y proponer liberar recursos antes de reintentar. |
| ResourceUnavailable | Indicar un archivo/recurso inaccesible o permisos insuficientes. |
| InvalidEngineOutput | Rechazar una salida vacía o inválida. |

Se añaden al final del enum los códigos `InsufficientMemory` y `ResourceUnavailable`,
conservando los valores anteriores. `TranslationService` los produce ante
OutOfMemoryException, IOException o UnauthorizedAccessException sin copiar sus mensajes.

Reintentar traducción crea una operación nueva con el **original y los idiomas del
mensaje fallido**, sin tomar el borrador nuevo ni la selección actual. No reemplaza el
historial anterior. El botón global Cancelar también cancela un reintento. Limpiar o
cerrar cancelan la operación activa y siguen invalidando resultados tardíos.

Un fallo de pronunciación o voz conserva la traducción válida y no obliga a repetirla.
Una frase no soportada en demo explica cómo elegir un ejemplo; no ofrece un reintento
idéntico que volvería a fallar.

## Validación

```powershell
dotnet test tests/Saylo.Desktop.Tests/Saylo.Desktop.Tests.csproj -c Release
dotnet test tests/Saylo.Modules.Conversations.Tests/Saylo.Modules.Conversations.Tests.csproj -c Release
dotnet test tests/Saylo.Architecture.Tests/Saylo.Architecture.Tests.csproj -c Release
dotnet test tests/Saylo.Modules.Translation.Tests/Saylo.Modules.Translation.Tests.csproj -c Release --filter 'FullyQualifiedName~TranslationServiceTests|FullyQualifiedName~ResourceFailureTests|FullyQualifiedName~ContractValidationTests'
```

Las pruebas de Desktop comprueban los ocho ejemplos completos, direcciones y guías,
validación sin truncar, configuración sin fallback, modelos ausentes, copia del worker,
errores de recursos, reintentos con contexto original, cancelación, limpieza y cierre
del recurso propietario. Las pruebas de reproducción nativa optativas del paquete 3
no son necesarias para validar este flujo escrito y se omiten salvo activación explícita.

En este entorno no están instalados el runtime y los modelos de Argos. Se verifica la
conexión y el diagnóstico real de modelos ausentes; la inferencia real y sus tiempos
requieren la instalación previa. La demostración permite revisar el flujo completo sin
esa dependencia y sin presentar sus resultados como traducciones de IA.
