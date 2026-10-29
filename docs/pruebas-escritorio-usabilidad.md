# Pruebas funcionales de escritorio y usabilidad — Paquete 6

Fecha: 29 de octubre de 2026, America/Santo_Domingo.
Rama: `feature/jencer-paquete6-pruebas-manual-docs`.
Base de aplicación: paquete 5, commit `4837830`. Este paquete añade pruebas y
documentación; no cambia la composición de motores de la aplicación.

## Alcance y significado de RF-026

Fuente funcional: SRS SAYLO Empresarial v0.6, 28/10/2026, sección 4, página 5.
RF-026 exige que el audio traducido comience una sola vez al terminar la síntesis,
sin pulsar reproducir, tanto desde escritura como desde grabación. Repetir permite
una escucha posterior. Se comprueba con autoplay activado, su valor inicial; la
opción explícita de desactivarlo se valida por separado.

La evaluación general de usabilidad corresponde también a RNF-009 y RNF-010
(sección 5.4, página 7). RNF-026, distinto de RF-026, trata la calidad y variante
de la guía fonética; requiere validación humana con vocabulario empresarial.

## Entorno y tipos de evidencia

- Sistema reportado: Microsoft Windows 10.0.26300, arquitectura X64.
- SDK: .NET 10.0.401; proyectos y aplicación: `net8.0`.
- Interfaz: Avalonia 12.1.4, MVVM y bindings compilados.
- Compilación y resultados aislados en `artifacts/package6/`, ignorados por Git.
- Sin instalación de modelos reales, sin grabar personas y sin conexión a telefonía.

Las pruebas de interacción usan ViewModels y contratos reales con adaptadores
controlados; no son una automatización de clics de Avalonia. Las cuatro pruebas
nuevas usan además el `TranslationService` real y sus etapas asíncronas. Las dos
comprobaciones Windows optativas sí acceden a la salida de audio, con PCM de ceros,
sin tonos ni habla. Ninguna de ellas valida calidad de una voz sintetizada.

La revisión visual/teclado de la ventana es una inspección técnica. No hubo una
muestra de empleados ni mediciones de usuarios; el
[protocolo del piloto](protocolo-usabilidad.md) está preparado para esa evaluación.

## Matriz de cobertura funcional

| Caso | Requisitos / recorrido | Evidencia reproducible |
| --- | --- | --- |
| F-01 | Origen, destino e intercambio ES↔EN, sin parejas inválidas | `WorkspaceViewModelTests`, `AudioInteractionTests`, `WrittenTranslationFlowTests` |
| F-02 | Texto válido, espacios, límite visible sin recortar y errores de modelos | `WrittenTranslationFlowTests` |
| F-03 | Captura voluntaria, límite 30 s, cancelar arranque o transcripción y liberar micrófono | `AudioInteractionTests` con adaptador y reloj controlados |
| F-04 | RF-021: corregir y confirmar la transcripción antes de traducir | `AudioInteractionTests`, `EmployeeAttentionWorkflowTests` |
| F-05 | RF-026: autoplay desde escrito ES→EN, escrito EN→ES, voz revisada ES→EN y voz revisada EN→ES | Cuatro casos de `EmployeeAttentionWorkflowTests.AnEmployeeCanPrepareReadHearRepeatCopyAndClearAnAttention` |
| F-06 | RF-027: texto visible mientras TTS y pronunciación siguen pendientes; voz no espera a la guía | `TranslationInteractionTests`, `EmployeeAttentionWorkflowTests` |
| F-07 | Autoplay único ante duplicados, desactivación explícita, repetir sin traducir ni sintetizar otra vez | `ChatInteractionTests`, `PlaybackStateAndErrorTests`, `EmployeeAttentionWorkflowTests` |
| F-08 | RF-010: detener, evitar superposición y recuperar detención fallida antes de grabar | `ChatInteractionTests`, `PlaybackStateAndErrorTests`, `NativePlaybackTests` optativas |
| F-09 | RF-022: copiar solo traducción y recuperar un fallo de portapapeles | `ChatInteractionTests`, `EmployeeAttentionWorkflowTests`; portapapeles doble, sin alterar el del empleado |
| F-10 | RF-023: confirmar o conservar al limpiar, mantener borrador y borrar referencias | `ChatInteractionTests`, `ConversationSessionTests`, `EmployeeAttentionWorkflowTests` |
| F-11 | Cancelación, resultados tardíos, cierre y reapertura sin recuperar la sesión | `TranslationInteractionTests`, `ChatInteractionTests`, `WrittenTranslationFlowTests`, `EmployeeAttentionWorkflowTests` |
| F-12 | Errores de micrófono, recursos y síntesis conservan texto y no exponen mensajes internos | `AudioInteractionTests`, `WrittenTranslationFlowTests`, `PlaybackStateAndErrorTests` |
| F-13 | Etiquetas y guía del idioma de destino en los ocho ejemplos | `WrittenTranslationFlowTests`; comprueba datos de ejemplo, no calidad de motores reales |
| F-14 | Separación Conversations/Translation y contratos, sin dependencia UI en el dominio | `SolutionArchitectureTests` |

Las pruebas F-05 y F-06 retienen deliberadamente la síntesis y la pronunciación:
antes de liberar la voz no hay reproducción, el texto ya es legible y copiable;
al terminar TTS aparece la primera reproducción sin ejecutar un comando de Play.
Tras detener y repetir, se comprueba la misma instancia de audio, una traducción,
una síntesis y un máximo de una reproducción concurrente. El recorrido incluye
corrección de transcripción, copia, conservar/confirmar limpieza y reapertura vacía.

## Ejecución reproducible

Desde la raíz, con SDK y dependencias disponibles y sin ejecutar la misma copia de
la aplicación que va a recompilarse:

```powershell
./tools/Test-Desktop.ps1
./tools/Test-Desktop.ps1 -NativeAudio
./tools/Test-Desktop.ps1 -NativeAudio -IncludeTranslation
```

`-NativeAudio` solicita las dos pruebas de salida Windows con PCM silencioso;
sin Windows o sin la opción se registran como omitidas. `-IncludeTranslation`
añade toda la suite backend, incluidas comprobaciones de motores que pueden
quedar omitidas por falta de configuración.

El ejecutor guarda un TRX por suite y `summary.json` en
`artifacts/package6/test-results/`. Registra aprobadas, fallidas, omitidas y código
de salida; devuelve 1 si una suite falla, no produce resultados o no ejecuta pruebas.
Elimina el TRX anterior de esa suite para no confundir una compilación fallida con
resultados previos. Restaura las variables de entorno de audio y MSBuild al finalizar.

`-NoRestore` presupone restauración previa con la misma carpeta de artifacts.
`-NoBuild` utiliza los binarios ya compilados; no debe utilizarse para dar por
probadas modificaciones de fuentes posteriores a esa compilación. Si se abre una
ventana para inspección, hacerlo desde una copia separada de los binarios y sus
dependencias para evitar bloqueos de DLL en Windows.

Para reproducir exclusivamente los cuatro recorridos nuevos:

```powershell
dotnet test tests/Saylo.Desktop.Tests/Saylo.Desktop.Tests.csproj `
  --artifacts-path artifacts/package6 -c Release `
  --filter FullyQualifiedName~EmployeeAttentionWorkflowTests
```

## Registro de esta ejecución

Los cuatro recorridos nuevos de RF-026 se compilaron y ejecutaron: **4 aprobados**.
Resultado local: `artifacts/package6/test-results/employee-attention.trx`.

La primera ejecución ampliada tuvo un bloqueo de DLL por la ventana de
demostración abierta. Esa ejecución de Desktop no produjo TRX y no cuenta como
una validación funcional aprobada. Se registra por separado de los defectos de la
aplicación; el resumen del ejecutor debe reflejar el fallo de compilación.

Después de separar los binarios de la ventana, la ejecución Release con
compilación concluyó con estos resultados:

| Suite | Aprobadas | Fallidas | Omitidas |
| --- | ---: | ---: | ---: |
| Desktop, incluidas cuatro nuevas y dos de salida Windows silenciosa | 90 | 0 | 0 |
| Conversations | 8 | 0 | 0 |
| Architecture | 8 | 0 | 0 |
| Translation ampliada | 117 | 1 | 5 |
| **Total** | **223** | **1** | **5** |

Comando final: `./tools/Test-Desktop.ps1 -NativeAudio -IncludeTranslation -NoRestore`.
Código de salida: **1**. Las 106 pruebas de escritorio, conversación y arquitectura
están aprobadas; la batería ampliada **no está completamente aprobada**.
Se conserva una copia del [resumen final](evidence/package6-summary.json) en la
documentación. Los TRX completos quedan en artifacts, fuera de Git, pues contienen
rutas del entorno de ejecución. La primera ejecución se conservó en `first-run/`.

Una ejecución intermedia con `-NoBuild` había aprobado 224 pruebas y omitido cinco.
Al repetir con compilación se reprodujo la incidencia histórica de Whisper:
`WhisperSpeechRecognitionTests.DisposalDuringModelLoadWaitsAndReleasesTheCreatedModel`,
línea 268, esperaba `OperationCanceledException` y la tarea terminó sin excepción.
Su variación entre ejecuciones se registra como **intermitente**, no como resuelta.
El archivo afectado procede del commit `86df9f4` y no se modifica en este paquete.
Equipo de reconocimiento: revisar la coordinación entre carga del modelo,
cancelación y cierre; repetir la prueba de manera controlada después de corregirla.

Las cinco omisiones corresponden a tres integraciones Whisper y dos Argos con
modelos/configuración reales no disponibles. No son pruebas aprobadas ni una
demostración de operación offline en un puesto instalado.

## Inspección técnica de usabilidad ejecutada

Se abrió la aplicación real con `--demo`, primero desde su salida compilada y
después desde `artifacts/package6/ui-demo/`. Solo se utilizaron frases de ejemplo.
La ventana se inspeccionó en su tamaño inicial de 1080 × 860 unidades lógicas.

| Comprobación | Observación |
| --- | --- |
| Modo y disponibilidad | Cabecera Demostración, aviso de captura pendiente y Traducir inicialmente deshabilitado. No se muestran micrófonos ficticios. |
| Preparación de texto | Usar ejemplo carga Buenos días., actualiza el contador y habilita Traducir. |
| Envío por teclado | Ctrl+Enter produce el mensaje de ejemplo y el estado Listo sin cambiar el borrador. |
| Intercambio por teclado | Ctrl+Mayús+S cambia visualmente origen a Inglés y destino a Español, conserva el borrador anterior y selecciona el ejemplo Good morning. |
| Navegación y foco | Desde Usar ejemplo, Tab alcanza Borrar texto y después el editor; se observa borde de foco y texto legible. No equivale a una auditoría de lector de pantalla. |
| Contenido del resultado | El árbol de accesibilidad expone original, traducción, guía aproximada, aviso de demo sin audio y las acciones de copia/repetición; el desplazamiento del chat permite llegar a la traducción. |
| Recorrido inverso | Con origen Inglés y destino Español, Usar ejemplo y Ctrl+Enter producen Good morning. → Buenos días.; el desplazamiento muestra la guía de ejemplo Bué-nos dí-as. |
| Confirmación de limpieza | Limpiar chat abre la advertencia que indica que el borrador se conserva y ofrece Conservar chat / Eliminar conversación. No se confirma el borrado desde la automatización de UI; ambas decisiones se prueban en las suites de interacción. |

**Hallazgo UI-01, prioridad media:** en Demostración, con la ventana inicial y una
respuesta corta, el original ocupa la porción visible del chat y la traducción
requiere desplazarse dentro de él. La respuesta existe y el scroll funciona, pero
su descubrimiento añade pasos. Se documenta el gesto en el manual; queda pendiente
mejorar el reparto de espacio o la visibilidad del resultado y repetir U-02 con
empleados. No se considera una pérdida de datos ni se declara resuelto.

La infraestructura de inspección presentó tiempos de espera en `list_windows` y
`get_window_state`, incluido `FrameArrived timed out: timed out waiting on channel`.
Se recuperó el cliente y se inspeccionaron capturas y controles válidos antes de
actuar. Estos incidentes limitan la evidencia visual: la ventana mínima 760 × 640,
la navegación completa sin ratón, las ayudas técnicas y los recorridos con motores
reales siguen pendientes. No se registran como aprobados por existir pruebas de
ViewModels.

## Límites de aceptación y seguimiento

| Pendiente | Implicación y responsable |
| --- | --- |
| Captura nativa y conexión de STT, pronunciación y TTS a Desktop | TI/equipo de motores: no puede aprobarse RF-026 completo con voz real en el ejecutable actual. |
| Reintento de generación de voz cuando TTS falla | El SRS RF-009 solicita reintento; actualmente Repetir solo actúa si ya existe audio. Equipo de motores/UI: incorporar recuperación de síntesis sin perder el texto. El manual explica el límite actual. |
| Inferencia real ES↔EN y calidad fonética | TI/revisor bilingüe: instalar modelos aprobados y evaluar frases empresariales fuera de los ejemplos. |
| Usuarios reales y lector de pantalla | Supervisor: ejecutar U-01 a U-09 y registrar ayudas, errores y claridad; la presencia de etiquetas no certifica accesibilidad completa. |
| Offline, latencia y consumo en el puesto | TI: medir con motores instalados y red deshabilitada; no inferirlo de los dobles ni de la duración de xUnit. |
| macOS y Ubuntu | Equipo de plataforma: pruebas propias con adaptadores nativos y evidencia por sistema, según RNF-004. |

El manual y la lista operativa permiten preparar el piloto, pero no se declara
cumplimiento integral del SRS ni aptitud para una atención real con voz hasta
resolver estas condiciones.
