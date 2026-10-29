# Tarea 3.2: integridad de traduccion empresarial

Responsable: Stanley Camacho Abreu. Fecha local: 2026-10-28, America/Caracas.
Rama conservada: users/stanley-translation.

**Se implementaron controles de integridad manteniendo CTranslate2, modelos y
worker Python. No se declara aceptacion empresarial completa.** Un rechazo
seguro evita entregar datos corruptos, pero no equivale a traducir correctamente
ni resuelve linguisticamente los ocho casos graves.

## Problemas originales y conservacion del trabajo

La [Tarea 3.1](translation-quality-validation.md) registro 80 frases, 40 por
direccion: 2 problemas criticos, 8 graves, 10 moderados, 14 menores y 46 sin
problemas observados. Los criticos alteraron un correo y los digitos/sufijo de
un seguimiento. Los pasajes largos perdian instrucciones e inventaban estados.

Se preservaron byte a byte informes, corpus, revision y resultados historicos.
La comparacion nueva comprueba sus hashes. Se adapto el arnes de evaluacion para
registrar rechazos controlados, conservando los fallos ante errores inesperados;
no se alteraron resultados o referencias anteriores para mejorar las metricas.

No se modificaron Desktop, Conversations, Identity, Api, ModelManagement,
TranslationService, Whisper ni el trabajo de otros desarrolladores. No se
implementaron TTS/pronunciacion, telefonia, base de datos ni APIs externas.
No se agregaron modelos, grabaciones o conversaciones privadas a Git.
No se cambio de rama ni se hizo commit/push.

## Proteccion de identificadores

El adaptador C# valida entrada, prepara segmentos, protege literales, infiere
mediante el mismo worker, comprueba/restaura referencias y verifica invariantes.
Entrega texto solo cuando todos los segmentos han pasado.

ProtectedText reconoce formatos comunes de emails, URL HTTP/HTTPS/www, telefonos
internacionales/agrupados, referencias alfanumericas y numeros precedidos por
pedido/order/code/codigo/tracking/referencia. Conserva ceros iniciales de codigos
reconocidos. Protege SQL, VPN y otros identificadores tecnicos explicitamente
enumerados. Las palabras naturales en MAYUSCULAS no se enmascaran como codigos.
ProtectedTerms permite agregar nombres y otros literales, con copia inmutable,
hasta 64 terminos y longitud limitada.

No es un parser universal de todas las sintaxis RFC ni de identificadores
arbitrarios. Formatos no reconocidos requieren nuevos casos y reglas antes de
prometer proteccion especifica.

Cada ocurrencia recibe una referencia aleatoria de nueve digitos, incluso
cuando el identificador se repite. Se probaron candidatos con el motor instalado:
las referencias alfanumericas opacas se omitian frecuentemente; las numericas
se copiaban mas a menudo. Eso no garantiza supervivencia ni fidelidad semantica.

RandomNumberGenerator genera candidatos. Se comprueban colisiones contra todo
el texto y las referencias de otros segmentos, con regeneracion acotada.
Antes de restaurar cualquier literal se exige aparicion exacta, unica, con
limites de token y orden correcto. Modificacion, duplicacion, ausencia,
fusion con otros digitos o reordenacion producen error. No se reparan
referencias perdidas ni se devuelven restauraciones parciales.

Se conservan posiciones del texto restaurado: comprobar un numero de pedido no
depende de si la salida dice "pedido" u "orden". Las referencias no salen al
usuario ni aparecen en errores/logs. No hay historial ni cache de mensajes.
Los nonces pueden cambiar redaccion o disponibilidad entre ejecuciones.

## Valores numericos y errores compatibles

NumericIntegrity compara hechos tipados: valor decimal canonico, clase, unidad
y orden de ocurrencia. No compara solamente cadenas. El orden de referencias y
valores reduce intercambios entre cantidades/productos; no demuestra todas las
relaciones del lenguaje natural.

Se contemplan enteros, decimales, signos, importes, porcentajes, cantidades con
unidades comunes, fechas de calendario reconocibles y horarios. Pruebas reales
y unitarias distinguen, por ejemplo:

- 49.95 USD y USD 49,95 son equivalentes.
- EUR 1,250.50 y 1.250,50 EUR son equivalentes.
- 09:00 y 9: 00; 17:30 y 5:30 p.m. conservan la hora.
- 31/12/2026 y 12/31/2026 permiten comprobar el mismo dia.
- Los meses escritos se reconocen en ambos idiomas.
- -$25 no equivale a $25; el signo menos Unicode tambien es negativo.
- USD no se convierte silenciosamente en un simbolo dolar sin pais.

Se rechazan cambios de valor, signo, unidad, moneda, fecha o porcentaje.
No se presupone que $ sea USD. Se rechazan fechas ambiguas como 03/04/2026,
separadores con tres cifras finales ambiguas, agrupaciones por espacios,
parentesis contables y rangos no verificables. Un entero aislado con ceros
iniciales se rechaza si no se reconocio como identificador.
No se asignan siglos a anos cortos ni se reinterpretan formatos por el idioma.
Numeros escritos con palabras y unidades desconocidas no tienen cobertura
semantica completa.

IntegrityValidationFailed se anadio al final de TranslationErrorCode, sin cambiar
los valores anteriores. El mensaje es generico; el diagnostico usa categorias
sin datos privados. TranslationService ya propaga el codigo como
TranslationStageFailed y termina: no emite TextTranslationCompleted ni inicia
pronunciacion/sintesis. En voz puede haberse entregado antes la transcripcion.

Tras consumir una respuesta correlacionada, un rechazo conserva el worker sano.
Cancelacion activa, timeout, EOF o fallo de protocolo siguen cerrando la sesion;
la siguiente solicitud puede reiniciarla. No se crea un proceso por traduccion.

## Textos largos, orden y cancelacion

Se conserva el maximo publico de 1000 unidades UTF-16. Cada segmento admite
hasta 240, configurable entre 80 y 240; como maximo hay 64 segmentos y 8000
unidades de salida agregada. Cada candidato se limita antes de procesarlo.
El limite nativo de 512 tokens permanece en el worker.

Se separan oraciones y saltos de linea conservando orden y separadores. No se
cortan literales reconocidos, decimales, abreviaturas conocidas o iniciales
habituales. Una oracion demasiado larga o exceso de segmentos produce error;
no hay recorte silencioso ni particion arbitraria por caracteres.

Se comprueba cancelacion entre segmentos y antes de entregar, bajo el timeout
existente de traduccion. Los resultados intermedios usan memoria acotada:
un fallo tardio impide cualquier entrega parcial. El semaforo y los IDs existentes
aislan solicitudes concurrentes y sus mapas.

Segmentar reduce el contexto repetitivo que produjo omisiones, pero pierde
contexto entre oraciones. Se ejecuto tambien un caso con "It will not be canceled"
despues de un pedido. Anaforas, alcance de negaciones, abreviaturas desconocidas
y relaciones discursivas requieren revision humana.
[Resultados largos](validation/integrity-results/2026-10-28/integrity-long-messages.json).

## Lenguaje profesional y glosario

Las reglas validan; no reescriben ni aplican sustituciones globales. Comprueban
negacion observable, algunas relaciones "antes/before" y posesivos ingleses de
tercera persona cuyo referente no se demuestra con la forma espanola admitida.
Los literales se excluyen: un "no" dentro de un correo no puede ocultar la perdida
de negacion en la oracion. No se garantiza deteccion de toda inversion de sentido.

Son reglas conservadoras: pueden rechazar traducciones correctas, como es-34.
RequireFormalSpanish es optativo y rechaza algunas formas de tuteo; no genera
tratamiento de usted ni garantiza cortesia. Un glosario optativo exige una de
las alternativas aprobadas cuando aparece un termino fuente. Si falta, falla
sin reparar. La configuracion se copia y queda inmutable.

El corpus principal uso customer-service-validation-v1: trato formal y reglas
para customer support, water damage y escalar. Es un perfil explicito declarado
en las pruebas, no el valor predeterminado del constructor.
Ejemplo para el futuro punto de composicion, sin modificar Desktop:

~~~csharp
var integrity = new TranslationIntegrityOptions(
    requireFormalSpanish: true,
    protectedTerms: ["Saylo Pro"],
    glossary:
    [
        new("en", "customer support",
            ["atenci\u00f3n al cliente", "soporte al cliente", "servicio al cliente"]),
        new("en", "water damage",
            ["da\u00f1os por agua", "da\u00f1o por agua",
             "da\u00f1os causados por agua", "da\u00f1o causado por agua"]),
        new("es", "escalar", ["escalate", "escalating", "escalated"])
    ]);
var options = new ArgosTranslationOptions(
    configuredPythonPath, configuredModelDirectory, integrity: integrity);
~~~

La composicion empresarial debe elegir/aprobar esas reglas. Los nombres
invariables van en ProtectedTerms; el glosario controla lenguaje natural no
protegido. Encontrar un termino no demuestra que se refiera al objeto correcto.
No hay una restriccion de decodificacion probada que genere cualquier glosario
o registro: se rechaza o revisa, sin afirmar correccion automatica.

## Corpus anterior y nuevo

En la ultima ejecucion, las **80 entradas se evaluaron: 68 traducciones entregadas por las reglas y 12 rechazos controlados**. Dos fechas ambiguas se rechazaron antes de inferir. El correo es-18 y seguimiento es-33 se conservaron exactamente en las salidas entregadas: **0 alteraciones criticas aceptadas**. Los **8 casos graves anteriores estan bloqueados y no tienen traduccion utilizable**, no se cuentan como corregidos. La comparacion conserva un aviso lexical en es-13 por formato de horas equivalente; no es perdida de identificador. La configuracion predeterminada, sin glosario/registro formal, entrego 72 y rechazo 8 en su ejecucion independiente.

[Resultados completos](validation/integrity-results/2026-10-28/integrity-corpus.json):
entrada, salida anterior, salida nueva o ausencia de salida, causa segura y
latencia de cada intento. Un rechazo no cuenta como traduccion correcta.
[Comparacion estructurada](validation/integrity-results/2026-10-28/integrity-comparison.json)
comprueba corpus e historial y no asigna nuevas notas linguisticas.

La configuracion predeterminada tambien ejecuto las 80 entradas:
[default-corpus.json](validation/integrity-results/2026-10-28/default-corpus.json).
Sin glosario/registro formal, errores de dominio de es-38, en-02 y en-37 pueden
seguir entregandose. No se presenta el perfil configurado como garantia del
constructor predeterminado.

Observaciones provisionales:

- es-18 conserva el correo, pero puede devolver "Please send them to ...":
  pierde la mencion explicita de documentos, una regresion de claridad.
- es-33 conserva el seguimiento, pero puede persistir "monitoring number".
- en-18 puede introducir "al" antes del correo. Integridad no certifica naturalidad.
- Persisten calcos, cortesia, genero y terminologia fuera del glosario.
- es-34 y fechas ambiguas ahora pueden bloquearse: perdida de disponibilidad,
  no una mejora linguistica. SQL tambien puede rechazarse si se pierde el nonce.
- La revision antigua describia "su direccion" en es-04; la entrada real dice
  "la direccion de entrega". El defecto es la adicion de "his". Se aclara aqui
  sin alterar el historial.

Que una salida cambie no demuestra mejora; una identica conserva problemas
anteriores. Toda aceptacion linguistica sigue pendiente de revision bilingue.
[Notas de revision](validation/integrity-results/2026-10-28/review-notes.json).

## Rendimiento y estabilidad

Mismo equipo: Windows 11 Pro x64, i5-11400H, 6 nucleos/12 hilos, 15.79 GiB RAM,
.NET 8.0.33, Python 3.12.14, CTranslate2 4.8.4 CPU/int8 y cuatro hilos.
No se instalaron paquetes nuevos.

| Direccion caliente | Tarea 3.1 n | Mediana / p95 anteriores (ms) | Aceptados nuevos n | Mediana / p95 nuevos (ms) |
| --- | --- | --- | --- | --- |
| es->en | 39 | 92.704 / 136.380 | 31 | 83.628 / 139.954 |
| en->es | 40 | 63.370 / 106.194 | 36 | 54.602 / 81.102 |

Primera solicitud con arranque: 2457.223 ms antes y 2464.415 ms ahora. Working set del worker durante el corpus: 257.9-261.1 MiB, 68 muestras posteriores a entregas. Antes: 257.7-263.0 MiB en 90 muestras incluyendo sondas largas. No es pico continuo ni memoria de Desktop/Whisper; el anfitrion .NET no se perfilo aqui.

Los aceptados nuevos son una muestra distinta por los rechazos. Se guardan
tambien tiempos de todos los intentos, sin seleccionar solo los rapidos.
p95 usa rango mas cercano y en muestras pequenas se acerca al maximo.
Una PC y una ejecucion no constituyen SLA.

Se compararon el mismo worker/modelos por IPC directo, solo dentro de una prueba,
y el adaptador protegido: texto simple, correo, seguimiento, dos oraciones y
pasaje largo. Produccion no tiene un interruptor para desactivar integridad.
La referencia directa es insegura: puede devolver contenido corrupto/incompleto
y no representa calidad equivalente.

| Caso | n | Mediana IPC directo ms | Mediana protegida ms | Preparacion ms | Validacion ms | Entregados |
| --- | --- | --- | --- | --- | --- | --- |
| plain-short | 7 | 27.595 | 33.466 | 0.302 | 0.492 | 7 |
| email | 7 | 83.159 | 84.327 | 0.442 | 0.621 | 7 |
| tracking | 7 | 89.239 | 76.924 | 0.362 | 0.665 | 7 |
| two-sentences | 7 | 54.566 | 75.144 | 0.370 | 1.061 | 7 |
| long-repeated | 3 | 513.471 | 1067.602 | 1.399 | 16.486 | 2 |

En la sonda larga hay 2 entregas y 1 rechazo: la mediana mezcla ambos estados y no debe confundirse con latencia garantizada de una traduccion completa. La mediana medida crece de 513.471 a 1067.602 ms (aproximadamente 2.08 veces). El working set protegido en esta prueba fue 257.9-261.4 MiB.

[Muestras de rendimiento](validation/integrity-results/2026-10-28/integrity-performance.json).
No se quitaron controles para ganar velocidad. Segmentar aumenta inferencia/IPC;
la ausencia de regresion material en mensajes largos no se declara satisfecha
de forma general.

Siguen probadas cancelacion activa/en cola, timeout, fallo/reinicio de proceso,
cierre durante inferencia y concurrencia. Se agregaron referencias repetidas,
mapas independientes y reutilizacion despues de rechazo.

Mediciones de escenarios de estabilidad de esta ejecucion: cancelacion activa
con reinicio 4.499 s; cancelacion en cola 4.275 s; cierre durante inferencia
2.233 s; timeout de traduccion de 1 ms, 2.136 s en total; recuperacion tras fallo
2.252 s. Los tiempos de escenario incluyen arranque/calibracion y recogida,
no son latencias puras de cancelacion. Los JSON stability-* y el resumen TRX
conservan esos alcances y duraciones. No se promete cierre total en 1 ms.

## Pruebas, reproduccion y archivos

Resultado final: **209 pruebas .NET aprobadas, 0 fallidas y 0 omitidas**, frente a 136 anteriores: 68 casos unitarios y 5 integraciones nuevas. **12 pruebas Python aprobadas**, frente a 7: cinco comprueban evidencia/comparacion. Restore correcto, build con 0 errores y 0 advertencias, pip check correcto y git diff --check sin errores. Las cinco integraciones Argos/Whisper antes omitidas siguen configuradas y pasan. El cierre final comprueba ausencia de workers huérfanos.

[Resumen y hashes TRX](validation/integrity-results/2026-10-28/test-summary.json).

Las pruebas con dobles demuestran coordinacion, errores y ausencia de etapas
opcionales; no simulan las traducciones del corpus. El arnes antiguo conserva
igualdad textual para entradas sin literales; con nonces nuevos verifica
literales y valores porque "orden" y "pedido" pueden variar sin mezcla.
Se conservan PID, correlacion y fallos ante errores inesperados.

Desde la raiz, con recursos existentes:

~~~powershell
$python = (Resolve-Path ./artifacts/argos/runtime/Scripts/python.exe).Path
dotnet restore
dotnet build Saylo.slnx
./tools/Test-TranslationIntegrity.ps1 -PythonExecutable $python -ModelDirectory ./artifacts/argos/models -ReportDirectory ./artifacts/translation-integrity/replay
& $python -I -B ./tools/Compare-TranslationIntegrity.py --new ./artifacts/translation-integrity/replay/integrity-corpus.json --output ./artifacts/translation-integrity/replay/comparison.json
& $python -B -m unittest discover -s ./tests/Saylo.Modules.Translation.Tests/Python -p 'test_*.py' -v
git diff --check
~~~

Para toda la solucion, configurar tambien las variables Argos/Whisper de las
guias existentes y un SAYLO_VALIDATION_REPORT_DIR nuevo; no escribir sobre el
historial. Sin configuracion las integraciones se omiten. El script restaura las
variables al terminar y admite -NoBuild para replay preparado.
[Inventario de cambios](validation/integrity-results/2026-10-28/changed-files.json).

## Riesgos y recomendacion

Conservar motor y controles como base para un piloto supervisado.
**No aprobar aun traduccion empresarial automatica sin revision.**
No hay corrupcion literal silenciosa observada en el corpus final, pero no se
prueba exactitud universal. Los ocho casos graves se bloquean en el perfil;
siguen sin traduccion utilizable y no se declaran resueltos linguisticamente.
Hay falsos rechazos, variacion por nonce y coste material en textos largos.

Faltan revision humana bilingue, mensajes reales autorizados, otros formatos e
identificadores y pruebas prolongadas. La desconexion efectiva de red, derechos
de modelos/recursos, avisos nativos y runtime Windows portable siguen pendientes
de Tarea 3.1. No se aprobo distribucion comercial ni se genero instalador.

Siguiente tarea: reducir rechazos recuperando contexto sin relajar integridad,
aprobar politica/glosario, mantener sustantivos/referentes y revisar este corpus
bilingue. Si el modelo sigue sin fidelidad suficiente, comparar alternativas
justificadas con las mismas evidencias y licencias antes de sustituirlo.
