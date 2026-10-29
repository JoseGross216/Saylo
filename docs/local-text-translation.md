# Traduccion local de texto es <-> en

Validacion ampliada de 80 frases, rendimiento, estabilidad y licencias:
[Tarea 3.1](translation-quality-validation.md). Sus resultados y pendientes
complementan las 14 muestras historicas descritas en esta guia.

## Decision tecnica y evaluacion de Argos

Se evaluo **Argos Translate 1.11.0** en Python **3.12.14**, Windows x64. PyPI
tambien publica las versiones estables anteriores 1.10.0 y 1.9.6. El catalogo
oficial contiene modelos directos es->en 1.9 y en->es 1.0.

La instalacion completa funciono, pero incorporo Stanza, PyTorch, spaCy,
MiniSBD y ONNX Runtime, y ocupo **1,021,488,363 bytes** en el entorno de evaluacion.
La rueda MiniSBD 0.9.5 incluye un LICENSE AGPLv3 y metadatos AGPLv3; algunos
modulos internos tienen otros avisos. La licencia MIT/CC0 de Argos no resuelve
las obligaciones de cada dependencia. Ademas, el frontend Argos tiene rutas de
descarga de segmentadores y `ChunkType.NONE` no esta implementado en
`PackageTranslation` de 1.11.0. No se parchea esa biblioteca ni se utiliza su
frontend completo en el producto.

| Opcion | Evaluacion para esta tarea |
| --- | --- |
| Argos 1.11.0 completo | Viable tecnicamente, pero demasiado amplio para mensajes cortos y con obligaciones pendientes en MiniSBD/segmentadores. |
| CTranslate2 CPU + modelos Argos | **Seleccionado**. APIs publicas de inferencia y tokenizacion, sin los segmentadores ni dependencias anteriores. |
| Marian con Transformers | Alternativa local; requeriria otra integracion y una distribucion con PyTorch. No se midio. |
| Marian convertido a ONNX | Alternativa local; requiere conversion, tokenizacion y validacion propias. No se implemento ni midio. |

El motor final es **CTranslate2 4.8.4 CPU/int8**, usando los archivos de modelos
distribuidos en formato Argos y tokenizadores **Moses + BPE** (es->en) y
**SentencePiece** (en->es). La clase C# `ArgosTextTranslationEngine` se llama asi
por el formato de los paquetes; **no ejecuta `argostranslate.translate` ni requiere
instalar `argostranslate`**. No se copia codigo del frontend, se usan las APIs de
`ctranslate2`, `sacremoses`, `subword-nmt` y `sentencepiece`.

Fuentes primarias consultadas:

- https://pypi.org/project/argostranslate/1.11.0/
- https://github.com/argosopentech/argos-translate/tree/v1.11.0
- https://github.com/argosopentech/argospm-index
- https://pypi.org/project/minisbd/0.9.5/
- https://opennmt.net/CTranslate2/installation.html
- https://opennmt.net/CTranslate2/python/ctranslate2.Translator.html
- https://github.com/OpenNMT/CTranslate2/blob/master/LICENSE
- https://pypi.org/project/subword-nmt/0.3.8/

## Arquitectura

Se conservan .NET 8, los 11 proyectos y el contrato publico `ITextTranslationEngine`.
No se modifica Desktop, Conversations, Whisper ni las reglas de `TranslationService`.
No se agregan paquetes NuGet ni implementaciones de pronunciacion/TTS.

`ArgosTextTranslationEngine` recibe opciones y `IModelCatalog`. En su primer uso:

1. Localiza los dos modelos y verifica integridad con `LocalModelCatalog`.
2. Inicia el Python configurado con `UseShellExecute=false`, sin ventana ni shell.
3. El worker verifica nuevamente los archivos y metadatos del manifiesto, incluidas
   rutas y archivos adicionales inesperados, y carga ambos modelos en CPU.
4. Inicializa extensiones numericas y tokenizadores antes del hilo lector de stdin.
   Este orden evita el bloqueo de inicializacion nativa observado durante el desarrollo.
5. Emite un mensaje `ready`; las siguientes solicitudes reutilizan el proceso y modelos.

Una instancia de vida larga por aplicacion, creada en el punto de composicion
futuro, evita cargar el motor por mensaje. Un semaforo serializa solicitudes y
protege el protocolo. La carga, hash y arranque son asincronos o se ejecutan en
un trabajador, sin trabajo nativo sincrono en el hilo de UI.

El worker es un proceso auxiliar de la aplicacion, no un microservicio ni un
servidor HTTP. No escucha puertos, no ejecuta descargas, no almacena textos y no
implementa telefonia, cuentas o base de datos.

## Protocolo y privacidad

JSON por lineas UTF-8 en stdin/stdout, protocolo 1, ID unico por solicitud:

```json
{"protocol":1,"id":"correlation-id","text":"authorized test text","source":"es","target":"en"}
```

La respuesta repite ID y version y devuelve `ok`/`text`, o un codigo de fallo.
La salida de bibliotecas se desvincula del descriptor de stdout y se dirige a
stderr, que C# drena sin retener ni registrar su contenido. Las excepciones del
worker no imprimen cuerpos de solicitudes ni mensajes de proveedores.

Solo las rutas de configuracion y el numero de hilos pasan por argumentos.
Los textos nunca aparecen en argumentos ni archivos temporales. Los resultados
viven en memoria y en la respuesta; las pruebas reales usan exclusivamente
fixtures publicos incluidos en el codigo y pueden emitir un informe autorizado.
Se deshabilita la escritura de bytecode con `-B`; `-I` evita paquetes del usuario
y PYTHONPATH. Python y dependencias vienen de una ruta explicita, no de PATH.

Un audit hook rechaza conexion de sockets, DNS, bind y solicitudes urllib dentro
del worker. Esta defensa se probo, pero no sustituye aislamiento de red del SO
para bibliotecas nativas. No se desconecto la red de la PC durante esta tarea.

## ModelManagement e integridad

`ArgosModels.ReadDescriptors` lee `argos-models.json`, valida esquema, IDs,
versiones y directorios y crea `ModelDescriptor` de proposito `TextTranslation`.
`LocalModelCatalog` existente comprueba disponibilidad, longitud y SHA-256.
No se modifica su contrato. El manifiesto enumera modelo, tokenizer y metadatos;
el worker rechaza diferencias entre la instalacion y esos archivos.

| Direccion | ID | Version | Paquete oficial |
| --- | --- | --- | --- |
| es->en | argos-es-en | 1.9 | translate-es_en-1_9.argosmodel |
| en->es | argos-en-es | 1.0 | translate-en_es-1_0.argosmodel |

Descargas explicitas desde los enlaces del catalogo oficial:

- https://argos-net.com/v1/translate-es_en-1_9.argosmodel
- https://argos-net.com/v1/translate-en_es-1_0.argosmodel

SHA-256 observados en esos archivos, fijados para reproducir esta evaluacion:

- es->en: `c54df2b62fceaf54a3ce5d97db6bf56efd7940063329f6778f4212d2acb370d4`
- en->es: `d698d0ef87ad70d5d184b7fa6965905bf4368f09a2bb9ffb165a79bac96af0c4`

Son hashes revisados de descargas HTTPS, no firmas del editor ni hashes publicados
por el catalogo, que no los proporciona. El instalador futuro debe aprobar los
artefactos, firmar su manifiesto y proteger las rutas de instalacion. No modificar
pesos o manifiestos mientras el worker este activo; cerrarlo antes de actualizar.

`Install-ArgosModels.py` verifica los hashes de archivo, metadatos y rutas antes
de instalar en un destino nuevo. Excluye los modelos Stanza, que no se usan.
La instalacion final de ambos paquetes ocupa **410,860,373 bytes**. Pesos,
entornos y archivos descargados quedan en `artifacts/`, fuera de Git.

## Instalacion explicita de desarrollo (PowerShell)

Usar un Python **3.12 x64** de desarrollo. La PC de evaluacion tiene Python 3.15
global, que no se uso: no se presupone disponibilidad de todas las ruedas para el.
Desde la raiz del repositorio:

```powershell
$python312 = 'RUTA_COMPLETA_AL_PYTHON_3_12_DE_DESARROLLO'
& $python312 -m venv ./artifacts/argos/runtime
$python = (Resolve-Path ./artifacts/argos/runtime/Scripts/python.exe).Path
& $python -m pip install -r ./tools/argos/requirements-windows-x64.lock
& $python -m pip check
& $python ./tools/Install-ArgosModels.py `
    --destination ./artifacts/argos/models `
    --archives ./artifacts/argos/downloads --download
```

`--download` autoriza explicitamente las descargas si faltan archivos. Para una
instalacion sin red, proporcionar los dos archivos en `--archives` y omitir ese
flag. El instalador rechaza un directorio de modelo existente para evitar mezclar
versiones. El perfil reducido y su lock no incluyen Argos, MiniSBD, Stanza,
PyTorch, spaCy ni ONNX Runtime. El entorno medido ocupa **143,101,381 bytes**.

Dependencias principales: CTranslate2 4.8.4, NumPy 2.5.4, SentencePiece 0.2.2,
Sacremoses 0.1.1 y subword-nmt 0.3.8. El lock fija tambien las transitivas.
`requirements.txt` declara el perfil; el lock corresponde al Windows x64 probado.

## Uso en C#

```csharp
var options = new ArgosTranslationOptions(configuredPythonPath, configuredModelDirectory);
var catalog = ArgosModels.CreateCatalog(options.ModelDirectory);
await using var engine = new ArgosTextTranslationEngine(options, catalog);
var text = await engine.TranslateAsync("Buenos dias.", Language.Spanish,
    Language.English, cancellationToken);
```

Namespace del adaptador: `Saylo.Modules.Translation.TextTranslation`.
El punto de composicion podra inyectar `engine` en `TranslationService`.
En voz se utiliza la transcripcion de Whisper; en ambos flujos el coordinador
entrega el texto antes de las etapas opcionales. Esta tarea no conecta controles UI.

Opciones: ruta Python, modelos, worker, directorio de datos no sensibles,
hilos CPU (4), tiempo de arranque (60 s) y traduccion (30 s).
El worker se copia a `TextTranslation/argos_worker.py` en build/publish.

## Errores, cancelacion y cierre

Se rechazan texto vacio, idiomas nulos/iguales/no soportados y entradas de mas
de 1000 unidades UTF-16. Solo es/en; no se hace pivote por un tercer idioma.
Se procesan pasajes cortos y se conservan saltos de parrafo. No hay segmentador
neural. La salida tiene un limite de 512 tokens; alcanzar el limite se trata como
error para no presentar una salida potencialmente truncada como completa.

Se reutilizan `TranslationEngineException` y codigos anteriores: `ModelUnavailable`,
`ModelInvalid`, `RuntimeUnavailable`, `EngineFailure` e `InvalidEngineOutput`, con
etapa `TextTranslation`. El coordinador ya conserva esos codigos.

- Cancelar una solicitud en cola no afecta a la activa.
- Cancelar una activa o agotar su tiempo descarta y cierra el worker. La API nativa
  sincrona no se interrumpe por fuerza dentro de Python; terminar el proceso es
  la frontera de cancelacion. La siguiente solicitud carga uno nuevo.
- Un fallo de protocolo, ID, EOF o JSON invalido descarta la sesion.
- `DisposeAsync` cancela trabajo, espera el semaforo y cierra stdin. El lector
  del worker detecta EOF y sale incluso si hay inferencia nativa en curso.
- Si no termina en 3 s, C# termina el arbol y espera hasta 5 s mas. El cierre es
  idempotente. Las pruebas comprueban que el PID real, no solo el launcher del
  virtualenv Windows, haya terminado.

## Pruebas reproducibles

Unitarias .NET con sesiones simuladas: no requieren Python ni modelos.
Pruebas Python de protocolo/audit hook: biblioteca estandar, sin modelos.
Reales: categoria `ArgosIntegration`, solo con variables configuradas.
La ausencia de configuracion marca omision; una ruta configurada incorrecta,
modelo corrupto o traduccion sin marcadores esperados hace fallar la prueba.

```powershell
dotnet restore
dotnet build Saylo.slnx
dotnet test Saylo.slnx
& $python -B -m unittest discover `
    -s ./tests/Saylo.Modules.Translation.Tests/Python -p 'test_*.py' -v
./tools/Test-Argos.ps1 -PythonExecutable $python `
    -ModelDirectory ./artifacts/argos/models `
    -ReportPath ./artifacts/argos/benchmark.json
git diff --check
```

El script restaura las variables de entorno al terminar. Para incluir las reales
en la solucion completa:

```powershell
$env:SAYLO_ARGOS_PYTHON = $python
$env:SAYLO_ARGOS_MODELS = (Resolve-Path ./artifacts/argos/models).Path
dotnet test Saylo.slnx
```

Las pruebas Whisper conservadas tienen configuracion independiente, documentada
en `whisper-speech-recognition.md`. Una omision no cuenta como prueba aprobada.

Validacion final ejecutada con los modelos y fixtures de ambos motores
configurados: `dotnet restore` correcto, `dotnet build Saylo.slnx` con **0 errores
y 0 advertencias**, y `dotnet test Saylo.slnx` con **130 aprobadas, 0 fallidas,
0 omitidas**. Se agregaron **30 casos .NET** (28 unitarios y 2 reales) a los
100 anteriores; el total conserva las 2 plantillas vacias de Identity/Conversations.
Ademas se ejecutaron **7 pruebas Python** nuevas, todas aprobadas.
`git diff --check` sin errores y sin workers Python restantes al terminar.

### Prueba con la red del equipo deshabilitada

1. Instalar/restaurar paquetes, modelos y compilar con red disponible.
2. Configurar las dos variables anteriores.
3. En una PC de prueba, desconectar Wi-Fi/Ethernet.
4. Ejecutar el siguiente comando sin build ni restore; ambas pruebas deben pasar.
5. Reconectar la red al terminar.

```powershell
dotnet test ./tests/Saylo.Modules.Translation.Tests/Saylo.Modules.Translation.Tests.csproj `
    --no-build --no-restore --filter 'Category=ArgosIntegration' --logger 'console;verbosity=detailed'
```

No se modificaron adaptadores de red de la PC de desarrollo. Se ejecuto inferencia
local con la politica de sockets/DNS del worker activa y se probo su rechazo;
la desconexion efectiva del equipo queda pendiente de ese procedimiento manual.

## Evidencia y rendimiento (2026-10-28)

Windows x64, .NET 8.0.33, CPython 3.12.14, i5-11400H, 4 hilos,
CPU/int8, ambos modelos cargados en el mismo proceso. Perfil reducido, sin Argos
completo instalado en ese entorno. `pip check` sin dependencias rotas.

- Arranque incluyendo validacion/inicio del worker: **2000.716 ms**.
- Primera traduccion es->en, total con arranque: **2091.446 ms**;
  solicitud/inferencia e IPC: **88.281 ms**.
- Primera en->es, con worker ya iniciado: **31.893 ms**.
- Posteriores es->en: **40.817-97.031 ms**, mediana **54.641 ms** (6 muestras).
- en->es: **30.885-59.465 ms**, mediana **34.977 ms** (7 muestras).
- Working set del PID Python real: **259.2-262.4 MiB**. No incluye el anfitrion
  .NET ni Whisper; no es memoria maxima garantizada ni memoria privada exclusiva.

Meter BCL `Saylo.TextTranslation.Argos`: histogramas `saylo.argos.startup` (ms),
`saylo.argos.translation` (ms) y `saylo.argos.worker_memory` (bytes). No incluyen
textos ni etiquetas de clientes. Son observaciones de esta muestra, no SLA/p95.

## Resultados reales y evaluacion cualitativa

Se procesaron las 10 frases solicitadas y cuatro controles adicionales. Resumen:

| Entrada | Salida real |
| --- | --- |
| Buenos dias, en que puedo ayudarle? | Good morning, how can I help you? |
| Puede proporcionarme su numero de pedido? | Can you give me your order number? |
| Voy a verificar la informacion. | I'll check information. |
| Su solicitud ha sido procesada correctamente. | Your request has been properly processed. |
| Gracias por comunicarse con nosotros. | Thank you for contacting us. |
| How can I help you today? | ¿Cómo puedo ayudarte hoy? |
| Could you please confirm your order number? | ¿Podría confirmar su número de pedido? |
| Let me check that information. | Déjame revisar esa información. |
| Your request has been processed successfully. | Su solicitud ha sido procesada con éxito. |
| Thank you for contacting us. | Gracias por contactarnos. |
| No cancele el pedido 12345. El importe es 25 euros. | Do not cancel an order 12345. The amount is 25 euro. |
| Do not cancel order 12345. The amount is 25 euros. | No cancele el pedido 12345. La cantidad es de 25 euros. |
| La ingeniera Maria revisara el servidor SQL. | The engineer Maria will check the SQL server. |
| Please ask Stanley to restart the SQL server. | Por favor, pida a Stanley que reinicie el servidor SQL. |

Negaciones, cantidades 12345/25, preguntas, nombres Maria/Stanley y SQL se
conservaron en estas muestras. Problemas encontrados:

- Registro inconsistente: tuteo en `ayudarte`/`Déjame`, tratamiento formal en otras.
- `Could you please...` pierde el `por favor`, aunque conserva la pregunta cortes.
- `I'll check information` es poco natural y pierde el articulo/matiz de especificidad.
- `an order 12345` cambia articulo definido y `25 euro` tiene un error de numero.
- `ingeniera` pasa a un sustantivo ingles sin marca de genero; no debe asumirse
  fidelidad de todos los matices, nombres o terminos por estos dos ejemplos.

Los marcadores automatizados son controles de regresion, no una certificacion de
calidad semantica. Hace falta evaluacion bilingue empresarial mas amplia y un
glosario aprobado; no se corrigen salidas con sustituciones improvisadas.

## Licencias y distribucion comercial

Inventario de metadatos realmente instalados en
`local-translation-dependency-licenses.json`; no reemplaza los avisos originales.

| Elemento | Evidencia / obligacion pendiente |
| --- | --- |
| CPython | PSF y avisos de sus componentes; incluir licencia del runtime distribuido. |
| CTranslate2 4.8.4 | MIT; conservar avisos y revisar los de MKL/oneDNN/OpenMP incluidos en la rueda nativa. |
| SentencePiece 0.2.2 | Apache-2.0, conservar avisos. |
| Sacremoses 0.1.1 / subword-nmt 0.3.8 | MIT, conservar avisos. |
| NumPy y transitivas | Metadatos BSD/MIT/Apache/Zlib/CC0, CNRI-Python y MPL-2.0/MIT en tqdm; revisar textos y componentes empaquetados. |
| Argos 1.11.0 evaluado | MIT/CC0 para el proyecto; no se distribuye con este perfil. |
| MiniSBD 0.9.5 evaluado | LICENSE principal AGPLv3; excluido del perfil final, igual que Stanza/PyTorch/spaCy/ONNX Runtime. |
| es->en 1.9 | README del paquete declara modelo OPUS original CC-BY 4.0: atribucion, enlace a licencia y cambios/conversiones deben documentarse. |
| en->es 1.0 | README menciona OpenSubtitles/ParaCrawl/UNPC, pero no declara una licencia especifica de los pesos. **Redistribucion comercial pendiente de confirmar**. |

No se infiere la licencia de un modelo a partir de la del motor o del indice.
Antes de distribuir: confirmar permiso de los pesos en->es y procedencia, aprobar
atribuciones de ambos modelos, avisos de todas las ruedas/binarios y elaborar
SBOM y manifiestos firmados. Si no se obtiene esa evidencia, sustituir ese modelo
por uno con licencia de pesos verificable y repetir las pruebas. La integracion
tecnica no equivale a autorizacion comercial del instalador.

## Empaquetado Windows y compatibilidad

El futuro instalador debe incluir ejecutable .NET, worker, CPython 3.12 x64
privado, dependencias nativas/Python y modelos aprobados. No copiar el virtualenv
de desarrollo: su referencia al interprete base no es relocatable. Preparar el
runtime privado en CI con CPython embeddable y rutas de site-packages controladas,
o una distribucion privada equivalente, y validar en Windows limpio sin Python.
El worker ya recibe una ruta explicita; no exige PATH, pip o Visual Studio al usuario.

Para preparar un wheelhouse de instalacion offline, en un entorno de construccion:

```powershell
& $python -m pip download --only-binary=:all: `
    -r ./tools/argos/requirements-windows-x64.lock -d ./artifacts/argos/wheelhouse
# Installation into a controlled private build runtime, never at application startup:
& $python -m pip install --no-index --find-links ./artifacts/argos/wheelhouse `
    -r ./tools/argos/requirements-windows-x64.lock
Get-ChildItem ./artifacts/argos/wheelhouse -File | Get-FileHash -Algorithm SHA256
```

El lock fija versiones; falta aprobar hashes de cada rueda para un build cerrado.
`dotnet publish` copia el worker, pero no crea ese runtime, instala modelos ni
construye el instalador. Esos pasos quedan para empaquetado.

CTranslate2 publica ruedas Windows x64, Linux x64/ARM64 y macOS x64/ARM64;
requiere Visual C++ runtime en Windows y, para binarios x86-64, al menos SSE4.1
con seleccion de instrucciones del backend. Solo se probo Windows x64. El lock
y binarios deben regenerarse y comprobarse por plataforma; no se afirma soporte
multiplataforma completo ni se instala CUDA.

## Inventario de esta tarea

Creados:

- `src/modules/Saylo.Modules.Translation/TextTranslation/ArgosTranslationOptions.cs`
- `src/modules/Saylo.Modules.Translation/TextTranslation/ArgosModels.cs`
- `src/modules/Saylo.Modules.Translation/TextTranslation/ArgosProtocol.cs`
- `src/modules/Saylo.Modules.Translation/TextTranslation/ArgosProcessSession.cs`
- `src/modules/Saylo.Modules.Translation/TextTranslation/ArgosTextTranslationEngine.cs`
- `src/modules/Saylo.Modules.Translation/TextTranslation/argos_worker.py`
- `tests/Saylo.Modules.Translation.Tests/ArgosTextTranslationTests.cs`
- `tests/Saylo.Modules.Translation.Tests/ArgosIntegrationTests.cs`
- `tests/Saylo.Modules.Translation.Tests/Python/test_argos_worker.py`
- `tools/Install-ArgosModels.py`
- `tools/Test-Argos.ps1`
- `tools/argos/requirements.txt`
- `tools/argos/requirements-windows-x64.lock`
- `docs/local-text-translation.md`
- `docs/local-translation-dependency-licenses.json`

Modificados por esta tarea: `.gitignore`, `Saylo.Modules.Translation.csproj` y
`docs/translation-pipeline.md`. Se preservaron las modificaciones locales previas,
incluidos los contratos de errores y las herramientas Whisper sin seguimiento.
No se elimina ningun archivo o proyecto ni se hace commit/push.

Proxima tarea recomendada: resolver la licencia en->es y ejecutar el piloto con
red fisicamente desconectada; despues preparar el runtime Windows privado y
evaluar calidad bilingue/glosario antes de integrar la UI.
