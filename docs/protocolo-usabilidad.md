# Protocolo de evaluación de usabilidad con empleados

Estado: **preparado, no ejecutado con participantes humanos**. La inspección técnica
del desarrollo se registra por separado en el
[informe de pruebas](pruebas-escritorio-usabilidad.md). No hay resultados SUS,
tiempos de usuarios, muestras ni tasas de éxito de empleados obtenidos todavía.

## Objetivo y preparación

Comprobar el flujo corto durante una atención y la comprensibilidad de controles,
foco, estados y errores (RNF-009 y RNF-010 del SRS v0.6). RF-026 se refiere
específicamente al autoplay en ambos modos de entrada; no es el identificador
general de usabilidad. RNF-026 trata la calidad de la guía de pronunciación.

El supervisor y TI preparan un puesto identificado por versión, SO, pantalla y
dispositivos. El recorrido de voz se habilita solo después de conectar y verificar
captura, reconocedor, pronunciación y síntesis reales. Si falta una capacidad, se
marca **no ejecutada**, no aprobada. La demostración sirve para el ensayo del texto
y del manual; no valida traducción libre, voz ni trabajo offline con modelos.

Para un piloto exploratorio se propone invitar a cinco empleados con distinta
experiencia en herramientas de escritorio, incluyendo usuarios de teclado y ayudas
técnicas cuando formen parte del equipo. Es una propuesta de organización, no una
muestra ya reunida ni una justificación estadística. Acordar con los participantes
la observación y el registro antes de comenzar. Usar únicamente datos ficticios.

Roles: facilitador que lee las tareas sin enseñar la solución; observador que anota
acciones, ayudas y errores; TI disponible para incidencias. El empleado dispone del
[manual](manual-empleados.md) y la [lista operativa](verificacion-operativa.md).
No cambiar permisos o configuración del puesto durante una tarea sin intervención de TI.

## Tareas

| ID | Consigna para el empleado | Qué debe observar el facilitador |
| --- | --- | --- |
| U-01 | Preparar una respuesta en inglés a partir de una frase española ficticia. | Distingue origen/destino, encuentra el editor y solicita traducir. |
| U-02 | Leer la traducción y su pronunciación; copiar únicamente la traducción en un destino de prueba. | Encuentra el resultado, desplaza el chat si hace falta y comprueba el aviso de copia. |
| U-03 | Cambiar el sentido de la atención para trabajar desde inglés hacia español. | Encuentra el intercambio, detecta que el borrador se conserva y revisa su idioma. |
| U-04 | Grabar una frase, corregir una palabra y traducirla. | Inicia voluntariamente, detiene, revisa, añade al borrador y confirma; no se traduce antes de revisar. Requiere integración de voz. |
| U-05 | Escuchar la respuesta sin pulsar Repetir, primero desde texto y después desde grabación. | Autoplay activado inicialmente, una escucha por síntesis y texto accesible durante la espera. Requiere TTS y salida reales. |
| U-06 | Detener, repetir y desactivar la voz automática para la siguiente respuesta. | Diferencia Detener de desactivar; encuentra Repetir; entiende que el audio es local y no entra a la llamada. |
| U-07 | Abrir Limpiar chat, conservarlo y después terminar la atención sin dejar borrador. | Entiende la confirmación y que borrar chat y borrar borrador son acciones distintas. Usa solo la sesión de prueba. |
| U-08 | Recuperarse de un aviso preparado de micrófono o voz y continuar por texto. | Comprende qué falló, encuentra la recuperación y mantiene disponible el texto. TI prepara el fallo de forma controlada. |
| U-09 | Repetir el recorrido escrito usando solo teclado, incluida una ventana reducida. | Tab/Mayús+Tab, foco perceptible, selectores, Ctrl+Mayús+S, editor, Ctrl+Enter y acceso al resultado. |

En la demostración, U-01 a U-03 usan Buenos días. / Good morning. y otras frases
precargadas mediante Usar ejemplo. La generación de voz y la calidad empresarial
de las traducciones no se puntúan en ese modo. En un puesto integrado se añaden
frases empresariales nuevas aprobadas por el supervisor y un revisor bilingüe.

## Cómo registrar una tarea

Marcar inicio cuando el empleado oye la consigna y final cuando consigue el
resultado o decide abandonar. Registrar duración real en segundos y separar el
tiempo de espera del motor cuando sea posible. No sustituirlo por la duración de
una prueba automatizada ni por un cálculo del facilitador.

- Resultado: completada sin ayuda / completada con ayuda / no completada /
  no ejecutada por capacidad pendiente.
- Número y tipo de ayudas; errores de idioma, clics equivocados, pérdida de foco,
  resultado difícil de encontrar o dudas sobre privacidad y llamada.
- Claridad declarada por el participante: escala 1 (muy confuso) a 5 (muy claro).
- Comentario literal voluntario y propuesta de mejora; registrar sin datos de clientes.

Ficha a copiar para cada tarea y participante:

```text
Sesión / participante anónimo / fecha:
Versión de SAYLO / SO / pantalla / escala / entrada y salida de audio:
Modo y motores habilitados:
Tarea U-__:
Inicio / fin / duración medida:
Tiempo de espera del motor, si pudo distinguirse:
Resultado:
Ayudas y errores:
Claridad (1–5), declarada por el participante:
Comentario y observaciones:
Incidencia / prioridad / responsable / acción / fecha de nueva prueba:
```

## Decisión del piloto

Antes de la sesión, el responsable acuerda criterios de aceptación. Como punto
de partida no normativo se propone completar al menos el 80 % de las tareas
ejecutables sin ayuda, claridad mediana de al menos 4/5 y ningún incidente crítico
de pérdida de contenido, captura no solicitada o confusión de destino al copiar.
Estos umbrales son una propuesta de piloto, no requisitos textuales del SRS.
Las tareas no ejecutadas se informan aparte y no se incluyen como éxitos.

Una deficiencia crítica impide aprobar el recorrido afectado. Las incidencias de
lectura, foco y navegación se priorizan y se repiten con empleados después del
ajuste. La aprobación debe indicar explícitamente qué recorridos, sistemas y
puestos se evaluaron; no se extrapola a macOS, Linux, lectores de pantalla u otros
modelos sin su propia evidencia. La variante y calidad fonética requieren revisión
humana bilingüe; los dobles de pruebas no la acreditan.
