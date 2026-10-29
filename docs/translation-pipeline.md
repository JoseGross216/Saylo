# Coordinacion inicial de traduccion

El coordinador se mantiene en .NET 8 y recibe los motores por el constructor de
`TranslationService`, sin conocer proveedores concretos. La integracion STT local
se documenta en [Reconocimiento con Whisper.net](whisper-speech-recognition.md).
El adaptador de traduccion local se documenta en [Traduccion de texto](local-text-translation.md).
Los dobles de prueba existen exclusivamente en el proyecto de pruebas.

## Contratos

- `ISpeechRecognitionEngine.TranscribeAsync`: audio capturado a texto de origen.
- `ITextTranslationEngine.TranslateAsync`: texto entre los idiomas de origen y destino.
- `IPronunciationEngine.GenerateAsync`: representacion fonetica validada y notacion
  `Approximate` o `Ipa`. El coordinador no inventa ni transforma pronunciaciones.
- `ISpeechSynthesisEngine.SynthesizeAsync`: audio y metadatos para reproduccion local.
- `ITranslationService`: operaciones de texto y voz con actualizaciones progresivas.

`Language.FromCode` normaliza espacios y mayusculas; acepta solo `es` y `en`.
Los constructores rechazan texto vacio, idiomas nulos o iguales y audio invalido.
Se conserva el texto original, incluidos sus espacios. Los modelos de entrada
validados no permiten modificar posteriormente sus propiedades.

`AudioData` toma una copia de los bytes y admite inicialmente PCM16 intercalado,
con signo y little-endian. Incluye formato, frecuencia de muestreo, canales y
bits por muestra. Rechaza datos vacios, formatos desconocidos, valores no
positivos y frames incompletos. Los futuros adaptadores deben decodificar WAV,
MP3 u otros contenedores antes de construir este tipo, o ampliar el contrato con
validaciones y pruebas especificas. No se asume validacion acustica ni de codecs.

## Entrega progresiva

Cada enumeracion representa una nueva operacion y genera su propio `OperationId`.
Enumerar de nuevo ejecuta de nuevo los motores. El consumidor debe procesar cada
actualizacion cuando llega, conservando el estado de la operacion en su propia capa.

Texto:

1. Traduccion del texto validado.
2. Entrega de `TextTranslationCompleted`, antes de iniciar pronunciacion o TTS.
3. Inicio de pronunciacion y TTS como tareas independientes.
4. Entrega de cada resultado adicional a medida que termina.

Voz:

1. Recepcion del audio ya capturado y validado.
2. Transcripcion y entrega de `TranscriptionCompleted`.
3. Ejecucion del mismo flujo de texto, utilizando la transcripcion.

Despues del texto, el orden entre pronunciacion y audio depende de cual tarea
finalice primero. No existe un orden garantizado si ambas ya han terminado.
El fin de la enumeracion indica que no quedan etapas pendientes. El consumidor
debe continuar enumerando para que se ejecuten las etapas restantes. Desktop sera
responsable de presentar las actualizaciones y decidir la reproduccion automatica;
este servicio no captura microfono, reproduce audio ni accede a controles graficos.

## Errores y cancelacion

Las entradas invalidas lanzan excepciones de argumentos antes de llamar motores.
Los errores de motores producen `TranslationStageFailed`, con etapa y codigo
`EngineFailure` o `InvalidEngineOutput`. No se exponen mensajes de excepciones de
proveedores que puedan contener datos del usuario.
Los adaptadores tambien pueden lanzar `TranslationEngineException` con codigos
especificos de modelo, runtime o formato; el coordinador conserva esos codigos.

Un fallo de STT o traduccion termina el flujo. Un fallo de pronunciacion o TTS
no elimina el texto ya entregado ni impide completar la otra etapa opcional.
Las salidas textuales vacias y las respuestas nulas se rechazan explicitamente.

El token del metodo y el token de enumeracion se respetan. Todos los motores de
una operacion reciben un token vinculado, que tambien permite cancelar trabajo
pendiente al abandonar la enumeracion. La cancelacion solicitada propaga
`OperationCanceledException`; no se convierte en un error de motor. Una excepcion
de cancelacion del proveedor sin cancelacion solicitada se identifica como fallo
de esa etapa.

Al disponer la enumeracion se cancelan y esperan las tareas opcionales para
observarlas y permitir liberar recursos. Los motores deben respetar el token;
un motor que lo ignore puede retrasar esta limpieza. No se puede interrumpir
por fuerza un motor nativo desde estos contratos. No se persisten datos, no se
reproducen audios y no se realizan llamadas de red.

## Archivos de esta implementacion

Creados:

- `src/modules/Saylo.Modules.Translation.Contracts/Language.cs`
- `src/modules/Saylo.Modules.Translation.Contracts/AudioData.cs`
- `src/modules/Saylo.Modules.Translation.Contracts/TranslationRequests.cs`
- `src/modules/Saylo.Modules.Translation.Contracts/PronunciationResult.cs`
- `src/modules/Saylo.Modules.Translation.Contracts/Engines.cs`
- `src/modules/Saylo.Modules.Translation.Contracts/TranslationUpdate.cs`
- `src/modules/Saylo.Modules.Translation.Contracts/ITranslationService.cs`
- `src/modules/Saylo.Modules.Translation/TranslationService.cs`
- `tests/Saylo.Modules.Translation.Tests/ContractValidationTests.cs`
- `tests/Saylo.Modules.Translation.Tests/TestEngines.cs`
- `tests/Saylo.Modules.Translation.Tests/TranslationServiceTests.cs`
- `docs/translation-pipeline.md`

Modificado:

- `tests/Saylo.Modules.Translation.Tests/Saylo.Modules.Translation.Tests.csproj`:
  referencia al modulo de traduccion; contratos accesibles transitivamente.

Eliminados unicamente los placeholders vacios `Class1.cs` de Translation y
Translation.Contracts y `UnitTest1.cs` del proyecto de pruebas de Translation.
Los proyectos y las pruebas de arquitectura existentes se conservan.

## Decisiones posteriores

Seleccion de motores locales, variantes de voz y pronunciacion, limites de
duracion de captura, tiempos maximos por motor, adaptacion de formatos y
registro en el punto de composicion de Desktop. No se implementan en esta tarea.
