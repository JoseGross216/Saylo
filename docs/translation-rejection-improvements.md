# Tarea 3.3: optimizar traducciones y reducir rechazos

Responsable: Stanley Camacho Abreu. Fecha local: 2026-10-29, America/Caracas.
Rama conservada: `users/stanley-translation`. Base: `586187145e69e2d8c4d11574178ddae057f23374`.

## 1. Resumen tecnico

Con los mismos modelos locales, **cuatro de los doce rechazos se recuperaron**
seleccionando traducciones completas alternativas del modelo. Con la politica
anterior: **72 entregadas por las reglas, 8 bloqueadas**, frente a 68/12.
El nuevo perfil optativo de atencion al cliente entrega **71 y bloquea 9**:
recupera los mismos cuatro y bloquea adicionalmente una traduccion terminologica
incorrecta de seguimiento. No se desactivaron controles para alcanzar una cuota.

**Superar las reglas no certifica fidelidad semantica ni aceptacion empresarial.**
La revision bilingue humana sigue pendiente. Las observaciones de este documento
son una revision tecnica provisional, no una aprobacion de un traductor humano.

Se mantienen .NET 8, monolito modular, CTranslate2 CPU/int8, Argos, worker
persistente y serializacion de solicitudes. No hay paquetes, modelos nuevos,
servicios externos, base de datos, pronunciacion ni sintesis. Los cambios de
produccion se limitan a `Saylo.Modules.Translation/TextTranslation`.
Desktop, Api, Conversations, Identity, ModelManagement y los contratos no cambian.
No se modificaron pruebas anteriores ni trabajo de Jencer.

La entrada original de 80 frases y toda la evidencia de las tareas 3.1/3.2
se conservaron byte a byte. Tambien se conserva el cambio previo preparado en
`docs/local-text-translation.md`, sin alterar su contenido ni el indice Git.
No se hizo commit, push ni cambio de rama.

## 2. Clasificacion individual de los doce rechazos

La [auditoria completa](validation/rejection-results/2026-10-29/rejection-audit.json)
contiene, para **cada caso**, texto original, idiomas, salida neuronal historica,
salidas candidatas actuales disponibles, regla, causa, decision, posible solucion,
riesgo y salida final de ambos perfiles. La salida historica procede del corpus
3.1 congelado; no se atribuye falsamente a una inferencia del pipeline actual.
Las dos fechas ambiguas se rechazan antes de llamar al worker: no hay salida
neuronal actual en esos dos casos.

| Caso | Regla anterior | Diagnostico y decision | Solucion y riesgo conservado |
| --- | --- | --- | --- |
| es-04 | Referent | Correcto: `la direccion` se tradujo con `his delivery address`, inventando propietario. | Candidato neutral `the delivery address`; sigue prohibido inferir propietario. |
| es-10 | Referent | Correcto: `su contrasena` paso a `his password`; no se demuestra un propietario masculino. | Sigue bloqueado: las cuatro alternativas usan `his`. Permitirlo puede cambiar la persona responsable. |
| es-12 | NumericAmbiguity | Restriccion conservadora correcta: `03/04/2026` es ambiguo; preguntar por el 3 de abril no confirma la convencion. | Fecha explicita o futura configuracion regional validada; no adivinar el dia. |
| es-17 | Referent | Correcto: `su caso` paso a `his case`; Maria es quien revisa y no prueba la propiedad del caso. | Sigue bloqueado, tambien alternativas con `her`; evitar cambiar el propietario. |
| es-28 | TemporalRelation | Correcto: `by Friday` puede incluir el viernes; no conserva `antes del viernes`. | Candidato `before Friday`, con negacion y condicion; no ampliar el plazo. |
| es-34 | Referent | **Posible falso positivo conservador**: `her ID` puede referirse razonablemente a Ana Perez. La regla no demuestra esa correferencia. | Se conserva el bloqueo; no aprobar enlaces de `su` sin contexto. Coste: menor disponibilidad de una frase posiblemente correcta. |
| es-38 | Glossary | Correcto: `scale up the incidence` no transmite escalar una incidencia al equipo tecnico. | Sigue bloqueado; las alternativas no recuperan `escalate`. No comunicar otra accion. |
| es-40 | NumericEquivalence | Correcto para la primera salida: `$80` no prueba que sean USD. Faltaba reconocer la notacion explicita `US $80`. | Equivalencia verificable USD/US$, sin convertir moneda; candidato completo con `US $80`. `$80`, US $90 y otras monedas siguen fallando. |
| en-02 | Glossary | Correcto: `contactar con el cliente` pierde `customer support` y cambia el destinatario. | Sigue bloqueado; no hay soporte/atencion al cliente en las alternativas. |
| en-04 | FormalRegister | Rechazo correcto **de politica**: `tu direccion` conserva el sentido basico, pero incumple el perfil formal. | Candidato nativo `su direccion`; no sustitucion global de pronombres. |
| en-12 | NumericAmbiguity | Restriccion conservadora correcta: `04/03/2026` y una pregunta por April 3 no establecen una convencion de fecha. | Sigue bloqueado preflight; no reinterpretar por idioma. |
| en-37 | Glossary | Correcto: `dano al agua` no significa dano causado por agua en una exclusion de garantia. | Sigue bloqueado; no aprobar un cambio en la cobertura. |

Por regla: 4 referentes, 2 fechas ambiguas, 3 glosarios, 1 relacion temporal,
1 equivalencia monetaria y 1 registro formal. No hay doce fallos del validador:
hay errores neuronales, restricciones de politica y una correferencia posiblemente
valida que el sistema no puede demostrar. No se declara ningun falso positivo
demostrado; `es-34` permanece como candidato para revision humana/contextual.
La entrada real de es-04 dice **la** direccion, no su direccion.

## 3. Causas encontradas

El adaptador consumia solo la primera hipotesis aunque el beam nativo ya era de
cuatro. Algunas alternativas eliminan el defecto sin tocar los validadores.
Otras repiten el mismo error y deben continuar bloqueadas. No se inyectan las
referencias esperadas del corpus ni traducciones escritas a mano en produccion.

`ProtectedText` no causo los doce rechazos originales: ninguno era
`MarkerCorrespondence`. Su mapa por ocurrencia, limites, unicidad y orden siguen
siendo necesarios. En las nuevas sondas largas hubo **dos rechazos por marcador**:
las alternativas no permitieron demostrar restauracion. Se conservaron esos
rechazos; no se recuperan nonces por proximidad ni se adivina el identificador.

La segmentacion mantiene los limites, pero pierde contexto entre oraciones.
Repetir inferencia sobre 27 segmentos identicos tambien repite el coste y expone
mas veces la traduccion a la variacion de los nonces. Reutilizar una traduccion
ya validada del mismo segmento reduce ese trabajo, sin resolver la correferencia.

La exploracion encontro otra alteracion de responsabilidad en un pasaje largo:
`Le informaremos ...` produjo `... after I'm through`. Se agrego un bloqueo
conservador para compromisos explicitos en primera persona plural, en lugar de
contar ese pasaje como una traduccion mejorada.

## 4. Correcciones aplicadas

- **Alternativas nativas acotadas:** se conserva la primera peticion. Solo tras
  un error de integridad se solicita una lista ordenada de hasta cuatro
  hipotesis del mismo texto protegido. Cada candidato completo pasa restauracion,
  numeros, negacion, referente, relacion temporal, glosario y registro.
  Se devuelve el primero que pasa; si ninguno pasa, se propaga el rechazo original.
- **Protocolo compatible:** capacidad `ranked_candidates_v1` negociada al iniciar;
  workers antiguos sin capacidad no reciben nuevas peticiones. Se comprueban ID,
  orden, tipos, texto no vacio, duplicados y limites. No se concatenan candidatos.
  Beam=4, cuatro hilos y modelos no cambian. La API nativa documenta
  [num_hypotheses y beam_size](https://opennmt.net/CTranslate2/python/ctranslate2.Translator.html).
- **Sin recortes:** cada candidato se limita a 4096 caracteres, respuesta JSON
  de alternativas a 65536 y decodificacion nativa a menos de 512 tokens completos.
  Hipotesis truncadas no se entregan. Una solicitud de alternativas no admite
  varios parrafos; el adaptador ya proporciona segmentos preparados.
- **Equivalencia monetaria explicita:** `US$`/`US $` se canonizan como USD,
  conservando valor y signo. `$` y dollars sin pais permanecen indeterminados;
  no hay conversion de moneda. Fechas, separadores, horas, unidades, porcentajes
  y orden numerico conservan las comprobaciones de 3.2.
- **Reutilizacion dentro de una solicitud:** diccionario local con igualdad
  ordinal del texto fuente exacto, configuracion inmutable y misma direccion.
  Guarda solo salidas restauradas y validadas; conserva cada ocurrencia y
  separador. No comparte mensajes entre solicitudes ni persiste conversaciones.
  Un fallo posterior impide completar todo el mensaje.
- **Responsabilidad plural:** cinco verbos futuros explicitos, `informaremos`,
  `confirmaremos`, `enviaremos`, `revisaremos`, `contactaremos`, requieren `we`
  y ausencia de `I/me/my/mine` en ingles. Se excluyen literales. Es una regla
  finita conservadora, no un analizador sintactico: puede rechazar expresiones
  impersonales o frases mixtas correctas. No demuestra todas las responsabilidades.

Se mantienen el semaforo, IDs nuevos por peticion, plazo de traduccion existente
para la operacion, cancelacion y cierre/reinicio ante fallos de comunicacion.
Un rechazo de integridad correlacionado conserva la sesion sana. No se agrega
un proceso por candidato ni se publican segmentos parciales.

### Perfil inicial de atencion al cliente

`CustomerServiceTranslationProfile.Create(protectedTerms)` ofrece un perfil
**optativo** `customer-service-v2`, formal y con 29 reglas bidireccionales para
saludos, pedidos, seguimiento, facturas, pagos, reembolsos, solicitudes, soporte,
confirmaciones, reclamaciones, despedidas y documentos. Conserva las tres reglas
anteriores para customer support, water damage y escalar.

Ejemplo para un futuro punto de composicion, sin modificar Desktop:

~~~csharp
var integrity = CustomerServiceTranslationProfile.Create(protectedTerms: ["Saylo Pro"]);
var options = new ArgosTranslationOptions(pythonPath, modelDirectory, integrity: integrity);
~~~

Las reglas **validan alternativas**, no reemplazan palabras. La configuracion
predeterminada del constructor sigue sin activar este perfil. Su vocabulario
debe aprobarse por dominio: encontrar un termino no demuestra alcance, sujeto,
promesa ni relacion con una cantidad. Los terminos invariables van en
`ProtectedTerms`, no en reemplazos linguísticos.

## 5. Rechazos recuperados

Salidas finales identicas en ambos perfiles:

| Caso | Salida completa seleccionada |
| --- | --- |
| es-04 | I need to confirm the delivery address. |
| es-28 | If the package doesn't get here before Friday, get back to us. |
| es-40 | The client said, "I did not authorize that US $80 charge." |
| en-04 | Necesito verificar su dirección de entrega. |

La revision tecnica no detecta en esas cuatro salidas el defecto que causaba el
bloqueo: propietario inventado, limite temporal, moneda indeterminada y tuteo,
respectivamente. En es-28, `get back to us` puede resultar menos formal que otras
formulaciones. Las cuatro requieren aun aprobacion bilingue empresarial.

## 6. Rechazos correctamente mantenidos

Permanecen sin salida utilizable es-10, es-12, es-17, es-34, es-38, en-02,
en-12 y en-37. Conservar es-34 es una decision conservadora pendiente de
resolver contexto; no se presenta como error neuronal demostrado.

En v2 se suma es-33: el identificador `ZX987654321ES` no se altera, pero
`monitoring number` no cumple la terminologia de seguimiento. Las cuatro
alternativas no aportan el termino aprobado. Es una perdida de disponibilidad
por un control nuevo, **no una traduccion corregida**.

## 7. Resultado del corpus y revision de regresiones

| Ejecucion | Entradas | Entregadas por reglas | Rechazadas | Recuperadas vs 3.2 | Nuevos rechazos |
| --- | --- | --- | --- | --- | --- |
| Tarea 3.2, perfil v1 | 80 | 68 | 12 | referencia | referencia |
| Tarea 3.3, mismo perfil v1 | 80 | 72 | 8 | 4 | 0 |
| Tarea 3.3, perfil empresarial v2 | 80 | 71 | 9 | 4 | 1: es-33 |

El corpus conserva SHA-256
`9a7117eb1ac9ca9b3e472e860fd967c35f5c50fa4c8a04910d370f623f35e35e`.
Las comparaciones validan los 80 IDs, direcciones, entradas, salida anterior y
hash de evidencia congelada; rechazan duplicados o historia divergente.

Evidencia final: [comparacion v1](validation/rejection-results/2026-10-29/previous-profile-v1-comparison.json),
[comparacion v2](validation/rejection-results/2026-10-29/customer-service-v2-comparison.json),
corpus completos en el mismo directorio y
[notas de revision](validation/rejection-results/2026-10-29/review-notes.json).
**Cero alteraciones criticas aceptadas** en los dos casos historicos de correo y
seguimiento. En v2 el seguimiento se bloquea por terminologia, sin salida parcial.
Los avisos literales de horas equivalentes, como es-13, no se cuentan como
corrupcion de identificadores ni como prueba de error semantico.

Regresiones y limitaciones observadas, incluidas salidas que pasan las reglas:

- v1 conserva `Please send them` en es-18; v2 exige `documents` y recupera una
  alternativa completa con el correo exacto. No se altero el corpus para ello.
- es-19 puede decir `Reboot server SQL`, poco natural. es-20 conserva un calco
  para autenticacion de dos factores. Preservar SQL/VPN no certifica estilo.
- en-18 puede variar entre `envie`, `envien` y `al support@example.com` por
  nonces. Pluralidad del destinatario y naturalidad no estan completamente cubiertas.
- La sonda URL devuelve `Abierto` por el imperativo `Open`: URLs exactas,
  traduccion de la instruccion insuficiente. No se aprueba su calidad por preservar enlaces.
- Los pasajes largos ingleses con politica predeterminada mezclan `Su` y `Te`.
  El perfil formal recupera `Le enviaremos` en la sonda empresarial correspondiente.
- La nueva comprobacion plural impide entregar la alteracion `I'm through`;
  no existe todavia alternativa segura para ese pasaje espanol. No se declara
  solucionada la omision, correferencia o semantica discursiva general.

Los ocho problemas graves historicos **no se declaran resueltos en bloque**:
es-04, es-28 y es-40 tienen nueva salida para revision; los otros cinco siguen
bloqueados. No hay una
nueva puntuacion humana del corpus. Ningun bloqueo cuenta como traduccion correcta.

Los archivos nuevos de `rejection-results/2026-10-28` son exploratorios de esta
tarea, anteriores al control de responsabilidad plural. Se conservan para
trazabilidad y se etiquetan como tales en las notas; **la evidencia final es la
del 29**. No sustituyen resultados historicos en `results` ni `integrity-results`.

## 8. Rendimiento y mensajes largos

Equipo y recursos existentes: Windows 11 x64, i5-11400H, 6 nucleos/12 hilos,
15.79 GiB RAM, .NET 8.0.33, Python 3.12.14, CTranslate2 4.8.4 CPU/int8,
Argos es-en 1.9/en-es 1.0, cuatro hilos. Una ejecucion por perfil, mismo equipo;
sin benchmark controlado de carga ni SLA.

| Medida | Tarea 3.2 v1 | Tarea 3.3 v1 | Tarea 3.3 v2 |
| --- | --- | --- | --- |
| Arranque con catalogo/validacion, ms | 2361.999 | 3472.555 | 3350.667 |
| Primera traduccion con arranque, ms | 2464.415 | 3732.031 | 3540.681 |
| es->en calientes aceptadas: n | 31 | 34 | 33 |
| es->en mediana / p95, ms | 83.628 / 139.954 | 114.737 / 257.456 | 113.044 / 268.731 |
| en->es calientes aceptadas: n | 36 | 37 | 37 |
| en->es mediana / p95, ms | 54.602 / 81.102 | 90.267 / 128.829 | 78.934 / 120.508 |
| Memoria worker muestreada, MiB | 257.9-261.1 | 258.0-261.9 | 257.5-261.8 |
| Validacion de candidato: mediana, ms | evidencia anterior disponible | 0.885 | 0.872 |
| Validacion total de 80 intentos, ms | evidencia anterior disponible | 104.049 | 106.872 |
| Reintentos de alternativas / recuperaciones | 0 / 0 | 10 / 4 | 11 / 4 |

**Hay regresion observada en latencia corta y arranque.** Las alternativas anaden
inferencia cuando falla la primera salida; los percentiles reflejan ese coste.
El arranque ocurre antes de pedir alternativas: no se atribuye causalmente su
incremento a N-best sin una comparacion controlada. Las muestras aceptadas
tambien cambian. Se conservan tiempos de todos los intentos en los corpus;
rechazos preflight rapidos no se usan para mejorar la mediana de entregas.

[Comparacion y muestras](validation/rejection-results/2026-10-29/performance-comparison.json)
incluye los tiempos de validacion historicos y nuevos, medianas, p95 de rango
mas cercano, memoria y contadores. La memoria es working set tras entregas,
no pico continuo ni memoria total de .NET/Desktop/Whisper.

Como referencia de 3.1, antes de integridad: primera traduccion 2457.223 ms;
es->en n=39, mediana/p95 92.704/136.380 ms; en->es n=40,
63.370/106.194 ms. Esas entregas incluian errores criticos y graves; no
representan calidad equivalente a las entregas filtradas actuales.

En el pasaje repetido ingles de 918 caracteres y 27 oraciones, cinco intentos
entregaron nueve ocurrencias exactas del pedido: **mediana 191.148 ms y p95
218.892 ms**, frente a 1067.602 ms de 3.2, cuya muestra mezclaba 2 entregas y
1 rechazo. Cada nueva solicitud infiere tres segmentos distintos y reutiliza
24, sin cache entre solicitudes; validacion total mediana por candidato
0.756 ms. Es una mejora medida en ese patron, no en cualquier texto largo.
La referencia IPC sin integridad de 3.2, 513.471 ms, era insegura y no representa
calidad equivalente.

El pasaje espanol tuvo **0 entregas y 5 rechazos controlados**: 3 por referente
y 2 por marcador, mediana de intento 502.706 ms. Ese tiempo es de rechazo,
no de traduccion completa; no se usa para afirmar una mejora de rendimiento.
Las tres sondas con oraciones distintas, anafora, importe, unidades, modelos,
correo y codigo completaron bajo las reglas; su revision humana sigue pendiente.
[Mensajes largos](validation/rejection-results/2026-10-29/long-messages.json).

No se modifico `SafeTextSegmenter`: mantiene 1000 unidades UTF-16 por solicitud,
240 por segmento, maximo 64 segmentos, separadores y presupuesto agregado de
8000. No corta identificadores ni valores; una oracion indivisible excesiva
continua fallando. Se comprobo fallo tardio sin publicar resultados anteriores.

## 9. Pruebas nuevas, validacion y reproduccion

**253 pruebas .NET aprobadas, 0 fallidas, 0 omitidas**: 209 anteriores y 44
nuevas, 41 unitarias y 3 integraciones con modelos reales. **25 pruebas Python
aprobadas**, 12 anteriores y 13 nuevas. Restore correcto; build de la solucion
con cero advertencias y errores; `pip check` correcto; `git diff --check` sin
errores. Se ejecutaron Argos y Whisper reales, sin omitirlos por falta de variables.
[Resumen y hashes TRX](validation/rejection-results/2026-10-29/test-summary.json).

Cobertura nueva: candidatos completos y limites del protocolo, moneda explicita
y signos incorrectos, fechas ambiguas antes de inferir, marcador ausente/duplicado,
restauracion alternativa exacta, referente incierto, compromiso plural, glosario
y registro, worker anterior sin capacidad, reutilizacion exclusivamente local,
identificadores distintos/repetidos, fallo tardio sin parcial, 12 solicitudes
concurrentes con mapas independientes, cancelacion antes de reutilizar y durante
alternativas, plazo total y propagacion de fallo en texto/voz de TranslationService.
No se inician pronunciacion ni sintesis despues del rechazo de traduccion.

Las sondas reales cubren correos repetidos, dos URLs, tracking, codigo de pedido,
telefono internacional y referencia alfanumerica. Se comprobaron cantidad exacta
y contenido de cada literal entregado; una restauracion no demostrable sigue
siendo error. [Sondas](validation/rejection-results/2026-10-29/identifier-and-business-probes.json).
Las pruebas previas conservan decimales, miles, porcentajes, horas, cantidades,
unidades, fechas y marcadores alterados/reordenados.

Las pruebas reales de estabilidad pasaron con un solo worker para concurrencia,
cancelacion en cola sin interrumpir la solicitud activa, cancelacion activa,
cierre en inferencia, timeout, fallo/reinicio y reutilizacion tras rechazo.
Los JSON `stability-*` y TRX registran la ejecucion actual. El timeout configurado
de 1 ms tarda 3339.794 ms incluyendo arranque y recogida del proceso: no se
promete cierre fisico en 1 ms. La recuperacion tras fallo tardo 3151.000 ms.
La inspeccion final no encontro workers Argos huerfanos.

Reproduccion desde la raiz, usando exclusivamente recursos ya instalados:

~~~powershell
$python = (Resolve-Path ./artifacts/argos/runtime/Scripts/python.exe).Path
dotnet restore
dotnet build Saylo.slnx
./tools/Test-TranslationRejections.ps1 -PythonExecutable $python -ModelDirectory ./artifacts/argos/models -ReportDirectory ./artifacts/translation-rejections/replay -NoBuild
& $python -B -m unittest discover -s ./tests/Saylo.Modules.Translation.Tests/Python -p 'test_*.py' -v
& $python -m pip check
git diff --check
git status --short
~~~

El runner restaura variables al terminar y genera comparaciones comprobadas por
hash. Sin `-NoBuild`, permite compilar. No escribir sobre evidencia congelada.
Para repetir la **solucion completa** con integraciones:

~~~powershell
$env:SAYLO_ARGOS_PYTHON = $python
$env:SAYLO_ARGOS_MODELS = (Resolve-Path ./artifacts/argos/models).Path
$env:SAYLO_WHISPER_MODEL_DIR = (Resolve-Path ./artifacts/whisper/models).Path
$env:SAYLO_WHISPER_ES_PCM = (Resolve-Path ./artifacts/whisper/audio/es.pcm).Path
$env:SAYLO_WHISPER_EN_PCM = (Resolve-Path ./artifacts/whisper/audio/en.pcm).Path
$env:SAYLO_WHISPER_ES_EXPECTED = 'necesito ayuda pedido'
$env:SAYLO_WHISPER_EN_EXPECTED = 'need help order'
$env:SAYLO_REJECTION_REPORT_DIR = [IO.Path]::GetFullPath('./artifacts/translation-rejections/full/rejections')
$env:SAYLO_INTEGRITY_REPORT_DIR = [IO.Path]::GetFullPath('./artifacts/translation-rejections/full/integrity')
$env:SAYLO_VALIDATION_REPORT_DIR = [IO.Path]::GetFullPath('./artifacts/translation-rejections/full/quality')
dotnet test Saylo.slnx --no-build --no-restore --logger trx --results-directory ./artifacts/translation-rejections/full/trx
~~~

La solucion existente es **Saylo.slnx**. La instruccion adjunta termina con
`dotnet build Saylo.sln`; se utilizo la solucion real, sin crear otra.
Sin variables de recursos, las integraciones se omiten: no seria equivalente
a los 253 resultados de esta ejecucion.

Archivos de produccion: ArgosTextTranslationEngine, ArgosProcessSession,
ArgosProtocol, NumericIntegrity, TranslationIntegrityPlan y argos_worker;
nuevo CustomerServiceTranslationProfile. Nuevos tests: TranslationRejectionTests,
TranslationRejectionIntegrationTests, Python/test_ranked_candidates y
Python/test_rejection_comparison. Herramientas nuevas: Test-TranslationRejections.ps1
y Compare-TranslationRejections.py. Se agregan este documento y la evidencia,
sin eliminar archivos. El inventario y preservacion estan en
[changed-files.json](validation/rejection-results/2026-10-29/changed-files.json).

## 10. Riesgos pendientes

Las reglas no prueban semantica universal, genero, destinatario, modalidad,
promesas, responsabilidades ni contexto discursivo. No se debe habilitar
traduccion empresarial automatica basandose solo en literales y numeros.
Hay ocho rechazos sin resolver, un nuevo rechazo terminologico con v2, falso
rechazo potencial, variacion por nonce y regresion de latencia corta.
Los mensajes largos espanoles de la sonda siguen sin traduccion utilizable.

La generacion permanece local y conserva el audit hook que bloquea sockets/DNS
de Python; sus pruebas pasan. No se agregaron accesos de red, APIs ni descargas.
La prueba operacional con red fisicamente desconectada y la auditoria de posibles
accesos nativos no se ejecutaron; no se presenta el audit hook como aislamiento
total del sistema operativo. Siguen pendientes revision de licencias/distribucion,
avisos y runtime portable indicados en 3.1. Solo se guardan fixtures autorizados
del corpus y sondas sinteticas; produccion no registra conversaciones ni candidatos.

## 11. Recomendacion siguiente

Mantener las alternativas acotadas y los bloqueos actuales para un piloto
supervisado. Encargar revision bilingue de las 80 salidas y aprobar glosario y
registro por dominio. Definir contexto explicito de destinatarios/posesion y
fechas antes de ampliar reglas; evaluar conservacion de sujetos, modalidad e
instrucciones entre segmentos con casos independientes.

Medir arranque y latencia corta bajo carga controlada para decidir si generar
alternativas en una sola inferencia compensa su coste. Si los modelos instalados
siguen sin representar soporte, garantia o escalamiento correctamente, comparar
alternativas justificadas con este corpus intacto, integridad, rendimiento y
licencias antes de cambiar motor o descargar recursos adicionales.
