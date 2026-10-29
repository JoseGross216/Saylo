# Tarea 4: motor local de pronunciacion escrita

Responsable: Stanley Camacho Abreu. Fecha: 2026-10-29, America/Caracas.
Rama: `users/stanley-translation`. Base inicial: `c19a4fc`.

**Se implemento un motor real que genera IPA mediante CMUdict y conversion
grafema-fonema administrada. No se implemento TTS ni una aproximacion escrita
con letras espanolas.** La cobertura inicial es deliberadamente limitada:
una lectura no demostrada produce un error controlado de la etapa de pronunciacion.
La aprobacion linguistica humana de las guias sigue pendiente.

## 1. Motor seleccionado

`LocalPronunciationEngine` implementa `IPronunciationEngine` e `IAsyncDisposable`
dentro de `Saylo.Modules.Translation/Pronunciation`.

- Ingles: CMU Pronouncing Dictionary, revision fija
  `74790861f652b15e4ac49015a90074ad62a27690`, con 126052 entradas lexicas distintas.
  Se convierten sus telefonos ARPABET y marcas de acento a IPA.
- Espanol: G2P original en C#, con vocabulario de dominio controlado de 540
  formas, silabificacion, acento escrito/predeterminado, diptongos e hiatos.
  El archivo contiene formas lexicas, no frases ni respuestas foneticas prefabricadas.
  Palabras fuera del dominio no se transcriben arbitrariamente.

La seleccion ofrece trazabilidad de datos, ausencia de un runtime nativo nuevo,
instalacion pequena y abstencion explicita. No pretende sustituir un analizador
linguistico general ni un lexicon bilingue revisado profesionalmente.

## 2. Alternativas y justificacion

Se revisaron fuentes primarias y licencias antes de instalar recursos:

| Alternativa | Exactitud y cobertura potencial | Dependencias y distribucion | Windows / C# / otras plataformas | Decision |
| --- | --- | --- | --- | --- |
| CMUdict + G2P de dominio | Ingles estadounidense lexical; espanol acotado con acento; ambiguedades se rechazan | Datos permisivos con avisos; BCL de .NET, sin paquetes nuevos; 3.45 MiB de datos ingleses | C# administrado, sin ABI; portable en principio a .NET 8 Windows/Linux/macOS | Seleccionada; medidas reales abajo |
| eSpeak NG | Muchas lenguas/variantes y generacion local de fonemas | Motor nativo, datos y GPL-3.0-or-later; revisar obligaciones del conjunto | CLI/DLL documentadas en Windows; tambien Linux/macOS | No incorporado automaticamente al producto propietario |
| Epitran | Tablas/reglas IPA; requeriria validar acento y variante para este dominio | MIT del nucleo; Python y, para ingles, Flite lex_lookup; cadena completa no aprobada aqui | Interoperacion por proceso; trabajo adicional de empaquetado Windows | No instalado; mas dependencias que la solucion seleccionada |
| Gruut | Lexicos y modelos locales CRF/POS para contexto; en-US y es-ES documentados | MIT del nucleo; paquetes de idioma, modelos y normalizadores con revision individual pendiente | Pipeline Python; documentacion centrada en Linux; Windows y es latinoamericano requieren evaluacion | Candidato para mejorar contexto en otra tarea, sin descargar sus modelos |

Fuentes: [CMUdict README](https://github.com/cmusphinx/cmudict/blob/74790861f652b15e4ac49015a90074ad62a27690/README),
[eSpeak NG](https://github.com/espeak-ng/espeak-ng/blob/master/README.md),
[Epitran](https://github.com/dmort27/epitran/blob/master/README.md),
[Gruut](https://github.com/rhasspy/gruut/blob/master/README.md) y sus licencias
en la [evidencia de seleccion](validation/pronunciation-license-evidence.json).

GPL permite uso comercial; el motivo de no incorporar eSpeak no es que prohíba
vender software, sino las obligaciones de integracion y redistribucion que
requieren una decision separada. Ejecutarlo en otro proceso no se considera
por si solo una exencion de licencia.

No se inventaron benchmarks de alternativas no instaladas: sus tiempos, memoria
y tamano exacto de instalacion quedan sin medir. Gruut ofrece mas contexto,
pero sus modelos/datos y dependencias no quedan autorizados por la sola licencia
MIT del nucleo. La eleccion considera riesgo linguistico, empaquetado y recursos,
ademas de licencia y coste.

## 3. Arquitectura e integracion

~~~text
Texto o audio -> Whisper si corresponde -> Argos -> integridad
  -> TextTranslationCompleted
  -> LocalPronunciationEngine -> PronunciationCompleted (IPA)
  -> etapa independiente de sintesis, sin implementacion TTS nueva
~~~

No fue necesario modificar `TranslationService`: ya emite el texto con `yield`
antes de iniciar etapas opcionales, captura errores de cada una y conserva sus
resultados independientes. La implementacion se suministra por su constructor
existente; no se modifican contratos publicos ni Desktop.

~~~csharp
var catalog = PronunciationResources.CreateCatalog(localDictionaryDirectory);
await using var pronunciation = new LocalPronunciationEngine(catalog);
var service = new TranslationService(recognition, translation, pronunciation, synthesis);
~~~

`recognition`, `translation` y `synthesis` siguen siendo dependencias independientes.
Esta tarea no aporta un proveedor de sintesis. Las pruebas reales usan un doble
que comunica `ModelUnavailable` para esa etapa; no generan audio sintetizado.

Un rechazo de integridad termina el flujo antes de pronunciacion o voz y conserva
`IntegrityValidationFailed`. Si solo falla pronunciacion, la traduccion ya emitida
permanece disponible. Para voz tambien se conserva la transcripcion previa.

El motor prepara entrada y calcula fonemas en `Task.Run`, fuera del hilo del
consumidor. Un semaforo serializa operaciones por instancia. Carga cada recurso
solo al primer uso de su idioma y lo reutiliza; no conserva textos ni guias entre
solicitudes. La cancelacion se comprueba en cola, carga, lectura de lineas y
conversion de palabras. La cancelacion de una solicitud en cola no cancela otra.

El cierre cancela operaciones, espera a que salgan del area protegida y suelta
los diccionarios. Los streams se cierran al cargar; no hay procesos, dispositivos
de audio ni recursos nativos nuevos. La recoleccion de memoria administrada no
se fuerza ni se promete una reduccion inmediata del working set.

## 4. Notaciones

Se devuelve exclusivamente `PronunciationNotation.Ipa`: **IPA amplio, de
referencia lexica**, con acentos y limites de palabra. No es transcripcion
estrecha de un hablante ni prediccion de entonacion, ritmo o habla enlazada.
La puntuacion se conserva como apoyo visual, sin pronunciar nombres de signos.

`Approximate` continua disponible en el contrato para otros motores, pero esta
implementacion no lo genera. Una equivalencia inglesa-espanola improvisada
ocultaria diferencias como /ɪ/ frente a /i/, /θ/, /ð/, /æ/, /ʌ/ o /ɹ/.
No se cambio el contrato para devolver simultaneamente dos notaciones.

## 5. Idiomas, variantes y contexto

Ingles: referencia estadounidense, con roticidad y distinciones del inventario
CMUdict; no se afirma representar todos los acentos de Estados Unidos.
Se conservan marcas de acento primario/secundario y se sitúan segun un algoritmo
de ataque silabico. Las variantes de reduccion/citacion admitidas se enumeran
en codigo. No se escoge ciegamente la primera variante de cualquier palabra.

`read` se desambigua solo en contextos locales soportados: `please/to/will/can`
y otros auxiliares admitidos para /ɹid/; `have/has/had` con objeto explicito o fin
de clausula para /ɹɛd/. `I read your message` y `have read access` se rechazan.
La lista conservadora de heteronimos incluye record, refund, lead, live, wind,
wound, object y otros: su contexto no se da por resuelto mediante una busqueda
aislada. Otras entradas con variantes no aprobadas tambien se rechazan.

Espanol: referencia amplia **seseante y yeista**, con /s/ para c/z y /ʝ/ para ll/y,
vocales, vibrante simple/multiple y acento lexical. No se presenta como una
variante nacional unica. Se contemplan tilde, vocal cerrada tonica, h muda,
qu/gu, dieresis, diptongos y sufijo -mente. Mexico tiene excepcion lexica explicita.
No se copio una tabla fonetica externa ni se aplica G2P a cualquier nombre.
Referencias normativas: [acentuacion](https://www.rae.es/dpd/tilde),
[diptongos](https://www.rae.es/dpd/diptongo) e [hiatos](https://www.rae.es/dpd/hiato).
Estas reglas ortograficas no eliminan la variacion fonetica regional.

## 6. Instalacion y recursos

Desde la raiz, con PowerShell 7 y .NET 8:

~~~powershell
# Explicit provisioning; never runs during GenerateAsync or application startup.
./tools/Install-PronunciationResources.ps1
~~~

Descarga unicamente el diccionario de texto y LICENSE desde la revision fijada
de CMUdict. Verifica tamano y SHA-256 antes de escribir; no reemplaza archivos
existentes de contenido diferente. La ubicacion predeterminada es
`artifacts/pronunciation/cmudict`, ignorada por Git. El motor no invoca este script.

En un equipo desconectado, copiar los dos archivos ya obtenidos y ejecutar:

~~~powershell
./tools/Install-PronunciationResources.ps1 -SourceDirectory C:/recursos/cmudict -DestinationDirectory C:/Saylo/resources/cmudict
~~~

No se descargan modelos durante la generacion. El espanol puede funcionar sin
el diccionario ingles. Una ruta inglesa ausente se informa al pedir ingles;
no impide seguir usando espanol. No se aceptan rutas UNC de recursos.

`PronunciationResources.CreateCatalog` registra datos y aviso como recurso
`ModelPurpose.Pronunciation` en el catalogo local existente. Comprueba presencia,
metadatos y hashes. La carga verifica ademas el hash del mismo handle abierto
del diccionario que va a analizar, evitando confiar solo en un archivo validado
y cerrado anteriormente. No se modifico ModelManagement.

## 7. Dependencias y tamano

No hay NuGet, paquetes Python, bibliotecas nativas, bases de datos ni modelos
neuronales nuevos. El motor usa la BCL de .NET 8 y los contratos/catalogo existentes.

| Recurso | Bytes | Distribucion |
| --- | --- | --- |
| cmudict.dict | 3618488 | Externo, ignorado en Git |
| LICENSE upstream | 1754 | Junto al diccionario, obligatorio |
| spanish-vocabulary.txt | 4030 | Texto original, recurso embebido |

El ingles suma aproximadamente 3.45 MiB en disco, sin contar el runtime .NET
ya requerido por Saylo. El tamano final de un instalador completo no se midio.
El codigo es portable a .NET 8 en Windows/Linux/macOS; **solo Windows x64 se
ejecuto aqui**, no se declara validacion de los otros sistemas.

## 8. Licencias

Los [terminos CMUdict fijados](https://github.com/cmusphinx/cmudict/blob/74790861f652b15e4ac49015a90074ad62a27690/LICENSE)
permiten redistribuir fuente/binarios, con o sin cambios, conservando sus avisos.
El README autoriza expresamente uso comercial. Se conserva la licencia completa,
incluidos agradecimientos y descargo, en [docs/licenses](licenses/cmudict-LICENSE.txt)
y se exige el LICENSE original junto al recurso instalado. No se incorporan
GPL/AGPL nuevos por esta implementacion.

Hashes y revision estan en codigo y en la
[evidencia de licencias](validation/pronunciation-license-evidence.json).
El vocabulario espanol y las reglas son trabajo original para Saylo; las paginas
normativas se consultaron como referencia, no se redistribuyeron como datos.
Esta revision no sustituye el trabajo pendiente de licencias/instalador del resto
del producto ni convierte otras dependencias previas en recursos aprobados.

## 9. Pruebas realizadas

Se agregaron **76 casos .NET**: 68 en `LocalPronunciationTests`, 5 en
`PronunciationServiceTests` y 3 integraciones reales. Cubren ambos idiomas,
acentuacion, silabas, contracciones, heteronimos y contexto, Unicode, entrada
vacia/excesiva, idioma no soportado, recursos ausentes/corruptos, licencia ausente,
salidas/fonemas invalidos, checksum del archivo abierto, cancelacion activa/en
cola, recuperacion, concurrencia, reutilizacion, streams cerrados y cierre idempotente.

Las pruebas del servicio comprueban entrega progresiva, aislamiento de fallo en
texto/voz y ausencia de pronunciacion/sintesis tras rechazo de integridad.
Las integraciones usan el diccionario completo y el G2P real; otra prueba recorre
Whisper -> Argos -> integridad -> texto -> pronunciacion con recursos existentes.
Los pequenos diccionarios de pruebas unitarias verifican conversion/coordinacion;
no se presentan como medidas de cobertura del corpus real.

Verificacion final: **329 pruebas .NET aprobadas, 0 fallidas, 0 omitidas**,
frente a 253 anteriores. **25 pruebas Python anteriores aprobadas**, sin cambios
en Python. Restore correcto, build con cero errores/advertencias y
`git diff --check` sin errores. Los hashes TRX estan en
[test-summary.json](validation/pronunciation-results/2026-10-29/test-summary.json).

La primera suite completa fallo al guardar un informe antiguo de Argos en una
carpeta de artifacts que faltaba. Se creo la carpeta y se repitio toda la solucion;
no se cambiaron las aserciones. El primer TRX/evidencia quedan en artifacts;
los resultados enlazados corresponden a la repeticion final.

Reproduccion de pronunciacion, usando el diccionario instalado:

~~~powershell
dotnet restore
dotnet build Saylo.slnx
./tools/Test-Pronunciation.ps1 -DictionaryDirectory ./artifacts/pronunciation/cmudict -ReportDirectory ./artifacts/pronunciation/replay -NoBuild
~~~

`-IncludePipeline` agrega la prueba real con Argos/Whisper y exige sus variables.
El runner restaura las variables que cambia. Para toda la solucion, configurar
tambien los recursos anteriores sin sobrescribir informes historicos:

~~~powershell
$env:SAYLO_CMUDICT_DIR = (Resolve-Path ./artifacts/pronunciation/cmudict).Path
$env:SAYLO_ARGOS_PYTHON = (Resolve-Path ./artifacts/argos/runtime/Scripts/python.exe).Path
$env:SAYLO_ARGOS_MODELS = (Resolve-Path ./artifacts/argos/models).Path
$env:SAYLO_WHISPER_MODEL_DIR = (Resolve-Path ./artifacts/whisper/models).Path
$env:SAYLO_WHISPER_ES_PCM = (Resolve-Path ./artifacts/whisper/audio/es.pcm).Path
$env:SAYLO_WHISPER_EN_PCM = (Resolve-Path ./artifacts/whisper/audio/en.pcm).Path
$env:SAYLO_WHISPER_ES_EXPECTED = 'necesito ayuda pedido'
$env:SAYLO_WHISPER_EN_EXPECTED = 'need help order'
$env:SAYLO_PRONUNCIATION_REPORT_DIR = [IO.Path]::GetFullPath('./artifacts/pronunciation/full/current')
$env:SAYLO_REJECTION_REPORT_DIR = [IO.Path]::GetFullPath('./artifacts/pronunciation/full/rejections')
$env:SAYLO_INTEGRITY_REPORT_DIR = [IO.Path]::GetFullPath('./artifacts/pronunciation/full/integrity')
$env:SAYLO_VALIDATION_REPORT_DIR = [IO.Path]::GetFullPath('./artifacts/pronunciation/full/quality')
dotnet test Saylo.slnx --no-build --no-restore --logger trx --results-directory ./artifacts/pronunciation/full/trx
& $env:SAYLO_ARGOS_PYTHON -B -m unittest discover -s ./tests/Saylo.Modules.Translation.Tests/Python -p 'test_*.py' -v
git diff --check
git status --short
~~~

Si se configura `SAYLO_ARGOS_REPORT`, crear previamente su directorio padre.
Sin variables de recursos se omiten integraciones: no equivaldria a esta validacion.

## 10. Resultados reales y casos linguisticos

El [corpus nuevo](validation/pronunciation-cases.json) contiene **46 muestras**:
22 inglesas y 24 espanolas. Resultado: **26 guias generadas y 20 abstenciones
controladas**. El conjunto incluye casos deliberadamente no admitidos; esa
proporcion no mide exactitud fonetica ni cobertura de todo el lenguaje empresarial.

Ejemplos de salida real, sin aprobacion humana implicita:

| Texto | IPA generado |
| --- | --- |
| Thank you. | ˈθæŋk ˈju. |
| Please confirm your delivery address. | ˈpliz kənˈfɝm ˈjɔɹ dɪˈlɪvɚi əˈdɹɛs. |
| I have read your message. | ˈaɪ ˈhæv ˈɹɛd ˈjɔɹ ˈmɛsədʒ. |
| Please read the message. | ˈpliz ˈɹid ðə ˈmɛsədʒ. |
| Hola. ¿Cómo puedo ayudarle hoy? | ˈola. ¿ˈkomo ˈpwedo aʝuˈdaɾle oi̯? |
| Por favor confirme su dirección de entrega. | poɾ faˈboɾ konˈfiɾme su diɾekˈsjon de enˈtɾeɡa. |
| término / termino / terminó | ˈteɾmino / teɾˈmino / teɾmiˈno |

[Salidas y muestras completas](validation/pronunciation-results/2026-10-29/quality-and-performance.json).
Se normalizan Unicode y apostrofo tipografico; no se altera el texto traducido
original que emitio el servicio. Contracciones conocidas usan el diccionario.
John tiene lectura de referencia inglesa; Jencer y nombres no cubiertos se
rechazan. Una lectura convencional no prueba la preferencia de su propietario.

Siglas, abreviaturas y literales tecnicos no reciben expansiones inventadas:
NASA, Dr./Sr., emails, URLs, telefonos, tracking y codigos causan abstencion del
mensaje completo. Tampoco se adivinan fechas, monedas o porcentajes.
Solo se verbalizan cardinales **aislados de 0 a 99**, sin ceros iniciales,
bajo una politica explicita de numero cardinal. `3 units`, fechas incluso ISO,
49.95 USD y 0048 necesitan una futura politica de lectura y se rechazan.
Numeros ya escritos con palabras se procesan como vocabulario ordinario.

Los errores usan `TranslationEngineException` de etapa Pronunciation:
`ModelUnavailable`, `ModelInvalid`, `UnsupportedLanguage` o
`InvalidEngineOutput` para una guia no verificable. El diagnostico interno
`PronunciationReason` es una categoria sin texto privado. No se amplio el enum
publico; la interfaz puede mostrar simplemente que la guia no esta disponible.

## 11. Rendimiento medido

Windows 11 x64, i5-11400H, 6 nucleos/12 hilos, .NET 8.0.33; misma maquina de las
tareas previas. Sin proceso Python ni inicializacion de sintetizador para IPA.

Constructor: **0.116 ms**. Primera generacion inglesa incluyendo catalogo,
hashes y carga: **270.254 ms**; carga medida: 269.556 ms. Primera muestra
espanola: **0.217 ms**, carga del vocabulario 0.098 ms. Una carga inglesa exitosa
para todo el corpus, benchmarks y solicitudes concurrentes.

| Generaciones posteriores | n | Entrada caracteres | Mediana ms | p95 ms |
| --- | --- | --- | --- | --- |
| Ingles corto | 40 | 42 | 0.085 | 0.149 |
| Espanol corto | 40 | 35 | 0.083 | 0.150 |
| Ingles largo | 15 | 1452 | 0.919 | 4.379 |
| Espanol largo | 15 | 1068 | 0.655 | 0.891 |

Los p95 usan rango mas cercano: con 15 muestras el p95 coincide con el maximo.
Se guardan todas las muestras, sin excluir valores lentos. Una PC y esta carga
no constituyen SLA ni comparacion de exactitud frente a otros motores.

24 solicitudes concurrentes, dos idiomas y una instancia: **0.826 ms** de lote,
aisladas y serializadas internamente. Cancelacion durante carga real: **4.830 ms**
desde cancelar hasta terminar; recuperacion con el mismo motor y carga nueva:
226.450 ms. Solicitud precancelada: 0.267 ms. No hay resultado parcial.
[Evidencia de cancelacion](validation/pronunciation-results/2026-10-29/cancellation.json).

Memoria administrada muestreada: aproximadamente 5.7 -> 36.5 MiB; working set
del anfitrion: 88.6 -> 124.1 MiB. Son snapshots del proceso de pruebas, con runtime,
asignaciones temporales y pruebas previas, **no memoria pico aislada del motor**.
Al cerrar se verifico que el motor deja de conservar ambos diccionarios.

En el [pipeline real](validation/pronunciation-results/2026-10-29/real-pipeline.json),
el texto de la primera solicitud llego a 2244.700 ms y la guia a 2470.120 ms;
la carga fonetica no retuvo el texto. En voz: transcripcion a 1931.947 ms,
traduccion a 2024.237 ms y pronunciacion a 2024.675 ms. Incluyen arranque de
otros motores; no son tiempos puros de conversion fonetica.

## 12. Limitaciones foneticas

No hay analisis general POS, desambiguacion de nombres/idiomas mezclados,
correferencia ni entonacion de oraciones. La fonetica depende de contexto,
registro y hablante; las reglas locales de read no resuelven cualquier frase.
Muchos heteronimos y variantes correctas se rechazan por conservadurismo.
La cobertura espanola es un dominio pequeno, no un diccionario completo.
El prestamo `email` se rechaza expresamente en espanol hasta contar con una
lectura lexica aprobada: pertenecer al vocabulario no autoriza aplicar reglas
nativas a cualquier extranjerismo. Se agrego una prueba para esta abstencion.

No se modelan de manera general reducciones entre palabras, aspiracion regional,
asimilaciones, alofonos aproximantes de b/d/g ni flapping estadounidense.
CMUdict contiene variantes y decisiones propias de un lexicon de tecnologia
del habla, no un patron humano infalible de IPA: por ejemplo, thirty presenta
una realizacion D y acento secundario que la conversion conserva. Esa salida
numerica requiere revision y no se declara foneticamente aprobada.
Las marcas de acento son lexicas, no instrucciones de dar prominencia a todas
las palabras de una oracion. No se presenta como habla natural sintetizada.

Si no se cubre una palabra, simbolo o lectura, falla todo el resultado de esa
etapa. No se mezclan fragmentos sin traducir con IPA ni se etiqueta una guia
parcial como completa. El limite es 8000 caracteres de entrada, 80 por palabra,
32000 de salida y un diccionario de como maximo 8 MB/160000 lineas.

## 13. Riesgos pendientes y proxima tarea

Realizar revision fonetica humana bilingue de las salidas y aprobar vocabulario,
variantes y casos de nombre propio. Medir cobertura con mensajes autorizados
independientes, no solamente el corpus inicial. Disenar normalizacion explicita
de cantidades/fechas/identificadores y ampliar contexto sin inventar lecturas.
Antes de una aproximacion legible para empleados, validar pedagogicamente cada
sonido y sus limites. No aprobar automaticamente una salida por ser Unicode IPA.

Probar Linux/macOS y una ejecucion con red desconectada a nivel del sistema;
esta tarea verifica un camino administrado local sin cliente HTTP en el motor,
sin procesos nuevos, UNC rechazadas y sin descarga automatica. El instalador
manual es el unico componente nuevo con descarga explicita. No se cambio la
configuracion de red del equipo ni se afirma aislamiento de red del sistema.

Siguiente paso recomendado: validacion humana y ampliacion controlada de
pronunciacion; despues, una tarea separada de seleccion/licencias/integracion
de TTS. Mantener audio y guia escrita independientes.

## 14. Recomendaciones para la interfaz de Jencer y archivos

- Mostrar primero el texto traducido y despues la guia cuando llegue el evento;
  usar estados separados para guia y audio, sin bloquear lectura/copia del texto.
- Etiquetar `Ipa` como guia IPA de referencia; explicar ˈ/ˌ y ofrecer ayuda
  fonetica. No presentarla como aproximacion espanola ni como audio.
- Ante un error opcional, conservar traduccion/transcripcion y mostrar guia no
  disponible. No sustituirla por el texto original ni por una invencion automatica.
- Usar una fuente con IPA y marcas combinantes comprobadas en Windows; cualquier
  fuente redistribuida requiere su propia licencia. No se agregaron fuentes aqui.
- Reutilizar una instancia del motor y liberarla al cerrar su ambito. No guardar
  conversaciones o guias de manera persistente por esta integracion.

Archivos nuevos: carpeta `Pronunciation` (seis clases y vocabulario), tres clases
de pruebas, dos scripts PowerShell, corpus, evidencia, licencia y este documento.
El unico archivo de la base inicial modificado es el csproj de Translation para
embeber el vocabulario. No se modificaron pruebas anteriores, TranslationService,
contratos, integridad, modelos de traduccion, ModelManagement, Desktop,
Conversations, Identity ni Api. No se elimino ningun archivo.

Se verifico la preservacion por hashes en el
[inventario](validation/pronunciation-results/2026-10-29/changed-files.json).
El repositorio inicio limpio en c19a4fc; durante el trabajo aparecio externamente
`2031ccc` con parte de la implementacion. Se conservo ese commit y se continuaron
las validaciones pendientes. **El agente no ejecuto commit, push ni cambio de rama.**
