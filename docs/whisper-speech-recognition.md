# Reconocimiento local con Whisper.net

## Alcance y dependencias

Se conservan los 11 proyectos y .NET 8. Solo Translation referencia
`Whisper.net` **1.9.1** y `Whisper.net.Runtime` **1.9.1**, este ultimo para CPU.
No se incorpora AllRuntimes, GPU, NAudio, microfono, traductor ni sintetizador.
Translation.Contracts sigue independiente de Whisper y de paquetes externos.

Fuentes de la version y de las APIs:

- https://www.nuget.org/packages/Whisper.net/1.9.1
- https://www.nuget.org/packages/Whisper.net.Runtime/1.9.1
- https://github.com/sandrohanea/whisper.net/tree/1.9.1
- https://raw.githubusercontent.com/sandrohanea/whisper.net/1.9.1/Whisper.net/WhisperProcessor.cs
- https://raw.githubusercontent.com/sandrohanea/whisper.net/1.9.1/Whisper.net/WhisperProcessorBuilder.cs

## Arquitectura y uso

`WhisperSpeechRecognitionEngine` implementa `ISpeechRecognitionEngine` e
`IAsyncDisposable`. Recibe `IModelCatalog` y un numero configurable de hilos
(4 por defecto). El punto de composicion debera crear una instancia de vida larga
y disponerla al cerrar; no crear una por mensaje. Desktop no se modifica aqui.

```csharp
var descriptor = WhisperModels.BaseMultilingual(configuredModelDirectory);
var catalog = new LocalModelCatalog(new[] { descriptor });
await using var speech = new WhisperSpeechRecognitionEngine(catalog, threads: 4);
string text = await speech.TranscribeAsync(audio, Language.Spanish, cancellationToken);
```

Namespaces: `Saylo.Modules.ModelManagement`,
`Saylo.Modules.Translation.Contracts` y
`Saylo.Modules.Translation.SpeechRecognition`.

El directorio viene de configuracion del anfitrion; no hay rutas personales ni
descarga automatica. `LocalModelCatalog` implementa los contratos anteriores,
localiza modelos configurados y verifica longitud y SHA-256 por lectura asincrona.
La presencia en el catalogo no garantiza integridad. El catalogo deja la
compatibilidad nativa como `Unknown`; el adaptador valida el proposito, idiomas,
integridad y carga real del formato mediante Whisper.

El modelo se carga perezosamente en el primer uso, en un trabajador del pool,
y se conserva en una `WhisperFactory`. Un semaforo serializa toda la operacion,
incluida carga y conversion. Cada solicitud crea un procesador ligero sobre esa
misma factory, configura `WithLanguage("es"/"en")` y `WithNoContext`, y lo dispone
al terminar. No se llama `WithTranslate`: se transcribe, no se traduce al ingles.
Los procesadores no comparten texto previo entre solicitudes.

El `DisposeAsync` del procesador espera al trabajador nativo incluso si la
enumeracion de Whisper se cancela antes de que este termine. Solo entonces se
libera el semaforo. El cierre del motor cancela trabajo activo y en cola, espera
la operacion y libera la factory una sola vez, fuera del hilo de UI. Las
primitivas administradas quedan para el GC para que solicitudes ya en cola
puedan observar el cierre sin acceder a primitivas dispuestas.

No se generan archivos temporales, registros de conversaciones ni peticiones de
red. No se habilitan los loggers de contenido del proveedor. Los histogramas BCL
del Meter `Saylo.SpeechRecognition.Whisper` solo publican numeros:

- `saylo.whisper.model_load` (ms): carga nativa; excluye validacion SHA-256.
- `saylo.whisper.inference` (ms): reconocimiento y limpieza del procesador.
- `saylo.whisper.audio_duration` (s): duracion de las muestras recibidas.

## Modelo e instalacion explicita de desarrollo

Modelo **multilingue** `ggml-base.bin`, no `ggml-base.en.bin`.

- Tamano esperado: **147951465 bytes**.
- SHA-256: `60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe`.
- Fuente verificable: https://huggingface.co/ggerganov/whisper.cpp/raw/main/ggml-base.bin
- Descarga: https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin

El hash y tamano se verificaron contra el puntero Git LFS de la fuente. No
reemplazar el hash por el calculado de cualquier archivo recibido: eso eliminaria
la verificacion contra una fuente confiable.

Desde la raiz del repositorio, PowerShell:

```powershell
$modelDir = Join-Path (Get-Location) 'artifacts/whisper/models'
New-Item -ItemType Directory -Path $modelDir -Force | Out-Null
$modelPath = Join-Path $modelDir 'ggml-base.bin'
Invoke-WebRequest 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin' -OutFile $modelPath
$expectedHash = '60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe'
if ((Get-FileHash -LiteralPath $modelPath -Algorithm SHA256).Hash -ne $expectedHash) {
    throw 'Model SHA-256 mismatch.'
}
if ((Get-Item -LiteralPath $modelPath).Length -ne 147951465) { throw 'Model size mismatch.' }
```

`artifacts/` y los pesos `ggml-*.bin` estan ignorados por Git. El instalador futuro
podra incluir el archivo y este manifiesto para una instalacion offline. Para
actualizar el modelo, cerrar y disponer el motor antes de reemplazar archivos;
el modelo y su hash no se recargan por solicitud.

## Audio soportado

Solo **PCM16 raw**, con signo, little-endian, mono, **16000 Hz**, frames completos.
Los valores se convierten a `float` mediante `sampleInt16 / 32768f`, sin cambiar
frecuencia ni canales. Los extremos son -1 y 32767/32768.

Los contratos ya rechazan datos vacios y frames incompletos. El adaptador rechaza
frecuencias/canales distintos y cabeceras RIFF/WAVE para no interpretarlas como
muestras. No admite WAV directamente ni MP3, stereo o remuestreo. PCM raw no tiene
checksum ni cabecera: no es posible distinguir cualquier secuencia de bytes de
muestras legitimas; se valida su estructura, no su contenido acustico.

Para una muestra WAV autorizada **ya PCM16 mono 16 kHz**:

```powershell
./tools/Convert-WavToPcm16.ps1 -InputPath ./artifacts/whisper/audio/es.wav -OutputPath ./artifacts/whisper/audio/es.pcm
```

Este comando verifica RIFF, formato, longitud y chunks y extrae solo `data`.
No remuestrea ni convierte codecs. Otros formatos deben prepararse con una
herramienta de conversion validada antes de pasar al motor.

## Errores y cancelacion

Se utiliza `TranslationEngineException`, independiente del proveedor, con etapa
`SpeechRecognition` y los codigos existentes/ampliados de Translation.Contracts:
`ModelUnavailable`, `ModelInvalid`, `RuntimeUnavailable`, `UnsupportedAudioFormat`,
`UnsupportedLanguage`, `EngineFailure` e `InvalidEngineOutput`.

El coordinador conserva esos codigos en `TranslationStageFailed`, sin publicar
mensajes internos del proveedor. La cancelacion solicitada sigue propagando
`OperationCanceledException`. Los idiomas publicos solo pueden construirse como
`es` o `en`. No se inventan transcripciones cuando no se reconoce voz.

La carga nativa sincrona no se puede abortar por fuerza: si se cancela durante
la carga, se espera su retorno y se conserva/libera de forma segura el contexto.
El reconocimiento usa el mecanismo de cancelacion de Whisper.

## Pruebas unitarias y reales

Las pruebas unitarias usan un backend simulado interno; no necesitan pesos ni
cargan bibliotecas nativas. Cubren conversion, formatos, catalogo, corrupcion,
errores, cancelacion, exclusion mutua, reutilizacion, cierre y coordinador.
Todas las pruebas anteriores se conservan.

```powershell
dotnet restore
dotnet build Saylo.slnx
dotnet test Saylo.slnx
git diff --check
```

Las **3 pruebas reales**, categoria `WhisperIntegration`, se omiten explicitamente
si no se han configurado modelo, muestras y palabras esperadas. Si se configura
una ruta inexistente, hash incorrecto o salida que no coincide, **fallan**; no se
devuelve exito sin ejecutar. Usar solo audio autorizado, no de empleados/clientes.
Las pruebas reales muestran la transcripcion de esas muestras en su salida.

Comando manual PowerShell desde la raiz, con archivos PCM preparados:

```powershell
./tools/Test-Whisper.ps1 `
    -ModelDirectory ./artifacts/whisper/models `
    -SpanishPcm ./artifacts/whisper/audio/es.pcm `
    -EnglishPcm ./artifacts/whisper/audio/en.pcm `
    -SpanishExpected 'necesito ayuda pedido' `
    -EnglishExpected 'need help order'
```

El script configura variables solo durante el comando y restaura sus valores.
Las palabras esperadas deben corresponder al audio; se comprueban en su idioma
original. Cada idioma se transcribe dos veces para comprobar reutilizacion; otra
prueba cancela una inferencia real y transcribe de nuevo con el mismo contexto.

Para ejecutar toda la solucion incluyendo integracion, configurar:

```powershell
$env:SAYLO_WHISPER_MODEL_DIR = (Resolve-Path ./artifacts/whisper/models).Path
$env:SAYLO_WHISPER_ES_PCM = (Resolve-Path ./artifacts/whisper/audio/es.pcm).Path
$env:SAYLO_WHISPER_EN_PCM = (Resolve-Path ./artifacts/whisper/audio/en.pcm).Path
$env:SAYLO_WHISPER_ES_EXPECTED = 'necesito ayuda pedido'
$env:SAYLO_WHISPER_EN_EXPECTED = 'need help order'
dotnet test Saylo.slnx
```

Prueba de aislamiento offline: instalar previamente paquetes y modelo, preparar
los audios, desconectar la red del equipo de prueba y ejecutar:

```powershell
dotnet test tests/Saylo.Modules.Translation.Tests/Saylo.Modules.Translation.Tests.csproj `
    --no-build --no-restore --filter 'Category=WhisperIntegration' --logger 'console;verbosity=detailed'
```

No se desconecto la red de la PC de desarrollo en esta tarea. La inferencia
ejecutada usa archivos locales; la comprobacion con aislamiento de red efectivo
queda para el piloto offline.

## Evidencia de desarrollo (2026-10-28)

Whisper.net/runtime 1.9.1, .NET 8.0.33, Windows x64, Intel Core i5-11400H,
4 hilos. Muestras sinteticas generadas localmente con SAPI, sin grabaciones de
personas: Microsoft Helena (espanol) y Microsoft David (ingles). SAPI se uso solo
para preparar fixtures, no se integro como motor del producto.

| Idioma | Audio | Inferencia 1 | Inferencia 2 | Carga nativa | Total 1 | Total 2 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| es | 5.820 s | 1688.622 ms | 1533.680 ms | 213.950 ms | 2095.1 ms | 1536.9 ms |
| en | 5.115 s | 1648.819 ms | 1619.206 ms | 160.801 ms | 1968.4 ms | 1625.1 ms |

Salida espanola: "Buenos dias. Necesito ayuda con mi pedido. Muchas gracias."
(el motor devolvio la tilde de "dias"). Salida inglesa: "Good morning. I need help
with my order. Thank you." Se comprobaron los marcadores originales en ambos
idiomas. Tambien paso la cancelacion nativa y reutilizacion posterior.
No son percentiles, pruebas de acentos humanos ni garantia de latencia.

Verificacion final: `dotnet restore` correcto, `dotnet build Saylo.slnx` con
0 errores y 0 advertencias, y `dotnet test Saylo.slnx` con las muestras
configuradas: **100 aprobadas, 0 fallidas, 0 omitidas**. Se agregaron **36 casos**:
33 unitarios y 3 reales. El total conserva las 8 pruebas de arquitectura y las
2 plantillas vacias anteriores de Identity/Conversations. `git diff --check`
sin errores de whitespace.

## Requisitos nativos y limites

Segun la documentacion del runtime 1.9.1:

- Windows: Windows 11 / Server 2022 o posterior y Visual C++ Redistributable de
  Visual Studio 2022; instalar la arquitectura correspondiente (x64 en esta prueba).
- Linux: `libstdc++6` y `glibc >= 2.31`; validar distribucion y arquitectura reales.
- macOS: se distribuyen binarios x64/arm64; el upstream no fija todos los
  prerrequisitos. Debe verificarse carga, firma y despliegue en el destino.
- En x86/x64, el runtime CPU normal requiere AVX, AVX2, FMA y F16C. Equipos sin
  esas instrucciones necesitaran evaluar `Runtime.NoAvx` por separado.

Solo se ejecuto el adaptador nativo en Windows x64. No se afirma soporte completo
de macOS/Linux ni de equipos sin las instrucciones CPU requeridas. No se cambia
globalmente el selector de runtime del proveedor; se instala solo el runtime CPU
y la factory se crea con `UseGpu = false`.

Proxima tarea recomendada: piloto STT con grabaciones autorizadas de personas,
ruido/acento empresarial y aislamiento de red; luego adaptador local de traduccion.
Captura de microfono, remuestreo y registro en Desktop requieren tareas separadas.

## Inventario de cambios

Creados:

- `src/modules/Saylo.Modules.ModelManagement/LocalModelCatalog.cs`
- `src/modules/Saylo.Modules.Translation.Contracts/TranslationEngineException.cs`
- `src/modules/Saylo.Modules.Translation/Properties/AssemblyInfo.cs`
- `src/modules/Saylo.Modules.Translation/SpeechRecognition/WhisperModels.cs`
- `src/modules/Saylo.Modules.Translation/SpeechRecognition/Pcm16Audio.cs`
- `src/modules/Saylo.Modules.Translation/SpeechRecognition/WhisperBackend.cs`
- `src/modules/Saylo.Modules.Translation/SpeechRecognition/WhisperSpeechRecognitionEngine.cs`
- `tests/Saylo.Modules.Translation.Tests/LocalModelCatalogTests.cs`
- `tests/Saylo.Modules.Translation.Tests/WhisperSpeechRecognitionTests.cs`
- `tests/Saylo.Modules.Translation.Tests/WhisperIntegrationTests.cs`
- `tools/Convert-WavToPcm16.ps1`
- `tools/Test-Whisper.ps1`
- `docs/whisper-speech-recognition.md`

Modificados: `.gitignore`, `TranslationUpdate.cs`, `TranslationService.cs`,
`Saylo.Modules.Translation.csproj` y `docs/translation-pipeline.md`.
No se elimina ningun archivo/proyecto.
Modelos, audio y referencias descargadas quedan solo bajo `artifacts/` ignorado.
