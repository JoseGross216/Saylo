# Validacion empresarial de traduccion offline

Responsable: Stanley Camacho Abreu. Fecha local: 2026-10-28, America/Caracas.
Rama inspeccionada: `users/stanley-translation`. HEAD inicial:
`24fd135`. El repositorio estaba limpio antes de esta tarea.

**Decision: conservar CTranslate2 como base tecnica y mejorar antes de aceptar
el uso empresarial. No se aprueba todavia el empaquetado comercial.**
La ejecucion e integridad pasan, pero hay corrupcion de identificadores/correos,
cambios de significado y omisiones en pasajes largos. Faltan revision humana
bilingue, prueba con red realmente desconectada y cierre de licencias.

## 1. Objetivo y alcance

Evaluar el motor existente, sin sustituirlo: calidad, rendimiento, integridad,
offline, ciclo de vida del worker y requisitos de distribucion Windows.
Se inspeccionaron TranslationService, ArgosTextTranslationEngine, sus opciones,
protocolo/sesion/worker, LocalModelCatalog, pruebas y herramientas existentes.

No se modifico codigo de produccion, Desktop, ModelManagement, contratos,
Identity, Api ni el trabajo de otros desarrolladores. No se implementaron
telefonia, base de datos, TTS ni pronunciacion. No se incorporaron pesos ni audio
al repositorio. No se cambio de rama ni se hizo commit/push.

## 2. Entorno de ejecucion

- Windows 11 Pro x64, version 10.0.26200.
- Intel Core i5-11400H @ 2.70 GHz: 6 nucleos, 12 procesadores logicos.
- Memoria fisica detectada: 16,952,647,680 bytes, aproximadamente 15.79 GiB.
- .NET SDK 10.0.401, proyectos `net8.0`, runtime de pruebas .NET 8.0.33.
- CPython 3.12.14 x64, entorno aislado `artifacts/argos/runtime`.
- CTranslate2 4.8.4, CPU/int8, 4 hilos intra-op, 1 hilo inter-op,
  beam size 4, limite de salida 512 tokens.
- NumPy 2.5.4, SentencePiece 0.2.2, Sacremoses 0.1.1, subword-nmt 0.3.8.
  Todas las transitivas figuran en el inventario de licencias.
- `pip check`: sin dependencias rotas. No se instalo ningun paquete nuevo.

Datos de equipo sin nombre de usuario/equipo: [environment.json](validation/results/2026-10-28/environment.json).
Los datos originales TRX permanecen en `artifacts/translation-validation/verified-tests`;
el resumen versionable conserva sus hashes y resultados, sin conversaciones privadas.
Es una sola PC, una secuencia de carga y un corpus sintetico; no constituye un SLA
ni una prueba de carga prolongada o de todas las computadoras de empleados.

## 3. Modelos, versiones e integridad

| Direccion | ID / version | SHA-256 del paquete original |
| --- | --- | --- |
| es -> en | argos-es-en / 1.9 | `c54df2b62fceaf54a3ce5d97db6bf56efd7940063329f6778f4212d2acb370d4` |
| en -> es | argos-en-es / 1.0 | `d698d0ef87ad70d5d184b7fa6965905bf4368f09a2bb9ffb165a79bac96af0c4` |

Los dos archivos descargados coinciden con los hashes fijados anteriormente por
el proyecto. Los 11 archivos instalados coinciden en longitud y SHA-256 con el
manifiesto y con los miembros de esos paquetes originales. No hay archivos
extra o ausentes en los directorios. Tambien paso la validacion real de
LocalModelCatalog y la carga CPU de ambos modelos.

Se conservan solo los recursos necesarios: modelo, configuracion cuando existe,
vocabulario, BPE o SentencePiece, metadata y README. Stanza fue excluido por el
instalador existente. La instalacion de modelos ocupa 410,860,373 bytes.

Los hashes fijados proceden de observaciones previas del proyecto; no son firmas
del editor. Manifiestos y modelos deben estar en una ruta protegida y el futuro
build debe aprobar y firmar el conjunto. No actualizar mientras exista un worker.
Evidencia por archivo y por paquete: [translation-license-evidence.json](validation/translation-license-evidence.json).

## 4. Conjunto y resultados de traduccion

[translation-quality-cases.json](validation/translation-quality-cases.json) contiene
**80 frases: 40 es -> en y 40 en -> es**, con ID estable, referencia de significado,
etiquetas, literales exactos y marcadores de negacion cuando corresponden.
Los nombres y direcciones example.com son fixtures sinteticos.

Incluye saludos, informacion, confirmaciones, preguntas/respuestas, negaciones,
fechas ambiguas y explicitas, horarios, precios/monedas, pedidos, nombres,
correos, SQL/VPN, cortesia, usted, ambiguedad y errores de expresion.
Las frases cortas tienen entre 26 y 77 unidades UTF-16. Las referencias se usan
solo para revision: **nunca se envian como resultado ni reemplazan la inferencia**.

El test ejecuto las 80 entradas con ArgosTextTranslationEngine y el worker
instalado; obtuvo 80 salidas reales, sin errores de comunicacion, usando el mismo
PID. Ademas ejecuto 10 sondas largas sinteticas: 5 por direccion, de 927/918
unidades UTF-16. No se cuentan como frases adicionales del corpus linguistico.

Resultados completos, tiempos individuales, metricas y sondas:
[translation-quality.json](validation/results/2026-10-28/translation-quality.json).
La huella del corpus se guarda en el informe. Las revisiones conservan tambien
la salida exacta observada para detectar si se revisa otro resultado.

## 5. Calidad observada

[translation-quality-review.json](validation/translation-quality-review.json)
registra la revision provisional realizada por el asistente para los 80 casos.
**No intervino un revisor humano bilingue; esa aceptacion sigue pendiente.**
Los conteos siguientes son por maxima gravedad en cada frase, no tasas
estadisticas de precision:

| Maxima gravedad provisional | Frases |
| --- | --- |
| Critica | 2 |
| Grave | 8 |
| Moderada | 10 |
| Menor | 14 |
| Sin problema observado en esta revision | 46 |

El ingles -> espanol conserva mas ejemplos sin incidencias en este corpus.
El espanol -> ingles presenta corrupcion de identificadores y referentes.
Ambas direcciones son utilizables para experimentacion supervisada; estos
resultados no justifican envio o reproduccion automatica sin controles.

| Caso | Entrada / aspecto | Salida real relevante | Evaluacion |
| --- | --- | --- | --- |
| es-18 | `soporte@example.com` | `support @ example.com` | Critica: cambia y fragmenta un destino. |
| es-33 | `ZX987654321ES` | `ZX987654310EN` | Critica: cambia digitos y sufijo. |
| es-04 | `su direccion de entrega` | `his delivery address` | Grave: cambia el referente. |
| es-10 | `su contrasena` | `his password` | Grave: cambia el referente en seguridad. |
| es-28 | `antes del viernes` | `by Friday` | Grave: puede incluir el viernes. |
| es-38 | escalar incidencia | `scale up the incidence` | Grave: instruccion tecnica incorrecta. |
| en-02 | `customer support` | `el cliente` | Grave: cambia el destinatario del contacto. |
| en-37 | `water damage` | `dano al agua` | Grave: cambia la condicion de garantia. |
| en-04 | trato formal | `tu direccion` | Moderada: tuteo inconsistente. |
| en-23 / en-40 | `charge` en facturacion | `carga` | Moderada: terminologia incorrecta. |

Las negaciones explicitas de los casos cortos no mostraron una inversion clara
en la revision provisional. Los 23 controles de marcador detectaron alguna
negacion esperada; eso **no prueba el alcance semantico**. La salida de es-31
tiene gramatica confusa. Nombres Maria/Lopez/Perez, pedidos AB-009871 y codigo
004812 se conservaron en sus casos, pero es-33 demuestra que no hay garantia
general. es-40 conserva 80 pero sustituye USD por un simbolo dolar ambiguo.

Hay errores de naturalidad (`contacting with support`, `We attend`), omision de
`please`, anglicismos y oscilacion de registro. SQL y VPN se conservan, pero
terminos de facturacion, seguimiento y escalado no son consistentes.

Los controles automaticos produjeron **6 avisos**: es-12, es-13, es-18, es-33,
es-40 y en-14. No son seis errores equivalentes: en-14 convierte 49.95 a 49,95,
una localizacion decimal aceptable; es-12/es-13 cambian espacios/formato sin
perder necesariamente fecha/hora. No se asignan notas semanticas mediante
igualdad de cadenas, BLEU ni simples marcadores.

En las sondas largas repetidas se observaron **omisiones y afirmaciones
inventadas** en las cinco repeticiones de ambas direcciones, con salida
determinista en esta muestra. Por ejemplo, es -> en anade
`Your request will be closed by email 12345`; en -> es anade
`Su solicitud no sera completada`. No estan en la entrada.
Son sondas artificiales repetitivas, no 10 mensajes reales de clientes;
demuestran un fallo de fidelidad bajo ese estres, no su frecuencia poblacional.
Se clasifican aparte como graves, sin mezclarlas con los 80 conteos anteriores.
El limite de 1000 caracteres y la comprobacion de 512 tokens no detectaron estas
omisiones: terminar antes del limite no garantiza una traduccion completa.

## 6. Mediciones de rendimiento y estabilidad

Ultima ejecucion completa guardada en el informe. Tiempos de pared en ms,
incluyendo IPC y sobrecarga del adaptador:

| Muestra | n | Minimo | Mediana | p95 | Maximo |
| --- | --- | --- | --- | --- | --- |
| Corpus corto es->en | 39 | 67.158 | 92.704 | 136.380 | 145.772 |
| Corpus corto en->es | 40 | 42.316 | 63.370 | 106.194 | 118.767 |
| Pasaje repetido es->en | 5 | 1087.721 | 1090.899 | 1252.359 | 1252.359 |
| Pasaje repetido en->es | 5 | 524.263 | 533.553 | 761.078 | 761.078 |

p95 usa rango mas cercano, indice ceil(0.95*n). En n=5 coincide con el maximo
y no permite una promesa de servicio. Se excluye la primera solicitud fria
del corpus caliente. Las sondas largas repiten pasajes de tres oraciones;
sus tiempos no implican fidelidad ni representan todos los mensajes largos.

- Inicializacion, incluyendo validacion en C# y arranque/carga:
  **2339.236 ms**.
- Primera traduccion es -> en con arranque: **2457.223 ms**.
- Primera medicion de solicitud/inferencia e IPC, sin arranque:
  **117.637 ms**.
- Primera en -> es con worker caliente: **54.303 ms**.
- Working set del PID Python real, 90 muestras posteriores a solicitudes:
  **257.7-263.0 MiB**.
  No es memoria privada ni pico continuo. El anfitrion .NET de pruebas midio
  86.9 MiB de working set al terminar; no representa Desktop.

Pruebas reales adicionales:

| Comprobacion | Resultado |
| --- | --- |
| 90 solicitudes consecutivas del corpus/sondas | Mismo worker, sin errores de protocolo. |
| 8 solicitudes concurrentes | Resultados iguales a sus 8 bases secuenciales, sin mezcla; serializacion prevista. |
| IDs de solicitud/respuesta | El parser de produccion valida cada respuesta contra el ID solicitado. |
| Cancelacion activa existente | Worker termina; siguiente solicitud usa otro PID y traduce. |
| Cancelacion en cola nueva | No interrumpe la activa; conserva el PID. |
| Timeout de traduccion de 1 ms | EngineFailure esperado; worker recogido. No es un timeout del arranque. |
| Fallo forzado del worker | EngineFailure esperado; siguiente solicitud carga otro PID y traduce. |
| Dispose durante inferencia | Solicitud cancelada; PID recogido; cierre repetido seguro. |
| Cierre final | Ningun worker argos_worker.py restante en la comprobacion final. |

La recuperacion tras fallo forzado tardo **2326.462 ms** en esta ejecucion.
La prueba de timeout tardo 3421.573 ms en total incluyendo arranque y recogida;
no se promete que el cierre completo termine en 1 ms. Los valores incluyen
maquina/planificador y no son garantias de tiempo real.

El timeout del adaptador empieza al obtener el semaforo; no limita toda la
espera en cola. El llamante debe aportar cancelacion para un presupuesto
global. Hay plazos de arranque/traduccion y recogida de procesos. No se hizo
soak test de horas, carga ilimitada ni fallo de hardware/almacenamiento.

## 7. Resultado offline

**Prueba con red efectivamente deshabilitada: PENDIENTE, NO EJECUTADA.**
No se desactivaron adaptadores, firewall, Wi-Fi ni Ethernet de la PC.

Evidencia tecnica aprobada de ejecucion local:

- Modelos y tokenizadores se cargaron desde rutas locales verificadas.
- El worker no implementa API de traduccion externa ni descargas; instalacion
  y pip son herramientas explicitas de preparacion, fuera del inicio del motor.
- Se ejecutaron ambas direcciones con el audit hook existente activo.
- Las pruebas Python comprueban rechazo de eventos socket/DNS/urllib, incluido
  un intento real de socket de loopback en un subproceso con el hook instalado.
- El hook Python no constituye aislamiento del SO para extensiones nativas.

Procedimiento reproducible para una PC de prueba, con desconexion realizada
manualmente por el responsable:

1. Preparar runtime, modelos y paquetes con red; ejecutar restore/build.
2. Guardar los hashes aprobados y configurar las rutas locales.
3. Desconectar manualmente todas las interfaces de red de la PC de prueba.
4. Ejecutar desde la raiz, sin build/restore ni auditoria online:

```powershell
./tools/Test-TranslationValidation.ps1 `
  -PythonExecutable ./artifacts/argos/runtime/Scripts/python.exe `
  -ModelDirectory ./artifacts/argos/models `
  -ReportDirectory ./artifacts/translation-validation/offline `
  -NoBuild

$env:SAYLO_ARGOS_PYTHON = (Resolve-Path ./artifacts/argos/runtime/Scripts/python.exe).Path
$env:SAYLO_ARGOS_MODELS = (Resolve-Path ./artifacts/argos/models).Path
dotnet test ./tests/Saylo.Modules.Translation.Tests/Saylo.Modules.Translation.Tests.csproj `
  --no-build --no-restore --filter 'Category=ArgosIntegration'
```

5. Conservar comandos, resultados, evidencia del estado de interfaces y del
   aislamiento, y hashes de modelo/runtime, sin registrar trafico privado.
6. Restaurar manualmente la conectividad y revisar los resultados.

Un pase sin red demostrara independencia operativa de servicios remotos en ese
equipo; no subsana los fallos linguisticos ni autoriza la distribucion.

## 8. Auditoria de licencias

Inventario instalado con versiones, fuentes, requires-dist, hashes de METADATA,
avisos, DLL/PYD y recursos:
[translation-license-evidence.json](validation/translation-license-evidence.json).
Revision por componente con obligaciones/riesgos:
[translation-license-audit.json](validation/translation-license-audit.json).

Son **13 distribuciones de inferencia mas pip como herramienta de construccion**.
El perfil coincide con el lock existente. Las dependencias de extras no instalados
no se consideran parte del runtime. Los hashes de candidatos PyPI se registran
como evidencia de registro; no prueban que la rueda instalada sea ese artefacto.

| Componente | Version | Licencia revisada / pendiente |
| --- | --- | --- |
| CTranslate2 | 4.8.4 | MIT para el proyecto; partes nativas separadas. |
| NumPy | 2.5.4 | BSD-3-Clause AND 0BSD AND MIT AND Zlib AND CC0-1.0; otros avisos de la rueda abajo. |
| SentencePiece | 0.2.2 | Apache-2.0 para software; no para todo modelo entrenado. |
| Sacremoses | 0.1.1 | MIT; procedencia de recursos de datos pendiente. |
| subword-nmt | 0.3.8 | MIT; no establece licencia de bpe.model. |
| regex | 2026.9.29 | Apache-2.0 AND CNRI-Python. |
| click | 8.5.0 | BSD-3-Clause. |
| joblib | 1.6.0 | BSD-3-Clause. |
| cloudpickle | 3.1.2 | BSD-3-Clause. |
| colorama | 0.4.6 | BSD-3-Clause, confirmado por texto incluido. |
| mock | 5.2.0 | BSD-3-Clause, confirmado por texto incluido. |
| PyYAML | 6.0.3 | MIT; libyaml nativa/procedencia pendiente. |
| tqdm | 4.70.1 | MPL-2.0 AND MIT; facilitar fuente cubierta y conservar avisos. |
| pip | 25.0.1 | MIT con vendorizadas adicionales; excluir del runtime final o auditar aparte. |

Conservar copyrights, condiciones y descargos de BSD/MIT; incluir Apache y
NOTICE aplicable, documentar modificaciones; conservar ambos textos de regex.
En tqdm, preparar acceso al codigo fuente cubierto por MPL y sus cambios,
sin inferir que toda la aplicacion tenga esa licencia.
[Texto MPL 2.0](https://www.mozilla.org/en-US/MPL/2.0/).

Hallazgos de redistribucion nativa:

- La rueda CTranslate2 instalada no enumera LICENSE/NOTICE en su RECORD;
  recuperar los avisos del [tag v4.8.4](https://github.com/OpenNMT/CTranslate2/blob/v4.8.4/LICENSE).
- Incluye `cudnn64_9.dll`, version PE 9.10.2.21, y `libiomp5md.dll`,
  version de archivo 20250910/producto 5.0. CPU/int8 no implica una rueda
  libre de componentes GPU ni que todo este bajo MIT.
- La [receta Windows v4.8.4](https://github.com/OpenNMT/CTranslate2/blob/v4.8.4/python/tools/prepare_build_environment_windows.sh)
  usa toolkit Intel 2025.3.0.372, oneDNN 3.1.1 y CUDA/cuDNN, y copia esas DLL.
  Es evidencia upstream; falta probar la procedencia exacta del build instalado.
- cuDNN tiene [terminos NVIDIA](https://docs.nvidia.com/deeplearning/cudnn/backend/v9.10.2/reference/eula.html)
  propios. Obtener terminos/avisos exactos de Intel OpenMP/MKL; oneDNN 3.1.1
  declara Apache-2.0. Falta SBOM de enlaces estaticos y headers de terceros.
- El LICENSE incluido en NumPy declara OpenBLAS BSD-3-Clause, LAPACK
  BSD-3-Clause-Open-MPI y runtime GCC GPL-3.0-or-later WITH GCC-exception-3.1.
  No basta con su METADATA. Versiones exactas de esos builds quedan pendientes.
- SentencePiece tampoco incluye LICENSE/NOTICE en el RECORD instalado;
  recuperar el [texto del tag](https://github.com/google/sentencepiece/blob/v0.2.2/LICENSE)
  e inventariar componentes nativos.
- CPython 3.12.14 requiere PSF y avisos historicos/incorporados. El interprete
  base observado usa OpenSSL 3.5.8, SQLite 3.53.1 y zlib 1.3.2.
  La licencia y el inventario del runtime privado final deben corresponder al
  binario distribuido, no copiar sin mas el entorno de desarrollo.
- VC runtime tambien requiere paquete/terminos exactos; se detecta una DLL
  msvcp140 incorporada en NumPy. No se ha cerrado ese inventario.

MiniSBD 0.9.5 **no esta instalado en el runtime seleccionado**. Su AGPLv3 se
confirmo en LICENSE/METADATA del entorno antiguo separado
`artifacts/argos/venv`; no se elimino ese entorno de evaluacion. Algunos modulos
internos tienen avisos diferentes, que no sustituyen el LICENSE principal.
Tampoco se instalaron argostranslate, Stanza, torch, spaCy ni ONNX Runtime en
el perfil reducido. No empaquetar `artifacts/` recursivamente ni ese venv antiguo.

Modelos y recursos:

- **es -> en 1.9:** README incluido declara que el modelo OPUS original es
  CC-BY 4.0. Mantener atribucion de Jorg Tiedemann/Santhosh Thottingal y la cita,
  enlace de licencia y cambios/conversiones. No se presume que un modelo con
  igual nombre en Hugging Face sea el mismo artefacto.
- **en -> es 1.0:** metadata/README no traen licencia especifica; mencionan
  OpenSubtitles, ParaCrawl y UNPC. Stanza no se distribuye con esta instalacion.
- El [mantenedor declaro el 2 de agosto de 2026](https://github.com/argosopentech/argos-translate/issues/533#issuecomment-5160080718)
  que los binarios .argosmodel se publican bajo MIT/CC0. Es evidencia relevante,
  pero no establece por si sola alcance y procedencia de cada recurso de los
  dos paquetes exactos. Las preguntas posteriores de terceros no son licencias.
- Los 11 archivos, incluidos vocabularios, BPE, SentencePiece, README y metadata,
  estan individualmente inventariados. Faltan derechos/procedencia verificables
  por recurso y reconciliar CC-BY con la declaracion general del editor.
  No se infiere la licencia de pesos a partir del motor ni de las bibliotecas
  de tokenizacion. **Redistribucion comercial de los paquetes pendiente.**

No hay una autorizacion global de empaquetado comercial en esta tarea. El
inventario tecnico no reemplaza la aprobacion empresarial de los pendientes.

## 9. Pruebas automatizadas y defectos reproducidos

| Validacion | Resultado final |
| --- | --- |
| dotnet restore | Correcto. |
| dotnet build Saylo.slnx | 0 advertencias, 0 errores. |
| dotnet test Saylo.slnx, modelos/audio configurados | **136 aprobadas, 0 fallidas, 0 omitidas**. |
| Python unittest, comando documentado | **7 aprobadas, 0 fallidas, 0 omitidas**. |
| pip check | Sin dependencias rotas. |
| Publicacion Release del modulo Translation | Worker copiado; SHA-256 igual al de fuente. |
| git diff --check | Sin errores en la verificacion final. |

Las cinco integraciones antes omitidas se ejecutaron: dos Argos (traducciones/
reutilizacion y cancelacion/recuperacion) y tres Whisper (es, en y cancelacion/
reutilizacion nativa). Usaron modelos y fixtures locales existentes, sin generar
audio ni modificar Whisper.

Se agregaron seis pruebas reales de validacion. Las unitarias previas y las
reglas progresivas de TranslationService permanecen. Las dos plantillas vacias
de Identity/Conversations siguen formando parte del total; no se presentan como
validacion funcional empresarial.

La primera suite ampliada produjo 130 pases y 6 fallos: callbacks de metricas
de la prueba Argos existente escribieron sobre un ITestOutputHelper ya fuera de
su ambito activo. La traza mostro `There is no currently active test`.
Las seis pruebas nuevas pasaron aisladas. Se corrigio solo la salida de
diagnostico: los callbacks encolan datos y la prueba imprime la cola en su propio
ambito. No se elimino ninguna asercion ni se altero el motor para cambiar salidas.
La suite completa volvio a pasar con los seis escenarios juntos.

La documentacion .NET advierte que callbacks en curso pueden sobrevivir a Dispose;
no se atribuye aqui el comportamiento a una version concreta del runtime sin
una reproduccion mas aislada:
[recogida de metricas](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-collection).

Resumen verificable y hashes de TRX:
[test-summary.json](validation/results/2026-10-28/test-summary.json).
Los resultados de calidad son datos revisables, no aserciones artificiales que
declaren el motor apto porque retorno texto. Los fallos linguisticos siguen
registrados aunque la prueba de ejecucion haya pasado.

## 10. Instalacion y futura distribucion Windows

No se genero un instalador ni un runtime redistribuible definitivo.
La publicacion del modulo solo demuestra que `TextTranslation/argos_worker.py`
llega al directorio publish sin alteraciones; no demuestra una aplicacion
instalable completa.

El instalador futuro debera incluir:

1. Aplicacion .NET 8/Avalonia publicada para Windows x64 con estrategia definida
   de runtime .NET privado o self-contained.
2. Python 3.12 x64 privado y relocatable, con rutas controladas y licencias.
   **No copiar el venv actual**: pyvenv.cfg apunta al interprete base del equipo.
   El empleado no debe instalar Python, pip, Visual Studio ni SDK.
3. Las 13 dependencias de inferencia y sus bibliotecas nativas, tokenizer/data,
   avisos y fuentes cubiertas por MPL. Excluir herramientas de construccion.
4. Los dos modelos solo despues de cerrar derechos y aprobar hashes.
5. Worker de la misma version de la aplicacion, configuracion de rutas absolutas,
   limites/plazos, hilos y manifiestos protegidos.
6. Datos/cache no sensibles en LocalAppData, instalacion protegida contra escritura
   de usuarios ordinarios y procedimiento de actualizacion con worker cerrado.

Preparar wheelhouse y lock con hashes aprobados en CI. Instalar con
`--no-index`, `--find-links` y `--require-hashes` una vez creado ese lock:
el lock actual fija versiones pero no cumple todavia esa verificacion por hash.
No ejecutar pip ni descargas al iniciar Saylo.

Resolver si se distribuye la rueda actual con avisos NVIDIA/Intel aprobados o
se construye una variante CPU trazable del mismo motor; no se retiro ninguna
DLL ni se recompilo CTranslate2 en esta tarea. Verificar VC runtime, ABI cp312,
DLL search paths, rutas con espacios/caracteres Unicode y antivirus/firma.
Validar en Windows limpio sin herramientas de desarrollo, sin privilegios de
administracion durante el uso y sin red; esa prueba esta pendiente.
[Python embebido](https://docs.python.org/3.12/using/windows.html#the-embeddable-package),
[requisitos CTranslate2](https://opennmt.net/CTranslate2/installation.html).

## 11. Reproduccion, archivos y recomendaciones

Desde la raiz, con los recursos ya instalados:

```powershell
$python = (Resolve-Path ./artifacts/argos/runtime/Scripts/python.exe).Path
dotnet restore
dotnet build Saylo.slnx
./tools/Test-TranslationValidation.ps1 -PythonExecutable $python `
  -ModelDirectory ./artifacts/argos/models `
  -ReportDirectory ./artifacts/translation-validation/replay

& $python -B -m unittest discover `
  -s ./tests/Saylo.Modules.Translation.Tests/Python -p 'test_*.py' -v
& $python -m pip check
& $python -I -B ./tools/Collect-TranslationLicenseEvidence.py `
  --models ./artifacts/argos/models --archives ./artifacts/argos/downloads `
  --output ./artifacts/translation-validation/license-replay.json
git diff --check
```

La recopilacion de evidencia no accede a red salvo si se agrega expresamente
`--online`. La auditoria de esta tarea si uso ese flag para consultar fuentes
primarias. Nunca usarlo en la prueba desconectada.
Para repetir las 136 pruebas completas, configurar ademas las variables Whisper
de [la guia existente](whisper-speech-recognition.md) y las rutas Argos, luego
ejecutar `dotnet test Saylo.slnx`. Sin esas variables las integraciones se omiten;
una omision no cuenta como pase.

Archivos nuevos:

- Este informe.
- `docs/validation/translation-quality-cases.json`: corpus y criterios.
- `docs/validation/translation-quality-review.json`: revision provisional.
- `docs/validation/translation-license-evidence.json`: inventario observado.
- `docs/validation/translation-license-audit.json`: obligaciones/pendientes.
- `docs/validation/results/2026-10-28/`: entorno, resultados reales, rendimiento,
  cinco informes de estabilidad y resumen de pruebas.
- `tests/Saylo.Modules.Translation.Tests/TranslationValidationTests.cs`.
- `tools/Test-TranslationValidation.ps1`.
- `tools/Collect-TranslationLicenseEvidence.py`.

Archivos existentes modificados:

- `tests/Saylo.Modules.Translation.Tests/ArgosIntegrationTests.cs`: corregir
  escritura de diagnostico desde callbacks; aserciones conservadas.
- `docs/local-text-translation.md`: enlace al informe ampliado.

**Siguiente tarea recomendada:** fidelidad empresarial del adaptador actual.
Proteger y validar literales (correo, pedido, seguimiento, cantidades/monedas)
con pruebas reales, definir tratamiento formal y glosario, y estudiar segmentacion
local para evitar degradacion de pasajes largos. No usar sustituciones ciegas ni
ocultar errores con las referencias. Cada cambio debe repetir este corpus y
agregar regresiones de los casos criticos/largos.

En paralelo de calendario, el responsable debe cerrar derechos de paquetes/
recursos y avisos nativos, ejecutar el replay con red desconectada y obtener
revision humana bilingue. Despues, preparar el runtime privado y piloto Windows
limpio. No hay evidencia que obligue a reemplazar todo el motor; si el adaptador
no logra superar los casos de fidelidad, comparar modelos con licencias
verificables usando el mismo conjunto antes de decidir.
